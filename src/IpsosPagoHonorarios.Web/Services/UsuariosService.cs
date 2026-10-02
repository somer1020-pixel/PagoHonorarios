using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Services;

/// <summary>
/// Usuarios internos (Operaciones, Finanzas, Administrador): solo el Administrador los mantiene. El usuario es el correo;
/// la contraseña la define la propia persona con un enlace de un solo uso (72 h), nunca el Administrador.
/// </summary>
public class UsuariosService(AppDbContext db, UserManager<Usuario> users, Auditor auditor, Correos correos, IUsuarioActual actual)
{
    public static readonly string[] Perfiles = Roles.Internos;

    public static string NombrePerfil(string perfil) => perfil == Roles.Admin ? "Administrador" : perfil;

    public sealed record UsuarioInterno(Usuario Usuario, string Perfil, bool Bloqueado, bool Activado, DateTime? UltimoIngreso, List<Area> Areas)
    {
        /// <summary>Operaciones y equivalentes ven solo sus áreas; Finanzas y Administrador ven todas.</summary>
        public bool PorArea => PerfilPorArea(Perfil);
    }

    public static bool PerfilPorArea(string? perfil) => perfil == Roles.Operaciones || Roles.ComoOperaciones.Contains(perfil);

    public async Task<List<UsuarioInterno>> ListarAsync()
    {
        var internos = await (from ur in db.UserRoles
                              join r in db.Roles on ur.RoleId equals r.Id
                              where Perfiles.Contains(r.Name!)
                              select new { ur.UserId, Rol = r.Name! }).ToListAsync();
        var perfilDe = internos.GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => Enumerable.Reverse(Perfiles).First(p => g.Any(x => x.Rol == p)));   // Admin pesa más si tuviera varios
        var ids = perfilDe.Keys.ToList();
        var lista = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync();
        var nombres = lista.Select(u => u.UserName).ToList();
        var ingresos = (await db.Auditorias.AsNoTracking()
                .Where(a => a.Entidad == "Usuario" && a.Accion == "Ingreso" && nombres.Contains(a.EntidadId))
                .GroupBy(a => a.EntidadId).Select(g => new { g.Key, Fecha = g.Max(a => a.Fecha) }).ToListAsync())
            .ToDictionary(x => x.Key!, x => (DateTime?)x.Fecha);
        var areas = (await db.UsuarioAreas.AsNoTracking().Include(x => x.Area).Where(x => ids.Contains(x.UsuarioId)).ToListAsync())
            .ToLookup(x => x.UsuarioId, x => x.Area);
        var ahora = DateTimeOffset.UtcNow;
        return lista
            .Select(u => new UsuarioInterno(u, perfilDe[u.Id], u.LockoutEnd > ahora, u.PasswordHash is not null, ingresos.GetValueOrDefault(u.UserName!),
                areas[u.Id].OrderBy(a => a.Nombre).ToList()))
            .OrderBy(x => !x.Usuario.Activo).ThenBy(x => Array.IndexOf(Perfiles, x.Perfil)).ThenBy(x => x.Usuario.NombreCompleto)
            .ToList();
    }

    /// <summary>Crea el usuario sin contraseña y devuelve el enlace de activación (también se envía por correo).</summary>
    public async Task<string> CrearAsync(string? email, string? nombre, string? perfil, IEnumerable<int>? areaIds, string baseUrl)
    {
        email = email?.Trim() ?? "";
        nombre = nombre?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(nombre)) throw new ReglaException("El nombre es obligatorio.");
        if (!System.Net.Mail.MailAddress.TryCreate(email, out var m) || m.Address != email) throw new ReglaException("El correo no es válido.");
        ExigirPerfil(perfil);
        var areas = await AreasValidasAsync(perfil!, areaIds);
        var normal = users.NormalizeEmail(email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normal || u.NormalizedUserName == users.NormalizeName(email)))
            throw new ReglaException("Ya existe un usuario (interno o prestador) con ese correo.");
        var u = new Usuario { UserName = email, Email = email, EmailConfirmed = true, NombreCompleto = nombre, LockoutEnabled = true };
        var r = await users.CreateAsync(u);
        if (!r.Succeeded) throw new ReglaException(string.Join(" ", r.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(u, perfil!);
        foreach (var a in areas) db.UsuarioAreas.Add(new UsuarioArea { UsuarioId = u.Id, AreaId = a.Id });
        var link = await EnlaceAsync(u, baseUrl, "Activar");
        correos.Encolar(email, "Tu acceso a Pago de Honorarios",
            $"Hola {nombre}: se creó tu usuario ({email}) con perfil {NombrePerfil(perfil!)}. Define tu contraseña en {link} (enlace de un solo uso, válido por 72 horas).");
        auditor.Registrar("Usuario", email, "Crear usuario", $"{nombre} · {NombrePerfil(perfil!)}{TextoAreas(areas)}");
        await db.SaveChangesAsync();
        return link;
    }

    public async Task EditarAsync(string id, string? nombre, string? perfil, IEnumerable<int>? areaIds)
    {
        var u = await InternoAsync(id);
        nombre = nombre?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(nombre)) throw new ReglaException("El nombre es obligatorio.");
        ExigirPerfil(perfil);
        var areas = await AreasValidasAsync(perfil!, areaIds);
        var antes = await PerfilAsync(u);
        if (antes == Roles.Admin && perfil != Roles.Admin) await ExigirOtroAdminAsync(u, "quitar el perfil Administrador");   // antes de cualquier cambio
        var actuales = await db.UsuarioAreas.Include(x => x.Area).Where(x => x.UsuarioId == u.Id).ToListAsync();
        if (!actuales.Select(x => x.AreaId).Order().SequenceEqual(areas.Select(a => a.Id).Order()))
        {
            db.UsuarioAreas.RemoveRange(actuales);
            foreach (var a in areas) db.UsuarioAreas.Add(new UsuarioArea { UsuarioId = u.Id, AreaId = a.Id });
            await users.UpdateSecurityStampAsync(u);   // la sesión abierta toma las nuevas áreas
            auditor.Registrar("Usuario", u.UserName, "Cambiar áreas",
                $"{(actuales.Count == 0 ? "ninguna" : string.Join(", ", actuales.Select(x => x.Area.Nombre).Order()))} → {(areas.Count == 0 ? "todas (perfil sin alcance por área)" : string.Join(", ", areas.Select(a => a.Nombre)))}");
        }
        if (antes != perfil)
        {
            await users.RemoveFromRolesAsync(u, (await users.GetRolesAsync(u)).Where(Perfiles.Contains));
            await users.AddToRoleAsync(u, perfil!);
            await users.UpdateSecurityStampAsync(u);   // la sesión abierta toma el nuevo perfil (o se cierra)
            auditor.Registrar("Usuario", u.UserName, "Cambiar perfil", $"{NombrePerfil(antes)} → {NombrePerfil(perfil!)}");
        }
        if (u.NombreCompleto != nombre)
        {
            auditor.Registrar("Usuario", u.UserName, "Editar usuario", $"{u.NombreCompleto} → {nombre}");
            u.NombreCompleto = nombre;
            await users.UpdateAsync(u);
        }
        await db.SaveChangesAsync();
    }

    public async Task CambiarActivoAsync(string id, bool activo)
    {
        var u = await InternoAsync(id);
        if (u.Activo == activo) return;
        if (!activo && await PerfilAsync(u) == Roles.Admin) await ExigirOtroAdminAsync(u, "desactivar");
        u.Activo = activo;
        await users.UpdateAsync(u);
        await users.UpdateSecurityStampAsync(u);   // cierra las sesiones abiertas
        auditor.Registrar("Usuario", u.UserName, activo ? "Reactivar usuario" : "Desactivar usuario", null);
        await db.SaveChangesAsync();
    }

    public async Task DesbloquearAsync(string id)
    {
        var u = await InternoAsync(id);
        await users.SetLockoutEndDateAsync(u, null);
        await users.ResetAccessFailedCountAsync(u);
        auditor.Registrar("Usuario", u.UserName, "Desbloquear usuario", null);
        await db.SaveChangesAsync();
    }

    /// <summary>Reenvía el enlace: de activación si aún no define contraseña; si no, de restablecimiento.</summary>
    public async Task<string> EnviarEnlaceAsync(string id, string baseUrl)
    {
        var u = await InternoAsync(id);
        if (!u.Activo) throw new ReglaException("El usuario está desactivado: reactívalo primero.");
        var activar = u.PasswordHash is null;
        var link = await EnlaceAsync(u, baseUrl, activar ? "Activar" : "Recuperar");
        correos.Encolar(u.Email, activar ? "Tu acceso a Pago de Honorarios" : "Restablece tu contraseña",
            $"Hola {u.NombreCompleto}: {(activar ? "define tu contraseña" : "define una nueva contraseña")} en {link} (enlace de un solo uso, válido por 72 horas).");
        auditor.Registrar("Usuario", u.UserName, activar ? "Reenviar activación" : "Restablecer contraseña", u.Email);
        await db.SaveChangesAsync();
        return link;
    }

    private async Task<string> EnlaceAsync(Usuario u, string baseUrl, string pagina) =>
        $"{baseUrl.TrimEnd('/')}/Cuenta/{pagina}?u={Uri.EscapeDataString(u.UserName!)}&t={Uri.EscapeDataString(await users.GeneratePasswordResetTokenAsync(u))}";

    /// <summary>Operaciones y equivalentes requieren al menos un área; Finanzas y Administrador no llevan áreas (ven todas).</summary>
    private async Task<List<Area>> AreasValidasAsync(string perfil, IEnumerable<int>? areaIds)
    {
        if (!PerfilPorArea(perfil)) return [];
        var ids = (areaIds ?? []).Distinct().ToList();
        var areas = await db.Areas.Where(a => ids.Contains(a.Id)).OrderBy(a => a.Nombre).ToListAsync();
        if (areas.Count == 0) throw new ReglaException($"Asigna al menos un área: el perfil {NombrePerfil(perfil)} solo ve las planillas de sus áreas.");
        return areas;
    }

    private static string TextoAreas(List<Area> areas) => areas.Count == 0 ? "" : " · áreas: " + string.Join(", ", areas.Select(a => a.Nombre));

    private static void ExigirPerfil(string? perfil)
    {
        if (perfil is null || !Perfiles.Contains(perfil)) throw new ReglaException($"Elige un perfil: {string.Join(", ", Perfiles.Select(NombrePerfil))}.");
    }

    private async Task<Usuario> InternoAsync(string id)
    {
        var u = await users.FindByIdAsync(id ?? "") ?? throw new ReglaException("Usuario no encontrado.");
        if (await PerfilAsync(u) is null) throw new ReglaException("Solo se administran aquí los usuarios internos; los prestadores se gestionan en Maestros / Prestadores.");
        return u;
    }

    private async Task<string?> PerfilAsync(Usuario u)
    {
        var roles = await users.GetRolesAsync(u);
        return Enumerable.Reverse(Perfiles).FirstOrDefault(roles.Contains);
    }

    /// <summary>Nadie puede quitarse a sí mismo el perfil Administrador ni dejar el sistema sin un Administrador activo.</summary>
    private async Task ExigirOtroAdminAsync(Usuario u, string accion)
    {
        if (actual.Principal is { } p && users.GetUserId(p) == u.Id) throw new ReglaException($"No puedes {accion} tu propio usuario.");
        var otros = (await users.GetUsersInRoleAsync(Roles.Admin)).Count(x => x.Id != u.Id && x.Activo);
        if (otros == 0) throw new ReglaException($"No se puede {accion}: es el único Administrador activo.");
    }
}
