namespace IpsosPagoHonorarios.Core;

/// <summary>Cuenta tal como se compara: banco, tipo y número normalizado. <paramref name="Fila"/> = N° de fila de la planilla, si viene de una.</summary>
public sealed record CuentaRef(string Banco, string TipoCuenta, string Numero, int? Fila = null)
{
    /// <summary>Como se muestra en pantalla: "RUT · ESTADO 20535454"; si faltan banco y tipo lo dice.</summary>
    public string Texto
    {
        get
        {
            var partes = new[] { TipoCuenta, Banco }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            return (partes.Length == 0 ? "sin banco ni tipo" : string.Join(" · ", partes)) + " " + Numero.Trim();
        }
    }

    public string Normalizada => CuentaReglas.Normalizar(Numero);
    public bool MismoBancoYTipo(CuentaRef o) =>
        string.Equals(Banco, o.Banco, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(TipoCuenta, o.TipoCuenta, StringComparison.OrdinalIgnoreCase);
    public bool Igual(CuentaRef o) => MismoBancoYTipo(o) && Normalizada == o.Normalizada;
}

public sealed record Clasificacion(ResultadoCuenta Resultado, string Detalle);

public static class CuentaReglas
{
    public const string TipoRut = "RUT";
    public const string BancoEstado = "ESTADO";

    /// <summary>Solo dígitos y sin ceros a la izquierda.</summary>
    public static string Normalizar(string? cuenta)
    {
        if (string.IsNullOrEmpty(cuenta)) return "";
        var digitos = new string(cuenta.Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
        return digitos;
    }

    /// <summary>Distancia Damerau-Levenshtein (alineamiento óptimo de cadenas: inserción, borrado, sustitución, transposición adyacente).</summary>
    public static int DamerauLevenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
        {
            var costo = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + costo);
            if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
        }
        return d[a.Length, b.Length];
    }

    /// <summary>
    /// R-07: banco y tipo en catálogo; tipo RUT ⇒ banco ESTADO y cuenta = RUT sin DV; solo dígitos, 6 a 20;
    /// ingresada dos veces; no puede ser (banco + normalizada) de otro RUT. Devuelve la lista de errores.
    /// </summary>
    public static List<string> ValidarRegistro(
        int rutPrestador, string? banco, string? tipo, string? cuenta, string? cuentaRepetida,
        IReadOnlyCollection<string> bancosCatalogo, IReadOnlyCollection<string> tiposCatalogo,
        Func<string, string, int?> duenoDeCuenta)
    {
        var errores = new List<string>();
        if (string.IsNullOrWhiteSpace(banco) || !bancosCatalogo.Contains(banco, StringComparer.OrdinalIgnoreCase))
            errores.Add("El banco no está en el catálogo de Finanzas.");
        if (string.IsNullOrWhiteSpace(tipo) || !tiposCatalogo.Contains(tipo, StringComparer.OrdinalIgnoreCase))
            errores.Add("El tipo de cuenta no está en el catálogo de Finanzas.");
        cuenta = cuenta?.Trim() ?? "";
        if (cuenta.Length is < 6 or > 20 || !cuenta.All(char.IsAsciiDigit))
            errores.Add("La cuenta debe tener solo dígitos, entre 6 y 20.");
        if (cuenta != (cuentaRepetida?.Trim() ?? ""))
            errores.Add("Las dos cuentas ingresadas no coinciden.");
        if (string.Equals(tipo, TipoRut, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(banco, BancoEstado, StringComparison.OrdinalIgnoreCase))
                errores.Add("Una cuenta tipo RUT debe ser del banco ESTADO.");
            if (Normalizar(cuenta) != rutPrestador.ToString())
                errores.Add("Una cuenta tipo RUT debe ser el RUT sin dígito verificador.");
        }
        if (errores.Count == 0 && banco is not null)
        {
            var dueno = duenoDeCuenta(banco, Normalizar(cuenta));
            if (dueno is not null && dueno != rutPrestador)
                errores.Add("La cuenta pertenece a otro RUT (cuenta de un tercero).");
        }
        return errores;
    }

    /// <summary>
    /// R-24: clasifica la cuenta de una fila de la planilla contra la cuenta vigente del RUT.
    /// </summary>
    /// <param name="rutPrestador">Cuerpo del RUT de la fila.</param>
    /// <param name="enPlanilla">Cuenta escrita en la fila; null o número vacío = sin cuenta.</param>
    /// <param name="registrada">Cuenta vigente registrada para el RUT, si existe.</param>
    /// <param name="duenoDeCuenta">Devuelve (RUT, nombre) del dueño de una (banco, cuenta normalizada) registrada, si existe.</param>
    /// <param name="otrasFilasMismoPrestador">Cuentas de las demás filas del mismo prestador en la planilla.</param>
    public static Clasificacion Clasificar(
        int rutPrestador, CuentaRef? enPlanilla, CuentaRef? registrada,
        Func<string, string, (int Rut, string Descripcion)?> duenoDeCuenta,
        IEnumerable<CuentaRef?> otrasFilasMismoPrestador)
    {
        if (enPlanilla is null || string.IsNullOrWhiteSpace(enPlanilla.Numero) || enPlanilla.Normalizada.Length == 0)
            return new(ResultadoCuenta.SinCuentaPlanilla, "Se usa la cuenta registrada.");

        var dueno = duenoDeCuenta(enPlanilla.Banco, enPlanilla.Normalizada);
        if (dueno is not null && dueno.Value.Rut != rutPrestador)
            return new(ResultadoCuenta.CuentaTercero, $"Pertenece a {dueno.Value.Descripcion}. Bloqueada.");

        var otras = otrasFilasMismoPrestador.Where(o => o is not null && o.Normalizada.Length > 0).ToList();
        var distintas = otras.Where(o => !o!.Igual(enPlanilla)).ToList();
        if (distintas.Count > 0)
        {
            // Dice cuál es la fila que difiere: sin eso las filas idénticas marcadas parecen un error de la validación.
            var primera = distintas[0]!;
            var donde = primera.Fila is { } f ? $"la fila {f} trae" : "otra fila trae";
            var resto = distintas.Count > 1 ? $" (y {distintas.Count - 1} fila{(distintas.Count > 2 ? "s" : "")} más con otra cuenta)" : "";
            return new(ResultadoCuenta.PosibleErrorTipeo, $"Filas del mismo prestador con cuentas distintas: {donde} {primera.Texto}{resto}.");
        }

        if (registrada is null)
            return new(ResultadoCuenta.CuentaNueva, "El RUT no tiene cuenta registrada.");

        if (enPlanilla.Igual(registrada))
            return new(ResultadoCuenta.Coincide, "");

        if (string.Equals(enPlanilla.TipoCuenta, TipoRut, StringComparison.OrdinalIgnoreCase) &&
            enPlanilla.Normalizada != rutPrestador.ToString())
            return new(ResultadoCuenta.PosibleErrorTipeo, "Tipo RUT: la cuenta debe ser el RUT sin DV.");

        if (enPlanilla.MismoBancoYTipo(registrada))
        {
            var distancia = DamerauLevenshtein(enPlanilla.Normalizada, registrada.Normalizada);
            if (distancia <= 2)
                return new(ResultadoCuenta.PosibleErrorTipeo, distancia == 1 && enPlanilla.Normalizada.Length == registrada.Normalizada.Length
                    ? "Dígitos transpuestos o un dígito distinto."
                    : $"Diferencia de {distancia} dígitos con la registrada.");
            // TODO(diseño): umbral de "cuenta muy distinta" cuando banco y tipo coinciden.
            return new(ResultadoCuenta.DistintaRegistrada, "Mismo banco y tipo, número muy distinto.");
        }

        var cambios = new List<string>();
        if (!string.Equals(enPlanilla.Banco, registrada.Banco, StringComparison.OrdinalIgnoreCase)) cambios.Add("banco");
        if (!string.Equals(enPlanilla.TipoCuenta, registrada.TipoCuenta, StringComparison.OrdinalIgnoreCase)) cambios.Add("tipo de cuenta");
        return new(ResultadoCuenta.DistintaRegistrada, "Otro " + string.Join(" y ", cambios) + ".");
    }

    /// <summary>Posiciones (sobre el texto mostrado) de los dígitos distintos, para resaltarlos.</summary>
    public static bool[] DigitosDistintos(string mostrado, string? referencia)
    {
        var marcas = new bool[mostrado.Length];
        if (string.IsNullOrEmpty(referencia)) return marcas;
        var a = Normalizar(mostrado);
        var b = Normalizar(referencia);
        if (a.Length != b.Length) return marcas;
        var offset = mostrado.Length - a.Length; // ceros a la izquierda
        for (var i = 0; i < a.Length; i++)
            marcas[offset + i] = a[i] != b[i];
        return marcas;
    }
}
