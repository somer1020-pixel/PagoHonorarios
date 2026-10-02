using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Services;

public enum TipoArchivoProduccion { Exportacion, Finanzas }

public sealed record SolicitudImportacion(
    int CicloId, int AreaId, string TipoGasto, string Responsable, string ResponsableEmail,
    TipoArchivoProduccion TipoArchivo, string NombreArchivo, byte[] Contenido);

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

        var planilla = await db.Planillas.Include(p => p.Lineas).FirstOrDefaultAsync(p => p.CicloId == ciclo!.Id && p.AreaId == area!.Id);
        if (planilla is not null && planilla.Estado is not (PlanillaEstado.Borrador or PlanillaEstado.ConAlertasCuenta))
            errores.Add(new(0, "Planilla", $"La planilla {area!.Nombre} de {ciclo!.Codigo} ya fue enviada a Finanzas ({planilla.Estado.Nombre()})."));

        if (errores.Count > 0)
        {
            auditor.Registrar("Produccion", s.NombreArchivo, "Importación rechazada", $"{area!.Nombre}: {errores.Count} errores");
            await db.SaveChangesAsync();
            return new(false, errores, avisos, null);
        }

        await using var tx = await db.Database.BeginTransactionAsync();
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
            planilla = new Planilla { CicloId = ciclo!.Id, AreaId = area!.Id };
            db.Planillas.Add(planilla);
        }
        else
        {
            // Nueva versión corregida: se reemplazan las filas (se conservan las diferidas desde el ciclo anterior).
            planilla.Version += 1;
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
            $"{area!.Nombre} v{planilla.Version}: {filas.Count} líneas, {Formato.Clp(total)} ({s.NombreArchivo})");

        // R-21: aviso al generarse su pago.
        var tasa = await parametros.TasaAsync(ciclo.Periodo.Year);
        foreach (var g in completa.Activas().GroupBy(l => l.Prestador))
        {
            var (bruto, ret, liq) = Montos.PorBoleta(g.Select(l => l.ValorTotalBruto), tasa);
            correos.Encolar(g.Key.Email, $"Tu pago de {ciclo.Codigo} está listo para boletear",
                $"Hola {g.Key.NombreCompleto}: emite una sola boleta por {Formato.Clp(bruto)} (retención {Formato.Clp(ret)}, recibirás {Formato.Clp(liq)}) y súbela en el portal.");
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new(true, [], avisos, completa);
    }
}
