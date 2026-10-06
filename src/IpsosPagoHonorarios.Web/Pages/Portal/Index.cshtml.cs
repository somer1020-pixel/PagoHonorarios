using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IpsosPagoHonorarios.Web.Pages.Portal;

/// <summary>Portal del prestador (R-17 a R-19): solo sus filas, solo su boleta.</summary>
public class IndexModel(AppDbContext db, PortalService portal, BoletaService boletas, Parametros parametros, PrestadoresService prestadores, IOptions<OpcionesWhatsApp> whatsapp) : PageModel
{
    public sealed record Chequeo(bool Ok, string Texto);

    public Prestador? Prestador { get; set; }
    public List<PortalService.PlanillaPortal> Planillas { get; set; } = [];
    public List<PortalService.PagoAnterior> Pagos { get; set; } = [];
    public Parametro Param { get; set; } = new();
    public decimal Tasa { get; set; }
    [TempData] public string? Resultado { get; set; }
    [TempData] public string? Error { get; set; }
    [TempData] public string? Aviso { get; set; }
    public bool WhatsAppDisponible => whatsapp.Value.Habilitado;
    [TempData] public int? PlanillaResultado { get; set; }

    public List<Chequeo> Chequeos => Resultado is null ? [] : Json.Leer<List<Chequeo>>(Resultado) ?? [];

    private int? PrestadorId => int.TryParse(User.FindFirst("prestador")?.Value, out var id) ? id : null;

    public async Task<IActionResult> OnGetAsync()
    {
        if (PrestadorId is not { } id) return Forbid();
        Prestador = await db.Prestadores.Include(p => p.Cuentas).FirstOrDefaultAsync(p => p.Id == id);
        if (Prestador is null) return Forbid();
        Planillas = await portal.MisPlanillasAsync(id);
        Pagos = await portal.MisPagosAsync(id);
        Param = await parametros.ObtenerAsync();
        Tasa = await parametros.TasaAsync(Planillas.FirstOrDefault()?.Planilla.Ciclo.Periodo.Year ?? DateTime.UtcNow.Year);
        return Page();
    }

    public async Task<IActionResult> OnPostSubirAsync(int planillaId, IFormFile? pdf)
    {
        if (PrestadorId is not { } id) return Forbid();
        PlanillaResultado = planillaId;
        try
        {
            if (pdf is null || pdf.Length == 0) throw new ReglaException("Selecciona el PDF de tu boleta.");
            using var ms = new MemoryStream();
            await pdf.CopyToAsync(ms);
            var r = await boletas.SubirAsync(planillaId, id, ms.ToArray(), pdf.FileName, BoletaCanal.Portal);
            var b = r.Boleta;
            var par = await parametros.ObtenerAsync();
            var planilla = await db.Planillas.Include(p => p.Ciclo).FirstAsync(p => p.Id == planillaId);
            var limite = Conciliacion.FechaLimite(planilla.Ciclo.Periodo, par.DiaLimiteBoleta);
            var receptorOk = RutHelper.TryParse(b.RutReceptor, out var rr, out _) && RutHelper.TryParse(par.RutEmpresa, out var re, out _) && rr == re;
            var fechaOk = b.FechaEmision is { } f && f >= planilla.Ciclo.Periodo && f <= limite;
            var lista = new List<Chequeo>
            {
                new(true, $"Boleta N° {b.NumeroBoleta} · leída (confianza {b.Confianza.ToString().ToLowerInvariant()})"),
                new(true, $"Archivo PDF válido ({Math.Max(1, pdf.Length / 1024)} KB, no duplicado)"),
                new(b.RutEmisor is not null, b.RutEmisor is null ? "No se pudo leer el RUT emisor" : $"Emitida por usted · {RutHelper.Formatear(b.RutEmisor.Value, b.RutEmisorDv!)}"),
                new(receptorOk, $"Receptor · {RutHelper.Formatear(b.RutReceptor ?? "—")}" + (receptorOk ? "" : $" (debe ser {RutHelper.Formatear(par.RutEmpresa)})")),
                new(fechaOk, $"Fecha {Formato.Fecha(b.FechaEmision)} (plazo hasta {Formato.Fecha(limite)})"),
                new(r.Conciliacion.Diferencia == 0 && b.MontoBruto is not null, b.MontoBruto == r.SumaPlanilla
                    ? $"Monto bruto {Formato.Clp(b.MontoBruto)} = total a boletear"
                    : $"Monto bruto {Formato.Clp(b.MontoBruto)} ≠ total a boletear {Formato.Clp(r.SumaPlanilla)} (diferencia {Formato.Clp(r.Conciliacion.Diferencia)})")
            };
            foreach (var p in r.Conciliacion.Problemas.Where(p => p.Contains("anulada") || p.Contains("ya se usó")))
                lista.Add(new(false, p));
            Resultado = Json.Serializar(lista);
        }
        catch (ReglaException ex) { Error = ex.Message; }
        return RedirectToPage();
    }
    /// <summary>El prestador indica su celular y si acepta avisos por WhatsApp (solicitud y recordatorio de boleta).</summary>
    public async Task<IActionResult> OnPostWhatsAppAsync(string? telefono, bool acepta)
    {
        if (PrestadorId is not { } id) return Forbid();
        try
        {
            await prestadores.ConfigurarWhatsAppAsync(id, telefono, acepta);
            Aviso = acepta ? "Listo: te avisaremos por WhatsApp cuando haya que subir una boleta." : "Quitamos los avisos por WhatsApp. Seguirás recibiendo correos.";
        }
        catch (ReglaException ex) { Error = ex.Message; }
        return RedirectToPage();
    }
}
