using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Infraestructura;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// Datos: SQL Server Express en producción (migraciones versionadas); SQLite para desarrollo local y pruebas.
var proveedor = cfg["Datos:Proveedor"] ?? "SqlServer";
builder.Services.AddDbContext<AppDbContext>(o =>
{
    // El filtro por área de Planilla se propaga a propósito a líneas, boletas y observaciones (navegaciones requeridas).
    o.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
    if (proveedor.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        o.UseSqlite(cfg.GetConnectionString("Default") ?? "Data Source=App_Data/honorarios.db", s => s.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
    else
        o.UseSqlServer(cfg.GetConnectionString("Default"), s => s.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
});

builder.Services.AddIdentity<Usuario, IdentityRole>(o =>
    {
        // §4: bloqueo tras 5 intentos fallidos durante 15 minutos. Sin segundo factor.
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
        // TODO(diseño): política de contraseña del portal. Mínimo provisorio: 8 caracteres con al menos un dígito.
        o.Password.RequiredLength = 8;
        o.Password.RequireDigit = true;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireLowercase = true;
        o.User.RequireUniqueEmail = false;
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<FabricaClaims>();
// Activación y recuperación: enlace de un solo uso válido 72 horas.
// Usuario desactivado o con perfil cambiado: la sesión abierta se revalida (y se cierra) en a lo más 5 minutos.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(72));
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Cuenta/Login";
    o.LogoutPath = "/Cuenta/Logout";
    o.AccessDeniedPath = "/Cuenta/AccesoDenegado";
    o.Cookie.Name = "honorarios.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.SlidingExpiration = true;
    o.ExpireTimeSpan = TimeSpan.FromHours(10);
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Politicas.Interno, p => p.RequireRole(Roles.Internos))
    .AddPolicy(Politicas.Prestador, p => p.RequireRole(Roles.Prestador));

builder.Services.AddRazorPages(o =>
{
    // Páginas internas: Operaciones, Finanzas y Admin. El portal es solo para prestadores; /Cuenta es pública.
    o.Conventions.AuthorizePage("/Index", Politicas.Interno);
    foreach (var carpeta in new[] { "/Ciclos", "/Boletas", "/Finanzas", "/Maestros" })
        o.Conventions.AuthorizeFolder(carpeta, Politicas.Interno);
    o.Conventions.AuthorizeFolder("/Portal", Politicas.Prestador);
    o.Conventions.AllowAnonymousToFolder("/Cuenta");
    o.Conventions.AllowAnonymousToPage("/Error");
    o.Conventions.AddPageApplicationModelConvention("/Cuenta/Login",
        m => m.EndpointMetadata.Add(new EnableRateLimitingAttribute("ingreso")));
    o.Conventions.AddPageApplicationModelConvention("/Cuenta/Recuperar",
        m => m.EndpointMetadata.Add(new EnableRateLimitingAttribute("ingreso")));
});

// Límite de intentos de ingreso por IP (además del bloqueo de Identity).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("ingreso", ctx => HttpMethods.IsPost(ctx.Request.Method)
        ? RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = cfg.GetValue("Publicacion:IntentosPorMinuto", 10), Window = TimeSpan.FromMinutes(1) })
        : RateLimitPartition.GetNoLimiter("get"));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Cloudflare Tunnel / IIS entregan la IP y el esquema originales.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Español sin entidades HTML (ñ, tildes).
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<HttpUsuarioActual>();
builder.Services.AddScoped<IUsuarioActual>(sp => sp.GetRequiredService<HttpUsuarioActual>());
builder.Services.Configure<OpcionesAlmacenamiento>(cfg.GetSection("Almacenamiento"));
builder.Services.Configure<OpcionesPlantillas>(cfg.GetSection("Plantillas"));
builder.Services.Configure<OpcionesPublicacion>(cfg.GetSection("Publicacion"));
builder.Services.Configure<OpcionesTurnstile>(cfg.GetSection("Turnstile"));
builder.Services.AddSingleton<Almacenamiento>();
builder.Services.AddHttpClient<Turnstile>();
builder.Services.AddScoped<Auditor>();
builder.Services.AddScoped<Correos>();
builder.Services.AddScoped<Parametros>();
builder.Services.AddScoped<CicloService>();
builder.Services.AddScoped<ExcelPlanilla>();
builder.Services.AddScoped<PlanillaService>();
builder.Services.AddScoped<CuentasService>();
builder.Services.Configure<OpcionesOcr>(cfg.GetSection("Ocr"));
builder.Services.AddSingleton<ILectorOcr, LectorOcrTesseract>();
builder.Services.AddScoped<BoletaService>();
builder.Services.AddScoped<ProduccionService>();
builder.Services.AddScoped<RevisionService>();
builder.Services.AddScoped<PlazosService>();
builder.Services.AddScoped<PagoService>();
builder.Services.AddScoped<PrestadoresService>();
builder.Services.AddScoped<UsuariosService>();
builder.Services.AddScoped<PortalService>();
builder.Services.AddScoped<ContextoLayout>();
builder.Services.AddScoped<Semilla>();
builder.Services.AddScoped<SemillaDemo>();
if (cfg.GetValue("Plazos:Habilitado", true))
    builder.Services.AddHostedService<PlazosBackgroundService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsSqlServer()) await db.Database.MigrateAsync();
    else
    {
        Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
        await db.Database.EnsureCreatedAsync();
    }
    await scope.ServiceProvider.GetRequiredService<Semilla>().EjecutarAsync();
    if (app.Environment.IsDevelopment() && cfg.GetValue("Semilla:Demo", true))
        await scope.ServiceProvider.GetRequiredService<SemillaDemo>().EjecutarAsync();
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/Error", "?codigo={0}");
app.UseMiddleware<RestriccionHostPrestadores>();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<PrestadorSoloPortal>();
app.UseAuthorization();
app.MapRazorPages();
app.Run();

public partial class Program;
