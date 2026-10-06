using System.Text;
using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Data;

/// <summary>Semilla de catálogos (desde CSV), parámetros, roles y usuario administrador inicial.</summary>
public class Semilla(AppDbContext db, RoleManager<IdentityRole> roles, UserManager<Usuario> users, IHostEnvironment env, IConfiguration config, ILogger<Semilla> log)
{
    public async Task EjecutarAsync()
    {
        var carpeta = Path.Combine(env.ContentRootPath, "Data", "Semilla");
        await CargarAsync(Path.Combine(carpeta, "areas.csv"), db.Areas, f => new Area { Nombre = f[0], CodigoArea = f[1] }, a => a.Nombre);
        await CargarAsync(Path.Combine(carpeta, "glosas.csv"), db.Glosas, f => new Glosa { NombreGlosa = f[0], Item = f[1], CuentaContable = f[2] }, g => g.NombreGlosa);
        await CargarAsync(Path.Combine(carpeta, "bancos.csv"), db.Bancos, f => new Banco { Nombre = f[0], CodigoBanco = f[1] }, b => b.Nombre);
        await CargarAsync(Path.Combine(carpeta, "tipos_cuenta.csv"), db.TiposCuenta, f => new TipoCuenta { Nombre = f[0], Codigo = f[1] }, t => t.Nombre);
        await CargarAsync(Path.Combine(carpeta, "tipos_gasto.csv"), db.TiposGasto, f => new TipoGasto { Nombre = f[0] }, t => t.Nombre);

        if (!await db.Parametros.AnyAsync()) db.Parametros.Add(new Parametro());
        // Bases creadas con el RUT ficticio inicial: se corrige al RUT real de la empresa receptora.
        foreach (var p in await db.Parametros.Where(p => p.RutEmpresa == "77777777-7").ToListAsync())
        {
            var real = new Parametro();
            p.RutEmpresa = real.RutEmpresa;
            p.RazonSocialEmpresa = real.RazonSocialEmpresa;
        }
        if (!await db.TasasRetencion.AnyAsync()) db.TasasRetencion.Add(new TasaRetencion { Anio = 2026, Tasa = 0.1525m });
        await db.SaveChangesAsync();

        foreach (var r in Roles.Todos)
            if (!await roles.RoleExistsAsync(r)) await roles.CreateAsync(new IdentityRole(r));

        var adminEmail = config["Semilla:AdminEmail"];
        var adminPass = config["Semilla:AdminPassword"];
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPass) && await users.FindByEmailAsync(adminEmail) is null)
        {
            var u = new Usuario { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true, NombreCompleto = "Administrador", LockoutEnabled = true };
            var res = await users.CreateAsync(u, adminPass);
            if (res.Succeeded)
            {
                await users.AddToRoleAsync(u, Roles.Admin);
                log.LogInformation("Administrador creado: {Email}. Ingrese con ese correo y la contraseña de Semilla:AdminPassword.", adminEmail);
            }
            else
                log.LogWarning("No se creó el administrador: {E} Corrija Semilla:AdminPassword (mínimo 8 caracteres, una minúscula y un número) y reinicie la aplicación.",
                    string.Join(" ", res.Errors.Select(e => e.Description)));
        }
        else if ((string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPass)) && (await users.GetUsersInRoleAsync(Roles.Admin)).Count == 0)
            log.LogWarning("No hay ningún usuario administrador y no están configurados Semilla:AdminEmail y Semilla:AdminPassword: nadie podrá ingresar. " +
                           "Defínalos en appsettings.Production.json y reinicie la aplicación.");
    }

    private async Task CargarAsync<T>(string ruta, DbSet<T> set, Func<string[], T> crear, Func<T, string> clave) where T : class
    {
        if (!File.Exists(ruta)) { log.LogWarning("No existe la semilla {Ruta}", ruta); return; }
        // Solo en una tabla vacía: si ya hay catálogo (script SQL o mantención en Maestros) no se toca, para no devolver
        // entradas que se renombraron o se borraron a propósito.
        if (await set.AnyAsync()) return;
        var existentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lineas = await File.ReadAllLinesAsync(ruta, Encoding.UTF8);
        foreach (var l in lineas.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var f = l.Split(';').Select(x => x.Trim()).ToArray();
            var e = crear(f.Concat(Enumerable.Repeat("", 3)).ToArray());
            if (existentes.Add(clave(e))) set.Add(e);
        }
    }
}
