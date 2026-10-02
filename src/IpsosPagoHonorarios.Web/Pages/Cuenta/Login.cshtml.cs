using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IpsosPagoHonorarios.Web.Pages.Cuenta;

public class LoginModel(SignInManager<Usuario> signIn, UserManager<Usuario> users, PrestadoresService prestadores, Auditor auditor,
    AppDbContext db, Turnstile turnstile, IHostEnvironment env) : PageModel
{
    [BindProperty] public string Usuario { get; set; } = "";
    [BindProperty] public string Contrasena { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? Error { get; set; }
    public bool EsDemo => env.IsDevelopment();
    public string? TurnstileKey => turnstile.Habilitado ? turnstile.SiteKey : null;

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? Redirect(User.IsInRole(Roles.Prestador) ? "/Portal" : "/") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await turnstile.VerificarAsync(Request.Form["cf-turnstile-response"], HttpContext.Connection.RemoteIpAddress?.ToString()))
        {
            Error = "No pudimos verificar que no eres un robot. Intenta de nuevo.";
            return Page();
        }
        var u = string.IsNullOrWhiteSpace(Usuario) ? null : await prestadores.BuscarUsuarioAsync(Usuario);
        if (u is null || !u.Activo)
        {
            Error = "Correo/RUT o contraseña incorrectos.";
            return Page();
        }
        var r = await signIn.PasswordSignInAsync(u, Contrasena ?? "", isPersistent: false, lockoutOnFailure: true);
        if (r.Succeeded)
        {
            var prestador = await users.IsInRoleAsync(u, Roles.Prestador);
            auditor.Registrar("Usuario", u.UserName, prestador ? "Acceso al portal" : "Ingreso", null);
            await db.SaveChangesAsync();
            if (prestador) return Redirect("/Portal");
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
        }
        if (r.IsLockedOut)
        {
            Error = "La cuenta está bloqueada por 15 minutos tras 5 intentos fallidos.";
            auditor.Registrar("Usuario", u.UserName, "Cuenta bloqueada", null);
            await db.SaveChangesAsync();
            return Page();
        }
        var restantes = users.Options.Lockout.MaxFailedAccessAttempts - await users.GetAccessFailedCountAsync(u);
        Error = $"Correo/RUT o contraseña incorrectos. Te quedan {restantes} intentos antes de bloquear la cuenta por 15 minutos.";
        return Page();
    }
}
