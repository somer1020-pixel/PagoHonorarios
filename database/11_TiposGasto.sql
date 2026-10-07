/* =====================================================================================
   IpsosPagoHonorarios · Tipos de gasto
   Agrega al catálogo los tipos de gasto que faltan (los que se eligen al cargar la producción y los que
   aceptan las planillas en la columna "Tipo de Gasto").

   Cómo usarlo: escribe los nombres nuevos en la lista de abajo (uno por línea) y ejecuta el script.
     - Seguro de repetir (idempotente): lo que ya existe no se duplica ni se modifica.
     - No borra ni renombra nada.
     - Valida cada nombre: no puede estar vacío ni tener más de 40 caracteres. Si hay uno inválido no se agrega ninguno.

   IMPORTANTE: la aplicación compara el nombre EXACTO (mayúsculas, minúsculas y espacios) con lo que trae la
   planilla. Escríbelo tal como aparece en la hoja Formato de Finanzas.
   ===================================================================================== */
USE [BD_PagoIpsos];
GO

-- sqlcmd trabaja por defecto con QUOTED_IDENTIFIER OFF; los índices filtrados (p. ej. los de Identity) lo exigen ON.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

DECLARE @tipos TABLE ([Nombre] nvarchar(200) NOT NULL);
INSERT INTO @tipos ([Nombre]) VALUES
    (N'Costo Directo'),     -- ya existe
    (N'Payroll');           -- ya existe
    -- Agrega los nuevos aquí. Ejemplo: cambia la línea de arriba por  (N'Payroll'),  y escribe debajo  (N'Nombre del tipo')

-- Limpia espacios al inicio y al final
UPDATE @tipos SET [Nombre] = LTRIM(RTRIM([Nombre]));

-- Validación: sin vacíos ni más de 40 caracteres (largo de la columna)
IF EXISTS (SELECT 1 FROM @tipos WHERE [Nombre] = N'' OR LEN([Nombre]) > 40)
BEGIN
    DECLARE @malos nvarchar(max) = N'';
    SELECT @malos = @malos + CASE WHEN @malos = N'' THEN N'' ELSE N' | ' END + CASE WHEN [Nombre] = N'' THEN N'(vacío)' ELSE [Nombre] END
      FROM @tipos WHERE [Nombre] = N'' OR LEN([Nombre]) > 40;
    ROLLBACK TRANSACTION;
    RAISERROR(N'No se agregó ningún tipo de gasto. Nombre vacío o de más de 40 caracteres: %s', 16, 1, @malos);
    RETURN;
END;

-- Agrega los que no existen (la comparación no distingue mayúsculas ni minúsculas) y evita repetidos en la lista.
INSERT INTO [TiposGasto] ([Nombre], [CreadoEn], [CreadoPor])
SELECT v.[Nombre], SYSUTCDATETIME(), N'script'
  FROM (SELECT DISTINCT [Nombre] FROM @tipos) v
 WHERE NOT EXISTS (SELECT 1 FROM [TiposGasto] t WHERE t.[Nombre] = v.[Nombre]);
PRINT CONCAT(N'Tipos de gasto agregados: ', @@ROWCOUNT);

COMMIT TRANSACTION;
GO

/* ---------- Resultado ---------- */
SELECT [Id], [Nombre] FROM [TiposGasto] ORDER BY [Id];
GO
