using System.IO.Compression;
using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.EntityFrameworkCore;
using static IpsosPagoHonorarios.Tests.Entorno;

namespace IpsosPagoHonorarios.Tests.Integracion;

/// <summary>Boletas (R-06, R-17 a R-19, R-22), revisión (R-09 a R-13), pago y cierre (R-14, R-15), auditoría (R-16) y avisos (R-21).</summary>
public class RevisionYPagoTests
{
    private sealed class F2F : IDisposable
    {
        public Entorno E { get; } = new();
        public Prestador Cam, Mat, Val, Fra, Agu;
        public Planilla P = null!;

        public F2F()
        {
            Cam = E.Prestador("Camila Rojas Fuentes", "15234871-1", "VISTA", "1734567890", "BANEFE");
            Mat = E.Prestador("Matías González Pérez", "16987452-2", "CORRIENTE", "0012345678", "CHILE");
            Val = E.Prestador("Valentina Muñoz Soto", "17345120-2", "VISTA", "1712343456", "BANEFE");
            Fra = E.Prestador("Francisca Silva Morales", "13987654-7", "CORRIENTE", "0068123456", "SANTANDER");
            Agu = E.Prestador("Agustín Flores Medina", "17654321-3", "RUT", "17654321", "ESTADO", CuentaEstado.PendienteValidacion);
        }

        public async Task<F2F> CargarAsync(bool boletaFrancisca = true)
        {
            P = await E.PlanillaAsync("FACE TO FACE",
                new Fila(Cam, "260041200105", Entorno.E, 6500, 42), new Fila(Cam, "260043100104", Entorno.E, 7200, 10),
                new Fila(Mat, "260041200105", Entorno.E, 6500, 38), new Fila(Mat, "260041200105", S, 45000, 1),
                new Fila(Val, "260043100104", Entorno.E, 7200, 25), new Fila(Fra, "260041200105", Entorno.E, 6500, 51),
                new Fila(Agu, "260041200105", Entorno.E, 6500, 40));
            await E.SubirAsync(P, Cam, "1245", 345000);
            await E.SubirAsync(P, Mat, "3318", 292000);
            await E.SubirAsync(P, Val, "87", 182000);   // diferencia de $2.000: se acepta mostrando la diferencia
            if (boletaFrancisca) await E.SubirAsync(P, Fra, "77", 331500);
            await E.SubirAsync(P, Agu, "512", 260000, BoletaCanal.Operaciones, "Entregó el PDF por correo");
            P = await E.RecargarAsync(P);
            return this;
        }

        public async Task EnviarAsync()
        {
            await E.Planillas.EnviarAsync(P.Id);
            P = await E.RecargarAsync(P);
        }

        public void Dispose() => E.Dispose();
    }

    [Fact]
    public async Task R06_UnaBoletaPorPrestador_NumeroRepetido_YReemplazo()
    {
        using var f = await new F2F().CargarAsync();
        var cam = f.P.Lineas.Where(l => l.PrestadorId == f.Cam.Id).ToList();
        Assert.All(cam, l => Assert.Equal("1245", l.NumeroBoleta));
        Assert.All(cam, l => Assert.Equal(LineaEstado.Lista, l.Estado));
        Assert.True(f.P.BoletaVigente(f.Cam.Id)!.Cuadra);
        // Valentina: diferencia → no cuadra, la línea sigue pendiente.
        var val = f.P.BoletaVigente(f.Val.Id)!;
        Assert.False(val.Cuadra);
        Assert.Contains("$2.000", val.ResultadoValidacion);
        Assert.Equal(LineaEstado.PendienteBoleta, f.P.Lineas.Single(l => l.PrestadorId == f.Val.Id).Estado);
        // Segunda boleta: reemplaza a la anterior.
        f.E.Como("Valentina Muñoz Soto");
        var r = await f.E.SubirAsync(f.P, f.Val, "92", 180000);
        Assert.True(r.Conciliacion.Cuadra);
        Assert.Equal(BoletaEstado.Reemplazada, (await f.E.Db.Boletas.FindAsync(val.Id))!.Estado);
        Assert.Equal(val.Id, r.Boleta.ReemplazaABoletaId);
    }

    [Fact]
    public async Task R19_ValidacionInmediataDelArchivo()
    {
        using var f = await new F2F().CargarAsync(boletaFrancisca: false);
        var ex = await Assert.ThrowsAsync<ReglaException>(() => f.E.Boletas.SubirAsync(f.P.Id, f.Fra.Id, "no soy un pdf"u8.ToArray(), "x.pdf", BoletaCanal.Portal));
        Assert.Contains("PDF", ex.Message);
        var grande = new byte[LectorPdf.TamanoMaximo + 1];
        "%PDF-"u8.CopyTo(grande);
        ex = await Assert.ThrowsAsync<ReglaException>(() => f.E.Boletas.SubirAsync(f.P.Id, f.Fra.Id, grande, "x.pdf", BoletaCanal.Portal));
        Assert.Contains("2 MB", ex.Message);
        // Emitida por otro RUT → "La boleta debe estar emitida por usted".
        var ajena = Pdf(f.Fra, "10", 331500, rutEmisor: f.Mat.RutPlanilla);
        ex = await Assert.ThrowsAsync<ReglaException>(() => f.E.Boletas.SubirAsync(f.P.Id, f.Fra.Id, ajena, "x.pdf", BoletaCanal.Portal));
        Assert.Equal("La boleta debe estar emitida por usted.", ex.Message);
        // PDF duplicado (hash).
        var pdf = Pdf(f.Fra, "11", 331500);
        await f.E.Boletas.SubirAsync(f.P.Id, f.Fra.Id, pdf, "a.pdf", BoletaCanal.Portal);
        f.P = await f.E.RecargarAsync(f.P);
        await f.E.Boletas.PedirNuevaAsync(f.P.BoletaVigente(f.Fra.Id)!.Id, "prueba");
        ex = await Assert.ThrowsAsync<ReglaException>(() => f.E.Boletas.SubirAsync(f.P.Id, f.Fra.Id, pdf, "b.pdf", BoletaCanal.Portal));
        Assert.Contains("duplicado", ex.Message);
    }

    [Fact]
    public async Task BoletaFueraDePlazo_NoSeAdmite_SoloElAdministradorLaAutoriza()
    {
        using var f = await new F2F().CargarAsync(boletaFrancisca: false);
        var r = await f.E.SubirAsync(f.P, f.Fra, "500", 331500, BoletaCanal.Operaciones, "prueba", fecha: new DateOnly(2026, 9, 1));
        Assert.False(r.Conciliacion.Cuadra);
        Assert.True(r.Boleta.FueraDePlazo);
        f.P = await f.E.RecargarAsync(f.P);
        Assert.Equal(LineaEstado.PendienteBoleta, f.P.Lineas.Single(l => l.PrestadorId == f.Fra.Id).Estado);   // no se admite
        await Assert.ThrowsAsync<ReglaException>(() => f.E.Boletas.ConfirmarAsync(r.Boleta.Id));

        // Operaciones (o Finanzas) no puede autorizar.
        f.E.Usuario.Principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, Roles.Operaciones)], "prueba"));
        var ex = await Assert.ThrowsAsync<ReglaException>(() => f.E.Boletas.AutorizarFueraDePlazoAsync(r.Boleta.Id, "urgente"));
        Assert.Contains("Administrador", ex.Message);

        // El Administrador sí, con motivo; la boleta pasa a cuadrar y queda en la bitácora.
        f.E.Como("Admin");
        f.E.Usuario.Principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, Roles.Admin)], "prueba"));
        await Assert.ThrowsAsync<ReglaException>(() => f.E.Boletas.AutorizarFueraDePlazoAsync(r.Boleta.Id, " "));
        var c = await f.E.Boletas.AutorizarFueraDePlazoAsync(r.Boleta.Id, "Prestador emitió en septiembre por error del sistema");
        Assert.True(c.Cuadra);
        var b = await f.E.Db.Boletas.FindAsync(r.Boleta.Id);
        Assert.Equal(("Admin", true), (b!.FechaAutorizadaPor, b.Cuadra));
        f.P = await f.E.RecargarAsync(f.P);
        Assert.Equal(LineaEstado.Lista, f.P.Lineas.Single(l => l.PrestadorId == f.Fra.Id).Estado);
        Assert.Contains(await f.E.Db.Auditorias.ToListAsync(), a => a.Accion == "Autorizar boleta fuera de plazo");
    }

    [Fact]
    public async Task R22_CargaEnNombreDelPrestador_MotivoObligatorio_CanalYAviso()
    {
        using var f = await new F2F().CargarAsync(boletaFrancisca: false);
        await Assert.ThrowsAsync<ReglaException>(() => f.E.SubirAsync(f.P, f.Fra, "50", 331500, BoletaCanal.Operaciones, " "));
        var b = f.P.BoletaVigente(f.Agu.Id)!;
        Assert.Equal(BoletaCanal.Operaciones, b.Canal);
        Assert.Equal("Entregó el PDF por correo", b.MotivoCargaOperaciones);
        Assert.Contains(await f.E.Db.Auditorias.ToListAsync(), a => a.Accion == "Cargar boleta en nombre del prestador" && a.Detalle!.Contains("motivo"));
        Assert.Contains(await f.E.Db.Correos.ToListAsync(), c => c.Para == f.Agu.Email && c.Asunto.Contains("Operaciones cargó tu boleta"));
    }

    [Fact]
    public async Task R17_R18_PrestadorSoloVeYSubeLoSuyo()
    {
        using var f = await new F2F().CargarAsync(boletaFrancisca: false);
        var mias = await f.E.Portal.MisPlanillasAsync(f.Val.Id);
        var unica = Assert.Single(mias);
        Assert.All(unica.Lineas, l => Assert.Equal(f.Val.Id, l.PrestadorId));
        Assert.Equal(180000m, unica.Bruto);
        Assert.Equal(27450m, unica.Retencion);
        Assert.Equal(152550m, unica.Liquido);
        // Un prestador sin filas en la planilla no puede subir boleta en ella.
        var otro = f.E.Prestador("Lucas Ortiz Díaz", "15678901-1");
        Assert.Empty(await f.E.Portal.MisPlanillasAsync(otro.Id));
        var ex = await Assert.ThrowsAsync<ReglaException>(() => f.E.SubirAsync(f.P, otro, "1", 180000));
        Assert.Contains("No tienes filas", ex.Message);
        // R-18: con la boleta ya cuadrada (y no observada) no se reemplaza.
        ex = await Assert.ThrowsAsync<ReglaException>(() => f.E.SubirAsync(f.P, f.Cam, "1246", 345000));
        Assert.Contains("no está observada", ex.Message);
    }

    [Fact]
    public async Task R09_R10_R11_RevisionDevolucionYReenvioATiempo()
    {
        using var f = await new F2F().CargarAsync();
        await f.EnviarAsync();
        var val = Flujo.Revisar(f.P, f.Val.Id);
        Assert.Equal((Chequeo.Ok, Chequeo.Ok, Chequeo.Error, Chequeo.Ok), (val.Personal, val.Operativa, val.Tributaria, val.Bancaria));
        Assert.Equal("Diferencia $2.000", val.DetalleTributaria);
        Assert.Equal(Chequeo.Pendiente, Flujo.Revisar(f.P, f.Agu.Id).Bancaria);
        Assert.Equal(Chequeo.Ok, Flujo.Revisar(f.P, f.Cam.Id).Tributaria);

        f.E.Como("Carolina Díaz");
        // R-10: no se devuelve sin observaciones abiertas.
        await Assert.ThrowsAsync<ReglaException>(() => f.E.Revision.DevolverAsync(f.P.Id));
        await Assert.ThrowsAsync<ReglaException>(() => f.E.Revision.ObservarAsync(f.P.Lineas.First().Id, ObservacionTipo.DatosIncompletos, "", "x"));
        var linea5 = f.P.Lineas.Single(l => l.PrestadorId == f.Val.Id);
        await f.E.Revision.ObservarAsync(linea5.Id, ObservacionTipo.DiferenciaMontos, "Monto boleta", "Boleta N° 87 por $182.000; la suma es $180.000.");
        var d = await f.E.Revision.DevolverAsync(f.P.Id);
        Assert.Equal(d.DevueltaEn.AddMinutes(60), d.VenceEn);   // plazo de 60 minutos
        f.P = await f.E.RecargarAsync(f.P);
        Assert.Equal(PlanillaEstado.Observada, f.P.Estado);
        Assert.Equal(BoletaEstado.Observada, f.P.BoletaVigente(f.Val.Id)!.Estado);
        var correos = await f.E.Db.Correos.ToListAsync();
        Assert.Contains(correos, c => c.Para == "andres.paredes@ejemplo.cl" && c.Asunto.Contains("devuelta"));
        Assert.Contains(correos, c => c.Para == f.Val.Email && c.Asunto.Contains("observada"));

        // La prestadora sube la boleta corregida: la observación queda Corregida.
        f.E.Como("Valentina Muñoz Soto");
        await f.E.SubirAsync(f.P, f.Val, "92", 180000);
        Assert.Equal(ObservacionEstado.Corregida, (await f.E.Db.Observaciones.SingleAsync()).Estado);

        // R-11: reenvío antes del vencimiento.
        f.E.Reloj.Advance(TimeSpan.FromMinutes(59));
        f.E.Como("Andrés Paredes");
        await f.E.Planillas.ReenviarAsync(f.P.Id);
        f.P = await f.E.RecargarAsync(f.P);
        Assert.Equal((PlanillaEstado.EnRevision, 2), (f.P.Estado, f.P.Version));
        Assert.Equal(DevolucionResultado.ReenviadaATiempo, (await f.E.Db.Devoluciones.SingleAsync()).Resultado);
        Assert.Contains(await f.E.Db.PlanillaArchivos.ToListAsync(), a => a.Version == 2 && a.NombreArchivo.EndsWith("FACE TO FACE v2.xlsx"));
    }

    [Fact]
    public async Task R11_ReenvioFueraDePlazo_NoSePermite()
    {
        using var f = await new F2F().CargarAsync();
        await f.EnviarAsync();
        await f.E.Revision.ObservarAsync(f.P.Lineas.Single(l => l.PrestadorId == f.Fra.Id).Id, ObservacionTipo.ErrorDigitacion, "Nombres", "x");
        await f.E.Revision.DevolverAsync(f.P.Id);
        f.E.Reloj.Advance(TimeSpan.FromMinutes(60));
        var ex = await Assert.ThrowsAsync<ReglaException>(() => f.E.Planillas.ReenviarAsync(f.P.Id));
        Assert.Contains("venció", ex.Message);
    }

    [Fact]
    public async Task R12_AlVencer_SeDifierenFilasConObservacionesAbiertas()
    {
        using var f = await new F2F().CargarAsync(boletaFrancisca: false);
        // Escenario del mockup: enviada con Francisca sin boleta; Finanzas observa y devuelve.
        f.P.Estado = PlanillaEstado.EnRevision;
        await f.E.Db.SaveChangesAsync();
        var lfra = f.P.Lineas.Single(l => l.PrestadorId == f.Fra.Id);
        var lval = f.P.Lineas.Single(l => l.PrestadorId == f.Val.Id);
        await f.E.Revision.ObservarAsync(lfra.Id, ObservacionTipo.FaltaBoleta, "N° boleta", "Sin boleta.");
        await f.E.Revision.ObservarAsync(lval.Id, ObservacionTipo.DiferenciaMontos, "Monto boleta", "Diferencia.");
        await f.E.Revision.DevolverAsync(f.P.Id);

        f.E.Reloj.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(0, await f.E.Plazos.ProcesarVencidasAsync());   // aún no vence
        f.E.Reloj.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await f.E.Plazos.ProcesarVencidasAsync());

        f.P = await f.E.RecargarAsync(f.P);
        Assert.Equal(PlanillaEstado.EnRevision, f.P.Estado);   // el resto sigue en revisión
        Assert.Equal(LineaEstado.Diferida, f.P.Lineas.Single(l => l.Id == lfra.Id).Estado);
        Assert.Equal(LineaEstado.Diferida, f.P.Lineas.Single(l => l.Id == lval.Id).Estado);
        Assert.Equal(LineaEstado.Lista, f.P.Lineas.First(l => l.PrestadorId == f.Cam.Id).Estado);
        Assert.All(await f.E.Db.Observaciones.ToListAsync(), o => Assert.Equal(ObservacionEstado.Vencida, o.Estado));
        Assert.Equal(DevolucionResultado.Vencida, (await f.E.Db.Devoluciones.SingleAsync()).Resultado);
        var nov = await f.E.Db.Planillas.Include(p => p.Ciclo).Include(p => p.Lineas).Include(p => p.Area).SingleAsync(p => p.Ciclo.Codigo == "NOV-2026");
        Assert.Equal("FACE TO FACE", nov.Area.Nombre);
        Assert.Equal([180000m, 331500m], nov.Lineas.Select(l => l.ValorTotalBruto).Order());
        Assert.All(nov.Lineas, l => Assert.Equal(LineaEstado.PendienteBoleta, l.Estado));
    }

    [Fact]
    public async Task R13_AprobacionBloqueada_ConObservacionesOCuentasSinValidar()
    {
        using var f = await new F2F().CargarAsync();
        f.E.Como("Valentina Muñoz Soto");
        await f.E.SubirAsync(f.P, f.Val, "92", 180000);
        f.E.Como("Andrés Paredes");
        await f.EnviarAsync();
        f.E.Como("Carolina Díaz");
        var o = await f.E.Revision.ObservarAsync(f.P.Lineas.First().Id, ObservacionTipo.ErrorDigitacion, "Nombres", "Revisar");
        var ex = await Assert.ThrowsAsync<ReglaException>(() => f.E.Revision.AprobarAsync(f.P.Id));
        Assert.Contains("observaciones abiertas", ex.Message);
        Assert.Contains("cuenta no validada", ex.Message);   // Agustín

        await f.E.Revision.CorregirAsync(o.Id, "Corregido");
        ex = await Assert.ThrowsAsync<ReglaException>(() => f.E.Revision.AprobarAsync(f.P.Id));
        Assert.Contains("correcciones sin revisar", ex.Message);
        await f.E.Revision.AceptarAsync(o.Id);

        var cuenta = await f.E.Db.CuentasBancarias.SingleAsync(c => c.PrestadorId == f.Agu.Id);
        await f.E.Cuentas.ValidarAsync(cuenta.Id);
        await f.E.Revision.AprobarAsync(f.P.Id);
        f.P = await f.E.RecargarAsync(f.P);
        Assert.Equal(PlanillaEstado.Aprobada, f.P.Estado);
        Assert.All(f.P.Activas(), l => Assert.Equal(LineaEstado.Aprobada, l.Estado));
        Assert.All(f.P.Boletas.Where(b => b.Vigente), b => Assert.Equal(BoletaEstado.Confirmada, b.Estado));
        // Congelada: ya no admite boletas (R-18) ni observaciones.
        await Assert.ThrowsAsync<ReglaException>(() => f.E.SubirAsync(f.P, f.Val, "93", 180000));
        await Assert.ThrowsAsync<ReglaException>(() => f.E.Revision.ObservarAsync(f.P.Lineas.First().Id, ObservacionTipo.ErrorDigitacion, "x", "y"));
    }

    [Fact]
    public async Task R14_R15_R16_R21_PagoCierreYArchivo()
    {
        using var f = await new F2F().CargarAsync();
        f.E.Como("Valentina Muñoz Soto");
        await f.E.SubirAsync(f.P, f.Val, "92", 180000);
        f.E.Como("Andrés Paredes");
        await f.EnviarAsync();
        f.E.Como("Carolina Díaz");
        await f.E.Cuentas.ValidarAsync((await f.E.Db.CuentasBancarias.SingleAsync(c => c.PrestadorId == f.Agu.Id)).Id);
        await f.E.Revision.AprobarAsync(f.P.Id);

        var (nomina, nombre) = await f.E.Pagos.NominaAsync(f.P.Id);
        Assert.StartsWith("Nomina honorarios OCTUBRE_2026", nombre);
        using (var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(nomina)))
        {
            var ws = wb.Worksheet("Nomina");
            Assert.Equal("15234871-1", ws.Cell(4, 1).GetString());
            Assert.Equal("1734567890", ws.Cell(4, 7).GetString());   // cuenta registrada (R-27)
            Assert.Equal(292387d, ws.Cell(4, 8).Value.GetNumber());  // líquido por boleta
        }
        Assert.Equal(PlanillaEstado.EnPago, (await f.E.RecargarAsync(f.P)).Estado);

        // R-15: no se cierra con líneas sin pagar.
        await Assert.ThrowsAsync<ReglaException>(() => f.E.Pagos.CerrarCicloAsync(f.P.CicloId));
        await Assert.ThrowsAsync<ReglaException>(() =>
            f.E.Pagos.RegistrarTransferenciaAsync(f.P.Id, f.Cam.Id, new DateOnly(2026, 11, 5), "OP-1", "no pdf"u8.ToArray(), "c.pdf"));
        var n = 0;
        // Comprobante como imagen: se admiten JPG y PNG (por contenido), además de PDF.
        byte[] jpg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F'];
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];
        Assert.Equal(".jpg", PagoService.TipoComprobante(jpg)?.Ext);
        Assert.Equal("image/png", PagoService.TipoComprobante(png)?.Mime);
        foreach (var x in await f.E.Pagos.ResumenAsync(await f.E.RecargarAsync(f.P)))
        {
            ++n;
            var (bytes, archivo) = n switch
            {
                1 => (jpg, "foto deposito.jpeg"),
                2 => (png, "captura.PNG"),
                _ => (BoletaPdf.Comprobante($"OP-00488{n}", x.Prestador.RutPlanilla, x.Liquido, new DateOnly(2026, 11, 5)), "comprobante.pdf")
            };
            await f.E.Pagos.RegistrarTransferenciaAsync(f.P.Id, x.Prestador.Id, new DateOnly(2026, 11, 5), $"OP-00488{n}", bytes, archivo);
        }
        f.P = await f.E.RecargarAsync(f.P);
        Assert.All(f.P.Activas(), l => Assert.Equal(LineaEstado.Pagada, l.Estado));
        Assert.Equal(5, await f.E.Db.Transferencias.CountAsync());   // una por boleta
        Assert.Equal(292387m, (await f.E.Db.Transferencias.SingleAsync(t => t.PrestadorId == f.Cam.Id)).MontoLiquido);
        Assert.Contains(await f.E.Db.Correos.ToListAsync(), c => c.Para == f.Cam.Email && c.Asunto.Contains("transferido"));

        await f.E.Pagos.CerrarCicloAsync(f.P.CicloId);
        var ciclo = await f.E.Db.Ciclos.SingleAsync(c => c.Id == f.P.CicloId);
        Assert.Equal(CicloEstado.Cerrado, ciclo.Estado);
        using (var zip = new ZipArchive(new MemoryStream(f.E.Archivos.Leer(ciclo.RutaZip!))))
        {
            var nombres = zip.Entries.Select(x => x.FullName).ToList();
            Assert.Contains(nombres, x => x.StartsWith("planillas/FACE TO FACE/Planilla_v1"));
            Assert.Contains(nombres, x => x.StartsWith("planillas/FACE TO FACE/Original_v1"));
            Assert.Contains(nombres, x => x.StartsWith("nominas/"));
            Assert.Equal(6, nombres.Count(x => x.StartsWith("boletas/")));      // incluye la reemplazada
            Assert.Equal(5, nombres.Count(x => x.StartsWith("comprobantes/")));
            Assert.Single(nombres, x => x.StartsWith("comprobantes/") && x.EndsWith(".jpg"));
            Assert.Single(nombres, x => x.StartsWith("comprobantes/") && x.EndsWith(".png"));
            Assert.Contains("bitacora.csv", nombres);
        }
        // Solo lectura tras el cierre.
        await Assert.ThrowsAsync<ReglaException>(() => f.E.Planillas.DiferirLineaAsync(f.P.Lineas.First().Id, "x"));
        // R-16: auditoría de cambios de estado, cambios bancarios, cargas de archivo y observaciones.
        var acciones = (await f.E.Db.Auditorias.Select(a => a.Accion).ToListAsync()).ToHashSet();
        Assert.Superset(new HashSet<string> { "Importar producción", "Validar cuentas", "Subir boleta (portal)", "Cargar boleta en nombre del prestador",
            "Enviar a Finanzas", "Validar cuenta", "Aprobar planilla", "Generar nómina", "Registrar transferencia", "Cerrar ciclo" }, acciones);
    }
}
