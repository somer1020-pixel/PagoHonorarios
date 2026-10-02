using System.Text.RegularExpressions;

namespace IpsosPagoHonorarios.Core;

/// <summary>R-01: RUT válido por módulo 11 (DV 0-9 o K), con o sin puntos y guion.</summary>
public static partial class RutHelper
{
    [GeneratedRegex(@"^(\d{1,8})-?([\dK])$")]
    private static partial Regex Patron();

    public static string CalcularDv(int cuerpo)
    {
        int suma = 0, mult = 2;
        for (var n = cuerpo; n > 0; n /= 10)
        {
            suma += n % 10 * mult;
            mult = mult == 7 ? 2 : mult + 1;
        }
        var r = 11 - suma % 11;
        return r switch { 11 => "0", 10 => "K", _ => r.ToString() };
    }

    public static bool TryParse(string? texto, out int cuerpo, out string dv)
    {
        cuerpo = 0;
        dv = "";
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var limpio = texto.Replace(".", "").Replace(" ", "").Trim().ToUpperInvariant();
        var m = Patron().Match(limpio);
        if (!m.Success) return false;
        cuerpo = int.Parse(m.Groups[1].Value);
        dv = m.Groups[2].Value;
        return cuerpo > 0 && CalcularDv(cuerpo) == dv;
    }

    public static bool EsValido(string? texto) => TryParse(texto, out _, out _);

    /// <summary>Formato de planilla y de almacenamiento: 12345678-5.</summary>
    public static string Normalizar(string texto) =>
        TryParse(texto, out var c, out var d) ? $"{c}-{d}" : throw new FormatException($"RUT inválido: {texto}");

    /// <summary>Formato de UI: 12.345.678-5.</summary>
    public static string Formatear(int cuerpo, string dv) => $"{cuerpo.ToString("#,0", Formato.Cl)}-{dv}";

    public static string Formatear(string texto) =>
        TryParse(texto, out var c, out var d) ? Formatear(c, d) : texto;

    /// <summary>Motivo del error, para mensajes de validación.</summary>
    public static string? Error(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "RUT vacío";
        var limpio = texto.Replace(".", "").Replace(" ", "").Trim().ToUpperInvariant();
        var m = Patron().Match(limpio);
        if (!m.Success) return "Formato inválido";
        return CalcularDv(int.Parse(m.Groups[1].Value)) == m.Groups[2].Value ? null : "Dígito verificador incorrecto";
    }
}
