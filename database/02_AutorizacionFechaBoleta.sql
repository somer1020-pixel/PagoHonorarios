/* =====================================================================================
   IpsosPagoHonorarios · Actualización para bases ya creadas con BD_PagoIpsos.sql
   Agrega a Boletas la autorización del Administrador para boletas fuera de plazo.
   Idempotente. Si la aplicación tiene permiso db_ddladmin, la aplica sola al iniciar.
   ===================================================================================== */
USE [BD_PagoIpsos];
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002191226_AutorizacionFechaBoleta'
)
BEGIN
    ALTER TABLE [Boletas] ADD [FechaAutorizadaEn] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002191226_AutorizacionFechaBoleta'
)
BEGIN
    ALTER TABLE [Boletas] ADD [FechaAutorizadaMotivo] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002191226_AutorizacionFechaBoleta'
)
BEGIN
    ALTER TABLE [Boletas] ADD [FechaAutorizadaPor] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002191226_AutorizacionFechaBoleta'
)
BEGIN
    ALTER TABLE [Boletas] ADD [FueraDePlazo] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002191226_AutorizacionFechaBoleta'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002191226_AutorizacionFechaBoleta', N'10.0.12');
END;

COMMIT;
GO
GO
