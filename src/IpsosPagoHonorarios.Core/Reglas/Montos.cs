namespace IpsosPagoHonorarios.Core;

public static class Montos
{
    /// <summary>ROUND(x, 0) como Excel: el medio se aleja de cero.</summary>
    public static decimal RedondearExcel(decimal valor) => Math.Round(valor, 0, MidpointRounding.AwayFromZero);

    /// <summary>R-03: valor total bruto = ROUND(valor unitario × cantidad, 0).</summary>
    public static decimal ValorTotal(decimal valorUnitario, decimal cantidad) => RedondearExcel(valorUnitario * cantidad);

    /// <summary>R-05: retención = ROUND(bruto × tasa). Se calcula por boleta, no por línea.</summary>
    public static decimal Retencion(decimal bruto, decimal tasa) => RedondearExcel(bruto * tasa);

    /// <summary>R-04/R-05: se transfiere el líquido = bruto − retención.</summary>
    public static decimal Liquido(decimal bruto, decimal tasa) => bruto - Retencion(bruto, tasa);

    /// <summary>R-05 aplicado por boleta: la base es la suma de las filas del prestador.</summary>
    public static (decimal Bruto, decimal Retencion, decimal Liquido) PorBoleta(IEnumerable<decimal> totalesFilas, decimal tasa)
    {
        var bruto = totalesFilas.Sum();
        var ret = Retencion(bruto, tasa);
        return (bruto, ret, bruto - ret);
    }
}
