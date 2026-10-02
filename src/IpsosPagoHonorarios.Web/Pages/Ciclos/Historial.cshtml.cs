using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Pages.Ciclos;

public class HistorialModel(AppDbContext db, Almacenamiento archivos) : PaginaBase
{
    public sealed record Fila(Ciclo Ciclo, int Planillas, int Prestadores, decimal Monto, DateOnly? UltimoPago);
    public List<Fila> Filas { get; set; } = [];

    public async Task OnGetAsync()
    {
        var ciclos = await db.Ciclos.Include(c => c.Planillas).ThenInclude(p => p.Lineas).AsSplitQuery().OrderByDescending(c => c.Periodo).ToListAsync();
        var pagos = await db.Transferencias.Select(t => new { t.Planilla.CicloId, t.Fecha }).ToListAsync();
        Filas = ciclos.Select(c =>
        {
            var lineas = c.Planillas.SelectMany(p => p.Lineas).Where(l => l.Estado != LineaEstado.Diferida).ToList();
            return new Fila(c, c.Planillas.Count, lineas.Select(l => l.PrestadorId).Distinct().Count(), lineas.Sum(l => l.ValorTotalBruto),
                pagos.Where(p => p.CicloId == c.Id).Select(p => (DateOnly?)p.Fecha).Max());
        }).ToList();
    }

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
