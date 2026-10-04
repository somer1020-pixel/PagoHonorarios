using System.Diagnostics;
using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Carga;

/// <summary>
/// Llena una base de PRUEBA con un volumen parecido al productivo: prestadores con cuenta validada, historia de ciclos
/// cerrados (planillas, boletas y transferencias), el ciclo abierto con planillas por área y usuarios del portal e internos.
/// Inserta directo con EF (rápido); los flujos reales (importar, subir boletas, revisar) los ejercita el ejecutor por HTTP.
/// NUNCA contra la base productiva: borra y recrea los datos de prueba que genera.
/// </summary>
public sealed class Generador(string conexion, int prestadores, int mesesHistoria, int lineasPorPlanilla, int areasExtra)
{
    private static readonly string[] Nombres = ["Camila", "Matías", "Valentina", "Benjamín", "Francisca", "Tomás", "Catalina", "Joaquín", "Javiera", "Vicente", "Antonia", "Martín", "Isidora", "Agustín", "Fernanda", "Diego"];
    private static readonly string[] Apellidos = ["González", "Muñoz", "Rojas", "Díaz", "Pérez", "Soto", "Contreras", "Silva", "Martínez", "Sepúlveda", "Morales", "Rodríguez", "López", "Fuentes", "Hernández", "Torres"];
    private static readonly string[] PerfilesExtra = [Roles.CEX, Roles.Public, Roles.BHT, Roles.MSU, Roles.AUM];
    private readonly Random _r = new(20261004);

    public async Task<Escenario> EjecutarAsync(DateOnly periodoAbierto)
    {
        var reloj = Stopwatch.StartNew();
        var usuario = new UsuarioFijo("Generador de carga");
        var opciones = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(conexion, s => s.CommandTimeout(600)).Options;
        await using var db = new AppDbContext(opciones, usuario, TimeProvider.System);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        if (!await db.Areas.AnyAsync()) throw new InvalidOperationException("La base no tiene catálogos: ejecuta antes database/BD_PagoIpsos.sql o inicia la aplicación una vez.");
        if (await db.Prestadores.AnyAsync()) throw new InvalidOperationException("La base ya tiene prestadores: el generador solo se usa sobre una base de prueba vacía (recién creada con BD_PagoIpsos.sql).");

        var escenario = new Escenario { Contrasena = $"Carga{_r.Next(100000, 999999)}a", Ciclo = Formato.CodigoCiclo(periodoAbierto) };
        var par = await db.Parametros.FirstAsync();
        var tasa = 0.1525m;

        // Catálogo: áreas extra de los perfiles operativos, Jobs.
        for (var i = 0; i < areasExtra; i++)
            db.Areas.Add(new Area { Nombre = $"{PerfilesExtra[i % PerfilesExtra.Length]} Área {i + 1}", CodigoArea = $"9{i:000}", Perfil = PerfilesExtra[i % PerfilesExtra.Length] });
        await db.SaveChangesAsync();
        var areas = await db.Areas.OrderBy(a => a.Id).ToListAsync();
        var glosas = await db.Glosas.ToListAsync();
        var jobs = Enumerable.Range(0, 300).Select(i => new Job { JobBookNumber = $"2600{i:00000000}", Nombre = $"Estudio de carga {i}", AreaSugeridaId = areas[i % areas.Count].Id, ValorUnitarioSugerido = 5000 + i % 20 * 500 }).ToList();
        db.Jobs.AddRange(jobs);
        await db.SaveChangesAsync();
        escenario.Jobs = jobs.Select(j => j.JobBookNumber).ToList();
        escenario.Glosas = glosas.Select(g => g.NombreGlosa).ToList();
        Paso($"catálogos: {areas.Count} áreas, {jobs.Count} jobs", reloj);

        // Prestadores con cuenta validada.
        var lista = new List<Prestador>();
        var ruts = new HashSet<int>();
        while (lista.Count < prestadores)
        {
            var rut = _r.Next(10_000_000, 26_000_000);
            if (!ruts.Add(rut)) continue;
            var p = new Prestador
            {
                Rut = rut, Dv = RutHelper.CalcularDv(rut), NombreCompleto = $"{Nombres[_r.Next(Nombres.Length)]} {Apellidos[_r.Next(Apellidos.Length)]} {Apellidos[_r.Next(Apellidos.Length)]}",
                Email = $"p{rut}@correo-ficticio.cl"
            };
            var cuenta = $"{_r.Next(10_000_000, 99_999_999)}{_r.Next(10, 99)}";
            p.Cuentas.Add(new CuentaBancaria { Banco = "BANCO DE CHILE", TipoCuenta = "VISTA", Cuenta = cuenta, CuentaNormalizada = CuentaReglas.Normalizar(cuenta), Estado = CuentaEstado.Validada, Vigente = true, Origen = CuentaOrigen.CargaInicial, ValidadaPor = "Generador", ValidadaEn = DateTime.UtcNow });
            lista.Add(p);
        }
        foreach (var lote in lista.Chunk(1000)) { db.Prestadores.AddRange(lote); await db.SaveChangesAsync(); }
        Paso($"prestadores: {lista.Count} con cuenta validada", reloj);

        // Historia: ciclos cerrados con todo pagado.
        for (var m = mesesHistoria; m >= 1; m--)
        {
            var periodo = periodoAbierto.AddMonths(-m);
            var c = new Ciclo { Periodo = periodo, Codigo = Formato.CodigoCiclo(periodo), Estado = CicloEstado.Cerrado, FechaPagoProgramada = periodo.AddMonths(1).AddDays(par.DiaPago - 1), CerradoEn = periodo.AddMonths(1).AddDays(12).ToDateTime(TimeOnly.MinValue), CerradoPor = "Generador" };
            db.Ciclos.Add(c);
            await db.SaveChangesAsync();
            await PlanillasAsync(db, c, areas, jobs, glosas, lista, tasa, cerrado: true, escenario);
            escenario.CiclosCerrados.Add(c.Id);
            Paso($"ciclo {c.Codigo} cerrado", reloj);
        }

        // Ciclo abierto.
        var abierto = new Ciclo { Periodo = periodoAbierto, Codigo = escenario.Ciclo, FechaPagoProgramada = periodoAbierto.AddMonths(1).AddDays(par.DiaPago - 1) };
        db.Ciclos.Add(abierto);
        await db.SaveChangesAsync();
        await PlanillasAsync(db, abierto, areas, jobs, glosas, lista, tasa, cerrado: false, escenario);
        Paso($"ciclo {abierto.Codigo} abierto", reloj);

        // Usuarios: un operativo por área (con esa área), dos de Finanzas y los prestadores del ciclo abierto en el portal.
        var hash = new PasswordHasher<Usuario>().HashPassword(null!, escenario.Contrasena);
        var roles = await db.Roles.ToDictionaryAsync(r => r.Name!, r => r.Id);
        Usuario Nuevo(string nombreUsuario, string? email, string nombre, string rol)
        {
            var u = new Usuario
            {
                UserName = nombreUsuario, NormalizedUserName = nombreUsuario.ToUpperInvariant(), Email = email, NormalizedEmail = email?.ToUpperInvariant(),
                EmailConfirmed = email is not null, NombreCompleto = nombre, PasswordHash = hash, SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true
            };
            db.Users.Add(u);
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = u.Id, RoleId = roles[rol] });
            return u;
        }
        var planillasAbiertas = await db.Planillas.Where(p => p.CicloId == abierto.Id).Select(p => new { p.Id, p.AreaId }).ToListAsync();
        foreach (var a in areas)
        {
            var email = $"ops{a.Id}@carga.cl";
            var u = Nuevo(email, email, $"Operaciones {a.Nombre}", a.Perfil);
            db.UsuarioAreas.Add(new UsuarioArea { UsuarioId = u.Id, AreaId = a.Id });
            escenario.Operaciones.Add(new(email, a.Nombre, a.Id, planillasAbiertas.Where(p => p.AreaId == a.Id).Select(p => p.Id).ToList()));
        }
        foreach (var i in new[] { 1, 2 })
        {
            Nuevo($"finanzas{i}@carga.cl", $"finanzas{i}@carga.cl", $"Finanzas {i}", Roles.Finanzas);
            escenario.Finanzas.Add(new($"finanzas{i}@carga.cl", "", 0, []));
        }
        var enPortal = escenario.Prestadores.Select(x => x.Rut).ToHashSet();
        foreach (var p in lista.Where(p => enPortal.Contains(p.RutPlanilla)))
        {
            var u = Nuevo(p.RutPlanilla, p.Email, p.NombreCompleto, Roles.Prestador);
            u.PrestadorId = p.Id;
            p.UsuarioId = u.Id;
            p.PortalInvitadoEn = p.PortalActivadoEn = DateTime.UtcNow;
            db.Entry(p).State = EntityState.Modified;
        }
        await db.SaveChangesAsync();
        Paso($"usuarios: {escenario.Operaciones.Count} operativos, {escenario.Finanzas.Count} de Finanzas, {enPortal.Count} prestadores en el portal", reloj);
        return escenario;
    }

    private async Task PlanillasAsync(AppDbContext db, Ciclo c, List<Area> areas, List<Job> jobs, List<Glosa> glosas, List<Prestador> lista,
        decimal tasa, bool cerrado, Escenario escenario)
    {
        var ahora = DateTime.UtcNow;
        foreach (var (a, ia) in areas.Select((a, i) => (a, i)))
        {
            // Algunas áreas con dos planillas en el ciclo (varias planillas por área).
            var cuantas = ia % 4 == 0 ? 2 : 1;
            for (var n = 1; n <= cuantas; n++)
            {
                // En el ciclo abierto: 1 de cada 3 planillas ya está en revisión de Finanzas (con boletas cuadradas).
                var enRevision = !cerrado && (ia + n) % 3 == 0;
                var p = new Planilla
                {
                    CicloId = c.Id, AreaId = a.Id, Numero = n, Nombre = n > 1 ? "Segunda quincena" : null, FechaRecepcion = c.Periodo.AddDays(27),
                    ResponsableNombre = $"Operaciones {a.Nombre}", ResponsableEmail = $"ops{a.Id}@carga.cl",
                    Estado = cerrado ? PlanillaEstado.Cerrada : enRevision ? PlanillaEstado.EnRevision : PlanillaEstado.Borrador
                };
                db.Planillas.Add(p);
                await db.SaveChangesAsync();

                // ~2 filas por prestador (Jobs o glosas distintas).
                var elegidos = lista.OrderBy(_ => _r.Next()).Take(lineasPorPlanilla / 2).ToList();
                var numero = 0;
                var lineas = new List<LineaPago>();
                foreach (var pr in elegidos)
                    for (var k = 0; k < 2; k++)
                    {
                        var vu = 5000m + _r.Next(0, 20) * 500;
                        var q = _r.Next(1, 40);
                        var cuenta = pr.Cuentas[0];
                        lineas.Add(new LineaPago
                        {
                            PlanillaId = p.Id, Numero = ++numero, TipoGasto = "Costo Directo", JobId = jobs[_r.Next(jobs.Count)].Id, GlosaId = glosas[k % glosas.Count].Id,
                            ValorUnitarioBruto = vu, Cantidad = q, ValorTotalBruto = Montos.ValorTotal(vu, q), PrestadorId = pr.Id, NombrePlanilla = pr.NombreCompleto,
                            CuentaPlanillaTipo = cuenta.TipoCuenta, CuentaPlanillaNumero = cuenta.Cuenta, CuentaPlanillaBanco = cuenta.Banco, CuentaBancariaId = cuenta.Id,
                            ResultadoCuenta = ResultadoCuenta.Coincide,
                            Estado = cerrado ? LineaEstado.Pagada : enRevision ? LineaEstado.Lista : LineaEstado.PendienteBoleta
                        });
                    }
                db.LineasPago.AddRange(lineas);
                await db.SaveChangesAsync();

                if (cerrado || enRevision)
                {
                    var boletas = new List<(BoletaHonorarios B, Prestador P, decimal Liquido)>();
                    foreach (var g in lineas.GroupBy(l => l.PrestadorId))
                    {
                        var pr = elegidos.First(x => x.Id == g.Key);
                        var (bruto, ret, liq) = Montos.PorBoleta(g.Select(l => l.ValorTotalBruto), tasa);
                        var b = new BoletaHonorarios
                        {
                            PlanillaId = p.Id, PrestadorId = pr.Id, Canal = BoletaCanal.Portal, SubidaPor = pr.NombreCompleto, SubidaEn = ahora,
                            Estado = BoletaEstado.Confirmada, NumeroBoleta = $"{c.Id}{p.Id}{pr.Id}", RutEmisor = pr.Rut, RutEmisorDv = pr.Dv, NombreEmisor = pr.NombreCompleto.ToUpperInvariant(),
                            RutReceptor = "76007075-0", FechaEmision = c.Periodo.AddDays(28), MontoBruto = bruto, MontoRetencion = ret, MontoLiquido = liq,
                            RutaPdf = "", HashPdf = Guid.NewGuid().ToString("N"), Confianza = Confianza.Alta, Cuadra = true
                        };
                        boletas.Add((b, pr, liq));
                    }
                    db.Boletas.AddRange(boletas.Select(x => x.B));
                    await db.SaveChangesAsync();
                    if (cerrado)
                    {
                        db.Transferencias.AddRange(boletas.Select((x, i) => new Transferencia
                        {
                            PlanillaId = p.Id, PrestadorId = x.P.Id, BoletaId = x.B.Id, CuentaBancariaId = x.P.Cuentas[0].Id, Fecha = c.FechaPagoProgramada,
                            NumeroOperacion = $"OP-{c.Id}-{p.Id}-{i}", MontoLiquido = x.Liquido, Comprobante = ""
                        }));
                        db.Auditorias.AddRange(boletas.Select(x => new Auditoria { Fecha = ahora, Usuario = "Generador", Entidad = nameof(Transferencia), EntidadId = x.P.RutPlanilla, Accion = "Registrar transferencia", Detalle = $"{c.Codigo} · {Formato.Clp(x.Liquido)}" }));
                        await db.SaveChangesAsync();
                    }
                    else escenario.PlanillasEnRevision.Add(p.Id);
                }
                else
                    foreach (var g in lineas.GroupBy(l => l.PrestadorId))
                    {
                        var pr = elegidos.First(x => x.Id == g.Key);
                        escenario.Prestadores.Add(new(pr.RutPlanilla, pr.NombreCompleto, p.Id, Montos.PorBoleta(g.Select(l => l.ValorTotalBruto), tasa).Bruto));
                    }
                db.ChangeTracker.Clear();
            }
        }
    }

    private static void Paso(string texto, Stopwatch reloj) => Console.WriteLine($"[{reloj.Elapsed:mm\\:ss}] {texto}");
}
