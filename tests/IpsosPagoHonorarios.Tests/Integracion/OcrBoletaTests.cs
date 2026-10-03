using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using static IpsosPagoHonorarios.Tests.Entorno;

namespace IpsosPagoHonorarios.Tests.Integracion;

/// <summary>Boletas cuyo PDF no trae texto (compartidas desde la app del SII): se leen por OCR.</summary>
public class OcrBoletaTests(ITestOutputHelper salida)
{
    private static byte[] BoletaApp => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Recursos", "boleta_app_sii_ficticia.pdf"));

    private sealed class OcrFijo(List<string>? lineas) : ILectorOcr
    {
        public int Llamadas { get; private set; }
        public Task<ResultadoOcr> ReconocerAsync(byte[] pdf, CancellationToken ct = default)
        {
            Llamadas++;
            return Task.FromResult(new ResultadoOcr(lineas, lineas is null ? null : "Fijo", []));
        }
    }

    private static async Task<(Entorno E, Planilla P, Prestador Pr)> PlanillaAsync()
    {
        var e = new Entorno();
        var par = e.Db.Parametros.First();
        par.RutEmpresa = "76007075-0";   // receptor de la boleta ficticia
        var pr = e.Prestador("Persona Ficticia de Prueba", "12.345.678-5", "Cuenta Corriente", "00012345678", "BANCO DE CHILE");
        var p = await e.PlanillaAsync("DATA PROCESSING", new Fila(pr, "260045100301", DPG, 471976, 1));
        return (e, p, pr);
    }

    private static BoletaService Boletas(Entorno e, ILectorOcr ocr) => new(e.Db, e.Ciclos, e.Parametros, e.Archivos, e.Auditor, e.Correos, e.Usuario, ocr);

    [Fact]
    public async Task PdfSinTexto_SeLeePorOcr_ConConfianzaMaximaMedia()
    {
        Assert.False(LectorPdf.TieneTexto(LectorPdf.ExtraerLineas(BoletaApp)));
        var (e, p, pr) = await PlanillaAsync();
        using var _ = e;
        var ocr = new OcrFijo(["BOLETA DE HONORARIOS ELECTRÓNICA N*117", "Fecha: 01 de octubre de 2026", "PERSONA FICTICIA", "DE PRUEBA",
            "Rut: 12.345.678-5", "Señor(es): IPSOS OBSERVER (CHILE)S.A.", "Rut: 76.007.075-0", "Total Honorario $ 471.976",
            "15,25% Impto. retenido 71.976", "Total 400.000"]);
        var r = await Boletas(e, ocr).SubirAsync(p.Id, pr.Id, BoletaApp, "boleta.pdf", BoletaCanal.Portal);
        Assert.Equal(1, ocr.Llamadas);
        Assert.True(r.Conciliacion.Cuadra, string.Join(" ", r.Conciliacion.Problemas));
        Assert.Equal(("117", 471976m), (r.Boleta.NumeroBoleta, r.Boleta.MontoBruto));
        Assert.Equal(Confianza.Media, r.Boleta.Confianza);   // leída por OCR: Operaciones o Finanzas confirman
        Assert.StartsWith("[OCR]", r.Boleta.TextoExtraido);

        // Un PDF con texto no pasa por la OCR.
        var (e2, p2, pr2) = await PlanillaAsync();
        using var __ = e2;
        var ocr2 = new OcrFijo(null);
        await Boletas(e2, ocr2).SubirAsync(p2.Id, pr2.Id, Pdf(pr2, "92", 471976m, new DateOnly(2026, 10, 1), "76007075-0"), "b.pdf", BoletaCanal.Portal);
        Assert.Equal(0, ocr2.Llamadas);
    }

    [Fact]
    public async Task SinOcrDisponible_LaBoletaQuedaParaCorreccionManual()
    {
        var (e, p, pr) = await PlanillaAsync();
        using var _ = e;
        var r = await Boletas(e, new OcrFijo(null)).SubirAsync(p.Id, pr.Id, BoletaApp, "boleta.pdf", BoletaCanal.Portal);
        Assert.False(r.Conciliacion.Cuadra);
        Assert.Equal(Confianza.Baja, r.Boleta.Confianza);
    }

    [Fact]
    public async Task Ocr_LeeLaBoletaDeLaAppDelSii()
    {
        var ocr = new LectorOcr(Options.Create(new OpcionesOcr()), new HostingEnvironment { ContentRootPath = AppContext.BaseDirectory },
            NullLogger<LectorOcr>.Instance);
        var r = await ocr.ReconocerAsync(BoletaApp);
        foreach (var d in r.Detalle) salida.WriteLine(d);
        var lineas = r.Lineas;
        if (lineas is null && !OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("CI") is null)
        {
            salida.WriteLine("Tesseract no está instalado en este equipo: se omite la OCR real (en CI y Windows sí se ejecuta).");
            return;
        }
        Assert.NotNull(lineas);
        Assert.NotNull(r.Motor);
        var l = LectorBoletaTexto.Leer(lineas);
        Assert.Equal("117", l.Datos.Numero);
        Assert.Equal("12345678-5", l.Datos.RutEmisor);
        Assert.Equal("76007075-0", l.Datos.RutReceptor);
        Assert.Equal(new DateOnly(2026, 10, 1), l.Datos.FechaEmision);
        Assert.Equal((471976m, 71976m, 400000m), (l.Datos.Bruto, l.Datos.Retencion, l.Datos.Liquido));
    }
}
