using System.Diagnostics;
using System.Text;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.Options;

namespace IpsosPagoHonorarios.Web.Services;

public class OpcionesOcr
{
    public bool Habilitado { get; set; } = true;
    /// <summary>Ruta de tesseract. Vacío: x64\tesseract.exe junto a la aplicación (Windows) o "tesseract" del sistema (Linux).</summary>
    public string? Tesseract { get; set; }
    /// <summary>Carpeta con {Idioma}.traineddata. Vacío: Ocr/tessdata de la aplicación.</summary>
    public string? Tessdata { get; set; }
    public string Idioma { get; set; } = "spa";
    public int TimeoutSegundos { get; set; } = 60;
}

/// <summary>Lee por OCR el texto de una boleta cuyo PDF no trae texto (p. ej. la boleta compartida desde la app del SII).</summary>
public interface ILectorOcr
{
    /// <summary>Líneas reconocidas, o null si la OCR no está disponible o falló (la boleta queda para corrección manual).</summary>
    Task<List<string>?> LeerAsync(byte[] pdf, CancellationToken ct = default);
}

public class LectorOcrTesseract(IOptions<OpcionesOcr> opciones, IHostEnvironment env, ILogger<LectorOcrTesseract> log) : ILectorOcr
{
    private static readonly Lock Candado = new();   // pdfium no es seguro entre hilos
    private const int LadoMaximo = 3500;            // píxeles: ~300 ppp en una boleta tamaño carta

    public async Task<List<string>?> LeerAsync(byte[] pdf, CancellationToken ct = default)
    {
        var op = opciones.Value;
        if (!op.Habilitado) return null;
        var temporales = new List<string>();
        try
        {
            var lineas = new List<string>();
            foreach (var imagen in Renderizar(pdf))
            {
                var ruta = Path.Combine(Path.GetTempPath(), $"boleta-ocr-{Guid.NewGuid():N}.pgm");
                await File.WriteAllBytesAsync(ruta, imagen, ct);
                temporales.Add(ruta);
                var texto = await TesseractAsync(ruta, op, ct);
                if (texto is null) return null;
                lineas.AddRange(texto.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l)));
            }
            return lineas.Count == 0 ? null : lineas;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "OCR de boleta no disponible: la boleta queda para corrección manual.");
            return null;
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

    private async Task<string?> TesseractAsync(string imagen, OpcionesOcr op, CancellationToken ct)
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
        psi.ArgumentList.Add(imagen);
        psi.ArgumentList.Add("stdout");
        psi.ArgumentList.Add("-l");
        psi.ArgumentList.Add(op.Idioma);
        psi.ArgumentList.Add("--psm");
        psi.ArgumentList.Add("4");   // columna de texto de tamaño variable: mantiene etiqueta y monto en la misma línea
        if (tessdata is not null)
        {
            psi.ArgumentList.Add("--tessdata-dir");
            psi.ArgumentList.Add(tessdata);
        }
        if (Path.GetDirectoryName(exe) is { Length: > 0 } dir) psi.WorkingDirectory = dir;   // DLL de Tesseract junto al exe

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar Tesseract.");
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(op.TimeoutSegundos));
        var salida = proc.StandardOutput.ReadToEndAsync(limite.Token);
        var errores = proc.StandardError.ReadToEndAsync(limite.Token);
        try { await proc.WaitForExitAsync(limite.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { proc.Kill(true); } catch { /* ya terminó */ }
            log.LogWarning("Tesseract superó {S} s.", op.TimeoutSegundos);
            return null;
        }
        if (proc.ExitCode != 0)
        {
            log.LogWarning("Tesseract terminó con código {C}: {E}", proc.ExitCode, await errores);
            return null;
        }
        return await salida;
    }
}
