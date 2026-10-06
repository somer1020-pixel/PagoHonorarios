/* =====================================================================================
   IpsosPagoHonorarios · Actualización para bases ya creadas con BD_PagoIpsos.sql
   Avisos por WhatsApp: autorización del prestador (Prestadores.WhatsAppAutorizado) y bandeja
   de mensajes (tabla WhatsApp). Idempotente. Si la aplicación tiene permiso db_ddladmin, la
   aplica sola al iniciar.
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
    WHERE [MigrationId] = N'20261006013401_WhatsApp'
)
BEGIN
    ALTER TABLE [Prestadores] ADD [WhatsAppAutorizado] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006013401_WhatsApp'
)
BEGIN
    ALTER TABLE [Prestadores] ADD [WhatsAppAutorizadoEn] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006013401_WhatsApp'
)
BEGIN
    CREATE TABLE [WhatsApp] (
        [Id] bigint NOT NULL IDENTITY,
        [CreadoEn] datetime2 NOT NULL,
        [PrestadorId] int NULL,
        [Para] nvarchar(20) NOT NULL,
        [Plantilla] nvarchar(100) NOT NULL,
        [Parametros] nvarchar(2000) NOT NULL,
        [Texto] nvarchar(1000) NOT NULL,
        [EnviadoEn] datetime2 NULL,
        [Intentos] int NOT NULL,
        [UltimoIntentoEn] datetime2 NULL,
        [ProveedorId] nvarchar(100) NULL,
        [Error] nvarchar(500) NULL,
        CONSTRAINT [PK_WhatsApp] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006013401_WhatsApp'
)
BEGIN
    CREATE INDEX [IX_WhatsApp_EnviadoEn_Intentos] ON [WhatsApp] ([EnviadoEn], [Intentos]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006013401_WhatsApp'
)
BEGIN
    CREATE INDEX [IX_WhatsApp_Para_Plantilla_CreadoEn] ON [WhatsApp] ([Para], [Plantilla], [CreadoEn]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006013401_WhatsApp'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006013401_WhatsApp', N'10.0.12');
END;

COMMIT;
GO
GO
