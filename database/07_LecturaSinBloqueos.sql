/* =====================================================================================
   IpsosPagoHonorarios · Configuración para muchos usuarios a la vez (ejecutar como DBA)
   Activa READ_COMMITTED_SNAPSHOT: las lecturas usan versiones de fila y no esperan a las
   escrituras (ni al revés). Sin esto, en la prueba de carga las consultas se bloqueaban
   mientras los prestadores subían boletas y vencían a los 30 s (errores 500).
   Requiere permiso ALTER sobre la base (db_owner o sysadmin); user_sql no lo tiene.
   WITH ROLLBACK IMMEDIATE corta las sesiones abiertas: ejecutar con la aplicación detenida
   o en una ventana de mantención. Idempotente.
   ===================================================================================== */
USE [master];
GO
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'BD_PagoIpsos' AND is_read_committed_snapshot_on = 0)
    ALTER DATABASE [BD_PagoIpsos] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO
SELECT name, is_read_committed_snapshot_on FROM sys.databases WHERE name = N'BD_PagoIpsos';
GO
