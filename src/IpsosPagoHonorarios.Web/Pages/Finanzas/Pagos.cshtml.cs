using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Pages.Finanzas;

[Authorize(Roles = $"{Roles.Finanzas},{Roles.Admin}")]
public class PagosModel(AppDbContext db, ContextoLayout ctx, CicloService ciclos, PagoService pagos, Parametros parametros, Almacenamiento archivos) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public int? Planilla { get; set; }
    public List<Planilla> Planillas { get; set; } = [];
    public Planilla? P { get; set; }
    public List<PagoPrestador> Resumen { get; set; } = [];
    public List<LineaPago> Diferidas { get; set; } = [];
    public Verificacion? Cierre { get; set; }
    public Ciclo? Ciclo { get; set; }
    public decimal Tasa { get; set; }
    public DateOnly Hoy { get; set; }

    public async Task OnGetAsync()
    {
        await ctx.CargarAsync();
        Ciclo = ctx.Ciclo;
        if (Ciclo is null) return;
        Hoy = ciclos.Hoy;
        Tasa = await parametros.TasaAsync(Ciclo.Periodo.Year);
        Planillas = await db.Planillas.Include(p => p.Area).Where(p => p.CicloId == Ciclo.Id).OrderBy(p => p.Area.Nombre).ToListAsync();
        var cicloCompleto = await db.Ciclos.Include(c => c.Planillas).ThenInclude(p => p.Lineas).FirstAsync(c => c.Id == Ciclo.Id);
        Cierre = Flujo.PuedeCerrar(cicloCompleto);
        Diferidas = await db.LineasPago.Include(l => l.Prestador).Include(l => l.Planilla).ThenInclude(p => p.Area)
            .Where(l => l.Planilla.CicloId == Ciclo.Id && l.Estado == LineaEstado.Diferida).OrderBy(l => l.PlanillaId).ThenBy(l => l.Numero).ToListAsync();
        var id = Planilla ?? Planillas.FirstOrDefault(p => p.Estado is PlanillaEstado.Aprobada or PlanillaEstado.EnPago)?.Id ?? Planillas.FirstOrDefault()?.Id;
        if (id is null) return;
        P = await ciclos.PlanillaCompletaAsync(id.Value);
        if (P is null) return;
        Planilla = P.Id;
        Resumen = await pagos.ResumenAsync(P);
    }

    private object Ruta => new { planilla = Planilla };

    public async Task<IActionResult> OnPostNominaAsync()
    {
        try
        {
            var (bytes, nombre) = await pagos.NominaAsync(Planilla ?? 0);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombre);
        }
        catch (ReglaException ex)
        {
            MensajeError = ex.Message;
            return RedirectToPage(Ruta);
        }
    }

    public async Task<IActionResult> OnPostRegistrarAsync(int prestadorId, DateOnly? fecha, string numeroOperacion, IFormFile? comprobante)
    {
        try
        {
            if (fecha is null) throw new ReglaException("La fecha es obligatoria.");
            if (comprobante is null || comprobante.Length == 0) throw new ReglaException("Adjunta el comprobante (PDF o imagen JPG/PNG).");
            var t = await pagos.RegistrarTransferenciaAsync(Planilla ?? 0, prestadorId, fecha.Value, numeroOperacion, await LeerAsync(comprobante), comprobante.FileName);
            MensajeOk = $"Pago registrado: {t.NumeroOperacion} por {Formato.Clp(t.MontoLiquido)}.";
        }
        catch (ReglaException ex) { MensajeError = ex.Message; }
        return RedirectToPage(Ruta);
    }

    public async Task<IActionResult> OnPostCerrarAsync()
    {
        await ctx.CargarAsync();
        return await AccionAsync(() => pagos.CerrarCicloAsync(ctx.Ciclo?.Id ?? 0), "Ciclo cerrado: ZIP de respaldos generado; el ciclo queda en solo lectura.", Ruta);
    }

    public async Task<IActionResult> OnGetComprobanteAsync(int transferenciaId)
    {
        var t = await db.Transferencias.FirstOrDefaultAsync(x => x.Id == transferenciaId);
        if (t is null || !archivos.Existe(t.Comprobante)) return NotFound();
        var bytes = archivos.Leer(t.Comprobante);
        var (ext, mime) = PagoService.TipoComprobante(bytes) ?? (".pdf", "application/pdf");
        return File(bytes, mime, $"comprobante_{t.NumeroOperacion}{ext}");
    }
}
