using IpsosPagoHonorarios.Core;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace IpsosPagoHonorarios.Web.Services;

/// <summary>
/// Genera PDFs con el formato de la boleta de honorarios electrónica del SII. Solo para datos de demo y pruebas
/// (datos ficticios). TODO(diseño): afinar la lectura con PDFs reales anonimizados.
/// </summary>
public static class BoletaPdf
{
    public static byte[] Generar(string nombreEmisor, string rutEmisor, string numero, DateOnly fecha, string rutReceptor, string receptor,
        string glosa, decimal bruto, decimal tasa)
    {
        var ret = Montos.Retencion(bruto, tasa);
        string M(decimal v) => v.ToString("#,0", Formato.Cl);
        var meses = new[] { "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre" };
        var lineas = new[]
        {
            nombreEmisor,
            $"RUT: {RutHelper.Formatear(rutEmisor)}",
            "Giro: Servicios profesionales",
            "",
            "BOLETA DE HONORARIOS ELECTRONICA",
            $"N {numero}",
            $"Fecha: {fecha.Day} de {meses[fecha.Month - 1]} de {fecha.Year}",
            "",
            $"Senor(es): {receptor}",
            $"Rut: {RutHelper.Formatear(rutReceptor)}",
            "",
            "Por atencion profesional:",
            glosa,
            $"Total Honorarios $: {M(bruto)}",
            $"{(tasa * 100).ToString("0.##", Formato.Cl)} % Impto. Retenido: {M(ret)}",
            $"Total: {M(bruto - ret)}",
            "",
            "Documento de demostracion - datos ficticios"
        };
        var builder = new PdfDocumentBuilder();
        var pagina = builder.AddPage(PageSize.A4);
        var fuente = builder.AddStandard14Font(Standard14Font.Helvetica);
        var y = 780.0;
        foreach (var l in lineas)
        {
            if (l.Length > 0) pagina.AddText(SinTildes(l), 11, new PdfPoint(60, y), fuente);
            y -= 20;
        }
        return builder.Build();
    }

    /// <summary>Las fuentes estándar del PDF se usan solo con ASCII.</summary>
    private static string SinTildes(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark && c < 128).ToArray());
    }

    /// <summary>Comprobante de transferencia ficticio (demo).</summary>
    public static byte[] Comprobante(string operacion, string rut, decimal monto, DateOnly fecha)
    {
        var builder = new PdfDocumentBuilder();
        var pagina = builder.AddPage(PageSize.A4);
        var fuente = builder.AddStandard14Font(Standard14Font.Helvetica);
        pagina.AddText($"Comprobante de transferencia {operacion}", 12, new PdfPoint(60, 780), fuente);
        pagina.AddText($"Destinatario {rut} - Monto {monto.ToString("#,0", Formato.Cl)} - Fecha {Formato.Fecha(fecha)}", 11, new PdfPoint(60, 755), fuente);
        pagina.AddText("Documento de demostracion - datos ficticios", 9, new PdfPoint(60, 730), fuente);
        return builder.Build();
    }
}
