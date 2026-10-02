/* =====================================================================================
   IpsosPagoHonorarios · Modelo de datos para SQL Server
   Servidor: AMCLSANSQL9 · Base de datos: BD_PagoIpsos
   Generado desde las migraciones de EF Core (idempotente: se puede ejecutar más de una vez).

   Uso (SSMS o sqlcmd, con un usuario que pueda crear la base):
     sqlcmd -S AMCLSANSQL9 -U <usuario_admin> -P <clave> -i BD_PagoIpsos.sql

   La aplicación también aplica migraciones pendientes al iniciar y siembra los catálogos;
   este script deja la base lista sin depender de eso. Datos de catálogo = extracto de la
   especificación (§10): reemplazar por la hoja Formato completa de Finanzas.
   ===================================================================================== */

IF DB_ID(N'BD_PagoIpsos') IS NULL
    CREATE DATABASE [BD_PagoIpsos];
GO

USE [BD_PagoIpsos];
GO

/* ---------- Permisos del usuario de la aplicación (descomentar si el login ya existe) ----------
   La aplicación necesita leer y escribir; db_ddladmin solo si se quiere que aplique
   migraciones futuras al iniciar.

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'user_sql')
    CREATE USER [user_sql] FOR LOGIN [user_sql];
ALTER ROLE db_datareader ADD MEMBER [user_sql];
ALTER ROLE db_datawriter ADD MEMBER [user_sql];
ALTER ROLE db_ddladmin  ADD MEMBER [user_sql];
GO
------------------------------------------------------------------------------------------------ */

/* ---------- Tablas, índices y claves (migración Inicial) ---------- */
IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [Areas] (
        [Id] int NOT NULL IDENTITY,
        [Nombre] nvarchar(100) NOT NULL,
        [CodigoArea] nvarchar(20) NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_Areas] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] nvarchar(450) NOT NULL,
        [NombreCompleto] nvarchar(max) NOT NULL,
        [PrestadorId] int NULL,
        [Activo] bit NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [Auditorias] (
        [Id] bigint NOT NULL IDENTITY,
        [Fecha] datetime2 NOT NULL,
        [Usuario] nvarchar(max) NULL,
        [Entidad] nvarchar(60) NOT NULL,
        [EntidadId] nvarchar(max) NULL,
        [Accion] nvarchar(100) NOT NULL,
        [Detalle] nvarchar(max) NULL,
        CONSTRAINT [PK_Auditorias] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [Bancos] (
        [Id] int NOT NULL IDENTITY,
        [Nombre] nvarchar(60) NOT NULL,
        [CodigoBanco] nvarchar(10) NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_Bancos] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [Ciclos] (
        [Id] int NOT NULL IDENTITY,
        [Periodo] date NOT NULL,
        [Codigo] nvarchar(10) NOT NULL,
        [Estado] nvarchar(40) NOT NULL,
        [FechaPagoProgramada] date NOT NULL,
        [CerradoEn] datetime2 NULL,
        [CerradoPor] nvarchar(max) NULL,
        [RutaZip] nvarchar(max) NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_Ciclos] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [Correos] (
        [Id] bigint NOT NULL IDENTITY,
        [CreadoEn] datetime2 NOT NULL,
        [Para] nvarchar(max) NOT NULL,
        [Asunto] nvarchar(max) NOT NULL,
        [Cuerpo] nvarchar(max) NOT NULL,
        [EnviadoEn] datetime2 NULL,
        [Error] nvarchar(max) NULL,
        CONSTRAINT [PK_Correos] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [Glosas] (
        [Id] int NOT NULL IDENTITY,
        [NombreGlosa] nvarchar(150) NOT NULL,
        [Item] nvarchar(20) NOT NULL,
        [CuentaContable] nvarchar(20) NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_Glosas] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [Parametros] (
        [Id] int NOT NULL IDENTITY,
        [PlazoCorreccionMinutos] int NOT NULL,
        [RutEmpresa] nvarchar(max) NOT NULL,
        [RazonSocialEmpresa] nvarchar(max) NOT NULL,
        [DiaDescargaDesde] int NOT NULL,
        [DiaDescargaHasta] int NOT NULL,
        [DiaPago] int NOT NULL,
        [DiaLimiteBoleta] int NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_Parametros] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [Prestadores] (
        [Id] int NOT NULL IDENTITY,
        [Rut] int NOT NULL,
        [Dv] nvarchar(1) NOT NULL,
        [NombreCompleto] nvarchar(200) NOT NULL,
        [Email] nvarchar(200) NULL,
        [Telefono] nvarchar(30) NULL,
        [UsuarioId] nvarchar(max) NULL,
        [PortalInvitadoEn] datetime2 NULL,
        [PortalActivadoEn] datetime2 NULL,
        [Activo] bit NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_Prestadores] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [TasasRetencion] (
        [Id] int NOT NULL IDENTITY,
        [Anio] int NOT NULL,
        [Tasa] decimal(9,4) NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_TasasRetencion] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [TiposCuenta] (
        [Id] int NOT NULL IDENTITY,
        [Nombre] nvarchar(40) NOT NULL,
        [Codigo] nvarchar(10) NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_TiposCuenta] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [TiposGasto] (
        [Id] int NOT NULL IDENTITY,
        [Nombre] nvarchar(40) NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_TiposGasto] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [Jobs] (
        [Id] int NOT NULL IDENTITY,
        [JobBookNumber] nchar(12) NOT NULL,
        [Nombre] nvarchar(200) NOT NULL,
        [AreaSugeridaId] int NULL,
        [ValorUnitarioSugerido] decimal(18,2) NULL,
        [Activo] bit NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_Jobs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Jobs_Areas_AreaSugeridaId] FOREIGN KEY ([AreaSugeridaId]) REFERENCES [Areas] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [Planillas] (
        [Id] int NOT NULL IDENTITY,
        [CicloId] int NOT NULL,
        [AreaId] int NOT NULL,
        [FechaRecepcion] date NOT NULL,
        [ResponsableNombre] nvarchar(150) NOT NULL,
        [ResponsableEmail] nvarchar(200) NOT NULL,
        [Estado] nvarchar(40) NOT NULL,
        [Version] int NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_Planillas] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Planillas_Areas_AreaId] FOREIGN KEY ([AreaId]) REFERENCES [Areas] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Planillas_Ciclos_CicloId] FOREIGN KEY ([CicloId]) REFERENCES [Ciclos] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [CuentasBancarias] (
        [Id] int NOT NULL IDENTITY,
        [PrestadorId] int NOT NULL,
        [Banco] nvarchar(60) NOT NULL,
        [TipoCuenta] nvarchar(40) NOT NULL,
        [Cuenta] nvarchar(20) NOT NULL,
        [CuentaNormalizada] nvarchar(20) NOT NULL,
        [Estado] nvarchar(40) NOT NULL,
        [Vigente] bit NOT NULL,
        [Origen] nvarchar(40) NOT NULL,
        [OrigenDetalle] nvarchar(max) NULL,
        [RequiereRevision] bit NOT NULL,
        [RegistradaPor] nvarchar(max) NULL,
        [RegistradaEn] datetime2 NULL,
        [ValidadaPor] nvarchar(max) NULL,
        [ValidadaEn] datetime2 NULL,
        [ReemplazadaEn] datetime2 NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_CuentasBancarias] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CuentasBancarias_Prestadores_PrestadorId] FOREIGN KEY ([PrestadorId]) REFERENCES [Prestadores] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [Boletas] (
        [Id] int NOT NULL IDENTITY,
        [PlanillaId] int NOT NULL,
        [PrestadorId] int NOT NULL,
        [Canal] nvarchar(40) NOT NULL,
        [MotivoCargaOperaciones] nvarchar(max) NULL,
        [SubidaPor] nvarchar(max) NULL,
        [SubidaEn] datetime2 NOT NULL,
        [Estado] nvarchar(40) NOT NULL,
        [NumeroBoleta] nvarchar(20) NULL,
        [RutEmisor] int NULL,
        [RutEmisorDv] nvarchar(1) NULL,
        [NombreEmisor] nvarchar(max) NULL,
        [RutReceptor] nvarchar(12) NULL,
        [FechaEmision] date NULL,
        [MontoBruto] decimal(18,0) NULL,
        [MontoRetencion] decimal(18,0) NULL,
        [MontoLiquido] decimal(18,0) NULL,
        [RutaPdf] nvarchar(max) NOT NULL,
        [HashPdf] nvarchar(64) NOT NULL,
        [TextoExtraido] nvarchar(max) NULL,
        [Confianza] nvarchar(40) NOT NULL,
        [ConfirmadaPor] nvarchar(max) NULL,
        [ConfirmadaEn] datetime2 NULL,
        [ReemplazaABoletaId] int NULL,
        [ResultadoValidacion] nvarchar(max) NULL,
        [Cuadra] bit NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_Boletas] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Boletas_Planillas_PlanillaId] FOREIGN KEY ([PlanillaId]) REFERENCES [Planillas] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Boletas_Prestadores_PrestadorId] FOREIGN KEY ([PrestadorId]) REFERENCES [Prestadores] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [Devoluciones] (
        [Id] int NOT NULL IDENTITY,
        [PlanillaId] int NOT NULL,
        [Version] int NOT NULL,
        [DevueltaEn] datetime2 NOT NULL,
        [DevueltaPor] nvarchar(max) NULL,
        [VenceEn] datetime2 NOT NULL,
        [ReenviadaEn] datetime2 NULL,
        [Resultado] nvarchar(40) NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_Devoluciones] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Devoluciones_Planillas_PlanillaId] FOREIGN KEY ([PlanillaId]) REFERENCES [Planillas] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [PlanillaArchivos] (
        [Id] int NOT NULL IDENTITY,
        [PlanillaId] int NOT NULL,
        [Version] int NOT NULL,
        [Tipo] nvarchar(max) NOT NULL,
        [NombreArchivo] nvarchar(max) NOT NULL,
        [Ruta] nvarchar(max) NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_PlanillaArchivos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PlanillaArchivos_Planillas_PlanillaId] FOREIGN KEY ([PlanillaId]) REFERENCES [Planillas] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [SolicitudesCorreccion] (
        [Id] int NOT NULL IDENTITY,
        [PlanillaId] int NOT NULL,
        [Version] int NOT NULL,
        [EnviadaA] nvarchar(max) NOT NULL,
        [EnviadaPor] nvarchar(max) NULL,
        [EnviadaEn] datetime2 NOT NULL,
        [Mensaje] nvarchar(max) NOT NULL,
        [Filas] nvarchar(max) NOT NULL,
        [Estado] nvarchar(40) NOT NULL,
        [Envios] int NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_SolicitudesCorreccion] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SolicitudesCorreccion_Planillas_PlanillaId] FOREIGN KEY ([PlanillaId]) REFERENCES [Planillas] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [LineasPago] (
        [Id] int NOT NULL IDENTITY,
        [PlanillaId] int NOT NULL,
        [Numero] int NOT NULL,
        [TipoGasto] nvarchar(40) NOT NULL,
        [JobId] int NOT NULL,
        [GlosaId] int NOT NULL,
        [ValorUnitarioBruto] decimal(18,2) NOT NULL,
        [Cantidad] decimal(18,4) NOT NULL,
        [ValorTotalBruto] decimal(18,0) NOT NULL,
        [PrestadorId] int NOT NULL,
        [NombrePlanilla] nvarchar(max) NOT NULL,
        [NumeroBoleta] nvarchar(20) NULL,
        [CuentaPlanillaTipo] nvarchar(40) NULL,
        [CuentaPlanillaNumero] nvarchar(30) NULL,
        [CuentaPlanillaBanco] nvarchar(60) NULL,
        [CuentaBancariaId] int NULL,
        [ResultadoCuenta] nvarchar(40) NOT NULL,
        [ResultadoCuentaDetalle] nvarchar(max) NULL,
        [AlertaCuentaResuelta] bit NOT NULL,
        [Estado] nvarchar(40) NOT NULL,
        [DiferidaDesdeCicloId] int NULL,
        [DiferidaACicloId] int NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_LineasPago] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LineasPago_CuentasBancarias_CuentaBancariaId] FOREIGN KEY ([CuentaBancariaId]) REFERENCES [CuentasBancarias] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LineasPago_Glosas_GlosaId] FOREIGN KEY ([GlosaId]) REFERENCES [Glosas] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LineasPago_Jobs_JobId] FOREIGN KEY ([JobId]) REFERENCES [Jobs] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LineasPago_Planillas_PlanillaId] FOREIGN KEY ([PlanillaId]) REFERENCES [Planillas] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LineasPago_Prestadores_PrestadorId] FOREIGN KEY ([PrestadorId]) REFERENCES [Prestadores] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [Transferencias] (
        [Id] int NOT NULL IDENTITY,
        [PlanillaId] int NOT NULL,
        [PrestadorId] int NOT NULL,
        [BoletaId] int NOT NULL,
        [CuentaBancariaId] int NOT NULL,
        [Fecha] date NOT NULL,
        [NumeroOperacion] nvarchar(50) NOT NULL,
        [MontoLiquido] decimal(18,0) NOT NULL,
        [Comprobante] nvarchar(max) NOT NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_Transferencias] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Transferencias_Boletas_BoletaId] FOREIGN KEY ([BoletaId]) REFERENCES [Boletas] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Transferencias_CuentasBancarias_CuentaBancariaId] FOREIGN KEY ([CuentaBancariaId]) REFERENCES [CuentasBancarias] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Transferencias_Planillas_PlanillaId] FOREIGN KEY ([PlanillaId]) REFERENCES [Planillas] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Transferencias_Prestadores_PrestadorId] FOREIGN KEY ([PrestadorId]) REFERENCES [Prestadores] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE TABLE [Observaciones] (
        [Id] int NOT NULL IDENTITY,
        [LineaPagoId] int NOT NULL,
        [Tipo] nvarchar(40) NOT NULL,
        [Campo] nvarchar(100) NOT NULL,
        [Detalle] nvarchar(1000) NOT NULL,
        [Estado] nvarchar(40) NOT NULL,
        [CreadaPor] nvarchar(max) NULL,
        [CreadaEn] datetime2 NOT NULL,
        [CorregidaPor] nvarchar(max) NULL,
        [CorregidaEn] datetime2 NULL,
        [Respuesta] nvarchar(max) NULL,
        [DevolucionId] int NULL,
        [CreadoEn] datetime2 NOT NULL,
        [CreadoPor] nvarchar(max) NULL,
        [ModificadoEn] datetime2 NULL,
        [ModificadoPor] nvarchar(max) NULL,
        CONSTRAINT [PK_Observaciones] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Observaciones_Devoluciones_DevolucionId] FOREIGN KEY ([DevolucionId]) REFERENCES [Devoluciones] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Observaciones_LineasPago_LineaPagoId] FOREIGN KEY ([LineaPagoId]) REFERENCES [LineasPago] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Areas_Nombre] ON [Areas] ([Nombre]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_Auditorias_Fecha] ON [Auditorias] ([Fecha]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Bancos_Nombre] ON [Bancos] ([Nombre]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_Boletas_HashPdf] ON [Boletas] ([HashPdf]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_Boletas_PlanillaId] ON [Boletas] ([PlanillaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_Boletas_PrestadorId] ON [Boletas] ([PrestadorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_Boletas_RutEmisor_NumeroBoleta] ON [Boletas] ([RutEmisor], [NumeroBoleta]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Ciclos_Codigo] ON [Ciclos] ([Codigo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Ciclos_Periodo] ON [Ciclos] ([Periodo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_CuentasBancarias_Banco_CuentaNormalizada] ON [CuentasBancarias] ([Banco], [CuentaNormalizada]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_CuentasBancarias_PrestadorId] ON [CuentasBancarias] ([PrestadorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_Devoluciones_PlanillaId] ON [Devoluciones] ([PlanillaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Glosas_NombreGlosa] ON [Glosas] ([NombreGlosa]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_Jobs_AreaSugeridaId] ON [Jobs] ([AreaSugeridaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Jobs_JobBookNumber] ON [Jobs] ([JobBookNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_LineasPago_CuentaBancariaId] ON [LineasPago] ([CuentaBancariaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_LineasPago_GlosaId] ON [LineasPago] ([GlosaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_LineasPago_JobId] ON [LineasPago] ([JobId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_LineasPago_PlanillaId] ON [LineasPago] ([PlanillaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_LineasPago_PrestadorId] ON [LineasPago] ([PrestadorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_Observaciones_DevolucionId] ON [Observaciones] ([DevolucionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_Observaciones_LineaPagoId] ON [Observaciones] ([LineaPagoId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_PlanillaArchivos_PlanillaId] ON [PlanillaArchivos] ([PlanillaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_Planillas_AreaId] ON [Planillas] ([AreaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Planillas_CicloId_AreaId] ON [Planillas] ([CicloId], [AreaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Prestadores_Rut] ON [Prestadores] ([Rut]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_SolicitudesCorreccion_PlanillaId] ON [SolicitudesCorreccion] ([PlanillaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TasasRetencion_Anio] ON [TasasRetencion] ([Anio]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TiposCuenta_Nombre] ON [TiposCuenta] ([Nombre]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TiposGasto_Nombre] ON [TiposGasto] ([Nombre]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Transferencias_BoletaId] ON [Transferencias] ([BoletaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_Transferencias_CuentaBancariaId] ON [Transferencias] ([CuentaBancariaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_Transferencias_PlanillaId] ON [Transferencias] ([PlanillaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    CREATE INDEX [IX_Transferencias_PrestadorId] ON [Transferencias] ([PrestadorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002121808_Inicial'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002121808_Inicial', N'10.0.12');
END;

COMMIT;
GO
GO

/* ---------- Datos iniciales ---------- */
SET NOCOUNT ON;

-- Roles de la aplicación (ASP.NET Core Identity)
IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'OPERACIONES')
    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp]) VALUES (CONVERT(nvarchar(450), NEWID()), N'Operaciones', N'OPERACIONES', CONVERT(nvarchar(max), NEWID()));
IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'FINANZAS')
    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp]) VALUES (CONVERT(nvarchar(450), NEWID()), N'Finanzas', N'FINANZAS', CONVERT(nvarchar(max), NEWID()));
IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'PRESTADOR')
    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp]) VALUES (CONVERT(nvarchar(450), NEWID()), N'Prestador', N'PRESTADOR', CONVERT(nvarchar(max), NEWID()));
IF NOT EXISTS (SELECT 1 FROM [AspNetRoles] WHERE [NormalizedName] = N'ADMIN')
    INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp]) VALUES (CONVERT(nvarchar(450), NEWID()), N'Admin', N'ADMIN', CONVERT(nvarchar(max), NEWID()));

-- Catálogos de Finanzas (TODO(diseño): completar con la hoja Formato)
IF NOT EXISTS (SELECT 1 FROM [Areas] WHERE [Nombre] = N'FACE TO FACE')
    INSERT INTO [Areas] ([Nombre], [CodigoArea], [CreadoEn], [CreadoPor]) VALUES (N'FACE TO FACE', N'21183', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Areas] WHERE [Nombre] = N'MYSTERY SHOPPING')
    INSERT INTO [Areas] ([Nombre], [CodigoArea], [CreadoEn], [CreadoPor]) VALUES (N'MYSTERY SHOPPING', N'21187', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Areas] WHERE [Nombre] = N'DATA PROCESSING')
    INSERT INTO [Areas] ([Nombre], [CodigoArea], [CreadoEn], [CreadoPor]) VALUES (N'DATA PROCESSING', N'21184', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Areas] WHERE [Nombre] = N'Operations CATI')
    INSERT INTO [Areas] ([Nombre], [CodigoArea], [CreadoEn], [CreadoPor]) VALUES (N'Operations CATI', N'21161', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Glosas] WHERE [NombreGlosa] = N'Honorarios entrevistadores')
    INSERT INTO [Glosas] ([NombreGlosa], [Item], [CuentaContable], [CreadoEn], [CreadoPor]) VALUES (N'Honorarios entrevistadores', N'1310', N'602101', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Glosas] WHERE [NombreGlosa] = N'Honorarios supervisión')
    INSERT INTO [Glosas] ([NombreGlosa], [Item], [CuentaContable], [CreadoEn], [CreadoPor]) VALUES (N'Honorarios supervisión', N'1330', N'602101', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Glosas] WHERE [NombreGlosa] = N'Honorario codificación externa')
    INSERT INTO [Glosas] ([NombreGlosa], [Item], [CuentaContable], [CreadoEn], [CreadoPor]) VALUES (N'Honorario codificación externa', N'2830', N'602101', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Bancos] WHERE [Nombre] = N'ESTADO')
    INSERT INTO [Bancos] ([Nombre], [CodigoBanco], [CreadoEn], [CreadoPor]) VALUES (N'ESTADO', N'12', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Bancos] WHERE [Nombre] = N'CHILE')
    INSERT INTO [Bancos] ([Nombre], [CodigoBanco], [CreadoEn], [CreadoPor]) VALUES (N'CHILE', N'1', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Bancos] WHERE [Nombre] = N'BANEFE')
    INSERT INTO [Bancos] ([Nombre], [CodigoBanco], [CreadoEn], [CreadoPor]) VALUES (N'BANEFE', N'37', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Bancos] WHERE [Nombre] = N'BCI')
    INSERT INTO [Bancos] ([Nombre], [CodigoBanco], [CreadoEn], [CreadoPor]) VALUES (N'BCI', N'16', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Bancos] WHERE [Nombre] = N'SANTANDER')
    INSERT INTO [Bancos] ([Nombre], [CodigoBanco], [CreadoEn], [CreadoPor]) VALUES (N'SANTANDER', N'37', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Bancos] WHERE [Nombre] = N'SCOTIABANK')
    INSERT INTO [Bancos] ([Nombre], [CodigoBanco], [CreadoEn], [CreadoPor]) VALUES (N'SCOTIABANK', N'', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Bancos] WHERE [Nombre] = N'ITAU')
    INSERT INTO [Bancos] ([Nombre], [CodigoBanco], [CreadoEn], [CreadoPor]) VALUES (N'ITAU', N'', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Bancos] WHERE [Nombre] = N'FALABELLA')
    INSERT INTO [Bancos] ([Nombre], [CodigoBanco], [CreadoEn], [CreadoPor]) VALUES (N'FALABELLA', N'', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Bancos] WHERE [Nombre] = N'MERCADO PAGO')
    INSERT INTO [Bancos] ([Nombre], [CodigoBanco], [CreadoEn], [CreadoPor]) VALUES (N'MERCADO PAGO', N'874', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [Bancos] WHERE [Nombre] = N'Tenpo')
    INSERT INTO [Bancos] ([Nombre], [CodigoBanco], [CreadoEn], [CreadoPor]) VALUES (N'Tenpo', N'730', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [TiposCuenta] WHERE [Nombre] = N'CORRIENTE')
    INSERT INTO [TiposCuenta] ([Nombre], [Codigo], [CreadoEn], [CreadoPor]) VALUES (N'CORRIENTE', N'', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [TiposCuenta] WHERE [Nombre] = N'VISTA')
    INSERT INTO [TiposCuenta] ([Nombre], [Codigo], [CreadoEn], [CreadoPor]) VALUES (N'VISTA', N'', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [TiposCuenta] WHERE [Nombre] = N'RUT')
    INSERT INTO [TiposCuenta] ([Nombre], [Codigo], [CreadoEn], [CreadoPor]) VALUES (N'RUT', N'', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [TiposCuenta] WHERE [Nombre] = N'AHORRO')
    INSERT INTO [TiposCuenta] ([Nombre], [Codigo], [CreadoEn], [CreadoPor]) VALUES (N'AHORRO', N'', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [TiposCuenta] WHERE [Nombre] = N'CHEQUERA ELECTRONICA')
    INSERT INTO [TiposCuenta] ([Nombre], [Codigo], [CreadoEn], [CreadoPor]) VALUES (N'CHEQUERA ELECTRONICA', N'', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [TiposCuenta] WHERE [Nombre] = N'DEBITO')
    INSERT INTO [TiposCuenta] ([Nombre], [Codigo], [CreadoEn], [CreadoPor]) VALUES (N'DEBITO', N'', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [TiposGasto] WHERE [Nombre] = N'Costo Directo')
    INSERT INTO [TiposGasto] ([Nombre], [CreadoEn], [CreadoPor]) VALUES (N'Costo Directo', SYSUTCDATETIME(), N'script');
IF NOT EXISTS (SELECT 1 FROM [TiposGasto] WHERE [Nombre] = N'Payroll')
    INSERT INTO [TiposGasto] ([Nombre], [CreadoEn], [CreadoPor]) VALUES (N'Payroll', SYSUTCDATETIME(), N'script');

-- Parámetros. TODO(diseño): RUT y razón social reales de la empresa receptora.
IF NOT EXISTS (SELECT 1 FROM [Parametros])
    INSERT INTO [Parametros] ([PlazoCorreccionMinutos], [RutEmpresa], [RazonSocialEmpresa], [DiaDescargaDesde], [DiaDescargaHasta], [DiaPago], [DiaLimiteBoleta], [CreadoEn], [CreadoPor])
    VALUES (60, N'77777777-7', N'TODO(diseño): razón social', 28, 30, 5, 10, SYSUTCDATETIME(), N'script');

-- Tasa de retención por año (R-05)
IF NOT EXISTS (SELECT 1 FROM [TasasRetencion] WHERE [Anio] = 2026)
    INSERT INTO [TasasRetencion] ([Anio], [Tasa], [CreadoEn], [CreadoPor]) VALUES (2026, 0.1525, SYSUTCDATETIME(), N'script');
GO

/* El primer usuario administrador lo crea la aplicación al iniciar si se configuran
   Semilla:AdminEmail y Semilla:AdminPassword (la contraseña se guarda con hash de Identity). */
