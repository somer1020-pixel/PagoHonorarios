using System.Globalization;

namespace IpsosPagoHonorarios.Core;

/// <summary>Localización es-CL: montos $1.234.567, fechas dd-mm-aaaa, horas HH:mm, zona America/Santiago.</summary>
public static class Formato
{
    public static readonly CultureInfo Cl = CreateCl();

    private static CultureInfo CreateCl()
    {
        var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        c.NumberFormat.NumberGroupSeparator = ".";
        c.NumberFormat.NumberDecimalSeparator = ",";
        c.NumberFormat.CurrencyGroupSeparator = ".";
        c.NumberFormat.CurrencyDecimalSeparator = ",";
        return c;
    }

    private static readonly Lazy<TimeZoneInfo> Zona = new(() =>
    {
        foreach (var id in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.Utc;
    });

    public static TimeZoneInfo ZonaChile => Zona.Value;

    public static DateTime ALocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ZonaChile);

    public static string Clp(decimal monto)
    {
        var v = Montos.RedondearExcel(monto);
        return (v < 0 ? "-$" : "$") + Math.Abs(v).ToString("#,0", Cl);
    }

    public static string Clp(decimal? monto) => monto is null ? "—" : Clp(monto.Value);

    public static string Cantidad(decimal q) => q.ToString("#,0.####", Cl);

    public static string Fecha(DateOnly d) => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    public static string Fecha(DateOnly? d) => d is null ? "—" : Fecha(d.Value);
    public static string Fecha(DateTime utc) => ALocal(utc).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
    public static string FechaHora(DateTime utc) => ALocal(utc).ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture);
    public static string FechaHora(DateTime? utc) => utc is null ? "—" : FechaHora(utc.Value);
    public static string Hora(DateTime utc) => ALocal(utc).ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string Porcentaje(decimal tasa) => (tasa * 100).ToString("0.##", Cl) + " %";

    private static readonly string[] Meses =
        ["ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"];
    private static readonly string[] MesesCortos =
        ["ENE", "FEB", "MAR", "ABR", "MAY", "JUN", "JUL", "AGO", "SEP", "OCT", "NOV", "DIC"];

    public static string MesLargo(int mes) => Meses[mes - 1];

    /// <summary>OCT-2026.</summary>
    /// <summary>Plazo legible: 60 → "1 hora", 90 → "1 h 30 min", 45 → "45 minutos".</summary>
    public static string Plazo(int minutos) => minutos switch
    {
        < 60 => $"{minutos} minutos",
        _ when minutos % 60 == 0 => minutos == 60 ? "1 hora" : $"{minutos / 60} horas",
        _ => $"{minutos / 60} h {minutos % 60} min"
    };

    public static string CodigoCiclo(DateOnly periodo) => $"{MesesCortos[periodo.Month - 1]}-{periodo.Year}";

    /// <summary>Planilla honorarios OCTUBRE_2026 - FACE TO FACE v2.xlsx</summary>
    public static string NombreArchivoPlanilla(DateOnly periodo, string area, int version) =>
        $"Planilla honorarios {MesLargo(periodo.Month)}_{periodo.Year} - {area} v{version}.xlsx";

    /// <summary>Enmascara una cuenta: ••••3456.</summary>
    public static string Enmascarar(string cuenta) =>
        cuenta.Length <= 4 ? cuenta : "••••" + cuenta[^4..];

    public static string EnmascararCorreo(string? email)
    {
        if (string.IsNullOrEmpty(email)) return "—";
        var at = email.IndexOf('@');
        return at <= 1 ? email : email[0] + "•••••" + email[at..];
    }
}
