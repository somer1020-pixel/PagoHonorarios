using System.Security.Cryptography;
using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace IpsosPagoHonorarios.Web.Services;

/// <summary>Lectura del PDF del SII con PdfPig: texto por líneas según la posición vertical de las palabras.</summary>
public static class LectorPdf
{
    public const int TamanoMaximo = 2 * 1024 * 1024;

    /// <summary>R-19: PDF real (cabecera %PDF-).</summary>
    public static bool EsPdf(byte[] bytes) =>
        bytes.Length >= 5 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F' && bytes[4] == '-';

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static List<string> ExtraerLineas(byte[] pdf)
    {
        var lineas = new List<string>();
        using var doc = PdfDocument.Open(pdf);
        foreach (var pagina in doc.GetPages())
        {
            var palabras = pagina.GetWords().ToList();
            foreach (var grupo in palabras
                         .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 3.0))
                         .OrderByDescending(g => g.Key))
                lineas.Add(string.Join(" ", grupo.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));
        }
        return lineas;
    }
}

public sealed record ResultadoSubida(BoletaHonorarios Boleta, ResultadoConciliacion Conciliacion, LecturaBoleta Lectura, decimal SumaPlanilla);

/// <summary>Boletas: subida (portal u Operaciones), lectura, conciliación y seguimiento.</summary>
public class BoletaService(
    AppDbContext db, CicloService ciclos, Parametros parametros, Almacenamiento archivos, Auditor auditor, Correos correos, IUsuarioActual usuario)
{
    /// <summary>
    /// R-19 / R-22 / R-06 / R-18: valida el archivo, lee la boleta, rechaza si el emisor no es el prestador, concilia con sus
    /// filas y reemplaza la boleta anterior.
    /// </summary>
    public async Task<ResultadoSubida> SubirAsync(int planillaId, int prestadorId, byte[] pdf, string nombreArchivo, BoletaCanal canal, string? motivo = null)
    {
        var p = await ciclos.PlanillaCompletaAsync(planillaId) ?? throw new ReglaException("Planilla no encontrada.");
        CicloService.ExigirAbierto(p.Ciclo);
        var r18 = Flujo.PuedeSubirBoleta(p, prestadorId);
        if (!r18.Ok) throw new ReglaException(string.Join(" ", r18.Motivos));
        if (canal == BoletaCanal.Operaciones && string.IsNullOrWhiteSpace(motivo))
            throw new ReglaException("El motivo es obligatorio para cargar una boleta en nombre del prestador.");
        if (pdf.Length > LectorPdf.TamanoMaximo) throw new ReglaException("El PDF pesa más de 2 MB.");
        if (!LectorPdf.EsPdf(pdf)) throw new ReglaException("El archivo no es un PDF válido.");
        var hash = LectorPdf.Hash(pdf);
        if (await db.Boletas.AnyAsync(b => b.HashPdf == hash)) throw new ReglaException("Este PDF ya fue subido (duplicado).");

        LecturaBoleta lectura;
        try { lectura = LectorBoletaTexto.Leer(LectorPdf.ExtraerLineas(pdf)); }
        catch (Exception) { throw new ReglaException("No se pudo leer el PDF."); }

        var prest = p.Lineas.First(l => l.PrestadorId == prestadorId).Prestador;
        if (RutHelper.TryParse(lectura.Datos.RutEmisor, out var emisor, out _) && emisor != prest.Rut)
        {
            auditor.Registrar(nameof(BoletaHonorarios), prest.RutPlanilla, "Boleta rechazada", $"Emisor {lectura.Datos.RutEmisor} ≠ prestador");
            await db.SaveChangesAsync();
            throw new ReglaException(Conciliacion.MensajeEmisor + ".");
        }

        var anterior = p.BoletaVigente(prestadorId);
        var b = new BoletaHonorarios
        {
            PlanillaId = p.Id, PrestadorId = prestadorId, Canal = canal, MotivoCargaOperaciones = canal == BoletaCanal.Operaciones ? motivo!.Trim() : null,
            SubidaPor = usuario.Nombre, SubidaEn = ciclos.AhoraUtc, Estado = BoletaEstado.Recibida,
            RutaPdf = archivos.Guardar($"boletas/{p.Ciclo.Codigo}", nombreArchivo, pdf), HashPdf = hash,
            TextoExtraido = lectura.TextoNormalizado, Confianza = lectura.Confianza, ReemplazaABoletaId = anterior?.Id
        };
        Aplicar(b, lectura.Datos);
        if (anterior is not null) anterior.Estado = BoletaEstado.Reemplazada;
        p.Boletas.Add(b);
        var conc = await ConciliarAsync(p, b);

        auditor.Registrar(nameof(BoletaHonorarios), prest.RutPlanilla, canal == BoletaCanal.Portal ? "Subir boleta (portal)" : "Cargar boleta en nombre del prestador",
            $"N° {b.NumeroBoleta} · {Formato.Clp(b.MontoBruto)} · {p.Area.Nombre}" + (anterior is null ? "" : $" · reemplaza N° {anterior.NumeroBoleta}") +
            (canal == BoletaCanal.Operaciones ? $" · motivo: {motivo}" : ""));
        if (canal == BoletaCanal.Operaciones)
            correos.Encolar(prest.Email, "Operaciones cargó tu boleta",
                $"Hola {prest.NombreCompleto}: Operaciones cargó la boleta N° {b.NumeroBoleta} en tu nombre para la planilla {p.Area.Nombre} {p.Ciclo.Codigo}. Motivo: {motivo}.");
        await db.SaveChangesAsync();
        return new(b, conc, lectura, p.Lineas.Where(l => l.PrestadorId == prestadorId && l.Estado != LineaEstado.Diferida).Sum(l => l.ValorTotalBruto));
    }

    private static void Aplicar(BoletaHonorarios b, DatosBoleta d)
    {
        b.NumeroBoleta = d.Numero;
        if (RutHelper.TryParse(d.RutEmisor, out var c, out var dv)) { b.RutEmisor = c; b.RutEmisorDv = dv; }
        else { b.RutEmisor = null; b.RutEmisorDv = null; }
        b.NombreEmisor = d.NombreEmisor;
        b.RutReceptor = d.RutReceptor is not null && RutHelper.EsValido(d.RutReceptor) ? RutHelper.Normalizar(d.RutReceptor) : d.RutReceptor;
        b.FechaEmision = d.FechaEmision;
        b.MontoBruto = d.Bruto;
        b.MontoRetencion = d.Retencion;
        b.MontoLiquido = d.Liquido;
    }

    private static DatosBoleta Datos(BoletaHonorarios b) => new()
    {
        Numero = b.NumeroBoleta, RutEmisor = b.RutEmisor is null ? null : $"{b.RutEmisor}-{b.RutEmisorDv}", NombreEmisor = b.NombreEmisor,
        RutReceptor = b.RutReceptor, FechaEmision = b.FechaEmision, Bruto = b.MontoBruto, Retencion = b.MontoRetencion, Liquido = b.MontoLiquido,
        Anulada = b.TextoExtraido?.Contains("ANULADA") == true
    };

    /// <summary>R-06: concilia la boleta con las filas del prestador y actualiza el estado de las líneas.</summary>
    public async Task<ResultadoConciliacion> ConciliarAsync(Planilla p, BoletaHonorarios b)
    {
        var par = await parametros.ObtenerAsync();
        var filas = p.Lineas.Where(l => l.PrestadorId == b.PrestadorId && l.Estado != LineaEstado.Diferida).ToList();
        // (RUT emisor, N°) sin uso en otro pago: no cuentan esta boleta, la que reemplaza ni las rechazadas o reemplazadas.
        var reemplaza = b.ReemplazaABoletaId ?? -1;
        var usado = b.RutEmisor is not null && b.NumeroBoleta is not null && await db.Boletas.AnyAsync(x =>
            x.RutEmisor == b.RutEmisor && x.NumeroBoleta == b.NumeroBoleta && x.Id != b.Id && x.Id != reemplaza &&
            x.Estado != BoletaEstado.Rechazada && x.Estado != BoletaEstado.Reemplazada);
        var conc = Conciliacion.Conciliar(Datos(b), filas.FirstOrDefault()?.Prestador.Rut ?? 0, par.RutEmpresa, p.Ciclo.Periodo,
            filas.Select(l => l.ValorTotalBruto), (_, _) => usado, par.DiaLimiteBoleta);
        b.Cuadra = conc.Cuadra;
        b.ResultadoValidacion = conc.Cuadra ? null : string.Join(" ", conc.Problemas);
        Flujo.PropagarNumeroBoleta(p, b.PrestadorId, b.NumeroBoleta);

        if (conc.Cuadra)
        {
            foreach (var l in filas.Where(l => l.Estado == LineaEstado.PendienteBoleta)) l.Estado = LineaEstado.Lista;
            // Una boleta nueva y cuadrada corrige las observaciones tributarias abiertas.
            var lineaIds = filas.Select(l => l.Id).ToList();
            var obs = await db.Observaciones.Where(o => lineaIds.Contains(o.LineaPagoId) && o.Estado == ObservacionEstado.Abierta &&
                                                        (o.Tipo == ObservacionTipo.FaltaBoleta || o.Tipo == ObservacionTipo.DiferenciaMontos)).ToListAsync();
            foreach (var o in obs)
            {
                o.Estado = ObservacionEstado.Corregida;
                o.CorregidaPor = usuario.Nombre;
                o.CorregidaEn = ciclos.AhoraUtc;
                o.Respuesta = $"Nueva boleta N° {b.NumeroBoleta} por {Formato.Clp(b.MontoBruto)}.";
                var l = filas.First(x => x.Id == o.LineaPagoId);
                l.Estado = LineaEstado.Corregida;
            }
        }
        return conc;
    }

    public async Task<BoletaHonorarios> ObtenerAsync(int id) =>
        await db.Boletas.Include(b => b.Planilla).ThenInclude(p => p.Ciclo).Include(b => b.Prestador).FirstOrDefaultAsync(b => b.Id == id)
        ?? throw new ReglaException("Boleta no encontrada.");

    /// <summary>Operaciones o Finanzas confirman la lectura.</summary>
    public async Task ConfirmarAsync(int boletaId)
    {
        var b = await ObtenerAsync(boletaId);
        CicloService.ExigirAbierto(b.Planilla.Ciclo);
        if (!b.Vigente) throw new ReglaException("La boleta no está vigente.");
        if (!b.Cuadra) throw new ReglaException("La boleta no cuadra: " + b.ResultadoValidacion);
        b.Estado = BoletaEstado.Confirmada;
        b.ConfirmadaPor = usuario.Nombre;
        b.ConfirmadaEn = ciclos.AhoraUtc;
        auditor.Registrar(nameof(BoletaHonorarios), b.Id, "Confirmar boleta", $"N° {b.NumeroBoleta} · {b.Prestador.NombreCompleto}");
        await db.SaveChangesAsync();
    }

    /// <summary>Operaciones o Finanzas corrigen la lectura (luego se vuelve a conciliar).</summary>
    public async Task<ResultadoConciliacion> CorregirLecturaAsync(int boletaId, DatosBoleta datos)
    {
        var b = await ObtenerAsync(boletaId);
        CicloService.ExigirAbierto(b.Planilla.Ciclo);
        if (!b.Vigente) throw new ReglaException("La boleta no está vigente.");
        if (RutHelper.TryParse(datos.RutEmisor, out var e, out _) && e != b.Prestador.Rut)
            throw new ReglaException(Conciliacion.MensajeEmisor + ".");
        var antes = $"N° {b.NumeroBoleta} {Formato.Clp(b.MontoBruto)}";
        Aplicar(b, datos);
        b.Confianza = LectorBoletaTexto.CalcularConfianza(datos);
        var p = await ciclos.PlanillaCompletaAsync(b.PlanillaId);
        var conc = await ConciliarAsync(p!, p!.Boletas.First(x => x.Id == b.Id));
        auditor.Registrar(nameof(BoletaHonorarios), b.Id, "Corregir lectura", $"{antes} → N° {b.NumeroBoleta} {Formato.Clp(b.MontoBruto)}");
        await db.SaveChangesAsync();
        return conc;
    }

    /// <summary>Pide una nueva boleta al prestador: la actual queda Observada y se avisa por correo (R-21).</summary>
    public async Task PedirNuevaAsync(int boletaId, string motivo)
    {
        var b = await ObtenerAsync(boletaId);
        CicloService.ExigirAbierto(b.Planilla.Ciclo);
        if (string.IsNullOrWhiteSpace(motivo)) throw new ReglaException("Indica el motivo.");
        b.Estado = BoletaEstado.Observada;
        auditor.Registrar(nameof(BoletaHonorarios), b.Id, "Pedir nueva boleta", motivo);
        correos.Encolar(b.Prestador.Email, "Tu boleta fue observada",
            $"Hola {b.Prestador.NombreCompleto}: tu boleta N° {b.NumeroBoleta} fue observada. Motivo: {motivo}. Sube una nueva desde el portal.");
        await db.SaveChangesAsync();
    }

    /// <summary>R-21: recordatorio manual a los prestadores sin boleta.</summary>
    public async Task<int> RecordarPendientesAsync(int planillaId)
    {
        var p = await ciclos.PlanillaCompletaAsync(planillaId) ?? throw new ReglaException("Planilla no encontrada.");
        var pendientes = p.Activas().GroupBy(l => l.PrestadorId).Where(g => p.BoletaVigente(g.Key) is null).Select(g => g.First().Prestador).ToList();
        var par = await parametros.ObtenerAsync();
        foreach (var prest in pendientes)
            correos.Encolar(prest.Email, $"Recordatorio: sube tu boleta de {p.Ciclo.Codigo}",
                $"Hola {prest.NombreCompleto}: aún no recibimos tu boleta para la planilla {p.Area.Nombre}. Plazo de emisión hasta el {Formato.Fecha(Conciliacion.FechaLimite(p.Ciclo.Periodo, par.DiaLimiteBoleta))}.");
        auditor.Registrar(nameof(Planilla), p.Id, "Recordar a pendientes", $"{pendientes.Count} prestadores");
        await db.SaveChangesAsync();
        return pendientes.Count;
    }
}
