-- =====================================================================
-- Migración 037: Cartera Ordinaria — marca de auditoría de PURGA de los
-- datos crudos de MiDecisor por intento (M2.3b3 — infraestructura DORMIDA).
--
-- Añade UNA columna a dbo.cartera_solicitud_cupo_intentos:
--   resultado_purgado_utc  DATETIME2 NULL
--
-- Semántica:
--   NULL     = NO se ha aplicado una operación formal de purga al intento.
--   NOT NULL = una operación formal de purga (NULL de los 6 campos crudos
--              con_informacion/score_raw/viabilidad_raw/rating_recaudos_raw/
--              monto_sugerido_raw/alertas_count) se aplicó con éxito en ese
--              instante UTC. Inmutable una vez escrita.
--
-- Un intento cuyos crudos ya son NULL porque nunca recibió resultado
-- (rechazo / error de auth/config/protocolo/transporte / desbordamiento /
-- cancelación) NO se considera "purgado": resultado_purgado_utc queda NULL
-- salvo que una purga formal se ejecute contra él (y para esos intentos la
-- purga es un no-op: nada que purgar → NoElegible).
--
-- INFRAESTRUCTURA DORMIDA: esta columna la escribe únicamente
-- CarteraConsultaRiesgoStore.PurgarResultadoIntentoAsync
-- (ICarteraResultadoRiesgoPurga), que NO está registrada en DI, NO tiene
-- ningún caller de runtime (scheduler / job / endpoint / worker) y NO define
-- período de retención. Activar la purga operativa requiere decisiones
-- externas (duración de retención, evento de inicio del conteo, gate durable
-- de consumo por el motor de decisión, invocador autorizado).
--
-- Idempotente y fail-fast — mismo patrón que 032 (ADD COLUMN sobre tabla
-- poblada) y 036 (TRY/TRANSACTION, THROW ante estructura incompatible,
-- verificación final). NO borra datos, NO altera columnas existentes,
-- NO toca ninguna otra tabla, NO DEFAULT, NO CHECK, NO índice, NO backfill.
--
-- Compatibilidad con re-verificaciones previas: la verificación final de 035
-- cuenta columnas de cartera_solicitud_cupo_intentos contra una lista fija
-- de 11 nombres (== 11); la de 036 cuenta 7 nombres. `resultado_purgado_utc`
-- no está en ninguna de las dos listas → ambos conteos no cambian y 035/036
-- siguen verdes en su 2ª pasada.
--
-- resultado_purgado_utc sólo aparece como literal string en predicados sobre
-- sys.columns; NUNCA como identificador SQL dentro del batch (no hay backfill
-- ni CHECK que lo referencie) → sin riesgo de Msg 207, sin necesidad de EXEC().
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

    PRINT '=== INICIO MIGRACIÓN 037: intento — marca de purga de datos crudos ===';

    DECLARE @objIdIntento INT = OBJECT_ID('dbo.cartera_solicitud_cupo_intentos', 'U');
    IF @objIdIntento IS NULL
        THROW 58000, N'Migración 037 abortada: dbo.cartera_solicitud_cupo_intentos no existe (falta migración 035). No se puede continuar.', 1;

    -- ── resultado_purgado_utc DATETIME2 NULL ───────────────────────────
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdIntento AND name = 'resultado_purgado_utc')
    BEGIN
        ALTER TABLE dbo.cartera_solicitud_cupo_intentos
            ADD resultado_purgado_utc DATETIME2 NULL;
        PRINT 'OK: columna resultado_purgado_utc agregada (NULL en todas las filas existentes)';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdIntento AND c.name = 'resultado_purgado_utc'
              AND ty.name = 'datetime2' AND c.is_nullable = 1
        )
            THROW 58001, N'Migración 037 abortada: resultado_purgado_utc ya existe pero no es datetime2 NULL — revisar manualmente.', 1;
        PRINT 'INFO: resultado_purgado_utc ya existe con la estructura esperada — omitida';
    END

    COMMIT TRANSACTION;
    PRINT '=== MIGRACIÓN 037 COMPLETADA ===';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT('MIGRACIÓN 037 ABORTADA: ', ERROR_MESSAGE());
    THROW;
END CATCH;
GO

-- ── Verificación final (idempotente, sólo lectura) ──────────────────────
DECLARE @objIdIntentoV INT = OBJECT_ID('dbo.cartera_solicitud_cupo_intentos', 'U');

DECLARE @colPresente INT = (
    SELECT COUNT(*) FROM sys.columns
    WHERE object_id = @objIdIntentoV AND name = 'resultado_purgado_utc'
);

DECLARE @tipoOk INT = (
    SELECT CASE WHEN EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objIdIntentoV AND c.name = 'resultado_purgado_utc'
          AND ty.name = 'datetime2' AND c.is_nullable = 1
    ) THEN 1 ELSE 0 END
);

DECLARE @resultado037 NVARCHAR(60) =
    CASE WHEN @colPresente = 1 AND @tipoOk = 1
         THEN N'OK — migración 037 aplicada y verificada'
         ELSE N'REVISAR — resultado_purgado_utc no coincide con lo esperado'
    END;

SELECT
    @colPresente  AS columna_presente,
    @tipoOk       AS tipo_datetime2_nullable_ok,
    @resultado037 AS resultado;

PRINT '=== VERIFICACIÓN 037 COMPLETA ===';
