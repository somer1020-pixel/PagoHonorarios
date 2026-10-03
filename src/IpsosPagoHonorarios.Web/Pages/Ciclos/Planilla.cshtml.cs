using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Pages.Ciclos;

public class PlanillaModel(AppDbContext db, ContextoLayout ctx, CicloService ciclos, PlanillaService planillas, ExcelPlanilla excel) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    public List<Planilla> Planillas { get; set; } = [];
    public Planilla? P { get; set; }
    public Verificacion? Envio { get; set; }
    public Devolucion? Devolucion { get; set; }
    public int ObservacionesAbiertas { get; set; }
    public bool PuedeOperar => Puede(Roles.Operaciones);

    public async Task OnGetAsync()
    {
        await ctx.CargarAsync();
        if (ctx.Ciclo is null) return;
        Planillas = await db.Planillas.Include(p => p.Area).Where(p => p.CicloId == ctx.Ciclo.Id).OrderBy(p => p.Area.Nombre).ThenBy(p => p.Numero).ToListAsync();
        var id = Id ?? Planillas.FirstOrDefault()?.Id;
        if (id is null) return;
        P = await ciclos.PlanillaCompletaAsync(id.Value);
        if (P is null) return;
        Id = P.Id;
        Envio = Flujo.PuedeEnviar(P);
        Devolucion = await ciclos.DevolucionActivaAsync(P.Id);
        ObservacionesAbiertas = (await ciclos.ObservacionesAsync(P.Id)).Count(o => o.Estado == ObservacionEstado.Abierta);
    }

    /// <summary>Descarga en formato Finanzas (R-27: O, P, Q con la cuenta registrada).</summary>
    public async Task<IActionResult> OnGetXlsxAsync()
    {
        var p = await ciclos.PlanillaCompletaAsync(Id ?? 0);
        if (p is null) return NotFound();
        var bytes = await excel.ExportarAsync(p);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Formato.NombreArchivoPlanilla(p.Ciclo.Periodo, p.Titulo, p.Version));
    }

    public Task<IActionResult> OnPostEnviarAsync() =>
        AccionAsync(() => planillas.EnviarAsync(Id ?? 0), "Planilla enviada a Finanzas.", new { id = Id }, [Roles.Operaciones]);

    public Task<IActionResult> OnPostReenviarAsync() =>
        AccionAsync(() => planillas.ReenviarAsync(Id ?? 0), "Planilla reenviada a Finanzas a tiempo (versión nueva archivada).", new { id = Id }, [Roles.Operaciones]);

    public Task<IActionResult> OnPostDiferirAsync(int lineaId, string? motivo) =>
        AccionAsync(() => planillas.DiferirLineaAsync(lineaId, string.IsNullOrWhiteSpace(motivo) ? "diferida por Operaciones" : motivo),
            "Filas diferidas al ciclo siguiente.", new { id = Id }, [Roles.Operaciones]);
}
