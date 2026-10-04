namespace IpsosPagoHonorarios.Core;

public sealed record Verificacion(bool Ok, List<string> Motivos)
{
    public static Verificacion De(List<string> motivos) => new(motivos.Count == 0, motivos);
}

/// <summary>Resultado de un chequeo de revisión (R-09): Ok, Pendiente o con error.</summary>
public enum Chequeo { Ok, Pendiente, Error }

public sealed record ChequeosPrestador(
    int PrestadorId, Chequeo Personal, Chequeo Operativa, Chequeo Tributaria, Chequeo Bancaria,
    string? DetalleTributaria, string? DetalleBancaria, decimal SumaPlanilla, decimal? MontoBoleta, string? DetalleOperativa = null);

/// <summary>Reglas de transición de la planilla y sus líneas.</summary>
public static class Flujo
{
    public static IEnumerable<LineaPago> Activas(this Planilla p) => p.Lineas.Where(l => l.Estado != LineaEstado.Diferida);

    public static BoletaHonorarios? BoletaVigente(this Planilla p, int prestadorId) =>
        p.Boletas.Where(b => b.PrestadorId == prestadorId && b.Vigente).OrderByDescending(b => b.SubidaEn).ThenByDescending(b => b.Id).FirstOrDefault();

    /// <summary>
    /// R-08 / R-25: solo se envía sin alertas de cuenta abiertas y con boleta de todos los prestadores
    /// (o con esas filas diferidas). También revisa R-01 y R-03 sobre las filas.
    /// </summary>
    public static Verificacion PuedeEnviar(Planilla p)
    {
        var motivos = new List<string>();
        if (p.Estado is not (PlanillaEstado.Borrador or PlanillaEstado.ConAlertasCuenta))
            motivos.Add($"La planilla está {p.Estado.Nombre()}.");
        var activas = p.Activas().ToList();
        if (activas.Count == 0) motivos.Add("La planilla no tiene líneas activas.");
        var alertas = activas.Where(l => l.AlertaAbierta).Select(l => l.PrestadorId).Distinct().Count();
        if (alertas > 0) motivos.Add($"Hay {alertas} alertas de cuenta abiertas. Resuélvelas o difiere las filas.");
        var sinBoleta = activas.Select(l => l.PrestadorId).Distinct().Count(id => p.BoletaVigente(id) is null);
        if (sinBoleta > 0) motivos.Add($"Faltan boletas de {sinBoleta} prestadores. Espera la boleta o difiere sus filas.");
        foreach (var l in activas)
        {
            if (l.Prestador is not null && !RutHelper.EsValido(l.Prestador.RutPlanilla)) motivos.Add($"Línea {l.Numero}: RUT inválido.");
            if (l.ValorTotalBruto <= 0) motivos.Add($"Línea {l.Numero}: el valor total debe ser mayor que 0.");
        }
        return Verificacion.De(motivos);
    }

    /// <summary>R-09: chequeos por prestador en cuatro grupos.</summary>
    public static ChequeosPrestador Revisar(Planilla p, int prestadorId)
    {
        var lineas = p.Activas().Where(l => l.PrestadorId == prestadorId).ToList();
        var prest = lineas.Select(l => l.Prestador).FirstOrDefault();
        var personal = prest is not null && RutHelper.EsValido(prest.RutPlanilla) && !string.IsNullOrWhiteSpace(prest.NombreCompleto)
            ? Chequeo.Ok : Chequeo.Error;
        var operativa = lineas.All(l => l.Job is not null && ProduccionReglas.JobValido(l.Job.JobBookNumber) && l.Glosa is not null &&
                                        l.Cantidad > 0 && l.ValorUnitarioBruto > 0 && l.ValorTotalBruto > 0)
            ? Chequeo.Ok : Chequeo.Error;
        // El total de la planilla puede venir por fórmula o ingresado a mano: se informa, no es error.
        var manuales = lineas.Where(l => l.ValorTotalBruto != Montos.ValorTotal(l.ValorUnitarioBruto, l.Cantidad)).Select(l => l.Numero).ToList();
        var detOper = manuales.Count == 0 ? null : $"Total ingresado a mano (≠ F×G) en línea {string.Join(", ", manuales)}";

        var suma = lineas.Sum(l => l.ValorTotalBruto);
        var boleta = p.BoletaVigente(prestadorId);
        Chequeo tributaria; string? detTrib = null;
        if (boleta is null) { tributaria = Chequeo.Error; detTrib = "Sin boleta"; }
        else if (boleta.MontoBruto != suma) { tributaria = Chequeo.Error; detTrib = "Diferencia " + Formato.Clp((boleta.MontoBruto ?? 0) - suma); }
        else if (!boleta.Cuadra) { tributaria = Chequeo.Error; detTrib = "No cuadra"; }
        else tributaria = Chequeo.Ok;

        var cuenta = prest?.Cuentas.FirstOrDefault(c => c.Vigente);
        Chequeo bancaria; string? detBanc = null;
        if (cuenta is null) { bancaria = Chequeo.Error; detBanc = "Sin cuenta registrada"; }
        else if (cuenta.PrestadorId != prestadorId) { bancaria = Chequeo.Error; detBanc = "Titular distinto"; }
        else if (lineas.Any(l => l.AlertaAbierta)) { bancaria = Chequeo.Error; detBanc = "Alerta de cuenta"; }
        else if (cuenta.Estado != CuentaEstado.Validada) { bancaria = Chequeo.Pendiente; detBanc = "Cuenta por validar"; }
        else bancaria = Chequeo.Ok;

        return new(prestadorId, personal, operativa, tributaria, bancaria, detTrib, detBanc, suma, boleta?.MontoBruto, detOper);
    }

    /// <summary>R-10: devolver requiere al menos una observación abierta.</summary>
    public static Verificacion PuedeDevolver(Planilla p, IEnumerable<Observacion> observaciones)
    {
        var motivos = new List<string>();
        if (p.Estado != PlanillaEstado.EnRevision) motivos.Add("Solo se devuelve una planilla en revisión.");
        if (!observaciones.Any(o => o.Estado == ObservacionEstado.Abierta)) motivos.Add("Se requiere al menos una observación abierta.");
        return Verificacion.De(motivos);
    }

    /// <summary>R-10: la devolución vence a los 60 minutos (configurable).</summary>
    public static DateTime Vencimiento(DateTime devueltaEn, int plazoMinutos = 60) => devueltaEn.AddMinutes(plazoMinutos);

    /// <summary>R-11: reenvío antes del vencimiento.</summary>
    public static bool ReenvioATiempo(Devolucion d, DateTime ahora) => d.Resultado == DevolucionResultado.Pendiente && ahora < d.VenceEn;

    /// <summary>
    /// R-13: aprobar solo sin observaciones abiertas ni correcciones sin revisar, con todas las cuentas validadas y todas las
    /// boletas cuadradas. R-23: solo se aprueba una línea con cuenta validada.
    /// </summary>
    public static Verificacion PuedeAprobar(Planilla p, IEnumerable<Observacion> observaciones)
    {
        var motivos = new List<string>();
        if (p.Estado != PlanillaEstado.EnRevision) motivos.Add("Solo se aprueba una planilla en revisión.");
        var obs = observaciones.ToList();
        var abiertas = obs.Count(o => o.Estado == ObservacionEstado.Abierta);
        if (abiertas > 0) motivos.Add($"Hay {abiertas} observaciones abiertas.");
        var sinRevisar = obs.Count(o => o.Estado == ObservacionEstado.Corregida);
        if (sinRevisar > 0) motivos.Add($"Hay {sinRevisar} correcciones sin revisar.");
        foreach (var id in p.Activas().Select(l => l.PrestadorId).Distinct())
        {
            var c = Revisar(p, id);
            var nombre = p.Lineas.First(l => l.PrestadorId == id).Prestador?.NombreCompleto ?? id.ToString();
            if (c.Bancaria != Chequeo.Ok) motivos.Add($"{nombre}: cuenta no validada ({c.DetalleBancaria}).");
            if (c.Tributaria != Chequeo.Ok) motivos.Add($"{nombre}: boleta no cuadrada ({c.DetalleTributaria}).");
        }
        if (!p.Activas().Any()) motivos.Add("La planilla no tiene líneas activas.");
        return Verificacion.De(motivos);
    }

    /// <summary>R-15: cierre solo con todas las líneas del ciclo pagadas o diferidas.</summary>
    public static Verificacion PuedeCerrar(Ciclo c) =>
        PuedeCerrar(c.Estado, c.Planillas.SelectMany(p => p.Lineas).Count(l => l.Estado is not (LineaEstado.Pagada or LineaEstado.Diferida)));

    /// <summary>Misma regla con el conteo hecho en la base (sin cargar las líneas del ciclo).</summary>
    public static Verificacion PuedeCerrar(CicloEstado estado, int lineasPendientes)
    {
        var motivos = new List<string>();
        if (estado == CicloEstado.Cerrado) motivos.Add("El ciclo ya está cerrado.");
        if (lineasPendientes > 0) motivos.Add($"Faltan {lineasPendientes} líneas por pagar o diferir.");
        return Verificacion.De(motivos);
    }

    /// <summary>R-18: el prestador sube boleta desde que la planilla existe hasta que se aprueba, solo para filas pendientes o con boleta observada.</summary>
    public static Verificacion PuedeSubirBoleta(Planilla p, int prestadorId)
    {
        var motivos = new List<string>();
        if (p.Estado is PlanillaEstado.Aprobada or PlanillaEstado.EnPago or PlanillaEstado.Cerrada)
            motivos.Add("La planilla ya fue aprobada.");
        var lineas = p.Lineas.Where(l => l.PrestadorId == prestadorId).ToList();
        if (lineas.Count == 0) motivos.Add("No tienes filas en esta planilla.");
        var boleta = p.BoletaVigente(prestadorId);
        var habilitada = lineas.Any(l => l.Estado is LineaEstado.PendienteBoleta or LineaEstado.Observada) ||
                         boleta?.Estado == BoletaEstado.Observada;
        if (lineas.Count > 0 && !habilitada) motivos.Add("Tu boleta ya fue recibida y no está observada.");
        return Verificacion.De(motivos);
    }

    /// <summary>R-12: líneas a diferir al vencer el plazo (todas las filas de prestadores con observaciones abiertas).</summary>
    public static List<LineaPago> LineasADiferir(Planilla p, IEnumerable<Observacion> observaciones)
    {
        var prestadores = observaciones.Where(o => o.Estado == ObservacionEstado.Abierta)
            .Select(o => o.LineaPago.PrestadorId).ToHashSet();
        return p.Activas().Where(l => prestadores.Contains(l.PrestadorId)).ToList();
    }

    /// <summary>R-06: N° de boleta que se repite en todas las filas del prestador (columna K).</summary>
    public static void PropagarNumeroBoleta(Planilla p, int prestadorId, string? numero)
    {
        foreach (var l in p.Lineas.Where(l => l.PrestadorId == prestadorId && l.Estado != LineaEstado.Diferida))
            l.NumeroBoleta = numero;
    }
}
