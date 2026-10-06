using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Pages.Maestros;

public class PrestadoresModel(AppDbContext db, PrestadoresService prestadores, CuentasService cuentas) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool Nuevo { get; set; }
    public List<Prestador> Lista { get; set; } = [];
    public Prestador? Sel { get; set; }
    public List<string> Bancos { get; set; } = [];
    public List<string> Tipos { get; set; } = [];
    public List<CuentaBancaria> ARevision { get; set; } = [];
    [TempData] public string? Enlace { get; set; }
    [TempData] public string? ResultadoCarga { get; set; }

    public const int MaxLista = 100;
    public int Total { get; set; }
    public bool PuedeEditar => Puede(Roles.Operaciones, Roles.Finanzas);
    public bool PuedeValidar => Puede(Roles.Finanzas);

    public async Task OnGetAsync()
    {
        var q = db.Prestadores.Include(p => p.Cuentas).AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var t = Q.Trim();
            var digitos = new string(t.Where(char.IsAsciiDigit).ToArray());
            int.TryParse(digitos.Length > 8 ? digitos[..^1] : digitos, out var rut);
            q = q.Where(p => p.NombreCompleto.Contains(t) || (rut > 0 && p.Rut == rut));
        }
        // Se muestran los primeros 100: con miles de prestadores, la lista completa hacía lenta la página (buscar para acotar).
        Total = await q.CountAsync();
        Lista = await q.OrderBy(p => p.NombreCompleto).Take(MaxLista).ToListAsync();
        if (!Nuevo)
            Sel = Id is null ? null : await db.Prestadores.Include(p => p.Cuentas).FirstOrDefaultAsync(p => p.Id == Id);
        Bancos = await db.Bancos.OrderBy(b => b.Id).Select(b => b.Nombre).ToListAsync();
        Tipos = await db.TiposCuenta.OrderBy(t => t.Id).Select(t => t.Nombre).ToListAsync();
        ARevision = await db.CuentasBancarias.Include(c => c.Prestador)
            .Where(c => c.Estado == CuentaEstado.PendienteValidacion).OrderBy(c => c.Id).ToListAsync();
    }

    public static (Tono, string) Portal(Prestador p) =>
        p.PortalActivadoEn is not null ? (Tono.Success, "Activado") : p.PortalInvitadoEn is not null ? (Tono.Info, "Invitado") : (Tono.Neutral, "Sin invitar");

    public async Task<IActionResult> OnPostGuardarAsync(string nombre, string rut, string? email, string? telefono, bool whatsApp)
    {
        if (!PuedeEditar) return Forbid();
        try
        {
            var p = await prestadores.GuardarAsync(Id, nombre, rut, email, telefono, whatsApp);
            MensajeOk = "Prestador guardado.";
            return RedirectToPage(new { id = p.Id });
        }
        catch (ReglaException ex)
        {
            MensajeError = ex.Message;
            return RedirectToPage(new { id = Id, nuevo = Id is null });
        }
    }

    public Task<IActionResult> OnPostRegistrarCuentaAsync(string banco, string tipo, string cuenta, string cuentaRepetida) =>
        AccionAsync(() => cuentas.RegistrarAsync(Id ?? 0, banco, tipo, cuenta, cuentaRepetida, "Mantención de prestadores"),
            "Cuenta registrada: queda pendiente de validación por Finanzas; la anterior pasó a Inactiva.", new { id = Id }, [Roles.Operaciones, Roles.Finanzas]);

    public Task<IActionResult> OnPostValidarAsync(int cuentaId) =>
        AccionAsync(() => cuentas.ValidarAsync(cuentaId), "Cuenta validada.", new { id = Id }, [Roles.Finanzas]);

    public Task<IActionResult> OnPostRechazarAsync(int cuentaId) =>
        AccionAsync(() => cuentas.RechazarAsync(cuentaId), "Cuenta rechazada.", new { id = Id }, [Roles.Finanzas]);

    public Task<IActionResult> OnPostInvitarAsync() =>
        AccionAsync(async () => Enlace = await prestadores.InvitarAsync(Id ?? 0, BaseUrl), "Invitación enviada por correo (enlace de 72 horas).", new { id = Id }, [Roles.Operaciones, Roles.Finanzas]);

    public Task<IActionResult> OnPostCopiarEnlaceAsync() =>
        AccionAsync(async () => Enlace = await prestadores.InvitarAsync(Id ?? 0, BaseUrl, soloCopiar: true), "Enlace de activación generado: cópialo y entrégalo por el canal habitual.", new { id = Id }, [Roles.Operaciones, Roles.Finanzas]);

    public Task<IActionResult> OnPostRestablecerAsync() =>
        AccionAsync(async () => Enlace = await prestadores.RestablecerAsync(Id ?? 0, BaseUrl), "Enlace de restablecimiento generado (y enviado si tiene correo).", new { id = Id }, [Roles.Operaciones, Roles.Finanzas]);

    /// <summary>R-28: carga inicial desde planillas anteriores (formato Finanzas).</summary>
    public async Task<IActionResult> OnPostCargaInicialAsync(List<IFormFile> archivos)
    {
        if (!PuedeValidar) return Forbid();
        try
        {
            if (archivos.Count == 0) throw new ReglaException("Selecciona una o más planillas anteriores (XLSX).");
            var filas = new List<FilaCuentaHistorica>();
            foreach (var a in archivos)
            {
                using var s = a.OpenReadStream();
                try { filas.AddRange(CuentasService.LeerHistoricas(s)); }
                catch (Exception) { throw new ReglaException($"No se pudo leer {a.FileName}."); }
            }
            var r = await cuentas.CargaInicialAsync(filas, string.Join(", ", archivos.Select(a => a.FileName)));
            ResultadoCarga = $"{r.Unicas}|{r.ConVarias}|{r.Compartidas}|{r.Omitidas}|{string.Join("\n", r.Detalle.Take(30))}";
            MensajeOk = "Carga inicial de cuentas procesada.";
        }
        catch (ReglaException ex) { MensajeError = ex.Message; }
        return RedirectToPage(new { id = Id });
    }
}
