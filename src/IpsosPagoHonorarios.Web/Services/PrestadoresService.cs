using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Services;

/// <summary>Mantención de prestadores y de su acceso al portal (R-20).</summary>
public class PrestadoresService(AppDbContext db, UserManager<Usuario> users, CicloService ciclos, Auditor auditor, Correos correos)
{
    public async Task<Prestador> GuardarAsync(int? id, string nombre, string rut, string? email, string? telefono)
    {
        if (string.IsNullOrWhiteSpace(nombre)) throw new ReglaException("El nombre es obligatorio.");
        if (!RutHelper.TryParse(rut, out var cuerpo, out var dv)) throw new ReglaException($"RUT inválido: {RutHelper.Error(rut)}.");
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        telefono = string.IsNullOrWhiteSpace(telefono) ? null : telefono.Trim();
        if (email is null && telefono is null) throw new ReglaException("Ingresa correo o teléfono (al menos uno).");
        if (email is not null && !System.Net.Mail.MailAddress.TryCreate(email, out _)) throw new ReglaException("El correo no es válido.");
        if (await db.Prestadores.AnyAsync(p => p.Rut == cuerpo && p.Id != id)) throw new ReglaException("Ya existe un prestador con ese RUT.");

        Prestador p;
        if (id is null)
        {
            p = new Prestador { Rut = cuerpo, Dv = dv };
            db.Prestadores.Add(p);
        }
        else
        {
            p = await db.Prestadores.FirstOrDefaultAsync(x => x.Id == id) ?? throw new ReglaException("Prestador no encontrado.");
            if (p.Rut != cuerpo && p.UsuarioId is not null) throw new ReglaException("El RUT es el usuario del portal: no se puede cambiar con el acceso creado.");
            p.Rut = cuerpo;
            p.Dv = dv;
        }
        p.NombreCompleto = nombre.Trim();
        p.Email = email;
        p.Telefono = telefono;
        if (p.UsuarioId is not null && await users.FindByIdAsync(p.UsuarioId) is { } u && u.Email != email)
        {
            u.Email = email;
            await users.UpdateAsync(u);
        }
        auditor.Registrar(nameof(Prestador), p.RutPlanilla, id is null ? "Crear prestador" : "Editar prestador", p.NombreCompleto);
        await db.SaveChangesAsync();
        return p;
    }

    /// <summary>R-20: usuario = RUT, solo contraseña. Crea la cuenta del portal si no existe.</summary>
    public async Task<Usuario> AsegurarUsuarioAsync(Prestador p)
    {
        if (p.Email is null && p.Telefono is null) throw new ReglaException("El prestador necesita correo o teléfono (al menos uno).");
        var u = p.UsuarioId is null ? null : await users.FindByIdAsync(p.UsuarioId);
        if (u is not null) return u;
        u = await users.FindByNameAsync(p.RutPlanilla);
        if (u is null)
        {
            u = new Usuario { UserName = p.RutPlanilla, Email = p.Email, NombreCompleto = p.NombreCompleto, PrestadorId = p.Id, EmailConfirmed = p.Email is not null, LockoutEnabled = true };
            var r = await users.CreateAsync(u);
            if (!r.Succeeded) throw new ReglaException(string.Join(" ", r.Errors.Select(e => e.Description)));
            await users.AddToRoleAsync(u, Roles.Prestador);
        }
        p.UsuarioId = u.Id;
        return u;
    }

    /// <summary>Enlace de un solo uso, válido 72 horas (token de restablecimiento de Identity).</summary>
    private async Task<string> EnlaceAsync(Usuario u, string baseUrl, string pagina) =>
        $"{baseUrl.TrimEnd('/')}/Cuenta/{pagina}?u={Uri.EscapeDataString(u.UserName!)}&t={Uri.EscapeDataString(await users.GeneratePasswordResetTokenAsync(u))}";

    /// <summary>Invita al portal: correo con enlace de activación; sin correo, Operaciones copia el enlace y lo entrega.</summary>
    public async Task<string> InvitarAsync(int prestadorId, string baseUrl, bool soloCopiar = false)
    {
        var p = await db.Prestadores.FirstOrDefaultAsync(x => x.Id == prestadorId) ?? throw new ReglaException("Prestador no encontrado.");
        var u = await AsegurarUsuarioAsync(p);
        var link = await EnlaceAsync(u, baseUrl, "Activar");
        p.PortalInvitadoEn ??= ciclos.AhoraUtc;
        if (!soloCopiar)
            correos.Encolar(p.Email, "Activa tu acceso al portal de boletas",
                $"Hola {p.NombreCompleto}: tu usuario es tu RUT ({p.RutPlanilla}). Activa tu cuenta en {link} (enlace de un solo uso, válido por 72 horas).");
        auditor.Registrar(nameof(Prestador), p.RutPlanilla, soloCopiar ? "Copiar enlace de activación" : "Invitar al portal",
            p.Email is null ? "Sin correo: enlace entregado por Operaciones" : p.Email);
        await db.SaveChangesAsync();
        return link;
    }

    /// <summary>Restablece la contraseña: correo con enlace; sin correo, se devuelve el enlace para entregarlo por su canal.</summary>
    public async Task<string> RestablecerAsync(int prestadorId, string baseUrl)
    {
        var p = await db.Prestadores.FirstOrDefaultAsync(x => x.Id == prestadorId) ?? throw new ReglaException("Prestador no encontrado.");
        var u = await AsegurarUsuarioAsync(p);
        var link = await EnlaceAsync(u, baseUrl, "Recuperar");
        correos.Encolar(p.Email, "Restablece tu contraseña", $"Hola {p.NombreCompleto}: define una nueva contraseña en {link} (válido por 72 horas).");
        auditor.Registrar(nameof(Prestador), p.RutPlanilla, "Restablecer contraseña", p.Email ?? "Sin correo: enlace entregado por Operaciones");
        await db.SaveChangesAsync();
        return link;
    }

    /// <summary>Recuperación iniciada por el usuario: solo por correo, sin SMS.</summary>
    public async Task SolicitarRecuperacionAsync(string correoORut, string baseUrl)
    {
        var u = await BuscarUsuarioAsync(correoORut);
        if (u?.Email is null) return; // No revela si existe; sin correo debe contactar a Operaciones.
        var link = await EnlaceAsync(u, baseUrl, "Recuperar");
        correos.Encolar(u.Email, "Recupera tu contraseña", $"Hola {u.NombreCompleto}: define una nueva contraseña en {link} (válido por 72 horas).");
        auditor.Registrar("Usuario", u.UserName, "Solicitar recuperación", null);
        await db.SaveChangesAsync();
    }

    public async Task<Usuario?> BuscarUsuarioAsync(string correoORut)
    {
        correoORut = correoORut.Trim();
        if (RutHelper.TryParse(correoORut, out var c, out var d)) return await users.FindByNameAsync($"{c}-{d}");
        return await users.FindByEmailAsync(correoORut) ?? await users.FindByNameAsync(correoORut);
    }

    /// <summary>Activación o recuperación con el enlace de un solo uso.</summary>
    public async Task<IdentityResult> DefinirContrasenaAsync(string userName, string token, string contrasena, bool activacion)
    {
        var u = await users.FindByNameAsync(userName);
        if (u is null) return IdentityResult.Failed(new IdentityError { Description = "Enlace inválido o vencido." });
        var r = await users.ResetPasswordAsync(u, token, contrasena);
        if (r.Succeeded)
        {
            await users.SetLockoutEndDateAsync(u, null);
            await users.ResetAccessFailedCountAsync(u);
            if (u.PrestadorId is { } pid && await db.Prestadores.FindAsync(pid) is { } p && activacion)
                p.PortalActivadoEn ??= ciclos.AhoraUtc;
            auditor.Registrar("Usuario", u.UserName, activacion ? "Activar cuenta" : "Recuperar contraseña", null);
            await db.SaveChangesAsync();
        }
        return r;
    }
}

/// <summary>R-17: el prestador ve solo sus filas. El filtro se aplica aquí, no en la vista.</summary>
public class PortalService(AppDbContext db, Parametros parametros)
{
    public sealed record PlanillaPortal(Planilla Planilla, List<LineaPago> Lineas, BoletaHonorarios? Boleta, decimal Bruto, decimal Retencion,
        decimal Liquido, Verificacion PuedeSubir, List<Observacion> Observaciones, Devolucion? Devolucion);

    public async Task<List<PlanillaPortal>> MisPlanillasAsync(int prestadorId)
    {
        var planillas = await db.Planillas.Include(p => p.Ciclo).Include(p => p.Area).Include(p => p.Devoluciones)
            .Include(p => p.Lineas.Where(l => l.PrestadorId == prestadorId)).ThenInclude(l => l.Job)
            .Include(p => p.Lineas.Where(l => l.PrestadorId == prestadorId)).ThenInclude(l => l.Glosa)
            .Include(p => p.Boletas.Where(b => b.PrestadorId == prestadorId))
            .Where(p => p.Lineas.Any(l => l.PrestadorId == prestadorId) && p.Ciclo.Estado == CicloEstado.Abierto)
            .AsNoTracking().AsSplitQuery().ToListAsync();
        var res = new List<PlanillaPortal>();
        foreach (var p in planillas.OrderByDescending(p => p.Ciclo.Periodo).ThenBy(p => p.Area.Nombre).ThenBy(p => p.Numero))
        {
            // R-17: aun si la consulta trajera más filas, aquí solo quedan las del prestador.
            p.Lineas = p.Lineas.Where(l => l.PrestadorId == prestadorId).ToList();
            p.Boletas = p.Boletas.Where(b => b.PrestadorId == prestadorId).ToList();
            var activas = p.Lineas.Where(l => l.Estado != LineaEstado.Diferida).OrderBy(l => l.Numero).ToList();
            if (activas.Count == 0) continue;
            var tasa = await parametros.TasaAsync(p.Ciclo.Periodo.Year);
            var (bruto, ret, liq) = Montos.PorBoleta(activas.Select(l => l.ValorTotalBruto), tasa);
            var ids = activas.Select(l => l.Id).ToList();
            var obs = await db.Observaciones.Where(o => ids.Contains(o.LineaPagoId) && o.Estado == ObservacionEstado.Abierta).ToListAsync();
            var dev = p.Devoluciones.Where(d => d.Resultado == DevolucionResultado.Pendiente).MaxBy(d => d.Id);
            res.Add(new(p, activas, p.BoletaVigente(prestadorId), bruto, ret, liq, Flujo.PuedeSubirBoleta(p, prestadorId), obs, obs.Count > 0 ? dev : null));
        }
        return res;
    }

    public sealed record PagoAnterior(string Ciclo, string? NumeroBoleta, DateOnly Fecha, decimal Bruto, decimal Liquido);

    public async Task<List<PagoAnterior>> MisPagosAsync(int prestadorId)
    {
        var t = await db.Transferencias.Include(x => x.Boleta).Include(x => x.Planilla).ThenInclude(p => p.Ciclo)
            .Where(x => x.PrestadorId == prestadorId).ToListAsync();
        return t.OrderByDescending(x => x.Fecha)
            .Select(x => new PagoAnterior(x.Planilla.Ciclo.Codigo, x.Boleta.NumeroBoleta, x.Fecha, x.Boleta.MontoBruto ?? 0, x.MontoLiquido)).ToList();
    }
}
