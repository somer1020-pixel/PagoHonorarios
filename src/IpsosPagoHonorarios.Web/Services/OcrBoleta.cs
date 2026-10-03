using System.Diagnostics;
using System.Text;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.Options;

namespace IpsosPagoHonorarios.Web.Services;

public class OpcionesOcr
{
    public bool Habilitado { get; set; } = true;
    /// <summary>
    /// Auto (por defecto): en Windows, el OCR integrado de Windows y, si no está disponible, Tesseract; en Linux, Tesseract.
    /// Windows | Tesseract: solo ese motor.
    /// </summary>
    public string Motor { get; set; } = "Auto";
    /// <summary>Ruta de tesseract. Vacío: x64\tesseract.exe junto a la aplicación (Windows) o "tesseract" del sistema (Linux).</summary>
    public string? Tesseract { get; set; }
    /// <summary>Carpeta con {Idioma}.traineddata. Vacío: Ocr/tessdata de la aplicación.</summary>
    public string? Tessdata { get; set; }
    public string Idioma { get; set; } = "spa";
    public int TimeoutSegundos { get; set; } = 60;
}

/// <summary>Resultado de la OCR: líneas reconocidas (null si no se pudo), motor usado y detalle de cada intento.</summary>
public sealed record ResultadoOcr(List<string>? Lineas, string? Motor, List<string> Detalle);

/// <summary>Lee por OCR el texto de una boleta cuyo PDF no trae texto (p. ej. la boleta compartida desde la app del SII).</summary>
public interface ILectorOcr
{
    Task<ResultadoOcr> ReconocerAsync(byte[] pdf, CancellationToken ct = default);
}

/// <summary>Un motor de OCR. Devuelve las líneas o lanza una excepción con el motivo (queda en el diagnóstico).</summary>
internal interface IMotorOcr
{
    string Nombre { get; }
    Task<List<string>> LeerAsync(byte[] pdf, CancellationToken ct);
}

/// <summary>Prueba los motores en orden y usa el primero que reconozca texto.</summary>
public class LectorOcr(IOptions<OpcionesOcr> opciones, IHostEnvironment env, ILogger<LectorOcr> log) : ILectorOcr
{
    public async Task<ResultadoOcr> ReconocerAsync(byte[] pdf, CancellationToken ct = default)
    {
        var op = opciones.Value;
        var detalle = new List<string>();
        if (!op.Habilitado)
        {
            detalle.Add("OCR deshabilitada (Ocr:Habilitado = false).");
            return new(null, null, detalle);
        }
        foreach (var motor in Motores(op, detalle))
        {
            var reloj = Stopwatch.StartNew();
            try
            {
                var lineas = (await motor.LeerAsync(pdf, ct)).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (lineas.Count == 0)
                {
                    detalle.Add($"{motor.Nombre}: no reconoció texto ({reloj.ElapsedMilliseconds} ms).");
                    continue;
                }
                detalle.Add($"{motor.Nombre}: {lineas.Count} líneas en {reloj.ElapsedMilliseconds} ms.");
                return new(lineas, motor.Nombre, detalle);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                detalle.Add($"{motor.Nombre}: {ex.GetType().Name}: {ex.Message}");
                log.LogWarning(ex, "OCR con {Motor} no disponible.", motor.Nombre);
            }
        }
        log.LogWarning("OCR no disponible: la boleta queda para corrección manual. {Detalle}", string.Join(" | ", detalle));
        return new(null, null, detalle);
    }

    private IEnumerable<IMotorOcr> Motores(OpcionesOcr op, List<string> detalle)
    {
        var motor = op.Motor?.Trim() ?? "Auto";
        var auto = motor.Equals("Auto", StringComparison.OrdinalIgnoreCase);
        if (auto || motor.Equals("Windows", StringComparison.OrdinalIgnoreCase))
        {
#if WINDOWS
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) yield return new MotorOcrWindows();
            else detalle.Add("OCR de Windows: requiere Windows 10 (2004) o superior.");
#else
            if (!auto || OperatingSystem.IsWindows())
                detalle.Add("OCR de Windows: no disponible en esta compilación (en Windows se compila para net10.0-windows).");
#endif
        }
        if (auto || motor.Equals("Tesseract", StringComparison.OrdinalIgnoreCase))
            yield return new MotorOcrTesseract(op, env);
    }
}

#if WINDOWS
/// <summary>
/// OCR integrado de Windows (Windows.Media.Ocr) y render del PDF con Windows.Data.Pdf: componentes del sistema firmados por
/// Microsoft, sin instalar nada (no los bloquea Control inteligente de aplicaciones). Usa el español si está instalado;
/// si no, el idioma del perfil (montos, RUT y fechas se leen igual).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class MotorOcrWindows : IMotorOcr
{
    public string Nombre => "OCR de Windows";

    public async Task<List<string>> LeerAsync(byte[] pdf, CancellationToken ct)
    {
        var motor = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("es"))
                    ?? Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
                    ?? throw new InvalidOperationException("No hay idiomas de OCR instalados en Windows (Configuración → Hora e idioma → Idioma → Español → Opciones → Reconocimiento óptico de caracteres).");

        using var origen = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var escritor = new Windows.Storage.Streams.DataWriter(origen))
        {
            escritor.WriteBytes(pdf);
            await escritor.StoreAsync().AsTask(ct);
            escritor.DetachStream();
        }
        origen.Seek(0);
        var documento = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(origen).AsTask(ct);

        var lineas = new List<string>();
        for (uint i = 0; i < Math.Min(documento.PageCount, 2u); i++)
        {
            using var pagina = documento.GetPage(i);
            var lado = Math.Max(pagina.Size.Width, pagina.Size.Height);
            var escala = Math.Clamp(Math.Min(3500, Windows.Media.Ocr.OcrEngine.MaxImageDimension) / lado, 1, 5);
            using var imagen = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var opciones = new Windows.Data.Pdf.PdfPageRenderOptions
            {
                DestinationWidth = (uint)(pagina.Size.Width * escala),
                DestinationHeight = (uint)(pagina.Size.Height * escala)
            };
            await pagina.RenderToStreamAsync(imagen, opciones).AsTask(ct);   // fondo blanco por defecto
            var decodificador = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(imagen).AsTask(ct);
            using var mapa = await decodificador.GetSoftwareBitmapAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied).AsTask(ct);
            var resultado = await motor.RecognizeAsync(mapa).AsTask(ct);
            // Windows separa en líneas distintas una etiqueta y su monto lejano: se reagrupan las palabras por posición.
            var palabras = resultado.Lines.SelectMany(l => l.Words)
                .Select(w => new IpsosPagoHonorarios.Core.PalabraOcr(w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Height, w.Text));
            lineas.AddRange(IpsosPagoHonorarios.Core.LectorBoletaTexto.AgruparEnLineas(palabras));
        }
        return lineas;
    }
}
#endif

/// <summary>
/// Tesseract (en Linux, el del sistema: apt install tesseract-ocr; en Windows, x64\tesseract.exe del paquete TesseractOCR,
/// que requiere el runtime de Visual C++ 2015-2022). La página se convierte a imagen con pdfium (Docnet.Core).
/// </summary>
internal sealed class MotorOcrTesseract(OpcionesOcr op, IHostEnvironment env) : IMotorOcr
{
    private static readonly Lock Candado = new();   // pdfium no es seguro entre hilos
    private const int LadoMaximo = 3500;            // píxeles: ~300 ppp en una boleta tamaño carta

    public string Nombre => "Tesseract";

    public async Task<List<string>> LeerAsync(byte[] pdf, CancellationToken ct)
    {
        var temporales = new List<string>();
        try
        {
            var lineas = new List<string>();
            foreach (var imagen in Renderizar(pdf))
            {
                var ruta = Path.Combine(Path.GetTempPath(), $"boleta-ocr-{Guid.NewGuid():N}.pgm");
                await File.WriteAllBytesAsync(ruta, imagen, ct);
                temporales.Add(ruta);
                var texto = await EjecutarAsync(ruta, ct);
                lineas.AddRange(texto.Split('\n').Select(l => l.TrimEnd('\r')));
            }
            return lineas;
        }
        finally
        {
            foreach (var t in temporales) try { File.Delete(t); } catch { /* temporal */ }
        }
    }

    /// <summary>Cada página (máx. 2) en escala de grises sobre fondo blanco, formato PGM (lo lee Tesseract directamente).</summary>
    private static List<byte[]> Renderizar(byte[] pdf)
    {
        var imagenes = new List<byte[]>();
        lock (Candado)
        {
            double escala;
            using (var doc = DocLib.Instance.GetDocReader(pdf, new PageDimensions(1)))
            using (var p0 = doc.GetPageReader(0))
                escala = Math.Clamp(LadoMaximo / (double)Math.Max(p0.GetPageWidth(), p0.GetPageHeight()), 1, 5);
            using var lector = DocLib.Instance.GetDocReader(pdf, new PageDimensions(escala));
            for (var i = 0; i < Math.Min(lector.GetPageCount(), 2); i++)
            {
                using var pagina = lector.GetPageReader(i);
                int ancho = pagina.GetPageWidth(), alto = pagina.GetPageHeight();
                var bgra = pagina.GetImage();
                var encabezado = Encoding.ASCII.GetBytes($"P5\n{ancho} {alto}\n255\n");
                var pgm = new byte[encabezado.Length + ancho * alto];
                encabezado.CopyTo(pgm, 0);
                for (int px = 0, o = encabezado.Length; px < ancho * alto; px++, o++)
                {
                    int b = bgra[px * 4], g = bgra[px * 4 + 1], r = bgra[px * 4 + 2], a = bgra[px * 4 + 3];
                    var lum = (r * 299 + g * 587 + b * 114) / 1000;
                    pgm[o] = (byte)((lum * a + 255 * (255 - a)) / 255);   // lo transparente queda blanco
                }
                imagenes.Add(pgm);
            }
        }
        return imagenes;
    }

    private async Task<string> EjecutarAsync(string imagen, CancellationToken ct)
    {
        var exe = !string.IsNullOrWhiteSpace(op.Tesseract) ? op.Tesseract
            : OperatingSystem.IsWindows() && File.Exists(Path.Combine(AppContext.BaseDirectory, "x64", "tesseract.exe"))
                ? Path.Combine(AppContext.BaseDirectory, "x64", "tesseract.exe")
                : "tesseract";
        var tessdata = !string.IsNullOrWhiteSpace(op.Tessdata) ? op.Tessdata
            : new[] { Path.Combine(AppContext.BaseDirectory, "Ocr", "tessdata"), Path.Combine(env.ContentRootPath, "Ocr", "tessdata") }
                .FirstOrDefault(d => File.Exists(Path.Combine(d, op.Idioma + ".traineddata")));

        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var a in new[] { imagen, "stdout", "-l", op.Idioma, "--psm", "4" }) psi.ArgumentList.Add(a);   // psm 4: etiqueta y monto en la misma línea
        if (tessdata is not null)
        {
            psi.ArgumentList.Add("--tessdata-dir");
            psi.ArgumentList.Add(tessdata);
        }
        if (Path.GetDirectoryName(exe) is { Length: > 0 } dir) psi.WorkingDirectory = dir;   // DLL de Tesseract junto al exe

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"No se pudo iniciar {exe}.");
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(op.TimeoutSegundos));
        var salida = proc.StandardOutput.ReadToEndAsync(limite.Token);
        var errores = proc.StandardError.ReadToEndAsync(limite.Token);
        try { await proc.WaitForExitAsync(limite.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { proc.Kill(true); } catch { /* ya terminó */ }
            throw new TimeoutException($"Tesseract superó {op.TimeoutSegundos} s.");
        }
        if (proc.ExitCode != 0)
        {
            var codigo = unchecked((uint)proc.ExitCode);
            var pista = codigo == 0xC0000135 ? " Falta el runtime de Visual C++ 2015-2022 (x64)." : "";
            throw new InvalidOperationException($"{exe} terminó con código 0x{codigo:X8}.{pista} {(await errores).Trim()}");
        }
        return await salida;
    }
}
