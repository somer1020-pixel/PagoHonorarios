/* =====================================================================================
   IpsosPagoHonorarios · Actualización para bases ya creadas con BD_PagoIpsos.sql
   Varias planillas por área en un mismo ciclo: agrega a Planillas el correlativo (Numero)
   y un nombre opcional, y cambia el índice único a (Ciclo, Área, Numero).
   Las planillas existentes quedan con Numero = 1. Idempotente. Si la aplicación tiene
   permiso db_ddladmin, la aplica sola al iniciar.
   ===================================================================================== */
USE [BD_PagoIpsos];
GO

-- sqlcmd trabaja por defecto con QUOTED_IDENTIFIER OFF; los índices filtrados (p. ej. los de Identity) lo exigen ON.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003224846_VariasPlanillasPorArea'
)
BEGIN
    DROP INDEX [IX_Planillas_CicloId_AreaId] ON [Planillas];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003224846_VariasPlanillasPorArea'
)
BEGIN
    ALTER TABLE [Planillas] ADD [Nombre] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003224846_VariasPlanillasPorArea'
)
BEGIN
    ALTER TABLE [Planillas] ADD [Numero] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003224846_VariasPlanillasPorArea'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Planillas_CicloId_AreaId_Numero] ON [Planillas] ([CicloId], [AreaId], [Numero]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003224846_VariasPlanillasPorArea'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261003224846_VariasPlanillasPorArea', N'10.0.12');
END;

COMMIT;
GO
