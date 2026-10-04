namespace IpsosPagoHonorarios.Carga;

/// <summary>Lo que el generador deja para el ejecutor: usuarios, contraseñas de prueba y planillas del ciclo abierto.</summary>
public sealed class Escenario
{
    public string Contrasena { get; set; } = "";
    public string Ciclo { get; set; } = "";
    public List<UsuarioInterno> Operaciones { get; set; } = [];
    public List<UsuarioInterno> Finanzas { get; set; } = [];
    public List<PrestadorPortal> Prestadores { get; set; } = [];
    public List<int> PlanillasEnRevision { get; set; } = [];
    public List<int> CiclosCerrados { get; set; } = [];
    public List<string> Jobs { get; set; } = [];
    public List<string> Glosas { get; set; } = [];
}

public sealed record UsuarioInterno(string Email, string Area, int AreaId, List<int> Planillas);

/// <summary>Prestador con usuario activo en el portal y sus filas pendientes de boleta en una planilla del ciclo abierto.</summary>
public sealed record PrestadorPortal(string Rut, string Nombre, int PlanillaId, decimal Bruto);
