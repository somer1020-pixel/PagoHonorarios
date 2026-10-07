using IpsosPagoHonorarios.Core;

namespace IpsosPagoHonorarios.Tests.Core;

/// <summary>R-07 (registro de cuentas), R-24 (validación al subir la planilla) y R-28 (carga inicial).</summary>
public class CuentasTests
{
    private static readonly string[] Bancos = ["ESTADO", "CHILE", "BANEFE", "BCI", "SANTANDER", "MERCADO PAGO", "Tenpo"];
    private static readonly string[] Tipos = ["CORRIENTE", "VISTA", "RUT", "AHORRO", "CHEQUERA ELECTRONICA", "DEBITO"];

    private static List<string> Registrar(int rut, string banco, string tipo, string cuenta, string? repetida = null, int? dueno = null) =>
        CuentaReglas.ValidarRegistro(rut, banco, tipo, cuenta, repetida ?? cuenta, Bancos, Tipos, (_, _) => dueno);

    [Fact]
    public void R07_CuentaValida() => Assert.Empty(Registrar(17345120, "BANEFE", "VISTA", "1712343456"));

    [Fact]
    public void R07_TipoRut_ExigeBancoEstadoYRutSinDv()
    {
        Assert.Empty(Registrar(17654321, "ESTADO", "RUT", "17654321"));
        Assert.Contains(Registrar(17654321, "CHILE", "RUT", "17654321"), e => e.Contains("ESTADO"));
        Assert.Contains(Registrar(17654321, "ESTADO", "RUT", "17654320"), e => e.Contains("RUT sin dígito"));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("123456789012345678901")]
    [InlineData("12-34567")]
    public void R07_SoloDigitosEntre6y20(string cuenta) => Assert.Contains(Registrar(1, "BCI", "CORRIENTE", cuenta), e => e.Contains("6 y 20"));

    [Fact]
    public void R07_DobleIngreso_Catalogo_Tercero()
    {
        Assert.Contains(Registrar(1, "BCI", "CORRIENTE", "77889900", "77889901"), e => e.Contains("no coinciden"));
        Assert.Contains(Registrar(1, "BANCO X", "CORRIENTE", "77889900"), e => e.Contains("banco"));
        Assert.Contains(Registrar(1, "BCI", "PLAZO", "77889900"), e => e.Contains("tipo de cuenta"));
        Assert.Contains(Registrar(17456789, "CHILE", "CORRIENTE", "0012345678", dueno: 16987452), e => e.Contains("tercero"));
        Assert.Empty(Registrar(16987452, "CHILE", "CORRIENTE", "0012345678", dueno: 16987452));
    }

    [Fact]
    public void Normalizacion_Y_DamerauLevenshtein()
    {
        Assert.Equal("71234560", CuentaReglas.Normalizar("0071234560"));
        Assert.Equal(1, CuentaReglas.DamerauLevenshtein("71234560", "71234506")); // transposición
        Assert.Equal(1, CuentaReglas.DamerauLevenshtein("18234560", "18234567"));
        Assert.Equal(2, CuentaReglas.DamerauLevenshtein("1234", "2143"));
        Assert.True(CuentaReglas.DamerauLevenshtein("12345678", "45678123") > 2);
    }

    private static readonly Func<string, string, (int, string)?> SinDuenos = (_, _) => null;

    [Fact]
    public void R24_LosSeisResultados()
    {
        var reg = new CuentaRef("SANTANDER", "CORRIENTE", "0071234506");
        Assert.Equal(ResultadoCuenta.Coincide,
            CuentaReglas.Clasificar(1, new CuentaRef("SANTANDER", "CORRIENTE", "71234506"), reg, SinDuenos, []).Resultado);
        Assert.Equal(ResultadoCuenta.PosibleErrorTipeo,
            CuentaReglas.Clasificar(1, new CuentaRef("SANTANDER", "CORRIENTE", "0071234560"), reg, SinDuenos, []).Resultado);
        Assert.Equal(ResultadoCuenta.CuentaNueva,
            CuentaReglas.Clasificar(1, new CuentaRef("BANEFE", "VISTA", "1712345678"), null, SinDuenos, []).Resultado);
        Assert.Equal(ResultadoCuenta.DistintaRegistrada,
            CuentaReglas.Clasificar(1, new CuentaRef("MERCADO PAGO", "VISTA", "1029384756"), new CuentaRef("BCI", "CORRIENTE", "77889900"), SinDuenos, []).Resultado);
        Assert.Equal(ResultadoCuenta.CuentaTercero,
            CuentaReglas.Clasificar(17456789, new CuentaRef("CHILE", "CORRIENTE", "0012345678"), new CuentaRef("CHILE", "CORRIENTE", "0045678123"),
                (b, n) => b == "CHILE" && n == "12345678" ? (16987452, "Matías") : null, []).Resultado);
        Assert.Equal(ResultadoCuenta.SinCuentaPlanilla,
            CuentaReglas.Clasificar(1, null, reg, SinDuenos, []).Resultado);
    }

    [Fact]
    public void R24_TipeoPorTipoRut_YPorFilasDistintas()
    {
        var reg = new CuentaRef("ESTADO", "RUT", "18234567");
        Assert.Equal(ResultadoCuenta.PosibleErrorTipeo, CuentaReglas.Clasificar(18234567, new CuentaRef("ESTADO", "RUT", "18234560"), reg, SinDuenos, []).Resultado);
        var mismaFila = new CuentaRef("BANEFE", "VISTA", "1798765432");
        Assert.Equal(ResultadoCuenta.PosibleErrorTipeo,
            CuentaReglas.Clasificar(1, mismaFila, mismaFila, SinDuenos, [new CuentaRef("BANEFE", "VISTA", "1798765433")]).Resultado);
        Assert.Equal(ResultadoCuenta.Coincide, CuentaReglas.Clasificar(1, mismaFila, mismaFila, SinDuenos, [mismaFila]).Resultado);
        // Mismo banco y tipo pero muy distinta → Distinta a la registrada.
        Assert.Equal(ResultadoCuenta.DistintaRegistrada,
            CuentaReglas.Clasificar(1, new CuentaRef("CHILE", "CORRIENTE", "11112222"), new CuentaRef("CHILE", "CORRIENTE", "99998888"), SinDuenos, []).Resultado);
    }

    [Fact]
    public void R24_FilasDistintas_LaAlertaDiceCualFilaDifiere()
    {
        // Un prestador con 4 filas iguales (RUT · ESTADO 20535454) y una fila 42 con el mismo número pero tipo VISTA.
        var rutF35 = new CuentaRef("ESTADO", "RUT", "20535454", 35);
        var rutF36 = new CuentaRef("ESTADO", "RUT", "20535454", 36);
        var rutF37 = new CuentaRef("ESTADO", "RUT", "20535454", 37);
        var vistaF42 = new CuentaRef("ESTADO", "VISTA", "20535454", 42);

        // Las filas iguales a la registrada siguen marcadas (regla R-24), pero ahora la alerta señala la fila distinta.
        var igual = CuentaReglas.Clasificar(20535454, rutF35, rutF35, SinDuenos, [rutF36, rutF37, vistaF42]);
        Assert.Equal(ResultadoCuenta.PosibleErrorTipeo, igual.Resultado);
        Assert.Equal("Filas del mismo prestador con cuentas distintas: la fila 42 trae VISTA · ESTADO 20535454.", igual.Detalle);

        // La fila distinta ve que las demás traen otra cuenta.
        var distinta = CuentaReglas.Clasificar(20535454, vistaF42, rutF35, SinDuenos, [rutF35, rutF36, rutF37]);
        Assert.Equal(ResultadoCuenta.PosibleErrorTipeo, distinta.Resultado);
        Assert.Equal("Filas del mismo prestador con cuentas distintas: la fila 35 trae RUT · ESTADO 20535454 (y 2 filas más con otra cuenta).", distinta.Detalle);

        // Una fila con el número pero sin banco ni tipo también es "distinta", y se dice.
        var sinBanco = new CuentaRef("", "", "20535454", 50);
        Assert.Contains("la fila 50 trae sin banco ni tipo 20535454",
            CuentaReglas.Clasificar(20535454, rutF35, rutF35, SinDuenos, [sinBanco]).Detalle);

        // Sin número de fila (otros usos) el mensaje sigue siendo claro.
        Assert.Equal("Filas del mismo prestador con cuentas distintas: otra fila trae VISTA · BANEFE 1798765433.",
            CuentaReglas.Clasificar(1, new CuentaRef("BANEFE", "VISTA", "1798765432"), null, SinDuenos, [new CuentaRef("BANEFE", "VISTA", "1798765433")]).Detalle);

        // Mismo banco, tipo y número (aunque con ceros o puntos) no es una diferencia.
        Assert.Equal(ResultadoCuenta.Coincide,
            CuentaReglas.Clasificar(20535454, rutF35, rutF35, SinDuenos, [new CuentaRef("estado", "rut", "020.535.454", 40)]).Resultado);
    }

    [Fact]
    public void R24_DigitosDistintosResaltados()
    {
        var m = CuentaReglas.DigitosDistintos("0071234560", "0071234506");
        Assert.Equal([false, false, false, false, false, false, false, false, true, true], m);
    }

    [Fact]
    public void R28_CargaInicial_TresCasos()
    {
        var plan = CargaInicialReglas.Planificar(
        [
            // Cuenta única (aparece dos veces igual) → Validada vigente.
            new("15234871-1", "Camila", "VISTA", "1734567890", "BANEFE", new(2026, 7, 1)),
            new("15234871-1", "Camila", "VISTA", "1734567890", "BANEFE", new(2026, 8, 1)),
            // Cuentas distintas → la más reciente vigente, el resto Inactiva, a revisión.
            new("17654321-3", "Agustín", "VISTA", "1100987654", "Tenpo", new(2026, 7, 1)),
            new("17654321-3", "Agustín", "RUT", "17654321", "ESTADO", new(2026, 9, 1)),
            // Misma cuenta en dos RUT → no se asocia a ninguno.
            new("18765432-7", "Martina", "VISTA", "1100223344", "Tenpo", new(2026, 7, 1)),
            new("15678901-1", "Lucas", "VISTA", "1100223344", "Tenpo", new(2026, 8, 1)),
        ]);
        var unica = Assert.Single(plan.Unicas);
        Assert.Equal(15234871, unica.Rut);
        Assert.Equal(2, plan.ConVarias.Count);
        Assert.Equal("17654321", plan.ConVarias.Single(c => c.Vigente).Cuenta);
        var compartida = Assert.Single(plan.CompartidasNoAsociadas);
        Assert.Equal([15678901, 18765432], compartida.Ruts);
    }
}
