using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Services;

namespace IpsosPagoHonorarios.Tests.Core;

/// <summary>R-02 (producción), R-06 (conciliación) y lectura de la boleta (§9).</summary>
public class ProduccionYBoletaTests
{
    private static readonly string[] Glosas = ["Honorarios entrevistadores", "Honorarios supervisión", "Honorario codificación externa"];

    private static FilaProduccion F(int fila, string rut = "15234871-1", string job = "260041200105", string glosa = "Honorarios entrevistadores", decimal? vu = 6500, decimal? q = 42) =>
        new() { Fila = fila, Rut = rut, Job = job, Glosa = glosa, ValorUnitario = vu, Cantidad = q, Nombre = "X" };

    [Fact]
    public void R02_ErroresPorFila_ConNumeroYMotivo()
    {
        var r = ProduccionReglas.Validar([F(9), F(14, rut: "15234871-9"), F(22, job: "26004120010"), F(31, q: 0), F(40, glosa: "Glosa inexistente"), F(41, vu: -1)],
            Glosas, _ => true, _ => true);
        Assert.False(r.Valido);
        Assert.Contains(r.Errores, e => e.Fila == 14 && e.Campo == "Rut" && e.Motivo.Contains("dígito verificador"));
        Assert.Contains(r.Errores, e => e.Fila == 22 && e.Motivo.Contains("11 dígitos"));
        Assert.Contains(r.Errores, e => e.Fila == 31 && e.Campo == "Cantidad");
        Assert.Contains(r.Errores, e => e.Fila == 40 && e.Campo == "Nombre Glosa");
        Assert.Contains(r.Errores, e => e.Fila == 41 && e.Campo == "Valor unitario bruto");
        Assert.DoesNotContain(r.Errores, e => e.Fila == 9);
    }

    [Fact]
    public void R02_RutYJobNuevosSeAvisan()
    {
        var r = ProduccionReglas.Validar([F(9, rut: "19.876.543-0", job: "260044200110")], Glosas, _ => false, _ => false);
        Assert.True(r.Valido);
        Assert.Contains(r.Avisos, a => a.Contains("19.876.543-0") && a.Contains("se creará el prestador"));
        Assert.Contains(r.Avisos, a => a.Contains("260044200110") && a.Contains("se creará"));
    }

    [Fact]
    public void TotalManualEnH_SeRespeta_YSeInforma()
    {
        var manual = F(16, vu: 250, q: 45) with { TotalArchivo = 13127 };
        Assert.Equal(13127m, ProduccionReglas.TotalDe(manual));                       // se usa H
        Assert.Equal(24961m, ProduccionReglas.TotalDe(F(17, vu: 250, q: 99.845m)));   // sin H: ROUND(F×G)
        var a = Assert.Single(ProduccionReglas.AvisosTotales([manual, F(17, vu: 250, q: 99.845m) with { TotalArchivo = 24961 }]));
        Assert.Contains("Fila 16", a);
        Assert.Contains("diferencia $1.877", a);
        Assert.Contains("Se usa el valor de H", a);
    }

    [Fact]
    public void R02_VentanaDeDescarga()
    {
        Assert.Null(ProduccionReglas.AvisoVentana(new DateOnly(2026, 10, 28)));
        Assert.Null(ProduccionReglas.AvisoVentana(new DateOnly(2026, 10, 30)));
        Assert.NotNull(ProduccionReglas.AvisoVentana(new DateOnly(2026, 10, 27)));
        Assert.NotNull(ProduccionReglas.AvisoVentana(new DateOnly(2026, 10, 31)));
    }

    private static DatosBoleta Boleta(decimal bruto = 345000, string emisor = "15234871-1", string receptor = "77777777-7", DateOnly? fecha = null, string numero = "1245") => new()
    {
        Numero = numero, RutEmisor = emisor, RutReceptor = receptor, FechaEmision = fecha ?? new DateOnly(2026, 10, 30),
        Bruto = bruto, Retencion = Montos.Retencion(bruto, 0.1525m), Liquido = Montos.Liquido(bruto, 0.1525m)
    };

    private static readonly DateOnly Oct = new(2026, 10, 1);

    [Fact]
    public void R06_ConciliacionConVariasFilas_345000()
    {
        var r = Conciliacion.Conciliar(Boleta(), 15234871, "77.777.777-7", Oct, [273000m, 72000m], (_, _) => false);
        Assert.True(r.Cuadra, string.Join(" ", r.Problemas));
    }

    [Fact]
    public void R06_FechaLimite_Dia10DelMesSiguiente()
    {
        Assert.Equal(new DateOnly(2026, 11, 10), Conciliacion.FechaLimite(Oct));
        Assert.Equal(new DateOnly(2027, 1, 10), Conciliacion.FechaLimite(new DateOnly(2026, 12, 1)));
        Assert.True(Conciliacion.Conciliar(Boleta(fecha: new DateOnly(2026, 11, 10)), 15234871, "77777777-7", Oct, [345000m], (_, _) => false).Cuadra);
        Assert.True(Conciliacion.Conciliar(Boleta(fecha: new DateOnly(2026, 10, 1)), 15234871, "77777777-7", Oct, [345000m], (_, _) => false).Cuadra);
        Assert.False(Conciliacion.Conciliar(Boleta(fecha: new DateOnly(2026, 11, 11)), 15234871, "77777777-7", Oct, [345000m], (_, _) => false).Cuadra);
        Assert.False(Conciliacion.Conciliar(Boleta(fecha: new DateOnly(2026, 9, 30)), 15234871, "77777777-7", Oct, [345000m], (_, _) => false).Cuadra);
    }

    [Fact]
    public void R06_Exigencias()
    {
        var emisor = Conciliacion.Conciliar(Boleta(emisor: "16987452-2"), 15234871, "77777777-7", Oct, [345000m], (_, _) => false);
        Assert.True(emisor.EmisorIncorrecto);
        Assert.Contains(emisor.Problemas, p => p.Contains("emitida por usted"));
        Assert.Contains(Conciliacion.Conciliar(Boleta(receptor: "12345678-5"), 15234871, "77777777-7", Oct, [345000m], (_, _) => false).Problemas, p => p.Contains("receptor"));
        var dif = Conciliacion.Conciliar(Boleta(bruto: 182000), 15234871, "77777777-7", Oct, [180000m], (_, _) => false);
        Assert.Equal(2000m, dif.Diferencia);   // tolerancia $0
        Assert.Contains(Conciliacion.Conciliar(Boleta() with { Anulada = true }, 15234871, "77777777-7", Oct, [345000m], (_, _) => false).Problemas, p => p.Contains("anulada"));
        Assert.Contains(Conciliacion.Conciliar(Boleta(), 15234871, "77777777-7", Oct, [345000m], (_, n) => n == "1245").Problemas, p => p.Contains("ya se usó"));
    }

    [Fact]
    public void Lectura_PdfDelSii_ConfianzaAlta()
    {
        var pdf = BoletaPdf.Generar("Valentina Muñoz Soto", "17345120-2", "92", new DateOnly(2026, 10, 31), "77777777-7", "Empresa", "Honorarios entrevistadores OCT-2026", 180000m, 0.1525m);
        Assert.True(LectorPdf.EsPdf(pdf));
        var l = LectorBoletaTexto.Leer(LectorPdf.ExtraerLineas(pdf));
        Assert.Equal("92", l.Datos.Numero);
        Assert.Equal("17345120-2", l.Datos.RutEmisor);
        Assert.Equal("77777777-7", l.Datos.RutReceptor);
        Assert.Equal(new DateOnly(2026, 10, 31), l.Datos.FechaEmision);
        Assert.Equal(180000m, l.Datos.Bruto);
        Assert.Equal(27450m, l.Datos.Retencion);
        Assert.Equal(152550m, l.Datos.Liquido);
        Assert.Equal("VALENTINA MUNOZ SOTO", l.Datos.NombreEmisor);
        Assert.Equal(Confianza.Alta, l.Confianza);
    }

    [Fact]
    public void Lectura_FormatoRealDelSii_GuionTipograficoYTituloEnDosLineas()
    {
        // Misma disposición que la boleta electrónica real del SII (datos ficticios): título en dos líneas, "N ° 164",
        // RUT con signo menos U+2212 y receptor en la misma línea que la razón social.
        string[] lineas =
        [
            "BOLETA DE HONORARIOS", "ELECTRONICA", "PERSONA FICTICIA DE PRUEBA", "N ° 164", "RUT: 12.345.678\u22125",
            "GIRO(S): OTRAS ACTIVIDADES DE SERVICIOS PERSONALES N.C.P.,", "Avenida Ficticia 123 , CIUDAD",
            "Fecha: 01 de Octubre de 2026", "Señor(es): IPSOS OBSERVER (CHILE)S.A. Rut: 76.007.075\u2212 0",
            "Domicilio: CALLE FICTICIA 555,", "Por atención profesional:", "DESARROLLO IVR 471.976", "Total Honorarios: $: 471.976",
            "15.25 % Impto. Retenido: 71.976", "Total: 400.000", "Fecha / Hora Emisión: 01/10/2026 12:04", "Res. Ex. N° 83 de 30/08/2004"
        ];
        var l = LectorBoletaTexto.Leer(lineas);
        Assert.Equal("164", l.Datos.Numero);
        Assert.Equal("12345678-5", l.Datos.RutEmisor);
        Assert.Equal("76007075-0", l.Datos.RutReceptor);
        Assert.Equal("PERSONA FICTICIA DE PRUEBA", l.Datos.NombreEmisor);
        Assert.Equal(new DateOnly(2026, 10, 1), l.Datos.FechaEmision);
        Assert.Equal((471976m, 71976m, 400000m), (l.Datos.Bruto, l.Datos.Retencion, l.Datos.Liquido));
        Assert.Equal(Confianza.Alta, l.Confianza);
        Assert.True(Conciliacion.Conciliar(l.Datos, 12345678, new Parametro().RutEmpresa, new DateOnly(2026, 10, 1), [471976m], (_, _) => false).Cuadra);
    }

    [Fact]
    public void Lectura_FechaCorta_Normalizacion_YConfianza()
    {
        var l = LectorBoletaTexto.Leer(["Boleta de Honorarios Electrónica", "N° 512", "RUT 17.654.321-3", "Fecha 31/10/2026", "Total Honorarios $ 260.000",
            "Retención 15,25 %: 39.650", "Total: 220.350"]);
        Assert.Equal("512", l.Datos.Numero);
        Assert.Equal(new DateOnly(2026, 10, 31), l.Datos.FechaEmision);
        Assert.Null(l.Datos.RutReceptor);
        Assert.Equal(39650m, l.Datos.Retencion);   // ignora el porcentaje
        Assert.Equal(Confianza.Media, l.Confianza);  // falta un campo que no es monto
        Assert.Equal(Confianza.Baja, LectorBoletaTexto.CalcularConfianza(l.Datos with { Liquido = 1 }));
        Assert.Equal("ELECTRONICA N°", LectorBoletaTexto.Normalizar("Electrónica n°"));
    }
}
