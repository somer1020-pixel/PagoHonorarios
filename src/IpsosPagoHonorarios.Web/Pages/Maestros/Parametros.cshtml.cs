using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Pages.Maestros;

/// <summary>Parámetros del ciclo de pago y tasa de retención por año: solo el Administrador.</summary>
[Authorize(Roles = Roles.Admin)]
public class ParametrosModel(AppDbContext db, Parametros parametros, Auditor auditor, ILectorOcr ocr) : PaginaBase
{
    public Parametro P { get; set; } = new();
    public List<TasaRetencion> Tasas { get; set; } = [];
    public List<Ciclo> Abiertos { get; set; } = [];
    public ResultadoOcr? PruebaOcr { get; set; }
    public LecturaBoleta? PruebaLectura { get; set; }
    public string? PruebaArchivo { get; set; }

    public async Task OnGetAsync()
    {
        P = await parametros.ObtenerAsync();
        Tasas = await db.TasasRetencion.OrderByDescending(t => t.Anio).ToListAsync();
        Abiertos = await db.Ciclos.Where(c => c.Estado == CicloEstado.Abierto).OrderBy(c => c.Periodo).ToListAsync();
    }

    public Task<IActionResult> OnPostGuardarAsync(int diaDescargaDesde, int diaDescargaHasta, int diaLimiteBoleta, int diaPago,
        int plazoCorreccionMinutos, string? rutEmpresa, string? razonSocialEmpresa, bool aplicarPagoAbiertos) =>
        AccionAsync(async () =>
        {
            if (diaDescargaDesde is < 1 or > 31 || diaDescargaHasta is < 1 or > 31 || diaDescargaDesde > diaDescargaHasta)
                throw new ReglaException("La ventana de descarga debe ir de un día a otro entre 1 y 31 (desde ≤ hasta).");
            if (diaLimiteBoleta is < 1 or > 28) throw new ReglaException("El día límite de la boleta debe estar entre 1 y 28.");
            if (diaPago is < 1 or > 28) throw new ReglaException("El día de pago debe estar entre 1 y 28.");
            if (plazoCorreccionMinutos is < 15 or > 2880) throw new ReglaException("El plazo de corrección debe estar entre 15 minutos y 48 horas (2.880 minutos).");
            if (!RutHelper.TryParse(rutEmpresa, out var cuerpo, out var dv)) throw new ReglaException($"RUT de la empresa inválido: {RutHelper.Error(rutEmpresa)}.");
            if (string.IsNullOrWhiteSpace(razonSocialEmpresa)) throw new ReglaException("La razón social es obligatoria.");

            var p = await db.Parametros.OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (p is null) db.Parametros.Add(p = new Parametro());
            var cambios = new List<string>();
            void Cambiar<T>(string nombre, T antes, T despues, Action<T> asignar)
            {
                if (EqualityComparer<T>.Default.Equals(antes, despues)) return;
                cambios.Add($"{nombre}: {antes} → {despues}");
                asignar(despues);
            }
            Cambiar("Descarga desde", p.DiaDescargaDesde, diaDescargaDesde, v => p.DiaDescargaDesde = v);
            Cambiar("Descarga hasta", p.DiaDescargaHasta, diaDescargaHasta, v => p.DiaDescargaHasta = v);
            Cambiar("Día límite boleta", p.DiaLimiteBoleta, diaLimiteBoleta, v => p.DiaLimiteBoleta = v);
            Cambiar("Día de pago", p.DiaPago, diaPago, v => p.DiaPago = v);
            Cambiar("Plazo de corrección (min)", p.PlazoCorreccionMinutos, plazoCorreccionMinutos, v => p.PlazoCorreccionMinutos = v);
            Cambiar("RUT empresa", p.RutEmpresa, $"{cuerpo}-{dv}", v => p.RutEmpresa = v);
            Cambiar("Razón social", p.RazonSocialEmpresa, razonSocialEmpresa.Trim(), v => p.RazonSocialEmpresa = v);
            if (aplicarPagoAbiertos)
                foreach (var c in await db.Ciclos.Where(c => c.Estado == CicloEstado.Abierto).ToListAsync())
                {
                    var fecha = c.Periodo.AddMonths(1).AddDays(diaPago - 1);
                    if (c.FechaPagoProgramada == fecha) continue;
                    cambios.Add($"Pago programado {c.Codigo}: {Formato.Fecha(c.FechaPagoProgramada)} → {Formato.Fecha(fecha)}");
                    c.FechaPagoProgramada = fecha;
                }
            if (cambios.Count == 0) throw new ReglaException("No hay cambios que guardar.");
            auditor.Registrar(nameof(Parametro), p.Id, "Editar parámetros", string.Join(" · ", cambios));
            await db.SaveChangesAsync();
            MensajeOk = "Parámetros guardados: " + string.Join(" · ", cambios) + ".";
        }, "Parámetros guardados.", null, [Roles.Admin]);

    /// <summary>Diagnóstico: lee por OCR el PDF indicado (o una boleta ficticia de la app del SII) y muestra motor, detalle y datos.</summary>
    public async Task<IActionResult> OnPostProbarOcrAsync(IFormFile? pdf)
    {
        await OnGetAsync();
        byte[] bytes;
        if (pdf is { Length: > 0 })
        {
            bytes = await LeerAsync(pdf);
            PruebaArchivo = pdf.FileName;
            if (!LectorPdf.EsPdf(bytes)) { MensajeError = "El archivo no es un PDF."; return Page(); }
        }
        else
        {
            using var s = typeof(ParametrosModel).Assembly.GetManifestResourceStream("BoletaPruebaOcr.pdf")!;
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            bytes = ms.ToArray();
            PruebaArchivo = "Boleta de prueba ficticia (formato app del SII, sin texto)";
        }
        PruebaOcr = await ocr.ReconocerAsync(bytes, HttpContext.RequestAborted);
        if (PruebaOcr.Lineas is { } l) PruebaLectura = LectorBoletaTexto.Leer(l);
        return Page();
    }

    public Task<IActionResult> OnPostTasaAsync(int anio, string? tasa) =>
        AccionAsync(async () =>
        {
            if (anio is < 2000 or > 2100) throw new ReglaException("Indica un año válido.");
            var pct = ExcelPlanilla.ParseNumero(tasa);
            if (pct is not (> 0 and < 100)) throw new ReglaException("La tasa debe ser un porcentaje mayor que 0 y menor que 100 (ej.: 15,25).");
            var valor = Math.Round(pct.Value / 100m, 4);
            var t = await db.TasasRetencion.FirstOrDefaultAsync(x => x.Anio == anio);
            var antes = t is null ? "—" : Formato.Porcentaje(t.Tasa);
            if (t is null) db.TasasRetencion.Add(t = new TasaRetencion { Anio = anio });
            t.Tasa = valor;
            auditor.Registrar(nameof(TasaRetencion), anio, "Tasa de retención", $"{antes} → {Formato.Porcentaje(valor)}");
            await db.SaveChangesAsync();
            MensajeOk = $"Tasa de retención {anio}: {Formato.Porcentaje(valor)}.";
        }, "Tasa guardada.", null, [Roles.Admin]);
}
