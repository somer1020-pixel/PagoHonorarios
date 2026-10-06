/* =====================================================================================
   IpsosPagoHonorarios · Catálogo de bancos y tipos de cuenta
   Completa los códigos que usa la nómina de transferencias y agrega los bancos que faltan.

   Seguro de repetir (idempotente) y NO pisa lo que ya está cargado:
     - Un banco que ya existe conserva su código; solo se completa si estaba vacío.
     - No se borra ni se renombra nada.
   Se puede ejecutar a mano (SSMS o sqlcmd) con el usuario administrador o user_sql.

   Los códigos de banco son los de la CMF (ex SBIF), sin ceros a la izquierda, igual que la hoja Formato
   de Finanzas (CHILE = 1, ESTADO = 12). Revísalos contra esa hoja antes de usarlos en producción.
   ===================================================================================== */
USE [BD_PagoIpsos];
GO

-- sqlcmd trabaja por defecto con QUOTED_IDENTIFIER OFF; los índices filtrados (p. ej. los de Identity) lo exigen ON.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

BEGIN TRANSACTION;

/* ---------- 1. Bancos ---------- */
DECLARE @bancos TABLE ([Nombre] nvarchar(60) NOT NULL, [Codigo] nvarchar(10) NOT NULL);
INSERT INTO @bancos ([Nombre], [Codigo]) VALUES
    -- Ya estaban en la hoja Formato (no se modifican si tienen código)
    (N'CHILE',          N'1'),
    (N'ESTADO',         N'12'),
    (N'BCI',            N'16'),
    (N'SANTANDER',      N'37'),
    (N'MERCADO PAGO',   N'874'),   -- La hoja de Finanzas usa 874; otras fuentes publican 875. Confírmalo con Finanzas.
    (N'Tenpo',          N'730'),
    -- Sin código hasta ahora
    (N'SCOTIABANK',     N'14'),
    (N'ITAU',           N'39'),
    (N'FALABELLA',      N'51'),
    -- Bancos que faltaban en el extracto
    (N'INTERNACIONAL',  N'9'),
    (N'BICE',           N'28'),
    (N'SECURITY',       N'49'),
    (N'RIPLEY',         N'53'),
    (N'CONSORCIO',      N'55'),
    (N'BTG PACTUAL',    N'59'),
    (N'COOPEUCH',       N'672');

-- Completa el código solo donde estaba vacío.
UPDATE b
   SET b.[CodigoBanco] = v.[Codigo], b.[ModificadoEn] = SYSUTCDATETIME(), b.[ModificadoPor] = N'script'
  FROM [Bancos] b
  JOIN @bancos v ON v.[Nombre] = b.[Nombre]
 WHERE LTRIM(RTRIM(b.[CodigoBanco])) = N'' AND v.[Codigo] <> N'';
PRINT CONCAT(N'Bancos con código completado: ', @@ROWCOUNT);

-- Agrega los que no existen (la comparación no distingue mayúsculas ni minúsculas).
INSERT INTO [Bancos] ([Nombre], [CodigoBanco], [CreadoEn], [CreadoPor])
SELECT v.[Nombre], v.[Codigo], SYSUTCDATETIME(), N'script'
  FROM @bancos v
 WHERE NOT EXISTS (SELECT 1 FROM [Bancos] b WHERE b.[Nombre] = v.[Nombre]);
PRINT CONCAT(N'Bancos agregados: ', @@ROWCOUNT);

/* ---------- 2. Tipos de cuenta ----------
   El código de cada tipo depende del formato de transferencia de Finanzas (hoja Formato, lista tipos_de_cuenta):
   no es un estándar único entre bancos, así que NO se inventa. Escribe aquí el código de cada tipo y ejecuta el
   script de nuevo: solo se completan los tipos que hoy tienen el código vacío. Ejemplo:  (N'CORRIENTE', N'01')   */
DECLARE @tipos TABLE ([Nombre] nvarchar(40) NOT NULL, [Codigo] nvarchar(10) NOT NULL);
INSERT INTO @tipos ([Nombre], [Codigo]) VALUES
    (N'CORRIENTE',            N''),
    (N'VISTA',                N''),
    (N'RUT',                  N''),
    (N'AHORRO',               N''),
    (N'CHEQUERA ELECTRONICA', N''),
    (N'DEBITO',               N'');

UPDATE t
   SET t.[Codigo] = v.[Codigo], t.[ModificadoEn] = SYSUTCDATETIME(), t.[ModificadoPor] = N'script'
  FROM [TiposCuenta] t
  JOIN @tipos v ON v.[Nombre] = t.[Nombre]
 WHERE LTRIM(RTRIM(t.[Codigo])) = N'' AND v.[Codigo] <> N'';
PRINT CONCAT(N'Tipos de cuenta con código completado: ', @@ROWCOUNT);

INSERT INTO [TiposCuenta] ([Nombre], [Codigo], [CreadoEn], [CreadoPor])
SELECT v.[Nombre], v.[Codigo], SYSUTCDATETIME(), N'script'
  FROM @tipos v
 WHERE NOT EXISTS (SELECT 1 FROM [TiposCuenta] t WHERE t.[Nombre] = v.[Nombre]);
PRINT CONCAT(N'Tipos de cuenta agregados: ', @@ROWCOUNT);

COMMIT TRANSACTION;
GO

/* ---------- Resultado y pendientes ---------- */
SELECT [Id], [Nombre], [CodigoBanco] FROM [Bancos] ORDER BY [Id];
SELECT [Id], [Nombre], [Codigo]      FROM [TiposCuenta] ORDER BY [Id];

-- Pendientes (deben quedar vacíos):
SELECT N'Banco sin código' AS [Pendiente], [Nombre] FROM [Bancos] WHERE LTRIM(RTRIM([CodigoBanco])) = N''
UNION ALL
SELECT N'Tipo de cuenta sin código', [Nombre] FROM [TiposCuenta] WHERE LTRIM(RTRIM([Codigo])) = N'';

-- Revisar: dos bancos con el mismo código (la hoja de Finanzas tiene BANEFE y SANTANDER con 37).
SELECT N'Código repetido' AS [Revisar], b.[CodigoBanco],
       STUFF((SELECT N', ' + x.[Nombre] FROM [Bancos] x WHERE x.[CodigoBanco] = b.[CodigoBanco] ORDER BY x.[Id]
              FOR XML PATH(N''), TYPE).value(N'.', N'nvarchar(max)'), 1, 2, N'') AS [Bancos]
  FROM [Bancos] b WHERE LTRIM(RTRIM(b.[CodigoBanco])) <> N''
 GROUP BY b.[CodigoBanco] HAVING COUNT(*) > 1;
GO
