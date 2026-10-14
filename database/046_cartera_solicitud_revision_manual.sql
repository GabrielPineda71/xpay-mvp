-- XPAY-702 - Migracion 046: resolucion manual de solicitudes de cupo.
-- Una fila por solicitud (UNIQUE id_solicitud): no hay edicion posterior.
-- Preserva el resultado automatico original (decision, estado, motivo y
-- fecha). La tabla cartera_solicitud_cupo_motivos_decision NO se modifica.
-- El monto manual lo calcula XPAY por politica; nunca viene del request.
-- Idempotente: crea si no existe; si existe, verifica columnas criticas.
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
    PRINT '=== INICIO MIGRACION 046: revision manual de solicitudes de cupo ===';

    IF OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U') IS NULL
        THROW 59460, N'Migracion 046 abortada: falta dbo.cartera_solicitudes_cupo.', 1;
    IF OBJECT_ID('dbo.usuarios', 'U') IS NULL
        THROW 59461, N'Migracion 046 abortada: falta dbo.usuarios.', 1;

    DECLARE @obj INT = OBJECT_ID('dbo.cartera_solicitud_cupo_revisiones', 'U');
    IF @obj IS NULL
    BEGIN
        CREATE TABLE dbo.cartera_solicitud_cupo_revisiones (
            id_revision                    BIGINT IDENTITY(1,1) NOT NULL,
            id_solicitud                   BIGINT          NOT NULL,
            decision_manual                VARCHAR(20)     NOT NULL,
            monto_aprobado_manual          DECIMAL(18,2)   NULL,
            regla_aplicada                 VARCHAR(60)     NOT NULL,
            motivo_codigo                  VARCHAR(50)     NOT NULL,
            observacion                    NVARCHAR(500)   NULL,
            id_admin                       BIGINT          NOT NULL,
            fecha_resolucion_utc           DATETIME2       NOT NULL,
            decision_automatica_original   VARCHAR(20)     NOT NULL,
            estado_automatico_original     VARCHAR(30)     NOT NULL,
            motivo_automatico_original     VARCHAR(50)     NULL,
            fecha_decision_automatica_original DATETIME2   NULL,
            CONSTRAINT pk_cartera_solicitud_cupo_revisiones PRIMARY KEY CLUSTERED (id_revision),
            CONSTRAINT uq_cartera_solicitud_cupo_revisiones_solicitud UNIQUE (id_solicitud),
            CONSTRAINT fk_cartera_solicitud_cupo_revisiones_solicitud
                FOREIGN KEY (id_solicitud) REFERENCES dbo.cartera_solicitudes_cupo (id_solicitud),
            CONSTRAINT fk_cartera_solicitud_cupo_revisiones_admin
                FOREIGN KEY (id_admin) REFERENCES dbo.usuarios (id_usuario),
            CONSTRAINT ck_cartera_solicitud_cupo_revisiones_decision
                CHECK (decision_manual IN ('APROBADA', 'RECHAZADA')),
            CONSTRAINT ck_cartera_solicitud_cupo_revisiones_monto
                CHECK ((decision_manual = 'APROBADA' AND monto_aprobado_manual > 0)
                    OR (decision_manual = 'RECHAZADA' AND monto_aprobado_manual IS NULL))
        );
        PRINT 'OK: tabla dbo.cartera_solicitud_cupo_revisiones creada';
    END
    ELSE
    BEGIN
        PRINT 'INFO: dbo.cartera_solicitud_cupo_revisiones ya existe - verificando estructura...';
        DECLARE @faltan NVARCHAR(600);
        SELECT @faltan = STRING_AGG(req.col, ', ')
        FROM (VALUES ('id_revision'), ('id_solicitud'), ('decision_manual'),
                     ('monto_aprobado_manual'), ('regla_aplicada'), ('motivo_codigo'),
                     ('observacion'), ('id_admin'), ('fecha_resolucion_utc'),
                     ('decision_automatica_original'), ('estado_automatico_original'),
                     ('motivo_automatico_original'),
                     ('fecha_decision_automatica_original')) AS req(col)
        WHERE NOT EXISTS (SELECT 1 FROM sys.columns c
                          WHERE c.object_id = @obj AND c.name = req.col);
        IF @faltan IS NOT NULL
            THROW 59462, N'Migracion 046 abortada: la tabla existe pero faltan columnas requeridas.', 1;
        IF NOT EXISTS (SELECT 1 FROM sys.key_constraints
                       WHERE name = 'uq_cartera_solicitud_cupo_revisiones_solicitud')
            THROW 59463, N'Migracion 046 abortada: falta la unicidad por id_solicitud.', 1;
        PRINT 'OK: estructura verificada';
    END

    COMMIT TRANSACTION;
    PRINT '=== FIN MIGRACION 046 ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
