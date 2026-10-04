using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.Encodings.Web;

namespace IpsosPagoHonorarios.Web.Infraestructura;

public enum Tono { Neutral, Info, Warning, Danger, Success }

/// <summary>
/// Badges con texto en todo estado (§5). Mapeo de la especificación; los estados no mapeados usan el tono del mockup
/// (TODO(diseño): confirmar).
/// </summary>
public static class Badge
{
    /// <summary>Fecha UTC en ISO 8601 para la cuenta regresiva del navegador.</summary>
    public static string IsoUtc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("o");

    public static IHtmlContent Html(Tono tono, string texto, string? extra = null) =>
        new HtmlString($"<span class=\"tag tag-{tono.ToString().ToLowerInvariant()}{(extra is null ? "" : " " + extra)}\">{HtmlEncoder.Default.Encode(texto)}</span>");

    public static IHtmlContent De(PlanillaEstado e) => Html(e switch
    {
        PlanillaEstado.Borrador => Tono.Neutral,
        PlanillaEstado.ConAlertasCuenta => Tono.Warning,
        PlanillaEstado.EnRevision => Tono.Info,
        PlanillaEstado.Observada => Tono.Warning,
        PlanillaEstado.Aprobada or PlanillaEstado.Cerrada => Tono.Success,
        PlanillaEstado.EnPago => Tono.Success,
        _ => Tono.Neutral
    }, e == PlanillaEstado.EnPago ? "Aprobada · en pago" : e.Nombre());

    public static IHtmlContent De(LineaEstado e) => Html(e switch
    {
        LineaEstado.PendienteBoleta => Tono.Neutral,
        LineaEstado.Lista => Tono.Info,
        LineaEstado.Observada => Tono.Warning,
        LineaEstado.Corregida => Tono.Info,
        LineaEstado.Aprobada or LineaEstado.Pagada => Tono.Success,
        LineaEstado.Diferida => Tono.Neutral,
        _ => Tono.Neutral
    }, e.Nombre());

    public static IHtmlContent De(CuentaEstado e) => Html(e switch
    {
        CuentaEstado.PendienteValidacion => Tono.Warning,
        CuentaEstado.Validada => Tono.Success,
        CuentaEstado.Inactiva => Tono.Neutral,
        CuentaEstado.Rechazada => Tono.Danger,
        _ => Tono.Neutral
    }, e.Nombre());

    public static IHtmlContent De(BoletaEstado e) => Html(e switch
    {
        BoletaEstado.Recibida => Tono.Info,
        BoletaEstado.Confirmada => Tono.Success,
        BoletaEstado.Observada => Tono.Warning,
        BoletaEstado.Rechazada => Tono.Danger,
        _ => Tono.Neutral
    }, e.ToString());

    public static Tono TonoDe(ResultadoCuenta r) => r switch
    {
        ResultadoCuenta.Coincide => Tono.Success,
        ResultadoCuenta.CuentaNueva => Tono.Info,
        ResultadoCuenta.CuentaTercero => Tono.Danger,
        ResultadoCuenta.SinCuentaPlanilla => Tono.Neutral,
        _ => Tono.Warning
    };

    public static IHtmlContent De(ResultadoCuenta r) => Html(TonoDe(r), r.Nombre());

    public static IHtmlContent De(ObservacionEstado e) => Html(e switch
    {
        ObservacionEstado.Abierta => Tono.Warning,
        ObservacionEstado.Corregida => Tono.Info,
        ObservacionEstado.Aceptada => Tono.Success,
        ObservacionEstado.Vencida => Tono.Danger,
        _ => Tono.Neutral
    }, e.ToString());

    public static IHtmlContent De(Confianza c) => Html(c switch
    {
        Confianza.Alta => Tono.Success,
        Confianza.Media => Tono.Warning,
        _ => Tono.Danger
    }, "Confianza " + c.ToString().ToLowerInvariant());

    public static IHtmlContent De(Chequeo c, string? detalle = null) => c switch
    {
        Chequeo.Ok => Html(Tono.Success, "✓ OK"),
        Chequeo.Pendiente => Html(Tono.Warning, "! " + (detalle ?? "Pendiente")),
        _ => Html(Tono.Danger, "✗ " + (detalle ?? "Error"))
    };

    public static IHtmlContent De(CicloEstado e) => Html(e == CicloEstado.Abierto ? Tono.Info : Tono.Success, e.ToString());
}

/// <summary>Datos comunes de la barra superior y el menú lateral.</summary>
public class ContextoLayout(AppDbContext db, CicloService ciclos, IHttpContextAccessor acc)
{
    private static readonly ResultadoCuenta[] ResultadosAlerta = Enum.GetValues<ResultadoCuenta>().Where(r => r.EsAlerta()).ToArray();

    public const string CookieCiclo = "honorarios.ciclo";

    private bool _cargado;
    public Ciclo? Ciclo { get; private set; }
    public List<Ciclo> Ciclos { get; private set; } = [];
    public Devolucion? DevolucionActiva { get; private set; }
    public string? DevolucionArea { get; private set; }
    public int AlertasCuenta { get; private set; }
    public int BoletasPendientes { get; private set; }
    public int ObservacionesAbiertas { get; private set; }

    public async Task CargarAsync()
    {
        if (_cargado) return;
        _cargado = true;
        var ctx = acc.HttpContext;
        Ciclo = await ciclos.SeleccionadoAsync(ctx?.Request.Query["ciclo"].FirstOrDefault() ?? ctx?.Request.Cookies[CookieCiclo]);
        Ciclos = await ciclos.TodosAsync();
        if (Ciclo is null) return;
        var ahora = ciclos.AhoraUtc;
        var dev = await db.Devoluciones.Include(d => d.Planilla).ThenInclude(p => p.Area)
            .Where(d => d.Planilla.CicloId == Ciclo.Id && d.Resultado == DevolucionResultado.Pendiente)
            .OrderBy(d => d.VenceEn).FirstOrDefaultAsync();
        DevolucionActiva = dev;
        DevolucionArea = dev?.Planilla.Titulo;
        // Contadores del menú (en cada página): se cuentan en la base, sin traer las líneas del ciclo.
        var cicloId = Ciclo.Id;
        var activas = db.LineasPago.Where(l => l.Planilla.CicloId == cicloId && l.Estado != LineaEstado.Diferida);
        // Una alerta por prestador y planilla (R-24 se informa por prestador).
        AlertasCuenta = await activas
            .Where(l => (l.Planilla.Estado == PlanillaEstado.Borrador || l.Planilla.Estado == PlanillaEstado.ConAlertasCuenta)
                        && ResultadosAlerta.Contains(l.ResultadoCuenta) && !l.AlertaCuentaResuelta)
            .Select(l => new { l.PlanillaId, l.PrestadorId }).Distinct().CountAsync();
        BoletasPendientes = await activas
            .Where(l => l.Planilla.Estado != PlanillaEstado.Aprobada && l.Planilla.Estado != PlanillaEstado.EnPago && l.Planilla.Estado != PlanillaEstado.Cerrada)
            .Where(l => !db.Boletas.Any(b => b.PlanillaId == l.PlanillaId && b.PrestadorId == l.PrestadorId
                                            && b.Estado != BoletaEstado.Reemplazada && b.Estado != BoletaEstado.Rechazada))
            .Select(l => new { l.PlanillaId, l.PrestadorId }).Distinct().CountAsync();
        ObservacionesAbiertas = await db.Observaciones.CountAsync(o => o.LineaPago.Planilla.CicloId == Ciclo.Id && o.Estado == ObservacionEstado.Abierta);
    }
}

/// <summary>Base de las páginas internas: mensajes, ciclo seleccionado y control de roles por acción.</summary>
public abstract class PaginaBase : PageModel
{
    [TempData] public string? MensajeOk { get; set; }
    [TempData] public string? MensajeError { get; set; }

    public bool Puede(params string[] roles) => roles.Append(Roles.Admin).Any(User.IsInRole);

    /// <summary>Ejecuta una acción de negocio; las reglas incumplidas se muestran como error.</summary>
    protected async Task<IActionResult> AccionAsync(Func<Task> accion, string ok, object? rutaValores = null, string[]? roles = null)
    {
        if (roles is not null && !Puede(roles)) return Forbid();
        var previo = MensajeOk;
        try
        {
            await accion();
            if (MensajeOk == previo) MensajeOk = ok;   // la acción puede dejar un mensaje más preciso
        }
        catch (ReglaException ex)
        {
            MensajeError = ex.Message;
        }
        return RedirectToPage(rutaValores);
    }

    protected string BaseUrl => $"{Request.Scheme}://{Request.Host}{Request.PathBase}";

    protected static async Task<byte[]> LeerAsync(IFormFile f)
    {
        using var ms = new MemoryStream();
        await f.CopyToAsync(ms);
        return ms.ToArray();
    }
}
