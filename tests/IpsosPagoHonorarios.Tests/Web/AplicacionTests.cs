using System.Net;
using System.Text.RegularExpressions;
using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IpsosPagoHonorarios.Tests.Web;

/// <summary>La aplicación completa con los datos de demo, sobre SQLite en un archivo temporal.</summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    public string Carpeta { get; } = Path.Combine(Path.GetTempPath(), "honorarios-web-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Carpeta);
        builder.UseEnvironment("Development");
        builder.UseSetting("Datos:Proveedor", "Sqlite");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(Carpeta, "web.db")}");
        builder.UseSetting("Almacenamiento:Ruta", Path.Combine(Carpeta, "archivos"));
        builder.UseSetting("Plazos:Habilitado", "false");
        builder.UseSetting("Publicacion:HostPrestadores", "boletas.ejemplo.cl");
        builder.UseSetting("Publicacion:IntentosPorMinuto", "1000");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(Carpeta, true); } catch { /* temporal */ }
    }
}

public class AplicacionTests(AppFactory app) : IClassFixture<AppFactory>
{
    private HttpClient Cliente(string? host = null)
    {
        var c = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        if (host is not null) c.DefaultRequestHeaders.Host = host;
        return c;
    }

    private static async Task<string> TokenAsync(HttpClient c, string url)
    {
        var html = await c.GetStringAsync(url);
        return Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> IngresarAsync(HttpClient c, string usuario, string clave = SemillaDemo.Contrasena) =>
        await c.PostAsync("/Cuenta/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Usuario"] = usuario, ["Contrasena"] = clave, ["__RequestVerificationToken"] = await TokenAsync(c, "/Cuenta/Login")
        }));

    /// <summary>Acceso denegado: 403 directo o redirección a la página de acceso denegado (que responde 403).</summary>
    private static void Denegado(HttpResponseMessage r) =>
        Assert.True(r.StatusCode == HttpStatusCode.Forbidden ||
                    (r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location!.OriginalString.Contains("/Cuenta/AccesoDenegado")), $"{r.StatusCode} {r.Headers.Location}");

    [Fact]
    public async Task Anonimo_EsRedirigidoAlIngreso()
    {
        var r = await Cliente().GetAsync("/Finanzas/Pagos");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("/Cuenta/Login", r.Headers.Location!.PathAndQuery);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Ciclos/Planilla")]
    [InlineData("/Boletas/Seguimiento")]
    [InlineData("/Finanzas/Revision")]
    [InlineData("/Maestros/Prestadores")]
    [InlineData("/Boletas/Seguimiento?handler=Pdf&boletaId=1")]
    public async Task R17_Prestador_SoloAccedeAlPortal_ElRestoEs403(string ruta)
    {
        var c = Cliente();
        var login = await IngresarAsync(c, "17.345.120-2");
        Assert.Equal("/Portal", login.Headers.Location!.OriginalString);
        var r = await c.GetAsync(ruta);
        if (ruta == "/") Assert.Equal("/Portal", r.Headers.Location!.OriginalString);
        else Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task R17_PortalMuestraSoloSusFilas_YNoSubeEnPlanillaAjena()
    {
        var c = Cliente();
        await IngresarAsync(c, "17.345.120-2");
        var html = await c.GetStringAsync("/Portal");
        Assert.Contains("Valentina Muñoz Soto", html);
        Assert.Contains("$180.000", html);
        Assert.Contains("$152.550", html);
        Assert.DoesNotContain("Camila Rojas", html);
        Assert.DoesNotContain("15.234.871-1", html);

        // Intenta subir una boleta en la planilla DATA PROCESSING, donde no tiene filas.
        int ajena;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ajena = await db.Planillas.Where(p => p.Area.Nombre == "DATA PROCESSING" && p.Ciclo.Codigo == "OCT-2026").Select(p => p.Id).SingleAsync();
        }
        var form = new MultipartFormDataContent
        {
            { new StringContent(ajena.ToString()), "planillaId" },
            { new StringContent(await TokenAsync(c, "/Portal")), "__RequestVerificationToken" },
            { new ByteArrayContent(Entorno.Pdf(new Prestador { NombreCompleto = "Valentina Muñoz Soto", Rut = 17345120, Dv = "2" }, "999", 180000)), "pdf", "b.pdf" }
        };
        var r = await c.PostAsync("/Portal?handler=Subir", form);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var despues = await c.GetStringAsync("/Portal");
        Assert.Contains("No tienes filas en esta planilla", despues);
        using var s2 = app.Services.CreateScope();
        Assert.False(await s2.ServiceProvider.GetRequiredService<AppDbContext>().Boletas.AnyAsync(b => b.NumeroBoleta == "999"));
    }

    [Fact]
    public async Task R16_AccesoAlPortalQuedaEnBitacora_YBloqueoTrasIntentos()
    {
        var c = Cliente();
        await IngresarAsync(c, "17.345.120-2");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.Auditorias.AnyAsync(a => a.Accion == "Acceso al portal" && a.EntidadId == "17345120-2"));
        }
        var otro = Cliente();
        string html = "";
        for (var i = 0; i < 5; i++) html = await (await IngresarAsync(otro, "13.987.654-7", "incorrecta1")).Content.ReadAsStringAsync();
        Assert.Contains("bloqueada por 15 minutos", html);
    }

    [Fact]
    public async Task Interno_PorRol()
    {
        var ops = Cliente();
        await IngresarAsync(ops, "andres.paredes@ejemplo.cl");
        Assert.Equal(HttpStatusCode.OK, (await ops.GetAsync("/Ciclos/ValidacionCuentas")).StatusCode);
        Denegado(await ops.GetAsync("/Finanzas/Revision"));
        Denegado(await ops.GetAsync("/Finanzas/Pagos"));
        Denegado(await ops.GetAsync("/Portal"));
        Assert.Equal(HttpStatusCode.Forbidden, (await ops.GetAsync("/Cuenta/AccesoDenegado")).StatusCode);
        var fin = Cliente();
        await IngresarAsync(fin, "carolina.diaz@ejemplo.cl");
        foreach (var ruta in new[] { "/", "/Ciclos/Produccion", "/Ciclos/ValidacionCuentas", "/Ciclos/Planilla", "/Boletas/Seguimiento", "/Ciclos/Correcciones",
                     "/Finanzas/Revision", "/Finanzas/Pagos", "/Maestros/Prestadores", "/Maestros/Jobs", "/Ciclos/Historial" })
            Assert.Equal(HttpStatusCode.OK, (await fin.GetAsync(ruta)).StatusCode);
        var xlsx = await fin.GetAsync("/Ciclos/Planilla?handler=Xlsx&id=1");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task HostDePrestadores_SoloPortalCuentaYEstaticos()
    {
        var c = Cliente("boletas.ejemplo.cl");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/Ciclos/Planilla")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/Finanzas/Pagos")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/Cuenta/Login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/css/site.css")).StatusCode);
        Assert.Equal("/Portal", (await c.GetAsync("/")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task R20_CuentaDelPortal_UsuarioRut_CorreoOTelefono_EnlaceDeUnSoloUso()
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var prestadores = sp.GetRequiredService<PrestadoresService>();
        var ex = await Assert.ThrowsAsync<ReglaException>(() => prestadores.GuardarAsync(null, "Sin Contacto", "12.345.678-5", null, " "));
        Assert.Contains("al menos uno", ex.Message);
        await Assert.ThrowsAsync<ReglaException>(() => prestadores.GuardarAsync(null, "RUT malo", "12.345.678-9", "a@b.cl", null));

        var p = await prestadores.GuardarAsync(null, "Persona Ficticia", "12.345.678-5", null, "+56 9 0000 0000");
        var enlace = await prestadores.InvitarAsync(p.Id, "https://boletas.ejemplo.cl", soloCopiar: true);
        Assert.StartsWith("https://boletas.ejemplo.cl/Cuenta/Activar?u=12345678-5&t=", enlace);
        var db = sp.GetRequiredService<AppDbContext>();
        var u = await db.Users.SingleAsync(x => x.UserName == "12345678-5");
        Assert.Equal(p.Id, u.PrestadorId);
        Assert.False(await db.Correos.AnyAsync(c => c.Cuerpo.Contains("12345678-5")));   // sin correo: lo entrega Operaciones
        Assert.Contains(await db.Auditorias.ToListAsync(), a => a.Accion == "Copiar enlace de activación");

        var token = Uri.UnescapeDataString(enlace.Split("&t=")[1]);
        Assert.True((await prestadores.DefinirContrasenaAsync("12345678-5", token, "clave1234", activacion: true)).Succeeded);
        Assert.False((await prestadores.DefinirContrasenaAsync("12345678-5", token, "otra12345", activacion: true)).Succeeded); // un solo uso
        var c = Cliente();
        Assert.Equal("/Portal", (await IngresarAsync(c, "12.345.678-5", "clave1234")).Headers.Location!.OriginalString);
    }
}
