using IpsosPagoHonorarios.Web.Infraestructura;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IpsosPagoHonorarios.Web.Pages.Ciclos;

/// <summary>Selector de ciclo de la barra superior: recuerda el ciclo en una cookie.</summary>
public class SeleccionarModel : PageModel
{
    public IActionResult OnGet(string? codigo, string? volver)
    {
        if (!string.IsNullOrWhiteSpace(codigo))
            Response.Cookies.Append(ContextoLayout.CookieCiclo, codigo, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true });
        return LocalRedirect(!string.IsNullOrEmpty(volver) && Url.IsLocalUrl(volver) ? volver : "/");
    }
}
