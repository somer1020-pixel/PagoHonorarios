using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IpsosPagoHonorarios.Core;

/// <summary>Expresiones regulares configurables para leer la boleta del SII (texto ya normalizado).</summary>
public sealed class OpcionesLectura
{
    public string Numero { get; set; } = @"BOLETA DE HONORARIOS ELECTRONICA.*?N\s*[°ºO]?\.?\s*:?\s*(\d+)";
    public string Rut { get; set; } = @"(\d{1,2}\.?\d{3}\.?\d{3}\s*-\s*[\dK])";
    public string FechaLarga { get; set; } = @"(\d{1,2})\s+DE\s+([A-Z]+)\s+DE\s+(\d{4})";
    public string FechaCorta { get; set; } = @"(\d{2})/(\d{2})/(\d{4})";
    public string Bruto { get; set; } = @"TOTAL\s+HONORARIOS";
    public string Retencion { get; set; } = @"IMPTO\.?\s+RETENIDO|RETENCION";
    public string Liquido { get; set; } = @"^\s*TOTAL\b(?!\s+HONORARIOS)";
}

public sealed record LecturaBoleta(DatosBoleta Datos, Confianza Confianza, string TextoNormalizado);

/// <summary>Lectura de la boleta a partir del texto extraído del PDF (líneas por posición vertical).</summary>
public static class LectorBoletaTexto
{
    private static readonly string[] Meses =
        ["ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"];

    /// <summary>Mayúsculas y sin tildes.</summary>
    public static string Normalizar(string texto)
    {
        var formD = texto.ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static LecturaBoleta Leer(IEnumerable<string> lineas, OpcionesLectura? op = null)
    {
        op ??= new OpcionesLectura();
        var norm = lineas.Select(Normalizar).ToList();
        var texto = string.Join("\n", norm);

        string? numero = null;
        var mNum = Regex.Match(texto, op.Numero, RegexOptions.Singleline);
        if (mNum.Success) numero = mNum.Groups[1].Value;

        string? emisor = null, receptor = null, nombre = null;
        foreach (Match m in Regex.Matches(texto, op.Rut))
        {
            var rut = Regex.Replace(m.Groups[1].Value, @"\s", "");
            if (!RutHelper.TryParse(rut, out _, out _)) continue;
            var n = RutHelper.Normalizar(rut);
            if (emisor is null) emisor = n;
            else if (n != emisor) { receptor = n; break; }
        }
        if (emisor is not null)
        {
            // El nombre del emisor suele estar en la línea anterior a su RUT.
            var idx = norm.FindIndex(l => Regex.Matches(l, op.Rut).Any(m => RutHelper.TryParse(Regex.Replace(m.Value, @"\s", ""), out var c, out _) && $"{c}" == emisor.Split('-')[0]));
            if (idx > 0 && !norm[idx - 1].Any(char.IsDigit)) nombre = norm[idx - 1].Trim();
        }

        DateOnly? fecha = null;
        var mFl = Regex.Match(texto, op.FechaLarga);
        if (mFl.Success)
        {
            var mes = Array.IndexOf(Meses, mFl.Groups[2].Value) + 1;
            if (mes > 0 && TryFecha(int.Parse(mFl.Groups[3].Value), mes, int.Parse(mFl.Groups[1].Value), out var f)) fecha = f;
        }
        if (fecha is null)
        {
            var mFc = Regex.Match(texto, op.FechaCorta);
            if (mFc.Success && TryFecha(int.Parse(mFc.Groups[3].Value), int.Parse(mFc.Groups[2].Value), int.Parse(mFc.Groups[1].Value), out var f)) fecha = f;
        }

        var bruto = MontoEnLinea(norm, op.Bruto);
        var retencion = MontoEnLinea(norm, op.Retencion);
        var liquido = MontoEnLinea(norm, op.Liquido);

        var datos = new DatosBoleta
        {
            Numero = numero, RutEmisor = emisor, RutReceptor = receptor, NombreEmisor = nombre, FechaEmision = fecha,
            Bruto = bruto, Retencion = retencion, Liquido = liquido,
            // TODO(diseño): fuente del dato "anulada" (R-06). Por ahora se detecta la palabra en el documento.
            Anulada = Regex.IsMatch(texto, @"\bANULADA\b")
        };
        return new(datos, CalcularConfianza(datos), texto);
    }

    /// <summary>Alta: todos los campos y bruto − retención = líquido. Media: falta un campo que no es monto. Baja: otro caso.</summary>
    public static Confianza CalcularConfianza(DatosBoleta d)
    {
        var montosOk = d.Bruto is not null && d.Retencion is not null && d.Liquido is not null &&
                       d.Bruto - d.Retencion == d.Liquido;
        var faltantes = new object?[] { d.Numero, d.RutEmisor, d.RutReceptor, d.FechaEmision }.Count(x => x is null);
        if (!montosOk) return Confianza.Baja;
        return faltantes switch { 0 => Confianza.Alta, 1 => Confianza.Media, _ => Confianza.Baja };
    }

    private static bool TryFecha(int y, int m, int d, out DateOnly f)
    {
        f = default;
        if (m is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(y, m)) return false;
        f = new DateOnly(y, m, d);
        return true;
    }

    private static decimal? MontoEnLinea(List<string> lineas, string patron)
    {
        foreach (var l in lineas)
        {
            if (!Regex.IsMatch(l, patron)) continue;
            var sinPct = Regex.Replace(l, @"\d+(?:[.,]\d+)?\s*%", " ");
            var resto = Regex.Replace(sinPct, patron, " ");
            var montos = Regex.Matches(resto, @"\$?\s*(\d{1,3}(?:\.\d{3})+|\d+)(?!\s*%)");
            if (montos.Count == 0) continue;
            return decimal.Parse(montos[^1].Groups[1].Value.Replace(".", ""), CultureInfo.InvariantCulture);
        }
        return null;
    }
}
