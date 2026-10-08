using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IpsosPagoHonorarios.Web.Services;

/// <summary>
/// Configuración de WhatsApp Business (API Cloud de Meta). El token es un secreto: va en appsettings.Production.json
/// (ignorado por git) o en la variable de entorno WhatsApp__AccessToken, nunca en el repositorio.
/// </summary>
public class OpcionesWhatsApp
{
    public bool Habilitado { get; set; }
    /// <summary>Quién entrega los mensajes: "Meta" (API Cloud directa), "Twilio" o "InstaPulse" (pasarela interna que envía por Meta).</summary>
    public string Proveedor { get; set; } = "Meta";
    public OpcionesTwilio Twilio { get; set; } = new();
    public OpcionesInstaPulse InstaPulse { get; set; } = new();
    public string UrlBase { get; set; } = "https://graph.facebook.com";
    public string ApiVersion { get; set; } = "v21.0";
    /// <summary>Identificador del número de teléfono de WhatsApp Business (no es el número en sí).</summary>
    public string? PhoneNumberId { get; set; }
    public string? AccessToken { get; set; }
    /// <summary>Código de idioma con el que se aprobaron las plantillas (es, es_CL…).</summary>
    public string Idioma { get; set; } = "es";
    public int MaxIntentos { get; set; } = 3;
    /// <summary>Nombres de las plantillas aprobadas en Meta (ver docs/whatsapp.md para su texto).</summary>
    public string PlantillaSolicitud { get; set; } = "solicitud_boleta";
    public string PlantillaRecordatorio { get; set; } = "recordatorio_boleta";
    public string PlantillaObservada { get; set; } = "boleta_observada";

    public bool UsaTwilio => string.Equals(Proveedor, "Twilio", StringComparison.OrdinalIgnoreCase);
    public bool UsaInstaPulse => string.Equals(Proveedor, "InstaPulse", StringComparison.OrdinalIgnoreCase);
    public bool ProveedorValido => UsaTwilio || UsaInstaPulse || string.Equals(Proveedor, "Meta", StringComparison.OrdinalIgnoreCase);

    public bool Configurado => UsaTwilio ? Twilio.Configurado
        : UsaInstaPulse ? InstaPulse.Configurado
        : !string.IsNullOrWhiteSpace(PhoneNumberId) && !string.IsNullOrWhiteSpace(AccessToken);
}

/// <summary>
/// WhatsApp a través de Twilio con un número propio aprobado. Las plantillas se crean y aprueban en Twilio (Content Template
/// Builder) y se identifican por su Content SID (HX…): <see cref="ContentSids"/> relaciona el nombre de cada plantilla de la
/// aplicación (p. ej. solicitud_boleta) con su Content SID. El Auth Token es un secreto: va fuera del repositorio.
/// </summary>
public class OpcionesTwilio
{
    public string UrlBase { get; set; } = "https://api.twilio.com";
    public string? AccountSid { get; set; }
    public string? AuthToken { get; set; }
    /// <summary>Número de WhatsApp aprobado en Twilio, p. ej. +56912345678 (o whatsapp:+56912345678).</summary>
    public string? From { get; set; }
    /// <summary>Alternativa a From: un Messaging Service (MG…) que tenga el número de WhatsApp en su remitente.</summary>
    public string? MessagingServiceSid { get; set; }
    public Dictionary<string, string> ContentSids { get; set; } = new();

    public bool Configurado => !string.IsNullOrWhiteSpace(AccountSid) && !string.IsNullOrWhiteSpace(AuthToken)
                               && (!string.IsNullOrWhiteSpace(From) || !string.IsNullOrWhiteSpace(MessagingServiceSid));
}

/// <summary>
/// WhatsApp a través de InstaPulse, la pasarela interna que envía por la API de Meta. Cada mensaje recorre 4 pasos
/// (participante, caso, sesión con una pauta y disparo). La pauta ya guarda el número de WhatsApp, el token de Meta y la
/// plantilla aprobada, por eso aquí no hay secretos: <see cref="Pautas"/> relaciona el nombre de cada plantilla de la
/// aplicación (p. ej. solicitud_boleta) con el PautaID creado en InstaPulse.
/// </summary>
public class OpcionesInstaPulse
{
    /// <summary>Dirección de la API, p. ej. https://servidor/InstaPulseAPI2.</summary>
    public string? UrlBase { get; set; }
    /// <summary>Valor del encabezado x-remote-user: define el proyecto (tenant) en InstaPulse.</summary>
    public string UsuarioRemoto { get; set; } = "IAOps";
    public Dictionary<string, int> Pautas { get; set; } = new();
    /// <summary>Deja la sesión activa para que InstaPulse asocie las respuestas del prestador.</summary>
    public bool MarcarActiva { get; set; } = true;
    public string TituloCaso { get; set; } = "Pago de Honorarios";

    public bool Configurado => !string.IsNullOrWhiteSpace(UrlBase) && !string.IsNullOrWhiteSpace(UsuarioRemoto) && Pautas.Count > 0;
}

public sealed record ResultadoEnvio(bool Ok, string? ProveedorId, string? Error, bool Reintentable);

public interface IProveedorWhatsApp
{
    Task<ResultadoEnvio> EnviarPlantillaAsync(string para, string plantilla, IReadOnlyList<string> parametros, CancellationToken ct = default);
}

/// <summary>Envío por la API Cloud de WhatsApp (mensajes de plantilla, únicos permitidos para iniciar una conversación).</summary>
public class ProveedorWhatsAppMeta(HttpClient http, IOptions<OpcionesWhatsApp> op) : IProveedorWhatsApp
{
    public async Task<ResultadoEnvio> EnviarPlantillaAsync(string para, string plantilla, IReadOnlyList<string> parametros, CancellationToken ct = default)
    {
        var o = op.Value;
        if (!o.Configurado) return new(false, null, "WhatsApp no está configurado: faltan PhoneNumberId o AccessToken.", false);
        var cuerpo = new
        {
            messaging_product = "whatsapp",
            to = para,
            type = "template",
            template = new
            {
                name = plantilla,
                language = new { code = o.Idioma },
                components = parametros.Count == 0
                    ? Array.Empty<object>()
                    : [new { type = "body", parameters = parametros.Select(t => new { type = "text", text = t }).ToArray() }]
            }
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{o.UrlBase.TrimEnd('/')}/{o.ApiVersion}/{o.PhoneNumberId}/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.AccessToken);
        try
        {
            using var resp = await http.SendAsync(req, ct);
            var texto = await resp.Content.ReadAsStringAsync(ct);
            if (resp.IsSuccessStatusCode) return new(true, IdMensaje(texto), null, false);
            var codigo = (int)resp.StatusCode;
            return new(false, null, ErrorDe(texto, codigo), codigo == 429 || codigo >= 500);
        }
        catch (HttpRequestException ex)
        {
            return new(false, null, "Sin conexión con WhatsApp: " + ex.Message, true);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, null, "WhatsApp no respondió a tiempo.", true);
        }
    }

    private static string? IdMensaje(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            return d.RootElement.GetProperty("messages")[0].GetProperty("id").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    /// <summary>Mensaje de error de Meta: "(código 132001) La plantilla no existe — detalle".</summary>
    internal static string ErrorDe(string json, int http)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var e = d.RootElement.GetProperty("error");
            var msg = e.TryGetProperty("message", out var m) ? m.GetString() : null;
            var cod = e.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : (int?)null;
            var det = e.TryGetProperty("error_data", out var ed) && ed.TryGetProperty("details", out var dt) ? dt.GetString() : null;
            var t = $"HTTP {http}" + (cod is null ? "" : $" (código {cod})") + (msg is null ? "" : $": {msg}") + (det is null ? "" : $" — {det}");
            return t.Length > 450 ? t[..450] : t;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return $"HTTP {http}"; }
    }
}

/// <summary>Envío por la API de mensajes de Twilio (WhatsApp con plantilla de contenido aprobada).</summary>
public class ProveedorWhatsAppTwilio(HttpClient http, IOptions<OpcionesWhatsApp> op) : IProveedorWhatsApp
{
    public async Task<ResultadoEnvio> EnviarPlantillaAsync(string para, string plantilla, IReadOnlyList<string> parametros, CancellationToken ct = default)
    {
        var t = op.Value.Twilio;
        if (!t.Configurado) return new(false, null, "Twilio no está configurado: faltan AccountSid, AuthToken o el número de origen (From / MessagingServiceSid).", false);
        if (!t.ContentSids.TryGetValue(plantilla, out var contentSid) || string.IsNullOrWhiteSpace(contentSid))
            return new(false, null, $"Twilio: falta el Content SID de la plantilla “{plantilla}” (WhatsApp:Twilio:ContentSids).", false);

        var campos = new List<KeyValuePair<string, string>>
        {
            new("To", "whatsapp:+" + para.TrimStart('+')),
            new("ContentSid", contentSid.Trim()),
            new("ContentVariables", JsonSerializer.Serialize(parametros.Select((v, i) => (Clave: (i + 1).ToString(), Valor: v)).ToDictionary(x => x.Clave, x => x.Valor)))
        };
        if (!string.IsNullOrWhiteSpace(t.MessagingServiceSid)) campos.Add(new("MessagingServiceSid", t.MessagingServiceSid.Trim()));
        else campos.Add(new("From", Origen(t.From!)));

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{t.UrlBase.TrimEnd('/')}/2010-04-01/Accounts/{Uri.EscapeDataString(t.AccountSid!.Trim())}/Messages.json")
        {
            Content = new FormUrlEncodedContent(campos)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{t.AccountSid!.Trim()}:{t.AuthToken!.Trim()}")));
        try
        {
            using var resp = await http.SendAsync(req, ct);
            var texto = await resp.Content.ReadAsStringAsync(ct);
            var codigo = (int)resp.StatusCode;
            if (resp.IsSuccessStatusCode) return new(true, Sid(texto), null, false);
            return new(false, null, ErrorDe(texto, codigo), codigo == 429 || codigo >= 500);
        }
        catch (HttpRequestException ex)
        {
            return new(false, null, "Sin conexión con Twilio: " + ex.Message, true);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, null, "Twilio no respondió a tiempo.", true);
        }
    }

    /// <summary>"+56 9 1234 5678" o "56912345678" → "whatsapp:+56912345678"; si ya trae "whatsapp:" se respeta.</summary>
    internal static string Origen(string from)
    {
        var f = from.Trim();
        if (f.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase)) return f;
        return "whatsapp:+" + new string(f.Where(char.IsAsciiDigit).ToArray());
    }

    private static string? Sid(string json)
    {
        try { using var d = JsonDocument.Parse(json); return d.RootElement.GetProperty("sid").GetString(); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    /// <summary>Error de Twilio: "HTTP 400 (código 21211): El número 'Para' no es válido".</summary>
    internal static string ErrorDe(string json, int http)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            var msg = r.TryGetProperty("message", out var m) ? m.GetString() : null;
            var cod = r.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : (int?)null;
            var t = $"HTTP {http}" + (cod is null ? "" : $" (código {cod})") + (msg is null ? "" : $": {msg}");
            return t.Length > 450 ? t[..450] : t;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return $"HTTP {http}"; }
    }
}

/// <summary>
/// Envío por InstaPulse (manual de integración de eventos y sistemas de pago): POST /participantes, /casos,
/// /casos/{id}/sessions y /sessions/{id}/start/meta. Las variables viajan ordenadas en body_params y también como metadata
/// p1, p2… (la pauta puede declarar "variables": ["p1","p2",…]).
/// </summary>
public class ProveedorWhatsAppInstaPulse(HttpClient http, IOptions<OpcionesWhatsApp> op) : IProveedorWhatsApp
{
    private sealed record Paso(JsonElement? Json, ResultadoEnvio? Falla);

    public async Task<ResultadoEnvio> EnviarPlantillaAsync(string para, string plantilla, IReadOnlyList<string> parametros, CancellationToken ct = default)
    {
        var o = op.Value;
        var ip = o.InstaPulse;
        if (!ip.Configurado) return new(false, null, "InstaPulse no está configurado: faltan UrlBase, UsuarioRemoto o Pautas.", false);
        if (!ip.Pautas.TryGetValue(plantilla, out var pautaId) || pautaId <= 0)
            return new(false, null, $"InstaPulse: falta la pauta de la plantilla “{plantilla}” (WhatsApp:InstaPulse:Pautas).", false);

        var telefono = "+" + para.TrimStart('+');
        var nombre = parametros.Count > 0 && !string.IsNullOrWhiteSpace(parametros[0]) ? parametros[0] : "Prestador";
        var metadata = new Dictionary<string, string> { ["origen"] = "PagoHonorarios", ["plantilla"] = plantilla };
        for (var i = 0; i < parametros.Count; i++) metadata[$"p{i + 1}"] = parametros[i];

        try
        {
            var participante = await PostAsync(ip, "/participantes", new { phone = telefono, name = nombre, language = o.Idioma, metadata }, "paso 1 (participantes)", ct);
            if (participante.Falla is { } f1) return f1;

            var caso = await PostAsync(ip, "/casos", new { phone_number = telefono, titulo = $"{ip.TituloCaso} · {plantilla}" }, "paso 2 (casos)", ct);
            if (caso.Falla is { } f2) return f2;
            if (Texto(caso.Json, "caso_id") is not { } casoId) return new(false, null, "InstaPulse paso 2 (casos): la respuesta no trae caso_id.", false);

            var sesion = await PostAsync(ip, $"/casos/{Uri.EscapeDataString(casoId)}/sessions",
                new { pauta_id = pautaId, incluir_en_historial_global = false }, "paso 3 (sesión)", ct);
            if (sesion.Falla is { } f3) return f3;
            if (Texto(sesion.Json, "session_id") is not { } sesionId) return new(false, null, "InstaPulse paso 3 (sesión): la respuesta no trae session_id.", false);

            var envio = await PostAsync(ip, $"/sessions/{Uri.EscapeDataString(sesionId)}/start/meta",
                new { language_code = o.Idioma, body_params = parametros, mark_active = ip.MarcarActiva, mode = (string?)null }, "paso 4 (start/meta)", ct);
            if (envio.Falla is { } f4) return f4;
            if (envio.Json is { ValueKind: JsonValueKind.Object } j && j.TryGetProperty("success", out var exito) && exito.ValueKind == JsonValueKind.False)
                return new(false, null, "InstaPulse paso 4 (start/meta): " + Detalle(j.GetRawText()), EsTransitorio(j.GetRawText()));
            return new(true, Texto(envio.Json, "message_id") ?? sesionId, null, false);
        }
        catch (HttpRequestException ex)
        {
            return new(false, null, "Sin conexión con InstaPulse: " + ex.Message, true);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, null, "InstaPulse no respondió a tiempo.", true);
        }
    }

    private async Task<Paso> PostAsync(OpcionesInstaPulse ip, string ruta, object cuerpo, string paso, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, ip.UrlBase!.TrimEnd('/') + ruta)
        {
            Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-remote-user", ip.UsuarioRemoto);
        using var resp = await http.SendAsync(req, ct);
        var texto = await resp.Content.ReadAsStringAsync(ct);
        var codigo = (int)resp.StatusCode;
        if (!resp.IsSuccessStatusCode)
            return new(null, new(false, null, $"InstaPulse {paso}: HTTP {codigo}: {Detalle(texto)}", codigo == 429 || (codigo >= 500 && EsTransitorio(texto))));
        try
        {
            using var d = JsonDocument.Parse(texto);
            return new(d.RootElement.Clone(), null);
        }
        catch (JsonException)
        {
            return new(null, new(false, null, $"InstaPulse {paso}: la respuesta no es JSON.", false));
        }
    }

    private static string? Texto(JsonElement? j, string campo) =>
        j is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(campo, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? v.ToString() is { Length: > 0 } t ? t : null : null;

    /// <summary>Errores de Meta que se repiten igual en cada intento (plantilla inexistente, variables, número sin WhatsApp…).</summary>
    private static readonly string[] CodigosDefinitivos = ["131026", "132000", "132001", "132005", "132007", "132012", "132015", "132016", "133010"];

    private static bool EsTransitorio(string texto) => !CodigosDefinitivos.Any(texto.Contains);

    /// <summary>Mensaje de error de InstaPulse (FastAPI: "detail"; también "message" o "error").</summary>
    internal static string Detalle(string texto)
    {
        try
        {
            using var d = JsonDocument.Parse(texto);
            var r = d.RootElement;
            if (r.ValueKind == JsonValueKind.Object)
                foreach (var campo in new[] { "detail", "message", "error" })
                    if (r.TryGetProperty(campo, out var v))
                    {
                        var t = v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
                        if (!string.IsNullOrWhiteSpace(t)) return Cortar(t!);
                    }
        }
        catch (JsonException) { }
        return Cortar(string.IsNullOrWhiteSpace(texto) ? "sin detalle" : texto.Trim());
    }

    private static string Cortar(string t) => t.Length > 400 ? t[..400] : t;
}

/// <summary>Envía la bandeja pendiente con reintentos (2 y 10 minutos) para fallas transitorias.</summary>
public class EnviadorWhatsApp(AppDbContext db, IProveedorWhatsApp proveedor, IOptions<OpcionesWhatsApp> op, TimeProvider reloj, ILogger<EnviadorWhatsApp> log)
{
    public async Task<int> EnviarPendientesAsync(CancellationToken ct = default)
    {
        var o = op.Value;
        if (!o.Habilitado || !o.Configurado) return 0;
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var pendientes = await db.WhatsApp.Where(m => m.EnviadoEn == null && m.Intentos < o.MaxIntentos).OrderBy(m => m.Id).Take(50).ToListAsync(ct);
        var enviados = 0;
        foreach (var m in pendientes)
        {
            if (m.UltimoIntentoEn is { } ultimo && ahora < ultimo + Espera(m.Intentos)) continue;
            var r = await proveedor.EnviarPlantillaAsync(m.Para, m.Plantilla, Json.Leer<List<string>>(m.Parametros) ?? [], ct);
            m.Intentos++;
            m.UltimoIntentoEn = ahora;
            if (r.Ok)
            {
                m.EnviadoEn = ahora;
                m.ProveedorId = r.ProveedorId;
                m.Error = null;
                enviados++;
            }
            else
            {
                m.Error = r.Error;
                if (!r.Reintentable) m.Intentos = o.MaxIntentos;   // error definitivo (plantilla, número, token): no insiste
                log.LogWarning("WhatsApp {Plantilla} → ****{Fin} falló (intento {N}): {Error}", m.Plantilla, m.Para[^4..], m.Intentos, r.Error);
            }
            await db.SaveChangesAsync(ct);
        }
        return enviados;
    }

    private static TimeSpan Espera(int intentos) => TimeSpan.FromMinutes(intentos <= 1 ? 2 : 10);
}

/// <summary>Proceso en segundo plano: vacía la bandeja de WhatsApp cada 30 segundos.</summary>
public class WhatsAppBackgroundService(IServiceScopeFactory scopes, TimeProvider reloj, ILogger<WhatsAppBackgroundService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), reloj);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var n = await scope.ServiceProvider.GetRequiredService<EnviadorWhatsApp>().EnviarPendientesAsync(ct);
                if (n > 0) log.LogInformation("WhatsApp enviados: {N}", n);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Error al enviar WhatsApp");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
