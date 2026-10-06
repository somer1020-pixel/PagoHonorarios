namespace IpsosPagoHonorarios.Core;

/// <summary>Teléfonos para WhatsApp: formato internacional sin "+" (E.164), con Chile por defecto.</summary>
public static class Telefono
{
    /// <summary>
    /// "+56 9 1234 5678", "9 1234 5678", "56912345678" → "56912345678". Un número que empieza con "+" de otro país se acepta
    /// tal cual (8 a 15 dígitos). Devuelve null si no es un celular válido (WhatsApp no opera con fijos chilenos).
    /// </summary>
    public static string? NormalizarWhatsApp(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim();
        var digitos = new string(t.Where(char.IsAsciiDigit).ToArray());
        if (digitos.Length == 0) return null;
        if (t.StartsWith('+') && !digitos.StartsWith("56"))
            return digitos.Length is >= 8 and <= 15 ? digitos : null;
        if (digitos.Length == 9 && digitos[0] == '9') return "56" + digitos;
        if (digitos.Length == 11 && digitos.StartsWith("569")) return digitos;
        return null;
    }

    /// <summary>Como se muestra: +56 9 1234 5678.</summary>
    public static string Formatear(string e164) =>
        e164.Length == 11 && e164.StartsWith("569") ? $"+56 9 {e164[3..7]} {e164[7..]}" : "+" + e164;
}
