using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using IpsosPagoHonorarios.Carga;

// Uso:
//   dotnet run -c Release -- generar  --conexion "<cadena SQL Server de PRUEBA>" [--prestadores 3000] [--meses 12] [--lineas 250] [--areas-extra 8] [--escenario escenario.json]
//   dotnet run -c Release -- ejecutar --url http://servidor:puerto --perfil humo|carga|pico|estres|resistencia [--escenario escenario.json] [--pid <pid del servidor>] [--informe resultados.md]
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var a = Args(args.Skip(1).ToArray());
string Arg(string k, string def) => a.TryGetValue(k, out var v) ? v : def;
var archivo = Arg("escenario", "escenario.json");
var json = new JsonSerializerOptions { WriteIndented = true };

switch (args.FirstOrDefault())
{
    case "generar":
    {
        var g = new Generador(a["conexion"], int.Parse(Arg("prestadores", "3000")), int.Parse(Arg("meses", "12")), int.Parse(Arg("lineas", "250")), int.Parse(Arg("areas-extra", "8")));
        var esc = await g.EjecutarAsync(DateOnly.Parse(Arg("periodo", "2026-10-01")));
        await File.WriteAllTextAsync(archivo, JsonSerializer.Serialize(esc, json));
        Console.WriteLine($"Escenario guardado en {archivo} (contraseña de prueba de todos los usuarios generados: {esc.Contrasena}).");
        return 0;
    }
    case "ejecutar":
    {
        var esc = JsonSerializer.Deserialize<Escenario>(await File.ReadAllTextAsync(archivo))!;
        var ej = new Ejecutor(new Uri(a["url"]), esc);
        var perfil = Arg("perfil", "humo");
        var etapas = Perfiles(perfil, esc);
        int? pid = a.TryGetValue("pid", out var ps) ? int.Parse(ps) : null;
        var informe = new StringBuilder($"# Prueba {perfil} · {DateTime.Now:yyyy-MM-dd HH:mm}\n\n");
        Console.WriteLine($"Perfil {perfil}: {etapas.Count} etapas contra {a["url"]}");
        foreach (var e in etapas)
        {
            using var monitor = pid is null ? null : new MonitorProceso(pid.Value);
            var antes = ej.Escrituras;
            var r = await ej.EjecutarAsync(e, CancellationToken.None);
            var desp = ej.Escrituras;
            var texto = Resumen(r, monitor, (desp.Subidas - antes.Subidas, desp.SubidasOk - antes.SubidasOk, desp.Importaciones - antes.Importaciones, desp.ImportacionesOk - antes.ImportacionesOk));
            Console.WriteLine(texto);
            informe.AppendLine(texto);
            // Estrés: se detiene al pasar el punto de quiebre (más de 10 % de errores o p95 sobre 10 s).
            if (perfil == "estres" && (r.Errores > r.Solicitudes * 0.10 || r.Percentil(0.95) > 10_000)) { Console.WriteLine("Punto de quiebre superado: fin del escalonamiento."); informe.AppendLine("Punto de quiebre superado: fin del escalonamiento.\n"); break; }
        }
        if (a.TryGetValue("informe", out var inf)) await File.WriteAllTextAsync(inf, informe.ToString());
        return 0;
    }
    default:
        Console.WriteLine("Comandos: generar | ejecutar (ver README.md).");
        return 1;
}

static List<Etapa> Perfiles(string perfil, Escenario esc)
{
    var ops = esc.Operaciones.Count;
    TimeSpan S(double s) => TimeSpan.FromSeconds(s);
    return perfil switch
    {
        // Verifica que todo responde antes de medir.
        "humo" => [new("humo", 2, 1, 5, S(30), S(0.5), S(1.5))],
        // Día de punta realista: todos los operativos y Finanzas trabajando, prestadores subiendo boletas; pausas humanas.
        "carga" => [new("carga punta", ops, 2, 60, S(300), S(2), S(6))],
        // Último día de plazo de boletas: 300 prestadores entran casi a la vez, más la operación interna normal.
        "pico" => [new("pico de prestadores", ops, 2, 300, S(120), S(1), S(3))],
        // Escalones sin pausa humana hasta encontrar el punto de quiebre.
        "estres" =>
        [
            new("estrés x1", ops / 2, 1, 20, S(60), S(0.1), S(0.3)),
            new("estrés x2", ops, 2, 50, S(60), S(0.1), S(0.3)),
            new("estrés x4", ops * 2, 4, 100, S(60), S(0.1), S(0.3)),
            new("estrés x8", ops * 4, 8, 200, S(60), S(0.1), S(0.3)),
            new("estrés x16", ops * 8, 16, 400, S(60), S(0.1), S(0.3)),
            new("estrés x32", ops * 16, 32, 800, S(60), S(0.1), S(0.3))
        ],
        // Carga normal sostenida para ver fugas de memoria o degradación.
        "resistencia" => [new("resistencia 20 min", ops, 2, 60, S(1200), S(2), S(6))],
        _ => throw new ArgumentException($"Perfil desconocido: {perfil}")
    };
}

static string Resumen(ResultadoEtapa r, MonitorProceso? m, (int Subidas, int SubidasOk, int Imp, int ImpOk) w)
{
    var e = r.Etapa;
    var sb = new StringBuilder();
    sb.AppendLine($"## {e.Nombre}: {e.Operaciones} operativos · {e.Finanzas} Finanzas · {e.Prestadores} prestadores · {r.Duracion.TotalSeconds:0} s");
    sb.AppendLine();
    sb.AppendLine($"Solicitudes {r.Solicitudes} · {r.PorSegundo:0.0}/s · errores {r.Errores} ({(r.Solicitudes == 0 ? 0 : 100.0 * r.Errores / r.Solicitudes):0.00} %) · p50 {r.Percentil(0.5):0} ms · p95 {r.Percentil(0.95):0} ms · p99 {r.Percentil(0.99):0} ms");
    sb.AppendLine($"Boletas subidas {w.Subidas} (cuadran {w.SubidasOk}) · importaciones {w.Imp} (cargadas {w.ImpOk})" + (m is null ? "" : $" · servidor: CPU media {m.CpuMedia:0} % (máx {m.CpuMax:0} %, {Environment.ProcessorCount} núcleos = {Environment.ProcessorCount * 100} %), memoria máx {m.RssMaxMb:0} MB"));
    sb.AppendLine();
    sb.AppendLine("| Acción | n | errores | p50 ms | p95 ms | p99 ms | máx ms | motivos |");
    sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
    foreach (var (k, v) in r.Metricas.OrderBy(x => x.Key))
    {
        var ms = v.Ms.ToArray();
        sb.AppendLine($"| {k} | {ms.Length} | {v.Errores} | {ResultadoEtapa.Percentil(ms, 0.5):0} | {ResultadoEtapa.Percentil(ms, 0.95):0} | {ResultadoEtapa.Percentil(ms, 0.99):0} | {(ms.Length == 0 ? 0 : ms.Max()):0} | {string.Join(", ", v.Motivos.Select(x => $"{x.Key}×{x.Value}"))} |");
    }
    return sb.ToString();
}

static Dictionary<string, string> Args(string[] a)
{
    var d = new Dictionary<string, string>();
    for (var i = 0; i + 1 < a.Length; i += 2) d[a[i].TrimStart('-')] = a[i + 1];
    return d;
}

/// <summary>CPU y memoria del proceso del servidor (Linux, /proc), si corre en la misma máquina.</summary>
sealed class MonitorProceso : IDisposable
{
    private readonly Timer _t;
    private readonly List<double> _cpu = [];
    private double _rss;
    private TimeSpan _ultimoCpu;
    private readonly Stopwatch _reloj = Stopwatch.StartNew();

    public MonitorProceso(int pid)
    {
        var p = Process.GetProcessById(pid);
        _ultimoCpu = p.TotalProcessorTime;
        _t = new Timer(_ =>
        {
            try
            {
                p.Refresh();
                var cpu = p.TotalProcessorTime;
                var trans = _reloj.Elapsed.TotalMilliseconds;
                _reloj.Restart();
                lock (_cpu) _cpu.Add((cpu - _ultimoCpu).TotalMilliseconds / trans * 100);
                _ultimoCpu = cpu;
                _rss = Math.Max(_rss, p.WorkingSet64 / 1048576.0);
            }
            catch { /* el proceso terminó */ }
        }, null, 1000, 1000);
    }

    public double CpuMedia { get { lock (_cpu) return _cpu.Count == 0 ? 0 : _cpu.Average(); } }
    public double CpuMax { get { lock (_cpu) return _cpu.Count == 0 ? 0 : _cpu.Max(); } }
    public double RssMaxMb => _rss;
    public void Dispose() => _t.Dispose();
}
