using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace IpsosPagoHonorarios.Web.Data;

/// <summary>
/// Configuración de SQL Server que la aplicación necesita para trabajar con muchos usuarios a la vez.
/// READ_COMMITTED_SNAPSHOT: las lecturas no esperan a las escrituras (ni al revés). Sin ella, en la prueba de carga
/// las consultas quedaban bloqueadas mientras los prestadores subían boletas y vencían a los 30 s (errores 500).
/// </summary>
public static class BaseDatos
{
    public const string ScriptLectura = "database/07_LecturaSinBloqueos.sql";

    /// <summary>null = no es SQL Server.</summary>
    public static async Task<bool?> LecturaSinBloqueosAsync(AppDbContext db, CancellationToken ct = default) =>
        db.Database.IsSqlServer()
            ? await db.Database.SqlQueryRaw<bool>("SELECT is_read_committed_snapshot_on AS [Value] FROM sys.databases WHERE name = DB_NAME()").SingleAsync(ct)
            : null;

    /// <summary>Al iniciar: si está desactivada, intenta activarla; sin permiso (lo normal con user_sql), avisa en el log.</summary>
    public static async Task AsegurarLecturaSinBloqueosAsync(AppDbContext db, ILogger log)
    {
        if (await LecturaSinBloqueosAsync(db) != false) return;
        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE");
            log.LogInformation("READ_COMMITTED_SNAPSHOT activado en la base.");
        }
        catch (SqlException ex)
        {
            log.LogWarning("READ_COMMITTED_SNAPSHOT está desactivado y el usuario de la aplicación no puede activarlo ({Mensaje}). " +
                           "Con varios usuarios a la vez las consultas se bloquean entre sí: pide al DBA ejecutar {Script}.", ex.Message, ScriptLectura);
        }
    }
}
