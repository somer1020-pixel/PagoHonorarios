using System.Security.Claims;
using System.Text.Json;
using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Services;

/// <summary>Violación de una regla de negocio: el mensaje se muestra al usuario.</summary>
public class ReglaException(string mensaje) : Exception(mensaje);

public interface IUsuarioActual
{
    string? Nombre { get; }
    ClaimsPrincipal? Principal { get; }
}

public class HttpUsuarioActual(IHttpContextAccessor acc) : IUsuarioActual
{
    /// <summary>Para procesos sin petición HTTP (semilla de demo, tareas): nombre a registrar.</summary>
    public string? Suplantado { get; set; }
    public ClaimsPrincipal? Principal => acc.HttpContext?.User;
    public string? Nombre => Suplantado ?? (Principal?.Identity?.IsAuthenticated == true
        ? Principal.FindFirstValue("nombre") ?? Principal.Identity!.Name
        : "sistema");
}

public class UsuarioFijo(string nombre) : IUsuarioActual
{
    public string? Nombre { get; set; } = nombre;
    public ClaimsPrincipal? Principal => null;
}

public class OpcionesAlmacenamiento
{
    /// <summary>TODO(diseño): ruta, retención y respaldo de PDFs y ZIPs.</summary>
    public string Ruta { get; set; } = "App_Data/archivos";
}

/// <summary>Almacenamiento de archivos (PDF de boletas, comprobantes, XLSX por versión, ZIP de cierre).</summary>
public class Almacenamiento
{
    private readonly string _raiz;

    public Almacenamiento(Microsoft.Extensions.Options.IOptions<OpcionesAlmacenamiento> op, IHostEnvironment env)
    {
        _raiz = Path.IsPathRooted(op.Value.Ruta) ? op.Value.Ruta : Path.Combine(env.ContentRootPath, op.Value.Ruta);
        Directory.CreateDirectory(_raiz);
    }

    public Almacenamiento(string raiz)
    {
        _raiz = raiz;
        Directory.CreateDirectory(_raiz);
    }

    public string Guardar(string carpeta, string nombre, byte[] contenido)
    {
        var seguro = string.Concat(nombre.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var rel = Path.Combine(carpeta, $"{Guid.NewGuid():N}_{seguro}");
        var abs = Path.Combine(_raiz, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllBytes(abs, contenido);
        return rel.Replace('\\', '/');
    }

    public byte[] Leer(string rel) => File.ReadAllBytes(RutaAbsoluta(rel));

    public bool Existe(string rel) => !string.IsNullOrEmpty(rel) && File.Exists(RutaAbsoluta(rel));

    public string RutaAbsoluta(string rel)
    {
        var abs = Path.GetFullPath(Path.Combine(_raiz, rel));
        if (!abs.StartsWith(Path.GetFullPath(_raiz), StringComparison.Ordinal)) throw new UnauthorizedAccessException();
        return abs;
    }

    /// <summary>Nombre original (sin el prefijo único).</summary>
    public static string NombreOriginal(string rel)
    {
        var n = Path.GetFileName(rel);
        var i = n.IndexOf('_');
        return i == 32 ? n[(i + 1)..] : n;
    }
}

/// <summary>R-16: auditoría de cambios de estado, cambios bancarios, cargas de archivo, observaciones y acceso al portal.</summary>
public class Auditor(AppDbContext db, IUsuarioActual usuario, TimeProvider reloj)
{
    public void Registrar(string entidad, object? id, string accion, string? detalle = null) =>
        db.Auditorias.Add(new Auditoria
        {
            Fecha = reloj.GetUtcNow().UtcDateTime,
            Usuario = usuario.Nombre,
            Entidad = entidad,
            EntidadId = id?.ToString(),
            Accion = accion,
            Detalle = detalle
        });
}

/// <summary>
/// Avisos solo por correo (R-20, R-21, R-26). Se encolan en la bandeja de salida; el envío real depende del proveedor
/// SMTP (TODO(diseño)). En desarrollo quedan visibles en la bitácora.
/// </summary>
public class Correos(AppDbContext db, TimeProvider reloj, ILogger<Correos> log)
{
    public void Encolar(string? para, string asunto, string cuerpo)
    {
        if (string.IsNullOrWhiteSpace(para)) return;
        db.Correos.Add(new CorreoSaliente { CreadoEn = reloj.GetUtcNow().UtcDateTime, Para = para, Asunto = asunto, Cuerpo = cuerpo });
        log.LogInformation("Correo encolado para {Para}: {Asunto}", para, asunto);
    }
}

public class Parametros(AppDbContext db)
{
    public async Task<Parametro> ObtenerAsync() =>
        await db.Parametros.OrderBy(p => p.Id).FirstOrDefaultAsync() ?? new Parametro();

    /// <summary>R-05: tasa configurable por año (2026 = 0,1525).</summary>
    public async Task<decimal> TasaAsync(int anio)
    {
        var tasas = await db.TasasRetencion.AsNoTracking().ToListAsync();
        return tasas.Where(t => t.Anio <= anio).OrderByDescending(t => t.Anio).Select(t => t.Tasa).FirstOrDefault(0.1525m);
    }
}

public static class Json
{
    public static string Serializar<T>(T valor) => JsonSerializer.Serialize(valor);
    public static T? Leer<T>(string json) => JsonSerializer.Deserialize<T>(json);
}
