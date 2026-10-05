using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Pages.Ciclos;

public class CorreccionesModel(
    AppDbContext db, ContextoLayout ctx, CicloService ciclos, RevisionService revision, PlanillaService planillas, PlazosService plazos, IHostEnvironment env) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public int? Planilla { get; set; }
    public List<Planilla> Planillas { get; set; } = [];
    public Planilla? P { get; set; }
    public List<Observacion> Observaciones { get; set; } = [];
    public Devolucion? Devolucion { get; set; }
    public string? CicloSiguiente { get; set; }
    public List<Glosa> Glosas { get; set; } = [];
    public List<Job> Jobs { get; set; } = [];
    public bool EsDemo => env.IsDevelopment();
    public bool PuedeOperar => Puede(Roles.Operaciones);
    public bool PuedeRevisar => Puede(Roles.Finanzas);
    public bool EsAdmin => Puede(Roles.Admin);

    public async Task OnGetAsync()
    {
        await ctx.CargarAsync();
        if (ctx.Ciclo is null) return;
        Planillas = await db.Planillas.Include(p => p.Area).Where(p => p.CicloId == ctx.Ciclo.Id).OrderBy(p => p.Area.Nombre).ThenBy(p => p.Numero).ToListAsync();
        var conObs = await db.Observaciones.Where(o => o.LineaPago.Planilla.CicloId == ctx.Ciclo.Id).Select(o => o.LineaPago.PlanillaId).Distinct().ToListAsync();
        var id = Planilla ?? Planillas.FirstOrDefault(p => p.Estado == PlanillaEstado.Observada)?.Id ?? Planillas.FirstOrDefault(p => conObs.Contains(p.Id))?.Id ?? Planillas.FirstOrDefault()?.Id;
        if (id is null) return;
        P = await ciclos.PlanillaCompletaAsync(id.Value);
        if (P is null) return;
        Planilla = P.Id;
        Observaciones = await ciclos.ObservacionesAsync(P.Id);
        Devolucion = P.Devoluciones.OrderByDescending(d => d.Id).FirstOrDefault();
        CicloSiguiente = Formato.CodigoCiclo(P.Ciclo.Periodo.AddMonths(1));
        Glosas = await db.Glosas.OrderBy(g => g.Item).ThenBy(g => g.NombreGlosa).ToListAsync();
        Jobs = await db.Jobs.Where(j => j.Activo).OrderBy(j => j.JobBookNumber).ToListAsync();
    }

    private object Ruta => new { planilla = Planilla };

    public Task<IActionResult> OnPostCorregirAsync(int observacionId, string respuesta, string? cantidad, string? valorUnitario, string? jobBookNumber, int? glosaId) =>
        AccionAsync(() => revision.CorregirAsync(observacionId, respuesta, ExcelPlanilla.ParseNumero(cantidad), ExcelPlanilla.ParseNumero(valorUnitario), jobBookNumber, glosaId),
            "Observación corregida: queda para revisión de Finanzas.", Ruta, [Roles.Operaciones]);

    public Task<IActionResult> OnPostAceptarAsync(int observacionId) =>
        AccionAsync(() => revision.AceptarAsync(observacionId), "Corrección aceptada.", Ruta, [Roles.Finanzas]);

    public Task<IActionResult> OnPostReabrirAsync(int observacionId) =>
        AccionAsync(() => revision.ReabrirAsync(observacionId), "Corrección reabierta.", Ruta, [Roles.Finanzas]);

    public Task<IActionResult> OnPostReenviarAsync() =>
        AccionAsync(() => planillas.ReenviarAsync(Planilla ?? 0), "Planilla reenviada a Finanzas a tiempo.", Ruta, [Roles.Operaciones]);

    public Task<IActionResult> OnPostDiferirAsync(int lineaId) =>
        AccionAsync(() => planillas.DiferirLineaAsync(lineaId, "diferida por Operaciones durante la corrección"), "Filas diferidas al ciclo siguiente.", Ruta, [Roles.Operaciones]);

    public Task<IActionResult> OnPostRevertirDiferimientoAsync(int observacionId, string? motivo) =>
        AccionAsync(() => revision.RevertirDiferimientoAsync(observacionId, motivo ?? ""),
            "Diferimiento revertido: la fila volvió al ciclo y se reabrió la corrección con un nuevo plazo.", Ruta, [Roles.Admin]);

    /// <summary>Solo en Development: equivale al ajuste simularVencido del mockup (R-12).</summary>
    public async Task<IActionResult> OnPostSimularVencimientoAsync()
    {
        if (!EsDemo) return NotFound();
        var d = await db.Devoluciones.Where(x => x.PlanillaId == Planilla && x.Resultado == DevolucionResultado.Pendiente).FirstOrDefaultAsync();
        if (d is not null)
        {
            d.VenceEn = ciclos.AhoraUtc.AddSeconds(-1);
            await db.SaveChangesAsync();
            await plazos.ProcesarVencidasAsync();
            MensajeOk = "Plazo vencido (simulado): las filas con observaciones abiertas pasaron al ciclo siguiente.";
        }
        return RedirectToPage(Ruta);
    }
}
