-- =====================================================================
-- Migración 036: Cartera Ordinaria — persistencia durable TIPADA del
-- resultado normalizado de MiDecisor por intento (M2.3b1).
--
-- Añade 7 columnas a dbo.cartera_solicitud_cupo_intentos:
--   fase_intento         VARCHAR(20) NOT NULL DEFAULT 'PRE_CALL'
--   con_informacion      BIT NULL
--   score_raw            VARCHAR(20) NULL
--   viabilidad_raw       VARCHAR(10) NULL
--   rating_recaudos_raw  VARCHAR(2)  NULL
--   monto_sugerido_raw   VARCHAR(20) NULL
--   alertas_count        INT NULL
--
-- Los campos *_raw guardan el string EXACTO que devuelve
-- IMiDecisorClient (MiDecisorResultado) — "-", "", "0", dígitos, NULL —
-- SIN convertir, SIN parsear, SIN interpretar. NO son los campos de
-- decisión de la solicitud (score_observado INT / monto_sugerido_observado
-- DECIMAL / estado_score / viabilidad_observada / rating_recaudos_observado),
-- que esta migración NO toca y quedan para una etapa posterior de motor de
-- política.
--
-- fase_intento (máquina de fases del intento):
--   PRE_CALL       — intento insertado PRE-CALL (solicitar-cupo).
--   ENVIO_INCIERTO — XPAY cruzó la frontera después de la cual NO puede
--                    hacer retry automático porque el proveedor puede o no
--                    haber sido contactado. NO significa "request enviado".
--   FINALIZADO     — el intento se completó (resultado_tecnico + fecha_fin).
--
-- Idempotente y fail-fast — mismo patrón que 032 (ADD COLUMN sobre tabla
-- poblada) y 035 (TRY/TRANSACTION, THROW ante estructura incompatible,
-- verificación final). NO borra datos, NO altera columnas existentes,
-- NO toca ninguna otra tabla, NO JSON, NO purge (eso es una migración
-- posterior).
--
-- Backfill: filas históricas (creadas por M2.3a) con resultado_tecnico
-- NOT NULL representan intentos ya finalizados → fase_intento = 'FINALIZADO'.
-- El resto queda 'PRE_CALL' (valor del DEFAULT aplicado por el ADD).
-- El orden ADD(DEFAULT) → backfill(WHERE resultado_tecnico IS NOT NULL AND
-- fase_intento <> 'FINALIZADO') hace la segunda ejecución un no-op seguro:
-- nunca revierte una fila FINALIZADO a PRE_CALL.
--
-- Compatibilidad con la re-verificación de 035: la verificación final de
-- 035 cuenta columnas de cartera_solicitud_cupo_intentos cuyo nombre está
-- en una lista fija de 11 nombres y exige == 11; añadir 7 columnas con
-- nombres DISTINTOS no cambia ese conteo. 035 sigue verde en su 2ª pasada.
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

    PRINT '=== INICIO MIGRACIÓN 036: intento — resultado normalizado de riesgo ===';

    DECLARE @objIdIntento INT = OBJECT_ID('dbo.cartera_solicitud_cupo_intentos', 'U');
    IF @objIdIntento IS NULL
        THROW 57000, N'Migración 036 abortada: dbo.cartera_solicitud_cupo_intentos no existe (falta migración 035). No se puede continuar.', 1;

    -- ── 1) fase_intento VARCHAR(20) NOT NULL DEFAULT 'PRE_CALL' ──────────
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdIntento AND name = 'fase_intento')
    BEGIN
        ALTER TABLE dbo.cartera_solicitud_cupo_intentos
            ADD fase_intento VARCHAR(20) NOT NULL CONSTRAINT df_cartera_intento_fase DEFAULT ('PRE_CALL');
        PRINT 'OK: columna fase_intento agregada (DEFAULT ''PRE_CALL'' aplicado a todas las filas existentes)';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdIntento AND c.name = 'fase_intento'
              AND ty.name = 'varchar' AND c.max_length = 20 AND c.is_nullable = 0
        )
            THROW 57001, N'Migración 036 abortada: fase_intento ya existe pero no es varchar(20) NOT NULL — revisar manualmente.', 1;
        PRINT 'INFO: fase_intento ya existe con la estructura esperada — omitida';
    END

    -- ── 2) con_informacion BIT NULL ────────────────────────────────────
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdIntento AND name = 'con_informacion')
    BEGIN
        ALTER TABLE dbo.cartera_solicitud_cupo_intentos ADD con_informacion BIT NULL;
        PRINT 'OK: columna con_informacion agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdIntento AND c.name = 'con_informacion' AND ty.name = 'bit' AND c.is_nullable = 1
        )
            THROW 57002, N'Migración 036 abortada: con_informacion ya existe pero no es bit NULL — revisar manualmente.', 1;
        PRINT 'INFO: con_informacion ya existe con la estructura esperada — omitida';
    END

    -- ── 3) score_raw VARCHAR(20) NULL ──────────────────────────────────
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdIntento AND name = 'score_raw')
    BEGIN
        ALTER TABLE dbo.cartera_solicitud_cupo_intentos ADD score_raw VARCHAR(20) NULL;
        PRINT 'OK: columna score_raw agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdIntento AND c.name = 'score_raw' AND ty.name = 'varchar' AND c.max_length = 20 AND c.is_nullable = 1
        )
            THROW 57003, N'Migración 036 abortada: score_raw ya existe pero no es varchar(20) NULL — revisar manualmente.', 1;
        PRINT 'INFO: score_raw ya existe con la estructura esperada — omitida';
    END

    -- ── 4) viabilidad_raw VARCHAR(10) NULL ─────────────────────────────
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdIntento AND name = 'viabilidad_raw')
    BEGIN
        ALTER TABLE dbo.cartera_solicitud_cupo_intentos ADD viabilidad_raw VARCHAR(10) NULL;
        PRINT 'OK: columna viabilidad_raw agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdIntento AND c.name = 'viabilidad_raw' AND ty.name = 'varchar' AND c.max_length = 10 AND c.is_nullable = 1
        )
            THROW 57004, N'Migración 036 abortada: viabilidad_raw ya existe pero no es varchar(10) NULL — revisar manualmente.', 1;
        PRINT 'INFO: viabilidad_raw ya existe con la estructura esperada — omitida';
    END

    -- ── 5) rating_recaudos_raw VARCHAR(2) NULL ─────────────────────────
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdIntento AND name = 'rating_recaudos_raw')
    BEGIN
        ALTER TABLE dbo.cartera_solicitud_cupo_intentos ADD rating_recaudos_raw VARCHAR(2) NULL;
        PRINT 'OK: columna rating_recaudos_raw agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdIntento AND c.name = 'rating_recaudos_raw' AND ty.name = 'varchar' AND c.max_length = 2 AND c.is_nullable = 1
        )
            THROW 57005, N'Migración 036 abortada: rating_recaudos_raw ya existe pero no es varchar(2) NULL — revisar manualmente.', 1;
        PRINT 'INFO: rating_recaudos_raw ya existe con la estructura esperada — omitida';
    END

    -- ── 6) monto_sugerido_raw VARCHAR(20) NULL ─────────────────────────
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdIntento AND name = 'monto_sugerido_raw')
    BEGIN
        ALTER TABLE dbo.cartera_solicitud_cupo_intentos ADD monto_sugerido_raw VARCHAR(20) NULL;
        PRINT 'OK: columna monto_sugerido_raw agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdIntento AND c.name = 'monto_sugerido_raw' AND ty.name = 'varchar' AND c.max_length = 20 AND c.is_nullable = 1
        )
            THROW 57006, N'Migración 036 abortada: monto_sugerido_raw ya existe pero no es varchar(20) NULL — revisar manualmente.', 1;
        PRINT 'INFO: monto_sugerido_raw ya existe con la estructura esperada — omitida';
    END

    -- ── 7) alertas_count INT NULL ─────────────────────────────────────
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdIntento AND name = 'alertas_count')
    BEGIN
        ALTER TABLE dbo.cartera_solicitud_cupo_intentos ADD alertas_count INT NULL;
        PRINT 'OK: columna alertas_count agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdIntento AND c.name = 'alertas_count' AND ty.name = 'int' AND c.is_nullable = 1
        )
            THROW 57007, N'Migración 036 abortada: alertas_count ya existe pero no es int NULL — revisar manualmente.', 1;
        PRINT 'INFO: alertas_count ya existe con la estructura esperada — omitida';
    END

    -- ── 8) Backfill de fase_intento para intentos históricos finalizados ─
    -- Sólo promueve filas con resultado_tecnico ya persistido y que aún no
    -- estén FINALIZADO. Nunca revierte FINALIZADO → PRE_CALL. Idempotente.
    UPDATE dbo.cartera_solicitud_cupo_intentos
       SET fase_intento = 'FINALIZADO'
     WHERE resultado_tecnico IS NOT NULL
       AND fase_intento <> 'FINALIZADO';
    PRINT CONCAT('OK: backfill fase_intento = FINALIZADO en ', @@ROWCOUNT, ' fila(s) histórica(s) con resultado.');

    -- ── 9) CHECK ck_cartera_intento_fase (tras el backfill) ────────────
    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = @objIdIntento AND name = 'ck_cartera_intento_fase'
    )
    BEGIN
        ALTER TABLE dbo.cartera_solicitud_cupo_intentos
            ADD CONSTRAINT ck_cartera_intento_fase
            CHECK (fase_intento IN ('PRE_CALL', 'ENVIO_INCIERTO', 'FINALIZADO'));
        PRINT 'OK: constraint ck_cartera_intento_fase agregada';
    END
    ELSE
        PRINT 'INFO: constraint ck_cartera_intento_fase ya existe — omitida';

    COMMIT TRANSACTION;
    PRINT '=== MIGRACIÓN 036 COMPLETADA ===';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT('MIGRACIÓN 036 ABORTADA: ', ERROR_MESSAGE());
    THROW;
END CATCH;
GO

-- ── Verificación final (idempotente, sólo lectura) ──────────────────────
DECLARE @objIdIntentoV INT = OBJECT_ID('dbo.cartera_solicitud_cupo_intentos', 'U');

DECLARE @colsNuevas INT = (
    SELECT COUNT(*) FROM sys.columns
    WHERE object_id = @objIdIntentoV AND name IN (
        'fase_intento', 'con_informacion', 'score_raw', 'viabilidad_raw',
        'rating_recaudos_raw', 'monto_sugerido_raw', 'alertas_count'
    )
);

DECLARE @faseNotNull INT = (
    SELECT CAST(1 - c.is_nullable AS INT) FROM sys.columns c
    WHERE c.object_id = @objIdIntentoV AND c.name = 'fase_intento'
);

DECLARE @checkFase INT = (
    SELECT COUNT(*) FROM sys.check_constraints
    WHERE parent_object_id = @objIdIntentoV AND name = 'ck_cartera_intento_fase'
);

DECLARE @faseInvalidas INT = (
    SELECT COUNT(*) FROM dbo.cartera_solicitud_cupo_intentos
    WHERE fase_intento NOT IN ('PRE_CALL', 'ENVIO_INCIERTO', 'FINALIZADO')
);

DECLARE @resultado036 NVARCHAR(60) =
    CASE WHEN @colsNuevas = 7 AND @faseNotNull = 1 AND @checkFase = 1 AND @faseInvalidas = 0
         THEN N'OK — migración 036 aplicada y verificada'
         ELSE N'REVISAR — algún componente de 036 no coincide con lo esperado'
    END;

SELECT
    @colsNuevas    AS columnas_nuevas_presentes,
    @faseNotNull   AS fase_intento_not_null,
    @checkFase     AS check_fase_presente,
    @faseInvalidas AS filas_con_fase_invalida,
    @resultado036  AS resultado;

PRINT '=== VERIFICACIÓN 036 COMPLETA ===';
