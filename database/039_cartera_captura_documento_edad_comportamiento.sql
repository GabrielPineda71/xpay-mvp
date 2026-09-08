-- =====================================================================
-- Migración 039: Cartera Ordinaria — extensión de captura durable P0
-- de MiDecisor para habilitar el futuro motor M2.4b (diseño 175 + 176 +
-- corrección Unicode 177). INFRAESTRUCTURA DORMIDA.
--
-- Añade 1 columna de staging (purgable) en el intento y 12 columnas de
-- snapshot (purga-seguras) en la solicitud:
--
--   dbo.cartera_solicitud_cupo_intentos
--     p0_provider_raw_json                   NVARCHAR(MAX) NULL   -- staging; lo anula la purga
--
--   dbo.cartera_solicitudes_cupo
--     tipo_documento_observado               NVARCHAR(60)  NULL
--     estado_documento_datos_basicos_raw     NVARCHAR(60)  NULL
--     estado_documento_info_demografica_raw  NVARCHAR(60)  NULL
--     estado_documento_captura               VARCHAR(12)   NULL   -- PRESENTE / AUSENTE / CONFLICTO
--     rango_edad_datos_basicos_raw           NVARCHAR(20)  NULL
--     rango_edad_info_demografica_raw        NVARCHAR(20)  NULL
--     rango_edad_captura                     VARCHAR(12)   NULL   -- PRESENTE / AUSENTE / CONFLICTO
--     consulta_anio_raw                      NVARCHAR(8)   NULL
--     consulta_mes_raw                       NVARCHAR(4)   NULL
--     consulta_dia_raw                       NVARCHAR(4)   NULL
--     comportamiento_vector_json             NVARCHAR(MAX) NULL
--     comportamiento_vector_count            INT           NULL   -- NULL = bloque ausente ; 0 = presente vacío
--
-- Semántica:
--   p0_provider_raw_json
--     "durable semantic raw projection" de los valores P0 del proveedor,
--     construida DESPUÉS de la deserialización de System.Text.Json. NO es el
--     JSON original del proveedor ni contiene el envelope completo. Preserva
--     strings exactos ("", " ", whitespace), null, orden y duplicados del
--     vectorComportamiento. La escribe FinalizarIntentoAsync junto al
--     resultado durable existente. La anula PurgarResultadoIntentoAsync en
--     la misma operación que hoy anula los 6 crudos.
--
--   tipo_documento_observado / estado_documento_*_raw / rango_edad_*_raw /
--   consulta_*_raw / comportamiento_vector_json / comportamiento_vector_count
--     Copia durable y purga-segura del valor RAW del proveedor (sin
--     normalizar, sin política crediticia), materializada por
--     CarteraConsultaRiesgoStore.ConsumirResultadoRiesgoAsync en la MISMA
--     transacción y AppLock que los 7 observados actuales. La purga de
--     M2.3b3 NUNCA toca cartera_solicitudes_cupo → estas columnas sobreviven
--     y completan el snapshot que M2.4b leerá tras la purga de crudos.
--
--   *_captura ∈ {PRESENTE, AUSENTE, CONFLICTO} — clasificación ESTRUCTURAL
--     (no política) del par de rutas datosBasicos / informacionDemografica.
--
-- INFRAESTRUCTURA DORMIDA: estas columnas sólo las escriben FinalizarIntentoAsync
-- (staging) y ConsumirResultadoRiesgoAsync (snapshot), que NO están registradas
-- en DI, NO tienen caller de runtime y NO emiten veredicto crediticio.
-- decision_crediticia / fecha_decision / monto_aprobado / estado_solicitud
-- NO cambian.
--
-- Idempotente y fail-fast — mismo patrón que 038 (ADD COLUMN sólo si falta;
-- si ya existe, verifica tipo/max_length/nullability y aborta con THROW ante
-- discrepancia). NO borra datos, NO altera columnas existentes, NO DEFAULT,
-- NO CHECK, NO índice, NO backfill.
--
-- Compatibilidad con re-verificaciones previas: las verificaciones finales de
-- 035/036/037/038 cuentan columnas contra listas fijas de nombres; ninguno de
-- los 13 nombres nuevos está en esas listas → los conteos previos no cambian y
-- 035/036/037/038 siguen verdes en su 2ª pasada.
--
-- Todos los strings raw del proveedor usan NVARCHAR (no VARCHAR) para
-- preservación exacta Unicode del valor deserializado (corrección 177).
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

    PRINT '=== INICIO MIGRACIÓN 039: extensión de captura P0 MiDecisor (dormida) ===';

    DECLARE @objIdIntento INT = OBJECT_ID('dbo.cartera_solicitud_cupo_intentos', 'U');
    IF @objIdIntento IS NULL
        THROW 59100, N'Migración 039 abortada: dbo.cartera_solicitud_cupo_intentos no existe (falta migración 035).', 1;

    DECLARE @objIdSolicitud INT = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U');
    IF @objIdSolicitud IS NULL
        THROW 59100, N'Migración 039 abortada: dbo.cartera_solicitudes_cupo no existe (falta migración 035).', 1;

    -- ── Helper conceptual (inline): añade una columna NVARCHAR/VARCHAR/INT NULL
    --    si falta; si existe, verifica tipo + max_length + nullability. ────────

    ---------------------------------------------------------------------------
    -- 1. cartera_solicitud_cupo_intentos.p0_provider_raw_json  NVARCHAR(MAX) NULL
    ---------------------------------------------------------------------------
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdIntento AND name = 'p0_provider_raw_json')
    BEGIN
        ALTER TABLE dbo.cartera_solicitud_cupo_intentos ADD p0_provider_raw_json NVARCHAR(MAX) NULL;
        PRINT 'OK: intentos.p0_provider_raw_json agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdIntento AND c.name = 'p0_provider_raw_json'
              AND ty.name = 'nvarchar' AND c.max_length = -1 AND c.is_nullable = 1)
            THROW 59101, N'Migración 039 abortada: p0_provider_raw_json ya existe pero no es NVARCHAR(MAX) NULL.', 1;
        PRINT 'INFO: intentos.p0_provider_raw_json ya existe con la estructura esperada — omitida';
    END

    ---------------------------------------------------------------------------
    -- 2..13. cartera_solicitudes_cupo — 12 columnas de snapshot
    ---------------------------------------------------------------------------
    -- tipo_documento_observado NVARCHAR(60) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'tipo_documento_observado')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo ADD tipo_documento_observado NVARCHAR(60) NULL;
        PRINT 'OK: solicitudes.tipo_documento_observado agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'tipo_documento_observado'
              AND ty.name = 'nvarchar' AND c.max_length = 120 AND c.is_nullable = 1)
            THROW 59102, N'Migración 039 abortada: tipo_documento_observado ya existe pero no es NVARCHAR(60) NULL.', 1;
        PRINT 'INFO: solicitudes.tipo_documento_observado ya existe con la estructura esperada — omitida';
    END

    -- estado_documento_datos_basicos_raw NVARCHAR(60) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'estado_documento_datos_basicos_raw')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo ADD estado_documento_datos_basicos_raw NVARCHAR(60) NULL;
        PRINT 'OK: solicitudes.estado_documento_datos_basicos_raw agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'estado_documento_datos_basicos_raw'
              AND ty.name = 'nvarchar' AND c.max_length = 120 AND c.is_nullable = 1)
            THROW 59103, N'Migración 039 abortada: estado_documento_datos_basicos_raw ya existe pero no es NVARCHAR(60) NULL.', 1;
        PRINT 'INFO: solicitudes.estado_documento_datos_basicos_raw ya existe con la estructura esperada — omitida';
    END

    -- estado_documento_info_demografica_raw NVARCHAR(60) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'estado_documento_info_demografica_raw')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo ADD estado_documento_info_demografica_raw NVARCHAR(60) NULL;
        PRINT 'OK: solicitudes.estado_documento_info_demografica_raw agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'estado_documento_info_demografica_raw'
              AND ty.name = 'nvarchar' AND c.max_length = 120 AND c.is_nullable = 1)
            THROW 59104, N'Migración 039 abortada: estado_documento_info_demografica_raw ya existe pero no es NVARCHAR(60) NULL.', 1;
        PRINT 'INFO: solicitudes.estado_documento_info_demografica_raw ya existe con la estructura esperada — omitida';
    END

    -- estado_documento_captura VARCHAR(12) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'estado_documento_captura')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo ADD estado_documento_captura VARCHAR(12) NULL;
        PRINT 'OK: solicitudes.estado_documento_captura agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'estado_documento_captura'
              AND ty.name = 'varchar' AND c.max_length = 12 AND c.is_nullable = 1)
            THROW 59105, N'Migración 039 abortada: estado_documento_captura ya existe pero no es VARCHAR(12) NULL.', 1;
        PRINT 'INFO: solicitudes.estado_documento_captura ya existe con la estructura esperada — omitida';
    END

    -- rango_edad_datos_basicos_raw NVARCHAR(20) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'rango_edad_datos_basicos_raw')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo ADD rango_edad_datos_basicos_raw NVARCHAR(20) NULL;
        PRINT 'OK: solicitudes.rango_edad_datos_basicos_raw agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'rango_edad_datos_basicos_raw'
              AND ty.name = 'nvarchar' AND c.max_length = 40 AND c.is_nullable = 1)
            THROW 59106, N'Migración 039 abortada: rango_edad_datos_basicos_raw ya existe pero no es NVARCHAR(20) NULL.', 1;
        PRINT 'INFO: solicitudes.rango_edad_datos_basicos_raw ya existe con la estructura esperada — omitida';
    END

    -- rango_edad_info_demografica_raw NVARCHAR(20) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'rango_edad_info_demografica_raw')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo ADD rango_edad_info_demografica_raw NVARCHAR(20) NULL;
        PRINT 'OK: solicitudes.rango_edad_info_demografica_raw agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'rango_edad_info_demografica_raw'
              AND ty.name = 'nvarchar' AND c.max_length = 40 AND c.is_nullable = 1)
            THROW 59107, N'Migración 039 abortada: rango_edad_info_demografica_raw ya existe pero no es NVARCHAR(20) NULL.', 1;
        PRINT 'INFO: solicitudes.rango_edad_info_demografica_raw ya existe con la estructura esperada — omitida';
    END

    -- rango_edad_captura VARCHAR(12) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'rango_edad_captura')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo ADD rango_edad_captura VARCHAR(12) NULL;
        PRINT 'OK: solicitudes.rango_edad_captura agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'rango_edad_captura'
              AND ty.name = 'varchar' AND c.max_length = 12 AND c.is_nullable = 1)
            THROW 59108, N'Migración 039 abortada: rango_edad_captura ya existe pero no es VARCHAR(12) NULL.', 1;
        PRINT 'INFO: solicitudes.rango_edad_captura ya existe con la estructura esperada — omitida';
    END

    -- consulta_anio_raw NVARCHAR(8) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'consulta_anio_raw')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo ADD consulta_anio_raw NVARCHAR(8) NULL;
        PRINT 'OK: solicitudes.consulta_anio_raw agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'consulta_anio_raw'
              AND ty.name = 'nvarchar' AND c.max_length = 16 AND c.is_nullable = 1)
            THROW 59109, N'Migración 039 abortada: consulta_anio_raw ya existe pero no es NVARCHAR(8) NULL.', 1;
        PRINT 'INFO: solicitudes.consulta_anio_raw ya existe con la estructura esperada — omitida';
    END

    -- consulta_mes_raw NVARCHAR(4) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'consulta_mes_raw')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo ADD consulta_mes_raw NVARCHAR(4) NULL;
        PRINT 'OK: solicitudes.consulta_mes_raw agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'consulta_mes_raw'
              AND ty.name = 'nvarchar' AND c.max_length = 8 AND c.is_nullable = 1)
            THROW 59110, N'Migración 039 abortada: consulta_mes_raw ya existe pero no es NVARCHAR(4) NULL.', 1;
        PRINT 'INFO: solicitudes.consulta_mes_raw ya existe con la estructura esperada — omitida';
    END

    -- consulta_dia_raw NVARCHAR(4) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'consulta_dia_raw')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo ADD consulta_dia_raw NVARCHAR(4) NULL;
        PRINT 'OK: solicitudes.consulta_dia_raw agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'consulta_dia_raw'
              AND ty.name = 'nvarchar' AND c.max_length = 8 AND c.is_nullable = 1)
            THROW 59111, N'Migración 039 abortada: consulta_dia_raw ya existe pero no es NVARCHAR(4) NULL.', 1;
        PRINT 'INFO: solicitudes.consulta_dia_raw ya existe con la estructura esperada — omitida';
    END

    -- comportamiento_vector_json NVARCHAR(MAX) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'comportamiento_vector_json')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo ADD comportamiento_vector_json NVARCHAR(MAX) NULL;
        PRINT 'OK: solicitudes.comportamiento_vector_json agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'comportamiento_vector_json'
              AND ty.name = 'nvarchar' AND c.max_length = -1 AND c.is_nullable = 1)
            THROW 59112, N'Migración 039 abortada: comportamiento_vector_json ya existe pero no es NVARCHAR(MAX) NULL.', 1;
        PRINT 'INFO: solicitudes.comportamiento_vector_json ya existe con la estructura esperada — omitida';
    END

    -- comportamiento_vector_count INT NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'comportamiento_vector_count')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo ADD comportamiento_vector_count INT NULL;
        PRINT 'OK: solicitudes.comportamiento_vector_count agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'comportamiento_vector_count'
              AND ty.name = 'int' AND c.is_nullable = 1)
            THROW 59113, N'Migración 039 abortada: comportamiento_vector_count ya existe pero no es INT NULL.', 1;
        PRINT 'INFO: solicitudes.comportamiento_vector_count ya existe con la estructura esperada — omitida';
    END

    COMMIT TRANSACTION;
    PRINT '=== MIGRACIÓN 039 COMPLETADA ===';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT('MIGRACIÓN 039 ABORTADA: ', ERROR_MESSAGE());
    THROW;
END CATCH;
GO

-- ── Verificación final (idempotente, sólo lectura) ──────────────────────
DECLARE @objIdIntentoV   INT = OBJECT_ID('dbo.cartera_solicitud_cupo_intentos', 'U');
DECLARE @objIdSolicitudV INT = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U');

DECLARE @stagingOk INT = (
    SELECT CASE WHEN EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objIdIntentoV AND c.name = 'p0_provider_raw_json'
          AND ty.name = 'nvarchar' AND c.max_length = -1 AND c.is_nullable = 1
    ) THEN 1 ELSE 0 END
);

DECLARE @snapshotOk INT = (
    SELECT COUNT(*) FROM (VALUES
        ('tipo_documento_observado','nvarchar',120),
        ('estado_documento_datos_basicos_raw','nvarchar',120),
        ('estado_documento_info_demografica_raw','nvarchar',120),
        ('estado_documento_captura','varchar',12),
        ('rango_edad_datos_basicos_raw','nvarchar',40),
        ('rango_edad_info_demografica_raw','nvarchar',40),
        ('rango_edad_captura','varchar',12),
        ('consulta_anio_raw','nvarchar',16),
        ('consulta_mes_raw','nvarchar',8),
        ('consulta_dia_raw','nvarchar',8),
        ('comportamiento_vector_json','nvarchar',-1)
    ) AS v(nombre, tipo, maxlen)
    JOIN sys.columns c ON c.object_id = @objIdSolicitudV AND c.name = v.nombre
    JOIN sys.types  ty ON ty.user_type_id = c.user_type_id AND ty.name = v.tipo
    WHERE c.max_length = v.maxlen AND c.is_nullable = 1
);

DECLARE @countColOk INT = (
    SELECT CASE WHEN EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objIdSolicitudV AND c.name = 'comportamiento_vector_count'
          AND ty.name = 'int' AND c.is_nullable = 1
    ) THEN 1 ELSE 0 END
);

DECLARE @resultado039 NVARCHAR(90) =
    CASE WHEN @stagingOk = 1 AND @snapshotOk = 11 AND @countColOk = 1
         THEN N'OK — migración 039 aplicada y verificada (1 staging + 12 snapshot)'
         ELSE N'REVISAR — alguna columna de 039 no coincide con lo esperado'
    END;

SELECT
    @stagingOk    AS p0_provider_raw_json_ok,
    @snapshotOk   AS snapshot_nvarchar_varchar_ok_de_11,
    @countColOk   AS comportamiento_vector_count_ok,
    @resultado039 AS resultado;

PRINT '=== VERIFICACIÓN 039 COMPLETA ===';
