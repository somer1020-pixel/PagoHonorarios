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
        // Totales agregados en la base: antes se cargaban todas las líneas de todos los ciclos (decenas de miles de filas).
        var ciclos = await db.Ciclos.AsNoTracking().OrderByDescending(c => c.Periodo).ToListAsync();
        Reaperturas = (await db.Auditorias.Where(a => a.Entidad == nameof(Ciclo) && a.Accion == "Reabrir ciclo").OrderByDescending(a => a.Id).ToListAsync())
            .ToLookup(a => a.EntidadId ?? "");
        var planillas = await db.Planillas.GroupBy(p => p.CicloId).Select(g => new { CicloId = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.CicloId, x => x.N);
        var lineas = await db.LineasPago.Where(l => l.Estado != LineaEstado.Diferida).GroupBy(l => l.Planilla.CicloId)
            .Select(g => new { CicloId = g.Key, Prestadores = g.Select(l => l.PrestadorId).Distinct().Count(), Monto = g.Sum(l => l.ValorTotalBruto) })
            .ToDictionaryAsync(x => x.CicloId);
        var pagos = await db.Transferencias.GroupBy(t => t.Planilla.CicloId).Select(g => new { CicloId = g.Key, Ultimo = g.Max(t => t.Fecha) })
            .ToDictionaryAsync(x => x.CicloId, x => (DateOnly?)x.Ultimo);
        Filas = ciclos.Select(c => new Fila(c, planillas.GetValueOrDefault(c.Id), lineas.GetValueOrDefault(c.Id)?.Prestadores ?? 0,
            lineas.GetValueOrDefault(c.Id)?.Monto ?? 0, pagos.GetValueOrDefault(c.Id))).ToList();
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
