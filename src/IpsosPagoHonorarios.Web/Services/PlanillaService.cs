using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Services;

/// <summary>Envío, reenvío, diferimiento y archivo de versiones de la planilla.</summary>
public class PlanillaService(
    AppDbContext db, CicloService ciclos, ExcelPlanilla excel, Almacenamiento archivos, Auditor auditor, Correos correos)
{
    /// <summary>R-08: envía la planilla a Finanzas.</summary>
    public async Task EnviarAsync(int planillaId)
    {
        var p = await ciclos.PlanillaCompletaAsync(planillaId) ?? throw new ReglaException("Planilla no encontrada.");
        CicloService.ExigirAbierto(p.Ciclo);
        var v = Flujo.PuedeEnviar(p);
        if (!v.Ok) throw new ReglaException(string.Join(" ", v.Motivos));
        var antes = p.Estado;
        p.Estado = PlanillaEstado.EnRevision;
        await ArchivarVersionAsync(p);
        auditor.Registrar(nameof(Planilla), p.Id, "Enviar a Finanzas", $"{p.Titulo} v{p.Version}: {antes} → EnRevision");
        await db.SaveChangesAsync();
    }

    /// <summary>R-11: reenvío antes del vencimiento: la devolución queda a tiempo, la versión sube en uno y se archiva el XLSX.</summary>
    public async Task ReenviarAsync(int planillaId)
    {
        var p = await ciclos.PlanillaCompletaAsync(planillaId) ?? throw new ReglaException("Planilla no encontrada.");
        CicloService.ExigirAbierto(p.Ciclo);
        if (p.Estado != PlanillaEstado.Observada) throw new ReglaException("Solo se reenvía una planilla observada.");
        var d = await ciclos.DevolucionActivaAsync(p.Id) ?? throw new ReglaException("No hay una devolución pendiente.");
        if (!Flujo.ReenvioATiempo(d, ciclos.AhoraUtc)) throw new ReglaException("El plazo de corrección venció.");
        // TODO(diseño): ¿el reenvío exige 0 observaciones abiertas? Hoy se permite reenviar con observaciones pendientes.
        d.Resultado = DevolucionResultado.ReenviadaATiempo;
        d.ReenviadaEn = ciclos.AhoraUtc;
        p.Version += 1;
        p.Estado = PlanillaEstado.EnRevision;
        await ArchivarVersionAsync(p);
        auditor.Registrar(nameof(Planilla), p.Id, "Reenviar a Finanzas", $"{p.Titulo} v{p.Version} (a tiempo)");
        await db.SaveChangesAsync();
    }

    public async Task ArchivarVersionAsync(Planilla p)
    {
        var bytes = await excel.ExportarAsync(p);
        var nombre = Formato.NombreArchivoPlanilla(p.Ciclo.Periodo, p.Titulo, p.Version);
        var ruta = archivos.Guardar($"planillas/{p.Ciclo.Codigo}", nombre, bytes);
        db.PlanillaArchivos.Add(new PlanillaArchivo { PlanillaId = p.Id, Version = p.Version, NombreArchivo = nombre, Ruta = ruta, Tipo = "Planilla" });
        auditor.Registrar(nameof(PlanillaArchivo), p.Id, "Archivar versión", nombre);
    }

    /// <summary>
    /// R-25 / R-12: difiere las filas del prestador (todas, para mantener una sola boleta por planilla) y las copia a la planilla
    /// de la misma área del ciclo siguiente, que se crea si no existe.
    /// </summary>
    public async Task<List<LineaPago>> DiferirAsync(Planilla p, int prestadorId, string motivo, bool validarEstado = true)
    {
        CicloService.ExigirAbierto(p.Ciclo);
        if (p.Estado is PlanillaEstado.Aprobada or PlanillaEstado.EnPago or PlanillaEstado.Cerrada)
            throw new ReglaException("La planilla ya fue aprobada: no se puede diferir.");
        var lineas = p.Lineas.Where(l => l.PrestadorId == prestadorId && l.Estado != LineaEstado.Diferida).ToList();
        if (lineas.Count == 0) throw new ReglaException("No hay filas activas de ese prestador.");
        if (validarEstado && lineas.Any(l => l.Estado is not (LineaEstado.PendienteBoleta or LineaEstado.Observada) && !l.AlertaAbierta))
            throw new ReglaException("Solo se difieren filas pendientes de boleta, observadas o con alerta de cuenta.");

        var siguiente = await ciclos.ObtenerOCrearAsync(p.Ciclo.Periodo.AddMonths(1));
        if (siguiente.Id == 0) await db.SaveChangesAsync();
        // Destino: la planilla equivalente (misma área y mismo nombre) del ciclo siguiente; si no existe, se crea.
        var destino = db.Planillas.Local.Where(x => x.CicloId == siguiente.Id && x.AreaId == p.AreaId && x.Nombre == p.Nombre).OrderBy(x => x.Numero).FirstOrDefault()
                      ?? await db.Planillas.IgnoreQueryFilters().Include(x => x.Lineas)
                          .Where(x => x.CicloId == siguiente.Id && x.AreaId == p.AreaId && x.Nombre == p.Nombre).OrderBy(x => x.Numero).FirstOrDefaultAsync();
        if (destino is null)
        {
            var numeros = db.Planillas.Local.Where(x => x.CicloId == siguiente.Id && x.AreaId == p.AreaId).Select(x => x.Numero)
                .Concat(await db.Planillas.IgnoreQueryFilters().Where(x => x.CicloId == siguiente.Id && x.AreaId == p.AreaId).Select(x => x.Numero).ToListAsync())
                .ToList();
            destino = new Planilla
            {
                CicloId = siguiente.Id, AreaId = p.AreaId, Area = p.Area, Nombre = p.Nombre, Numero = numeros.Count == 0 ? 1 : numeros.Max() + 1,
                FechaRecepcion = ciclos.Hoy, ResponsableNombre = p.ResponsableNombre, ResponsableEmail = p.ResponsableEmail
            };
            db.Planillas.Add(destino);
            auditor.Registrar(nameof(Planilla), $"{siguiente.Codigo}/{destino.Titulo}", "Crear planilla por diferimiento");
        }
        var numero = destino.Lineas.Count == 0 ? 0 : destino.Lineas.Max(l => l.Numero);
        foreach (var l in lineas)
        {
            l.Estado = LineaEstado.Diferida;
            l.DiferidaACicloId = siguiente.Id;
            destino.Lineas.Add(new LineaPago
            {
                Numero = ++numero, TipoGasto = l.TipoGasto, JobId = l.JobId, GlosaId = l.GlosaId,
                ValorUnitarioBruto = l.ValorUnitarioBruto, Cantidad = l.Cantidad, ValorTotalBruto = l.ValorTotalBruto,
                PrestadorId = l.PrestadorId, NombrePlanilla = l.NombrePlanilla,
                CuentaPlanillaTipo = l.CuentaPlanillaTipo, CuentaPlanillaNumero = l.CuentaPlanillaNumero, CuentaPlanillaBanco = l.CuentaPlanillaBanco,
                CuentaBancariaId = l.CuentaBancariaId, ResultadoCuenta = l.ResultadoCuenta, ResultadoCuentaDetalle = l.ResultadoCuentaDetalle,
                AlertaCuentaResuelta = l.AlertaCuentaResuelta, Estado = LineaEstado.PendienteBoleta, DiferidaDesdeCicloId = p.CicloId
            });
        }
        var prest = lineas[0].Prestador;
        auditor.Registrar(nameof(LineaPago), string.Join(",", lineas.Select(l => l.Numero)), "Diferir",
            $"{p.Titulo} {p.Ciclo.Codigo} → {siguiente.Codigo}: {prest?.NombreCompleto} ({motivo})");
        if (prest is not null)
            correos.Encolar(prest.Email, $"Tu pago de {p.Ciclo.Codigo} pasa a {siguiente.Codigo}",
                $"Hola {prest.NombreCompleto}: tus filas de la planilla {p.Titulo} se pagarán en el ciclo {siguiente.Codigo}. Motivo: {motivo}.");

        // Sin alertas pendientes la planilla vuelve a Borrador (R-25).
        if (p.Estado == PlanillaEstado.ConAlertasCuenta && !p.Activas().Any(l => l.AlertaAbierta))
            p.Estado = PlanillaEstado.Borrador;
        return lineas;
    }

    public async Task DiferirLineaAsync(int lineaId, string motivo)
    {
        var l = await db.LineasPago.FirstOrDefaultAsync(x => x.Id == lineaId) ?? throw new ReglaException("Línea no encontrada.");
        var p = await ciclos.PlanillaCompletaAsync(l.PlanillaId) ?? throw new ReglaException("Planilla no encontrada.");
        await DiferirAsync(p, l.PrestadorId, motivo);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Revierte un diferimiento: trae de vuelta las filas de un prestador desde la planilla del ciclo siguiente a la
    /// planilla <paramref name="planillaId"/>, siempre que en el destino sigan pendientes de boleta (sin boleta ni pago).
    /// No guarda: la reapertura de la observación y la nueva devolución las hace RevisionService en la misma transacción.
    /// </summary>
    public async Task<List<LineaPago>> RevertirDiferimientoAsync(int planillaId, int prestadorId, string motivo)
    {
        var p = await ciclos.PlanillaCompletaAsync(planillaId, todasLasAreas: true) ?? throw new ReglaException("Planilla no encontrada.");
        CicloService.ExigirAbierto(p.Ciclo);
        if (p.Estado is PlanillaEstado.Aprobada or PlanillaEstado.EnPago or PlanillaEstado.Cerrada)
            throw new ReglaException("La planilla ya fue aprobada: no se puede revertir el diferimiento.");
        var origen = p.Lineas.Where(l => l.PrestadorId == prestadorId && l.Estado == LineaEstado.Diferida).ToList();
        if (origen.Count == 0) throw new ReglaException("Este prestador no tiene filas diferidas en esta planilla.");
        var cicloDestinoId = origen[0].DiferidaACicloId ?? throw new ReglaException("No se encuentra el ciclo de destino del diferimiento.");

        var destinos = await db.Planillas.IgnoreQueryFilters()
            .Include(x => x.Ciclo).Include(x => x.Lineas).Include(x => x.Boletas)
            .Where(x => x.CicloId == cicloDestinoId && x.AreaId == p.AreaId && x.Nombre == p.Nombre).ToListAsync();
        var copiadas = destinos.SelectMany(d => d.Lineas.Where(l => l.PrestadorId == prestadorId && l.DiferidaDesdeCicloId == p.CicloId)).ToList();

        // Seguridad: el destino no debe haber avanzado (ciclo abierto, planilla sin aprobar, sin boleta ni pago del prestador).
        var destinoCiclo = destinos.FirstOrDefault()?.Ciclo;
        if (destinoCiclo is not null && destinoCiclo.Estado != CicloEstado.Abierto)
            throw new ReglaException($"El ciclo de destino {destinoCiclo.Codigo} ya está cerrado: no se puede revertir.");
        foreach (var d in destinos.Where(d => d.Lineas.Any(l => l.PrestadorId == prestadorId && l.DiferidaDesdeCicloId == p.CicloId))
                     .Where(d => d.Estado is PlanillaEstado.Aprobada or PlanillaEstado.EnPago or PlanillaEstado.Cerrada))
            throw new ReglaException($"En el ciclo siguiente la planilla {d.Titulo} ya fue aprobada: no se puede revertir.");
        if (copiadas.Any(l => l.Estado != LineaEstado.PendienteBoleta))
            throw new ReglaException("En el ciclo siguiente el prestador ya avanzó (boleta o pago): no se puede revertir automáticamente.");
        if (destinos.Any(d => d.Boletas.Any(b => b.PrestadorId == prestadorId && b.Vigente)))
            throw new ReglaException("El prestador ya subió una boleta en el ciclo siguiente: no se puede revertir automáticamente.");

        // Quitar las copias del destino y restaurar las filas de origen.
        db.LineasPago.RemoveRange(copiadas);
        foreach (var d in destinos) d.Lineas.RemoveAll(copiadas.Contains);
        foreach (var l in origen) { l.Estado = LineaEstado.PendienteBoleta; l.DiferidaACicloId = null; }

        // Planilla de destino que quedó vacía y se había creado por el diferimiento: se elimina para no dejar basura.
        foreach (var d in destinos.Where(d => d.Lineas.Count == 0 && d.Estado == PlanillaEstado.Borrador && d.Boletas.Count == 0))
            db.Planillas.Remove(d);

        var prest = origen[0].Prestador;
        auditor.Registrar(nameof(LineaPago), string.Join(",", origen.Select(l => l.Numero)), "Revertir diferimiento",
            $"{p.Titulo} {p.Ciclo.Codigo} ← {destinoCiclo?.Codigo}: {prest?.NombreCompleto} ({motivo})");
        if (prest is not null)
            correos.Encolar(prest.Email, $"Tu pago vuelve al ciclo {p.Ciclo.Codigo}",
                $"Hola {prest.NombreCompleto}: tus filas de la planilla {p.Titulo} vuelven al ciclo {p.Ciclo.Codigo}. Motivo: {motivo}.");
        return origen;
    }
}
