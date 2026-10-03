using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Pages.Finanzas;

[Authorize(Roles = $"{Roles.Finanzas},{Roles.Admin}")]
public class RevisionModel(Parametros parametros, AppDbContext db, ContextoLayout ctx, CicloService ciclos, RevisionService revision, CuentasService cuentas, ExcelPlanilla excel, Almacenamiento almacen) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public int? Planilla { get; set; }
    [BindProperty(SupportsGet = true)] public int? Prestador { get; set; }
    public List<Planilla> Planillas { get; set; } = [];
    public Planilla? P { get; set; }
    public List<ChequeosPrestador> Chequeos { get; set; } = [];
    public List<Observacion> Observaciones { get; set; } = [];
    public Verificacion? Aprobacion { get; set; }
    public ChequeosPrestador? Sel { get; set; }
    public string PlazoCorreccion { get; set; } = "";
    /// <summary>XLSX enviado a Finanzas (última versión archivada) y archivo original subido por Operaciones.</summary>
    public PlanillaArchivo? Enviada { get; set; }
    public PlanillaArchivo? Original { get; set; }

    public async Task OnGetAsync()
    {
        await ctx.CargarAsync();
        PlazoCorreccion = Formato.Plazo((await parametros.ObtenerAsync()).PlazoCorreccionMinutos);
        if (ctx.Ciclo is null) return;
        Planillas = await db.Planillas.Include(p => p.Area).Where(p => p.CicloId == ctx.Ciclo.Id).OrderBy(p => p.Area.Nombre).ThenBy(p => p.Numero).ToListAsync();
        var id = Planilla ?? Planillas.FirstOrDefault(p => p.Estado == PlanillaEstado.EnRevision)?.Id ?? Planillas.FirstOrDefault(p => p.Estado == PlanillaEstado.Observada)?.Id ?? Planillas.FirstOrDefault()?.Id;
        if (id is null) return;
        P = await ciclos.PlanillaCompletaAsync(id.Value);
        if (P is null) return;
        Planilla = P.Id;
        Observaciones = await ciclos.ObservacionesAsync(P.Id);
        var docs = await db.PlanillaArchivos.AsNoTracking().Where(a => a.PlanillaId == P.Id).OrderByDescending(a => a.Version).ThenByDescending(a => a.Id).ToListAsync();
        Enviada = docs.FirstOrDefault(a => a.Tipo == "Planilla");
        Original = docs.FirstOrDefault(a => a.Tipo == "Original");
        Chequeos = P.Activas().GroupBy(l => l.PrestadorId).OrderBy(g => g.Min(l => l.Numero)).Select(g => Flujo.Revisar(P, g.Key)).ToList();
        Aprobacion = Flujo.PuedeAprobar(P, Observaciones);
        Sel = Chequeos.FirstOrDefault(c => c.PrestadorId == Prestador)
              ?? Chequeos.FirstOrDefault(c => c.Tributaria != Chequeo.Ok || c.Bancaria != Chequeo.Ok) ?? Chequeos.FirstOrDefault();
        Prestador = Sel?.PrestadorId;
    }

    public (Tono, string) EstadoDe(int prestadorId)
    {
        var lineas = P!.Activas().Where(l => l.PrestadorId == prestadorId).ToList();
        if (lineas.Any(l => l.Estado == LineaEstado.Observada)) return (Tono.Warning, "Observada");
        if (lineas.Any(l => l.Estado == LineaEstado.Corregida)) return (Tono.Info, "Corregida");
        var c = Chequeos.First(x => x.PrestadorId == prestadorId);
        if (c.Bancaria == Chequeo.Pendiente) return (Tono.Warning, "Cuenta por validar");
        var e = lineas[0].Estado;
        return (e is LineaEstado.Aprobada or LineaEstado.Pagada ? Tono.Success : e == LineaEstado.PendienteBoleta ? Tono.Neutral : Tono.Info, e.Nombre());
    }

    private const string TipoXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Descarga en Excel de la planilla enviada a Finanzas: el XLSX archivado al enviar (o reenviar) la versión. Con
    /// <paramref name="archivoId"/> descarga ese archivo (p. ej. el original subido); sin archivo archivado, la genera.
    /// </summary>
    public async Task<IActionResult> OnGetXlsxAsync(int? archivoId)
    {
        var id = Planilla ?? 0;
        var docs = db.PlanillaArchivos.AsNoTracking().Where(a => a.PlanillaId == id && a.Tipo != "Nomina");
        var doc = archivoId is null
            ? await docs.Where(a => a.Tipo == "Planilla").OrderByDescending(a => a.Version).ThenByDescending(a => a.Id).FirstOrDefaultAsync()
            : await docs.FirstOrDefaultAsync(a => a.Id == archivoId);
        if (doc is not null && almacen.Existe(doc.Ruta)) return File(almacen.Leer(doc.Ruta), TipoXlsx, doc.NombreArchivo);
        if (archivoId is not null) return NotFound();
        var p = await ciclos.PlanillaCompletaAsync(id);
        if (p is null) return NotFound();
        return File(await excel.ExportarAsync(p), TipoXlsx, Formato.NombreArchivoPlanilla(p.Ciclo.Periodo, p.Titulo, p.Version));
    }

    private object Ruta => new { planilla = Planilla, prestador = Prestador };

    public Task<IActionResult> OnPostObservarAsync(int lineaId, ObservacionTipo tipo, string campo, string detalle) =>
        AccionAsync(() => revision.ObservarAsync(lineaId, tipo, campo, detalle), "Observación agregada.", Ruta);

    public Task<IActionResult> OnPostDevolverAsync() =>
        AccionAsync(async () =>
        {
            var d = await revision.DevolverAsync(Planilla ?? 0);
            MensajeOk = $"Planilla devuelta: Operaciones tiene hasta las {Formato.Hora(d.VenceEn)} para corregir.";
        }, "Planilla devuelta.", Ruta);

    public Task<IActionResult> OnPostAprobarAsync() =>
        AccionAsync(() => revision.AprobarAsync(Planilla ?? 0), "Planilla aprobada: quedó congelada y las boletas confirmadas.", Ruta);

    public Task<IActionResult> OnPostValidarCuentaAsync(int cuentaId) =>
        AccionAsync(() => cuentas.ValidarAsync(cuentaId), "Cuenta validada.", Ruta);

    public Task<IActionResult> OnPostRechazarCuentaAsync(int cuentaId) =>
        AccionAsync(() => cuentas.RechazarAsync(cuentaId), "Cuenta rechazada.", Ruta);
}
