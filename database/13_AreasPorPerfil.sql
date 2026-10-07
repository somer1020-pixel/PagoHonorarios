/* =====================================================================================
   IpsosPagoHonorarios · Áreas por perfil
   Carga en bloque las áreas con su código y el perfil al que pertenecen. Un usuario de un perfil solo puede
   gestionar (y ver las planillas de) las áreas de ese perfil. (Una sola área también se agrega en la aplicación:
   Maestros -> Jobs y glosas -> Nueva área, con perfil Finanzas.)

   Perfiles válidos (escríbelos exactamente así):  Operaciones | CEX | Public | BHT | MSU | AUM

   Cómo usarlo
     1. Pega tus áreas en la lista de abajo, una por línea:  (N'Nombre del área', N'Código', N'Perfil'),
        Con la hoja Formato de Finanzas en Excel (nombre en A, código en B, perfil en C) una fórmula arma cada línea:
          ="(N'"&SUSTITUIR(A2;"'";"''")&"', N'"&B2&"', N'"&C2&"'),"        (en inglés: SUBSTITUTE)
     2. Ejecuta el script. La última línea de la lista termina en ;  y las demás en ,
     3. Después asigna las áreas a cada usuario en la aplicación (Maestros -> Usuarios): sin áreas no ve planillas.

   Reglas (seguro de repetir; idempotente)
     - Un área nueva se agrega. Un área que ya existe NO se modifica, salvo completar su código si estaba vacío.
     - Si un área existente tiene otro código o perfil que el de la lista, solo se informa. Para corregirlos pon
       @ActualizarExistentes = 1. OJO: al cambiar el perfil de un área, los usuarios que ya la tienen asignada y son de
       otro perfil la CONSERVAN (siguen viendo sus planillas) hasta que se les quite en Maestros -> Usuarios. El script
       los lista al final para que los revises.
     - Se rechaza (y no se agrega ninguna) toda la lista si hay nombre o código vacíos o más largos que la columna
       (nombre 100, código 20), un perfil que no es uno de los válidos, o el mismo nombre con datos distintos.

   IMPORTANTE: el nombre del área debe ser EXACTO (mayúsculas, tildes y espacios) al que trae la planilla de producción.
   ===================================================================================== */
USE [BD_PagoIpsos];
GO

-- sqlcmd trabaja por defecto con QUOTED_IDENTIFIER OFF; los índices filtrados (p. ej. los de Identity) lo exigen ON.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @ActualizarExistentes bit = 0;   -- 1 = corrige código y perfil de las áreas que ya existen y difieren (ver arriba)

BEGIN TRANSACTION;

DECLARE @areas TABLE ([Nombre] nvarchar(400) NOT NULL, [Codigo] nvarchar(100) NOT NULL, [Perfil] nvarchar(100) NOT NULL);
INSERT INTO @areas ([Nombre], [Codigo], [Perfil]) VALUES
    (N'FACE TO FACE',      N'21183', N'Operaciones'),   -- ya existe
    (N'MYSTERY SHOPPING',  N'21187', N'Operaciones'),   -- ya existe
    (N'DATA PROCESSING',   N'21184', N'Operaciones'),   -- ya existe
    (N'Operations CATI',   N'21161', N'Operaciones');   -- ya existe
    -- Agrega las demás aquí: cambia el ; del último por una coma y escribe las líneas nuevas debajo. Ejemplo:
    --   (N'Nombre del área', N'21190', N'CEX'),

-- Limpia espacios al inicio y al final
UPDATE @areas SET [Nombre] = LTRIM(RTRIM([Nombre])), [Codigo] = LTRIM(RTRIM([Codigo])), [Perfil] = LTRIM(RTRIM([Perfil]));

-- Validación 1: vacíos, largos de columna y perfiles válidos (el perfil se compara exacto, con mayúsculas)
IF EXISTS (SELECT 1 FROM @areas WHERE [Nombre] = N'' OR [Codigo] = N'' OR LEN([Nombre]) > 100 OR LEN([Codigo]) > 20
                                  OR [Perfil] COLLATE Latin1_General_CS_AS NOT IN (N'Operaciones', N'CEX', N'Public', N'BHT', N'MSU', N'AUM'))
BEGIN
    DECLARE @malos nvarchar(max) = N'';
    SELECT @malos = @malos + CASE WHEN @malos = N'' THEN N'' ELSE N' | ' END
                  + LEFT(CASE WHEN [Nombre] = N'' THEN N'(sin nombre)' ELSE [Nombre] END, 50) + N' [perfil: ' + LEFT([Perfil], 20) + N']'
      FROM @areas WHERE [Nombre] = N'' OR [Codigo] = N'' OR LEN([Nombre]) > 100 OR LEN([Codigo]) > 20
                     OR [Perfil] COLLATE Latin1_General_CS_AS NOT IN (N'Operaciones', N'CEX', N'Public', N'BHT', N'MSU', N'AUM');
    ROLLBACK TRANSACTION;
    RAISERROR(N'No se agregó ninguna área. Hay nombre o código vacíos o muy largos (nombre máx. 100, código máx. 20) o un perfil inválido (válidos: Operaciones, CEX, Public, BHT, MSU, AUM): %s', 16, 1, @malos);
    RETURN;
END;

-- Validación 2: el mismo nombre repetido con código o perfil distintos (no se sabe cuál es el correcto)
IF EXISTS (SELECT 1 FROM @areas GROUP BY [Nombre] HAVING COUNT(DISTINCT [Codigo] + N'|' + [Perfil]) > 1)
BEGIN
    DECLARE @amb nvarchar(max) = N'';
    SELECT @amb = @amb + CASE WHEN @amb = N'' THEN N'' ELSE N' | ' END + LEFT([Nombre], 60)
      FROM @areas GROUP BY [Nombre] HAVING COUNT(DISTINCT [Codigo] + N'|' + [Perfil]) > 1;
    ROLLBACK TRANSACTION;
    RAISERROR(N'No se agregó ninguna área. Nombre repetido con código o perfil distintos: %s', 16, 1, @amb);
    RETURN;
END;

-- Una sola fila por nombre
DECLARE @unicas TABLE ([Nombre] nvarchar(100) NOT NULL, [Codigo] nvarchar(20) NOT NULL, [Perfil] nvarchar(40) NOT NULL);
INSERT INTO @unicas SELECT DISTINCT [Nombre], [Codigo], [Perfil] FROM @areas;

-- Completa el código vacío de áreas existentes
UPDATE a
   SET a.[CodigoArea] = v.[Codigo], a.[ModificadoEn] = SYSUTCDATETIME(), a.[ModificadoPor] = N'script'
  FROM [Areas] a JOIN @unicas v ON v.[Nombre] = a.[Nombre]
 WHERE LTRIM(RTRIM(a.[CodigoArea])) = N'';
PRINT CONCAT(N'Áreas con código completado: ', @@ROWCOUNT);

-- Corrige código y perfil de las que difieren, solo si se pidió
IF @ActualizarExistentes = 1
BEGIN
    UPDATE a
       SET a.[CodigoArea] = v.[Codigo], a.[Perfil] = v.[Perfil], a.[ModificadoEn] = SYSUTCDATETIME(), a.[ModificadoPor] = N'script'
      FROM [Areas] a JOIN @unicas v ON v.[Nombre] = a.[Nombre]
     WHERE a.[CodigoArea] <> v.[Codigo] OR a.[Perfil] COLLATE Latin1_General_CS_AS <> v.[Perfil];
    PRINT CONCAT(N'Áreas corregidas (código o perfil): ', @@ROWCOUNT);
END;

-- Agrega las que no existen (la comparación no distingue mayúsculas ni minúsculas)
INSERT INTO [Areas] ([Nombre], [CodigoArea], [Perfil], [CreadoEn], [CreadoPor])
SELECT v.[Nombre], v.[Codigo], v.[Perfil], SYSUTCDATETIME(), N'script'
  FROM @unicas v
 WHERE NOT EXISTS (SELECT 1 FROM [Areas] a WHERE a.[Nombre] = v.[Nombre]);
PRINT CONCAT(N'Áreas agregadas: ', @@ROWCOUNT);

/* ---------- Avisos (no detienen la carga; solo aparecen si hay algo que revisar) ---------- */
-- Existen con otro código o perfil y no se modificaron
IF EXISTS (SELECT 1 FROM [Areas] a JOIN @unicas v ON v.[Nombre] = a.[Nombre]
            WHERE a.[CodigoArea] <> v.[Codigo] OR a.[Perfil] COLLATE Latin1_General_CS_AS <> v.[Perfil])
    SELECT N'Difiere: no se modificó' AS [Revisar], a.[Nombre],
           a.[CodigoArea] AS [Código en la base], v.[Codigo] AS [Código en la lista],
           a.[Perfil] AS [Perfil en la base], v.[Perfil] AS [Perfil en la lista]
      FROM [Areas] a JOIN @unicas v ON v.[Nombre] = a.[Nombre]
     WHERE a.[CodigoArea] <> v.[Codigo] OR a.[Perfil] COLLATE Latin1_General_CS_AS <> v.[Perfil];

-- Mismo nombre salvo mayúsculas/minúsculas (contra la base o dentro de la lista): la aplicación los trata como áreas distintas
IF EXISTS (SELECT 1 FROM (SELECT [Nombre] AS [N] FROM @areas UNION ALL SELECT [Nombre] FROM [Areas]) x
            GROUP BY x.[N] HAVING COUNT(DISTINCT x.[N] COLLATE Latin1_General_CS_AS) > 1)
    SELECT N'Mayúsculas distintas' AS [Revisar], MIN(x.[N] COLLATE Latin1_General_CS_AS) AS [Una forma], MAX(x.[N] COLLATE Latin1_General_CS_AS) AS [Otra forma]
      FROM (SELECT [Nombre] AS [N] FROM @areas UNION ALL SELECT [Nombre] FROM [Areas]) x
     GROUP BY x.[N] HAVING COUNT(DISTINCT x.[N] COLLATE Latin1_General_CS_AS) > 1;

-- Usuarios que tienen asignada un área de OTRO perfil (conservan acceso a sus planillas hasta que se la quiten)
IF EXISTS (SELECT 1 FROM [UsuarioAreas] ua
             JOIN [Areas] a ON a.[Id] = ua.[AreaId]
             JOIN [AspNetUsers] u ON u.[Id] = ua.[UsuarioId]
             JOIN [AspNetUserRoles] ur ON ur.[UserId] = u.[Id]
             JOIN [AspNetRoles] r ON r.[Id] = ur.[RoleId]
            WHERE r.[Name] IN (N'Operaciones', N'CEX', N'Public', N'BHT', N'MSU', N'AUM') AND r.[Name] COLLATE Latin1_General_CS_AS <> a.[Perfil] COLLATE Latin1_General_CS_AS)
    SELECT N'Usuario con área de otro perfil' AS [Revisar], u.[UserName] AS [Usuario], r.[Name] AS [Perfil del usuario], a.[Nombre] AS [Área], a.[Perfil] AS [Perfil del área]
      FROM [UsuarioAreas] ua
      JOIN [Areas] a ON a.[Id] = ua.[AreaId]
      JOIN [AspNetUsers] u ON u.[Id] = ua.[UsuarioId]
      JOIN [AspNetUserRoles] ur ON ur.[UserId] = u.[Id]
      JOIN [AspNetRoles] r ON r.[Id] = ur.[RoleId]
     WHERE r.[Name] IN (N'Operaciones', N'CEX', N'Public', N'BHT', N'MSU', N'AUM') AND r.[Name] COLLATE Latin1_General_CS_AS <> a.[Perfil] COLLATE Latin1_General_CS_AS
     ORDER BY u.[UserName], a.[Nombre];

COMMIT TRANSACTION;
GO

/* ---------- Resultado: áreas por perfil ---------- */
SELECT [Perfil], [Nombre], [CodigoArea] AS [Código], [Id] FROM [Areas] ORDER BY [Perfil], [Nombre];
SELECT [Perfil], COUNT(*) AS [Áreas] FROM [Areas] GROUP BY [Perfil] ORDER BY [Perfil];
-- Áreas sin ningún usuario asignado (nadie las ve hasta asignarlas en Maestros -> Usuarios)
IF EXISTS (SELECT 1 FROM [Areas] a WHERE NOT EXISTS (SELECT 1 FROM [UsuarioAreas] ua WHERE ua.[AreaId] = a.[Id]))
    SELECT N'Área sin usuarios asignados' AS [Recuerda], a.[Perfil], a.[Nombre]
      FROM [Areas] a WHERE NOT EXISTS (SELECT 1 FROM [UsuarioAreas] ua WHERE ua.[AreaId] = a.[Id]) ORDER BY a.[Perfil], a.[Nombre];
GO
