using System.Security.Claims;
using System.Text.Json;
using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IpsosPagoHonorarios.Web.Infraestructura;

public static class Politicas
{
    public const string Interno = "Interno";
    public const string Prestador = "Prestador";
}

public class OpcionesPublicacion
{
    /// <summary>Host público de los prestadores (p. ej. boletas.ejemplo.cl). Vacío = sin restricción por host.</summary>
    public string? HostPrestadores { get; set; }
}

/// <summary>Agrega el nombre completo y el prestador asociado a la identidad.</summary>
public class FabricaClaims(UserManager<Usuario> users, RoleManager<IdentityRole> roles, IOptions<IdentityOptions> op)
    : UserClaimsPrincipalFactory<Usuario, IdentityRole>(users, roles, op)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Usuario u)
    {
        var id = await base.GenerateClaimsAsync(u);
        id.AddClaim(new Claim("nombre", string.IsNullOrWhiteSpace(u.NombreCompleto) ? u.UserName ?? "" : u.NombreCompleto));
        if (u.PrestadorId is { } pid) id.AddClaim(new Claim("prestador", pid.ToString()));
        return id;
    }
}

public static class RutasPublicas
{
    public static bool EsEstatico(PathString p) =>
        p.StartsWithSegments("/css") || p.StartsWithSegments("/js") || p.StartsWithSegments("/lib") ||
        p.StartsWithSegments("/img") || p.Equals("/favicon.ico") || p.StartsWithSegments("/Error");

    public static bool EsPortalOCuenta(PathString p) => p.StartsWithSegments("/Portal") || p.StartsWithSegments("/Cuenta");
}

/// <summary>El rol Prestador solo accede a /Portal (y /Cuenta); cualquier otra ruta responde 403.</summary>
public class PrestadorSoloPortal(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var u = ctx.User;
        if (u.Identity?.IsAuthenticated == true && u.IsInRole(Roles.Prestador) && !Roles.Internos.Any(u.IsInRole))
        {
            var p = ctx.Request.Path;
            if (p == "/" ) { ctx.Response.Redirect("/Portal"); return; }
            if (!RutasPublicas.EsPortalOCuenta(p) && !RutasPublicas.EsEstatico(p))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }
        await next(ctx);
    }
}

/// <summary>En boletas.&lt;dominio&gt; solo se sirven /Portal, /Cuenta y estáticos.</summary>
public class RestriccionHostPrestadores(RequestDelegate next, IOptions<OpcionesPublicacion> op)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var host = op.Value.HostPrestadores;
        if (!string.IsNullOrWhiteSpace(host) && ctx.Request.Host.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
        {
            var p = ctx.Request.Path;
            if (p == "/") { ctx.Response.Redirect("/Portal"); return; }
            if (!RutasPublicas.EsPortalOCuenta(p) && !RutasPublicas.EsEstatico(p))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }
        await next(ctx);
    }
}

public class OpcionesTurnstile
{
    /// <summary>Cloudflare Turnstile (opcional). Sin claves, no se usa.</summary>
    public string? SiteKey { get; set; }
    public string? SecretKey { get; set; }
}

/// <summary>Verificación opcional de Cloudflare Turnstile en el ingreso.</summary>
public class Turnstile(HttpClient http, IOptions<OpcionesTurnstile> op)
{
    public bool Habilitado => !string.IsNullOrWhiteSpace(op.Value.SiteKey) && !string.IsNullOrWhiteSpace(op.Value.SecretKey);
    public string? SiteKey => op.Value.SiteKey;

    public async Task<bool> VerificarAsync(string? token, string? ip)
    {
        if (!Habilitado) return true;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var resp = await http.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["secret"] = op.Value.SecretKey!, ["response"] = token, ["remoteip"] = ip ?? ""
        }));
        if (!resp.IsSuccessStatusCode) return false;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
    }
}
