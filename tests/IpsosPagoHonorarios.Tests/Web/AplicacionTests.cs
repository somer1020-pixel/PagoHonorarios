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

        // Revisión de Finanzas: descarga en Excel de la planilla enviada.
        Assert.Contains("Descargar planilla enviada", await fin.GetStringAsync("/Finanzas/Revision?planilla=1"));
        var enviada = await fin.GetAsync("/Finanzas/Revision?handler=Xlsx&planilla=1");
        Assert.Equal(HttpStatusCode.OK, enviada.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", enviada.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith(".xlsx", enviada.Content.Headers.ContentDisposition!.FileNameStar ?? enviada.Content.Headers.ContentDisposition.FileName!.Trim('"'));
        using var libro = new ClosedXML.Excel.XLWorkbook(new MemoryStream(await enviada.Content.ReadAsByteArrayAsync()));
        Assert.NotEmpty(libro.Worksheets);
        Denegado(await ops.GetAsync("/Finanzas/Revision?handler=Xlsx&planilla=1"));
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

    [Fact]
    public async Task UsuariosInternos_SoloAdministrador_CicloDeVida()
    {
        var fin = Cliente();
        await IngresarAsync(fin, "carolina.diaz@ejemplo.cl");
        Denegado(await fin.GetAsync("/Maestros/Usuarios"));
        Assert.DoesNotContain("/Maestros/Usuarios", await fin.GetStringAsync("/Maestros/Jobs"));

        var adm = Cliente();
        await IngresarAsync(adm, "admin@ejemplo.cl");
        var html = await adm.GetStringAsync("/Maestros/Usuarios");
        Assert.Contains("carolina.diaz@ejemplo.cl", html);
        Assert.DoesNotContain("17345120-2", html);   // los prestadores no se listan

        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var usuarios = sp.GetRequiredService<UsuariosService>();
        var prestadores = sp.GetRequiredService<PrestadoresService>();
        var db = sp.GetRequiredService<AppDbContext>();

        await Assert.ThrowsAsync<ReglaException>(() => usuarios.CrearAsync("no-es-correo", "X", Roles.Finanzas, null, "https://h"));
        await Assert.ThrowsAsync<ReglaException>(() => usuarios.CrearAsync("nuevo@ejemplo.cl", "X", Roles.Prestador, null, "https://h"));
        await Assert.ThrowsAsync<ReglaException>(() => usuarios.CrearAsync("carolina.diaz@ejemplo.cl", "X", Roles.Finanzas, null, "https://h"));

        var enlace = await usuarios.CrearAsync("nuevo@ejemplo.cl", "Usuario Nuevo", Roles.Finanzas, null, "https://h");
        Assert.StartsWith("https://h/Cuenta/Activar?u=nuevo%40ejemplo.cl&t=", enlace);
        Assert.Contains(await db.Correos.ToListAsync(), c => c.Para == "nuevo@ejemplo.cl" && c.Cuerpo.Contains(enlace));
        var nuevo = (await usuarios.ListarAsync()).Single(x => x.Usuario.Email == "nuevo@ejemplo.cl");
        Assert.False(nuevo.Activado);
        Assert.Equal(Roles.Finanzas, nuevo.Perfil);

        var token = Uri.UnescapeDataString(enlace.Split("&t=")[1]);
        Assert.True((await prestadores.DefinirContrasenaAsync("nuevo@ejemplo.cl", token, "clave1234", activacion: true)).Succeeded);
        var c = Cliente();
        Assert.Equal("/", (await IngresarAsync(c, "nuevo@ejemplo.cl", "clave1234")).Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/Finanzas/Revision")).StatusCode);

        var f2f = await db.Areas.SingleAsync(a => a.Nombre == "FACE TO FACE");
        Assert.Contains("al menos un área", (await Assert.ThrowsAsync<ReglaException>(() => usuarios.EditarAsync(nuevo.Usuario.Id, "X", Roles.Operaciones, null))).Message);
        await usuarios.EditarAsync(nuevo.Usuario.Id, "Usuario Nuevo Editado", Roles.Operaciones, [f2f.Id]);
        var editado = (await usuarios.ListarAsync()).Single(x => x.Usuario.Email == "nuevo@ejemplo.cl");
        Assert.Equal((Roles.Operaciones, "Usuario Nuevo Editado"), (editado.Perfil, editado.Usuario.NombreCompleto));
        Assert.True(editado.Activado);
        Assert.NotNull(editado.UltimoIngreso);

        await usuarios.CambiarActivoAsync(nuevo.Usuario.Id, false);
        Assert.Contains("incorrectos", await (await IngresarAsync(Cliente(), "nuevo@ejemplo.cl", "clave1234")).Content.ReadAsStringAsync());
        await Assert.ThrowsAsync<ReglaException>(() => usuarios.EnviarEnlaceAsync(nuevo.Usuario.Id, "https://h"));
        await usuarios.CambiarActivoAsync(nuevo.Usuario.Id, true);
        Assert.StartsWith("https://h/Cuenta/Recuperar?", await usuarios.EnviarEnlaceAsync(nuevo.Usuario.Id, "https://h"));

        // Resguardos: no se deja el sistema sin Administrador activo; los prestadores no se administran aquí.
        var admin = (await usuarios.ListarAsync()).Single(x => x.Usuario.Email == "admin@ejemplo.cl");
        Assert.Contains("único Administrador", (await Assert.ThrowsAsync<ReglaException>(() => usuarios.CambiarActivoAsync(admin.Usuario.Id, false))).Message);
        Assert.Contains("único Administrador", (await Assert.ThrowsAsync<ReglaException>(() => usuarios.EditarAsync(admin.Usuario.Id, "Admin", Roles.Finanzas, null))).Message);
        var prestador = await db.Users.FirstAsync(u => u.PrestadorId != null);
        await Assert.ThrowsAsync<ReglaException>(() => usuarios.CambiarActivoAsync(prestador.Id, false));

        // Por la página: el Administrador no puede desactivarse a sí mismo (aunque exista otro Administrador).
        await usuarios.EditarAsync(nuevo.Usuario.Id, "Usuario Nuevo", Roles.Admin, null);
        var r = await adm.PostAsync("/Maestros/Usuarios?handler=Activo", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = admin.Usuario.Id, ["activo"] = "false", ["__RequestVerificationToken"] = await TokenAsync(adm, "/Maestros/Usuarios")
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("No puedes desactivar tu propio usuario", await adm.GetStringAsync("/Maestros/Usuarios"));
        Assert.True((await db.Users.AsNoTracking().SingleAsync(u => u.Id == admin.Usuario.Id)).Activo);
        Assert.Contains(await db.Auditorias.ToListAsync(), a => a.Accion == "Cambiar perfil" && a.EntidadId == "nuevo@ejemplo.cl");
    }

    [Theory]
    [InlineData(Roles.CEX)] [InlineData(Roles.Public)] [InlineData(Roles.BHT)] [InlineData(Roles.MSU)] [InlineData(Roles.AUM)]
    public async Task PerfilesComoOperaciones_MismasPantallasYPermisos(string perfil)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var usuarios = sp.GetRequiredService<UsuariosService>();
        var email = $"{perfil.ToLowerInvariant()}@ejemplo.cl";
        var areaId = (await sp.GetRequiredService<AppDbContext>().Areas.SingleAsync(a => a.Nombre == "DATA PROCESSING")).Id;
        await Assert.ThrowsAsync<ReglaException>(() => usuarios.CrearAsync(email, $"Usuario {perfil}", perfil, null, "https://h"));   // requiere área
        var enlace = await usuarios.CrearAsync(email, $"Usuario {perfil}", perfil, [areaId], "https://h");
        var token = Uri.UnescapeDataString(enlace.Split("&t=")[1]);
        Assert.True((await sp.GetRequiredService<PrestadoresService>().DefinirContrasenaAsync(email, token, "clave1234", activacion: true)).Succeeded);
        Assert.Equal(perfil, (await usuarios.ListarAsync()).Single(x => x.Usuario.Email == email).Perfil);

        var u = await sp.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Usuario>>().FindByEmailAsync(email);
        var principal = await sp.GetRequiredService<Microsoft.AspNetCore.Identity.IUserClaimsPrincipalFactory<Usuario>>().CreateAsync(u!);
        Assert.True(principal.IsInRole(perfil));
        Assert.True(principal.IsInRole(Roles.Operaciones));   // toda regla de Operaciones aplica
        Assert.False(principal.IsInRole(Roles.Finanzas));

        var c = Cliente();
        Assert.Equal("/", (await IngresarAsync(c, email, "clave1234")).Headers.Location!.OriginalString);
        foreach (var ruta in new[] { "/", "/Ciclos/Produccion", "/Ciclos/ValidacionCuentas", "/Ciclos/Planilla", "/Boletas/Seguimiento", "/Ciclos/Correcciones",
                     "/Maestros/Prestadores", "/Maestros/Jobs", "/Ciclos/Historial" })
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(ruta)).StatusCode);
        Denegado(await c.GetAsync("/Finanzas/Revision"));
        Denegado(await c.GetAsync("/Finanzas/Pagos"));
        Denegado(await c.GetAsync("/Maestros/Usuarios"));
        Assert.Contains($"<span class=\"rol-top\">{perfil}</span>", await c.GetStringAsync("/"));   // el encabezado muestra el perfil específico
    }

    [Fact]
    public async Task Operaciones_SoloVeLasPlanillasDeSusAreas()
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var oct = await db.Ciclos.SingleAsync(c => c.Codigo == "OCT-2026");
        var planillas = await db.Planillas.Include(p => p.Area).Where(p => p.CicloId == oct.Id).ToDictionaryAsync(p => p.Area.Nombre);
        var f2f = planillas["FACE TO FACE"];
        var dp = planillas["DATA PROCESSING"];
        var boletaDp = await db.Boletas.FirstAsync(b => b.PlanillaId == dp.Id);

        var andres = Cliente();   // Operaciones · FACE TO FACE
        await IngresarAsync(andres, "andres.paredes@ejemplo.cl");
        var panel = await andres.GetStringAsync("/?ciclo=OCT-2026");
        Assert.Contains("FACE TO FACE", panel);
        Assert.DoesNotContain("DATA PROCESSING", panel);
        Assert.DoesNotContain("MYSTERY SHOPPING", panel);
        Assert.Contains("Tu actividad reciente", panel);
        Assert.Contains("FACE TO FACE", await andres.GetStringAsync($"/Ciclos/Planilla?id={f2f.Id}"));
        Assert.DoesNotContain("DATA PROCESSING", await andres.GetStringAsync($"/Ciclos/Planilla?id={dp.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await andres.GetAsync($"/Ciclos/Planilla?handler=Xlsx&id={dp.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await andres.GetAsync($"/Ciclos/Planilla?handler=Xlsx&id={f2f.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await andres.GetAsync($"/Boletas/Seguimiento?handler=Pdf&boletaId={boletaDp.Id}")).StatusCode);
        Assert.DoesNotContain("DATA PROCESSING", await andres.GetStringAsync($"/Boletas/Seguimiento?planilla={dp.Id}"));
        Assert.DoesNotContain("<option value=\"" + dp.AreaId + "\"", await andres.GetStringAsync("/Ciclos/Produccion"));
        Denegado(await andres.GetAsync($"/Ciclos/Historial?handler=Zip&cicloId={oct.Id}"));

        var felipe = Cliente();   // Operaciones · DATA PROCESSING y Operations CATI
        await IngresarAsync(felipe, "felipe.araya@ejemplo.cl");
        var panelFelipe = await felipe.GetStringAsync("/?ciclo=OCT-2026");
        Assert.Contains("DATA PROCESSING", panelFelipe);
        Assert.DoesNotContain("FACE TO FACE", panelFelipe);

        var fin = Cliente();      // Finanzas ve todas
        await IngresarAsync(fin, "carolina.diaz@ejemplo.cl");
        var panelFin = await fin.GetStringAsync("/?ciclo=OCT-2026");
        Assert.Contains("FACE TO FACE", panelFin);
        Assert.Contains("DATA PROCESSING", panelFin);
        Assert.Contains("MYSTERY SHOPPING", panelFin);

        // Servicios con la identidad de Andrés: la planilla de otra área no existe para él y no puede cargar producción en ella.
        var users = sp.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Usuario>>();
        var principal = await sp.GetRequiredService<Microsoft.AspNetCore.Identity.IUserClaimsPrincipalFactory<Usuario>>()
            .CreateAsync((await users.FindByEmailAsync("andres.paredes@ejemplo.cl"))!);
        Assert.Equal([f2f.AreaId], Alcance.Areas(principal));
        using var scope2 = app.Services.CreateScope();
        scope2.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext =
            new Microsoft.AspNetCore.Http.DefaultHttpContext { User = principal, RequestServices = scope2.ServiceProvider };
        var ciclos = scope2.ServiceProvider.GetRequiredService<CicloService>();
        Assert.Null(await ciclos.PlanillaCompletaAsync(dp.Id));
        Assert.NotNull(await ciclos.PlanillaCompletaAsync(f2f.Id));
        Assert.NotNull(await ciclos.PlanillaCompletaAsync(dp.Id, todasLasAreas: true));
        var r = await scope2.ServiceProvider.GetRequiredService<ProduccionService>().ImportarAsync(new SolicitudImportacion(oct.Id, dp.AreaId, "Costo Directo",
            "Andrés Paredes", "andres.paredes@ejemplo.cl", TipoArchivoProduccion.Exportacion, "x.csv", "a;b"u8.ToArray()));
        Assert.False(r.Cargada);
        Assert.Contains(r.Errores, e => e.Campo == "Área" && e.Motivo.Contains("No tienes asignada"));
    }
}
