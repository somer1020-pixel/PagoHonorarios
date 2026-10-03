using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Pages.Ciclos;

public class ValidacionCuentasModel(
    AppDbContext db, ContextoLayout ctx, CicloService ciclos, CuentasService cuentas, PlanillaService planillas, ProduccionService produccion) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public int? Planilla { get; set; }
    public List<Planilla> Planillas { get; set; } = [];
    public Planilla? P { get; set; }
    public List<SolicitudCorreccion> Solicitudes { get; set; } = [];
    public List<string> Bancos { get; set; } = [];
    public List<string> Tipos { get; set; } = [];
    public Verificacion? Envio { get; set; }

    public bool PuedeOperar => Puede(Roles.Operaciones);
    public bool PuedeRegistrar => Puede(Roles.Operaciones, Roles.Finanzas);

    public async Task OnGetAsync()
    {
        await ctx.CargarAsync();
        if (ctx.Ciclo is null) return;
        Planillas = await db.Planillas.Include(p => p.Area).Where(p => p.CicloId == ctx.Ciclo.Id).OrderBy(p => p.Area.Nombre).ThenBy(p => p.Numero).ToListAsync();
        var id = Planilla ?? Planillas.FirstOrDefault(p => p.Estado == PlanillaEstado.ConAlertasCuenta)?.Id ?? Planillas.FirstOrDefault()?.Id;
        if (id is null) return;
        P = await ciclos.PlanillaCompletaAsync(id.Value);
        if (P is null) return;
        Planilla = P.Id;
        Solicitudes = await db.SolicitudesCorreccion.Where(s => s.PlanillaId == P.Id).OrderByDescending(s => s.Id).ToListAsync();
        Bancos = await db.Bancos.OrderBy(b => b.Id).Select(b => b.Nombre).ToListAsync();
        Tipos = await db.TiposCuenta.OrderBy(t => t.Id).Select(t => t.Nombre).ToListAsync();
        Envio = Flujo.PuedeEnviar(P);
    }

    public Task<IActionResult> OnPostRegistrarAsync(int prestadorId, string banco, string tipo, string cuenta, string cuentaRepetida) =>
        AccionAsync(() => cuentas.RegistrarAsync(prestadorId, banco, tipo, cuenta, cuentaRepetida, $"Validación de cuentas, planilla {Planilla}"),
            "Cuenta registrada: queda pendiente de validación por Finanzas.", new { planilla = Planilla }, [Roles.Operaciones, Roles.Finanzas]);

    public Task<IActionResult> OnPostDiferirAsync(int lineaId) =>
        AccionAsync(() => planillas.DiferirLineaAsync(lineaId, "alerta de cuenta"), "Filas diferidas al ciclo siguiente.", new { planilla = Planilla }, [Roles.Operaciones]);

    public Task<IActionResult> OnPostSolicitarAsync(string para, string mensaje, int[] lineas) =>
        AccionAsync(() => cuentas.SolicitarCorreccionAsync(Planilla ?? 0, para, mensaje, lineas.Length > 0 ? lineas : null),
            "Solicitud de corrección enviada y registrada.", new { planilla = Planilla }, [Roles.Operaciones]);

    public Task<IActionResult> OnPostReenviarSolicitudAsync(int solicitudId) =>
        AccionAsync(() => cuentas.ReenviarSolicitudAsync(solicitudId), "Solicitud reenviada.", new { planilla = Planilla }, [Roles.Operaciones]);

    public Task<IActionResult> OnPostEnviarAsync() =>
        AccionAsync(() => planillas.EnviarAsync(Planilla ?? 0), "Planilla enviada a Finanzas.", new { planilla = Planilla }, [Roles.Operaciones]);

    public async Task<IActionResult> OnPostCorregidaAsync(IFormFile? archivo, TipoArchivoProduccion tipoArchivo)
    {
        if (!PuedeOperar) return Forbid();
        var p = await db.Planillas.Include(x => x.Area).FirstOrDefaultAsync(x => x.Id == Planilla);
        if (p is null) return NotFound();
        if (archivo is null || archivo.Length == 0)
        {
            MensajeError = "Selecciona el archivo corregido.";
            return RedirectToPage(new { planilla = Planilla });
        }
        try
        {
            var tipoGasto = await db.LineasPago.Where(l => l.PlanillaId == p.Id).Select(l => l.TipoGasto).FirstOrDefaultAsync() ?? "Costo Directo";
            var r = await produccion.ImportarAsync(new SolicitudImportacion(p.CicloId, p.AreaId, tipoGasto, p.ResponsableNombre, p.ResponsableEmail,
                tipoArchivo, archivo.FileName, await LeerAsync(archivo), PlanillaId: p.Id));
            if (r.Cargada) MensajeOk = $"Versión v{r.Planilla!.Version} cargada y validada completa.";
            else MensajeError = "La versión corregida no se cargó: " + string.Join(" · ", r.Errores.Take(5).Select(e => $"fila {e.Fila} {e.Campo}: {e.Motivo}"));
        }
        catch (ReglaException ex) { MensajeError = ex.Message; }
        return RedirectToPage(new { planilla = Planilla });
    }
}
