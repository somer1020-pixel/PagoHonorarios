using ClosedXML.Excel;
using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.EntityFrameworkCore;
using static IpsosPagoHonorarios.Tests.Entorno;

namespace IpsosPagoHonorarios.Tests.Integracion;

/// <summary>R-02, R-08, R-16, R-21, R-23 a R-28 y formato XLSX sobre servicios reales.</summary>
public class PlanillaYCuentasTests
{
    private static async Task<(Entorno E, Planilla P, Dictionary<string, Prestador> Pr)> DataProcessingAsync()
    {
        var e = new Entorno();
        var pr = new Dictionary<string, Prestador>
        {
            ["pau"] = e.Prestador("Paula Navarro", "16234567-2", "CORRIENTE", "0071234506", "SANTANDER"),
            ["die"] = e.Prestador("Diego Morales", "15876543-8", "RUT", "15876543", "ESTADO"),
            ["jav"] = e.Prestador("Javiera Soto", "19345678-2"),
            ["rod"] = e.Prestador("Rodrigo Pinto", "17456789-1", "CORRIENTE", "0045678123", "CHILE"),
            ["fer"] = e.Prestador("Fernanda Lagos", "18234567-9", "RUT", "18234567", "ESTADO"),
            ["ign"] = e.Prestador("Ignacio Vera", "20456789-1", "VISTA", "1798765432", "BANEFE"),
            ["sof"] = e.Prestador("Sofía Campos", "14567890-0", "CORRIENTE", "77889900", "BCI"),
            ["mat"] = e.Prestador("Matías González Pérez", "16987452-2", "CORRIENTE", "0012345678", "CHILE"),
        };
        const string J = "260045100301";
        var p = await e.PlanillaAsync("DATA PROCESSING",
            new Fila(pr["pau"], J, DPG, 250, 85.28m, ("CORRIENTE", "0071234560", "SANTANDER")),
            new Fila(pr["pau"], J, DPG, 250, 102.4m, ("CORRIENTE", "0071234560", "SANTANDER")),
            new Fila(pr["die"], J, DPG, 250, 120.5m, ("RUT", "15876543", "ESTADO")),
            new Fila(pr["jav"], J, DPG, 250, 96.75m, ("VISTA", "1712345678", "BANEFE")),
            new Fila(pr["rod"], J, DPG, 250, 110, ("CORRIENTE", "0012345678", "CHILE")),
            new Fila(pr["fer"], J, DPG, 250, 88.36m, ("RUT", "18234560", "ESTADO")),
            new Fila(pr["ign"], J, DPG, 250, 130.12m, ("VISTA", "1798765432", "BANEFE")),
            new Fila(pr["ign"], J, DPG, 250, 45, ("VISTA", "1798765432", "BANEFE")),
            new Fila(pr["sof"], J, DPG, 250, 99.845m, ("VISTA", "1029384756", "MERCADO PAGO")));
        return (e, await e.RecargarAsync(p), pr);
    }

    [Fact]
    public async Task R24_CasoDataProcessing_2Coincide_2Tipeo_1Nueva_1Distinta_1Tercero()
    {
        var (e, p, _) = await DataProcessingAsync();
        using var _e = e;
        Assert.Equal(219564m, p.Activas().Sum(l => l.ValorTotalBruto));
        Assert.Equal(21320m, p.Lineas.Single(l => l.Numero == 1).ValorTotalBruto);
        var porPrestador = p.Activas().GroupBy(l => l.PrestadorId).Select(g => g.First().ResultadoCuenta).ToList();
        Assert.Equal(2, porPrestador.Count(r => r == ResultadoCuenta.Coincide));
        Assert.Equal(2, porPrestador.Count(r => r == ResultadoCuenta.PosibleErrorTipeo));
        Assert.Equal(1, porPrestador.Count(r => r == ResultadoCuenta.CuentaNueva));
        Assert.Equal(1, porPrestador.Count(r => r == ResultadoCuenta.DistintaRegistrada));
        Assert.Equal(1, porPrestador.Count(r => r == ResultadoCuenta.CuentaTercero));
        Assert.Contains("Matías", p.Lineas.Single(l => l.Numero == 5).ResultadoCuentaDetalle);
    }

    [Fact]
    public async Task R25_ConAlertasNoSeEnvia_SeResuelveRegistrandoODifiriendo()
    {
        var (e, p, pr) = await DataProcessingAsync();
        using var _e = e;
        Assert.Equal(PlanillaEstado.ConAlertasCuenta, p.Estado);
        var ex = await Assert.ThrowsAsync<ReglaException>(() => e.Planillas.EnviarAsync(p.Id));
        Assert.Contains("5 alertas", ex.Message);

        // Registrar la cuenta nueva de Javiera (R-23) resuelve su alerta.
        var cta = await e.Cuentas.RegistrarAsync(pr["jav"].Id, "BANEFE", "VISTA", "1712345678", "1712345678");
        Assert.Equal(CuentaEstado.PendienteValidacion, cta.Estado);
        p = await e.RecargarAsync(p);
        Assert.True(p.Lineas.Single(l => l.Numero == 4).AlertaCuentaResuelta);

        // Diferir el resto de las filas con alertas deja la planilla en Borrador.
        foreach (var n in new[] { 1, 5, 6, 9 })
            await e.Planillas.DiferirLineaAsync(p.Lineas.Single(l => l.Numero == n).Id, "prueba");
        p = await e.RecargarAsync(p);
        Assert.Equal(PlanillaEstado.Borrador, p.Estado);
        Assert.All(p.Lineas.Where(l => l.PrestadorId == pr["pau"].Id), l => Assert.Equal(LineaEstado.Diferida, l.Estado));
        var nov = await e.Db.Planillas.Include(x => x.Ciclo).Include(x => x.Lineas).SingleAsync(x => x.Ciclo.Codigo == "NOV-2026");
        Assert.Equal(5, nov.Lineas.Count);   // Paula (2 filas), Rodrigo, Fernanda y Sofía copiadas al ciclo siguiente
        Assert.All(nov.Lineas, l => Assert.Equal(p.CicloId, l.DiferidaDesdeCicloId));
        // El ciclo creado por adelantado no reemplaza al ciclo en curso como selección por defecto.
        Assert.Equal("OCT-2026", (await e.Ciclos.SeleccionadoAsync(null))!.Codigo);
    }

    [Fact]
    public async Task R08_SoloSeEnviaConBoletaDeTodos()
    {
        using var e = new Entorno();
        var cam = e.Prestador("Camila Rojas Fuentes", "15234871-1", "VISTA", "1734567890", "BANEFE");
        var fra = e.Prestador("Francisca Silva Morales", "13987654-7", "CORRIENTE", "0068123456", "SANTANDER");
        var p = await e.PlanillaAsync("FACE TO FACE", new Fila(cam, "260041200105", E, 6500, 42), new Fila(cam, "260043100104", E, 7200, 10),
            new Fila(fra, "260041200105", E, 6500, 51));
        Assert.Equal(PlanillaEstado.Borrador, p.Estado);
        await e.SubirAsync(p, cam, "1245", 345000);
        var ex = await Assert.ThrowsAsync<ReglaException>(() => e.Planillas.EnviarAsync(p.Id));
        Assert.Contains("Faltan boletas de 1", ex.Message);
        await e.Planillas.DiferirLineaAsync(p.Lineas.Single(l => l.PrestadorId == fra.Id).Id, "sin boleta");
        await e.Planillas.EnviarAsync(p.Id);
        Assert.Equal(PlanillaEstado.EnRevision, (await e.RecargarAsync(p)).Estado);
        Assert.True(await e.Db.PlanillaArchivos.AnyAsync(a => a.PlanillaId == p.Id && a.Tipo == "Planilla"));
    }

    [Fact]
    public async Task R02_ArchivoConErroresNoSeCarga_YNuevosSeCrean()
    {
        using var e = new Entorno();
        var cam = e.Prestador("Camila Rojas Fuentes", "15234871-1");
        var malo = await e.ImportarAsync("FACE TO FACE", new Fila(cam, "26004120010", E, 6500, 42), new Fila(cam, "260041200105", E, 6500, 0));
        Assert.False(malo.Cargada);
        Assert.Equal(2, malo.Errores.Count);
        Assert.False(await e.Db.Planillas.AnyAsync());
        Assert.Contains(await e.Db.Auditorias.ToListAsync(), a => a.Accion == "Importación rechazada");

        var nuevo = new Prestador { NombreCompleto = "Benjamín Castro Núñez", Rut = 19876543, Dv = "0" };
        var ok = await e.ImportarAsync("FACE TO FACE", new Fila(nuevo, "260044200110", E, 7450, 28, SinCuenta: true));
        Assert.True(ok.Cargada);
        Assert.Contains(ok.Avisos, a => a.Contains("19.876.543-0"));
        Assert.True(await e.Db.Prestadores.AnyAsync(x => x.Rut == 19876543));
        Assert.True(await e.Db.Jobs.AnyAsync(j => j.JobBookNumber == "260044200110"));
        Assert.Equal(208600m, ok.Planilla!.Activas().Single().ValorTotalBruto);
    }

    [Fact]
    public async Task R26_SolicitudDeCorreccion_RegistradaYReenviable()
    {
        var (e, p, _) = await DataProcessingAsync();
        using var _e = e;
        var s = await e.Cuentas.SolicitarCorreccionAsync(p.Id, "Felipe Araya <felipe.araya@ejemplo.cl>", "Revisa las cuentas.");
        var filas = Json.Leer<List<FilaSolicitud>>(s.Filas)!;
        Assert.Equal([1, 2, 4, 5, 6, 9], filas.Select(f => f.Fila));
        Assert.Contains(await e.Db.Correos.ToListAsync(), c => c.Para == "felipe.araya@ejemplo.cl" && c.Cuerpo.Contains("Fila 5"));
        await e.Cuentas.ReenviarSolicitudAsync(s.Id);
        var r = await e.Db.SolicitudesCorreccion.SingleAsync();
        Assert.Equal(2, r.Envios);
        Assert.Equal(SolicitudEstado.Reenviada, r.Estado);
    }

    [Fact]
    public async Task R23_RegistroYValidacion_DejaAnteriorInactiva()
    {
        using var e = new Entorno();
        var agu = e.Prestador("Agustín Flores Medina", "17654321-3", "VISTA", "1100987654", "Tenpo");
        await e.Cuentas.RegistrarAsync(agu.Id, "ESTADO", "RUT", "17654321", "17654321");
        var cuentas = await e.Db.CuentasBancarias.Where(c => c.PrestadorId == agu.Id).ToListAsync();
        Assert.Single(cuentas, c => c.Vigente);
        Assert.Equal(CuentaEstado.Inactiva, cuentas.Single(c => c.Banco == "Tenpo").Estado);
        var nueva = cuentas.Single(c => c.Vigente);
        Assert.Equal(CuentaEstado.PendienteValidacion, nueva.Estado);
        e.Como("Carolina Díaz");
        await e.Cuentas.ValidarAsync(nueva.Id);
        Assert.Equal(CuentaEstado.Validada, (await e.Db.CuentasBancarias.FindAsync(nueva.Id))!.Estado);
        Assert.Equal("Carolina Díaz", (await e.Db.CuentasBancarias.FindAsync(nueva.Id))!.ValidadaPor);
        // R-07: la cuenta de otro RUT no se puede registrar.
        var otro = e.Prestador("Lucas Ortiz Díaz", "15678901-1");
        var ex = await Assert.ThrowsAsync<ReglaException>(() => e.Cuentas.RegistrarAsync(otro.Id, "ESTADO", "RUT", "17654321", "17654321"));
        Assert.Contains("RUT", ex.Message);
        // R-16: los cambios bancarios quedan en la bitácora.
        Assert.Equal(2, await e.Db.Auditorias.CountAsync(a => a.Accion == "Registrar cuenta" || a.Accion == "Validar cuenta"));
    }

    [Fact]
    public async Task R28_CargaInicial_RepetibleSinDuplicar()
    {
        using var e = new Entorno();
        List<FilaCuentaHistorica> filas =
        [
            new("15234871-1", "Camila Rojas Fuentes", "VISTA", "1734567890", "BANEFE", new(2026, 8, 1)),
            new("17654321-3", "Agustín Flores Medina", "VISTA", "1100987654", "Tenpo", new(2026, 7, 1)),
            new("17654321-3", "Agustín Flores Medina", "RUT", "17654321", "ESTADO", new(2026, 9, 1)),
            new("18765432-7", "Martina Vega Rojas", "VISTA", "1100223344", "Tenpo", new(2026, 7, 1)),
            new("15678901-1", "Lucas Ortiz Díaz", "VISTA", "1100223344", "Tenpo", new(2026, 8, 1)),
        ];
        var r = await e.Cuentas.CargaInicialAsync(filas, "prueba");
        Assert.Equal((1, 1, 1), (r.Unicas, r.ConVarias, r.Compartidas));
        var cam = await e.Db.Prestadores.Include(p => p.Cuentas).SingleAsync(p => p.Rut == 15234871);
        Assert.Equal(CuentaEstado.Validada, cam.Cuentas.Single().Estado);
        var agu = await e.Db.Prestadores.Include(p => p.Cuentas).SingleAsync(p => p.Rut == 17654321);
        Assert.Equal("ESTADO", agu.Cuentas.Single(c => c.Vigente).Banco);
        Assert.True(agu.Cuentas.Single(c => c.Vigente).RequiereRevision);
        Assert.Equal(CuentaEstado.Inactiva, agu.Cuentas.Single(c => !c.Vigente).Estado);
        Assert.False(await e.Db.CuentasBancarias.AnyAsync(c => c.CuentaNormalizada == "1100223344"));

        var total = await e.Db.CuentasBancarias.CountAsync();
        await e.Cuentas.CargaInicialAsync(filas, "repetida");
        Assert.Equal(total, await e.Db.CuentasBancarias.CountAsync());
    }

    [Fact]
    public async Task R27_Exportacion_RoundTripSinDiferencias_YB3IgualSumaH()
    {
        var (e, p, _) = await DataProcessingAsync();
        using var _e = e;
        var bytes = await e.Excel.ExportarAsync(p);

        using (var wb = new XLWorkbook(new MemoryStream(bytes)))
        {
            var ws = wb.Worksheet("Planilla");
            Assert.Equal("SUM(H9:H3007)", ws.Cell("B3").FormulaA1);
            Assert.Equal(219564d, ws.Cell("B3").Value.GetNumber());
            Assert.Equal("21184", ws.Cell("B5").Value.ToString());
            Assert.Equal("ROUND(F9*G9,0)", ws.Cell("H9").FormulaA1);
            Assert.Equal(21320d, ws.Cell("H9").Value.GetNumber());
            Assert.Equal("2830", ws.Cell("E9").Value.ToString());
            var sumaH = Enumerable.Range(9, 9).Sum(r => ws.Cell(r, 8).Value.GetNumber());
            Assert.Equal(ws.Cell("B3").Value.GetNumber(), sumaH);
            // R-27: O, P, Q con la cuenta registrada, no con la de la planilla (Paula: 0071234506, no 0071234560).
            Assert.Equal("0071234506", ws.Cell(9, 16).GetString());
            Assert.Equal("16234567-2", ws.Cell(9, 9).GetString());
            Assert.True(wb.Worksheets.Contains("Formato"));
            Assert.Contains(wb.DefinedNames, n => n.Name == "banco");
            Assert.Contains(wb.DefinedNames, n => n.Name == "tipos_de_cuenta");
        }

        var leido = ExcelPlanilla.LeerFormatoFinanzas(new MemoryStream(bytes));
        Assert.Equal(p.FechaRecepcion, leido.Encabezado!.FechaRecepcion);
        Assert.Equal(p.ResponsableNombre, leido.Encabezado.Responsable);
        Assert.Equal(p.Area.Nombre, leido.Encabezado.Area);
        var originales = p.Activas().OrderBy(l => l.Numero).ToList();
        Assert.Equal(originales.Count, leido.Filas.Count);
        for (var i = 0; i < originales.Count; i++)
        {
            var o = originales[i];
            var f = leido.Filas[i];
            var cta = o.Prestador.Cuentas.FirstOrDefault(c => c.Vigente);
            Assert.Equal((o.TipoGasto, o.Job.JobBookNumber, o.Glosa.NombreGlosa, o.ValorUnitarioBruto, o.Cantidad, o.Prestador.RutPlanilla, o.Prestador.NombreCompleto),
                (f.TipoGasto, f.Job, f.Glosa, f.ValorUnitario, f.Cantidad, f.Rut, f.Nombre));
            Assert.Equal(o.ValorTotalBruto, Montos.ValorTotal(f.ValorUnitario!.Value, f.Cantidad!.Value));
            // R-27: cuenta registrada; sin cuenta registrada se conserva la de la planilla.
            Assert.Equal((cta?.TipoCuenta ?? o.CuentaPlanillaTipo, cta?.Cuenta ?? o.CuentaPlanillaNumero, cta?.Banco ?? o.CuentaPlanillaBanco), (f.TipoCuenta, f.Cuenta, f.Banco));
        }

        // Reimportar el archivo exportado produce la misma planilla (nueva versión, mismos montos).
        var r = await e.Produccion.ImportarAsync(new SolicitudImportacion(p.CicloId, p.AreaId, "Costo Directo", p.ResponsableNombre, p.ResponsableEmail,
            TipoArchivoProduccion.Finanzas, "reimportada.xlsx", bytes));
        Assert.True(r.Cargada, string.Join("; ", r.Errores.Select(x => x.Motivo)));
        Assert.Equal(2, r.Planilla!.Version);
        Assert.Equal(originales.Select(l => (l.Numero, l.ValorTotalBruto, l.Cantidad)),
            r.Planilla.Activas().OrderBy(l => l.Numero).Select(l => (l.Numero, l.ValorTotalBruto, l.Cantidad)));
    }

    [Fact]
    public async Task PlanillaFinanzas_SeDetectaAunqueSeElijaExportacion()
    {
        var (e, p, _) = await DataProcessingAsync();
        using var _e = e;
        var bytes = await e.Excel.ExportarAsync(p);
        var r = await e.Produccion.ImportarAsync(new SolicitudImportacion(p.CicloId, p.AreaId, "Costo Directo", p.ResponsableNombre, p.ResponsableEmail,
            TipoArchivoProduccion.Exportacion, "Planilla honorarios OCTUBRE_2026 - DATA PROCESSING v1.xlsx", bytes));
        Assert.True(r.Cargada, string.Join("; ", r.Errores.Take(3).Select(x => $"{x.Fila} {x.Campo} {x.Motivo}")));
        Assert.Equal(219564m, r.Planilla!.Activas().Sum(l => l.ValorTotalBruto));
    }

    [Fact]
    public async Task TotalManualEnH_SeUsa_SeExportaYCuadraConLaBoleta()
    {
        var (e, p, pr) = await DataProcessingAsync();
        using var _e = e;
        var bytes = await e.Excel.ExportarAsync(p);
        using (var wb = new XLWorkbook(new MemoryStream(bytes)))
        {
            var h = wb.Worksheet("Planilla").Cell(16, 8);     // línea 8: Ignacio 45 × $250
            h.Value = 13127;                                    // valor escrito a mano en vez de la fórmula
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            bytes = ms.ToArray();
        }
        var r = await e.Produccion.ImportarAsync(new SolicitudImportacion(p.CicloId, p.AreaId, "Costo Directo", p.ResponsableNombre, p.ResponsableEmail,
            TipoArchivoProduccion.Finanzas, "manual.xlsx", bytes));
        Assert.True(r.Cargada);
        Assert.Contains(r.Avisos, a => a.Contains("Fila 16") && a.Contains("Se usa el valor de H"));
        var pl = await e.RecargarAsync(r.Planilla!);
        Assert.Equal(13127m, pl.Lineas.Single(l => l.Numero == 8).ValorTotalBruto);
        Assert.Equal(221441m, pl.Activas().Sum(l => l.ValorTotalBruto));

        // La boleta por la suma de H (130,12 × 250 = 32.530 + 13.127) cuadra.
        var ign = pr["ign"];
        var sub = await e.SubirAsync(pl, ign, "77", 45657m);
        Assert.True(sub.Conciliacion.Cuadra, string.Join(" ", sub.Conciliacion.Problemas));
        pl = await e.RecargarAsync(pl);
        var c = Flujo.Revisar(pl, ign.Id);
        Assert.Equal(Chequeo.Ok, c.Operativa);
        Assert.Contains("línea 8", c.DetalleOperativa);

        // La exportación conserva el valor manual (sin reemplazarlo por la fórmula).
        using var wb2 = new XLWorkbook(new MemoryStream(await e.Excel.ExportarAsync(pl)));
        Assert.False(wb2.Worksheet("Planilla").Cell(16, 8).HasFormula);
        Assert.Equal(13127d, wb2.Worksheet("Planilla").Cell(16, 8).Value.GetNumber());
        Assert.Equal(221441d, wb2.Worksheet("Planilla").Cell("B3").Value.GetNumber());
    }

    [Fact]
    public async Task R21_AvisoAlGenerarsePago()
    {
        using var e = new Entorno();
        var val = e.Prestador("Valentina Muñoz Soto", "17345120-2", "VISTA", "1712343456", "BANEFE", email: "valentina@correo-ficticio.cl");
        await e.PlanillaAsync("FACE TO FACE", new Fila(val, "260043100104", E, 7200, 25));
        var c = await e.Db.Correos.SingleAsync(x => x.Para == "valentina@correo-ficticio.cl");
        Assert.Contains("$180.000", c.Cuerpo);
        Assert.Contains("$152.550", c.Cuerpo);
    }
}
