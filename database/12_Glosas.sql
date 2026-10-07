/* =====================================================================================
   IpsosPagoHonorarios · Glosas, ítems y cuentas contables
   Carga en bloque el catálogo de glosas: nombre de la glosa, ítem presupuestario y cuenta contable.
   (Una glosa suelta también se agrega en la aplicación: Maestros -> Jobs y glosas, con perfil Finanzas.)

   Cómo usarlo
     1. Pega tus glosas en la lista de abajo, una por línea:  (N'Nombre de la glosa', N'Ítem', N'Cuenta contable'),
        Con la hoja Formato de Finanzas en Excel, una fórmula arma cada línea (nombre en A, ítem en B, cuenta en C):
          ="(N'"&SUSTITUIR(A2;"'";"''")&"', N'"&B2&"', N'"&C2&"'),"        (en inglés: SUBSTITUTE)
     2. Ejecuta el script. El último renglón de la lista termina en ;  y los demás en ,

   Reglas (seguro de repetir; idempotente)
     - Una glosa nueva se agrega. Una glosa que ya existe NO se modifica, salvo completar su ítem o su cuenta si estaban vacíos.
     - Si una glosa existente tiene un ítem o cuenta DISTINTO al de la lista, solo se informa. Para corregirlos pon
       @ActualizarExistentes = 1: cambia el ítem y la cuenta de esa glosa en TODAS las planillas que la usan, también
       en las de ciclos anteriores. Hazlo solo si Finanzas confirma que el dato anterior estaba mal.
     - Se rechaza (y no se agrega ninguna) toda la lista si hay un nombre, ítem o cuenta vacíos o más largos que la columna
       (nombre 150, ítem 20, cuenta 20), o el mismo nombre repetido con datos distintos.

   IMPORTANTE: al importar una planilla, la aplicación compara el nombre de la glosa EXACTO (mayúsculas, tildes y espacios)
   con el que trae la columna Glosa. Escríbelo tal como está en la hoja Formato de Finanzas.
   ===================================================================================== */
USE [BD_PagoIpsos];
GO

-- sqlcmd trabaja por defecto con QUOTED_IDENTIFIER OFF; los índices filtrados (p. ej. los de Identity) lo exigen ON.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @ActualizarExistentes bit = 0;   -- 1 = corrige ítem y cuenta de las glosas que ya existen y difieren (ver arriba)

BEGIN TRANSACTION;

DECLARE @glosas TABLE ([Nombre] nvarchar(400) NOT NULL, [Item] nvarchar(100) NOT NULL, [Cuenta] nvarchar(100) NOT NULL);
INSERT INTO @glosas ([Nombre], [Item], [Cuenta]) VALUES
    (N'Honorarios entrevistadores',     N'1310', N'602101'),   -- ya existe
    (N'Honorarios supervisión',         N'1330', N'602101'),   -- ya existe
    (N'Honorario codificación externa', N'2830', N'602101');   -- ya existe
    -- Agrega las demás aquí: cambia el ; del último por una coma y escribe las líneas nuevas debajo.

-- Limpia espacios al inicio y al final
UPDATE @glosas SET [Nombre] = LTRIM(RTRIM([Nombre])), [Item] = LTRIM(RTRIM([Item])), [Cuenta] = LTRIM(RTRIM([Cuenta]));

-- Validación 1: vacíos y largos de columna
IF EXISTS (SELECT 1 FROM @glosas WHERE [Nombre] = N'' OR [Item] = N'' OR [Cuenta] = N''
                                   OR LEN([Nombre]) > 150 OR LEN([Item]) > 20 OR LEN([Cuenta]) > 20)
BEGIN
    DECLARE @malos nvarchar(max) = N'';
    SELECT @malos = @malos + CASE WHEN @malos = N'' THEN N'' ELSE N' | ' END + LEFT(CASE WHEN [Nombre] = N'' THEN N'(sin nombre)' ELSE [Nombre] END, 60)
      FROM @glosas WHERE [Nombre] = N'' OR [Item] = N'' OR [Cuenta] = N'' OR LEN([Nombre]) > 150 OR LEN([Item]) > 20 OR LEN([Cuenta]) > 20;
    ROLLBACK TRANSACTION;
    RAISERROR(N'No se agregó ninguna glosa. Hay datos vacíos o demasiado largos (nombre máx. 150, ítem y cuenta máx. 20): %s', 16, 1, @malos);
    RETURN;
END;

-- Validación 2: el mismo nombre repetido con ítem o cuenta distintos (no se sabe cuál es el correcto)
IF EXISTS (SELECT 1 FROM @glosas GROUP BY [Nombre] HAVING COUNT(DISTINCT [Item] + N'|' + [Cuenta]) > 1)
BEGIN
    DECLARE @amb nvarchar(max) = N'';
    SELECT @amb = @amb + CASE WHEN @amb = N'' THEN N'' ELSE N' | ' END + LEFT([Nombre], 60)
      FROM @glosas GROUP BY [Nombre] HAVING COUNT(DISTINCT [Item] + N'|' + [Cuenta]) > 1;
    ROLLBACK TRANSACTION;
    RAISERROR(N'No se agregó ninguna glosa. Nombre repetido con ítem o cuenta distintos: %s', 16, 1, @amb);
    RETURN;
END;

-- Una sola fila por nombre
DECLARE @unicas TABLE ([Nombre] nvarchar(150) NOT NULL, [Item] nvarchar(20) NOT NULL, [Cuenta] nvarchar(20) NOT NULL);
INSERT INTO @unicas SELECT DISTINCT [Nombre], [Item], [Cuenta] FROM @glosas;

-- Completa ítem o cuenta vacíos de glosas existentes
UPDATE g
   SET g.[Item]   = CASE WHEN LTRIM(RTRIM(g.[Item]))           = N'' THEN v.[Item]   ELSE g.[Item]   END,
       g.[CuentaContable] = CASE WHEN LTRIM(RTRIM(g.[CuentaContable])) = N'' THEN v.[Cuenta] ELSE g.[CuentaContable] END,
       g.[ModificadoEn] = SYSUTCDATETIME(), g.[ModificadoPor] = N'script'
  FROM [Glosas] g JOIN @unicas v ON v.[Nombre] = g.[NombreGlosa]
 WHERE LTRIM(RTRIM(g.[Item])) = N'' OR LTRIM(RTRIM(g.[CuentaContable])) = N'';
PRINT CONCAT(N'Glosas con ítem o cuenta completados: ', @@ROWCOUNT);

-- Corrige las que difieren, solo si se pidió
IF @ActualizarExistentes = 1
BEGIN
    UPDATE g
       SET g.[Item] = v.[Item], g.[CuentaContable] = v.[Cuenta], g.[ModificadoEn] = SYSUTCDATETIME(), g.[ModificadoPor] = N'script'
      FROM [Glosas] g JOIN @unicas v ON v.[Nombre] = g.[NombreGlosa]
     WHERE g.[Item] <> v.[Item] OR g.[CuentaContable] <> v.[Cuenta];
    PRINT CONCAT(N'Glosas corregidas (ítem o cuenta): ', @@ROWCOUNT);
END;

-- Agrega las que no existen (la comparación no distingue mayúsculas ni minúsculas)
INSERT INTO [Glosas] ([NombreGlosa], [Item], [CuentaContable], [CreadoEn], [CreadoPor])
SELECT v.[Nombre], v.[Item], v.[Cuenta], SYSUTCDATETIME(), N'script'
  FROM @unicas v
 WHERE NOT EXISTS (SELECT 1 FROM [Glosas] g WHERE g.[NombreGlosa] = v.[Nombre]);
PRINT CONCAT(N'Glosas agregadas: ', @@ROWCOUNT);

/* ---------- Avisos (no detienen la carga; solo aparecen si hay algo que revisar) ---------- */
-- Existen con otro ítem o cuenta y no se modificaron
IF EXISTS (SELECT 1 FROM [Glosas] g JOIN @unicas v ON v.[Nombre] = g.[NombreGlosa] WHERE g.[Item] <> v.[Item] OR g.[CuentaContable] <> v.[Cuenta])
    SELECT N'Difiere: no se modificó' AS [Revisar], g.[NombreGlosa],
           g.[Item] AS [Item en la base], v.[Item] AS [Item en la lista],
           g.[CuentaContable] AS [Cuenta en la base], v.[Cuenta] AS [Cuenta en la lista]
      FROM [Glosas] g JOIN @unicas v ON v.[Nombre] = g.[NombreGlosa]
     WHERE g.[Item] <> v.[Item] OR g.[CuentaContable] <> v.[Cuenta];

-- Mismo nombre salvo mayúsculas/minúsculas (contra la base o dentro de la lista): la aplicación los trata como glosas distintas
IF EXISTS (SELECT 1 FROM (SELECT [Nombre] AS [N] FROM @glosas UNION ALL SELECT [NombreGlosa] FROM [Glosas]) x
            GROUP BY x.[N] HAVING COUNT(DISTINCT x.[N] COLLATE Latin1_General_CS_AS) > 1)
    SELECT N'Mayúsculas distintas' AS [Revisar], MIN(x.[N] COLLATE Latin1_General_CS_AS) AS [Una forma], MAX(x.[N] COLLATE Latin1_General_CS_AS) AS [Otra forma]
      FROM (SELECT [Nombre] AS [N] FROM @glosas UNION ALL SELECT [NombreGlosa] FROM [Glosas]) x
     GROUP BY x.[N] HAVING COUNT(DISTINCT x.[N] COLLATE Latin1_General_CS_AS) > 1;

-- Ítem o cuenta con caracteres que no son dígitos (los actuales son numéricos: confirma que es correcto)
IF EXISTS (SELECT 1 FROM @unicas WHERE [Item] LIKE N'%[^0-9]%' OR [Cuenta] LIKE N'%[^0-9]%')
    SELECT N'No numérico' AS [Revisar], [Nombre], [Item], [Cuenta]
      FROM @unicas WHERE [Item] LIKE N'%[^0-9]%' OR [Cuenta] LIKE N'%[^0-9]%';

COMMIT TRANSACTION;
GO

/* ---------- Resultado ---------- */
SELECT [Id], [NombreGlosa], [Item], [CuentaContable] FROM [Glosas] ORDER BY [Item], [NombreGlosa];
GO
