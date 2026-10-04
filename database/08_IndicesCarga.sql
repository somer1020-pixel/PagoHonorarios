/* =====================================================================================
   IpsosPagoHonorarios · Actualización para bases ya creadas con BD_PagoIpsos.sql
   Índices de cobertura para los contadores del menú, el panel y el historial (resultado de
   la prueba de carga): LineasPago (PlanillaId, Estado) y Boletas (PlanillaId, PrestadorId).
   Idempotente. Si la aplicación tiene permiso db_ddladmin, la aplica sola al iniciar.
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
    WHERE [MigrationId] = N'20261004215711_IndicesCarga'
)
BEGIN
    DROP INDEX [IX_LineasPago_PlanillaId] ON [LineasPago];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004215711_IndicesCarga'
)
BEGIN
    DROP INDEX [IX_Boletas_PlanillaId] ON [Boletas];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004215711_IndicesCarga'
)
BEGIN
    CREATE INDEX [IX_LineasPago_PlanillaId_Estado] ON [LineasPago] ([PlanillaId], [Estado]) INCLUDE ([PrestadorId], [ResultadoCuenta], [AlertaCuentaResuelta], [ValorTotalBruto]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004215711_IndicesCarga'
)
BEGIN
    CREATE INDEX [IX_Boletas_PlanillaId_PrestadorId] ON [Boletas] ([PlanillaId], [PrestadorId]) INCLUDE ([Estado]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004215711_IndicesCarga'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004215711_IndicesCarga', N'10.0.12');
END;

COMMIT;
GO
