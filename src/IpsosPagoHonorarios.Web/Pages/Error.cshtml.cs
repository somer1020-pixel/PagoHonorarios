using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IpsosPagoHonorarios.Web.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public int Codigo { get; set; } = 500;
    public string Titulo => Codigo switch { 403 => "Acceso denegado", 404 => "Página no encontrada", 429 => "Demasiados intentos", _ => "Ocurrió un error" };
    public string Detalle => Codigo switch
    {
        403 => "Tu rol no tiene permiso para esta pantalla.",
        404 => "La dirección no existe.",
        429 => "Espera un minuto antes de volver a intentar.",
        _ => "Intenta nuevamente. Si el problema persiste, avisa al administrador."
    };

    public void OnGet(int? codigo) => Codigo = codigo ?? 500;
    public void OnPost(int? codigo) => Codigo = codigo ?? 500;
}
