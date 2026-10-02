using System.IO.Compression;
using System.Text;
using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Services;

public sealed record PagoPrestador(
    Prestador Prestador, BoletaHonorarios? Boleta, CuentaBancaria? Cuenta, decimal Bruto, decimal Retencion, decimal Liquido,
    Transferencia? Transferencia, List<LineaPago> Lineas);

/// <summary>Nómina, transferencias, cierre y archivo (R-14, R-15, R-27).</summary>
public class PagoService(
    AppDbContext db, CicloService ciclos, Parametros parametros, Almacenamiento archivos, Auditor auditor, Correos correos, IUsuarioActual usuario)
{
    /// <summary>Una transferencia por boleta: bruto = suma de filas aprobadas, retención por boleta (R-05).</summary>
    public async Task<List<PagoPrestador>> ResumenAsync(Planilla p)
    {
        var tasa = await parametros.TasaAsync(p.Ciclo.Periodo.Year);
        var trans = await db.Transferencias.Where(t => t.PlanillaId == p.Id).ToListAsync();
        return p.Lineas.Where(l => l.Estado is LineaEstado.Aprobada or LineaEstado.Pagada)
            .GroupBy(l => l.PrestadorId)
            .Select(g =>
            {
                var prest = g.First().Prestador;
                var (bruto, ret, liq) = Montos.PorBoleta(g.Select(l => l.ValorTotalBruto), tasa);
                var boleta = p.BoletaVigente(prest.Id);
                return new PagoPrestador(prest, boleta, prest.Cuentas.FirstOrDefault(c => c.Vigente), bruto, ret, liq,
                    trans.FirstOrDefault(t => t.BoletaId == boleta?.Id), g.OrderBy(l => l.Numero).ToList());
            })
            .OrderBy(x => x.Lineas[0].Numero).ToList();
    }

    /// <summary>R-14 / R-27: nómina XLSX con la cuenta registrada y validada. La planilla pasa a EnPago.</summary>
    public async Task<(byte[] Bytes, string Nombre)> NominaAsync(int planillaId)
    {
        var p = await ciclos.PlanillaCompletaAsync(planillaId) ?? throw new ReglaException("Planilla no encontrada.");
        CicloService.ExigirAbierto(p.Ciclo);
        if (p.Estado is not (PlanillaEstado.Aprobada or PlanillaEstado.EnPago)) throw new ReglaException("La planilla no está aprobada.");
        var resumen = await ResumenAsync(p);
        var sinCuenta = resumen.Where(r => r.Cuenta is not { Estado: CuentaEstado.Validada }).Select(r => r.Prestador.NombreCompleto).ToList();
        if (sinCuenta.Count > 0) throw new ReglaException("Cuentas no validadas: " + string.Join(", ", sinCuenta) + ".");
        var bancos = await db.Bancos.ToDictionaryAsync(b => b.Nombre, b => b.CodigoBanco);
        var tipos = await db.TiposCuenta.ToDictionaryAsync(t => t.Nombre, t => t.Codigo);
        var filas = resumen.Where(r => r.Transferencia is null).Select(r => new FilaNomina(
            r.Prestador.RutPlanilla, r.Prestador.NombreCompleto, r.Cuenta!.Banco, bancos.GetValueOrDefault(r.Cuenta.Banco, ""),
            r.Cuenta.TipoCuenta, tipos.GetValueOrDefault(r.Cuenta.TipoCuenta, ""), r.Cuenta.Cuenta, r.Liquido, r.Boleta?.NumeroBoleta ?? "", r.Prestador.Email));
        var nombre = $"Nomina honorarios {Formato.MesLargo(p.Ciclo.Periodo.Month)}_{p.Ciclo.Periodo.Year} - {p.Area.Nombre}.xlsx";
        var bytes = ExcelPlanilla.Nomina(filas, $"Nómina {p.Area.Nombre} {p.Ciclo.Codigo}");
        db.PlanillaArchivos.Add(new PlanillaArchivo
        {
            PlanillaId = p.Id, Version = p.Version, Tipo = "Nomina", NombreArchivo = nombre, Ruta = archivos.Guardar($"nominas/{p.Ciclo.Codigo}", nombre, bytes)
        });
        if (p.Estado == PlanillaEstado.Aprobada) p.Estado = PlanillaEstado.EnPago;
        auditor.Registrar(nameof(Planilla), p.Id, "Generar nómina", nombre);
        await db.SaveChangesAsync();
        return (bytes, nombre);
    }

    public const int MaxComprobante = 5 * 1024 * 1024;

    /// <summary>Formatos admitidos para el comprobante de transferencia (por contenido, no por extensión): PDF, JPG o PNG.</summary>
    public static (string Ext, string Mime)? TipoComprobante(byte[] b)
    {
        if (b.Length == 0) return null;
        if (LectorPdf.EsPdf(b)) return (".pdf", "application/pdf");
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return (".jpg", "image/jpeg");
        if (b.Length > 8 && b.AsSpan(0, 8).SequenceEqual((byte[])[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return (".png", "image/png");
        return null;
    }

    /// <summary>R-14: registra fecha, N° de operación y comprobante; las líneas pasan a Pagada. R-23/R-27: solo cuenta validada.</summary>
    public async Task<Transferencia> RegistrarTransferenciaAsync(int planillaId, int prestadorId, DateOnly fecha, string numeroOperacion, byte[] comprobante, string nombreComprobante)
    {
        var p = await ciclos.PlanillaCompletaAsync(planillaId) ?? throw new ReglaException("Planilla no encontrada.");
        CicloService.ExigirAbierto(p.Ciclo);
        if (p.Estado is not (PlanillaEstado.Aprobada or PlanillaEstado.EnPago)) throw new ReglaException("La planilla no está aprobada.");
        if (string.IsNullOrWhiteSpace(numeroOperacion)) throw new ReglaException("El N° de operación es obligatorio.");
        var tipo = TipoComprobante(comprobante) ?? throw new ReglaException("El comprobante debe ser un PDF o una imagen (JPG o PNG).");
        if (comprobante.Length > MaxComprobante) throw new ReglaException("El comprobante supera el máximo de 5 MB.");
        nombreComprobante = Path.ChangeExtension(string.IsNullOrWhiteSpace(nombreComprobante) ? "comprobante" : Path.GetFileName(nombreComprobante), tipo.Ext);
        var r = (await ResumenAsync(p)).FirstOrDefault(x => x.Prestador.Id == prestadorId) ?? throw new ReglaException("El prestador no tiene líneas aprobadas.");
        if (r.Transferencia is not null) throw new ReglaException("La transferencia de esta boleta ya fue registrada.");
        if (r.Boleta is null) throw new ReglaException("No hay boleta vigente.");
        if (r.Cuenta is not { Estado: CuentaEstado.Validada }) throw new ReglaException("Solo se paga a la cuenta registrada y validada.");
        var t = new Transferencia
        {
            PlanillaId = p.Id, PrestadorId = prestadorId, BoletaId = r.Boleta.Id, CuentaBancariaId = r.Cuenta.Id, Fecha = fecha,
            NumeroOperacion = numeroOperacion.Trim(), MontoLiquido = r.Liquido,
            Comprobante = archivos.Guardar($"comprobantes/{p.Ciclo.Codigo}", nombreComprobante, comprobante)
        };
        db.Transferencias.Add(t);
        foreach (var l in r.Lineas) l.Estado = LineaEstado.Pagada;
        if (p.Estado == PlanillaEstado.Aprobada) p.Estado = PlanillaEstado.EnPago;
        auditor.Registrar(nameof(Transferencia), r.Prestador.RutPlanilla, "Registrar transferencia",
            $"{numeroOperacion} · {Formato.Clp(r.Liquido)} · {r.Cuenta.Descripcion}");
        correos.Encolar(r.Prestador.Email, $"Tu pago de {p.Ciclo.Codigo} fue transferido",
            $"Hola {r.Prestador.NombreCompleto}: transferimos {Formato.Clp(r.Liquido)} a tu cuenta {r.Cuenta.TipoCuenta} {r.Cuenta.Banco} {Formato.Enmascarar(r.Cuenta.Cuenta)} el {Formato.Fecha(fecha)}.");
        await db.SaveChangesAsync();
        return t;
    }

    private static string ExtensionDe(string ruta) => Path.GetExtension(ruta) is { Length: > 0 } e ? e.ToLowerInvariant() : ".pdf";

    /// <summary>R-15: cierre con todas las líneas pagadas o diferidas; ZIP con planillas, boletas, nóminas, comprobantes y bitácora.</summary>
    public async Task CerrarCicloAsync(int cicloId)
    {
        var c = await db.Ciclos.Include(x => x.Planillas).ThenInclude(p => p.Lineas).Include(x => x.Planillas).ThenInclude(p => p.Area)
                    .FirstOrDefaultAsync(x => x.Id == cicloId) ?? throw new ReglaException("Ciclo no encontrado.");
        var v = Flujo.PuedeCerrar(c);
        if (!v.Ok) throw new ReglaException(string.Join(" ", v.Motivos));
        var ids = c.Planillas.Select(p => p.Id).ToList();
        var docs = await db.PlanillaArchivos.Where(a => ids.Contains(a.PlanillaId)).ToListAsync();
        var bols = await db.Boletas.Include(b => b.Prestador).Where(b => ids.Contains(b.PlanillaId)).ToListAsync();
        var trans = await db.Transferencias.Include(t => t.Prestador).Where(t => ids.Contains(t.PlanillaId)).ToListAsync();
        var bitacora = await db.Auditorias.Where(a => a.Fecha >= c.CreadoEn).OrderBy(a => a.Id).ToListAsync();

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string ruta, string rel)
            {
                if (!archivos.Existe(rel)) return;
                var e = zip.CreateEntry(ruta, CompressionLevel.Optimal);
                using var s = e.Open();
                s.Write(archivos.Leer(rel));
            }
            foreach (var a in docs)
            {
                var area = c.Planillas.First(p => p.Id == a.PlanillaId).Area.Nombre;
                var carpeta = a.Tipo == "Nomina" ? "nominas" : "planillas";
                Add($"{carpeta}/{area}/{a.Tipo}_v{a.Version}_{a.NombreArchivo}", a.Ruta);
            }
            foreach (var b in bols) Add($"boletas/{b.Prestador.RutPlanilla}_N{b.NumeroBoleta}_{b.Estado}_{b.Id}.pdf", b.RutaPdf);
            foreach (var t in trans) Add($"comprobantes/{t.Prestador.RutPlanilla}_{t.NumeroOperacion}{ExtensionDe(t.Comprobante)}", t.Comprobante);
            var csv = new StringBuilder("Fecha;Usuario;Entidad;Id;Accion;Detalle\n");
            foreach (var a in bitacora)
                csv.AppendLine(string.Join(";", Formato.FechaHora(a.Fecha), a.Usuario, a.Entidad, a.EntidadId, a.Accion, a.Detalle?.Replace(';', ',').Replace('\n', ' ')));
            var be = zip.CreateEntry("bitacora.csv");
            await using (var s = be.Open()) await s.WriteAsync(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray());
        }
        c.RutaZip = archivos.Guardar("cierres", $"Respaldos {c.Codigo}.zip", ms.ToArray());
        c.Estado = CicloEstado.Cerrado;
        c.CerradoEn = ciclos.AhoraUtc;
        c.CerradoPor = usuario.Nombre;
        foreach (var p in c.Planillas) p.Estado = PlanillaEstado.Cerrada;
        auditor.Registrar(nameof(Ciclo), c.Codigo, "Cerrar ciclo", $"ZIP con {docs.Count} planillas/nóminas, {bols.Count} boletas, {trans.Count} comprobantes");
        await db.SaveChangesAsync();
    }
}
