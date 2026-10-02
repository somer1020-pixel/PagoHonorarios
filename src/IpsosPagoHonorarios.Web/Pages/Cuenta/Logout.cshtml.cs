using IpsosPagoHonorarios.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IpsosPagoHonorarios.Web.Pages.Cuenta;

public class LogoutModel(SignInManager<Usuario> signIn) : PageModel
{
    public IActionResult OnGet() => Redirect("/Cuenta/Login");

    public async Task<IActionResult> OnPostAsync()
    {
        await signIn.SignOutAsync();
        return Redirect("/Cuenta/Login");
    }
}
