using System.Net;
using System.Text.Json;
using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static IpsosPagoHonorarios.Tests.Entorno;

namespace IpsosPagoHonorarios.Tests.Integracion;

/// <summary>Avisos por WhatsApp: teléfonos, reglas de encolado, reintentos y contrato con la API de Meta.</summary>
public class WhatsAppTests
{
    private static readonly DateOnly Limite = new(2026, 11, 10);

    [Theory]
    [InlineData("+56 9 1234 5678", "56912345678")]
    [InlineData("9 1234 5678", "56912345678")]
    [InlineData("56912345678", "56912345678")]
    [InlineData("+569-1234-5678", "56912345678")]
    [InlineData("+54 9 11 2345 6789", "5491123456789")]   // otro país: solo con "+"
    [InlineData("22 123 4567", null)]                       // fijo chileno: no tiene WhatsApp
    [InlineData("+56 22 123 4567", null)]
    [InlineData("12345", null)]
    [InlineData("+1 234", null)]
    [InlineData("abc", null)]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void Telefono_NormalizaParaWhatsApp(string? entrada, string? esperado) =>
        Assert.Equal(esperado, Telefono.NormalizarWhatsApp(entrada));

    [Fact]
    public void Telefono_FormateaCelularChileno() => Assert.Equal("+56 9 1234 5678", Telefono.Formatear("56912345678"));

    private static Entorno Activado()
    {
        var e = new Entorno();
        e.WhatsApp.Habilitado = true;
        return e;
    }

    private static Prestador Autorizado(Entorno e, string telefono = "+56 9 1234 5678", bool autoriza = true)
    {
        var p = e.Prestador("Francisca Silva Morales", "13987654-7", "CORRIENTE", "0068123456", "SANTANDER");
        p.Telefono = telefono;
        p.WhatsAppAutorizado = autoriza;
        e.Db.SaveChanges();
        return p;
    }

    [Fact]
    public async Task Encolar_ExigeCanalActivo_Autorizacion_YCelularValido()
    {
        using var e = new Entorno();   // apagado
        var p = Autorizado(e);
        await e.Correos.EncolarSolicitudBoletaAsync(p, "OCT-2026", 331500m, Limite);
        await e.Db.SaveChangesAsync();
        Assert.Empty(await e.Db.WhatsApp.ToListAsync());                       // canal desactivado

        e.WhatsApp.Habilitado = true;
        p.WhatsAppAutorizado = false;
        await e.Correos.EncolarSolicitudBoletaAsync(p, "OCT-2026", 331500m, Limite);
        await e.Db.SaveChangesAsync();
        Assert.Empty(await e.Db.WhatsApp.ToListAsync());                       // sin consentimiento

        p.WhatsAppAutorizado = true;
        p.Telefono = "22 123 4567";
        await e.Correos.EncolarSolicitudBoletaAsync(p, "OCT-2026", 331500m, Limite);
        await e.Db.SaveChangesAsync();
        Assert.Empty(await e.Db.WhatsApp.ToListAsync());                       // teléfono fijo

        p.Telefono = "9 1234 5678";
        await e.Correos.EncolarSolicitudBoletaAsync(p, "OCT-2026", 331500m, Limite);
        await e.Db.SaveChangesAsync();
        var m = await e.Db.WhatsApp.SingleAsync();
        Assert.Equal("56912345678", m.Para);
        Assert.Equal("solicitud_boleta", m.Plantilla);
        Assert.Equal(p.Id, m.PrestadorId);
        Assert.Equal([p.NombreCompleto, "OCT-2026", Formato.Clp(331500m), "10-11-2026"], Json.Leer<List<string>>(m.Parametros));
        Assert.Null(m.EnviadoEn);
    }

    [Fact]
    public async Task Encolar_NoRepiteElMismoAvisoEn24Horas()
    {
        using var e = Activado();
        var p = Autorizado(e);
        await e.Correos.EncolarRecordatorioBoletaAsync(p, "OCT-2026", Limite);
        await e.Correos.EncolarRecordatorioBoletaAsync(p, "OCT-2026", Limite);   // antes de guardar: ya está en el contexto
        await e.Db.SaveChangesAsync();
        Assert.Single(await e.Db.WhatsApp.ToListAsync());

        await e.Correos.EncolarRecordatorioBoletaAsync(p, "OCT-2026", Limite);   // ya guardado
        await e.Db.SaveChangesAsync();
        Assert.Single(await e.Db.WhatsApp.ToListAsync());

        await e.Correos.EncolarRecordatorioBoletaAsync(p, "OCT-2026", Limite.AddDays(1));   // otro dato: es otro aviso
        await e.Db.SaveChangesAsync();
        Assert.Equal(2, await e.Db.WhatsApp.CountAsync());

        e.Reloj.Advance(TimeSpan.FromHours(25));
        await e.Correos.EncolarRecordatorioBoletaAsync(p, "OCT-2026", Limite);
        await e.Db.SaveChangesAsync();
        Assert.Equal(3, await e.Db.WhatsApp.CountAsync());
    }

    [Fact]
    public async Task Encolar_LimpiaLasVariables()
    {
        using var e = Activado();
        var p = Autorizado(e);
        await e.Correos.EncolarBoletaObservadaAsync(p, "Línea 1:\n\n  monto   distinto\t$2.000 " + new string('x', 300), "a la brevedad");
        await e.Correos.EncolarBoletaObservadaAsync(p, "   ", "a la brevedad");
        await e.Db.SaveChangesAsync();
        var todos = (await e.Db.WhatsApp.OrderBy(m => m.Id).ToListAsync()).Select(m => Json.Leer<List<string>>(m.Parametros)!).ToList();
        Assert.StartsWith("Línea 1: monto distinto $2.000 xxx", todos[0][1]);
        Assert.DoesNotContain('\n', todos[0][1]);
        Assert.Equal(200, todos[0][1].Length);
        Assert.EndsWith("...", todos[0][1]);
        Assert.Equal("-", todos[1][1]);   // WhatsApp rechaza variables vacías
    }

    private sealed class ProveedorFalso : IProveedorWhatsApp
    {
        public List<(string Para, string Plantilla, IReadOnlyList<string> Parametros)> Enviados { get; } = [];
        public Queue<ResultadoEnvio> Respuestas { get; } = new();

        public Task<ResultadoEnvio> EnviarPlantillaAsync(string para, string plantilla, IReadOnlyList<string> parametros, CancellationToken ct = default)
        {
            Enviados.Add((para, plantilla, parametros));
            return Task.FromResult(Respuestas.Count > 0 ? Respuestas.Dequeue() : new ResultadoEnvio(true, "wamid.OK", null, false));
        }
    }

    private static EnviadorWhatsApp Enviador(Entorno e, ProveedorFalso p) =>
        new(e.Db, p, Options.Create(e.WhatsApp), e.Reloj, NullLogger<EnviadorWhatsApp>.Instance);

    private static async Task EncolarUnoAsync(Entorno e)
    {
        await e.Correos.EncolarSolicitudBoletaAsync(Autorizado(e), "OCT-2026", 331500m, Limite);
        await e.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Enviador_EnviaUnaVez_YRegistraElIdDeWhatsApp()
    {
        using var e = Activado();
        await EncolarUnoAsync(e);
        var prov = new ProveedorFalso();
        var env = Enviador(e, prov);
        Assert.Equal(1, await env.EnviarPendientesAsync());
        Assert.Equal(0, await env.EnviarPendientesAsync());
        var (para, plantilla, parametros) = Assert.Single(prov.Enviados);
        Assert.Equal(("56912345678", "solicitud_boleta", 4), (para, plantilla, parametros.Count));
        var m = await e.Db.WhatsApp.SingleAsync();
        Assert.NotNull(m.EnviadoEn);
        Assert.Equal(("wamid.OK", 1, null), (m.ProveedorId, m.Intentos, m.Error));
    }

    [Fact]
    public async Task Enviador_ReintentaFallasTransitorias_ConEspera()
    {
        using var e = Activado();
        await EncolarUnoAsync(e);
        var prov = new ProveedorFalso();
        prov.Respuestas.Enqueue(new(false, null, "HTTP 503", Reintentable: true));
        var env = Enviador(e, prov);

        Assert.Equal(0, await env.EnviarPendientesAsync());
        Assert.Equal("HTTP 503", (await e.Db.WhatsApp.SingleAsync()).Error);
        Assert.Equal(0, await env.EnviarPendientesAsync());          // todavía no pasan los 2 minutos
        Assert.Single(prov.Enviados);

        e.Reloj.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(1, await env.EnviarPendientesAsync());
        var m = await e.Db.WhatsApp.SingleAsync();
        Assert.NotNull(m.EnviadoEn);
        Assert.Null(m.Error);
        Assert.Equal(2, m.Intentos);
    }

    [Fact]
    public async Task Enviador_ConErrorDefinitivo_NoInsiste()
    {
        using var e = Activado();
        await EncolarUnoAsync(e);
        var prov = new ProveedorFalso();
        prov.Respuestas.Enqueue(new(false, null, "HTTP 404 (código 132001): plantilla inexistente", Reintentable: false));
        var env = Enviador(e, prov);

        await env.EnviarPendientesAsync();
        e.Reloj.Advance(TimeSpan.FromHours(1));
        await env.EnviarPendientesAsync();
        Assert.Single(prov.Enviados);
        var m = await e.Db.WhatsApp.SingleAsync();
        Assert.Null(m.EnviadoEn);
        Assert.Contains("132001", m.Error);
        Assert.Equal(e.WhatsApp.MaxIntentos, m.Intentos);
    }

    [Fact]
    public async Task Enviador_SeDetieneAlLlegarAlMaximoDeIntentos()
    {
        using var e = Activado();
        await EncolarUnoAsync(e);
        var prov = new ProveedorFalso();
        for (var i = 0; i < 5; i++) prov.Respuestas.Enqueue(new(false, null, "sin red", Reintentable: true));
        var env = Enviador(e, prov);
        for (var i = 0; i < 6; i++)
        {
            await env.EnviarPendientesAsync();
            e.Reloj.Advance(TimeSpan.FromMinutes(15));
        }
        Assert.Equal(e.WhatsApp.MaxIntentos, prov.Enviados.Count);
    }

    [Fact]
    public async Task Enviador_ConElCanalApagado_NoEnvia()
    {
        using var e = Activado();
        await EncolarUnoAsync(e);
        e.WhatsApp.Habilitado = false;
        var prov = new ProveedorFalso();
        Assert.Equal(0, await Enviador(e, prov).EnviarPendientesAsync());
        Assert.Empty(prov.Enviados);
    }

    // ---- Contrato HTTP con la API Cloud de Meta ----

    private sealed class Manejador(Func<HttpResponseMessage> responder) : HttpMessageHandler
    {
        public string? Url, Autorizacion, Cuerpo;
        public int Llamadas;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Llamadas++;
            Url = r.RequestUri?.ToString();
            Autorizacion = r.Headers.Authorization?.ToString();
            Cuerpo = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
            return responder();
        }
    }

    private static HttpResponseMessage Json_(HttpStatusCode codigo, string json) =>
        new(codigo) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static ProveedorWhatsAppMeta Meta(Manejador m, Action<OpcionesWhatsApp>? ajustar = null)
    {
        var o = new OpcionesWhatsApp { PhoneNumberId = "123456", AccessToken = "tok-secreto" };
        ajustar?.Invoke(o);
        return new(new HttpClient(m), Options.Create(o));
    }

    [Fact]
    public async Task Meta_EnviaLaPlantillaConElFormatoDeLaApiCloud()
    {
        var m = new Manejador(() => Json_(HttpStatusCode.OK, """{"messaging_product":"whatsapp","messages":[{"id":"wamid.ABC123"}]}"""));
        var r = await Meta(m).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana", "OCT-2026", "$100.000", "10-11-2026"]);

        Assert.True(r.Ok);
        Assert.Equal("wamid.ABC123", r.ProveedorId);
        Assert.Equal("https://graph.facebook.com/v21.0/123456/messages", m.Url);
        Assert.Equal("Bearer tok-secreto", m.Autorizacion);
        using var d = JsonDocument.Parse(m.Cuerpo!);
        var raiz = d.RootElement;
        Assert.Equal("whatsapp", raiz.GetProperty("messaging_product").GetString());
        Assert.Equal("56912345678", raiz.GetProperty("to").GetString());
        Assert.Equal("template", raiz.GetProperty("type").GetString());
        var t = raiz.GetProperty("template");
        Assert.Equal("solicitud_boleta", t.GetProperty("name").GetString());
        Assert.Equal("es", t.GetProperty("language").GetProperty("code").GetString());
        var cuerpo = t.GetProperty("components")[0];
        Assert.Equal("body", cuerpo.GetProperty("type").GetString());
        Assert.Equal(["Ana", "OCT-2026", "$100.000", "10-11-2026"],
            cuerpo.GetProperty("parameters").EnumerateArray().Select(x => x.GetProperty("text").GetString()!).ToArray());
        Assert.All(cuerpo.GetProperty("parameters").EnumerateArray(), x => Assert.Equal("text", x.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task Meta_ErrorDeLaApi_SeExplicaYNoSeReintenta()
    {
        var m = new Manejador(() => Json_(HttpStatusCode.NotFound,
            """{"error":{"message":"Template name does not exist in the translation","type":"OAuthException","code":132001,"error_data":{"details":"template solicitud_boleta not found"}}}"""));
        var r = await Meta(m).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana"]);
        Assert.False(r.Ok);
        Assert.False(r.Reintentable);
        Assert.Contains("132001", r.Error);
        Assert.Contains("Template name does not exist", r.Error);
        Assert.Contains("solicitud_boleta not found", r.Error);
        Assert.DoesNotContain("tok-secreto", r.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task Meta_SoloReintentaLoTransitorio(HttpStatusCode codigo, bool reintentable)
    {
        var r = await Meta(new Manejador(() => Json_(codigo, "no es json"))).EnviarPlantillaAsync("56912345678", "x", []);
        Assert.False(r.Ok);
        Assert.Equal(reintentable, r.Reintentable);
        Assert.Equal($"HTTP {(int)codigo}", r.Error);
    }

    [Fact]
    public async Task Meta_SinConexion_EsReintentable()
    {
        var m = new Manejador(() => throw new HttpRequestException("sin red"));
        var r = await Meta(m).EnviarPlantillaAsync("56912345678", "x", ["a"]);
        Assert.False(r.Ok);
        Assert.True(r.Reintentable);
        Assert.Contains("sin red", r.Error);
    }

    [Fact]
    public async Task Meta_SinCredenciales_NoLlamaALaApi()
    {
        var m = new Manejador(() => Json_(HttpStatusCode.OK, "{}"));
        var r = await Meta(m, o => o.AccessToken = null).EnviarPlantillaAsync("56912345678", "x", ["a"]);
        Assert.False(r.Ok);
        Assert.False(r.Reintentable);
        Assert.Contains("no está configurado", r.Error);
        Assert.Equal(0, m.Llamadas);
    }

    // ---- Contrato HTTP con Twilio ----

    private static ProveedorWhatsAppTwilio Twilio(Manejador m, Action<OpcionesTwilio>? ajustar = null)
    {
        var t = new OpcionesTwilio
        {
            AccountSid = "AC123", AuthToken = "secreto-twilio", From = "+56 9 8888 7777",
            ContentSids = { ["solicitud_boleta"] = "HXabc123" }
        };
        ajustar?.Invoke(t);
        return new(new HttpClient(m), Options.Create(new OpcionesWhatsApp { Proveedor = "Twilio", Twilio = t }));
    }

    [Fact]
    public async Task Twilio_EnviaLaPlantillaPorContentSidConVariablesNumeradas()
    {
        var m = new Manejador(() => Json_(HttpStatusCode.Created, """{"sid":"SM9f8e7d","status":"queued"}"""));
        var r = await Twilio(m).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana", "OCT-2026", "$100.000", "10-11-2026"]);

        Assert.True(r.Ok);
        Assert.Equal("SM9f8e7d", r.ProveedorId);
        Assert.Equal("https://api.twilio.com/2010-04-01/Accounts/AC123/Messages.json", m.Url);
        Assert.Equal("Basic " + Convert.ToBase64String("AC123:secreto-twilio"u8.ToArray()), m.Autorizacion);
        var f = System.Web.HttpUtility.ParseQueryString(m.Cuerpo!);
        Assert.Equal("whatsapp:+56912345678", f["To"]);
        Assert.Equal("whatsapp:+56988887777", f["From"]);
        Assert.Equal("HXabc123", f["ContentSid"]);
        Assert.Null(f["MessagingServiceSid"]);
        Assert.Null(f["Body"]);   // una plantilla no lleva texto libre
        using var vars = JsonDocument.Parse(f["ContentVariables"]!);
        Assert.Equal(["1", "2", "3", "4"], vars.RootElement.EnumerateObject().Select(x => x.Name).ToArray());
        Assert.Equal(["Ana", "OCT-2026", "$100.000", "10-11-2026"], vars.RootElement.EnumerateObject().Select(x => x.Value.GetString()!).ToArray());
    }

    [Fact]
    public async Task Twilio_ConMessagingService_UsaEseRemitente()
    {
        var m = new Manejador(() => Json_(HttpStatusCode.Created, """{"sid":"SM1"}"""));
        await Twilio(m, t => { t.From = null; t.MessagingServiceSid = "MGabc"; }).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana"]);
        var f = System.Web.HttpUtility.ParseQueryString(m.Cuerpo!);
        Assert.Equal("MGabc", f["MessagingServiceSid"]);
        Assert.Null(f["From"]);
    }

    [Theory]
    [InlineData("+56 9 8888 7777", "whatsapp:+56988887777")]
    [InlineData("56988887777", "whatsapp:+56988887777")]
    [InlineData("whatsapp:+14155238886", "whatsapp:+14155238886")]
    public async Task Twilio_NormalizaElNumeroDeOrigen(string from, string esperado)
    {
        var m = new Manejador(() => Json_(HttpStatusCode.Created, """{"sid":"SM1"}"""));
        await Twilio(m, t => t.From = from).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana"]);
        Assert.Equal(esperado, System.Web.HttpUtility.ParseQueryString(m.Cuerpo!)["From"]);
    }

    [Fact]
    public async Task Twilio_ErrorDeLaApi_SeExplicaYNoSeReintenta()
    {
        var m = new Manejador(() => Json_(HttpStatusCode.BadRequest,
            """{"code":21211,"message":"The 'To' number whatsapp:+5691 is not a valid phone number.","more_info":"https://www.twilio.com/docs/errors/21211","status":400}"""));
        var r = await Twilio(m).EnviarPlantillaAsync("5691", "solicitud_boleta", ["Ana"]);
        Assert.False(r.Ok);
        Assert.False(r.Reintentable);
        Assert.Contains("21211", r.Error);
        Assert.Contains("not a valid phone number", r.Error);
        Assert.DoesNotContain("secreto-twilio", r.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task Twilio_SoloReintentaLoTransitorio(HttpStatusCode codigo, bool reintentable)
    {
        var r = await Twilio(new Manejador(() => Json_(codigo, "no es json"))).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana"]);
        Assert.False(r.Ok);
        Assert.Equal(reintentable, r.Reintentable);
        Assert.Equal($"HTTP {(int)codigo}", r.Error);
    }

    [Fact]
    public async Task Twilio_SinConexion_EsReintentable()
    {
        var r = await Twilio(new Manejador(() => throw new HttpRequestException("sin red"))).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana"]);
        Assert.True(r.Reintentable);
        Assert.Contains("Twilio", r.Error);
    }

    [Fact]
    public async Task Twilio_PlantillaSinContentSid_NoLlamaALaApi()
    {
        var m = new Manejador(() => Json_(HttpStatusCode.Created, "{}"));
        var r = await Twilio(m).EnviarPlantillaAsync("56912345678", "recordatorio_boleta", ["Ana"]);
        Assert.False(r.Ok);
        Assert.False(r.Reintentable);
        Assert.Contains("recordatorio_boleta", r.Error);
        Assert.Contains("Content SID", r.Error);
        Assert.Equal(0, m.Llamadas);
    }

    [Fact]
    public async Task Twilio_SinCredenciales_NoLlamaALaApi()
    {
        var m = new Manejador(() => Json_(HttpStatusCode.Created, "{}"));
        var r = await Twilio(m, t => t.AuthToken = null).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana"]);
        Assert.False(r.Ok);
        Assert.False(r.Reintentable);
        Assert.Contains("Twilio no está configurado", r.Error);
        Assert.Equal(0, m.Llamadas);
    }

    [Fact]
    public void Opciones_ElProveedorDefineQuePideLaConfiguracion()
    {
        var o = new OpcionesWhatsApp { PhoneNumberId = "1", AccessToken = "t" };
        Assert.True(o.Configurado);                       // Meta por defecto
        o.Proveedor = "twilio";                           // sin distinguir mayúsculas
        Assert.True(o.UsaTwilio);
        Assert.False(o.Configurado);                      // las credenciales de Meta no sirven para Twilio
        o.Twilio = new OpcionesTwilio { AccountSid = "AC1", AuthToken = "x", From = "+56912345678" };
        Assert.True(o.Configurado);
        o.Proveedor = "twilo";
        Assert.False(o.ProveedorValido);
    }

    // ---- Contrato HTTP con InstaPulse (manual de integración de eventos y sistemas de pago) ----

    private sealed class ManejadorInstaPulse(Func<string, string?, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(string Metodo, string Url, string? Usuario, string? Cuerpo)> Llamadas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var cuerpo = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
            Llamadas.Add((r.Method.Method, r.RequestUri!.ToString(), r.Headers.TryGetValues("x-remote-user", out var v) ? v.First() : null, cuerpo));
            return responder(r.RequestUri.AbsolutePath, cuerpo);
        }
    }

    /// <summary>Respuestas correctas de los 4 pasos, como en el manual.</summary>
    private static HttpResponseMessage RespuestaOk(string ruta) => ruta switch
    {
        "/InstaPulseAPI2/participantes" => Json_(HttpStatusCode.OK, """{"success":true,"phone":"+56912345678","name":"Ana","project_id":"P","usuario":"IAOps","pauta_id":null}"""),
        "/InstaPulseAPI2/casos" => Json_(HttpStatusCode.OK, """{"caso_id":"b1a45749-0fa7","message":"Caso creado exitosamente."}"""),
        var r when r.EndsWith("/sessions") => Json_(HttpStatusCode.OK, """{"session_id":"a9315d0e-561b","message":"Sesión creada y lista para ser iniciada."}"""),
        _ => Json_(HttpStatusCode.OK, """{"success":true,"session_id":"a9315d0e-561b","channel":"META","message_id":"wamid.HBgN123","status":"sent","mode":"marketing"}""")
    };

    private static ProveedorWhatsAppInstaPulse InstaPulse(ManejadorInstaPulse m, Action<OpcionesInstaPulse>? ajustar = null)
    {
        var ip = new OpcionesInstaPulse
        {
            UrlBase = "https://servidor-ipsos.latam/InstaPulseAPI2", UsuarioRemoto = "IAOps",
            Pautas = { ["solicitud_boleta"] = 28, ["recordatorio_boleta"] = 29 }
        };
        ajustar?.Invoke(ip);
        return new(new HttpClient(m), Options.Create(new OpcionesWhatsApp { Proveedor = "InstaPulse", InstaPulse = ip }));
    }

    [Fact]
    public async Task InstaPulse_RecorreLosCuatroPasosDelManual()
    {
        var m = new ManejadorInstaPulse((ruta, _) => RespuestaOk(ruta));
        var r = await InstaPulse(m).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana Pérez", "OCT-2026", "$100.000", "10-11-2026"]);

        Assert.True(r.Ok);
        Assert.Equal("wamid.HBgN123", r.ProveedorId);
        Assert.Equal(
        [
            "https://servidor-ipsos.latam/InstaPulseAPI2/participantes",
            "https://servidor-ipsos.latam/InstaPulseAPI2/casos",
            "https://servidor-ipsos.latam/InstaPulseAPI2/casos/b1a45749-0fa7/sessions",
            "https://servidor-ipsos.latam/InstaPulseAPI2/sessions/a9315d0e-561b/start/meta"
        ], m.Llamadas.Select(l => l.Url).ToArray());
        Assert.All(m.Llamadas, l => { Assert.Equal("POST", l.Metodo); Assert.Equal("IAOps", l.Usuario); });

        using var paso1 = JsonDocument.Parse(m.Llamadas[0].Cuerpo!);
        Assert.Equal("+56912345678", paso1.RootElement.GetProperty("phone").GetString());
        Assert.Equal("Ana Pérez", paso1.RootElement.GetProperty("name").GetString());
        Assert.Equal("es", paso1.RootElement.GetProperty("language").GetString());
        var meta = paso1.RootElement.GetProperty("metadata");
        Assert.Equal("PagoHonorarios", meta.GetProperty("origen").GetString());
        Assert.Equal(["Ana Pérez", "OCT-2026", "$100.000", "10-11-2026"], Enumerable.Range(1, 4).Select(i => meta.GetProperty($"p{i}").GetString()!).ToArray());

        using var paso2 = JsonDocument.Parse(m.Llamadas[1].Cuerpo!);
        Assert.Equal("+56912345678", paso2.RootElement.GetProperty("phone_number").GetString());
        Assert.Contains("solicitud_boleta", paso2.RootElement.GetProperty("titulo").GetString());

        using var paso3 = JsonDocument.Parse(m.Llamadas[2].Cuerpo!);
        Assert.Equal(28, paso3.RootElement.GetProperty("pauta_id").GetInt32());
        Assert.False(paso3.RootElement.GetProperty("incluir_en_historial_global").GetBoolean());

        using var paso4 = JsonDocument.Parse(m.Llamadas[3].Cuerpo!);
        Assert.Equal(["Ana Pérez", "OCT-2026", "$100.000", "10-11-2026"],
            paso4.RootElement.GetProperty("body_params").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal("es", paso4.RootElement.GetProperty("language_code").GetString());
        Assert.True(paso4.RootElement.GetProperty("mark_active").GetBoolean());
        Assert.Equal(JsonValueKind.Null, paso4.RootElement.GetProperty("mode").ValueKind);   // null = InstaPulse detecta la ventana de 24 h
    }

    [Fact]
    public async Task InstaPulse_UsaLaPautaDeCadaPlantilla_YAceptaUrlConBarraFinal()
    {
        var m = new ManejadorInstaPulse((ruta, _) => RespuestaOk(ruta));
        await InstaPulse(m, ip => ip.UrlBase = "https://servidor-ipsos.latam/InstaPulseAPI2/").EnviarPlantillaAsync("56912345678", "recordatorio_boleta", ["Ana", "OCT-2026", "10-11-2026"]);
        Assert.Equal("https://servidor-ipsos.latam/InstaPulseAPI2/participantes", m.Llamadas[0].Url);
        using var paso3 = JsonDocument.Parse(m.Llamadas[2].Cuerpo!);
        Assert.Equal(29, paso3.RootElement.GetProperty("pauta_id").GetInt32());
    }

    [Fact]
    public async Task InstaPulse_SinPautaOSinConfiguracion_NoLlamaALaApi()
    {
        var m = new ManejadorInstaPulse((ruta, _) => RespuestaOk(ruta));
        var sinPauta = await InstaPulse(m).EnviarPlantillaAsync("56912345678", "boleta_observada", ["Ana"]);
        Assert.False(sinPauta.Ok);
        Assert.False(sinPauta.Reintentable);
        Assert.Contains("boleta_observada", sinPauta.Error);
        Assert.Contains("pauta", sinPauta.Error);

        var sinUrl = await InstaPulse(m, ip => ip.UrlBase = null).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana"]);
        Assert.False(sinUrl.Ok);
        Assert.Contains("no está configurado", sinUrl.Error);
        Assert.Empty(m.Llamadas);
    }

    [Theory]
    [InlineData("/InstaPulseAPI2/participantes", 1, "paso 1")]
    [InlineData("/InstaPulseAPI2/casos", 2, "paso 2")]
    [InlineData("/InstaPulseAPI2/casos/b1a45749-0fa7/sessions", 3, "paso 3")]
    [InlineData("/InstaPulseAPI2/sessions/a9315d0e-561b/start/meta", 4, "paso 4")]
    public async Task InstaPulse_UnaFallaEnCualquierPasoSeInformaYDetieneElFlujo(string rutaQueFalla, int llamadasEsperadas, string paso)
    {
        var m = new ManejadorInstaPulse((ruta, _) => ruta == rutaQueFalla ? Json_(HttpStatusCode.BadRequest, """{"detail":"Participante inválido"}""") : RespuestaOk(ruta));
        var r = await InstaPulse(m).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana"]);
        Assert.False(r.Ok);
        Assert.False(r.Reintentable);                  // un 400 no se arregla reintentando
        Assert.Contains(paso, r.Error);
        Assert.Contains("HTTP 400", r.Error);
        Assert.Contains("Participante inválido", r.Error);
        Assert.Equal(llamadasEsperadas, m.Llamadas.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "Error interno", true)]
    [InlineData(HttpStatusCode.BadGateway, "bad gateway", true)]
    [InlineData(HttpStatusCode.TooManyRequests, "demasiadas", true)]
    [InlineData(HttpStatusCode.InternalServerError, "Meta error 132001: Template name does not exist in the translation", false)]
    [InlineData(HttpStatusCode.InternalServerError, "Meta error 132000: Number of parameters does not match", false)]
    [InlineData(HttpStatusCode.NotFound, "no existe", false)]
    public async Task InstaPulse_SoloReintentaLoTransitorio(HttpStatusCode codigo, string detalle, bool reintentable)
    {
        var m = new ManejadorInstaPulse((ruta, _) => ruta.EndsWith("/start/meta") ? Json_(codigo, JsonSerializer.Serialize(new { detail = detalle })) : RespuestaOk(ruta));
        var r = await InstaPulse(m).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana"]);
        Assert.False(r.Ok);
        Assert.Equal(reintentable, r.Reintentable);
        Assert.Contains(detalle, r.Error);
    }

    [Fact]
    public async Task InstaPulse_RespuestaSinIdsOConExitoFalso_EsErrorDefinitivo()
    {
        var sinCaso = await InstaPulse(new ManejadorInstaPulse((_, _) => Json_(HttpStatusCode.OK, """{"message":"ok"}"""))).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana"]);
        Assert.False(sinCaso.Ok);
        Assert.Contains("caso_id", sinCaso.Error);

        var exitoFalso = await InstaPulse(new ManejadorInstaPulse((ruta, _) => ruta.EndsWith("/start/meta")
            ? Json_(HttpStatusCode.OK, """{"success":false,"error":"Ventana de 24 h cerrada"}""") : RespuestaOk(ruta))).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana"]);
        Assert.False(exitoFalso.Ok);
        Assert.Contains("Ventana de 24 h cerrada", exitoFalso.Error);

        var noJson = await InstaPulse(new ManejadorInstaPulse((_, _) => Json_(HttpStatusCode.OK, "<html>proxy</html>"))).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana"]);
        Assert.False(noJson.Ok);
        Assert.Contains("no es JSON", noJson.Error);
    }

    [Fact]
    public async Task InstaPulse_SinConexion_EsReintentable()
    {
        var r = await InstaPulse(new ManejadorInstaPulse((_, _) => throw new HttpRequestException("sin red"))).EnviarPlantillaAsync("56912345678", "solicitud_boleta", ["Ana"]);
        Assert.False(r.Ok);
        Assert.True(r.Reintentable);
        Assert.Contains("InstaPulse", r.Error);
    }

    [Fact]
    public void Opciones_InstaPulseEsUnProveedorValidoYPideSuPropiaConfiguracion()
    {
        var o = new OpcionesWhatsApp { Proveedor = "instapulse", PhoneNumberId = "1", AccessToken = "t" };
        Assert.True(o.UsaInstaPulse);
        Assert.True(o.ProveedorValido);
        Assert.False(o.Configurado);                     // las credenciales de Meta no sirven para InstaPulse
        o.InstaPulse = new OpcionesInstaPulse { UrlBase = "https://x/api", Pautas = { ["solicitud_boleta"] = 1 } };
        Assert.True(o.Configurado);
        o.InstaPulse.Pautas.Clear();
        Assert.False(o.Configurado);
    }

    // ---- Los tres avisos, en el flujo real ----

    private static async Task<(Planilla P, Prestador Fra)> PlanillaConPrestadorAsync(Entorno e)
    {
        var fra = Autorizado(e);
        var pl = await e.PlanillaAsync("FACE TO FACE", new Fila(fra, "260041200105", Entorno.E, 6500, 51));
        return (pl, fra);
    }

    [Fact]
    public async Task Aviso_SolicitudInicial_AlImportarLaPlanilla()
    {
        using var e = Activado();
        var (_, fra) = await PlanillaConPrestadorAsync(e);
        var m = await e.Db.WhatsApp.SingleAsync();
        Assert.Equal("solicitud_boleta", m.Plantilla);
        Assert.Equal([fra.NombreCompleto, "OCT-2026", Formato.Clp(331500m), Formato.Fecha(Conciliacion.FechaLimite(Octubre, 10))],
            Json.Leer<List<string>>(m.Parametros));
    }

    [Fact]
    public async Task Aviso_SinAutorizacion_NoSeEnviaWhatsApp_PeroSiElCorreo()
    {
        using var e = Activado();
        var fra = Autorizado(e, autoriza: false);
        await e.PlanillaAsync("FACE TO FACE", new Fila(fra, "260041200105", Entorno.E, 6500, 51));
        Assert.Empty(await e.Db.WhatsApp.ToListAsync());
        Assert.Contains(await e.Db.Correos.ToListAsync(), c => c.Asunto.Contains("listo para boletear"));
    }

    [Fact]
    public async Task Aviso_Recordatorio_ASoloQuienNoHaSubidoBoleta()
    {
        using var e = Activado();
        var (pl, fra) = await PlanillaConPrestadorAsync(e);
        e.Db.WhatsApp.RemoveRange(e.Db.WhatsApp);   // dejar solo lo que genere el recordatorio
        await e.Db.SaveChangesAsync();
        Assert.Equal(1, await e.Boletas.RecordarPendientesAsync(pl.Id));
        var m = await e.Db.WhatsApp.SingleAsync();
        Assert.Equal("recordatorio_boleta", m.Plantilla);
        Assert.Equal([fra.NombreCompleto, "OCT-2026", Formato.Fecha(Conciliacion.FechaLimite(Octubre, 10))], Json.Leer<List<string>>(m.Parametros));

        await e.SubirAsync(pl, fra, "77", 331500);
        e.Db.WhatsApp.RemoveRange(e.Db.WhatsApp);
        await e.Db.SaveChangesAsync();
        Assert.Equal(0, await e.Boletas.RecordarPendientesAsync(pl.Id));   // ya subió: no se le recuerda
        Assert.Empty(await e.Db.WhatsApp.ToListAsync());
    }

    [Fact]
    public async Task Aviso_BoletaObservada_PidiendoUnaNueva()
    {
        using var e = Activado();
        var (pl, fra) = await PlanillaConPrestadorAsync(e);
        var r = await e.SubirAsync(pl, fra, "77", 331500);
        await e.Boletas.PedirNuevaAsync(r.Boleta.Id, "El monto no coincide con la planilla");
        var m = await e.Db.WhatsApp.SingleAsync(x => x.Plantilla == "boleta_observada");
        Assert.Equal([fra.NombreCompleto, "El monto no coincide con la planilla", "a la brevedad"], Json.Leer<List<string>>(m.Parametros));
    }
}
