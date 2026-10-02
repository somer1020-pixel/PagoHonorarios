namespace IpsosPagoHonorarios.Core;

public enum CicloEstado { Abierto, Cerrado }

/// <summary>Borrador → ConAlertasCuenta ⇄ Borrador → EnRevision → Observada → EnRevision → Aprobada → EnPago → Cerrada.</summary>
public enum PlanillaEstado { Borrador, ConAlertasCuenta, EnRevision, Observada, Aprobada, EnPago, Cerrada }

/// <summary>PendienteBoleta → Lista → Observada → Corregida → Lista → Aprobada → Pagada; desde PendienteBoleta u Observada → Diferida.</summary>
public enum LineaEstado { PendienteBoleta, Lista, Observada, Corregida, Aprobada, Pagada, Diferida }

public enum CuentaEstado { PendienteValidacion, Validada, Inactiva, Rechazada }

public enum CuentaOrigen { CargaInicial, Registro }

public enum BoletaEstado { Recibida, Confirmada, Observada, Reemplazada, Rechazada }

public enum BoletaCanal { Portal, Operaciones }

public enum Confianza { Alta, Media, Baja }

public enum ObservacionEstado { Abierta, Corregida, Aceptada, Vencida }

public enum ObservacionTipo
{
    ErrorDigitacion,
    DatosIncompletos,
    FaltaBoleta,
    DiferenciaMontos,
    InconsistenciaNumerica,
    DatosBancariosIncorrectos
}

public enum DevolucionResultado { Pendiente, ReenviadaATiempo, Vencida }

/// <summary>TODO(diseño): estados de SolicitudCorreccion no enumerados en la especificación.</summary>
public enum SolicitudEstado { Enviada, Reenviada }

/// <summary>R-24.</summary>
public enum ResultadoCuenta
{
    Coincide,
    PosibleErrorTipeo,
    CuentaNueva,
    DistintaRegistrada,
    CuentaTercero,
    SinCuentaPlanilla
}

public static class Roles
{
    public const string Operaciones = "Operaciones";
    public const string Finanzas = "Finanzas";
    public const string Prestador = "Prestador";
    public const string Admin = "Admin";
    public const string CEX = "CEX";
    public const string Public = "Public";
    public const string BHT = "BHT";
    public const string MSU = "MSU";
    public const string AUM = "AUM";

    /// <summary>
    /// Perfiles que gestionan procesos de pago igual que Operaciones: mismas pantallas y permisos. Al ingresar, la identidad
    /// recibe además el rol Operaciones (ver FabricaClaims), así que toda regla de Operaciones aplica también a ellos.
    /// </summary>
    public static readonly string[] ComoOperaciones = [CEX, Public, BHT, MSU, AUM];

    /// <summary>Perfiles internos asignables en Maestros → Usuarios.</summary>
    public static readonly string[] Internos = [Operaciones, .. ComoOperaciones, Finanzas, Admin];
    /// <summary>Los perfiles específicos van antes que Operaciones para mostrarse en el encabezado.</summary>
    public static readonly string[] Todos = [.. ComoOperaciones, Operaciones, Finanzas, Prestador, Admin];
}

public static class Textos
{
    public static string Nombre(this ObservacionTipo t) => t switch
    {
        ObservacionTipo.ErrorDigitacion => "Error de digitación",
        ObservacionTipo.DatosIncompletos => "Datos incompletos",
        ObservacionTipo.FaltaBoleta => "Falta de boleta",
        ObservacionTipo.DiferenciaMontos => "Diferencia entre montos",
        ObservacionTipo.InconsistenciaNumerica => "Inconsistencia numérica",
        ObservacionTipo.DatosBancariosIncorrectos => "Datos bancarios incorrectos",
        _ => t.ToString()
    };

    public static string Nombre(this ResultadoCuenta r) => r switch
    {
        ResultadoCuenta.Coincide => "Coincide",
        ResultadoCuenta.PosibleErrorTipeo => "Posible error de tipeo",
        ResultadoCuenta.CuentaNueva => "Cuenta nueva",
        ResultadoCuenta.DistintaRegistrada => "Distinta a la registrada",
        ResultadoCuenta.CuentaTercero => "Cuenta de tercero",
        ResultadoCuenta.SinCuentaPlanilla => "Sin cuenta en la planilla",
        _ => r.ToString()
    };

    public static string Nombre(this PlanillaEstado e) => e switch
    {
        PlanillaEstado.ConAlertasCuenta => "Con alertas de cuenta",
        PlanillaEstado.EnRevision => "En revisión",
        PlanillaEstado.EnPago => "En pago",
        _ => e.ToString()
    };

    public static string Nombre(this LineaEstado e) => e switch
    {
        LineaEstado.PendienteBoleta => "Pendiente boleta",
        _ => e.ToString()
    };

    public static string Nombre(this CuentaEstado e) => e switch
    {
        CuentaEstado.PendienteValidacion => "Pendiente de validación",
        _ => e.ToString()
    };

    /// <summary>R-24: resultados que constituyen alerta (Coincide y Sin cuenta no generan alerta).</summary>
    public static bool EsAlerta(this ResultadoCuenta r) =>
        r is not (ResultadoCuenta.Coincide or ResultadoCuenta.SinCuentaPlanilla);
}
