using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Pages.Ciclos;

public class HistorialModel(AppDbContext db, Almacenamiento archivos, PagoService pagos) : PaginaBase
{
    public sealed record Fila(Ciclo Ciclo, int Planillas, int Prestadores, decimal Monto, DateOnly? UltimoPago);
    public List<Fila> Filas { get; set; } = [];
    /// <summary>Reaperturas por código de ciclo (bitácora), la más reciente primero.</summary>
    public ILookup<string, Auditoria> Reaperturas { get; set; } = Enumerable.Empty<Auditoria>().ToLookup(a => "");
    public bool EsAdmin => User.IsInRole(Roles.Admin);

    public async Task OnGetAsync()
    {
        var ciclos = await db.Ciclos.Include(c => c.Planillas).ThenInclude(p => p.Lineas).AsSplitQuery().OrderByDescending(c => c.Periodo).ToListAsync();
        Reaperturas = (await db.Auditorias.Where(a => a.Entidad == nameof(Ciclo) && a.Accion == "Reabrir ciclo").OrderByDescending(a => a.Id).ToListAsync())
            .ToLookup(a => a.EntidadId ?? "");
        var pagos = await db.Transferencias.Select(t => new { t.Planilla.CicloId, t.Fecha }).ToListAsync();
        Filas = ciclos.Select(c =>
        {
            var lineas = c.Planillas.SelectMany(p => p.Lineas).Where(l => l.Estado != LineaEstado.Diferida).ToList();
            return new Fila(c, c.Planillas.Count, lineas.Select(l => l.PrestadorId).Distinct().Count(), lineas.Sum(l => l.ValorTotalBruto),
                pagos.Where(p => p.CicloId == c.Id).Select(p => (DateOnly?)p.Fecha).Max());
        }).ToList();
    }

    public Task<IActionResult> OnPostReabrirAsync(int cicloId, string? motivo) =>
        AccionAsync(() => pagos.ReabrirCicloAsync(cicloId, motivo), "Ciclo reabierto. Quedó registrado en la bitácora con el motivo.", null, [Roles.Admin]);

    /// <summary>El ZIP de cierre contiene todas las áreas: solo para quien ve todas (Finanzas, Administrador).</summary>
    public bool PuedeZip => Alcance.VeTodas(User);

    public async Task<IActionResult> OnGetZipAsync(int cicloId)
    {
        if (!PuedeZip) return Forbid();
        var c = await db.Ciclos.FirstOrDefaultAsync(x => x.Id == cicloId);
        if (c?.RutaZip is null || !archivos.Existe(c.RutaZip)) return NotFound();
        return File(archivos.Leer(c.RutaZip), "application/zip", $"Respaldos {c.Codigo}.zip");
    }
}
