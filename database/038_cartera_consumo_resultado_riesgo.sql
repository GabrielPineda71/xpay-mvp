-- =====================================================================
-- Migración 038: Cartera Ordinaria — consumo durable DORMIDO del resultado
-- MiDecisor (M2.4a). Normalización purga-segura del intento útil a
-- observaciones tipadas de la solicitud + marca de consumo por intento.
--
-- Añade 3 columnas:
--
--   dbo.cartera_solicitud_cupo_intentos
--     resultado_consumido_utc   DATETIME2 NULL
--
--   dbo.cartera_solicitudes_cupo
--     con_informacion_observado BIT NULL
--     alertas_count_observado   INT NULL
--
-- Semántica:
--   resultado_consumido_utc
--     NULL     = NO se ha completado un consumo durable del resultado del
--                intento.
--     NOT NULL = el resultado de ese intento se normalizó y se persistió
--                durablemente como observaciones de la solicitud
--                (con_informacion_observado / score_observado / estado_score /
--                viabilidad_observada / rating_recaudos_observado /
--                monto_sugerido_observado / alertas_count_observado) en ese
--                instante UTC. Inmutable una vez escrita. NO es fecha_decision
--                (no hay veredicto), NO inicia el reloj legal de retención, NO
--                autoriza la purga por sí sola.
--
--   con_informacion_observado / alertas_count_observado
--     Copia durable y purga-segura de intento.con_informacion e
--     intento.alertas_count en el momento del consumo. La purga de M2.3b3
--     pone NULL los 6 crudos del intento pero NUNCA toca
--     cartera_solicitudes_cupo → estas columnas sobreviven a la purga y
--     completan el snapshot que el futuro motor de decisión (M2.4b) leerá.
--
-- INFRAESTRUCTURA DORMIDA: estas columnas las escribe únicamente
-- CarteraConsultaRiesgoStore.ConsumirResultadoRiesgoAsync
-- (ICarteraResultadoRiesgoConsumo), que NO está registrada en DI, NO tiene
-- ningún caller de runtime (scheduler / job / endpoint / worker) y NO emite
-- veredicto crediticio. `decision_crediticia`, `fecha_decision`,
-- `monto_aprobado`, `estado_solicitud` NO cambian.
--
-- Idempotente y fail-fast — mismo patrón que 037 (ADD COLUMN sólo si falta;
-- si ya existe, verifica tipo/nullability y aborta con THROW ante
-- discrepancia). NO borra datos, NO altera columnas existentes, NO DEFAULT,
-- NO CHECK, NO índice, NO backfill.
--
-- Compatibilidad con re-verificaciones previas: la verificación final de 035
-- cuenta columnas de cartera_solicitudes_cupo contra una lista fija de 27
-- nombres y de cartera_solicitud_cupo_intentos contra 11 nombres; la de 036
-- cuenta 7 nombres; la de 037 cuenta resultado_purgado_utc. Ninguno de
-- resultado_consumido_utc / con_informacion_observado / alertas_count_observado
-- está en esas listas → todos los conteos previos no cambian y 035/036/037
-- siguen verdes en su 2ª pasada.
--
-- Las 3 columnas nuevas sólo aparecen como literal string en predicados sobre
-- sys.columns; NUNCA como identificador SQL dentro del batch (no hay backfill
-- ni CHECK que las referencie) → sin riesgo de Msg 207, sin necesidad de EXEC().
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

    PRINT '=== INICIO MIGRACIÓN 038: consumo durable dormido del resultado MiDecisor ===';

    DECLARE @objIdIntento INT = OBJECT_ID('dbo.cartera_solicitud_cupo_intentos', 'U');
    IF @objIdIntento IS NULL
        THROW 59000, N'Migración 038 abortada: dbo.cartera_solicitud_cupo_intentos no existe (falta migración 035). No se puede continuar.', 1;

    DECLARE @objIdSolicitud INT = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U');
    IF @objIdSolicitud IS NULL
        THROW 59000, N'Migración 038 abortada: dbo.cartera_solicitudes_cupo no existe (falta migración 035). No se puede continuar.', 1;

    -- ── intentos.resultado_consumido_utc DATETIME2 NULL ────────────────
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdIntento AND name = 'resultado_consumido_utc')
    BEGIN
        ALTER TABLE dbo.cartera_solicitud_cupo_intentos
            ADD resultado_consumido_utc DATETIME2 NULL;
        PRINT 'OK: columna resultado_consumido_utc agregada (NULL en todas las filas existentes)';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdIntento AND c.name = 'resultado_consumido_utc'
              AND ty.name = 'datetime2' AND c.is_nullable = 1
        )
            THROW 59001, N'Migración 038 abortada: resultado_consumido_utc ya existe pero no es datetime2 NULL — revisar manualmente.', 1;
        PRINT 'INFO: resultado_consumido_utc ya existe con la estructura esperada — omitida';
    END

    -- ── solicitudes.con_informacion_observado BIT NULL ────────────────
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'con_informacion_observado')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo
            ADD con_informacion_observado BIT NULL;
        PRINT 'OK: columna con_informacion_observado agregada (NULL en todas las filas existentes)';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'con_informacion_observado'
              AND ty.name = 'bit' AND c.is_nullable = 1
        )
            THROW 59002, N'Migración 038 abortada: con_informacion_observado ya existe pero no es bit NULL — revisar manualmente.', 1;
        PRINT 'INFO: con_informacion_observado ya existe con la estructura esperada — omitida';
    END

    -- ── solicitudes.alertas_count_observado INT NULL ─────────────────
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'alertas_count_observado')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo
            ADD alertas_count_observado INT NULL;
        PRINT 'OK: columna alertas_count_observado agregada (NULL en todas las filas existentes)';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'alertas_count_observado'
              AND ty.name = 'int' AND c.is_nullable = 1
        )
            THROW 59003, N'Migración 038 abortada: alertas_count_observado ya existe pero no es int NULL — revisar manualmente.', 1;
        PRINT 'INFO: alertas_count_observado ya existe con la estructura esperada — omitida';
    END

    COMMIT TRANSACTION;
    PRINT '=== MIGRACIÓN 038 COMPLETADA ===';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT('MIGRACIÓN 038 ABORTADA: ', ERROR_MESSAGE());
    THROW;
END CATCH;
GO

-- ── Verificación final (idempotente, sólo lectura) ──────────────────────
DECLARE @objIdIntentoV   INT = OBJECT_ID('dbo.cartera_solicitud_cupo_intentos', 'U');
DECLARE @objIdSolicitudV INT = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U');

DECLARE @consumidoOk INT = (
    SELECT CASE WHEN EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objIdIntentoV AND c.name = 'resultado_consumido_utc'
          AND ty.name = 'datetime2' AND c.is_nullable = 1
    ) THEN 1 ELSE 0 END
);

DECLARE @conInformacionOk INT = (
    SELECT CASE WHEN EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objIdSolicitudV AND c.name = 'con_informacion_observado'
          AND ty.name = 'bit' AND c.is_nullable = 1
    ) THEN 1 ELSE 0 END
);

DECLARE @alertasObsOk INT = (
    SELECT CASE WHEN EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objIdSolicitudV AND c.name = 'alertas_count_observado'
          AND ty.name = 'int' AND c.is_nullable = 1
    ) THEN 1 ELSE 0 END
);

DECLARE @resultado038 NVARCHAR(80) =
    CASE WHEN @consumidoOk = 1 AND @conInformacionOk = 1 AND @alertasObsOk = 1
         THEN N'OK — migración 038 aplicada y verificada'
         ELSE N'REVISAR — alguna columna de 038 no coincide con lo esperado'
    END;

SELECT
    @consumidoOk       AS resultado_consumido_utc_ok,
    @conInformacionOk  AS con_informacion_observado_ok,
    @alertasObsOk      AS alertas_count_observado_ok,
    @resultado038      AS resultado;

PRINT '=== VERIFICACIÓN 038 COMPLETA ===';
