using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Services;

public enum TipoArchivoProduccion { Exportacion, Finanzas }

public sealed record SolicitudImportacion(
    int CicloId, int AreaId, string TipoGasto, string Responsable, string ResponsableEmail,
    TipoArchivoProduccion TipoArchivo, string NombreArchivo, byte[] Contenido,
    // Destino: PlanillaId = reemplazar esa planilla (nueva versión); NuevaPlanilla = crear otra planilla del área en el ciclo.
    // Sin ninguno: si el área no tiene planilla en el ciclo se crea; si tiene una sola se reemplaza; si tiene varias hay que elegir.
    int? PlanillaId = null, bool NuevaPlanilla = false, string? NombrePlanilla = null);

public sealed record ResultadoImportacion(bool Cargada, List<ErrorFila> Errores, List<string> Avisos, Planilla? Planilla);

/// <summary>Carga de producción y generación de la planilla por área (R-02, R-03, R-08, R-21).</summary>
public class ProduccionService(
    AppDbContext db, CicloService ciclos, Parametros parametros, CuentasService cuentas, BoletaService boletas,
    Almacenamiento archivos, Auditor auditor, Correos correos)
{
    public async Task<ResultadoImportacion> ImportarAsync(SolicitudImportacion s)
    {
        var errores = new List<ErrorFila>();
        if (string.IsNullOrWhiteSpace(s.Responsable)) errores.Add(new(0, "Responsable", "El responsable es obligatorio."));
        // TODO(diseño): obligatoriedad y forma del correo del responsable. Se exige un correo con formato válido.
        if (string.IsNullOrWhiteSpace(s.ResponsableEmail) || !System.Net.Mail.MailAddress.TryCreate(s.ResponsableEmail, out _))
            errores.Add(new(0, "Correo del responsable", "Ingresa un correo válido."));
        var area = await db.Areas.FirstOrDefaultAsync(a => a.Id == s.AreaId);
        if (area is null) errores.Add(new(0, "Área", "El área responsable es obligatoria."));
        else if (db.AreasVisibles is { } visibles && !visibles.Contains(area.Id))
            errores.Add(new(0, "Área", $"No tienes asignada el área {area.Nombre}. Pide al Administrador que te la asigne."));
        var tiposGasto = await db.TiposGasto.Select(t => t.Nombre).ToListAsync();
        if (!tiposGasto.Contains(s.TipoGasto)) errores.Add(new(0, "Tipo de gasto", "El tipo de gasto es obligatorio."));
        var ext = Path.GetExtension(s.NombreArchivo).ToLowerInvariant();
        if (ext is not (".xlsx" or ".csv"))
            errores.Add(new(0, "Archivo", "El archivo debe ser XLSX (o CSV para la exportación del sistema)."));
        var ciclo = await db.Ciclos.FirstOrDefaultAsync(c => c.Id == s.CicloId);
        if (ciclo is null) errores.Add(new(0, "Ciclo", "Ciclo no encontrado."));
        if (errores.Count > 0) return new(false, errores, [], null);
        CicloService.ExigirAbierto(ciclo!);

        ArchivoLeido leido;
        try
        {
            using var ms = new MemoryStream(s.Contenido);
            // El formato se detecta por el contenido: una planilla de Finanzas (títulos en la fila 8) se lee como tal
            // aunque se haya elegido "Exportación del sistema", y viceversa.
            var esFinanzas = ext == ".xlsx" && ExcelPlanilla.EsFormatoFinanzas(ms);
            ms.Position = 0;
            leido = esFinanzas ? ExcelPlanilla.LeerFormatoFinanzas(ms) : ExcelPlanilla.LeerExportacion(ms, s.NombreArchivo);
        }
        catch (Exception)
        {
            return new(false, [new(0, "Archivo", "No se pudo leer el archivo.")], [], null);
        }

        var glosas = await db.Glosas.ToListAsync();
        var jobs = await db.Jobs.ToListAsync();
        var prestadores = await db.Prestadores.Include(p => p.Cuentas).ToListAsync();
        // R-03: el valor unitario se sugiere desde el Job cuando no viene.
        var filas = leido.Filas.Select(f => f.ValorUnitario is null && jobs.FirstOrDefault(j => j.JobBookNumber == f.Job?.Trim())?.ValorUnitarioSugerido is { } vu
            ? f with { ValorUnitario = vu } : f).ToList();
        var validacion = ProduccionReglas.Validar(filas, glosas.Select(g => g.NombreGlosa).ToList(),
            rut => prestadores.Any(p => p.Rut == rut), job => jobs.Any(j => j.JobBookNumber == job));
        errores.AddRange(validacion.Errores);
        foreach (var f in filas.Where(f => f.TipoGasto is not null && !tiposGasto.Contains(f.TipoGasto)))
            errores.Add(new(f.Fila, "Tipo de Gasto", $"«{f.TipoGasto}» no está en el catálogo"));

        var avisos = new List<string>();
        var par = await parametros.ObtenerAsync();
        if (ProduccionReglas.AvisoVentana(ciclos.Hoy, par.DiaDescargaDesde, par.DiaDescargaHasta) is { } aviso) avisos.Add(aviso);
        avisos.AddRange(validacion.Avisos);
        avisos.AddRange(ProduccionReglas.AvisosTotales(filas));
        if (leido.Encabezado?.MontoTotal is { } b3 && filas.All(f => f.ValorUnitario is not null && f.Cantidad is not null))
        {
            var suma = filas.Sum(ProduccionReglas.TotalDe);
            if (Montos.RedondearExcel(b3) != suma)
                avisos.Add($"El monto total del archivo (B3 = {Formato.Clp(b3)}) difiere de la suma de la columna H ({Formato.Clp(suma)}).");
        }
        if (leido.Encabezado?.Area is { Length: > 0 } areaArchivo && !areaArchivo.Equals(area!.Nombre, StringComparison.OrdinalIgnoreCase))
            avisos.Add($"El archivo indica el área «{areaArchivo}» (B4), pero se cargó en {area.Nombre}.");

        // Un área puede tener varias planillas en el ciclo: se reemplaza la indicada o se crea una nueva.
        var delArea = await db.Planillas.IgnoreQueryFilters().Include(p => p.Area).Where(p => p.CicloId == ciclo!.Id && p.AreaId == area!.Id).ToListAsync();
        var nombrePlanilla = string.IsNullOrWhiteSpace(s.NombrePlanilla) ? null : s.NombrePlanilla.Trim();
        if (nombrePlanilla is { Length: > 100 }) errores.Add(new(0, "Nombre de la planilla", "Máximo 100 caracteres."));
        Planilla? planilla = null;
        if (s.PlanillaId is { } pid)
        {
            planilla = delArea.FirstOrDefault(p => p.Id == pid);
            if (planilla is null) errores.Add(new(0, "Planilla", $"La planilla a reemplazar no es del área {area!.Nombre} en {ciclo!.Codigo}."));
        }
        else if (!s.NuevaPlanilla && delArea.Count == 1) planilla = delArea[0];
        else if (!s.NuevaPlanilla && delArea.Count > 1)
            errores.Add(new(0, "Planilla", $"El área {area!.Nombre} tiene {delArea.Count} planillas en {ciclo!.Codigo}: indica cuál reemplazar o si es una planilla nueva."));
        if (planilla is null && nombrePlanilla is not null && delArea.Any(p => string.Equals(p.Nombre, nombrePlanilla, StringComparison.OrdinalIgnoreCase)))
            errores.Add(new(0, "Nombre de la planilla", $"Ya existe la planilla «{area!.Nombre} · {nombrePlanilla}» en {ciclo!.Codigo}: elige reemplazarla o usa otro nombre."));
        if (planilla is not null && planilla.Estado is not (PlanillaEstado.Borrador or PlanillaEstado.ConAlertasCuenta))
            errores.Add(new(0, "Planilla", $"La planilla {planilla.Titulo} de {ciclo!.Codigo} ya fue enviada a Finanzas ({planilla.Estado.Nombre()})."));
        if (planilla is not null) await db.Entry(planilla).Collection(p => p.Lineas).LoadAsync();

        // Posible duplicado: filas idénticas (RUT, Job, glosa, cantidad y valor) en otra planilla del área en el ciclo.
        if (errores.Count == 0 && delArea.Any(p => p.Id != planilla?.Id))
        {
            var otras = delArea.Where(p => p.Id != planilla?.Id).Select(p => p.Id).ToList();
            var existentes = await db.LineasPago.IgnoreQueryFilters().Where(l => otras.Contains(l.PlanillaId) && l.Estado != LineaEstado.Diferida)
                .Select(l => new { l.PlanillaId, l.Prestador.Rut, l.Job.JobBookNumber, l.Glosa.NombreGlosa, l.Cantidad, l.ValorUnitarioBruto }).ToListAsync();
            foreach (var g in existentes.GroupBy(x => x.PlanillaId))
            {
                var claves = g.Select(x => (x.Rut, x.JobBookNumber.Trim(), x.NombreGlosa, x.Cantidad, x.ValorUnitarioBruto)).ToHashSet();
                var repetidas = filas.Count(f => RutHelper.TryParse(f.Rut, out var r, out _) && f.Job is not null && f.Glosa is not null &&
                                                 claves.Contains((r, f.Job.Trim(), f.Glosa.Trim(), f.Cantidad ?? 0, f.ValorUnitario ?? 0)));
                if (repetidas > 0)
                    avisos.Add($"Posible duplicado: {repetidas} de {filas.Count} filas son idénticas a filas de la planilla {delArea.First(p => p.Id == g.Key).Titulo}. Revisa que no se pague dos veces.");
            }
        }

        if (errores.Count > 0)
        {
            auditor.Registrar("Produccion", s.NombreArchivo, "Importación rechazada", $"{area!.Nombre}: {errores.Count} errores");
            await db.SaveChangesAsync();
            return new(false, errores, avisos, null);
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        try { return await GuardarImportacionAsync(s, tx, planilla, area!, ciclo!, filas, prestadores, jobs, glosas, leido, avisos, nombrePlanilla, delArea); }
        catch (Exception ex) when (CicloService.EsConflicto(ex)) { throw new ReglaException(CicloService.MensajeConflicto); }
    }

    private async Task<ResultadoImportacion> GuardarImportacionAsync(SolicitudImportacion s, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx,
        Planilla? planilla, Area area, Ciclo ciclo, List<FilaProduccion> filas, List<Prestador> prestadores, List<Job> jobs, List<Glosa> glosas,
        ArchivoLeido leido, List<string> avisos, string? nombrePlanilla, List<Planilla> delArea)
    {
        // Reemplazo: primero se toma la planilla (las boletas que suben sus prestadores esperan su turno) y se confirma su estado.
        if (planilla is not null)
        {
            await ciclos.BloquearPlanillaAsync(planilla.Id);
            var estado = await db.Planillas.IgnoreQueryFilters().Where(x => x.Id == planilla.Id).Select(x => x.Estado).SingleAsync();
            if (estado is not (PlanillaEstado.Borrador or PlanillaEstado.ConAlertasCuenta))
                throw new ReglaException($"La planilla {planilla.Titulo} de {ciclo.Codigo} ya fue enviada a Finanzas ({estado.Nombre()}).");
        }
        // Prestadores y Jobs nuevos (R-02: se crean y se avisa).
        foreach (var f in filas)
        {
            RutHelper.TryParse(f.Rut, out var rut, out var dv);
            if (prestadores.All(p => p.Rut != rut))
            {
                var nuevo = new Prestador { Rut = rut, Dv = dv, NombreCompleto = f.Nombre?.Trim() ?? "(sin nombre)" };
                db.Prestadores.Add(nuevo);
                prestadores.Add(nuevo);
                auditor.Registrar(nameof(Prestador), nuevo.RutPlanilla, "Crear prestador (producción)", nuevo.NombreCompleto);
            }
            var jobNum = f.Job!.Trim();
            if (jobs.All(j => j.JobBookNumber != jobNum))
            {
                var nj = new Job { JobBookNumber = jobNum, Nombre = f.NombreJob?.Trim() ?? "(sin nombre)", AreaSugeridaId = area!.Id, ValorUnitarioSugerido = f.ValorUnitario };
                db.Jobs.Add(nj);
                jobs.Add(nj);
                auditor.Registrar(nameof(Job), jobNum, "Crear Job (producción)", nj.Nombre);
            }
        }
        if (planilla is null)
        {
            planilla = new Planilla
            {
                CicloId = ciclo!.Id, AreaId = area!.Id, Area = area, Nombre = nombrePlanilla,
                Numero = delArea.Count == 0 ? 1 : delArea.Max(p => p.Numero) + 1
            };
            db.Planillas.Add(planilla);
        }
        else
        {
            // Nueva versión corregida: se reemplazan las filas (se conservan las diferidas desde el ciclo anterior).
            planilla.Version += 1;
            if (nombrePlanilla is not null) planilla.Nombre = nombrePlanilla;
            var reemplazadas = planilla.Lineas.Where(l => l.DiferidaDesdeCicloId is null).ToList();
            var ids = reemplazadas.Select(l => l.Id).ToList();
            if (await db.Observaciones.AnyAsync(o => ids.Contains(o.LineaPagoId)))
                throw new ReglaException("La planilla tiene observaciones: no se puede reemplazar.");
            db.LineasPago.RemoveRange(reemplazadas);
            foreach (var l in reemplazadas) planilla.Lineas.Remove(l);
        }
        planilla.FechaRecepcion = leido.Encabezado?.FechaRecepcion ?? ciclos.Hoy;
        planilla.ResponsableNombre = s.Responsable.Trim();
        planilla.ResponsableEmail = s.ResponsableEmail.Trim();
        planilla.Estado = PlanillaEstado.Borrador;
        await db.SaveChangesAsync();

        var numero = planilla.Lineas.Count == 0 ? 0 : planilla.Lineas.Max(l => l.Numero);
        foreach (var f in filas)
        {
            RutHelper.TryParse(f.Rut, out var rut, out _);
            var prest = prestadores.First(p => p.Rut == rut);
            var job = jobs.First(j => j.JobBookNumber == f.Job!.Trim());
            var glosa = glosas.First(g => g.NombreGlosa == f.Glosa!.Trim());
            planilla.Lineas.Add(new LineaPago
            {
                Numero = ++numero, TipoGasto = f.TipoGasto ?? s.TipoGasto, Job = job, JobId = job.Id, Glosa = glosa, GlosaId = glosa.Id,
                ValorUnitarioBruto = f.ValorUnitario!.Value, Cantidad = f.Cantidad!.Value,
                ValorTotalBruto = ProduccionReglas.TotalDe(f),
                Prestador = prest, PrestadorId = prest.Id, NombrePlanilla = f.Nombre?.Trim() ?? prest.NombreCompleto,
                NumeroBoleta = string.IsNullOrWhiteSpace(f.NumeroBoleta) ? null : f.NumeroBoleta.Trim(),
                CuentaPlanillaTipo = f.TipoCuenta?.Trim(), CuentaPlanillaNumero = f.Cuenta?.Trim(), CuentaPlanillaBanco = f.Banco?.Trim(),
                Estado = LineaEstado.PendienteBoleta
            });
        }
        await db.SaveChangesAsync();

        var completa = (await ciclos.PlanillaCompletaAsync(planilla.Id))!;
        // R-08: validación preliminar al generar (R-24 sobre cuentas; R-06 con boletas ya recibidas).
        await cuentas.ValidarPlanillaAsync(completa);
        foreach (var b in completa.Boletas.Where(b => b.Vigente).ToList()) await boletas.ConciliarAsync(completa, b);

        var ruta = archivos.Guardar($"planillas/{ciclo!.Codigo}", s.NombreArchivo, s.Contenido);
        db.PlanillaArchivos.Add(new PlanillaArchivo { PlanillaId = planilla.Id, Version = planilla.Version, NombreArchivo = s.NombreArchivo, Ruta = ruta, Tipo = "Original" });
        var total = completa.Activas().Sum(l => l.ValorTotalBruto);
        auditor.Registrar(nameof(Planilla), planilla.Id, "Importar producción",
            $"{planilla.Titulo} v{planilla.Version}: {filas.Count} líneas, {Formato.Clp(total)} ({s.NombreArchivo})");

        // R-21: aviso al generarse su pago.
        var tasa = await parametros.TasaAsync(ciclo.Periodo.Year);
        var limiteBoleta = Conciliacion.FechaLimite(ciclo.Periodo, (await parametros.ObtenerAsync()).DiaLimiteBoleta);
        foreach (var g in completa.Activas().GroupBy(l => l.Prestador))
        {
            var (bruto, ret, liq) = Montos.PorBoleta(g.Select(l => l.ValorTotalBruto), tasa);
            // Con celular válido el aviso va por WhatsApp; si no, por correo.
            if (!await correos.EncolarSolicitudBoletaAsync(g.Key, ciclo.Codigo, bruto, limiteBoleta))
                correos.Encolar(g.Key.Email, $"Tu pago de {ciclo.Codigo} está listo para boletear",
                    $"Hola {g.Key.NombreCompleto}: emite una sola boleta por {Formato.Clp(bruto)} (retención {Formato.Clp(ret)}, recibirás {Formato.Clp(liq)}) y súbela en el portal.");
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new(true, [], avisos, completa);
    }
}
