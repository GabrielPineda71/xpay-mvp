-- =====================================================================
-- Migración 042: Cartera Ordinaria — marca durable de purga DORMIDA del
-- snapshot P0 (M2.4a extensión / XPAY-213/214). Política de retención B4
-- (XPAY-212, working copy §Q.8): 5 años desde fecha_fin de cada consulta,
-- alcance SCOPE_OPTION_C.
--
-- Añade 1 columna:
--
--   dbo.cartera_solicitudes_cupo
--     p0_raw_purgado_utc   DATETIME2 NULL
--
-- Semántica:
--   p0_raw_purgado_utc
--     NULL     = los 9 campos RAW/VERBATIM_PROVIDER del snapshot P0
--                (tipo_documento_observado, estado_documento_datos_basicos_raw,
--                estado_documento_info_demografica_raw,
--                rango_edad_datos_basicos_raw, rango_edad_info_demografica_raw,
--                consulta_anio_raw, consulta_mes_raw, consulta_dia_raw,
--                comportamiento_vector_json) NO han sido purgados
--                formalmente. Puede estar NULL tanto porque nunca hubo P0
--                materializado (p. ej. ERROR_PROVEEDOR antes de consumo) como
--                porque el plazo de retención aún no venció — esta columna NO
--                distingue esos casos por sí sola; los 9 campos sí lo hacen.
--     NOT NULL = una purga formal (NULL de los 9 campos anteriores) se aplicó
--                en ese instante UTC. Inmutable. Marca AUTORITATIVA de
--                idempotencia — igual criterio que resultado_purgado_utc de
--                cartera_solicitud_cupo_intentos (migración 037): NUNCA se
--                infiere "ya purgado" sólo de que los campos estén en NULL.
--
-- NO purga (permanecen siempre, sin relación con esta columna):
--   estado_documento_captura, rango_edad_captura, comportamiento_vector_count
--   (metadato de captura derivado de XPAY, no dato del proveedor — XPAY-211/212)
--   ni decision_crediticia / monto_aprobado / codigo_motivo_decision /
--   fecha_decision / id_cupo_ordinario / fecha_materializacion_cupo.
--
-- INFRAESTRUCTURA DORMIDA: esta columna la escribirá únicamente la extensión
-- B4 de CarteraConsultaRiesgoStore (XPAY-214), que NO está registrada como
-- scheduler/worker automático y NO ejecuta ninguna purga real por sí sola —
-- requiere invocación explícita (endpoint admin manual) hoy sin invocar.
--
-- Idempotente y fail-fast — mismo patrón que 037/038 (ADD COLUMN sólo si
-- falta; si ya existe, verifica tipo/nullability y aborta con THROW ante
-- discrepancia). NO borra datos, NO altera columnas existentes, NO DEFAULT,
-- NO CHECK, NO índice, NO backfill.
--
-- Compatibilidad con re-verificaciones previas: las verificaciones finales de
-- 035/036/037/038/039/040/041 cuentan columnas contra listas fijas de nombres
-- que NO incluyen p0_raw_purgado_utc → ninguna de ellas cambia de resultado.
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

    PRINT '=== INICIO MIGRACIÓN 042: marca durable de purga del snapshot P0 (DORMIDA) ===';

    DECLARE @objIdSolicitud INT = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U');
    IF @objIdSolicitud IS NULL
        THROW 59400, N'Migración 042 abortada: dbo.cartera_solicitudes_cupo no existe (falta migración 035). No se puede continuar.', 1;

    -- ── solicitudes.p0_raw_purgado_utc DATETIME2 NULL ──────────────────
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'p0_raw_purgado_utc')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo
            ADD p0_raw_purgado_utc DATETIME2 NULL;
        PRINT 'OK: columna p0_raw_purgado_utc agregada (NULL en todas las filas existentes)';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'p0_raw_purgado_utc'
              AND ty.name = 'datetime2' AND c.is_nullable = 1
        )
            THROW 59401, N'Migración 042 abortada: p0_raw_purgado_utc ya existe pero no es datetime2 NULL — revisar manualmente.', 1;
        PRINT 'INFO: p0_raw_purgado_utc ya existe con la estructura esperada — omitida';
    END

    COMMIT TRANSACTION;
    PRINT '=== MIGRACIÓN 042 COMPLETADA ===';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT('MIGRACIÓN 042 ABORTADA: ', ERROR_MESSAGE());
    THROW;
END CATCH;
GO

-- ── Verificación final (idempotente, sólo lectura) ──────────────────────
DECLARE @objIdSolicitudV INT = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U');

DECLARE @p0PurgadoOk INT = (
    SELECT CASE WHEN EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objIdSolicitudV AND c.name = 'p0_raw_purgado_utc'
          AND ty.name = 'datetime2' AND c.is_nullable = 1
    ) THEN 1 ELSE 0 END
);

DECLARE @resultado042 NVARCHAR(80) =
    CASE WHEN @p0PurgadoOk = 1
         THEN N'OK — migración 042 aplicada y verificada'
         ELSE N'REVISAR — p0_raw_purgado_utc no coincide con lo esperado'
    END;

SELECT
    @p0PurgadoOk   AS p0_raw_purgado_utc_ok,
    @resultado042  AS resultado;

PRINT '=== VERIFICACIÓN 042 COMPLETA ===';
