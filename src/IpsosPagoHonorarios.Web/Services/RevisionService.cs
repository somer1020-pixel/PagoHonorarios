using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Services;

/// <summary>Revisión de Finanzas, observaciones, devolución con plazo, correcciones y aprobación (R-09 a R-13).</summary>
public class RevisionService(
    AppDbContext db, CicloService ciclos, Parametros parametros, Auditor auditor, Correos correos, IUsuarioActual usuario)
{
    public async Task<Observacion> ObservarAsync(int lineaId, ObservacionTipo tipo, string campo, string detalle)
    {
        if (string.IsNullOrWhiteSpace(campo) || string.IsNullOrWhiteSpace(detalle))
            throw new ReglaException("Línea, tipo, campo y detalle son obligatorios.");
        var l = await db.LineasPago.Include(x => x.Prestador).Include(x => x.Planilla).ThenInclude(p => p.Ciclo)
                    .FirstOrDefaultAsync(x => x.Id == lineaId) ?? throw new ReglaException("Línea no encontrada.");
        CicloService.ExigirAbierto(l.Planilla.Ciclo);
        if (l.Planilla.Estado != PlanillaEstado.EnRevision) throw new ReglaException("Solo se observa una planilla en revisión.");
        if (l.Estado == LineaEstado.Diferida) throw new ReglaException("La línea está diferida.");
        var o = new Observacion
        {
            LineaPagoId = l.Id, Tipo = tipo, Campo = campo.Trim(), Detalle = detalle.Trim(), CreadaPor = usuario.Nombre, CreadaEn = ciclos.AhoraUtc
        };
        db.Observaciones.Add(o);
        l.Estado = LineaEstado.Observada;
        auditor.Registrar(nameof(Observacion), l.Id, "Observar", $"Línea {l.Numero} · {l.Prestador.NombreCompleto} · {tipo.Nombre()} · {campo}: {detalle}");
        await db.SaveChangesAsync();
        return o;
    }

    /// <summary>
    /// R-10: requiere ≥ 1 observación abierta. Crea la devolución que vence en 60 minutos, deja la planilla Observada, avisa al
    /// responsable y a los prestadores con problemas de boleta.
    /// </summary>
    public async Task<Devolucion> DevolverAsync(int planillaId)
    {
        var p = await ciclos.PlanillaCompletaAsync(planillaId) ?? throw new ReglaException("Planilla no encontrada.");
        CicloService.ExigirAbierto(p.Ciclo);
        var obs = await ciclos.ObservacionesAsync(p.Id);
        var v = Flujo.PuedeDevolver(p, obs);
        if (!v.Ok) throw new ReglaException(string.Join(" ", v.Motivos));
        var par = await parametros.ObtenerAsync();
        var ahora = ciclos.AhoraUtc;
        var d = new Devolucion
        {
            PlanillaId = p.Id, Version = p.Version, DevueltaEn = ahora, DevueltaPor = usuario.Nombre,
            VenceEn = Flujo.Vencimiento(ahora, par.PlazoCorreccionMinutos)
        };
        db.Devoluciones.Add(d);
        var abiertas = obs.Where(o => o.Estado == ObservacionEstado.Abierta).ToList();
        foreach (var o in abiertas) o.Devolucion = d;
        p.Estado = PlanillaEstado.Observada;

        var vence = Formato.FechaHora(d.VenceEn);
        correos.Encolar(p.ResponsableEmail, $"Planilla {p.Titulo} v{p.Version} devuelta por Finanzas (vence {Formato.Hora(d.VenceEn)})",
            $"Hola {p.ResponsableNombre}: Finanzas devolvió la planilla con {abiertas.Count} observaciones. Tienes hasta {vence}. Lo que no se corrija pasa al ciclo siguiente.\n" +
            string.Join("\n", abiertas.Select(o => $"Línea {o.LineaPago.Numero} · {o.Tipo.Nombre()} · {o.Campo}: {o.Detalle}")));
        foreach (var g in abiertas.Where(o => o.Tipo is ObservacionTipo.FaltaBoleta or ObservacionTipo.DiferenciaMontos).GroupBy(o => o.LineaPago.PrestadorId))
        {
            var prest = g.First().LineaPago.Prestador;
            var boleta = p.BoletaVigente(prest.Id);
            if (boleta is not null) boleta.Estado = BoletaEstado.Observada;
            correos.Encolar(prest.Email, $"Tu boleta fue observada (plazo {Formato.Plazo(par.PlazoCorreccionMinutos)})",
                $"Hola {prest.NombreCompleto}: {string.Join(" ", g.Select(o => o.Detalle))} Sube una nueva boleta en el portal antes de {vence}.");
        }
        auditor.Registrar(nameof(Planilla), p.Id, "Devolver planilla", $"{p.Titulo} v{p.Version} con {abiertas.Count} observaciones; vence {vence}");
        await db.SaveChangesAsync();
        return d;
    }

    /// <summary>Operaciones corrige una observación: puede ajustar cantidad, valor unitario, Job Book Number y glosa (ítem).</summary>
    public async Task CorregirAsync(int observacionId, string respuesta, decimal? cantidad = null, decimal? valorUnitario = null,
        string? jobBookNumber = null, int? glosaId = null)
    {
        var o = await db.Observaciones.Include(x => x.LineaPago).ThenInclude(l => l.Planilla).ThenInclude(p => p.Ciclo)
                    .FirstOrDefaultAsync(x => x.Id == observacionId) ?? throw new ReglaException("Observación no encontrada.");
        CicloService.ExigirAbierto(o.LineaPago.Planilla.Ciclo);
        if (o.Estado != ObservacionEstado.Abierta) throw new ReglaException("La observación no está abierta.");
        if (o.LineaPago.Planilla.Estado is not (PlanillaEstado.Observada or PlanillaEstado.EnRevision))
            throw new ReglaException("La planilla no admite correcciones.");
        if (string.IsNullOrWhiteSpace(respuesta)) throw new ReglaException("Describe la corrección.");
        var l = o.LineaPago;
        var cambios = new List<string>();
        jobBookNumber = string.IsNullOrWhiteSpace(jobBookNumber) ? null : jobBookNumber.Trim();
        if (jobBookNumber is not null)
        {
            if (!ProduccionReglas.JobValido(jobBookNumber)) throw new ReglaException("El Job Book Number debe tener 12 dígitos.");
            var antes = await db.Jobs.FirstAsync(j => j.Id == l.JobId);
            if (antes.JobBookNumber != jobBookNumber)
            {
                var job = await db.Jobs.FirstOrDefaultAsync(j => j.JobBookNumber == jobBookNumber);
                if (job is null)
                {
                    // Igual que en la carga de producción: un Job nuevo se crea y queda en la bitácora.
                    job = new Job { JobBookNumber = jobBookNumber, Nombre = "(creado en corrección)", AreaSugeridaId = l.Planilla.AreaId };
                    db.Jobs.Add(job);
                    auditor.Registrar(nameof(Job), jobBookNumber, "Crear Job (corrección)", $"Línea {l.Numero}");
                }
                l.Job = job;
                cambios.Add($"Job {antes.JobBookNumber} → {jobBookNumber}");
            }
        }
        if (glosaId is not null && glosaId != l.GlosaId)
        {
            var nueva = await db.Glosas.FirstOrDefaultAsync(g => g.Id == glosaId) ?? throw new ReglaException("Glosa no encontrada.");
            var antes = await db.Glosas.FirstAsync(g => g.Id == l.GlosaId);
            l.GlosaId = nueva.Id;
            l.Glosa = nueva;
            cambios.Add($"Ítem {antes.Item} ({antes.NombreGlosa}) → {nueva.Item} ({nueva.NombreGlosa})");
        }
        if (cantidad is not null || valorUnitario is not null)
        {
            if (cantidad is <= 0 || valorUnitario is <= 0) throw new ReglaException("Valor unitario y cantidad deben ser mayores que 0.");
            l.Cantidad = cantidad ?? l.Cantidad;
            l.ValorUnitarioBruto = valorUnitario ?? l.ValorUnitarioBruto;
            var total = Montos.ValorTotal(l.ValorUnitarioBruto, l.Cantidad);
            if (total != l.ValorTotalBruto) cambios.Add($"Total {Formato.Clp(l.ValorTotalBruto)} → {Formato.Clp(total)}");
            l.ValorTotalBruto = total;
        }
        o.Estado = ObservacionEstado.Corregida;
        o.Respuesta = respuesta.Trim();
        o.CorregidaPor = usuario.Nombre;
        o.CorregidaEn = ciclos.AhoraUtc;
        l.Estado = LineaEstado.Corregida;
        auditor.Registrar(nameof(Observacion), o.Id, "Corregir observación",
            $"Línea {l.Numero}: {respuesta}" + (cambios.Count == 0 ? "" : " · " + string.Join(" · ", cambios)));
        await db.SaveChangesAsync();
    }

    /// <summary>Finanzas acepta una corrección.</summary>
    public async Task AceptarAsync(int observacionId)
    {
        var o = await CargarAsync(observacionId);
        if (o.Estado != ObservacionEstado.Corregida) throw new ReglaException("Solo se acepta una observación corregida.");
        o.Estado = ObservacionEstado.Aceptada;
        var pendientes = await db.Observaciones.AnyAsync(x => x.LineaPagoId == o.LineaPagoId && x.Id != o.Id &&
                                                             (x.Estado == ObservacionEstado.Abierta || x.Estado == ObservacionEstado.Corregida));
        if (!pendientes && o.LineaPago.Estado == LineaEstado.Corregida) o.LineaPago.Estado = LineaEstado.Lista;
        auditor.Registrar(nameof(Observacion), o.Id, "Aceptar corrección", $"Línea {o.LineaPago.Numero}");
        await db.SaveChangesAsync();
    }

    /// <summary>Finanzas reabre una corrección.</summary>
    public async Task ReabrirAsync(int observacionId)
    {
        var o = await CargarAsync(observacionId);
        if (o.Estado != ObservacionEstado.Corregida) throw new ReglaException("Solo se reabre una observación corregida.");
        o.Estado = ObservacionEstado.Abierta;
        o.LineaPago.Estado = LineaEstado.Observada;
        auditor.Registrar(nameof(Observacion), o.Id, "Reabrir corrección", $"Línea {o.LineaPago.Numero}");
        await db.SaveChangesAsync();
    }

    private async Task<Observacion> CargarAsync(int id)
    {
        var o = await db.Observaciones.Include(x => x.LineaPago).ThenInclude(l => l.Planilla).ThenInclude(p => p.Ciclo)
                    .FirstOrDefaultAsync(x => x.Id == id) ?? throw new ReglaException("Observación no encontrada.");
        CicloService.ExigirAbierto(o.LineaPago.Planilla.Ciclo);
        return o;
    }

    /// <summary>R-13: aprobación. Congela la planilla y confirma las boletas.</summary>
    public async Task AprobarAsync(int planillaId)
    {
        var p = await ciclos.PlanillaCompletaAsync(planillaId) ?? throw new ReglaException("Planilla no encontrada.");
        CicloService.ExigirAbierto(p.Ciclo);
        var obs = await ciclos.ObservacionesAsync(p.Id);
        var v = Flujo.PuedeAprobar(p, obs);
        if (!v.Ok) throw new ReglaException(string.Join(" ", v.Motivos));
        p.Estado = PlanillaEstado.Aprobada;
        foreach (var l in p.Activas()) l.Estado = LineaEstado.Aprobada;
        foreach (var id in p.Activas().Select(l => l.PrestadorId).Distinct())
        {
            var b = p.BoletaVigente(id)!;
            b.Estado = BoletaEstado.Confirmada;
            b.ConfirmadaPor ??= usuario.Nombre;
            b.ConfirmadaEn ??= ciclos.AhoraUtc;
        }
        auditor.Registrar(nameof(Planilla), p.Id, "Aprobar planilla", $"{p.Titulo} v{p.Version}");
        await db.SaveChangesAsync();
    }
}

/// <summary>R-12: al vencer el plazo, las filas con observaciones abiertas se difieren y las observaciones quedan Vencida.</summary>
public class PlazosService(AppDbContext db, CicloService ciclos, PlanillaService planillas, Auditor auditor, Correos correos)
{
    public async Task<int> ProcesarVencidasAsync(CancellationToken ct = default)
    {
        var ahora = ciclos.AhoraUtc;
        var vencidas = await db.Devoluciones.Where(d => d.Resultado == DevolucionResultado.Pendiente && d.VenceEn <= ahora).ToListAsync(ct);
        foreach (var d in vencidas)
        {
            var p = await ciclos.PlanillaCompletaAsync(d.PlanillaId);
            if (p is null) continue;
            var obs = await ciclos.ObservacionesAsync(p.Id);
            var aDiferir = Flujo.LineasADiferir(p, obs);
            foreach (var prestadorId in aDiferir.Select(l => l.PrestadorId).Distinct())
                await planillas.DiferirAsync(p, prestadorId, "plazo de corrección vencido", validarEstado: false);
            foreach (var o in obs.Where(o => o.Estado == ObservacionEstado.Abierta)) o.Estado = ObservacionEstado.Vencida;
            d.Resultado = DevolucionResultado.Vencida;
            if (p.Estado == PlanillaEstado.Observada) p.Estado = PlanillaEstado.EnRevision;
            auditor.Registrar(nameof(Devolucion), d.Id, "Plazo vencido",
                $"{p.Titulo} v{p.Version}: {aDiferir.Count} filas diferidas; el resto sigue en revisión");
            correos.Encolar(p.ResponsableEmail, $"Plazo vencido: {p.Titulo} {p.Ciclo.Codigo}",
                $"Venció el plazo de corrección. {aDiferir.Count} filas pasaron al ciclo siguiente.");
            await db.SaveChangesAsync(ct);
        }
        return vencidas.Count;
    }
}

/// <summary>Proceso en segundo plano que revisa los plazos cada minuto (R-12).</summary>
public class PlazosBackgroundService(IServiceScopeFactory scopes, TimeProvider reloj, ILogger<PlazosBackgroundService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), reloj);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var n = await scope.ServiceProvider.GetRequiredService<PlazosService>().ProcesarVencidasAsync(ct);
                if (n > 0) log.LogInformation("Plazos vencidos procesados: {N}", n);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Error al procesar plazos vencidos");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
