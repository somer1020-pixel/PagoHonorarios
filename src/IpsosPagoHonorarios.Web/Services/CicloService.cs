using IpsosPagoHonorarios.Core;
using IpsosPagoHonorarios.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Services;

public class CicloService(AppDbContext db, Parametros parametros, Auditor auditor, TimeProvider reloj)
{
    public DateOnly Hoy => DateOnly.FromDateTime(Formato.ALocal(reloj.GetUtcNow().UtcDateTime));
    public DateTime AhoraUtc => reloj.GetUtcNow().UtcDateTime;

    public static DateOnly PeriodoDe(DateOnly fecha) => new(fecha.Year, fecha.Month, 1);

    public async Task<Ciclo> ObtenerOCrearAsync(DateOnly periodo)
    {
        periodo = PeriodoDe(periodo);
        var c = await db.Ciclos.FirstOrDefaultAsync(x => x.Periodo == periodo);
        if (c is not null) return c;
        var p = await parametros.ObtenerAsync();
        c = new Ciclo
        {
            Periodo = periodo,
            Codigo = Formato.CodigoCiclo(periodo),
            FechaPagoProgramada = periodo.AddMonths(1).AddDays(p.DiaPago - 1)
        };
        db.Ciclos.Add(c);
        auditor.Registrar(nameof(Ciclo), c.Codigo, "Crear ciclo");
        return c;
    }

    /// <summary>
    /// Ciclo por código; si no, el abierto más reciente que no sea futuro (el diferimiento crea el ciclo siguiente por adelantado),
    /// luego el abierto más antiguo y por último el más reciente.
    /// </summary>
    public async Task<Ciclo?> SeleccionadoAsync(string? codigo)
    {
        if (!string.IsNullOrEmpty(codigo))
        {
            var c = await db.Ciclos.FirstOrDefaultAsync(x => x.Codigo == codigo);
            if (c is not null) return c;
        }
        var actual = PeriodoDe(Hoy);
        return await db.Ciclos.Where(x => x.Estado == CicloEstado.Abierto && x.Periodo <= actual).OrderByDescending(x => x.Periodo).FirstOrDefaultAsync()
               ?? await db.Ciclos.Where(x => x.Estado == CicloEstado.Abierto).OrderBy(x => x.Periodo).FirstOrDefaultAsync()
               ?? await db.Ciclos.OrderByDescending(x => x.Periodo).FirstOrDefaultAsync();
    }

    public Task<List<Ciclo>> TodosAsync() => db.Ciclos.OrderByDescending(c => c.Periodo).ToListAsync();

    /// <summary>R-15: luego del cierre el ciclo es de solo lectura.</summary>
    public static void ExigirAbierto(Ciclo c)
    {
        if (c.Estado == CicloEstado.Cerrado) throw new ReglaException($"El ciclo {c.Codigo} está cerrado: solo lectura.");
    }

    /// <summary>
    /// Toma la fila de la planilla para escribir (dentro de una transacción): las escrituras sobre una misma planilla
    /// (subir boletas, reemplazarla) se ordenan una tras otra en vez de bloquearse entre sí. En la prueba de carga, sin esto,
    /// reemplazar una planilla mientras sus prestadores subían boletas producía deadlocks.
    /// </summary>
    public Task BloquearPlanillaAsync(int planillaId) =>
        db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Planillas SET Version = Version WHERE Id = {planillaId}");

    /// <summary>Conflicto de concurrencia (otra persona modificó lo mismo a la vez): deadlock de SQL Server o fila ya cambiada.</summary>
    public static bool EsConflicto(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is DbUpdateConcurrencyException || (e is Microsoft.Data.SqlClient.SqlException s && s.Number == 1205)) return true;
        return false;
    }

    public const string MensajeConflicto = "Otra persona modificó esta planilla al mismo tiempo. Vuelve a intentarlo.";

    /// <summary>
    /// La planilla con solo las filas y boletas de un prestador: lo que necesita subir o conciliar su boleta. En la prueba de
    /// carga, leer la planilla completa (cientos de filas) en cada subida era lo más costoso del pico del portal.
    /// </summary>
    public Task<Planilla?> PlanillaDelPrestadorAsync(int id, int prestadorId) =>
        db.Planillas
            .Include(p => p.Ciclo).Include(p => p.Area)
            .Include(p => p.Lineas.Where(l => l.PrestadorId == prestadorId)).ThenInclude(l => l.Prestador).ThenInclude(x => x.Cuentas)
            .Include(p => p.Boletas.Where(b => b.PrestadorId == prestadorId))
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id);

    /// <summary>Carga una planilla con todo lo necesario para las reglas.</summary>
    /// <param name="todasLasAreas">Ignora el alcance por área del usuario (procesos que deben mantener la consistencia entre áreas).</param>
    public Task<Planilla?> PlanillaCompletaAsync(int id, bool todasLasAreas = false) =>
        (todasLasAreas ? db.Planillas.IgnoreQueryFilters() : db.Planillas)
            .Include(p => p.Ciclo).Include(p => p.Area)
            .Include(p => p.Lineas).ThenInclude(l => l.Prestador).ThenInclude(x => x.Cuentas)
            .Include(p => p.Lineas).ThenInclude(l => l.Job)
            .Include(p => p.Lineas).ThenInclude(l => l.Glosa)
            .Include(p => p.Lineas).ThenInclude(l => l.CuentaBancaria)
            .Include(p => p.Boletas)
            .Include(p => p.Devoluciones)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id);

    public async Task<List<Observacion>> ObservacionesAsync(int planillaId) =>
        await db.Observaciones.Include(o => o.LineaPago).ThenInclude(l => l.Prestador)
            .Include(o => o.LineaPago).ThenInclude(l => l.Job).Include(o => o.LineaPago).ThenInclude(l => l.Glosa)
            .Where(o => o.LineaPago.PlanillaId == planillaId).OrderBy(o => o.Id).ToListAsync();

    public async Task<Devolucion?> DevolucionActivaAsync(int planillaId) =>
        await db.Devoluciones.Where(d => d.PlanillaId == planillaId && d.Resultado == DevolucionResultado.Pendiente)
            .OrderByDescending(d => d.Id).FirstOrDefaultAsync();
}
