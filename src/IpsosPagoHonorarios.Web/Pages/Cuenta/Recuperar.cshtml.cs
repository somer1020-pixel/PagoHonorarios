using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IpsosPagoHonorarios.Web.Pages.Cuenta;

/// <summary>Sin token: solicita el enlace por correo. Con token (u, t): define la nueva contraseña. También sirve /Cuenta/Activar.</summary>
public class RecuperarModel(PrestadoresService prestadores) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? U { get; set; }
    [BindProperty(SupportsGet = true)] public string? T { get; set; }
    [BindProperty] public string? CorreoORut { get; set; }
    [BindProperty] public string? Contrasena { get; set; }
    [BindProperty] public string? Repetir { get; set; }
    public bool Enviado { get; set; }
    public bool Listo { get; set; }
    public List<string> Errores { get; } = [];
    public virtual bool Activacion => false;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(T))
        {
            if (!string.IsNullOrWhiteSpace(CorreoORut))
                await prestadores.SolicitarRecuperacionAsync(CorreoORut, $"{Request.Scheme}://{Request.Host}{Request.PathBase}");
            Enviado = true;
            return Page();
        }
        if (string.IsNullOrEmpty(Contrasena) || Contrasena != Repetir)
        {
            Errores.Add("Las contraseñas no coinciden.");
            return Page();
        }
        var r = await prestadores.DefinirContrasenaAsync(U ?? "", T, Contrasena, Activacion);
        if (!r.Succeeded)
        {
            Errores.AddRange(r.Errors.Select(e => e.Code == "InvalidToken" ? "El enlace no es válido o venció (72 horas, un solo uso). Pide uno nuevo a Operaciones." : e.Description));
            return Page();
        }
        Listo = true;
        return Page();
    }
}
