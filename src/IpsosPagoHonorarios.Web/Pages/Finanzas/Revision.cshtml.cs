using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Pages.Finanzas;

[Authorize(Roles = $"{Roles.Finanzas},{Roles.Admin}")]
public class RevisionModel(AppDbContext db, ContextoLayout ctx, CicloService ciclos, RevisionService revision, CuentasService cuentas) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public int? Planilla { get; set; }
    [BindProperty(SupportsGet = true)] public int? Prestador { get; set; }
    public List<Planilla> Planillas { get; set; } = [];
    public Planilla? P { get; set; }
    public List<ChequeosPrestador> Chequeos { get; set; } = [];
    public List<Observacion> Observaciones { get; set; } = [];
    public Verificacion? Aprobacion { get; set; }
    public ChequeosPrestador? Sel { get; set; }

    public async Task OnGetAsync()
    {
        await ctx.CargarAsync();
        if (ctx.Ciclo is null) return;
        Planillas = await db.Planillas.Include(p => p.Area).Where(p => p.CicloId == ctx.Ciclo.Id).OrderBy(p => p.Area.Nombre).ToListAsync();
        var id = Planilla ?? Planillas.FirstOrDefault(p => p.Estado == PlanillaEstado.EnRevision)?.Id ?? Planillas.FirstOrDefault(p => p.Estado == PlanillaEstado.Observada)?.Id ?? Planillas.FirstOrDefault()?.Id;
        if (id is null) return;
        P = await ciclos.PlanillaCompletaAsync(id.Value);
        if (P is null) return;
        Planilla = P.Id;
        Observaciones = await ciclos.ObservacionesAsync(P.Id);
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

    private object Ruta => new { planilla = Planilla, prestador = Prestador };

    public Task<IActionResult> OnPostObservarAsync(int lineaId, ObservacionTipo tipo, string campo, string detalle) =>
        AccionAsync(() => revision.ObservarAsync(lineaId, tipo, campo, detalle), "Observación agregada.", Ruta);

    public Task<IActionResult> OnPostDevolverAsync() =>
        AccionAsync(() => revision.DevolverAsync(Planilla ?? 0), "Planilla devuelta: Operaciones tiene 1 hora para corregir.", Ruta);

    public Task<IActionResult> OnPostAprobarAsync() =>
        AccionAsync(() => revision.AprobarAsync(Planilla ?? 0), "Planilla aprobada: quedó congelada y las boletas confirmadas.", Ruta);

    public Task<IActionResult> OnPostValidarCuentaAsync(int cuentaId) =>
        AccionAsync(() => cuentas.ValidarAsync(cuentaId), "Cuenta validada.", Ruta);

    public Task<IActionResult> OnPostRechazarCuentaAsync(int cuentaId) =>
        AccionAsync(() => cuentas.RechazarAsync(cuentaId), "Cuenta rechazada.", Ruta);
}
