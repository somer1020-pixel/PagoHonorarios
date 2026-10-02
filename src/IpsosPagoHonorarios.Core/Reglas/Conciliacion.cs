namespace IpsosPagoHonorarios.Core;

/// <summary>Datos leídos (o corregidos) de una boleta de honorarios electrónica.</summary>
public sealed record DatosBoleta
{
    public string? Numero { get; init; }
    public string? RutEmisor { get; init; }
    public string? NombreEmisor { get; init; }
    public string? RutReceptor { get; init; }
    public DateOnly? FechaEmision { get; init; }
    public decimal? Bruto { get; init; }
    public decimal? Retencion { get; init; }
    public decimal? Liquido { get; init; }
    public bool Anulada { get; init; }
}

public sealed record ResultadoConciliacion(List<string> Problemas, bool EmisorIncorrecto, decimal Diferencia)
{
    public bool Cuadra => Problemas.Count == 0;
}

public static class Conciliacion
{
    public const string MensajeEmisor = "La boleta debe estar emitida por usted";

    /// <summary>R-06: fecha de emisión entre el día 1 del período y el día 10 del mes siguiente.</summary>
    public static DateOnly FechaLimite(DateOnly periodo, int diaLimite = 10) =>
        new DateOnly(periodo.Year, periodo.Month, 1).AddMonths(1).AddDays(diaLimite - 1);

    /// <summary>
    /// R-06: una boleta por prestador y planilla por el total de sus filas. Exige RUT emisor = prestador,
    /// RUT receptor = empresa, fecha dentro del rango, bruto = suma de filas (tolerancia $0), no anulada
    /// y (RUT emisor, N°) sin uso previo.
    /// </summary>
    public static ResultadoConciliacion Conciliar(
        DatosBoleta b, int rutPrestador, string rutEmpresa, DateOnly periodo,
        IEnumerable<decimal> totalesFilas, Func<int, string, bool> numeroYaUsado, int diaLimite = 10)
    {
        var problemas = new List<string>();
        var emisorIncorrecto = false;

        if (!RutHelper.TryParse(b.RutEmisor, out var emisor, out _))
            problemas.Add("No se pudo leer el RUT emisor.");
        else if (emisor != rutPrestador)
        {
            emisorIncorrecto = true;
            problemas.Add(MensajeEmisor + ".");
        }

        if (!RutHelper.TryParse(b.RutReceptor, out var receptor, out _) ||
            !RutHelper.TryParse(rutEmpresa, out var empresa, out _) || receptor != empresa)
            problemas.Add($"El RUT receptor debe ser {RutHelper.Formatear(rutEmpresa)}.");

        var inicio = new DateOnly(periodo.Year, periodo.Month, 1);
        var limite = FechaLimite(periodo, diaLimite);
        if (b.FechaEmision is null)
            problemas.Add("No se pudo leer la fecha de emisión.");
        else if (b.FechaEmision < inicio || b.FechaEmision > limite)
            problemas.Add($"La fecha {Formato.Fecha(b.FechaEmision)} está fuera del plazo ({Formato.Fecha(inicio)} a {Formato.Fecha(limite)}).");

        var suma = totalesFilas.Sum();
        var diferencia = (b.Bruto ?? 0) - suma;
        if (b.Bruto is null)
            problemas.Add("No se pudo leer el monto bruto.");
        else if (diferencia != 0)
            problemas.Add($"El bruto {Formato.Clp(b.Bruto)} no coincide con la suma de sus filas {Formato.Clp(suma)} (diferencia {Formato.Clp(diferencia)}).");

        if (b.Anulada)
            problemas.Add("La boleta está anulada.");

        if (string.IsNullOrWhiteSpace(b.Numero))
            problemas.Add("No se pudo leer el N° de boleta.");
        else if (emisor > 0 && numeroYaUsado(emisor, b.Numero))
            problemas.Add($"La boleta N° {b.Numero} ya se usó en otro pago.");

        return new(problemas, emisorIncorrecto, diferencia);
    }
}
