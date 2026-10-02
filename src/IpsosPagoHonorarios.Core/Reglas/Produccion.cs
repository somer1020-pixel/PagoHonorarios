namespace IpsosPagoHonorarios.Core;

/// <summary>Una fila leída de la exportación del sistema o de una planilla en formato Finanzas.</summary>
public sealed record FilaProduccion
{
    public int Fila { get; init; }
    public string? TipoGasto { get; init; }
    public string? Job { get; init; }
    public string? NombreJob { get; init; }
    public string? Glosa { get; init; }
    public decimal? ValorUnitario { get; init; }
    public decimal? Cantidad { get; init; }
    /// <summary>Valor total bruto escrito en el archivo (columna H), si viene.</summary>
    public decimal? TotalArchivo { get; init; }
    public string? Rut { get; init; }
    public string? Nombre { get; init; }
    public string? NumeroBoleta { get; init; }
    public string? TipoCuenta { get; init; }
    public string? Cuenta { get; init; }
    public string? Banco { get; init; }
}

public sealed record ErrorFila(int Fila, string Campo, string Motivo);

public sealed record ResultadoValidacionProduccion(List<ErrorFila> Errores, List<string> Avisos)
{
    public bool Valido => Errores.Count == 0;
}

public static class ProduccionReglas
{
    /// <summary>R-02: la producción se carga entre los días 28 y 30; fuera de esa ventana se permite con aviso.</summary>
    public static string? AvisoVentana(DateOnly hoy, int desde = 28, int hasta = 30) =>
        hoy.Day >= desde && hoy.Day <= hasta
            ? null
            : $"Hoy {Formato.Fecha(hoy)} está fuera de la ventana de descarga (días {desde} a {hasta}). Se puede cargar, con aviso.";

    /// <summary>Valor total de la fila: el de la columna H si viene (fórmula o valor manual); si no, ROUND(F×G) (R-03).</summary>
    public static decimal TotalDe(FilaProduccion f) =>
        f.TotalArchivo is { } h ? Montos.RedondearExcel(h) : Montos.ValorTotal(f.ValorUnitario ?? 0, f.Cantidad ?? 0);

    /// <summary>Informa las filas cuyo total (H) se ingresó a mano y no corresponde a ROUND(F×G). Se respeta el valor de H.</summary>
    public static List<string> AvisosTotales(IEnumerable<FilaProduccion> filas)
    {
        var avisos = new List<string>();
        foreach (var f in filas)
        {
            if (f.TotalArchivo is not { } h || f.ValorUnitario is not { } vu || f.Cantidad is not { } q) continue;
            var calculado = Montos.ValorTotal(vu, q);
            if (Montos.RedondearExcel(h) != calculado)
                avisos.Add($"Fila {f.Fila}: valor total ingresado a mano (H = {Formato.Clp(h)}); F×G daría {Formato.Cantidad(q)} × {Formato.Clp(vu)} = " +
                           $"{Formato.Clp(calculado)} (diferencia {Formato.Clp(Montos.RedondearExcel(h) - calculado)}). Se usa el valor de H.");
        }
        return avisos;
    }

    public static bool JobValido(string? job) => job is { Length: 12 } && job.All(char.IsAsciiDigit);

    /// <summary>
    /// R-02: rechaza la fila si el RUT es inválido, el Job no tiene 12 dígitos, la glosa no está en catálogo
    /// o el valor unitario o la cantidad son ≤ 0. Si el RUT o el Job no existen, se avisa que se crearán.
    /// Un archivo con errores no se carga.
    /// </summary>
    public static ResultadoValidacionProduccion Validar(
        IEnumerable<FilaProduccion> filas,
        IReadOnlyCollection<string> glosasCatalogo,
        Func<int, bool> existeRut,
        Func<string, bool> existeJob)
    {
        var errores = new List<ErrorFila>();
        var avisos = new List<string>();
        var rutsNuevos = new HashSet<int>();
        var jobsNuevos = new HashSet<string>();
        var algunaFila = false;

        foreach (var f in filas)
        {
            algunaFila = true;
            var rutError = RutHelper.Error(f.Rut);
            if (rutError is not null)
                errores.Add(new(f.Fila, "Rut", $"RUT inválido: {f.Rut} ({(rutError == "Dígito verificador incorrecto" ? "dígito verificador" : rutError.ToLowerInvariant())})"));
            else if (RutHelper.TryParse(f.Rut, out var cuerpo, out var dv) && !existeRut(cuerpo) && rutsNuevos.Add(cuerpo))
                avisos.Add($"RUT {RutHelper.Formatear(cuerpo, dv)} no existe: se creará el prestador.");

            var job = f.Job?.Trim();
            if (!JobValido(job))
                errores.Add(new(f.Fila, "Job Book Number", $"{(string.IsNullOrEmpty(job) ? "(vacío)" : job)} tiene {job?.Count(char.IsAsciiDigit) ?? 0} dígitos; se esperan 12"));
            else if (!existeJob(job!) && jobsNuevos.Add(job!))
                avisos.Add($"Job {job} no existe: se creará.");

            if (string.IsNullOrWhiteSpace(f.Glosa) || !glosasCatalogo.Contains(f.Glosa.Trim()))
                errores.Add(new(f.Fila, "Nombre Glosa", $"La glosa «{f.Glosa}» no está en el catálogo"));

            if (f.ValorUnitario is null or <= 0)
                errores.Add(new(f.Fila, "Valor unitario bruto", $"Debe ser mayor que 0 (vino {Mostrar(f.ValorUnitario)})"));
            if (f.Cantidad is null or <= 0)
                errores.Add(new(f.Fila, "Cantidad", $"Debe ser mayor que 0 (vino {Mostrar(f.Cantidad)})"));
            if (f.TotalArchivo is <= 0)
                errores.Add(new(f.Fila, "Valor total bruto", $"Debe ser mayor que 0 (vino {Mostrar(f.TotalArchivo)})"));
        }

        if (!algunaFila)
            errores.Add(new(0, "Archivo", "El archivo no tiene filas de datos."));
        return new(errores, avisos);
    }

    private static string Mostrar(decimal? v) => v is null ? "vacío" : Formato.Cantidad(v.Value);
}
