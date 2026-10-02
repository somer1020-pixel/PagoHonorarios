using System.Text;
using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Data;

/// <summary>
/// Datos de demo (solo Development), ficticios, de la sección 11 de la especificación. El flujo se ejecuta con los servicios
/// reales (importación, boletas, revisión, pago y cierre); solo el envío de FACE TO FACE sin la boleta de Francisca se fuerza
/// para reproducir el escenario del mockup. Nunca datos personales reales.
/// </summary>
public class SemillaDemo(IServiceProvider sp, AppDbContext db, UserManager<Usuario> users, ILogger<SemillaDemo> log)
{
    public const string Contrasena = "Demo2026!";
    public static readonly DateOnly PeriodoDemo = new(2026, 10, 1);

    private sealed record P(string Clave, string Nombre, string Rut, string? Tipo, string? Cuenta, string? Banco, bool Pendiente = false, string Portal = "Activado");

    private static readonly P[] Prestadores =
    [
        new("cam", "Camila Rojas Fuentes", "15234871-1", "VISTA", "1734567890", "BANEFE"),
        new("mat", "Matías González Pérez", "16987452-2", "CORRIENTE", "0012345678", "CHILE"),
        new("val", "Valentina Muñoz Soto", "17345120-2", "VISTA", "1712343456", "BANEFE"),
        new("fra", "Francisca Silva Morales", "13987654-7", "CORRIENTE", "0068123456", "SANTANDER"),
        new("agu", "Agustín Flores Medina", "17654321-3", "RUT", "17654321", "ESTADO", Pendiente: true),
        new("tom", "Tomás Herrera Lagos", "18456123-9", "RUT", "18456123", "ESTADO"),
        new("cat", "Catalina Reyes Vidal", "16543210-K", "CORRIENTE", "77123456", "BCI"),
        new("ben", "Benjamín Castro Núñez", "19876543-0", "VISTA", "1045678912", "MERCADO PAGO", Portal: "Sin invitar"),
        new("mar", "Martina Vega Rojas", "18765432-7", "VISTA", "1100223344", "Tenpo"),
        new("luc", "Lucas Ortiz Díaz", "15678901-1", "RUT", "15678901", "ESTADO"),
        new("ant", "Antonia Cruz Peña", "20123456-5", "VISTA", "1022334455", "MERCADO PAGO"),
        new("pau", "Paula Navarro", "16234567-2", "CORRIENTE", "0071234506", "SANTANDER"),
        new("die", "Diego Morales", "15876543-8", "RUT", "15876543", "ESTADO"),
        new("jav", "Javiera Soto", "19345678-2", null, null, null, Portal: "Invitado"),
        new("rod", "Rodrigo Pinto", "17456789-1", "CORRIENTE", "0045678123", "CHILE"),
        new("fer", "Fernanda Lagos", "18234567-9", "RUT", "18234567", "ESTADO"),
        new("ign", "Ignacio Vera", "20456789-1", "VISTA", "1798765432", "BANEFE"),
        new("sof", "Sofía Campos", "14567890-0", "CORRIENTE", "77889900", "BCI")
    ];

    private const string E = "Honorarios entrevistadores", S = "Honorarios supervisión", DPG = "Honorario codificación externa";
    private const string GlosaMs = "TODO(diseño): glosa MYSTERY SHOPPING";

    private T S_<T>() where T : notnull => sp.GetRequiredService<T>();
    private void Como(string nombre) => ((HttpUsuarioActual)sp.GetRequiredService<IUsuarioActual>()).Suplantado = nombre;

    public async Task EjecutarAsync()
    {
        if (await db.Prestadores.AnyAsync()) return;
        log.LogInformation("Cargando datos de demo (ficticios)…");
        Como("Semilla demo");

        // Usuarios internos ficticios.
        foreach (var (mail, nombre, rol) in new[]
                 {
                     ("andres.paredes@ejemplo.cl", "Andrés Paredes", Roles.Operaciones), ("daniela.fuentes@ejemplo.cl", "Daniela Fuentes", Roles.Operaciones),
                     ("felipe.araya@ejemplo.cl", "Felipe Araya", Roles.Operaciones), ("carolina.diaz@ejemplo.cl", "Carolina Díaz", Roles.Finanzas),
                     ("admin@ejemplo.cl", "Admin Demo", Roles.Admin)
                 })
        {
            var u = new Usuario { UserName = mail, Email = mail, EmailConfirmed = true, NombreCompleto = nombre, LockoutEnabled = true };
            await users.CreateAsync(u, Contrasena);
            await users.AddToRoleAsync(u, rol);
        }

        // Glosa de MYSTERY SHOPPING pendiente de definición y Jobs.
        db.Glosas.Add(new Glosa { NombreGlosa = GlosaMs, Item = "TODO", CuentaContable = "TODO" });
        var areas = await db.Areas.ToDictionaryAsync(a => a.Nombre);
        // Cada usuario de Operaciones ve solo las planillas de sus áreas.
        foreach (var (mail, area) in new[] { ("andres.paredes@ejemplo.cl", "FACE TO FACE"), ("daniela.fuentes@ejemplo.cl", "MYSTERY SHOPPING"),
                     ("felipe.araya@ejemplo.cl", "DATA PROCESSING"), ("felipe.araya@ejemplo.cl", "Operations CATI") })
            if (areas.TryGetValue(area, out var a) && await users.FindByEmailAsync(mail) is { } u)
                db.UsuarioAreas.Add(new UsuarioArea { UsuarioId = u.Id, AreaId = a.Id });
        foreach (var (id, n, a, v) in new[]
                 {
                     ("260041200105", "Hábitos de Consumo Hogar 2026", "FACE TO FACE", 6500m), ("260043100104", "Evaluación Transporte Urbano", "FACE TO FACE", 7200m),
                     ("260044200110", "Panel Salud Regiones", "FACE TO FACE", 7450m), ("260045100301", "Codificación Abiertas Tracking", "DATA PROCESSING", 250m),
                     ("260047300210", "Mystery Shopper Retail Q4", "MYSTERY SHOPPING", 14000m)
                 })
            db.Jobs.Add(new Job { JobBookNumber = id, Nombre = n, AreaSugeridaId = areas[a].Id, ValorUnitarioSugerido = v });

        // Prestadores, cuentas registradas (carga inicial) y acceso al portal.
        var ahora = DateTime.UtcNow;
        var pr = new Dictionary<string, Prestador>();
        foreach (var x in Prestadores)
        {
            RutHelper.TryParse(x.Rut, out var c, out var dv);
            var p = new Prestador
            {
                Rut = c, Dv = dv, NombreCompleto = x.Nombre,
                Email = x.Clave == "fra" ? null : Correo(x.Nombre), Telefono = x.Clave == "fra" ? "+56 9 5555 0101" : null
            };
            if (x.Cuenta is not null)
                p.Cuentas.Add(new CuentaBancaria
                {
                    Banco = x.Banco!, TipoCuenta = x.Tipo!, Cuenta = x.Cuenta, CuentaNormalizada = CuentaReglas.Normalizar(x.Cuenta),
                    Estado = x.Pendiente ? CuentaEstado.Inactiva : CuentaEstado.Validada, Vigente = !x.Pendiente,
                    Origen = CuentaOrigen.CargaInicial, OrigenDetalle = "Planillas 2026 (demo)", RegistradaPor = "Carga inicial", RegistradaEn = ahora.AddMonths(-3),
                    ValidadaPor = "Carga inicial", ValidadaEn = ahora.AddMonths(-3)
                });
            if (x.Pendiente)
                p.Cuentas.Insert(0, new CuentaBancaria
                {
                    Banco = "Tenpo", TipoCuenta = "VISTA", Cuenta = "1100987654", CuentaNormalizada = "1100987654", Estado = CuentaEstado.Inactiva,
                    Origen = CuentaOrigen.CargaInicial, OrigenDetalle = "Planillas 2026 (demo)", RegistradaPor = "Carga inicial", RegistradaEn = ahora.AddMonths(-3),
                    ReemplazadaEn = ahora.AddDays(-4)
                });
            db.Prestadores.Add(p);
            pr[x.Clave] = p;
        }
        await db.SaveChangesAsync();
        // Agustín: cambio de cuenta registrado por Operaciones, pendiente de validación (R-23).
        var agu = pr["agu"];
        var cAgu = agu.Cuentas.First(c => c.Banco == "ESTADO");
        cAgu.Estado = CuentaEstado.PendienteValidacion; cAgu.Vigente = true; cAgu.Origen = CuentaOrigen.Registro;
        cAgu.OrigenDetalle = "Cambio informado por el prestador"; cAgu.RegistradaPor = "Andrés Paredes"; cAgu.RegistradaEn = ahora.AddDays(-4);
        cAgu.ValidadaPor = null; cAgu.ValidadaEn = null;

        var prestadores = S_<PrestadoresService>();
        foreach (var x in Prestadores.Where(x => x.Portal != "Sin invitar"))
        {
            var p = pr[x.Clave];
            var u = await prestadores.AsegurarUsuarioAsync(p);
            p.PortalInvitadoEn = ahora.AddMonths(-1);
            if (x.Portal == "Activado")
            {
                await users.AddPasswordAsync(u, Contrasena);
                p.PortalActivadoEn = ahora.AddMonths(-1).AddDays(1);
            }
        }
        await db.SaveChangesAsync();

        var tasa = 0.1525m;
        var ciclos = S_<CicloService>();
        // Ciclos anteriores cerrados (Valentina), con el flujo completo y ZIP de respaldo.
        await CicloCerradoAsync(new DateOnly(2026, 8, 1), 21, "76", new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 5), "OP-0047120", pr, tasa);
        await CicloCerradoAsync(new DateOnly(2026, 9, 1), 23, "81", new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 6), "OP-0047981", pr, tasa);

        var oct = await ciclos.ObtenerOCrearAsync(PeriodoDemo);
        await db.SaveChangesAsync();
        var fechaBoleta = Clamp(ciclos.Hoy, PeriodoDemo, Conciliacion.FechaLimite(PeriodoDemo));

        // FACE TO FACE: 10 líneas, 8 prestadores, 315 unidades, $2.161.600.
        Como("Andrés Paredes");
        var f2f = await ImportarAsync(oct, "FACE TO FACE", "Andrés Paredes", "andres.paredes@ejemplo.cl", "produccion_f2f_oct2026.csv",
        [
            ("cam", "260041200105", E, 6500, 42), ("cam", "260043100104", E, 7200, 10), ("mat", "260041200105", E, 6500, 38),
            ("mat", "260041200105", S, 45000, 1), ("val", "260043100104", E, 7200, 25), ("fra", "260041200105", E, 6500, 51),
            ("agu", "260041200105", E, 6500, 40), ("tom", "260041200105", E, 6500, 45), ("cat", "260043100104", E, 7200, 35),
            ("ben", "260044200110", E, 7450, 28)
        ], pr, null);
        var boletas = S_<BoletaService>();
        foreach (var (k, n, monto) in new[] { ("cam", "1245", 345000m), ("mat", "3318", 292000m), ("val", "87", 182000m), ("tom", "2207", 292500m), ("cat", "640", 252000m), ("ben", "1093", 208600m) })
            await SubirAsync(boletas, f2f, pr[k], n, monto, fechaBoleta, tasa, BoletaCanal.Portal, null);
        await SubirAsync(boletas, f2f, pr["agu"], "512", 260000m, fechaBoleta, tasa, BoletaCanal.Operaciones,
            "El prestador entregó el PDF por correo a Operaciones; sin acceso al portal esa semana.");
        foreach (var b in await db.Boletas.Where(b => b.PlanillaId == f2f.Id && (b.NumeroBoleta == "1245" || b.NumeroBoleta == "3318" || b.NumeroBoleta == "2207" || b.NumeroBoleta == "640")).ToListAsync())
            await boletas.ConfirmarAsync(b.Id);

        // Escenario del mockup: v2 enviada a Finanzas (forzado: Francisca aún sin boleta).
        var pF2f = (await ciclos.PlanillaCompletaAsync(f2f.Id))!;
        pF2f.Version = 2;
        pF2f.Estado = PlanillaEstado.EnRevision;
        await S_<PlanillaService>().ArchivarVersionAsync(pF2f);
        S_<Auditor>().Registrar(nameof(Planilla), pF2f.Id, "Reenviar a Finanzas", "FACE TO FACE v2 (demo)");
        await db.SaveChangesAsync();

        var rev = S_<RevisionService>();
        Como("Carolina Díaz");
        int L(int n) => pF2f.Lineas.First(l => l.Numero == n).Id;
        var o5 = await rev.ObservarAsync(L(5), ObservacionTipo.DiferenciaMontos, "Monto boleta", "Boleta N° 87 por $182.000; la suma de sus filas es $180.000.");
        var o6 = await rev.ObservarAsync(L(6), ObservacionTipo.FaltaBoleta, "N° boleta", "No se ha recibido boleta para esta planilla.");
        var o4 = await rev.ObservarAsync(L(4), ObservacionTipo.InconsistenciaNumerica, "Valor total bruto", "Valor total $54.000 no corresponde a 1 × $45.000.");
        var o2 = await rev.ObservarAsync(L(2), ObservacionTipo.ErrorDigitacion, "Nombres", "Apellido materno escrito «Fuente».");
        Como("Andrés Paredes");
        await rev.CorregirAsync(o4.Id, "Corregido a $45.000 con la fórmula ROUND(F*G,0).");
        await rev.CorregirAsync(o2.Id, "Corregido a «Camila Rojas Fuentes».");
        Como("Carolina Díaz");
        await rev.AceptarAsync(o2.Id);
        await rev.DevolverAsync(f2f.Id);
        _ = (o5, o6);

        // MYSTERY SHOPPING: 4 líneas, $532.000 (filas ilustrativas; glosa TODO(diseño)).
        Como("Daniela Fuentes");
        var ms = await ImportarAsync(oct, "MYSTERY SHOPPING", "Daniela Fuentes", "daniela.fuentes@ejemplo.cl", "produccion_ms_oct2026.csv",
        [
            ("mar", "260047300210", GlosaMs, 14000, 14), ("luc", "260047300210", GlosaMs, 14000, 12),
            ("ant", "260047300210", GlosaMs, 14000, 8), ("ant", "260047300210", GlosaMs, 14000, 4)
        ], pr, null);
        foreach (var (k, n, monto) in new[] { ("mar", "301", 196000m), ("luc", "118", 168000m), ("ant", "45", 168000m) })
            await SubirAsync(boletas, ms, pr[k], n, monto, fechaBoleta, tasa, BoletaCanal.Portal, null);
        await S_<PlanillaService>().EnviarAsync(ms.Id);

        // DATA PROCESSING v1: 7 prestadores, 9 filas, $219.564, 5 alertas de cuenta.
        Como("Felipe Araya");
        var dpCuentas = new Dictionary<string, (string, string, string)>
        {
            ["pau"] = ("CORRIENTE", "0071234560", "SANTANDER"), ["die"] = ("RUT", "15876543", "ESTADO"), ["jav"] = ("VISTA", "1712345678", "BANEFE"),
            ["rod"] = ("CORRIENTE", "0012345678", "CHILE"), ["fer"] = ("RUT", "18234560", "ESTADO"), ["ign"] = ("VISTA", "1798765432", "BANEFE"),
            ["sof"] = ("VISTA", "1029384756", "MERCADO PAGO")
        };
        var dp = await ImportarAsync(oct, "DATA PROCESSING", "Felipe Araya", "felipe.araya@ejemplo.cl", "Planilla DATA PROCESSING v1.csv",
        [
            ("pau", "260045100301", DPG, 250, 85.28m), ("pau", "260045100301", DPG, 250, 102.4m), ("die", "260045100301", DPG, 250, 120.5m),
            ("jav", "260045100301", DPG, 250, 96.75m), ("rod", "260045100301", DPG, 250, 110), ("fer", "260045100301", DPG, 250, 88.36m),
            ("ign", "260045100301", DPG, 250, 130.12m), ("ign", "260045100301", DPG, 250, 45), ("sof", "260045100301", DPG, 250, 99.845m)
        ], pr, dpCuentas);
        await SubirAsync(boletas, dp, pr["die"], "204", 30125m, fechaBoleta, tasa, BoletaCanal.Portal, null);
        await SubirAsync(boletas, dp, pr["ign"], "77", 43780m, fechaBoleta, tasa, BoletaCanal.Portal, null);

        Como("sistema");
        log.LogInformation("Datos de demo cargados.");
    }

    private async Task CicloCerradoAsync(DateOnly periodo, decimal cantidad, string boleta, DateOnly fechaBoleta, DateOnly fechaPago, string op,
        Dictionary<string, Prestador> pr, decimal tasa)
    {
        var ciclo = await S_<CicloService>().ObtenerOCrearAsync(periodo);
        await db.SaveChangesAsync();
        Como("Andrés Paredes");
        var p = await ImportarAsync(ciclo, "FACE TO FACE", "Andrés Paredes", "andres.paredes@ejemplo.cl", $"produccion_f2f_{ciclo.Codigo}.csv",
            [("val", "260043100104", E, 7200, cantidad)], pr, null);
        var b = await SubirAsync(S_<BoletaService>(), p, pr["val"], boleta, 7200 * cantidad, fechaBoleta, tasa, BoletaCanal.Portal, null);
        await S_<PlanillaService>().EnviarAsync(p.Id);
        Como("Carolina Díaz");
        await S_<RevisionService>().AprobarAsync(p.Id);
        var pagos = S_<PagoService>();
        await pagos.NominaAsync(p.Id);
        var liq = Montos.Liquido(7200 * cantidad, tasa);
        await pagos.RegistrarTransferenciaAsync(p.Id, pr["val"].Id, fechaPago, op, BoletaPdf.Comprobante(op, pr["val"].RutPlanilla, liq, fechaPago), $"{op}.pdf");
        await pagos.CerrarCicloAsync(ciclo.Id);
        _ = b;
    }

    private async Task<Planilla> ImportarAsync(Ciclo ciclo, string area, string resp, string email, string archivo,
        (string K, string Job, string Glosa, decimal Vu, decimal Q)[] filas, Dictionary<string, Prestador> pr,
        Dictionary<string, (string Tipo, string Cuenta, string Banco)>? cuentasPlanilla)
    {
        var sb = new StringBuilder("Rut;Nombre;Job;Glosa;Valor unitario;Cantidad;Tipo cuenta;Cuenta;Banco\n");
        foreach (var f in filas)
        {
            var p = pr[f.K];
            var cta = cuentasPlanilla is not null
                ? cuentasPlanilla[f.K]
                : p.Cuentas.FirstOrDefault(c => c.Vigente) is { } c ? (c.TipoCuenta, c.Cuenta, c.Banco) : ("", "", "");
            // Exportación es-CL: decimales con coma (el punto es separador de miles).
            sb.AppendLine(string.Join(";", p.RutPlanilla, p.NombreCompleto, f.Job, f.Glosa, f.Vu.ToString("0.####", Formato.Cl),
                f.Q.ToString("0.####", Formato.Cl), cta.Item1, cta.Item2, cta.Item3));
        }
        var areaId = (await db.Areas.FirstAsync(a => a.Nombre == area)).Id;
        var r = await S_<ProduccionService>().ImportarAsync(new SolicitudImportacion(ciclo.Id, areaId, "Costo Directo", resp, email,
            TipoArchivoProduccion.Exportacion, archivo, Encoding.UTF8.GetBytes(sb.ToString())));
        if (!r.Cargada) throw new InvalidOperationException("Demo: " + string.Join("; ", r.Errores.Select(e => $"{e.Fila} {e.Campo} {e.Motivo}")));
        return r.Planilla!;
    }

    private async Task<BoletaHonorarios> SubirAsync(BoletaService boletas, Planilla p, Prestador pr, string numero, decimal bruto, DateOnly fecha,
        decimal tasa, BoletaCanal canal, string? motivo)
    {
        var par = await db.Parametros.FirstAsync();
        var ciclo = await db.Ciclos.FirstAsync(c => c.Id == p.CicloId);
        var linea = await db.LineasPago.Include(l => l.Job).Include(l => l.Glosa).FirstAsync(l => l.PlanillaId == p.Id && l.PrestadorId == pr.Id);
        var pdf = BoletaPdf.Generar(pr.NombreCompleto, pr.RutPlanilla, numero, fecha, par.RutEmpresa, "Empresa receptora (demo)",
            $"{linea.Glosa.NombreGlosa} {ciclo.Codigo} Job {linea.Job.JobBookNumber}", bruto, tasa);
        var r = await boletas.SubirAsync(p.Id, pr.Id, pdf, $"boleta_{numero}.pdf", canal, motivo);
        return r.Boleta;
    }

    private static DateOnly Clamp(DateOnly d, DateOnly min, DateOnly max) => d < min ? min : d > max ? max : d;

    private static string Correo(string nombre)
    {
        var partes = LectorBoletaTexto.Normalizar(nombre).ToLowerInvariant().Split(' ');
        return $"{partes[0]}.{partes[1]}@correo-ficticio.cl".Replace("ñ", "n");
    }
}
