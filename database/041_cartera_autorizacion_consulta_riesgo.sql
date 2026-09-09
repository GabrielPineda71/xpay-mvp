-- =====================================================================
-- Migración 041: Cartera Ordinaria — evidencia durable de la AUTORIZACIÓN
-- del titular para consulta en centrales de riesgo (MiDecisor / DataCrédito).
--
-- Gobierno: ACTA 001 v1.0 (08/09/2026, Pereira) §3 — "Antes de realizar una
-- consulta real, XPAY deberá contar con evidencia de autorización del cliente".
-- Diseño técnico: XPAY-195 ; corrección de scope: XPAY-196 ; regla V1
-- (estricta, sin reutilización cross-request): XPAY-197.
--
-- A. CREATE TABLE dbo.cartera_autorizacion_consulta_riesgo
--      id_autorizacion        BIGINT IDENTITY(1,1) NOT NULL  (PK)
--      id_persona             BIGINT NOT NULL   (FK -> personas ; TITULAR de la información)
--      id_usuario             BIGINT NOT NULL   (FK -> usuarios ; cuenta que ejecutó "AUTORIZO")
--      id_solicitud_origen    BIGINT NOT NULL   (FK -> cartera_solicitudes_cupo ; RELATION_ORIGIN_ID V1)
--      version_texto          VARCHAR(60)  NOT NULL  (identificador estable de la versión aceptada)
--      hash_texto             CHAR(64)     NOT NULL  (SHA-256 hex del texto exacto aceptado)
--      texto_snapshot         NVARCHAR(MAX) NOT NULL (copia literal del texto mostrado y aceptado)
--      fecha_aceptacion_utc   DATETIME2 NOT NULL     (instante de aceptación, UTC ; NO es el
--                                                     inicio del reloj de retención de 5 años)
--      correlation_id         VARCHAR(100) NULL      (traza técnica del request de captura)
--
--    Constraints: UNIQUE(id_solicitud_origen, version_texto)  (idempotencia de la aceptación)
--                 CHECK(LEN(hash_texto) = 64) ; CHECK(LEN(version_texto) > 0)
--    Índice:      ix_..._persona_version (id_persona, version_texto)  (lookup de validación)
--
-- B. INMUTABILIDAD V1: la fila es evidencia histórica append-only. NO se
--    diseña estado / revocación / superseded / cambio-material / fecha de
--    expiración / canal obligatorio / id_cupo. La inmutabilidad se garantiza
--    por (a) ausencia de columnas mutables, (b) store insert-only, (c) ausencia
--    de endpoint PUT/PATCH/DELETE. Sin trigger en esta iteración.
--
-- C. NO toca cartera_cupos_ordinarios (RELATION_END / retención = fase separada).
--    NO crea catálogo de versiones (sólo existe v1 = texto de ACTA 001 §3.1).
--
-- D. SIN backfill — tabla nace vacía.
--
-- INFRAESTRUCTURA V1: la tabla la ESCRIBE únicamente
-- CarteraAutorizacionConsultaRiesgoStore.RegistrarAceptacionAsync (invocado por
-- el endpoint POST .../autorizar-consulta-riesgo) y la LEE
-- AutorizacionConsultaRiesgoDurable (implementación de IConsultaRiesgoAutorizacion
-- NO registrada en DI — el registro sigue apuntando a
-- AutorizacionConsultaRiesgoNoDisponible). NO llama a MiDecisor.
--
-- Idempotente y fail-fast — mismo patrón que 035/038/039/040.
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

    PRINT '=== INICIO MIGRACIÓN 041: evidencia durable de autorización de consulta de riesgo (V1) ===';

    DECLARE @objIdPersonas   INT = OBJECT_ID('dbo.personas', 'U');
    DECLARE @objIdUsuarios   INT = OBJECT_ID('dbo.usuarios', 'U');
    DECLARE @objIdSolicitud  INT = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U');

    IF @objIdPersonas IS NULL
        THROW 59300, N'Migración 041 abortada: dbo.personas no existe (falta migración 001).', 1;
    IF @objIdUsuarios IS NULL
        THROW 59301, N'Migración 041 abortada: dbo.usuarios no existe (falta migración 001).', 1;
    IF @objIdSolicitud IS NULL
        THROW 59302, N'Migración 041 abortada: dbo.cartera_solicitudes_cupo no existe (falta migración 035).', 1;

    ---------------------------------------------------------------------------
    -- A. dbo.cartera_autorizacion_consulta_riesgo
    ---------------------------------------------------------------------------
    DECLARE @objIdAutz INT = OBJECT_ID('dbo.cartera_autorizacion_consulta_riesgo', 'U');

    IF @objIdAutz IS NULL
    BEGIN
        CREATE TABLE dbo.cartera_autorizacion_consulta_riesgo (
            id_autorizacion       BIGINT        IDENTITY(1,1) NOT NULL,
            id_persona            BIGINT        NOT NULL,
            id_usuario            BIGINT        NOT NULL,
            id_solicitud_origen   BIGINT        NOT NULL,
            version_texto         VARCHAR(60)   NOT NULL,
            hash_texto            CHAR(64)      NOT NULL,
            texto_snapshot        NVARCHAR(MAX) NOT NULL,
            fecha_aceptacion_utc  DATETIME2     NOT NULL,
            correlation_id        VARCHAR(100)  NULL,

            CONSTRAINT pk_cartera_autorizacion_consulta_riesgo
                PRIMARY KEY CLUSTERED (id_autorizacion),

            CONSTRAINT fk_cartera_autorizacion_consulta_riesgo_persona
                FOREIGN KEY (id_persona) REFERENCES dbo.personas (id_persona),

            CONSTRAINT fk_cartera_autorizacion_consulta_riesgo_usuario
                FOREIGN KEY (id_usuario) REFERENCES dbo.usuarios (id_usuario),

            CONSTRAINT fk_cartera_autorizacion_consulta_riesgo_solicitud
                FOREIGN KEY (id_solicitud_origen) REFERENCES dbo.cartera_solicitudes_cupo (id_solicitud),

            CONSTRAINT uq_cartera_autorizacion_consulta_riesgo_sol_version
                UNIQUE (id_solicitud_origen, version_texto),

            CONSTRAINT ck_cartera_autorizacion_consulta_riesgo_hash_len
                CHECK (LEN(hash_texto) = 64),

            CONSTRAINT ck_cartera_autorizacion_consulta_riesgo_version_ok
                CHECK (LEN(version_texto) > 0)
        );

        SET @objIdAutz = OBJECT_ID('dbo.cartera_autorizacion_consulta_riesgo', 'U');
        PRINT 'OK: tabla dbo.cartera_autorizacion_consulta_riesgo creada';
    END
    ELSE
    BEGIN
        PRINT 'INFO: dbo.cartera_autorizacion_consulta_riesgo ya existe — verificando estructura crítica...';

        DECLARE @colsFaltantes NVARCHAR(400);
        SELECT @colsFaltantes = STRING_AGG(req.col, ', ')
        FROM (VALUES ('id_autorizacion'), ('id_persona'), ('id_usuario'), ('id_solicitud_origen'),
                     ('version_texto'), ('hash_texto'), ('texto_snapshot'), ('fecha_aceptacion_utc'),
                     ('correlation_id')) AS req(col)
        WHERE NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = @objIdAutz AND c.name = req.col);

        IF @colsFaltantes IS NOT NULL
            THROW 59310, N'Migración 041 abortada: cartera_autorizacion_consulta_riesgo ya existe pero le faltan columnas requeridas. Revisar manualmente.', 1;

        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdAutz AND c.name = 'hash_texto'
              AND ty.name = 'char' AND c.max_length = 64 AND c.is_nullable = 0)
            THROW 59311, N'Migración 041 abortada: hash_texto no es CHAR(64) NOT NULL. Revisar manualmente.', 1;

        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdAutz AND c.name = 'version_texto'
              AND ty.name = 'varchar' AND c.max_length = 60 AND c.is_nullable = 0)
            THROW 59312, N'Migración 041 abortada: version_texto no es VARCHAR(60) NOT NULL. Revisar manualmente.', 1;

        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdAutz AND c.name = 'texto_snapshot'
              AND ty.name = 'nvarchar' AND c.max_length = -1 AND c.is_nullable = 0)
            THROW 59313, N'Migración 041 abortada: texto_snapshot no es NVARCHAR(MAX) NOT NULL. Revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = @objIdAutz AND type = 'PK')
            THROW 59314, N'Migración 041 abortada: cartera_autorizacion_consulta_riesgo sin PK. Revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @objIdAutz AND name = 'fk_cartera_autorizacion_consulta_riesgo_persona')
        OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @objIdAutz AND name = 'fk_cartera_autorizacion_consulta_riesgo_usuario')
        OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @objIdAutz AND name = 'fk_cartera_autorizacion_consulta_riesgo_solicitud')
            THROW 59315, N'Migración 041 abortada: falta alguna FK (persona / usuario / solicitud). Revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdAutz AND name = 'uq_cartera_autorizacion_consulta_riesgo_sol_version')
            THROW 59316, N'Migración 041 abortada: falta el UNIQUE(id_solicitud_origen, version_texto). Revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @objIdAutz AND name = 'ck_cartera_autorizacion_consulta_riesgo_hash_len')
        OR NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @objIdAutz AND name = 'ck_cartera_autorizacion_consulta_riesgo_version_ok')
            THROW 59317, N'Migración 041 abortada: falta algún CHECK (hash_len / version_ok). Revisar manualmente.', 1;

        PRINT 'OK: estructura de dbo.cartera_autorizacion_consulta_riesgo verificada';
    END

    ---------------------------------------------------------------------------
    -- B. Índice de apoyo a la validación (id_persona, version_texto)
    ---------------------------------------------------------------------------
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdAutz AND name = 'ix_cartera_autorizacion_consulta_riesgo_persona_version')
    BEGIN
        CREATE INDEX ix_cartera_autorizacion_consulta_riesgo_persona_version
            ON dbo.cartera_autorizacion_consulta_riesgo (id_persona, version_texto);
        PRINT 'OK: ix_cartera_autorizacion_consulta_riesgo_persona_version creado';
    END
    ELSE
        PRINT 'INFO: ix_cartera_autorizacion_consulta_riesgo_persona_version ya existe — omitido';

    COMMIT TRANSACTION;
    PRINT '=== MIGRACIÓN 041 COMPLETADA ===';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT '=== MIGRACIÓN 041 ABORTADA ===';
    THROW;
END CATCH;
GO

-- ── Verificación final (idempotente, sólo lectura) ──────────────────────
DECLARE @objIdAutzV INT = OBJECT_ID('dbo.cartera_autorizacion_consulta_riesgo', 'U');

DECLARE @tablaOk INT = (
    SELECT CASE WHEN @objIdAutzV IS NOT NULL
                 AND EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                             WHERE c.object_id = @objIdAutzV AND c.name = 'hash_texto'
                               AND ty.name = 'char' AND c.max_length = 64 AND c.is_nullable = 0)
                 AND EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                             WHERE c.object_id = @objIdAutzV AND c.name = 'texto_snapshot'
                               AND ty.name = 'nvarchar' AND c.max_length = -1 AND c.is_nullable = 0)
                 AND (SELECT COUNT(*) FROM sys.foreign_keys
                      WHERE parent_object_id = @objIdAutzV
                        AND name IN ('fk_cartera_autorizacion_consulta_riesgo_persona',
                                     'fk_cartera_autorizacion_consulta_riesgo_usuario',
                                     'fk_cartera_autorizacion_consulta_riesgo_solicitud')) = 3
           THEN 1 ELSE 0 END
);

DECLARE @uniqueOk INT = (
    SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdAutzV
                             AND name = 'uq_cartera_autorizacion_consulta_riesgo_sol_version' AND is_unique = 1)
           THEN 1 ELSE 0 END
);

DECLARE @indexOk INT = (
    SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdAutzV
                             AND name = 'ix_cartera_autorizacion_consulta_riesgo_persona_version')
           THEN 1 ELSE 0 END
);

DECLARE @resultado041 NVARCHAR(90) =
    CASE WHEN @tablaOk = 1 AND @uniqueOk = 1 AND @indexOk = 1
         THEN N'OK — migración 041 aplicada y verificada (tabla + FKs + UNIQUE + índice)'
         ELSE N'REVISAR — algún objeto de 041 no coincide con lo esperado'
    END;

SELECT @tablaOk AS tabla_autz_ok, @uniqueOk AS unique_sol_version_ok, @indexOk AS indice_persona_version_ok, @resultado041 AS resultado;

PRINT '=== VERIFICACIÓN 041 COMPLETA ===';
