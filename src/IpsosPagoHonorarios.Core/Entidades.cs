namespace IpsosPagoHonorarios.Core;

/// <summary>Todas las entidades llevan CreadoEn/Por y ModificadoEn/Por (los completa el DbContext).</summary>
public abstract class EntidadAuditable
{
    public int Id { get; set; }
    public DateTime CreadoEn { get; set; }
    public string? CreadoPor { get; set; }
    public DateTime? ModificadoEn { get; set; }
    public string? ModificadoPor { get; set; }
}

public class Ciclo : EntidadAuditable
{
    /// <summary>Día 1 del mes; único.</summary>
    public DateOnly Periodo { get; set; }
    /// <summary>Ej.: OCT-2026.</summary>
    public string Codigo { get; set; } = "";
    public CicloEstado Estado { get; set; } = CicloEstado.Abierto;
    public DateOnly FechaPagoProgramada { get; set; }
    public DateTime? CerradoEn { get; set; }
    public string? CerradoPor { get; set; }
    public string? RutaZip { get; set; }
    public List<Planilla> Planillas { get; set; } = [];
}

public class Area : EntidadAuditable
{
    public string Nombre { get; set; } = "";
    public string CodigoArea { get; set; } = "";
}

/// <summary>Ítem presupuestario.</summary>
public class Glosa : EntidadAuditable
{
    public string NombreGlosa { get; set; } = "";
    public string Item { get; set; } = "";
    public string CuentaContable { get; set; } = "";
}

public class Banco : EntidadAuditable
{
    public string Nombre { get; set; } = "";
    public string CodigoBanco { get; set; } = "";
}

public class TipoCuenta : EntidadAuditable
{
    public string Nombre { get; set; } = "";
    public string Codigo { get; set; } = "";
}

public class TipoGasto : EntidadAuditable
{
    public string Nombre { get; set; } = "";
}

public class Job : EntidadAuditable
{
    /// <summary>12 dígitos, único.</summary>
    public string JobBookNumber { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int? AreaSugeridaId { get; set; }
    public Area? AreaSugerida { get; set; }
    public decimal? ValorUnitarioSugerido { get; set; }
    public bool Activo { get; set; } = true;
}

public class Planilla : EntidadAuditable
{
    public int CicloId { get; set; }
    public Ciclo Ciclo { get; set; } = null!;
    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;
    public DateOnly FechaRecepcion { get; set; }
    public string ResponsableNombre { get; set; } = "";
    public string ResponsableEmail { get; set; } = "";
    public PlanillaEstado Estado { get; set; } = PlanillaEstado.Borrador;
    public int Version { get; set; } = 1;
    public List<LineaPago> Lineas { get; set; } = [];
    public List<BoletaHonorarios> Boletas { get; set; } = [];
    public List<Devolucion> Devoluciones { get; set; } = [];
}

/// <summary>XLSX archivado de cada versión de la planilla (R-11, R-15).</summary>
public class PlanillaArchivo : EntidadAuditable
{
    public int PlanillaId { get; set; }
    public Planilla Planilla { get; set; } = null!;
    public int Version { get; set; }
    /// <summary>Planilla (XLSX de la versión), Original (archivo subido) o Nomina.</summary>
    public string Tipo { get; set; } = "Planilla";
    public string NombreArchivo { get; set; } = "";
    public string Ruta { get; set; } = "";
}

public class Prestador : EntidadAuditable
{
    public int Rut { get; set; }
    public string Dv { get; set; } = "";
    public string NombreCompleto { get; set; } = "";
    public string? Email { get; set; }
    public string? Telefono { get; set; }
    public string? UsuarioId { get; set; }
    public DateTime? PortalInvitadoEn { get; set; }
    public DateTime? PortalActivadoEn { get; set; }
    public bool Activo { get; set; } = true;
    public List<CuentaBancaria> Cuentas { get; set; } = [];

    /// <summary>Formato planilla: 12345678-5 (también es el UserName del portal).</summary>
    public string RutPlanilla => $"{Rut}-{Dv}";
    public string RutUi => RutHelper.Formatear(Rut, Dv);
}

public class CuentaBancaria : EntidadAuditable
{
    public int PrestadorId { get; set; }
    public Prestador Prestador { get; set; } = null!;
    public string Banco { get; set; } = "";
    public string TipoCuenta { get; set; } = "";
    public string Cuenta { get; set; } = "";
    /// <summary>Solo dígitos, sin ceros a la izquierda.</summary>
    public string CuentaNormalizada { get; set; } = "";
    public CuentaEstado Estado { get; set; } = CuentaEstado.PendienteValidacion;
    /// <summary>Una sola vigente por prestador.</summary>
    public bool Vigente { get; set; }
    public CuentaOrigen Origen { get; set; } = CuentaOrigen.Registro;
    public string? OrigenDetalle { get; set; }
    /// <summary>R-28: caso con cuentas distintas en planillas anteriores, a revisión de Finanzas.</summary>
    public bool RequiereRevision { get; set; }
    public string? RegistradaPor { get; set; }
    public DateTime? RegistradaEn { get; set; }
    public string? ValidadaPor { get; set; }
    public DateTime? ValidadaEn { get; set; }
    public DateTime? ReemplazadaEn { get; set; }

    public string Descripcion => $"{TipoCuenta} · {Banco} · {Cuenta}";
}

public class LineaPago : EntidadAuditable
{
    public int PlanillaId { get; set; }
    public Planilla Planilla { get; set; } = null!;
    public int Numero { get; set; }
    public string TipoGasto { get; set; } = "";
    public int JobId { get; set; }
    public Job Job { get; set; } = null!;
    public int GlosaId { get; set; }
    public Glosa Glosa { get; set; } = null!;
    public decimal ValorUnitarioBruto { get; set; }
    public decimal Cantidad { get; set; }
    public decimal ValorTotalBruto { get; set; }
    public int PrestadorId { get; set; }
    public Prestador Prestador { get; set; } = null!;
    /// <summary>Nombre tal como vino en la planilla (columna J).</summary>
    public string NombrePlanilla { get; set; } = "";
    public string? NumeroBoleta { get; set; }
    public string? CuentaPlanillaTipo { get; set; }
    public string? CuentaPlanillaNumero { get; set; }
    public string? CuentaPlanillaBanco { get; set; }
    public int? CuentaBancariaId { get; set; }
    public CuentaBancaria? CuentaBancaria { get; set; }
    public ResultadoCuenta ResultadoCuenta { get; set; }
    public string? ResultadoCuentaDetalle { get; set; }
    public bool AlertaCuentaResuelta { get; set; }
    public LineaEstado Estado { get; set; } = LineaEstado.PendienteBoleta;
    public int? DiferidaDesdeCicloId { get; set; }
    public int? DiferidaACicloId { get; set; }

    public bool AlertaAbierta => ResultadoCuenta.EsAlerta() && !AlertaCuentaResuelta && Estado != LineaEstado.Diferida;
}

public class BoletaHonorarios : EntidadAuditable
{
    public int PlanillaId { get; set; }
    public Planilla Planilla { get; set; } = null!;
    public int PrestadorId { get; set; }
    public Prestador Prestador { get; set; } = null!;
    public BoletaCanal Canal { get; set; }
    public string? MotivoCargaOperaciones { get; set; }
    public string? SubidaPor { get; set; }
    public DateTime SubidaEn { get; set; }
    public BoletaEstado Estado { get; set; } = BoletaEstado.Recibida;
    public string? NumeroBoleta { get; set; }
    public int? RutEmisor { get; set; }
    public string? RutEmisorDv { get; set; }
    public string? NombreEmisor { get; set; }
    public string? RutReceptor { get; set; }
    public DateOnly? FechaEmision { get; set; }
    public decimal? MontoBruto { get; set; }
    public decimal? MontoRetencion { get; set; }
    public decimal? MontoLiquido { get; set; }
    public string RutaPdf { get; set; } = "";
    public string HashPdf { get; set; } = "";
    public string? TextoExtraido { get; set; }
    public Confianza Confianza { get; set; }
    public string? ConfirmadaPor { get; set; }
    public DateTime? ConfirmadaEn { get; set; }
    public int? ReemplazaABoletaId { get; set; }
    /// <summary>Resultado de la conciliación R-06 (vacío = cuadra).</summary>
    public string? ResultadoValidacion { get; set; }
    public bool Cuadra { get; set; }

    public bool Vigente => Estado is not (BoletaEstado.Reemplazada or BoletaEstado.Rechazada);
}

public class Observacion : EntidadAuditable
{
    public int LineaPagoId { get; set; }
    public LineaPago LineaPago { get; set; } = null!;
    public ObservacionTipo Tipo { get; set; }
    public string Campo { get; set; } = "";
    public string Detalle { get; set; } = "";
    public ObservacionEstado Estado { get; set; } = ObservacionEstado.Abierta;
    public string? CreadaPor { get; set; }
    public DateTime CreadaEn { get; set; }
    public string? CorregidaPor { get; set; }
    public DateTime? CorregidaEn { get; set; }
    public string? Respuesta { get; set; }
    public int? DevolucionId { get; set; }
    public Devolucion? Devolucion { get; set; }
}

public class Devolucion : EntidadAuditable
{
    public int PlanillaId { get; set; }
    public Planilla Planilla { get; set; } = null!;
    public int Version { get; set; }
    public DateTime DevueltaEn { get; set; }
    public string? DevueltaPor { get; set; }
    public DateTime VenceEn { get; set; }
    public DateTime? ReenviadaEn { get; set; }
    public DevolucionResultado Resultado { get; set; } = DevolucionResultado.Pendiente;
}

public class SolicitudCorreccion : EntidadAuditable
{
    public int PlanillaId { get; set; }
    public Planilla Planilla { get; set; } = null!;
    public int Version { get; set; }
    public string EnviadaA { get; set; } = "";
    public string? EnviadaPor { get; set; }
    public DateTime EnviadaEn { get; set; }
    public string Mensaje { get; set; } = "";
    /// <summary>JSON con N° de fila, resultado y qué corregir.</summary>
    public string Filas { get; set; } = "[]";
    public SolicitudEstado Estado { get; set; } = SolicitudEstado.Enviada;
    public int Envios { get; set; } = 1;
}

/// <summary>Una por boleta.</summary>
public class Transferencia : EntidadAuditable
{
    public int PlanillaId { get; set; }
    public Planilla Planilla { get; set; } = null!;
    public int PrestadorId { get; set; }
    public Prestador Prestador { get; set; } = null!;
    public int BoletaId { get; set; }
    public BoletaHonorarios Boleta { get; set; } = null!;
    public int CuentaBancariaId { get; set; }
    public CuentaBancaria CuentaBancaria { get; set; } = null!;
    public DateOnly Fecha { get; set; }
    public string NumeroOperacion { get; set; } = "";
    public decimal MontoLiquido { get; set; }
    public string Comprobante { get; set; } = "";
}

public class Auditoria
{
    public long Id { get; set; }
    public DateTime Fecha { get; set; }
    public string? Usuario { get; set; }
    public string Entidad { get; set; } = "";
    public string? EntidadId { get; set; }
    public string Accion { get; set; } = "";
    public string? Detalle { get; set; }
}

public class Parametro : EntidadAuditable
{
    public int PlazoCorreccionMinutos { get; set; } = 60;
    /// <summary>TODO(diseño): RUT y razón social reales de la empresa receptora.</summary>
    public string RutEmpresa { get; set; } = "77777777-7";
    public string RazonSocialEmpresa { get; set; } = "TODO(diseño): razón social";
    public int DiaDescargaDesde { get; set; } = 28;
    public int DiaDescargaHasta { get; set; } = 30;
    public int DiaPago { get; set; } = 5;
    public int DiaLimiteBoleta { get; set; } = 10;
}

/// <summary>R-05: tasa de retención configurable por año.</summary>
public class TasaRetencion : EntidadAuditable
{
    public int Anio { get; set; }
    public decimal Tasa { get; set; }
}

/// <summary>Bandeja de correos salientes (R-21, R-26). TODO(diseño): proveedor SMTP.</summary>
public class CorreoSaliente
{
    public long Id { get; set; }
    public DateTime CreadoEn { get; set; }
    public string Para { get; set; } = "";
    public string Asunto { get; set; } = "";
    public string Cuerpo { get; set; } = "";
    public DateTime? EnviadoEn { get; set; }
    public string? Error { get; set; }
}
