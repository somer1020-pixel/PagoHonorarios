using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IpsosPagoHonorarios.Web.Services;

public class OpcionesPlantillas
{
    /// <summary>Ruta de la plantilla oficial de Finanzas (opcional). Si no existe se genera una equivalente.</summary>
    public string? PlanillaFinanzas { get; set; }
}

/// <summary>Encabezado (filas 1–5) leído de una planilla en formato Finanzas.</summary>
public sealed record EncabezadoPlanilla(DateOnly? FechaRecepcion, string? Responsable, string? Area, decimal? MontoTotal);

public sealed record ArchivoLeido(EncabezadoPlanilla? Encabezado, List<FilaProduccion> Filas);

/// <summary>Lectura y escritura del formato XLSX de Finanzas (§10) con ClosedXML.</summary>
public class ExcelPlanilla(AppDbContext db, IOptions<OpcionesPlantillas> opciones, IHostEnvironment env)
{
    public const string HojaPlanilla = "Planilla";
    public const string HojaFormato = "Formato";
    public const int FilaTitulos = 8;
    public const int PrimeraFila = 9;
    public const int UltimaFila = 3007;

    public static readonly string[] Titulos =
    [
        "Tipo de Gasto", "Job Book Number", "Job Book Number Name", "Nombre Glosa", "Item", "Valor unitario bruto", "Cantidad",
        "Valor total bruto", "Rut", "Nombres (Completo, nombres y apellidos)", "N° boleta", "Responsable", "Área Responsable",
        "Código Área Responsable", "Tipo Cuenta", "Cuenta", "Banco"
    ];

    // ---------- Exportación ----------

    /// <summary>
    /// Escribe la planilla sobre la plantilla oficial (o una equivalente), conservando fórmulas, listas desplegables y la hoja
    /// Formato. R-27: las columnas O, P y Q usan la cuenta registrada y vigente, no la escrita en la planilla.
    /// </summary>
    public async Task<byte[]> ExportarAsync(Planilla p)
    {
        using var wb = await AbrirPlantillaAsync();
        var ws = wb.Worksheet(HojaPlanilla);
        ws.Cell("B1").Value = p.FechaRecepcion.ToDateTime(TimeOnly.MinValue);
        ws.Cell("B1").Style.DateFormat.Format = "dd/mm/yyyy";
        ws.Cell("B2").Value = p.ResponsableNombre;
        ws.Cell("B3").FormulaA1 = $"SUM(H{PrimeraFila}:H{UltimaFila})";
        ws.Cell("B4").Value = p.Area.Nombre;
        ws.Cell("B5").FormulaA1 = "VLOOKUP(B4,Formato!$A$2:$B$500,2,FALSE)";

        var fila = PrimeraFila;
        foreach (var l in p.Lineas.Where(l => l.Estado != LineaEstado.Diferida).OrderBy(l => l.Numero))
        {
            var cta = l.Prestador.Cuentas.FirstOrDefault(c => c.Vigente);
            ws.Cell(fila, 1).Value = l.TipoGasto;
            ws.Cell(fila, 2).SetValue(l.Job.JobBookNumber).Style.NumberFormat.Format = "@";
            ws.Cell(fila, 3).Value = l.Job.Nombre;
            ws.Cell(fila, 4).Value = l.Glosa.NombreGlosa;
            ws.Cell(fila, 5).FormulaA1 = $"VLOOKUP(D{fila},Formato!$D$2:$E$500,2,FALSE)";
            ws.Cell(fila, 6).Value = l.ValorUnitarioBruto;
            ws.Cell(fila, 7).Value = l.Cantidad;
            ws.Cell(fila, 8).FormulaA1 = $"ROUND(F{fila}*G{fila},0)";
            ws.Cell(fila, 9).Value = l.Prestador.RutPlanilla;
            ws.Cell(fila, 10).Value = l.Prestador.NombreCompleto;
            ws.Cell(fila, 11).SetValue(l.NumeroBoleta ?? "").Style.NumberFormat.Format = "@";
            ws.Cell(fila, 12).FormulaA1 = "$B$2";
            ws.Cell(fila, 13).FormulaA1 = "$B$4";
            ws.Cell(fila, 14).FormulaA1 = "$B$5";
            ws.Cell(fila, 15).Value = cta?.TipoCuenta ?? l.CuentaPlanillaTipo ?? "";
            ws.Cell(fila, 16).SetValue(cta?.Cuenta ?? l.CuentaPlanillaNumero ?? "").Style.NumberFormat.Format = "@";
            ws.Cell(fila, 17).Value = cta?.Banco ?? l.CuentaPlanillaBanco ?? "";
            fila++;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private async Task<XLWorkbook> AbrirPlantillaAsync()
    {
        var ruta = opciones.Value.PlanillaFinanzas;
        if (!string.IsNullOrWhiteSpace(ruta))
        {
            var abs = Path.IsPathRooted(ruta) ? ruta : Path.Combine(env.ContentRootPath, ruta);
            if (File.Exists(abs)) return new XLWorkbook(abs);
        }
        return await CrearPlantillaAsync();
    }

    /// <summary>Plantilla equivalente a la oficial: hoja Planilla con títulos y validaciones, hoja Formato con catálogos.</summary>
    private async Task<XLWorkbook> CrearPlantillaAsync()
    {
        var areas = await db.Areas.AsNoTracking().OrderBy(a => a.Nombre).ToListAsync();
        var glosas = await db.Glosas.AsNoTracking().OrderBy(g => g.NombreGlosa).ToListAsync();
        var bancos = await db.Bancos.AsNoTracking().OrderBy(b => b.Id).ToListAsync();
        var tipos = await db.TiposCuenta.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
        var gastos = await db.TiposGasto.AsNoTracking().OrderBy(t => t.Id).ToListAsync();

        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(HojaPlanilla);
        var fmt = wb.AddWorksheet(HojaFormato);

        fmt.Cell("A1").Value = "Área"; fmt.Cell("B1").Value = "Código Área";
        for (var i = 0; i < areas.Count; i++) { fmt.Cell(i + 2, 1).Value = areas[i].Nombre; fmt.Cell(i + 2, 2).SetValue(areas[i].CodigoArea); }
        fmt.Cell("D1").Value = "Nombre Glosa"; fmt.Cell("E1").Value = "Item"; fmt.Cell("F1").Value = "Cuenta contable";
        for (var i = 0; i < glosas.Count; i++)
        {
            fmt.Cell(i + 2, 4).Value = glosas[i].NombreGlosa;
            fmt.Cell(i + 2, 5).SetValue(glosas[i].Item);
            fmt.Cell(i + 2, 6).SetValue(glosas[i].CuentaContable);
        }
        fmt.Cell("H1").Value = "Banco"; fmt.Cell("I1").Value = "Código";
        for (var i = 0; i < bancos.Count; i++) { fmt.Cell(i + 2, 8).Value = bancos[i].Nombre; fmt.Cell(i + 2, 9).SetValue(bancos[i].CodigoBanco); }
        fmt.Cell("K1").Value = "Tipo de cuenta"; fmt.Cell("L1").Value = "Código";
        for (var i = 0; i < tipos.Count; i++) { fmt.Cell(i + 2, 11).Value = tipos[i].Nombre; fmt.Cell(i + 2, 12).SetValue(tipos[i].Codigo); }
        fmt.Cell("N1").Value = "Tipo de gasto";
        for (var i = 0; i < gastos.Count; i++) fmt.Cell(i + 2, 14).Value = gastos[i].Nombre;
        wb.DefinedNames.Add("banco", fmt.Range(2, 8, Math.Max(2, bancos.Count + 1), 8));
        wb.DefinedNames.Add("tipos_de_cuenta", fmt.Range(2, 11, Math.Max(2, tipos.Count + 1), 11));

        ws.Cell("A1").Value = "Fecha de Recepción";
        ws.Cell("A2").Value = "Nombre del Responsable";
        ws.Cell("A3").Value = "Monto Total a pago";
        ws.Cell("A4").Value = "Área Responsable";
        ws.Cell("A5").Value = "Código Área Responsable";
        ws.Range("A1:A5").Style.Font.Bold = true;
        ws.Cell("B3").Style.NumberFormat.Format = "$#,##0";
        for (var c = 0; c < Titulos.Length; c++) ws.Cell(FilaTitulos, c + 1).Value = Titulos[c];
        var t = ws.Range(FilaTitulos, 1, FilaTitulos, Titulos.Length);
        t.Style.Font.Bold = true;
        t.Style.Fill.BackgroundColor = XLColor.FromHtml("#121B5A");
        t.Style.Font.FontColor = XLColor.White;
        ws.Range($"O{PrimeraFila}:O{UltimaFila}").CreateDataValidation().List("=tipos_de_cuenta");
        ws.Range($"Q{PrimeraFila}:Q{UltimaFila}").CreateDataValidation().List("=banco");
        ws.Range($"A{PrimeraFila}:A{UltimaFila}").CreateDataValidation().List($"=Formato!$N$2:$N${Math.Max(2, gastos.Count + 1)}");
        ws.Range($"B{PrimeraFila}:B{UltimaFila}").Style.NumberFormat.Format = "@";
        ws.Range($"P{PrimeraFila}:P{UltimaFila}").Style.NumberFormat.Format = "@";
        ws.Range($"F{PrimeraFila}:F{UltimaFila}").Style.NumberFormat.Format = "#,##0.##";
        ws.Range($"H{PrimeraFila}:H{UltimaFila}").Style.NumberFormat.Format = "#,##0";
        ws.Columns(1, Titulos.Length).Width = 18;
        ws.SheetView.FreezeRows(FilaTitulos);
        return wb;
    }

    // ---------- Importación ----------

    /// <summary>Lee una planilla en formato Finanzas: encabezado B1–B5 y datos desde la fila 9 hasta la primera fila con Rut vacío.</summary>
    public static ArchivoLeido LeerFormatoFinanzas(Stream xlsx)
    {
        using var wb = new XLWorkbook(xlsx);
        var ws = wb.Worksheets.FirstOrDefault(w => w.Name.Equals(HojaPlanilla, StringComparison.OrdinalIgnoreCase)) ?? wb.Worksheet(1);
        DateOnly? fecha = null;
        var b1 = ws.Cell("B1");
        if (b1.Value.IsDateTime) fecha = DateOnly.FromDateTime(b1.Value.GetDateTime());
        else if (DateOnly.TryParseExact(b1.GetString().Trim(), ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)) fecha = f;
        decimal? total = null;
        try { if (ws.Cell("B3").Value.IsNumber) total = (decimal)ws.Cell("B3").Value.GetNumber(); } catch { /* fórmula no evaluable */ }

        var filas = new List<FilaProduccion>();
        for (var r = PrimeraFila; r <= UltimaFila; r++)
        {
            var rut = Texto(ws.Cell(r, 9));
            if (string.IsNullOrWhiteSpace(rut)) break;
            filas.Add(new FilaProduccion
            {
                Fila = r,
                TipoGasto = Texto(ws.Cell(r, 1)),
                Job = Texto(ws.Cell(r, 2)),
                NombreJob = Texto(ws.Cell(r, 3)),
                Glosa = Texto(ws.Cell(r, 4)),
                ValorUnitario = Numero(ws.Cell(r, 6)),
                Cantidad = Numero(ws.Cell(r, 7)),
                Rut = rut,
                Nombre = Texto(ws.Cell(r, 10)),
                NumeroBoleta = Texto(ws.Cell(r, 11)),
                TipoCuenta = Texto(ws.Cell(r, 15)),
                Cuenta = Texto(ws.Cell(r, 16)),
                Banco = Texto(ws.Cell(r, 17))
            });
        }
        return new(new EncabezadoPlanilla(fecha, Texto(ws.Cell("B2")), Texto(ws.Cell("B4")), total), filas);
    }

    private static readonly Dictionary<string, string> Alias = new()
    {
        ["rut"] = "rut", ["nombre"] = "nombre", ["nombres"] = "nombre", ["nombre completo"] = "nombre",
        ["job"] = "job", ["job book number"] = "job", ["jobbooknumber"] = "job",
        ["nombre job"] = "nombrejob", ["job book number name"] = "nombrejob",
        ["glosa"] = "glosa", ["nombre glosa"] = "glosa",
        ["valor unitario"] = "vu", ["valor unitario bruto"] = "vu", ["valor"] = "vu",
        ["cantidad"] = "q", ["tipo cuenta"] = "tipo", ["tipo de cuenta"] = "tipo", ["cuenta"] = "cuenta", ["banco"] = "banco",
        ["n° boleta"] = "boleta", ["boleta"] = "boleta", ["tipo de gasto"] = "tg", ["tipo gasto"] = "tg"
    };

    /// <summary>
    /// Exportación del sistema de encuestas (XLSX o CSV) con fila de títulos. TODO(diseño): formato oficial de la exportación
    /// (pregunta abierta 5). Se reconocen columnas por nombre: Rut, Nombre, Job, Nombre Job, Glosa, Valor unitario, Cantidad,
    /// Tipo cuenta, Cuenta, Banco.
    /// </summary>
    public static ArchivoLeido LeerExportacion(Stream archivo, string nombreArchivo)
    {
        List<string[]> tabla;
        if (nombreArchivo.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) tabla = LeerCsv(archivo);
        else
        {
            using var wb = new XLWorkbook(archivo);
            var ws = wb.Worksheet(1);
            var usado = ws.RangeUsed();
            tabla = [];
            if (usado is not null)
                foreach (var row in usado.Rows())
                    tabla.Add(row.Cells().Select(Texto).Select(s => s ?? "").ToArray());
        }
        if (tabla.Count == 0) return new(null, []);
        var idx = tabla[0].Select((h, i) => (Clave: Alias.GetValueOrDefault(Clave(h)), i)).Where(x => x.Clave is not null)
            .GroupBy(x => x.Clave!).ToDictionary(g => g.Key, g => g.First().i);
        string? V(string[] f, string k) => idx.TryGetValue(k, out var i) && i < f.Length && !string.IsNullOrWhiteSpace(f[i]) ? f[i].Trim() : null;
        var filas = new List<FilaProduccion>();
        for (var r = 1; r < tabla.Count; r++)
        {
            var f = tabla[r];
            if (f.All(string.IsNullOrWhiteSpace)) continue;
            filas.Add(new FilaProduccion
            {
                Fila = r + 1, TipoGasto = V(f, "tg"), Job = V(f, "job"), NombreJob = V(f, "nombrejob"), Glosa = V(f, "glosa"),
                ValorUnitario = ParseNumero(V(f, "vu")), Cantidad = ParseNumero(V(f, "q")), Rut = V(f, "rut"), Nombre = V(f, "nombre"),
                NumeroBoleta = V(f, "boleta"), TipoCuenta = V(f, "tipo"), Cuenta = V(f, "cuenta"), Banco = V(f, "banco")
            });
        }
        return new(null, filas);
    }

    private static string Clave(string h) => Regex.Replace(LectorBoletaTexto.Normalizar(h).ToLowerInvariant(), @"\s+", " ").Trim();

    private static List<string[]> LeerCsv(Stream s)
    {
        using var sr = new StreamReader(s, Encoding.UTF8, true);
        var lineas = new List<string>();
        while (sr.ReadLine() is { } l) lineas.Add(l);
        if (lineas.Count == 0) return [];
        var sep = lineas[0].Count(c => c == ';') >= lineas[0].Count(c => c == ',') ? ';' : ',';
        return lineas.Select(l => DividirCsv(l, sep)).ToList();
    }

    private static string[] DividirCsv(string linea, char sep)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        var comillas = false;
        for (var i = 0; i < linea.Length; i++)
        {
            var c = linea[i];
            if (c == '"') { if (comillas && i + 1 < linea.Length && linea[i + 1] == '"') { sb.Append('"'); i++; } else comillas = !comillas; }
            else if (c == sep && !comillas) { res.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        res.Add(sb.ToString());
        return res.ToArray();
    }

    /// <summary>Acepta 85,28 · 85.28 · 1.234.567 · $6.500.</summary>
    public static decimal? ParseNumero(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Replace("$", "").Replace(" ", "").Trim();
        if (s.Contains(',') && s.Contains('.')) s = s.Replace(".", "").Replace(',', '.');
        else if (s.Contains(',')) s = s.Replace(',', '.');
        else if (Regex.IsMatch(s, @"^-?\d{1,3}(\.\d{3})+$")) s = s.Replace(".", "");
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static string? Texto(IXLCell c)
    {
        var v = c.Value;
        if (v.IsBlank) return null;
        if (v.IsNumber) return ((decimal)v.GetNumber()).ToString("0.####", CultureInfo.InvariantCulture);
        if (v.IsDateTime) return v.GetDateTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        if (v.IsError) return null;
        var s = c.GetString().Trim();
        return s.Length == 0 ? null : s;
    }

    private static decimal? Numero(IXLCell c)
    {
        var v = c.Value;
        if (v.IsNumber) return (decimal)v.GetNumber();
        return ParseNumero(Texto(c));
    }

    // ---------- Nómina ----------

    /// <summary>
    /// R-14 / R-27: nómina con la cuenta registrada y validada. Una fila por boleta (transferencia).
    /// TODO(diseño): formato de la nómina para la carga masiva del banco.
    /// </summary>
    public static byte[] Nomina(IEnumerable<FilaNomina> filas, string titulo)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Nomina");
        string[] h = ["Rut", "Nombre", "Banco", "Código banco", "Tipo cuenta", "Código tipo", "Cuenta", "Monto líquido", "N° boleta", "Correo"];
        ws.Cell(1, 1).Value = titulo;
        ws.Cell(1, 1).Style.Font.Bold = true;
        for (var i = 0; i < h.Length; i++) ws.Cell(3, i + 1).Value = h[i];
        ws.Range(3, 1, 3, h.Length).Style.Font.Bold = true;
        var r = 4;
        foreach (var f in filas)
        {
            ws.Cell(r, 1).Value = f.Rut; ws.Cell(r, 2).Value = f.Nombre; ws.Cell(r, 3).Value = f.Banco;
            ws.Cell(r, 4).SetValue(f.CodigoBanco); ws.Cell(r, 5).Value = f.TipoCuenta; ws.Cell(r, 6).SetValue(f.CodigoTipo);
            ws.Cell(r, 7).SetValue(f.Cuenta); ws.Cell(r, 8).Value = f.MontoLiquido; ws.Cell(r, 9).SetValue(f.NumeroBoleta);
            ws.Cell(r, 10).Value = f.Correo ?? "";
            r++;
        }
        ws.Cell(r, 7).Value = "Total";
        ws.Cell(r, 8).FormulaA1 = $"SUM(H4:H{Math.Max(4, r - 1)})";
        ws.Column(8).Style.NumberFormat.Format = "#,##0";
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}

public sealed record FilaNomina(string Rut, string Nombre, string Banco, string CodigoBanco, string TipoCuenta, string CodigoTipo,
    string Cuenta, decimal MontoLiquido, string NumeroBoleta, string? Correo);
