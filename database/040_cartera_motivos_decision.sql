-- =====================================================================
-- Migración 040: Cartera Ordinaria — persistencia de la decisión crediticia
-- M2.4b (motor DORMIDO). Contrato cerrado en XPAY-181 / XPAY-182.
--
-- A. CREATE TABLE dbo.cartera_solicitud_cupo_motivos_decision
--      id_motivo      BIGINT IDENTITY(1,1) NOT NULL  (PK)
--      id_solicitud   BIGINT NOT NULL                (FK -> cartera_solicitudes_cupo)
--      orden          SMALLINT NOT NULL              (1-based, sin huecos)
--      codigo_motivo  VARCHAR(50) NOT NULL           (vocabulario controlado en CÓDIGO — sin CHECK aquí)
--    Constraints: UNIQUE(id_solicitud, orden) ; UNIQUE(id_solicitud, codigo_motivo) ; CHECK(orden >= 1).
--    SIN es_primario, SIN fecha_registro, SIN texto libre, SIN CHECK de catálogo, SIN índice adicional.
--
-- B. ADD dbo.cartera_solicitudes_cupo.senal_posible_suplantacion BIT NULL
--      NULL = no evaluada / no disponible ; 0 = evaluada, señal ausente ; 1 = señal presente.
--      NO es motivo crediticio. NO es fraude probado.
--
-- C. Recrea el índice único filtrado ux_cartera_solicitudes_cupo_usuario_activa
--    para incluir 'PENDIENTE_REVISION_MANUAL' (una solicitud NO_DECIDIBLE pendiente de
--    revisión manual bloquea una segunda solicitud paralela del mismo usuario).
--    SQL Server no permite ALTER del predicado de un índice filtrado -> DROP + CREATE.
--    Riesgo de datos históricos: NINGUNO — 'PENDIENTE_REVISION_MANUAL' no existe hoy en
--    ninguna fila, así que recrear con ese estado añadido no hace elegible ninguna fila
--    nueva y no puede violar la unicidad.
--
-- D. SIN backfill (0 filas históricas con decisión != PENDIENTE ; tabla hija nace vacía).
--
-- E. NO altera decision_crediticia / estado_solicitud / codigo_motivo_decision /
--    monto_aprobado / fecha_decision — sus tipos actuales (VARCHAR(20)/VARCHAR(30)/
--    VARCHAR(50)/DECIMAL(18,2) NULL/DATETIME2 NULL, todos SIN CHECK) ya soportan los
--    valores nuevos NO_DECIDIBLE / PENDIENTE_REVISION_MANUAL / monto 0 / monto NULL.
--
-- INFRAESTRUCTURA DORMIDA: la tabla y la columna sólo las escribe
-- CarteraDecisionCrediticiaStore.AplicarDecisionAsync (ICarteraDecisionCrediticia),
-- que NO está registrada en DI, NO tiene caller de runtime y NO emite ninguna
-- transición automática hoy.
--
-- Idempotente y fail-fast — mismo patrón que 035/038/039.
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

    PRINT '=== INICIO MIGRACIÓN 040: persistencia de decisión crediticia M2.4b (dormida) ===';

    DECLARE @objIdSolicitud INT = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U');
    IF @objIdSolicitud IS NULL
        THROW 59200, N'Migración 040 abortada: dbo.cartera_solicitudes_cupo no existe (falta migración 035).', 1;

    ---------------------------------------------------------------------------
    -- A. dbo.cartera_solicitud_cupo_motivos_decision
    ---------------------------------------------------------------------------
    DECLARE @objIdMotivos INT = OBJECT_ID('dbo.cartera_solicitud_cupo_motivos_decision', 'U');

    IF @objIdMotivos IS NULL
    BEGIN
        CREATE TABLE dbo.cartera_solicitud_cupo_motivos_decision (
            id_motivo       BIGINT      IDENTITY(1,1) NOT NULL,
            id_solicitud    BIGINT      NOT NULL,
            orden           SMALLINT    NOT NULL,
            codigo_motivo   VARCHAR(50) NOT NULL,

            CONSTRAINT pk_cartera_solicitud_cupo_motivos_decision
                PRIMARY KEY CLUSTERED (id_motivo),

            CONSTRAINT fk_cartera_solicitud_cupo_motivos_decision_solicitud
                FOREIGN KEY (id_solicitud) REFERENCES dbo.cartera_solicitudes_cupo (id_solicitud),

            CONSTRAINT uq_cartera_solicitud_cupo_motivos_decision_sol_orden
                UNIQUE (id_solicitud, orden),

            CONSTRAINT uq_cartera_solicitud_cupo_motivos_decision_sol_codigo
                UNIQUE (id_solicitud, codigo_motivo),

            CONSTRAINT ck_cartera_solicitud_cupo_motivos_decision_orden
                CHECK (orden >= 1)
        );

        SET @objIdMotivos = OBJECT_ID('dbo.cartera_solicitud_cupo_motivos_decision', 'U');
        PRINT 'OK: tabla dbo.cartera_solicitud_cupo_motivos_decision creada';
    END
    ELSE
    BEGIN
        PRINT 'INFO: dbo.cartera_solicitud_cupo_motivos_decision ya existe — verificando estructura crítica...';

        DECLARE @colsFaltantesMotivos NVARCHAR(400);
        SELECT @colsFaltantesMotivos = STRING_AGG(req.col, ', ')
        FROM (VALUES ('id_motivo'), ('id_solicitud'), ('orden'), ('codigo_motivo')) AS req(col)
        WHERE NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = @objIdMotivos AND c.name = req.col);

        IF @colsFaltantesMotivos IS NOT NULL
            THROW 59201, N'Migración 040 abortada: cartera_solicitud_cupo_motivos_decision ya existe pero le faltan columnas requeridas. Revisar manualmente.', 1;

        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdMotivos AND c.name = 'codigo_motivo'
              AND ty.name = 'varchar' AND c.max_length = 50 AND c.is_nullable = 0)
            THROW 59202, N'Migración 040 abortada: codigo_motivo no es VARCHAR(50) NOT NULL. Revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = @objIdMotivos AND type = 'PK')
            THROW 59203, N'Migración 040 abortada: cartera_solicitud_cupo_motivos_decision sin PK. Revisar manualmente.', 1;

        IF NOT EXISTS (
            SELECT 1 FROM sys.foreign_keys fk
            WHERE fk.parent_object_id = @objIdMotivos
              AND fk.name = 'fk_cartera_solicitud_cupo_motivos_decision_solicitud'
              AND fk.referenced_object_id = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U'))
            THROW 59204, N'Migración 040 abortada: falta la FK a cartera_solicitudes_cupo. Revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdMotivos AND name = 'uq_cartera_solicitud_cupo_motivos_decision_sol_orden')
        OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdMotivos AND name = 'uq_cartera_solicitud_cupo_motivos_decision_sol_codigo')
            THROW 59205, N'Migración 040 abortada: falta alguno de los UNIQUE (sol_orden / sol_codigo). Revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @objIdMotivos AND name = 'ck_cartera_solicitud_cupo_motivos_decision_orden')
            THROW 59206, N'Migración 040 abortada: falta el CHECK ck_..._orden. Revisar manualmente.', 1;

        PRINT 'OK: estructura de dbo.cartera_solicitud_cupo_motivos_decision verificada';
    END

    ---------------------------------------------------------------------------
    -- B. dbo.cartera_solicitudes_cupo.senal_posible_suplantacion BIT NULL
    ---------------------------------------------------------------------------
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdSolicitud AND name = 'senal_posible_suplantacion')
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo ADD senal_posible_suplantacion BIT NULL;
        PRINT 'OK: solicitudes.senal_posible_suplantacion agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdSolicitud AND c.name = 'senal_posible_suplantacion'
              AND ty.name = 'bit' AND c.is_nullable = 1)
            THROW 59210, N'Migración 040 abortada: senal_posible_suplantacion ya existe pero no es BIT NULL. Revisar manualmente.', 1;
        PRINT 'INFO: solicitudes.senal_posible_suplantacion ya existe con la estructura esperada — omitida';
    END

    ---------------------------------------------------------------------------
    -- C. Recrear ux_cartera_solicitudes_cupo_usuario_activa con 6 estados
    ---------------------------------------------------------------------------
    DECLARE @filtroActual NVARCHAR(MAX);
    SELECT @filtroActual = i.filter_definition
    FROM sys.indexes i
    WHERE i.object_id = @objIdSolicitud AND i.name = 'ux_cartera_solicitudes_cupo_usuario_activa';

    IF @filtroActual IS NULL
    BEGIN
        -- El índice no existe (o no es filtrado) — crearlo directamente con los 6 estados.
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdSolicitud AND name = 'ux_cartera_solicitudes_cupo_usuario_activa')
            THROW 59220, N'Migración 040 abortada: ux_cartera_solicitudes_cupo_usuario_activa existe pero no es un índice filtrado. Revisar manualmente.', 1;

        CREATE UNIQUE INDEX ux_cartera_solicitudes_cupo_usuario_activa
            ON dbo.cartera_solicitudes_cupo (id_usuario)
            WHERE estado_solicitud IN ('RECIBIDA', 'VALIDANDO', 'CONSULTANDO_RIESGO', 'EN_EVALUACION', 'APROBADA_PENDIENTE_CUPO', 'PENDIENTE_REVISION_MANUAL');
        PRINT 'OK: ux_cartera_solicitudes_cupo_usuario_activa creado (6 estados activos)';
    END
    ELSE IF @filtroActual LIKE '%PENDIENTE_REVISION_MANUAL%'
    BEGIN
        PRINT 'INFO: ux_cartera_solicitudes_cupo_usuario_activa ya incluye PENDIENTE_REVISION_MANUAL — omitido';
    END
    ELSE
    BEGIN
        -- Índice filtrado existente con la definición de 5 estados (035). Verificar
        -- que contiene los 5 esperados antes de recrear (defensa: no recrear sobre
        -- una definición desconocida).
        IF @filtroActual NOT LIKE '%estado_solicitud%'
           OR @filtroActual NOT LIKE '%RECIBIDA%'
           OR @filtroActual NOT LIKE '%VALIDANDO%'
           OR @filtroActual NOT LIKE '%CONSULTANDO_RIESGO%'
           OR @filtroActual NOT LIKE '%EN_EVALUACION%'
           OR @filtroActual NOT LIKE '%APROBADA_PENDIENTE_CUPO%'
            THROW 59221, N'Migración 040 abortada: ux_cartera_solicitudes_cupo_usuario_activa tiene un filtro inesperado (no son los 5 estados de 035). Revisar manualmente — no se recrea.', 1;

        DROP INDEX ux_cartera_solicitudes_cupo_usuario_activa ON dbo.cartera_solicitudes_cupo;
        CREATE UNIQUE INDEX ux_cartera_solicitudes_cupo_usuario_activa
            ON dbo.cartera_solicitudes_cupo (id_usuario)
            WHERE estado_solicitud IN ('RECIBIDA', 'VALIDANDO', 'CONSULTANDO_RIESGO', 'EN_EVALUACION', 'APROBADA_PENDIENTE_CUPO', 'PENDIENTE_REVISION_MANUAL');
        PRINT 'OK: ux_cartera_solicitudes_cupo_usuario_activa recreado con PENDIENTE_REVISION_MANUAL (6 estados)';
    END

    COMMIT TRANSACTION;
    PRINT '=== MIGRACIÓN 040 COMPLETADA ===';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT('MIGRACIÓN 040 ABORTADA: ', ERROR_MESSAGE());
    THROW;
END CATCH;
GO

-- ── Verificación final (idempotente, sólo lectura) ──────────────────────
DECLARE @objIdSolicitudV INT = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U');
DECLARE @objIdMotivosV   INT = OBJECT_ID('dbo.cartera_solicitud_cupo_motivos_decision', 'U');

DECLARE @tablaOk INT = (
    SELECT CASE WHEN @objIdMotivosV IS NOT NULL
                 AND EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                             WHERE c.object_id = @objIdMotivosV AND c.name = 'codigo_motivo'
                               AND ty.name = 'varchar' AND c.max_length = 50 AND c.is_nullable = 0)
                 AND (SELECT COUNT(*) FROM sys.indexes WHERE object_id = @objIdMotivosV
                      AND name IN ('uq_cartera_solicitud_cupo_motivos_decision_sol_orden',
                                   'uq_cartera_solicitud_cupo_motivos_decision_sol_codigo')) = 2
           THEN 1 ELSE 0 END
);

DECLARE @senalOk INT = (
    SELECT CASE WHEN EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objIdSolicitudV AND c.name = 'senal_posible_suplantacion'
          AND ty.name = 'bit' AND c.is_nullable = 1
    ) THEN 1 ELSE 0 END
);

DECLARE @indiceOk INT = (
    SELECT CASE WHEN EXISTS (
        SELECT 1 FROM sys.indexes i
        WHERE i.object_id = @objIdSolicitudV AND i.name = 'ux_cartera_solicitudes_cupo_usuario_activa'
          AND i.is_unique = 1
          AND i.filter_definition LIKE '%RECIBIDA%'
          AND i.filter_definition LIKE '%APROBADA_PENDIENTE_CUPO%'
          AND i.filter_definition LIKE '%PENDIENTE_REVISION_MANUAL%'
    ) THEN 1 ELSE 0 END
);

DECLARE @resultado040 NVARCHAR(90) =
    CASE WHEN @tablaOk = 1 AND @senalOk = 1 AND @indiceOk = 1
         THEN N'OK — migración 040 aplicada y verificada (tabla motivos + señal + índice 6 estados)'
         ELSE N'REVISAR — algún objeto de 040 no coincide con lo esperado'
    END;

SELECT @tablaOk AS tabla_motivos_ok, @senalOk AS senal_suplantacion_ok, @indiceOk AS indice_activo_6_estados_ok, @resultado040 AS resultado;

PRINT '=== VERIFICACIÓN 040 COMPLETA ===';
