using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using IpsosPagoHonorarios.Web.Services;

namespace IpsosPagoHonorarios.Carga;

/// <summary>Una etapa de la prueba: cuántos usuarios virtuales de cada tipo y por cuánto tiempo, con su pausa entre acciones.</summary>
public sealed record Etapa(string Nombre, int Operaciones, int Finanzas, int Prestadores, TimeSpan Duracion, TimeSpan PausaMin, TimeSpan PausaMax);

/// <summary>Usuarios virtuales por HTTP contra la aplicación publicada; cada uno con su sesión (cookie) y su IP simulada.</summary>
public sealed class Ejecutor(Uri url, Escenario esc)
{
    private readonly ConcurrentDictionary<string, Metrica> _metricas = new();
    private readonly ConcurrentQueue<PrestadorPortal> _prestadores = new(esc.Prestadores.DistinctBy(p => p.Rut).OrderBy(_ => Random.Shared.Next()));
    private int _ip;
    private int _subidas, _subidasOk, _importaciones, _importacionesOk;

    public sealed class Metrica
    {
        public readonly ConcurrentBag<double> Ms = [];
        public int Errores;
        public readonly ConcurrentDictionary<string, int> Motivos = new();
    }

    public IReadOnlyDictionary<string, Metrica> Metricas => _metricas;
    public (int Subidas, int SubidasOk, int Importaciones, int ImportacionesOk) Escrituras => (_subidas, _subidasOk, _importaciones, _importacionesOk);

    public async Task<ResultadoEtapa> EjecutarAsync(Etapa e, CancellationToken ct)
    {
        _metricas.Clear();
        var reloj = Stopwatch.StartNew();
        using var fin = CancellationTokenSource.CreateLinkedTokenSource(ct);
        fin.CancelAfter(e.Duracion);
        var tareas = new List<Task>();
        // Arranque escalonado en el primer 10 % de la etapa, para no medir solo el instante del primer ingreso.
        var total = e.Operaciones + e.Finanzas + e.Prestadores;
        var paso = total == 0 ? TimeSpan.Zero : e.Duracion / 10 / total;
        var i = 0;
        for (var k = 0; k < e.Operaciones; k++) { var u = esc.Operaciones[k % esc.Operaciones.Count]; tareas.Add(Vu(i++ * paso, () => OperacionesAsync(u, e, fin.Token), fin.Token)); }
        for (var k = 0; k < e.Finanzas; k++) { var u = esc.Finanzas[k % esc.Finanzas.Count]; tareas.Add(Vu(i++ * paso, () => FinanzasAsync(u, e, fin.Token), fin.Token)); }
        for (var k = 0; k < e.Prestadores; k++) tareas.Add(Vu(i++ * paso, () => PrestadorAsync(e, fin.Token), fin.Token));
        await Task.WhenAll(tareas);
        return new ResultadoEtapa(e, reloj.Elapsed, _metricas.ToDictionary(x => x.Key, x => x.Value));
    }

    private static async Task Vu(TimeSpan retraso, Func<Task> cuerpo, CancellationToken ct)
    {
        try { await Task.Delay(retraso, ct); await cuerpo(); }
        catch (OperationCanceledException) { }
    }

    // ---------- Usuarios virtuales ----------

    private async Task OperacionesAsync(UsuarioInterno u, Etapa e, CancellationToken ct)
    {
        using var c = Cliente();
        if (!await IngresarAsync(c, u.Email, "ops.ingreso", ct)) return;
        var n = 0;
        while (!ct.IsCancellationRequested)
        {
            var pl = u.Planillas.Count == 0 ? 0 : u.Planillas[Random.Shared.Next(u.Planillas.Count)];
            var r = Random.Shared.Next(100);
            if (r < 20) await GetAsync(c, "ops.panel", "/", ct);
            else if (r < 40) await GetAsync(c, "ops.planilla", $"/Ciclos/Planilla?id={pl}", ct);
            else if (r < 55) await GetAsync(c, "ops.seguimiento", $"/Boletas/Seguimiento?planilla={pl}", ct);
            else if (r < 70) await GetAsync(c, "ops.validacion", $"/Ciclos/ValidacionCuentas?planilla={pl}", ct);
            else if (r < 80) await GetAsync(c, "ops.produccion", "/Ciclos/Produccion", ct);
            else if (r < 88) await GetAsync(c, "ops.prestadores", $"/Maestros/Prestadores?q={Uri.EscapeDataString(esc.Prestadores[Random.Shared.Next(esc.Prestadores.Count)].Nombre.Split(' ')[0])}", ct);
            else if (r < 96) await GetAsync(c, "ops.xlsx", $"/Ciclos/Planilla?handler=Xlsx&id={pl}", ct, esperaHtml: false);
            else if (pl != 0 && ++n % 2 == 1) await ImportarAsync(c, u, pl, ct);
            await PausaAsync(e, ct);
        }
    }

    private async Task FinanzasAsync(UsuarioInterno u, Etapa e, CancellationToken ct)
    {
        using var c = Cliente();
        if (!await IngresarAsync(c, u.Email, "fin.ingreso", ct)) return;
        while (!ct.IsCancellationRequested)
        {
            var pl = esc.PlanillasEnRevision.Count == 0 ? 0 : esc.PlanillasEnRevision[Random.Shared.Next(esc.PlanillasEnRevision.Count)];
            var r = Random.Shared.Next(100);
            if (r < 35) await GetAsync(c, "fin.revision", $"/Finanzas/Revision?planilla={pl}", ct);
            else if (r < 50) await GetAsync(c, "fin.pagos", "/Finanzas/Pagos", ct);
            else if (r < 65) await GetAsync(c, "fin.panel", "/", ct);
            else if (r < 80) await GetAsync(c, "fin.historial", "/Ciclos/Historial", ct);
            else if (r < 92) await GetAsync(c, "fin.xlsx-enviada", $"/Finanzas/Revision?handler=Xlsx&planilla={pl}", ct, esperaHtml: false);
            else await GetAsync(c, "fin.correcciones", "/Ciclos/Correcciones", ct);
            await PausaAsync(e, ct);
        }
    }

    /// <summary>Una sesión del portal por iteración: ingresa, mira su pago, sube su boleta (una vez por prestador) y sale.</summary>
    private async Task PrestadorAsync(Etapa e, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!_prestadores.TryDequeue(out var p)) { await PausaAsync(e, ct); continue; }
            using var c = Cliente();
            try { await SesionPortalAsync(c, p, e, ct); }
            finally { _prestadores.Enqueue(p); }   // vuelve a la fila: en la próxima sesión solo consulta (ya subió su boleta)
        }
    }

    private async Task SesionPortalAsync(HttpClient c, PrestadorPortal p, Etapa e, CancellationToken ct)
    {
        {
            if (!await IngresarAsync(c, p.Rut, "portal.ingreso", ct)) return;
            var html = await GetAsync(c, "portal.inicio", "/Portal", ct);
            await PausaAsync(e, ct);
            if (html is not null && html.Contains($"name=\"planillaId\" value=\"{p.PlanillaId}\""))
            {
                Interlocked.Increment(ref _subidas);
                var numero = Random.Shared.Next(100_000, 999_999).ToString();
                var pdf = BoletaPdf.Generar(p.Nombre, p.Rut, numero, new DateOnly(2026, 10, 30), "76007075-0", "IPSOS OBSERVER (CHILE) S.A.", "Honorarios", p.Bruto, 0.1525m);
                var form = new MultipartFormDataContent
                {
                    { new StringContent(Token(html)), "__RequestVerificationToken" },
                    { new StringContent(p.PlanillaId.ToString()), "planillaId" },
                    { new ByteArrayContent(pdf) { Headers = { ContentType = new("application/pdf") } }, "pdf", $"boleta_{numero}.pdf" }
                };
                var resp = await EnviarAsync(c, "portal.subir-boleta", HttpMethod.Post, "/Portal?handler=Subir", form, ct, esperado: HttpStatusCode.Redirect);
                if (resp is not null)
                {
                    var despues = await GetAsync(c, "portal.resultado", "/Portal", ct);
                    if (despues?.Contains($"N° {numero}") == true && despues.Contains("Coincide")) Interlocked.Increment(ref _subidasOk);
                }
            }
            await PausaAsync(e, ct);
        }
    }

    private async Task ImportarAsync(HttpClient c, UsuarioInterno u, int planilla, CancellationToken ct)
    {
        var html = await GetAsync(c, "ops.produccion", "/Ciclos/Produccion", ct);
        if (html is null) return;
        // ~250 filas de la exportación del sistema, con prestadores de la planilla (reemplaza esa planilla: nueva versión).
        var filas = esc.Prestadores.Where(p => p.PlanillaId == planilla).Take(125).ToList();
        if (filas.Count == 0) return;
        var csv = new StringBuilder("Rut;Nombre;Job;Nombre Job;Glosa;Valor unitario;Cantidad;Tipo cuenta;Cuenta;Banco\n");
        foreach (var p in filas)
            for (var k = 0; k < 2; k++)
                csv.AppendLine($"{p.Rut};{p.Nombre};{esc.Jobs[Random.Shared.Next(esc.Jobs.Count)]};Estudio;{esc.Glosas[k % esc.Glosas.Count]};{5000 + Random.Shared.Next(20) * 500};{Random.Shared.Next(1, 40)};;;");
        Interlocked.Increment(ref _importaciones);
        var form = new MultipartFormDataContent
        {
            { new StringContent(Token(html)), "__RequestVerificationToken" },
            { new StringContent("Exportacion"), "TipoArchivo" },
            { new StringContent(u.AreaId.ToString()), "AreaId" },
            { new StringContent(planilla.ToString()), "Destino" },
            { new StringContent("Costo Directo"), "TipoGasto" },
            { new StringContent($"Operaciones {u.Area}"), "Responsable" },
            { new StringContent(u.Email), "ResponsableEmail" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes(csv.ToString())) { Headers = { ContentType = new("text/csv") } }, "Archivo", "produccion.csv" }
        };
        var r = await EnviarAsync(c, "ops.importar", HttpMethod.Post, "/Ciclos/Produccion", form, ct, esperado: HttpStatusCode.Redirect, aceptarOk: true);
        if (r is not null && (r.StatusCode == HttpStatusCode.Redirect || r.Cuerpo.Contains("Cargada"))) Interlocked.Increment(ref _importacionesOk);
    }

    // ---------- HTTP ----------

    private HttpClient Cliente()
    {
        var h = new SocketsHttpHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        var c = new HttpClient(h) { BaseAddress = url, Timeout = TimeSpan.FromSeconds(60) };
        // Cada usuario virtual llega con su propia IP (como personas distintas): el límite de intentos de ingreso es por IP.
        var n = Interlocked.Increment(ref _ip);
        c.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{n / 65536 % 256}.{n / 256 % 256}.{n % 256}");
        return c;
    }

    private async Task<bool> IngresarAsync(HttpClient c, string usuario, string nombre, CancellationToken ct)
    {
        var html = await GetAsync(c, "ingreso.pagina", "/Cuenta/Login", ct);
        if (html is null) return false;
        var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["Usuario"] = usuario, ["Contrasena"] = esc.Contrasena, ["__RequestVerificationToken"] = Token(html) });
        var r = await EnviarAsync(c, nombre, HttpMethod.Post, "/Cuenta/Login", form, ct, esperado: HttpStatusCode.Redirect);
        return r is not null;
    }

    private async Task<string?> GetAsync(HttpClient c, string nombre, string ruta, CancellationToken ct, bool esperaHtml = true)
    {
        var r = await EnviarAsync(c, nombre, HttpMethod.Get, ruta, null, ct);
        return r?.Cuerpo;
    }

    private sealed record Respuesta(HttpStatusCode StatusCode, string Cuerpo);

    private async Task<Respuesta?> EnviarAsync(HttpClient c, string nombre, HttpMethod metodo, string ruta, HttpContent? cuerpo, CancellationToken ct,
        HttpStatusCode esperado = HttpStatusCode.OK, bool aceptarOk = false)
    {
        var m = _metricas.GetOrAdd(nombre, _ => new Metrica());
        var reloj = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(metodo, ruta) { Content = cuerpo };
            using var resp = await c.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
            var texto = resp.Content.Headers.ContentType?.MediaType?.Contains("html") == true ? await resp.Content.ReadAsStringAsync(ct) : "";
            if (resp.Content.Headers.ContentType?.MediaType?.Contains("html") != true) await resp.Content.ReadAsByteArrayAsync(ct);
            reloj.Stop();
            m.Ms.Add(reloj.Elapsed.TotalMilliseconds);
            var ok = resp.StatusCode == esperado || (aceptarOk && resp.StatusCode == HttpStatusCode.OK);
            if (ok && resp.StatusCode == HttpStatusCode.Redirect && resp.Headers.Location?.OriginalString.Contains("/Cuenta/") == true && !ruta.StartsWith("/Cuenta/Login")) ok = false;
            if (ok && esperado == HttpStatusCode.Redirect && ruta.StartsWith("/Cuenta/Login") && resp.Headers.Location?.OriginalString.Contains("AccesoDenegado") == true) ok = false;
            if (!ok)
            {
                Interlocked.Increment(ref m.Errores);
                m.Motivos.AddOrUpdate($"HTTP {(int)resp.StatusCode}", 1, (_, v) => v + 1);
                return null;
            }
            return new(resp.StatusCode, texto);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
        catch (Exception ex)
        {
            reloj.Stop();
            m.Ms.Add(reloj.Elapsed.TotalMilliseconds);
            Interlocked.Increment(ref m.Errores);
            m.Motivos.AddOrUpdate(ex is TaskCanceledException ? "timeout 60 s" : ex.GetType().Name, 1, (_, v) => v + 1);
            return null;
        }
    }

    private static string Token(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

    private static Task PausaAsync(Etapa e, CancellationToken ct)
    {
        var ms = e.PausaMin.TotalMilliseconds + Random.Shared.NextDouble() * (e.PausaMax - e.PausaMin).TotalMilliseconds;
        return Task.Delay(TimeSpan.FromMilliseconds(ms), ct);
    }
}

public sealed record ResultadoEtapa(Etapa Etapa, TimeSpan Duracion, Dictionary<string, Ejecutor.Metrica> Metricas)
{
    public int Solicitudes => Metricas.Values.Sum(m => m.Ms.Count);
    public int Errores => Metricas.Values.Sum(m => m.Errores);
    public double PorSegundo => Solicitudes / Duracion.TotalSeconds;
    public double Percentil(double p) => Percentil(Metricas.Values.SelectMany(m => m.Ms), p);

    public static double Percentil(IEnumerable<double> valores, double p)
    {
        var v = valores.Order().ToArray();
        return v.Length == 0 ? 0 : v[Math.Clamp((int)Math.Ceiling(p * v.Length) - 1, 0, v.Length - 1)];
    }
}
