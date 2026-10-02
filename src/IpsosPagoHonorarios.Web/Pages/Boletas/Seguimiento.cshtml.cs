using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Pages.Boletas;

public class SeguimientoModel(AppDbContext db, ContextoLayout ctx, CicloService ciclos, BoletaService boletas, Almacenamiento archivos, Parametros parametros) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public int? Planilla { get; set; }
    [BindProperty(SupportsGet = true)] public int? Prestador { get; set; }
    public List<Planilla> Planillas { get; set; } = [];
    public Planilla? P { get; set; }
    public List<(Prestador Prestador, decimal Suma, BoletaHonorarios? Boleta)> Filas { get; set; } = [];
    public (Prestador Prestador, decimal Suma, BoletaHonorarios? Boleta)? Sel { get; set; }
    public decimal Tasa { get; set; }
    public string RutEmpresa { get; set; } = "";
    public bool PuedeOperaciones => Puede(Roles.Operaciones);
    public bool PuedeSeguimiento => Puede(Roles.Operaciones, Roles.Finanzas);

    public async Task OnGetAsync()
    {
        await ctx.CargarAsync();
        if (ctx.Ciclo is null) return;
        Planillas = await db.Planillas.Include(p => p.Area).Where(p => p.CicloId == ctx.Ciclo.Id).OrderBy(p => p.Area.Nombre).ToListAsync();
        var id = Planilla ?? Planillas.FirstOrDefault(p => p.Estado == PlanillaEstado.Observada)?.Id ?? Planillas.FirstOrDefault()?.Id;
        if (id is null) return;
        P = await ciclos.PlanillaCompletaAsync(id.Value);
        if (P is null) return;
        Planilla = P.Id;
        Tasa = await parametros.TasaAsync(P.Ciclo.Periodo.Year);
        RutEmpresa = (await parametros.ObtenerAsync()).RutEmpresa;
        Filas = P.Activas().GroupBy(l => l.PrestadorId).OrderBy(g => g.Min(l => l.Numero))
            .Select(g => (g.First().Prestador, g.Sum(l => l.ValorTotalBruto), P.BoletaVigente(g.Key))).ToList();
        var sel = Filas.FirstOrDefault(f => f.Prestador.Id == Prestador);
        if (sel.Prestador is null) sel = Filas.FirstOrDefault(f => f.Boleta is null || f.Boleta.Estado == BoletaEstado.Observada || !f.Boleta.Cuadra);
        if (sel.Prestador is null) sel = Filas.FirstOrDefault();
        Sel = sel.Prestador is null ? null : sel;
        Prestador = Sel?.Prestador.Id;
    }

    public async Task<IActionResult> OnGetPdfAsync(int boletaId)
    {
        var b = await db.Boletas.FirstOrDefaultAsync(x => x.Id == boletaId);
        if (b is null || !archivos.Existe(b.RutaPdf)) return NotFound();
        return File(archivos.Leer(b.RutaPdf), "application/pdf", $"boleta_{b.NumeroBoleta ?? b.Id.ToString()}.pdf");
    }

    private object Ruta => new { planilla = Planilla, prestador = Prestador };

    public Task<IActionResult> OnPostConfirmarAsync(int boletaId) =>
        AccionAsync(() => boletas.ConfirmarAsync(boletaId), "Datos de la boleta confirmados.", Ruta, [Roles.Operaciones, Roles.Finanzas]);

    public Task<IActionResult> OnPostCorregirAsync(int boletaId, string? numero, string? rutEmisor, string? rutReceptor, string? fecha, string? bruto, string? retencion, string? liquido) =>
        AccionAsync(async () =>
        {
            DateOnly? f = DateOnly.TryParseExact(fecha, ["dd-MM-yyyy", "yyyy-MM-dd", "dd/MM/yyyy"], out var d) ? d : null;
            var r = await boletas.CorregirLecturaAsync(boletaId, new DatosBoleta
            {
                Numero = numero?.Trim(), RutEmisor = rutEmisor, RutReceptor = rutReceptor, FechaEmision = f,
                Bruto = ExcelPlanilla.ParseNumero(bruto), Retencion = ExcelPlanilla.ParseNumero(retencion), Liquido = ExcelPlanilla.ParseNumero(liquido)
            });
            if (!r.Cuadra) throw new ReglaException("Lectura corregida, pero la boleta no cuadra: " + string.Join(" ", r.Problemas));
        }, "Lectura corregida: la boleta cuadra.", Ruta, [Roles.Operaciones, Roles.Finanzas]);

    public Task<IActionResult> OnPostPedirNuevaAsync(int boletaId, string motivo) =>
        AccionAsync(() => boletas.PedirNuevaAsync(boletaId, motivo), "Se pidió una nueva boleta al prestador (aviso por correo).", Ruta, [Roles.Operaciones, Roles.Finanzas]);

    public Task<IActionResult> OnPostRecordarAsync() =>
        AccionAsync(async () => { var n = await boletas.RecordarPendientesAsync(Planilla ?? 0); MensajeOk = $"Recordatorio enviado a {n} prestadores."; },
            "Recordatorio enviado.", Ruta, [Roles.Operaciones, Roles.Finanzas]);

    /// <summary>R-22: carga en nombre del prestador, con motivo obligatorio.</summary>
    public async Task<IActionResult> OnPostCargarAsync(int prestadorId, IFormFile? pdf, string? motivo)
    {
        if (!PuedeOperaciones) return Forbid();
        Prestador = prestadorId;
        try
        {
            if (pdf is null || pdf.Length == 0) throw new ReglaException("Selecciona el PDF de la boleta.");
            var r = await boletas.SubirAsync(Planilla ?? 0, prestadorId, await LeerAsync(pdf), pdf.FileName, BoletaCanal.Operaciones, motivo);
            if (r.Conciliacion.Cuadra) MensajeOk = $"Boleta N° {r.Boleta.NumeroBoleta} cargada en nombre del prestador. Cuadra con la planilla.";
            else MensajeError = $"Boleta N° {r.Boleta.NumeroBoleta} cargada, pero no cuadra: " + string.Join(" ", r.Conciliacion.Problemas);
        }
        catch (ReglaException ex) { MensajeError = ex.Message; }
        return RedirectToPage(Ruta);
    }
}
