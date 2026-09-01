-- =====================================================================
-- Migración 035: Cartera Ordinaria — Originación automática de cupo vía
-- MiDecisor/DataCrédito (Implementación ETAPA 1 — schema + entidades).
-- Dos tablas 100% nuevas: dbo.cartera_solicitudes_cupo (una fila por
-- solicitud lógica) y dbo.cartera_solicitud_cupo_intentos (una fila por
-- intento de proveedor asociado a esa solicitud).
--
-- Idempotente y fail-fast — no borra datos, no altera columnas existentes,
-- no toca ninguna otra tabla. Diseño cerrado en IMPLEMENTATION SPEC 006,
-- con tres correcciones obligatorias aplicadas en esta etapa:
--   - estado_score            VARCHAR(30) NULL (no NOT NULL) — la solicitud
--     se persiste ANTES de llamar a MiDecisor (PRE-CALL); NULL significa
--     "todavía no se ha evaluado respuesta MiDecisor", nunca SCORE_PENDIENTE.
--   - resultado_tecnico       VARCHAR(30) NULL (no NOT NULL) — el intento se
--     inserta ANTES de hacer la llamada HTTP; TX1 (etapa posterior) lo
--     completa con el resultado real.
--   - edad_calculada_al_momento INT NULL (no NOT NULL) — la solicitud se
--     persiste PRE-CALL sin fabricar la edad; NULL significa "edad no
--     calculada/no disponible". No se calcula ni se usa para aprobar/
--     rechazar crédito en esta etapa (decisión de diseño 016).
--
-- Comportamiento (mismo patrón que 029/031/034):
--   1) Si una tabla NO existe: la crea completa, con PK, FK, UNIQUE y CHECK.
--   2) Si YA existe: NO la modifica ni la repara silenciosamente. Verifica
--      que las columnas requeridas existan y que PK/FK/UNIQUE/CHECK/índice
--      filtrado existan — si CUALQUIERA falla, aborta con THROW (rollback
--      automático por XACT_ABORT), sin agregar/recrear nada.
--
-- Una solicitud activa por usuario: se garantiza a nivel de BD con un
-- índice UNIQUE FILTRADO sobre (id_usuario) WHERE estado_solicitud IN
-- (los 5 estados activos). La cláusula IN en el predicado de un índice
-- filtrado es sintaxis válida y soportada por SQL Server (operador
-- documentado por Microsoft para filter_definition junto con =, <>, >, <,
-- IS NULL, IS NOT NULL, AND) — no requiere una alternativa distinta.
--
-- Convención de nombres seguida (evidencia real, no inventada): las
-- migraciones de CREATE TABLE más recientes de este repo (029, 031 — y la
-- propia familia Cartera Ordinaria en 021) usan minúsculas pk_/fk_/uq_/ck_/
-- ix_ inline dentro del CREATE TABLE, sin CONSTRAINT DF_ nombrado para
-- DEFAULT (eso solo aparece en 032, y únicamente para un ALTER TABLE ADD
-- sobre una tabla ya poblada — no aplica aquí, donde las tablas son nuevas).
-- Se sigue ese mismo patrón; ux_ (en vez de ix_) se usa exclusivamente para
-- el índice UNIQUE FILTRADO, replicando en minúsculas la distinción PK_/
-- UX_/IX_ ya usada por 034 para el mismo propósito conceptual.
--
-- fecha_solicitud / fecha_inicio / fecha_actualizacion llevan
-- DEFAULT GETUTCDATE() aunque la especificación no lo escribió
-- explícitamente junto al tipo — se alinea con el patrón real de toda la
-- familia Cartera Ordinaria (021: fecha_aprobacion, fecha_solicitud,
-- created_at siempre DEFAULT GETUTCDATE() cuando son NOT NULL).
--
-- Esta migración NO crea: lógica de negocio, llamadas a MiDecisor, motor
-- de política de crédito, ni ningún cambio en otras tablas.
--
-- Hardening 009 (posterior a la creación inicial, sin cambios de diseño):
-- las ramas de re-ejecución (tabla/índice/constraint ya existentes) ahora
-- validan estructura real — columnas exactas de cada FK, columnas exactas
-- de cada UNIQUE, y unicidad+columna+presencia de los 5 estados activos en
-- el filtro del índice filtrado — en vez de solo verificar el nombre. La
-- verificación final pasó de exigir un COUNT total exacto de columnas/
-- checks a verificar presencia de los elementos que esta migración
-- requiere, para no romperse ante extensiones legítimas futuras de estas
-- tablas.
--
-- NO EJECUTADA POR EL AGENTE — preparada para revisión y ejecución manual
-- del usuario.
-- =====================================================================

SET XACT_ABORT ON;

-- Requeridas por SQL Server para crear el índice filtrado más abajo
-- (mismo motivo documentado en 029/031/032/034).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT '=== INICIO MIGRACIÓN 035: Cartera Ordinaria — solicitudes de cupo ===';

    -- ── Guard: tablas referenciadas por FK deben existir ─────────────────
    IF OBJECT_ID('dbo.usuarios', 'U') IS NULL
        THROW 56000, N'Migración 035 abortada: dbo.usuarios no existe. No se puede continuar.', 1;
    IF OBJECT_ID('dbo.personas', 'U') IS NULL
        THROW 56001, N'Migración 035 abortada: dbo.personas no existe. No se puede continuar.', 1;
    IF OBJECT_ID('dbo.cartera_politicas_credito', 'U') IS NULL
        THROW 56002, N'Migración 035 abortada: dbo.cartera_politicas_credito no existe. No se puede continuar.', 1;
    IF OBJECT_ID('dbo.cartera_cupos_ordinarios', 'U') IS NULL
        THROW 56003, N'Migración 035 abortada: dbo.cartera_cupos_ordinarios no existe. No se puede continuar.', 1;

    -- ═══════════════════════════════════════════════════════════════════
    -- TABLA 1: dbo.cartera_solicitudes_cupo
    -- ═══════════════════════════════════════════════════════════════════
    DECLARE @objIdSolicitud INT = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U');

    IF @objIdSolicitud IS NULL
    BEGIN
        CREATE TABLE dbo.cartera_solicitudes_cupo (
            id_solicitud                        BIGINT          IDENTITY(1,1) NOT NULL,
            id_usuario                          BIGINT          NOT NULL,
            id_persona                          BIGINT          NOT NULL,
            monto_solicitado                    DECIMAL(18,2)   NOT NULL,
            estado_solicitud                    VARCHAR(30)     NOT NULL,
            decision_crediticia                 VARCHAR(20)     NOT NULL DEFAULT 'PENDIENTE',
            monto_aprobado                      DECIMAL(18,2)   NULL,
            codigo_motivo_decision              VARCHAR(50)     NULL,
            id_politica_aplicada                BIGINT          NOT NULL,
            score_datacredito_minimo_aplicado   INT             NULL,
            cupo_minimo_aplicado                DECIMAL(18,2)   NOT NULL,
            cupo_maximo_aplicado                DECIMAL(18,2)   NOT NULL,
            edad_minima_aplicada                INT             NOT NULL,
            edad_maxima_aplicada                INT             NOT NULL,
            edad_calculada_al_momento           INT             NULL,  -- NULL = edad no calculada/no disponible (PRE-CALL: no se calcula ni se usa para decisión)
            score_observado                     INT             NULL,
            estado_score                        VARCHAR(30)     NULL,  -- NULL = todavía no se ha evaluado respuesta MiDecisor (PRE-CALL)
            viabilidad_observada                VARCHAR(10)     NULL,
            rating_recaudos_observado           CHAR(1)         NULL,
            monto_sugerido_observado            DECIMAL(18,2)   NULL,
            numero_intento                      INT             NOT NULL DEFAULT 1,
            id_cupo_ordinario                   BIGINT          NULL,
            correlation_id                      VARCHAR(64)     NOT NULL,
            fecha_solicitud                     DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
            fecha_decision                      DATETIME2       NULL,
            fecha_materializacion_cupo          DATETIME2       NULL,
            fecha_actualizacion                 DATETIME2       NOT NULL DEFAULT GETUTCDATE(),

            CONSTRAINT pk_cartera_solicitudes_cupo
                PRIMARY KEY CLUSTERED (id_solicitud),

            CONSTRAINT fk_cartera_solicitudes_cupo_usuario
                FOREIGN KEY (id_usuario) REFERENCES dbo.usuarios (id_usuario),

            CONSTRAINT fk_cartera_solicitudes_cupo_persona
                FOREIGN KEY (id_persona) REFERENCES dbo.personas (id_persona),

            CONSTRAINT fk_cartera_solicitudes_cupo_politica
                FOREIGN KEY (id_politica_aplicada) REFERENCES dbo.cartera_politicas_credito (id_politica),

            CONSTRAINT fk_cartera_solicitudes_cupo_cupo_ordinario
                FOREIGN KEY (id_cupo_ordinario) REFERENCES dbo.cartera_cupos_ordinarios (id_cupo),

            CONSTRAINT ck_cartera_solicitudes_cupo_monto_solicitado
                CHECK (monto_solicitado > 0),

            CONSTRAINT ck_cartera_solicitudes_cupo_numero_intento
                CHECK (numero_intento >= 1),

            CONSTRAINT ck_cartera_solicitudes_cupo_cupo_minimo
                CHECK (cupo_minimo_aplicado >= 0),

            CONSTRAINT ck_cartera_solicitudes_cupo_cupo_maximo_vs_minimo
                CHECK (cupo_maximo_aplicado >= cupo_minimo_aplicado),

            -- Tolera NULL (edad no calculada): NULL >= 0 evalúa a UNKNOWN, no
            -- FALSE, por lo que el CHECK sólo restringe valores negativos reales.
            CONSTRAINT ck_cartera_solicitudes_cupo_edad_calculada
                CHECK (edad_calculada_al_momento >= 0)
        );

        SET @objIdSolicitud = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U');
        PRINT 'OK: tabla dbo.cartera_solicitudes_cupo creada';
    END
    ELSE
    BEGIN
        PRINT 'INFO: dbo.cartera_solicitudes_cupo ya existe — verificando estructura crítica...';

        DECLARE @columnasFaltantesSolicitud NVARCHAR(800);
        SELECT @columnasFaltantesSolicitud = STRING_AGG(req.col, ', ')
        FROM (VALUES
            ('id_solicitud'), ('id_usuario'), ('id_persona'), ('monto_solicitado'),
            ('estado_solicitud'), ('decision_crediticia'), ('monto_aprobado'),
            ('codigo_motivo_decision'), ('id_politica_aplicada'), ('score_datacredito_minimo_aplicado'),
            ('cupo_minimo_aplicado'), ('cupo_maximo_aplicado'), ('edad_minima_aplicada'),
            ('edad_maxima_aplicada'), ('edad_calculada_al_momento'), ('score_observado'),
            ('estado_score'), ('viabilidad_observada'), ('rating_recaudos_observado'),
            ('monto_sugerido_observado'), ('numero_intento'), ('id_cupo_ordinario'),
            ('correlation_id'), ('fecha_solicitud'), ('fecha_decision'),
            ('fecha_materializacion_cupo'), ('fecha_actualizacion')
        ) AS req(col)
        WHERE NOT EXISTS (
            SELECT 1 FROM sys.columns c WHERE c.object_id = @objIdSolicitud AND c.name = req.col
        );

        IF @columnasFaltantesSolicitud IS NOT NULL
            THROW 56010, N'Migración 035 abortada: dbo.cartera_solicitudes_cupo ya existe pero le faltan columnas requeridas. No se agregan columnas automáticamente — revisar manualmente.', 1;

        -- estado_score/resultado_tecnico deben ser NULL (correcciones PRE-CALL) — verificación crítica.
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c
            WHERE c.object_id = @objIdSolicitud AND c.name = 'estado_score' AND c.is_nullable = 1
        )
            THROW 56011, N'Migración 035 abortada: dbo.cartera_solicitudes_cupo.estado_score ya existe pero no es NULL — debe permitir NULL (PRE-CALL). Revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = @objIdSolicitud AND type = 'PK')
            THROW 56012, N'Migración 035 abortada: dbo.cartera_solicitudes_cupo existe pero no tiene clave primaria. Revisar manualmente.', 1;

        -- Hardening 009: no basta con que la FK exista con ese nombre — se
        -- valida que conecte exactamente la columna hija y la tabla/columna
        -- referenciada esperadas, vía sys.foreign_key_columns.
        IF NOT EXISTS (
            SELECT 1 FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fk.parent_object_id = @objIdSolicitud AND fk.name = 'fk_cartera_solicitudes_cupo_usuario'
              AND fk.referenced_object_id = OBJECT_ID('dbo.usuarios', 'U')
              AND pc.name = 'id_usuario' AND rc.name = 'id_usuario'
        )
            THROW 56013, N'Migración 035 abortada: falta fk_cartera_solicitudes_cupo_usuario o no conecta id_usuario -> dbo.usuarios.id_usuario. Revisar manualmente.', 1;

        IF NOT EXISTS (
            SELECT 1 FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fk.parent_object_id = @objIdSolicitud AND fk.name = 'fk_cartera_solicitudes_cupo_persona'
              AND fk.referenced_object_id = OBJECT_ID('dbo.personas', 'U')
              AND pc.name = 'id_persona' AND rc.name = 'id_persona'
        )
            THROW 56014, N'Migración 035 abortada: falta fk_cartera_solicitudes_cupo_persona o no conecta id_persona -> dbo.personas.id_persona. Revisar manualmente.', 1;

        IF NOT EXISTS (
            SELECT 1 FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fk.parent_object_id = @objIdSolicitud AND fk.name = 'fk_cartera_solicitudes_cupo_politica'
              AND fk.referenced_object_id = OBJECT_ID('dbo.cartera_politicas_credito', 'U')
              AND pc.name = 'id_politica_aplicada' AND rc.name = 'id_politica'
        )
            THROW 56015, N'Migración 035 abortada: falta fk_cartera_solicitudes_cupo_politica o no conecta id_politica_aplicada -> dbo.cartera_politicas_credito.id_politica. Revisar manualmente.', 1;

        IF NOT EXISTS (
            SELECT 1 FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fk.parent_object_id = @objIdSolicitud AND fk.name = 'fk_cartera_solicitudes_cupo_cupo_ordinario'
              AND fk.referenced_object_id = OBJECT_ID('dbo.cartera_cupos_ordinarios', 'U')
              AND pc.name = 'id_cupo_ordinario' AND rc.name = 'id_cupo'
        )
            THROW 56016, N'Migración 035 abortada: falta fk_cartera_solicitudes_cupo_cupo_ordinario o no conecta id_cupo_ordinario -> dbo.cartera_cupos_ordinarios.id_cupo. Revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @objIdSolicitud AND name = 'ck_cartera_solicitudes_cupo_monto_solicitado')
            THROW 56017, N'Migración 035 abortada: falta ck_cartera_solicitudes_cupo_monto_solicitado. Revisar manualmente.', 1;
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @objIdSolicitud AND name = 'ck_cartera_solicitudes_cupo_numero_intento')
            THROW 56018, N'Migración 035 abortada: falta ck_cartera_solicitudes_cupo_numero_intento. Revisar manualmente.', 1;
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @objIdSolicitud AND name = 'ck_cartera_solicitudes_cupo_cupo_minimo')
            THROW 56019, N'Migración 035 abortada: falta ck_cartera_solicitudes_cupo_cupo_minimo. Revisar manualmente.', 1;
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @objIdSolicitud AND name = 'ck_cartera_solicitudes_cupo_cupo_maximo_vs_minimo')
            THROW 56020, N'Migración 035 abortada: falta ck_cartera_solicitudes_cupo_cupo_maximo_vs_minimo. Revisar manualmente.', 1;
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @objIdSolicitud AND name = 'ck_cartera_solicitudes_cupo_edad_calculada')
            THROW 56021, N'Migración 035 abortada: falta ck_cartera_solicitudes_cupo_edad_calculada. Revisar manualmente.', 1;

        PRINT 'OK: estructura crítica de dbo.cartera_solicitudes_cupo verificada';
    END

    -- ── Índice UNIQUE FILTRADO: una solicitud activa por usuario ─────────
    -- Estados activos: RECIBIDA, VALIDANDO, CONSULTANDO_RIESGO, EN_EVALUACION,
    -- APROBADA_PENDIENTE_CUPO. IN es un operador soportado por SQL Server en
    -- el predicado de un índice filtrado (junto con =, <>, >, <, IS NULL,
    -- IS NOT NULL, AND) — sintaxis válida, no requiere alternativa.
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdSolicitud AND name = 'ux_cartera_solicitudes_cupo_usuario_activa')
    BEGIN
        CREATE UNIQUE INDEX ux_cartera_solicitudes_cupo_usuario_activa
            ON dbo.cartera_solicitudes_cupo (id_usuario)
            WHERE estado_solicitud IN ('RECIBIDA', 'VALIDANDO', 'CONSULTANDO_RIESGO', 'EN_EVALUACION', 'APROBADA_PENDIENTE_CUPO');
        PRINT 'OK: ux_cartera_solicitudes_cupo_usuario_activa creado (UNIQUE FILTRADO — una solicitud activa por usuario)';
    END
    ELSE
    BEGIN
        -- Hardening 009: ya existe con ese nombre — validar estructura antes de
        -- tratarlo como idempotente, en vez de solo confirmar el nombre.
        -- filter_definition es normalizado por SQL Server (no necesariamente el
        -- texto literal del WHERE original) y su forma exacta normalizada no se
        -- ha observado empíricamente contra una instancia real todavía (mismo
        -- motivo documentado en 034 para UX_personas_documento_verificado) — por
        -- eso se usa una comparación tolerante por presencia (LIKE) de la columna
        -- y de los 5 valores de estado, en vez de igualdad textual frágil.
        -- Limitación documentada: esta comprobación no distingue IN de NOT IN ni
        -- de una condición reordenada con los mismos 5 literales bajo otra
        -- semántica — cubre el caso real esperado (recreación accidental o
        -- manual con una definición distinta) sin sobreingeniería adicional.
        DECLARE @ixActivaUnique BIT, @ixActivaCols NVARCHAR(200), @ixActivaFiltro NVARCHAR(MAX);
        SELECT
            @ixActivaUnique = i.is_unique,
            @ixActivaCols   = STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal),
            @ixActivaFiltro = i.filter_definition
        FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = @objIdSolicitud AND i.name = 'ux_cartera_solicitudes_cupo_usuario_activa'
        GROUP BY i.is_unique, i.filter_definition;

        IF @ixActivaUnique <> 1
           OR @ixActivaCols <> 'id_usuario'
           OR @ixActivaFiltro IS NULL
           OR @ixActivaFiltro NOT LIKE '%estado_solicitud%'
           OR @ixActivaFiltro NOT LIKE '%RECIBIDA%'
           OR @ixActivaFiltro NOT LIKE '%VALIDANDO%'
           OR @ixActivaFiltro NOT LIKE '%CONSULTANDO_RIESGO%'
           OR @ixActivaFiltro NOT LIKE '%EN_EVALUACION%'
           OR @ixActivaFiltro NOT LIKE '%APROBADA_PENDIENTE_CUPO%'
            THROW 56040, N'Migración 035 abortada: ux_cartera_solicitudes_cupo_usuario_activa ya existe pero su estructura (unicidad/columna/filtro de los 5 estados activos) no coincide con lo esperado — revisar manualmente, no se recrea automáticamente.', 1;

        PRINT 'INFO: ux_cartera_solicitudes_cupo_usuario_activa ya existe con la estructura esperada — omitido';
    END

    -- ═══════════════════════════════════════════════════════════════════
    -- TABLA 2: dbo.cartera_solicitud_cupo_intentos
    -- ═══════════════════════════════════════════════════════════════════
    DECLARE @objIdIntento INT = OBJECT_ID('dbo.cartera_solicitud_cupo_intentos', 'U');

    IF @objIdIntento IS NULL
    BEGIN
        CREATE TABLE dbo.cartera_solicitud_cupo_intentos (
            id_intento                       BIGINT           IDENTITY(1,1) NOT NULL,
            id_solicitud                     BIGINT           NOT NULL,
            numero_intento                   INT              NOT NULL,
            idempotency_key                  UNIQUEIDENTIFIER NOT NULL,
            fecha_inicio                     DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
            fecha_fin                        DATETIME2        NULL,
            resultado_tecnico                VARCHAR(30)      NULL,  -- NULL = intento insertado PRE-CALL, aún sin resultado (TX1 lo completa)
            http_status_observado            INT              NULL,
            content_status_observado         VARCHAR(30)      NULL,
            correlation_id                   VARCHAR(64)      NOT NULL,
            es_intento_con_resultado_util    BIT              NOT NULL DEFAULT 0,

            CONSTRAINT pk_cartera_solicitud_cupo_intentos
                PRIMARY KEY CLUSTERED (id_intento),

            CONSTRAINT fk_cartera_solicitud_cupo_intentos_solicitud
                FOREIGN KEY (id_solicitud) REFERENCES dbo.cartera_solicitudes_cupo (id_solicitud),

            CONSTRAINT uq_cartera_solicitud_cupo_intentos_idempotency_key
                UNIQUE (idempotency_key),

            CONSTRAINT uq_cartera_solicitud_cupo_intentos_solicitud_numero
                UNIQUE (id_solicitud, numero_intento),

            CONSTRAINT ck_cartera_solicitud_cupo_intentos_numero_intento
                CHECK (numero_intento >= 1)
        );

        SET @objIdIntento = OBJECT_ID('dbo.cartera_solicitud_cupo_intentos', 'U');
        PRINT 'OK: tabla dbo.cartera_solicitud_cupo_intentos creada';
    END
    ELSE
    BEGIN
        PRINT 'INFO: dbo.cartera_solicitud_cupo_intentos ya existe — verificando estructura crítica...';

        DECLARE @columnasFaltantesIntento NVARCHAR(400);
        SELECT @columnasFaltantesIntento = STRING_AGG(req.col, ', ')
        FROM (VALUES
            ('id_intento'), ('id_solicitud'), ('numero_intento'), ('idempotency_key'),
            ('fecha_inicio'), ('fecha_fin'), ('resultado_tecnico'), ('http_status_observado'),
            ('content_status_observado'), ('correlation_id'), ('es_intento_con_resultado_util')
        ) AS req(col)
        WHERE NOT EXISTS (
            SELECT 1 FROM sys.columns c WHERE c.object_id = @objIdIntento AND c.name = req.col
        );

        IF @columnasFaltantesIntento IS NOT NULL
            THROW 56030, N'Migración 035 abortada: dbo.cartera_solicitud_cupo_intentos ya existe pero le faltan columnas requeridas. No se agregan columnas automáticamente — revisar manualmente.', 1;

        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdIntento AND c.name = 'idempotency_key' AND ty.name = 'uniqueidentifier'
        )
            THROW 56031, N'Migración 035 abortada: dbo.cartera_solicitud_cupo_intentos.idempotency_key no es uniqueidentifier. Revisar manualmente.', 1;

        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c
            WHERE c.object_id = @objIdIntento AND c.name = 'resultado_tecnico' AND c.is_nullable = 1
        )
            THROW 56032, N'Migración 035 abortada: dbo.cartera_solicitud_cupo_intentos.resultado_tecnico ya existe pero no es NULL — debe permitir NULL (PRE-CALL). Revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = @objIdIntento AND type = 'PK')
            THROW 56033, N'Migración 035 abortada: dbo.cartera_solicitud_cupo_intentos existe pero no tiene clave primaria. Revisar manualmente.', 1;

        -- Hardening 009: valida que la FK conecte exactamente id_solicitud con
        -- dbo.cartera_solicitudes_cupo.id_solicitud, no solo que exista el nombre.
        IF NOT EXISTS (
            SELECT 1 FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fk.parent_object_id = @objIdIntento AND fk.name = 'fk_cartera_solicitud_cupo_intentos_solicitud'
              AND fk.referenced_object_id = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U')
              AND pc.name = 'id_solicitud' AND rc.name = 'id_solicitud'
        )
            THROW 56034, N'Migración 035 abortada: falta fk_cartera_solicitud_cupo_intentos_solicitud o no conecta id_solicitud -> dbo.cartera_solicitudes_cupo.id_solicitud. Revisar manualmente.', 1;

        -- Hardening 009: valida que la UNIQUE proteja exactamente las columnas
        -- esperadas (y, en el caso compuesto, en ese orden lógico) — no solo que
        -- exista una restricción UQ con ese nombre.
        IF NOT EXISTS (
            SELECT 1
            FROM sys.key_constraints kc
            JOIN sys.indexes i ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE kc.parent_object_id = @objIdIntento AND kc.type = 'UQ' AND kc.name = 'uq_cartera_solicitud_cupo_intentos_idempotency_key'
            GROUP BY kc.name
            HAVING STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) = 'idempotency_key'
        )
            THROW 56035, N'Migración 035 abortada: falta uq_cartera_solicitud_cupo_intentos_idempotency_key o no protege exactamente (idempotency_key) — es el mecanismo real de unicidad de Idempotency-Key, no un candado aplicativo. Revisar manualmente.', 1;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.key_constraints kc
            JOIN sys.indexes i ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE kc.parent_object_id = @objIdIntento AND kc.type = 'UQ' AND kc.name = 'uq_cartera_solicitud_cupo_intentos_solicitud_numero'
            GROUP BY kc.name
            HAVING STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) = 'id_solicitud,numero_intento'
        )
            THROW 56036, N'Migración 035 abortada: falta uq_cartera_solicitud_cupo_intentos_solicitud_numero o no protege exactamente (id_solicitud, numero_intento) en ese orden. Revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @objIdIntento AND name = 'ck_cartera_solicitud_cupo_intentos_numero_intento')
            THROW 56037, N'Migración 035 abortada: falta ck_cartera_solicitud_cupo_intentos_numero_intento. Revisar manualmente.', 1;

        PRINT 'OK: estructura crítica de dbo.cartera_solicitud_cupo_intentos verificada';
    END

    COMMIT TRANSACTION;
    PRINT '=== FIN MIGRACIÓN 035 (COMMIT OK) ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '=== ERROR EN MIGRACIÓN 035 — ROLLBACK EJECUTADO ===';
    THROW;
END CATCH;

-- ── Verificación final ───────────────────────────────────────────────────
-- Banderas booleanas independientes + conteos — confirmación legible para
-- quien ejecute el script, no el mecanismo de aplicación de la política
-- (eso ya ocurrió vía THROW dentro de la transacción).

DECLARE @objIdSolicitudFinal INT = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U');
DECLARE @objIdIntentoFinal INT = OBJECT_ID('dbo.cartera_solicitud_cupo_intentos', 'U');

-- Hardening 009: columnas_*_ok y checks_solicitudes_ok pasaron de exigir un
-- COUNT(*) total exacto a verificar PRESENCIA de los elementos que esta
-- migración requiere (COUNT de columnas/checks CUYO NOMBRE está en la lista
-- requerida). Responde "¿existe todo lo que 035 necesita?" en vez de
-- "¿la tabla es exactamente idéntica a 035 para siempre?" — una columna o
-- CHECK adicional legítimo agregado por una migración futura ya no produce
-- un falso REVISAR aquí.
SELECT
    CASE WHEN @objIdSolicitudFinal IS NOT NULL THEN 1 ELSE 0 END AS tabla_solicitudes_ok,
    CASE WHEN (
        SELECT COUNT(*) FROM sys.columns WHERE object_id = @objIdSolicitudFinal AND name IN (
            'id_solicitud','id_usuario','id_persona','monto_solicitado','estado_solicitud','decision_crediticia',
            'monto_aprobado','codigo_motivo_decision','id_politica_aplicada','score_datacredito_minimo_aplicado',
            'cupo_minimo_aplicado','cupo_maximo_aplicado','edad_minima_aplicada','edad_maxima_aplicada',
            'edad_calculada_al_momento','score_observado','estado_score','viabilidad_observada',
            'rating_recaudos_observado','monto_sugerido_observado','numero_intento','id_cupo_ordinario',
            'correlation_id','fecha_solicitud','fecha_decision','fecha_materializacion_cupo','fecha_actualizacion'
        )
    ) = 27 THEN 1 ELSE 0 END AS columnas_solicitudes_ok,
    CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @objIdSolicitudFinal AND name = 'fk_cartera_solicitudes_cupo_usuario') THEN 1 ELSE 0 END AS fk_usuario_ok,
    CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @objIdSolicitudFinal AND name = 'fk_cartera_solicitudes_cupo_persona') THEN 1 ELSE 0 END AS fk_persona_ok,
    CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @objIdSolicitudFinal AND name = 'fk_cartera_solicitudes_cupo_politica') THEN 1 ELSE 0 END AS fk_politica_ok,
    CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @objIdSolicitudFinal AND name = 'fk_cartera_solicitudes_cupo_cupo_ordinario') THEN 1 ELSE 0 END AS fk_cupo_ordinario_ok,
    CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdSolicitudFinal AND name = 'ux_cartera_solicitudes_cupo_usuario_activa' AND is_unique = 1) THEN 1 ELSE 0 END AS indice_filtrado_activa_ok,
    (SELECT filter_definition FROM sys.indexes WHERE object_id = @objIdSolicitudFinal AND name = 'ux_cartera_solicitudes_cupo_usuario_activa') AS indice_filtrado_activa_filtro,
    CASE WHEN (
        SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = @objIdSolicitudFinal AND name IN (
            'ck_cartera_solicitudes_cupo_monto_solicitado','ck_cartera_solicitudes_cupo_numero_intento',
            'ck_cartera_solicitudes_cupo_cupo_minimo','ck_cartera_solicitudes_cupo_cupo_maximo_vs_minimo',
            'ck_cartera_solicitudes_cupo_edad_calculada'
        )
    ) = 5 THEN 1 ELSE 0 END AS checks_solicitudes_ok,
    CASE WHEN @objIdIntentoFinal IS NOT NULL THEN 1 ELSE 0 END AS tabla_intentos_ok,
    CASE WHEN (
        SELECT COUNT(*) FROM sys.columns WHERE object_id = @objIdIntentoFinal AND name IN (
            'id_intento','id_solicitud','numero_intento','idempotency_key','fecha_inicio','fecha_fin',
            'resultado_tecnico','http_status_observado','content_status_observado','correlation_id',
            'es_intento_con_resultado_util'
        )
    ) = 11 THEN 1 ELSE 0 END AS columnas_intentos_ok,
    CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @objIdIntentoFinal AND name = 'fk_cartera_solicitud_cupo_intentos_solicitud') THEN 1 ELSE 0 END AS fk_intento_solicitud_ok,
    CASE WHEN EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = @objIdIntentoFinal AND type = 'UQ' AND name = 'uq_cartera_solicitud_cupo_intentos_idempotency_key') THEN 1 ELSE 0 END AS unique_idempotency_key_ok,
    CASE WHEN EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = @objIdIntentoFinal AND type = 'UQ' AND name = 'uq_cartera_solicitud_cupo_intentos_solicitud_numero') THEN 1 ELSE 0 END AS unique_solicitud_numero_ok,
    (SELECT COUNT(*) FROM dbo.cartera_solicitudes_cupo) AS filas_solicitudes_actuales,
    (SELECT COUNT(*) FROM dbo.cartera_solicitud_cupo_intentos) AS filas_intentos_actuales;

DECLARE @columnasSolicitudPresentesFinal INT = (
    SELECT COUNT(*) FROM sys.columns WHERE object_id = @objIdSolicitudFinal AND name IN (
        'id_solicitud','id_usuario','id_persona','monto_solicitado','estado_solicitud','decision_crediticia',
        'monto_aprobado','codigo_motivo_decision','id_politica_aplicada','score_datacredito_minimo_aplicado',
        'cupo_minimo_aplicado','cupo_maximo_aplicado','edad_minima_aplicada','edad_maxima_aplicada',
        'edad_calculada_al_momento','score_observado','estado_score','viabilidad_observada',
        'rating_recaudos_observado','monto_sugerido_observado','numero_intento','id_cupo_ordinario',
        'correlation_id','fecha_solicitud','fecha_decision','fecha_materializacion_cupo','fecha_actualizacion'
    )
);
DECLARE @columnasIntentoPresentesFinal INT = (
    SELECT COUNT(*) FROM sys.columns WHERE object_id = @objIdIntentoFinal AND name IN (
        'id_intento','id_solicitud','numero_intento','idempotency_key','fecha_inicio','fecha_fin',
        'resultado_tecnico','http_status_observado','content_status_observado','correlation_id',
        'es_intento_con_resultado_util'
    )
);

DECLARE @resultadoFinal035 NVARCHAR(20) =
    CASE WHEN
        @objIdSolicitudFinal IS NOT NULL
        AND @objIdIntentoFinal IS NOT NULL
        AND @columnasSolicitudPresentesFinal = 27
        AND @columnasIntentoPresentesFinal = 11
        AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdSolicitudFinal AND name = 'ux_cartera_solicitudes_cupo_usuario_activa' AND is_unique = 1)
        AND EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = @objIdIntentoFinal AND type = 'UQ' AND name = 'uq_cartera_solicitud_cupo_intentos_idempotency_key')
    THEN N'OK — migración 035 aplicada y verificada'
    ELSE N'REVISAR — algún componente no coincide con el esperado; ver columnas anteriores'
    END;
SELECT @resultadoFinal035 AS resultado;

PRINT '=== VERIFICACIÓN COMPLETA ===';
