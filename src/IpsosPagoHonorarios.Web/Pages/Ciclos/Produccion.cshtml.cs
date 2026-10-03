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
    public string Ventana { get; set; } = "";
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
    public bool CicloCerrado => Ciclo?.Estado == CicloEstado.Cerrado;
    /// <summary>Con el ciclo seleccionado cerrado: otro ciclo abierto al que cambiar o, si no hay, el código del siguiente a abrir.</summary>
    public Ciclo? CicloAbierto { get; set; }
    public string? SiguienteCodigo { get; set; }

    /// <summary>Por qué la planilla de un área no admite una nueva carga (null = la admite).</summary>
    public static string? MotivoSinCarga(Planilla p) => p.Estado switch
    {
        PlanillaEstado.EnRevision => "Enviada a Finanzas y en revisión: no admite una nueva carga.",
        PlanillaEstado.Observada => "Finanzas la observó: las correcciones se hacen en Correcciones, no con una nueva carga.",
        PlanillaEstado.Aprobada => "Aprobada por Finanzas: solo lectura.",
        PlanillaEstado.EnPago => "En pago: solo lectura.",
        PlanillaEstado.Cerrada => "Cerrada: solo lectura.",
        _ => null
    };

    public static string Situacion(Planilla p) => MotivoSinCarga(p) ?? (p.Estado == PlanillaEstado.ConAlertasCuenta
        ? "Con alertas de cuenta (revísalas en Validación de cuentas). Admite una nueva carga, que reemplaza la versión actual."
        : "Admite una nueva carga, que reemplaza la versión actual.");

    private async Task CargarAsync()
    {
        await ctx.CargarAsync();
        Ciclo = ctx.Ciclo;
        var visibles = db.AreasVisibles;
        Areas = await db.Areas.Where(a => visibles == null || visibles.Contains(a.Id)).OrderBy(a => a.Nombre).ToListAsync();
        TiposGasto = await db.TiposGasto.OrderBy(t => t.Id).Select(t => t.Nombre).ToListAsync();
        var par = await parametros.ObtenerAsync();
        AvisoVentana = ProduccionReglas.AvisoVentana(ciclos.Hoy, par.DiaDescargaDesde, par.DiaDescargaHasta);
        Ventana = $"días {par.DiaDescargaDesde} a {par.DiaDescargaHasta}";
        if (Ciclo is null) return;
        Planillas = await db.Planillas.Include(p => p.Area).Where(p => p.CicloId == Ciclo.Id).OrderBy(p => p.Area.Nombre).ToListAsync();
        if (CicloCerrado)
        {
            CicloAbierto = await db.Ciclos.Where(c => c.Estado == CicloEstado.Abierto).OrderByDescending(c => c.Periodo).FirstOrDefaultAsync();
            if (CicloAbierto is null)
                SiguienteCodigo = Formato.CodigoCiclo((await db.Ciclos.MaxAsync(c => c.Periodo)).AddMonths(1));
        }
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

    /// <summary>Con todos los ciclos cerrados: abre el ciclo siguiente para poder cargar producción.</summary>
    public async Task<IActionResult> OnPostAbrirCicloAsync()
    {
        if (!PuedeCargar) return Forbid();
        if (await db.Ciclos.AnyAsync(c => c.Estado == CicloEstado.Abierto))
        {
            MensajeError = "Ya hay un ciclo abierto: selecciónalo en la barra superior.";
            return RedirectToPage();
        }
        var ultimo = await db.Ciclos.MaxAsync(c => (DateOnly?)c.Periodo) ?? ciclos.Hoy.AddMonths(-1);
        var c = await ciclos.ObtenerOCrearAsync(ultimo.AddMonths(1));
        await db.SaveChangesAsync();
        MensajeOk = $"Ciclo {c.Codigo} abierto: ya puedes cargar la producción.";
        return LocalRedirect($"/Ciclos/Seleccionar?codigo={c.Codigo}&volver=%2FCiclos%2FProduccion");
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
