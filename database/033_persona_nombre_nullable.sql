-- =====================================================================
-- Migración 033: Persona — nullability de primer_nombre/primer_apellido
-- (Commit 3, registro-inicial — Motor de Evaluación de Crédito
-- Datacrédito / onboarding móvil, Fase 0)
--
-- Idempotente y fail-fast — no borra datos, no transforma valores
-- existentes, no hace backfill, no toca ninguna otra columna, tabla,
-- índice, FK ni constraint. Toca EXCLUSIVAMENTE dbo.personas:
--   1) primer_nombre    VARCHAR(100) NOT NULL -> VARCHAR(100) NULL
--   2) primer_apellido  VARCHAR(100) NOT NULL -> VARCHAR(100) NULL
--
-- Motivo: la Persona-cascarón creada por POST /api/usuarios/registro-inicial
-- (Commit 3, todavía no implementado) solo recibe usuario+clave+celular —
-- no hay ningún nombre/apellido real disponible en esa etapa, y está
-- explícitamente prohibido rellenar estas columnas con placeholders
-- ("N/A", "PENDIENTE", username, etc.). Precheck confirmado antes de esta
-- migración: ningún índice, CHECK ni FK de dbo.personas depende de
-- primer_nombre/primer_apellido (los únicos índices reales son la PK y
-- IX_personas_celular/IX_personas_documento/IX_personas_email, ninguno
-- las involucra) — sin bloqueo estructural para este cambio.
--
-- Separación de dominios (sin cambios respecto a decisiones previas):
-- esta migración NO agrega columnas, NO crea reglas de Datacrédito, NO
-- sincroniza NombreVerificadoCompleto/ApellidoVerificadoCompleto hacia
-- primer_nombre/primer_apellido (esa sincronización, si se decide hacer,
-- pertenece al Commit 4, no a este).
--
-- Comportamiento:
--   1) Para cada columna: valida que el tipo/longitud actual sea
--      exactamente VARCHAR(100). Si no coincide: THROW — no se repara en
--      silencio.
--   2) Si la columna es NOT NULL: ALTER COLUMN a NULL, preservando todos
--      los valores existentes (ALTER COLUMN no transforma datos, solo
--      cambia la restricción de nullability).
--   3) Si la columna ya es NULL (mismo tipo/longitud): se omite sin error
--      — estado idempotente válido. Permite reejecutar el script tantas
--      veces como sea necesario sin fallar ni volver a alterar.
--   4) Cada columna se evalúa de forma independiente: si una ya está
--      migrada y la otra no, solo se ajusta la pendiente.
--
-- Esta migración NO crea: columnas nuevas, índices nuevos, backfill de
-- ningún tipo, ni ningún UPDATE/INSERT/DELETE sobre datos existentes.
--
-- NO EJECUTADA POR EL AGENTE — preparada para revisión y ejecución manual
-- del usuario.
-- =====================================================================

SET XACT_ABORT ON;

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT '=== INICIO MIGRACIÓN 033: Persona — primer_nombre/primer_apellido nullable ===';

    DECLARE @objIdPersonas INT = OBJECT_ID('dbo.personas', 'U');
    IF @objIdPersonas IS NULL
        THROW 54000, N'Migración 033 abortada: dbo.personas no existe. No se puede continuar.', 1;

    -- ═══════════════════════════════════════════════════════════════════
    -- 1) primer_nombre: NOT NULL -> NULL (mismo tipo/longitud, sin tocar datos)
    -- ═══════════════════════════════════════════════════════════════════
    DECLARE @pnNullable BIT, @pnTipo SYSNAME, @pnLen SMALLINT;
    SELECT @pnNullable = c.is_nullable, @pnTipo = ty.name, @pnLen = c.max_length
    FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
    WHERE c.object_id = @objIdPersonas AND c.name = 'primer_nombre';

    IF @pnTipo IS NULL
        THROW 54001, N'Migración 033 abortada: dbo.personas.primer_nombre no existe — estructura inesperada.', 1;
    ELSE IF @pnTipo <> 'varchar' OR @pnLen <> 100
        THROW 54002, N'Migración 033 abortada: dbo.personas.primer_nombre no es varchar(100) — revisar manualmente antes de continuar.', 1;
    ELSE IF @pnNullable = 0
    BEGIN
        ALTER TABLE dbo.personas ALTER COLUMN primer_nombre VARCHAR(100) NULL;
        PRINT 'OK: dbo.personas.primer_nombre alterado a NULL (valores existentes preservados)';
    END
    ELSE
        PRINT 'INFO: dbo.personas.primer_nombre ya es NULL — omitido';

    -- ═══════════════════════════════════════════════════════════════════
    -- 2) primer_apellido: NOT NULL -> NULL (mismo tipo/longitud, sin tocar datos)
    -- ═══════════════════════════════════════════════════════════════════
    DECLARE @paNullable BIT, @paTipo SYSNAME, @paLen SMALLINT;
    SELECT @paNullable = c.is_nullable, @paTipo = ty.name, @paLen = c.max_length
    FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
    WHERE c.object_id = @objIdPersonas AND c.name = 'primer_apellido';

    IF @paTipo IS NULL
        THROW 54003, N'Migración 033 abortada: dbo.personas.primer_apellido no existe — estructura inesperada.', 1;
    ELSE IF @paTipo <> 'varchar' OR @paLen <> 100
        THROW 54004, N'Migración 033 abortada: dbo.personas.primer_apellido no es varchar(100) — revisar manualmente antes de continuar.', 1;
    ELSE IF @paNullable = 0
    BEGIN
        ALTER TABLE dbo.personas ALTER COLUMN primer_apellido VARCHAR(100) NULL;
        PRINT 'OK: dbo.personas.primer_apellido alterado a NULL (valores existentes preservados)';
    END
    ELSE
        PRINT 'INFO: dbo.personas.primer_apellido ya es NULL — omitido';

    COMMIT TRANSACTION;
    PRINT '=== FIN MIGRACIÓN 033 (COMMIT OK) ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '=== ERROR EN MIGRACIÓN 033 — ROLLBACK EJECUTADO ===';
    THROW;
END CATCH;

-- ── Verificación final ───────────────────────────────────────────────────
-- Banderas booleanas independientes + conteos — confirmación legible para
-- quien ejecute el script, no el mecanismo de aplicación de la política
-- (eso ya ocurrió vía THROW dentro de la transacción). primer_nombre y
-- primer_apellido ya existían antes de esta migración (no son columnas
-- nuevas), así que no aplica aquí el problema de enlace prematuro de
-- nombres resuelto en 032 (sp_executesql) — se pueden referenciar
-- directamente como columnas reales sin riesgo de "Invalid column name".

DECLARE @objIdFinal INT = OBJECT_ID('dbo.personas', 'U');

SELECT
    CASE WHEN @objIdFinal IS NOT NULL THEN 1 ELSE 0 END AS tabla_personas_ok,
    CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdFinal AND name = 'primer_nombre') THEN 1 ELSE 0 END AS primer_nombre_existe,
    CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdFinal AND name = 'primer_apellido') THEN 1 ELSE 0 END AS primer_apellido_existe,
    CASE WHEN (
        SELECT COUNT(*) FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objIdFinal AND c.name IN ('primer_nombre','primer_apellido')
          AND ty.name = 'varchar' AND c.max_length = 100
    ) = 2 THEN 1 ELSE 0 END AS ambas_varchar_100_ok,
    CASE WHEN (
        SELECT COUNT(*) FROM sys.columns
        WHERE object_id = @objIdFinal AND name IN ('primer_nombre','primer_apellido') AND is_nullable = 1
    ) = 2 THEN 1 ELSE 0 END AS ambas_nullable_ok,
    (SELECT COUNT(*) FROM dbo.personas) AS total_personas,
    (SELECT COUNT(*) FROM dbo.personas WHERE primer_nombre IS NOT NULL) AS personas_con_primer_nombre_preservado,
    (SELECT COUNT(*) FROM dbo.personas WHERE primer_apellido IS NOT NULL) AS personas_con_primer_apellido_preservado,
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id = @objIdFinal) AS total_indices_sin_cambio,
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = @objIdFinal OR referenced_object_id = @objIdFinal) AS total_fks_sin_cambio,
    CASE WHEN
        @objIdFinal IS NOT NULL
        AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdFinal AND name = 'primer_nombre')
        AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdFinal AND name = 'primer_apellido')
        AND (SELECT COUNT(*) FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
             WHERE c.object_id = @objIdFinal AND c.name IN ('primer_nombre','primer_apellido')
               AND ty.name = 'varchar' AND c.max_length = 100) = 2
        AND (SELECT COUNT(*) FROM sys.columns
             WHERE object_id = @objIdFinal AND name IN ('primer_nombre','primer_apellido') AND is_nullable = 1) = 2
    THEN N'OK — primer_nombre y primer_apellido son VARCHAR(100) NULL, sin pérdida de datos'
    ELSE N'REVISAR — algún conteo no coincide con el esperado; ver columnas anteriores'
    END AS resultado;

SELECT c.name AS columna, ty.name AS tipo, c.max_length, c.is_nullable
FROM sys.columns c
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE c.object_id = @objIdFinal
  AND c.name IN ('primer_nombre','primer_apellido')
ORDER BY c.column_id;

-- Muestra id_persona/primer_nombre/primer_apellido de las filas existentes,
-- para confirmar visualmente que los valores históricos no cambiaron.
SELECT id_persona, primer_nombre, primer_apellido
FROM dbo.personas
ORDER BY id_persona;

PRINT '=== VERIFICACIÓN COMPLETA ===';
