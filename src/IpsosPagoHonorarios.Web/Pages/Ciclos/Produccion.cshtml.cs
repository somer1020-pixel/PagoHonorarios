using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Pages.Ciclos;

public class ProduccionModel(AppDbContext db, ContextoLayout ctx, ProduccionService produccion, CicloService ciclos, Parametros parametros) : PaginaBase
{
    public Ciclo? Ciclo { get; set; }
    public List<Area> Areas { get; set; } = [];
    public List<string> TiposGasto { get; set; } = [];
    public string? AvisoVentana { get; set; }
    public ResultadoImportacion? Resultado { get; set; }
    public Planilla? Resumen { get; set; }
    public List<Planilla> Planillas { get; set; } = [];

    [BindProperty] public TipoArchivoProduccion TipoArchivo { get; set; }
    [BindProperty] public int AreaId { get; set; }
    [BindProperty] public string TipoGasto { get; set; } = "Costo Directo";
    [BindProperty] public string Responsable { get; set; } = "";
    [BindProperty] public string ResponsableEmail { get; set; } = "";
    [BindProperty] public IFormFile? Archivo { get; set; }
    [BindProperty(SupportsGet = true)] public int? Planilla { get; set; }

    public bool PuedeCargar => Puede(Roles.Operaciones);

    private async Task CargarAsync()
    {
        await ctx.CargarAsync();
        Ciclo = ctx.Ciclo;
        var visibles = db.AreasVisibles;
        Areas = await db.Areas.Where(a => visibles == null || visibles.Contains(a.Id)).OrderBy(a => a.Nombre).ToListAsync();
        TiposGasto = await db.TiposGasto.OrderBy(t => t.Id).Select(t => t.Nombre).ToListAsync();
        var par = await parametros.ObtenerAsync();
        AvisoVentana = ProduccionReglas.AvisoVentana(ciclos.Hoy, par.DiaDescargaDesde, par.DiaDescargaHasta);
        if (Ciclo is null) return;
        Planillas = await db.Planillas.Include(p => p.Area).Where(p => p.CicloId == Ciclo.Id).OrderBy(p => p.Area.Nombre).ToListAsync();
        var id = Planilla ?? Planillas.OrderByDescending(p => p.ModificadoEn ?? p.CreadoEn).FirstOrDefault()?.Id;
        if (id is not null) Resumen = await ciclos.PlanillaCompletaAsync(id.Value);
        if (Resumen?.CicloId != Ciclo.Id) Resumen = null;
    }

    public async Task OnGetAsync()
    {
        await CargarAsync();
        if (string.IsNullOrEmpty(Responsable) && User.FindFirst("nombre")?.Value is { } n && Puede(Roles.Operaciones))
        {
            Responsable = n;
            ResponsableEmail = User.Identity?.Name ?? "";
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!PuedeCargar) return Forbid();
        await CargarAsync();
        if (Ciclo is null)
        {
            // Primer uso: crea el ciclo del mes en curso.
            Ciclo = await ciclos.ObtenerOCrearAsync(ciclos.Hoy);
            await db.SaveChangesAsync();
        }
        if (Archivo is null || Archivo.Length == 0)
        {
            Resultado = new(false, [new(0, "Archivo", "Selecciona un archivo XLSX o CSV.")], [], null);
            return Page();
        }
        try
        {
            Resultado = await produccion.ImportarAsync(new SolicitudImportacion(Ciclo.Id, AreaId, TipoGasto, Responsable, ResponsableEmail,
                TipoArchivo, Archivo.FileName, await LeerAsync(Archivo)));
        }
        catch (ReglaException ex)
        {
            Resultado = new(false, [new(0, "Planilla", ex.Message)], [], null);
        }
        if (!Resultado.Cargada) return Page();
        var p = Resultado.Planilla!;
        MensajeOk = $"Planilla {p.Area.Nombre} v{p.Version} generada: {p.Activas().Count()} líneas, {Formato.Clp(p.Activas().Sum(l => l.ValorTotalBruto))}." +
                    (Resultado.Avisos.Count > 0 ? " Avisos: " + string.Join(" ", Resultado.Avisos) : "");
        return p.Estado == PlanillaEstado.ConAlertasCuenta
            ? RedirectToPage("/Ciclos/ValidacionCuentas", new { planilla = p.Id })
            : RedirectToPage(new { planilla = p.Id });
    }
}
