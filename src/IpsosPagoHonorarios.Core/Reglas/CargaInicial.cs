namespace IpsosPagoHonorarios.Core;

/// <summary>Fila de una planilla anterior: columnas I (Rut), J (nombre), O (tipo), P (cuenta), Q (banco).</summary>
public sealed record FilaCuentaHistorica(string Rut, string? Nombre, string TipoCuenta, string Cuenta, string Banco, DateOnly FechaPlanilla);

public sealed record CuentaPropuesta(int Rut, string Dv, string? Nombre, string Banco, string TipoCuenta, string Cuenta, DateOnly Fecha, bool Vigente);

public sealed record PlanCargaInicial(
    List<CuentaPropuesta> Unicas,
    List<CuentaPropuesta> ConVarias,
    List<(string Banco, string Cuenta, List<int> Ruts)> CompartidasNoAsociadas,
    List<string> Ignoradas);

/// <summary>R-28: carga inicial de cuentas desde planillas anteriores.</summary>
public static class CargaInicialReglas
{
    /// <summary>
    /// Cuenta única por RUT → Validada vigente. Cuentas distintas → la más reciente vigente, el resto Inactiva y el caso a
    /// revisión de Finanzas. Misma cuenta (banco + normalizada) en varios RUT → no se asocia a ninguno.
    /// </summary>
    public static PlanCargaInicial Planificar(IEnumerable<FilaCuentaHistorica> filas)
    {
        var ignoradas = new List<string>();
        var validas = new List<(int Rut, string Dv, FilaCuentaHistorica F, string Norm)>();
        foreach (var f in filas)
        {
            if (!RutHelper.TryParse(f.Rut, out var rut, out var dv)) { ignoradas.Add($"RUT inválido: {f.Rut}"); continue; }
            var norm = CuentaReglas.Normalizar(f.Cuenta);
            if (norm.Length == 0 || string.IsNullOrWhiteSpace(f.Banco)) { ignoradas.Add($"Sin cuenta: {f.Rut}"); continue; }
            validas.Add((rut, dv, f with { Banco = f.Banco.Trim(), TipoCuenta = f.TipoCuenta.Trim() }, norm));
        }

        var compartidas = validas
            .GroupBy(v => (Banco: v.F.Banco.ToUpperInvariant(), v.Norm))
            .Where(g => g.Select(v => v.Rut).Distinct().Count() > 1)
            .Select(g => (g.First().F.Banco, g.Key.Norm, g.Select(v => v.Rut).Distinct().Order().ToList()))
            .ToList();
        var claveCompartida = compartidas.Select(c => (c.Item1.ToUpperInvariant(), c.Item2)).ToHashSet();

        var unicas = new List<CuentaPropuesta>();
        var varias = new List<CuentaPropuesta>();
        foreach (var porRut in validas.Where(v => !claveCompartida.Contains((v.F.Banco.ToUpperInvariant(), v.Norm))).GroupBy(v => v.Rut))
        {
            var distintas = porRut
                .GroupBy(v => (Banco: v.F.Banco.ToUpperInvariant(), Tipo: v.F.TipoCuenta.ToUpperInvariant(), v.Norm))
                .Select(g => g.OrderByDescending(v => v.F.FechaPlanilla).First())
                .OrderByDescending(v => v.F.FechaPlanilla)
                .ToList();
            var nombre = porRut.OrderByDescending(v => v.F.FechaPlanilla).Select(v => v.F.Nombre).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            if (distintas.Count == 1)
            {
                var v = distintas[0];
                unicas.Add(new(v.Rut, v.Dv, nombre, v.F.Banco, v.F.TipoCuenta, v.F.Cuenta.Trim(), v.F.FechaPlanilla, true));
            }
            else
            {
                varias.AddRange(distintas.Select((v, i) =>
                    new CuentaPropuesta(v.Rut, v.Dv, nombre, v.F.Banco, v.F.TipoCuenta, v.F.Cuenta.Trim(), v.F.FechaPlanilla, i == 0)));
            }
        }
        return new(unicas, varias, compartidas, ignoradas);
    }
}
