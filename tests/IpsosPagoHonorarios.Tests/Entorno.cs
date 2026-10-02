using System.Globalization;
using System.Text;
using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using IpsosPagoHonorarios.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace IpsosPagoHonorarios.Tests;

/// <summary>Servicios reales sobre SQLite en memoria, con reloj controlado (TimeProvider) y datos ficticios.</summary>
public sealed class Entorno : IDisposable
{
    public const string E = "Honorarios entrevistadores", S = "Honorarios supervisión", DPG = "Honorario codificación externa";
    public const string RutEmpresa = "77777777-7";
    public static readonly DateOnly Octubre = new(2026, 10, 1);

    private readonly SqliteConnection _conn;
    public string Carpeta { get; } = Path.Combine(Path.GetTempPath(), "honorarios-test-" + Guid.NewGuid().ToString("N"));
    public FakeTimeProvider Reloj { get; } = new(new DateTimeOffset(2026, 10, 29, 13, 0, 0, TimeSpan.Zero)); // 10:00 en Santiago
    public UsuarioFijo Usuario { get; } = new("Andrés Paredes");
    public AppDbContext Db { get; }
    public Almacenamiento Archivos { get; }
    public Auditor Auditor { get; }
    public Correos Correos { get; }
    public Parametros Parametros { get; }
    public CicloService Ciclos { get; }
    public ExcelPlanilla Excel { get; }
    public PlanillaService Planillas { get; }
    public CuentasService Cuentas { get; }
    public BoletaService Boletas { get; }
    public ProduccionService Produccion { get; }
    public RevisionService Revision { get; }
    public PlazosService Plazos { get; }
    public PagoService Pagos { get; }
    public PortalService Portal { get; }

    public Entorno()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options, Usuario, Reloj);
        Db.Database.EnsureCreated();
        Archivos = new Almacenamiento(Carpeta);
        Auditor = new Auditor(Db, Usuario, Reloj);
        Correos = new Correos(Db, Reloj, NullLogger<Correos>.Instance);
        Parametros = new Parametros(Db);
        Ciclos = new CicloService(Db, Parametros, Auditor, Reloj);
        Excel = new ExcelPlanilla(Db, Options.Create(new OpcionesPlantillas()), new HostingEnvironment { ContentRootPath = Carpeta });
        Planillas = new PlanillaService(Db, Ciclos, Excel, Archivos, Auditor, Correos);
        Cuentas = new CuentasService(Db, Ciclos, Auditor, Correos, Usuario);
        Boletas = new BoletaService(Db, Ciclos, Parametros, Archivos, Auditor, Correos, Usuario);
        Produccion = new ProduccionService(Db, Ciclos, Parametros, Cuentas, Boletas, Archivos, Auditor, Correos);
        Revision = new RevisionService(Db, Ciclos, Parametros, Auditor, Correos, Usuario);
        Plazos = new PlazosService(Db, Ciclos, Planillas, Auditor, Correos);
        Pagos = new PagoService(Db, Ciclos, Parametros, Archivos, Auditor, Correos, Usuario);
        Portal = new PortalService(Db, Parametros);
        Sembrar();
    }

    private void Sembrar()
    {
        foreach (var (n, c) in new[] { ("FACE TO FACE", "21183"), ("MYSTERY SHOPPING", "21187"), ("DATA PROCESSING", "21184"), ("Operations CATI", "21161") })
            Db.Areas.Add(new Area { Nombre = n, CodigoArea = c });
        foreach (var (n, i) in new[] { (E, "1310"), (S, "1330"), (DPG, "2830") })
            Db.Glosas.Add(new Glosa { NombreGlosa = n, Item = i, CuentaContable = "602101" });
        foreach (var (n, c) in new[] { ("ESTADO", "12"), ("CHILE", "1"), ("BANEFE", "37"), ("BCI", "16"), ("SANTANDER", "37"), ("MERCADO PAGO", "874"), ("Tenpo", "730") })
            Db.Bancos.Add(new Banco { Nombre = n, CodigoBanco = c });
        foreach (var t in new[] { "CORRIENTE", "VISTA", "RUT", "AHORRO", "CHEQUERA ELECTRONICA", "DEBITO" })
            Db.TiposCuenta.Add(new TipoCuenta { Nombre = t, Codigo = "" });
        Db.TiposGasto.AddRange(new TipoGasto { Nombre = "Costo Directo" }, new TipoGasto { Nombre = "Payroll" });
        Db.Parametros.Add(new Parametro { RutEmpresa = RutEmpresa });
        Db.TasasRetencion.Add(new TasaRetencion { Anio = 2026, Tasa = 0.1525m });
        Db.SaveChanges();
    }

    public void Como(string nombre) => Usuario.Nombre = nombre;

    public Prestador Prestador(string nombre, string rut, string? tipo = null, string? cuenta = null, string? banco = null,
        CuentaEstado estado = CuentaEstado.Validada, string? email = null)
    {
        RutHelper.TryParse(rut, out var c, out var dv);
        var p = new Prestador { NombreCompleto = nombre, Rut = c, Dv = dv, Email = email ?? $"p{c}@correo-ficticio.cl" };
        if (cuenta is not null)
            p.Cuentas.Add(new CuentaBancaria
            {
                Banco = banco!, TipoCuenta = tipo!, Cuenta = cuenta, CuentaNormalizada = CuentaReglas.Normalizar(cuenta), Estado = estado, Vigente = true,
                Origen = CuentaOrigen.CargaInicial
            });
        Db.Prestadores.Add(p);
        Db.SaveChanges();
        return p;
    }

    public async Task<Ciclo> CicloAsync(DateOnly? periodo = null)
    {
        var c = await Ciclos.ObtenerOCrearAsync(periodo ?? Octubre);
        await Db.SaveChangesAsync();
        return c;
    }

    public sealed record Fila(Prestador P, string Job, string Glosa, decimal Vu, decimal Q, (string Tipo, string Cuenta, string Banco)? Cuenta = null, bool SinCuenta = false);

    public static byte[] Csv(IEnumerable<Fila> filas)
    {
        var sb = new StringBuilder("Rut;Nombre;Job;Nombre Job;Glosa;Valor unitario;Cantidad;Tipo cuenta;Cuenta;Banco\n");
        foreach (var f in filas)
        {
            var c = f.SinCuenta ? ("", "", "") : f.Cuenta ?? (f.P.Cuentas.FirstOrDefault(x => x.Vigente) is { } v ? (v.TipoCuenta, v.Cuenta, v.Banco) : ("", "", ""));
            sb.AppendLine(string.Join(";", f.P.RutPlanilla, f.P.NombreCompleto, f.Job, "Job " + f.Job, f.Glosa,
                f.Vu.ToString("0.####", Formato.Cl), f.Q.ToString("0.####", Formato.Cl), c.Item1, c.Item2, c.Item3));
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<ResultadoImportacion> ImportarAsync(string area, params Fila[] filas)
    {
        var ciclo = await CicloAsync();
        var areaId = (await Db.Areas.FirstAsync(a => a.Nombre == area)).Id;
        return await Produccion.ImportarAsync(new SolicitudImportacion(ciclo.Id, areaId, "Costo Directo", "Andrés Paredes", "andres.paredes@ejemplo.cl",
            TipoArchivoProduccion.Exportacion, "produccion.csv", Csv(filas)));
    }

    public async Task<Planilla> PlanillaAsync(string area, params Fila[] filas)
    {
        var r = await ImportarAsync(area, filas);
        Assert.True(r.Cargada, string.Join("; ", r.Errores.Select(e => $"{e.Fila} {e.Campo} {e.Motivo}")));
        return r.Planilla!;
    }

    public static byte[] Pdf(Prestador p, string numero, decimal bruto, DateOnly? fecha = null, string receptor = RutEmpresa, string? rutEmisor = null) =>
        BoletaPdf.Generar(p.NombreCompleto, rutEmisor ?? p.RutPlanilla, numero, fecha ?? new DateOnly(2026, 10, 30), receptor, "Empresa ficticia", "Honorarios OCT-2026", bruto, 0.1525m);

    public Task<ResultadoSubida> SubirAsync(Planilla pl, Prestador p, string numero, decimal bruto, BoletaCanal canal = BoletaCanal.Portal, string? motivo = null, DateOnly? fecha = null) =>
        Boletas.SubirAsync(pl.Id, p.Id, Pdf(p, numero, bruto, fecha), $"boleta_{numero}.pdf", canal, motivo);

    public async Task<Planilla> RecargarAsync(Planilla p) => (await Ciclos.PlanillaCompletaAsync(p.Id))!;

    public void Dispose()
    {
        Db.Dispose();
        _conn.Dispose();
        try { Directory.Delete(Carpeta, true); } catch { /* temporal */ }
    }
}
