using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Pages;

public class IndexModel(AppDbContext db, ContextoLayout ctx, Parametros parametros) : PaginaBase
{
    public Ciclo? Ciclo { get; set; }
    public List<Planilla> Planillas { get; set; } = [];
    public Devolucion? Devolucion { get; set; }
    public Devolucion? Vencida { get; set; }
    public int Prestadores { get; set; }
    public decimal Cantidad { get; set; }
    public decimal Bruto { get; set; }
    public decimal Liquido { get; set; }
    public int BoletasRecibidas { get; set; }
    public int BoletasEsperadas { get; set; }
    public List<Auditoria> Bitacora { get; set; } = [];
    public List<(string Paso, string Estado, string Clase)> Pasos { get; set; } = [];

    public (int Recibidas, int Esperadas) BoletasDe(Planilla p)
    {
        var ids = p.Activas().Select(l => l.PrestadorId).Distinct().ToList();
        return (ids.Count(id => p.BoletaVigente(id) is not null), ids.Count);
    }

    public static (string Texto, string Url) Accion(Planilla p) => p.Estado switch
    {
        PlanillaEstado.ConAlertasCuenta => ("Validar cuentas", $"/Ciclos/ValidacionCuentas?planilla={p.Id}"),
        PlanillaEstado.Observada => ("Correcciones", $"/Ciclos/Correcciones?planilla={p.Id}"),
        PlanillaEstado.EnRevision => ("Revisión", $"/Finanzas/Revision?planilla={p.Id}"),
        PlanillaEstado.Aprobada or PlanillaEstado.EnPago => ("Pagos", $"/Finanzas/Pagos?planilla={p.Id}"),
        _ => ("Ver planilla", $"/Ciclos/Planilla?id={p.Id}")
    };

    public async Task OnGetAsync()
    {
        await ctx.CargarAsync();
        Ciclo = ctx.Ciclo;
        Devolucion = ctx.DevolucionActiva;
        Bitacora = await db.Auditorias.OrderByDescending(a => a.Id).Take(8).ToListAsync();
        if (Ciclo is null) return;
        Planillas = await db.Planillas.Where(p => p.CicloId == Ciclo.Id).Include(p => p.Area).Include(p => p.Lineas).Include(p => p.Boletas)
            .AsSplitQuery().OrderBy(p => p.Area.Nombre).ToListAsync();
        Vencida = await db.Devoluciones.Include(d => d.Planilla).ThenInclude(p => p.Area)
            .Where(d => d.Planilla.CicloId == Ciclo.Id && d.Resultado == DevolucionResultado.Vencida).OrderByDescending(d => d.Id).FirstOrDefaultAsync();
        var tasa = await parametros.TasaAsync(Ciclo.Periodo.Year);
        var activas = Planillas.SelectMany(p => p.Activas()).ToList();
        Prestadores = activas.Select(l => l.PrestadorId).Distinct().Count();
        Cantidad = activas.Sum(l => l.Cantidad);
        Bruto = activas.Sum(l => l.ValorTotalBruto);
        Liquido = Planillas.Sum(p => p.Activas().GroupBy(l => l.PrestadorId).Sum(g => Montos.PorBoleta(g.Select(l => l.ValorTotalBruto), tasa).Liquido));
        foreach (var p in Planillas)
        {
            var (r, e) = BoletasDe(p);
            BoletasRecibidas += r;
            BoletasEsperadas += e;
        }

        var hay = Planillas.Count > 0;
        bool Todas(params PlanillaEstado[] e) => hay && Planillas.All(p => e.Contains(p.Estado));
        bool Alguna(params PlanillaEstado[] e) => Planillas.Any(p => e.Contains(p.Estado));
        var conAlertas = Planillas.Count(p => p.Estado == PlanillaEstado.ConAlertasCuenta);
        var postRevision = new[] { PlanillaEstado.Aprobada, PlanillaEstado.EnPago, PlanillaEstado.Cerrada };
        Pasos =
        [
            ("1 · Producción", hay ? "Completado" : "Pendiente", hay ? "ok" : ""),
            ("2 · Cuentas", conAlertas > 0 ? $"{conAlertas} planilla{(conAlertas > 1 ? "s" : "")} con alertas" : hay ? "Sin alertas" : "Pendiente", hay && conAlertas == 0 ? "ok" : hay ? "ok" : ""),
            ("3 · Boletas", BoletasEsperadas > 0 && BoletasRecibidas == BoletasEsperadas ? "Completo" : $"En curso · {BoletasRecibidas}/{BoletasEsperadas}",
                BoletasEsperadas > 0 && BoletasRecibidas == BoletasEsperadas ? "ok" : hay ? "parcial" : ""),
            ("4 · Revisión Finanzas", Todas(postRevision) ? "Completado" : Alguna(PlanillaEstado.EnRevision, PlanillaEstado.Observada) ? "Paso activo" : "Pendiente",
                Todas(postRevision) ? "ok" : Alguna(PlanillaEstado.EnRevision, PlanillaEstado.Observada) ? "activo" : ""),
            ("5 · Transferencias", Todas(PlanillaEstado.Cerrada) ? "Completado" : Alguna(PlanillaEstado.Aprobada, PlanillaEstado.EnPago) ? "En curso" : "Pendiente",
                Todas(PlanillaEstado.Cerrada) ? "ok" : Alguna(PlanillaEstado.Aprobada, PlanillaEstado.EnPago) ? "activo" : ""),
            ("6 · Cierre", Ciclo.Estado == CicloEstado.Cerrado ? "Cerrado" : "Pendiente", Ciclo.Estado == CicloEstado.Cerrado ? "ok" : "")
        ];
    }
}
