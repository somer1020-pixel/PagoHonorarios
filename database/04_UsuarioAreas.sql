/* =====================================================================================
   IpsosPagoHonorarios · Actualización para bases ya creadas con BD_PagoIpsos.sql
   Agrega UsuarioAreas: áreas que gestiona cada usuario de Operaciones (y CEX, Public, BHT,
   MSU, AUM); solo ven las planillas de esas áreas. Finanzas y Administrador ven todas.
   Idempotente. Si la aplicación tiene permiso db_ddladmin, la aplica sola al iniciar.
   Después de aplicarla, el Administrador debe asignar las áreas en Maestros → Usuarios:
   un usuario operativo sin áreas no ve planillas.
   ===================================================================================== */
USE [BD_PagoIpsos];
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002204034_UsuarioAreas'
)
BEGIN
    CREATE TABLE [UsuarioAreas] (
        [UsuarioId] nvarchar(450) NOT NULL,
        [AreaId] int NOT NULL,
        CONSTRAINT [PK_UsuarioAreas] PRIMARY KEY ([UsuarioId], [AreaId]),
        CONSTRAINT [FK_UsuarioAreas_Areas_AreaId] FOREIGN KEY ([AreaId]) REFERENCES [Areas] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UsuarioAreas_AspNetUsers_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002204034_UsuarioAreas'
)
BEGIN
    CREATE INDEX [IX_UsuarioAreas_AreaId] ON [UsuarioAreas] ([AreaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002204034_UsuarioAreas'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002204034_UsuarioAreas', N'10.0.12');
END;

COMMIT;
GO
