/* Perfiles CEX, Public, BHT, MSU y AUM: gestionan procesos de pago con las mismas pantallas y permisos que Operaciones.
   Idempotente. La aplicación también los crea al iniciar; este script es opcional. */
USE [BD_PagoIpsos];
GO

-- sqlcmd trabaja por defecto con QUOTED_IDENTIFIER OFF; los índices filtrados (p. ej. los de Identity) lo exigen ON.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'CEX')
    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp]) VALUES (CONVERT(nvarchar(450), NEWID()), N'CEX', N'CEX', CONVERT(nvarchar(max), NEWID()));
IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'PUBLIC')
    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp]) VALUES (CONVERT(nvarchar(450), NEWID()), N'Public', N'PUBLIC', CONVERT(nvarchar(max), NEWID()));
IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'BHT')
    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp]) VALUES (CONVERT(nvarchar(450), NEWID()), N'BHT', N'BHT', CONVERT(nvarchar(max), NEWID()));
IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'MSU')
    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp]) VALUES (CONVERT(nvarchar(450), NEWID()), N'MSU', N'MSU', CONVERT(nvarchar(max), NEWID()));
IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'AUM')
    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp]) VALUES (CONVERT(nvarchar(450), NEWID()), N'AUM', N'AUM', CONVERT(nvarchar(max), NEWID()));
GO
