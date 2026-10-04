/* =====================================================================================
   IpsosPagoHonorarios · Actualización para bases ya creadas con BD_PagoIpsos.sql
   Agrega a Areas el perfil al que pertenece (Operaciones, CEX, Public, BHT, MSU, AUM).
   Las áreas existentes quedan en Operaciones. Idempotente. Si la aplicación tiene
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
    WHERE [MigrationId] = N'20261002205056_AreaPerfil'
)
BEGIN
    ALTER TABLE [Areas] ADD [Perfil] nvarchar(40) NOT NULL DEFAULT N'Operaciones';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002205056_AreaPerfil'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002205056_AreaPerfil', N'10.0.12');
END;

COMMIT;
GO
