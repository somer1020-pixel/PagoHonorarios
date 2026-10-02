using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IpsosPagoHonorarios.Web.Pages.Maestros;

/// <summary>Usuarios internos (Operaciones, Finanzas, Administrador): solo el Administrador.</summary>
[Authorize(Roles = Roles.Admin)]
public class UsuariosModel(UsuariosService usuarios) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public string? Editar { get; set; }
    [TempData] public string? Enlace { get; set; }
    [TempData] public string? EnlacePara { get; set; }
    public List<UsuariosService.UsuarioInterno> Lista { get; set; } = [];
    public UsuariosService.UsuarioInterno? Sel { get; set; }
    public string? MiId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    public async Task OnGetAsync()
    {
        Lista = await usuarios.ListarAsync();
        Sel = Lista.FirstOrDefault(x => x.Usuario.Id == Editar);
    }

    public Task<IActionResult> OnPostCrearAsync(string? email, string? nombre, string? perfil) =>
        AccionAsync(async () => { Enlace = await usuarios.CrearAsync(email, nombre, perfil, BaseUrl); EnlacePara = email?.Trim(); },
            "Usuario creado. Se envió el enlace de activación por correo (72 horas); también puedes copiarlo abajo.", null, [Roles.Admin]);

    public Task<IActionResult> OnPostEditarAsync(string id, string? nombre, string? perfil) =>
        AccionAsync(() => usuarios.EditarAsync(id, nombre, perfil), "Usuario actualizado.", null, [Roles.Admin]);

    public Task<IActionResult> OnPostActivoAsync(string id, bool activo) =>
        AccionAsync(() => usuarios.CambiarActivoAsync(id, activo), activo ? "Usuario reactivado." : "Usuario desactivado: ya no puede ingresar.", null, [Roles.Admin]);

    public Task<IActionResult> OnPostDesbloquearAsync(string id) =>
        AccionAsync(() => usuarios.DesbloquearAsync(id), "Usuario desbloqueado.", null, [Roles.Admin]);

    public Task<IActionResult> OnPostEnlaceAsync(string id) =>
        AccionAsync(async () =>
        {
            Enlace = await usuarios.EnviarEnlaceAsync(id, BaseUrl);
            EnlacePara = (await usuarios.ListarAsync()).FirstOrDefault(x => x.Usuario.Id == id)?.Usuario.Email;
        }, "Enlace enviado por correo (72 horas, un solo uso); también puedes copiarlo abajo.", null, [Roles.Admin]);
}
