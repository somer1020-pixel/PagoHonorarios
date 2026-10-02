using IpsosPagoHonorarios.Core;

namespace IpsosPagoHonorarios.Tests.Core;

/// <summary>R-01, R-03, R-04, R-05: RUT y montos.</summary>
public class ReglasBasicasTests
{
    [Theory]
    [InlineData("12.345.678-5")]
    [InlineData("12345678-5")]
    [InlineData("123456785")]
    [InlineData("16.543.210-K")]
    [InlineData("16543210-k")]
    [InlineData("19.876.543-0")]
    [InlineData(" 15.234.871-1 ")]
    public void R01_RutValido_ConOSinPuntosYGuion(string rut) => Assert.True(RutHelper.EsValido(rut));

    [Theory]
    [InlineData("15234871-9", "Dígito verificador incorrecto")]
    [InlineData("17.654.321-4", "Dígito verificador incorrecto")]
    [InlineData("12.345.678-X", "Formato inválido")]
    [InlineData("abc", "Formato inválido")]
    [InlineData("", "RUT vacío")]
    public void R01_RutInvalido(string rut, string motivo)
    {
        Assert.False(RutHelper.EsValido(rut));
        Assert.Equal(motivo, RutHelper.Error(rut));
    }

    [Fact]
    public void R01_FormatoUiYPlanilla()
    {
        Assert.Equal("12.345.678-5", RutHelper.Formatear("12345678-5"));
        Assert.Equal("12345678-5", RutHelper.Normalizar("12.345.678-5"));
        Assert.Equal("16543210-K", RutHelper.Normalizar("16.543.210-k"));
        Assert.Equal("K", RutHelper.CalcularDv(16543210));
        Assert.Equal("0", RutHelper.CalcularDv(19876543));
    }

    [Fact]
    public void R03_RedondeoExcel_ConCantidadDecimal()
    {
        Assert.Equal(21320m, Montos.ValorTotal(250m, 85.28m));        // caso de la especificación
        Assert.Equal(24188m, Montos.ValorTotal(250m, 96.75m));        // 24.187,5 → lejos de cero
        Assert.Equal(24961m, Montos.ValorTotal(250m, 99.845m));       // 24.961,25
        Assert.Equal(-3m, Montos.RedondearExcel(-2.5m));
        Assert.Equal(345000m, Montos.ValorTotal(6500m, 42m) + Montos.ValorTotal(7200m, 10m));
    }

    [Fact]
    public void R04_R05_RetencionPorBoleta_LiquidoSeTransfiere()
    {
        Assert.Equal(27450m, Montos.Retencion(180000m, 0.1525m));
        Assert.Equal(152550m, Montos.Liquido(180000m, 0.1525m));
        // Por boleta, no por línea: 2 filas de Camila.
        var (bruto, ret, liq) = Montos.PorBoleta([273000m, 72000m], 0.1525m);
        Assert.Equal((345000m, 52613m, 292387m), (bruto, ret, liq));
        // El redondeo se aplica una vez sobre el total de la boleta: 200 × 15,25 % = 30,5 → 31 (por línea serían 15 + 15).
        Assert.Equal(31m, Montos.PorBoleta([100m, 100m], 0.1525m).Retencion);
        Assert.Equal(30m, Montos.Retencion(100m, 0.1525m) * 2);
    }

    [Fact]
    public void R05_PagosAprobadosDeLaEspecificacion()
    {
        // 6 transferencias FACE TO FACE: bruto $1.650.100, retención $251.641, líquido $1.398.459.
        decimal[] brutos = [345000, 292000, 260000, 292500, 252000, 208600];
        var r = brutos.Select(b => Montos.PorBoleta([b], 0.1525m)).ToList();
        Assert.Equal(1650100m, r.Sum(x => x.Bruto));
        Assert.Equal(251641m, r.Sum(x => x.Retencion));
        Assert.Equal(1398459m, r.Sum(x => x.Liquido));
    }

    [Fact]
    public void Formato_Chile()
    {
        Assert.Equal("$1.234.567", Formato.Clp(1234567m));
        Assert.Equal("85,28", Formato.Cantidad(85.28m));
        Assert.Equal("05-11-2026", Formato.Fecha(new DateOnly(2026, 11, 5)));
        Assert.Equal("OCT-2026", Formato.CodigoCiclo(new DateOnly(2026, 10, 1)));
        Assert.Equal("Planilla honorarios OCTUBRE_2026 - FACE TO FACE v2.xlsx", Formato.NombreArchivoPlanilla(new DateOnly(2026, 10, 1), "FACE TO FACE", 2));
        Assert.Equal("••••3456", Formato.Enmascarar("1712343456"));
        Assert.Equal("15,25 %", Formato.Porcentaje(0.1525m));
    }
}
