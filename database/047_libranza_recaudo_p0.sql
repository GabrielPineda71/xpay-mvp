-- XPAY P0 Anticipo de Sueldo + Passport - Migracion 047.
-- Cierra en BD los P0 del recaudo de anticipos de nomina:
--   1. libranza_parametros_empresa.monto_minimo_anticipo (minimo comercial
--      de utilizacion, parametrizable; 100.000 COP por defecto).
--   2. passport_webhook_events: unicidad durable (event_type, payment_id)
--      y columna id_lote_recaudo (trazabilidad del recaudo).
--   3. passport_breb_retiros: unicidad de passport_payment_id y columnas de
--      resolucion controlada de envios inciertos (sin payment_id).
--   4. libranza_lotes_recaudo: lote durable empresa + convenio + corte,
--      referencia opaca unica (qr_code_reference), QR dinamico Passport,
--      payment_id y marcas de tiempo financieras.
--   5. libranza_lote_recaudo_detalles: lote -> empleado -> anticipo ->
--      monto. Una obligacion nunca pertenece a dos lotes vigentes/aplicados.
--
-- Idempotente: crea solo lo que falta; si un objeto ya existe se verifica
-- y ante una discrepancia critica se aborta con THROW. NO borra ni corrige
-- datos: si existen duplicados que impiden una restriccion UNIQUE, aborta
-- y exige revision manual. NO modifica 045/046. NO se aplica en este ticket
-- a QA ni a PROD.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

PRINT '=== INICIO MIGRACION 047: recaudo libranza P0 ===';
IF OBJECT_ID('dbo.libranza_anticipos', 'U') IS NULL
    THROW 59470, N'Migracion 047 abortada: falta dbo.libranza_anticipos.', 1;
IF OBJECT_ID('dbo.libranza_parametros_empresa', 'U') IS NULL
    THROW 59470, N'Migracion 047 abortada: falta dbo.libranza_parametros_empresa.', 1;
IF OBJECT_ID('dbo.passport_webhook_events', 'U') IS NULL
    THROW 59470, N'Migracion 047 abortada: falta dbo.passport_webhook_events.', 1;
IF OBJECT_ID('dbo.passport_breb_retiros', 'U') IS NULL
    THROW 59470, N'Migracion 047 abortada: falta dbo.passport_breb_retiros.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.ledger_cuentas WHERE codigo = '110103' AND id_unidad_negocio = 1)
    THROW 59470, N'Migracion 047 abortada: falta la cuenta ledger 110103 (migracion 044).', 1;
GO

-- ── 1. Monto minimo de anticipo (parametrizable) ─────────────────────────────
IF COL_LENGTH('dbo.libranza_parametros_empresa', 'monto_minimo_anticipo') IS NULL
BEGIN
    ALTER TABLE dbo.libranza_parametros_empresa
        ADD monto_minimo_anticipo DECIMAL(18,2) NOT NULL
            CONSTRAINT df_lpe_monto_minimo_anticipo DEFAULT (100000.00);
    PRINT 'OK: libranza_parametros_empresa.monto_minimo_anticipo creada (100000.00).';
END
ELSE
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
        WHERE c.object_id = OBJECT_ID('dbo.libranza_parametros_empresa')
          AND c.name = 'monto_minimo_anticipo' AND t.name = 'decimal'
          AND c.precision = 18 AND c.scale = 2 AND c.is_nullable = 0)
        THROW 59471, N'Migracion 047 abortada: monto_minimo_anticipo existe con tipo distinto de DECIMAL(18,2) NOT NULL.', 1;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_lpe_monto_minimo_anticipo')
    EXEC sp_executesql N'ALTER TABLE dbo.libranza_parametros_empresa
        ADD CONSTRAINT ck_lpe_monto_minimo_anticipo CHECK (monto_minimo_anticipo > 0);';
GO

-- ── 2. passport_webhook_events: deduplicacion durable ───────────────────────
-- Antes de crear la restriccion se verifica que no existan duplicados
-- historicos. Si existen, se aborta: nunca se borran eventos automaticamente.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'uq_pwh_evento_pago'
               AND object_id = OBJECT_ID('dbo.passport_webhook_events'))
BEGIN
    IF EXISTS (
        SELECT event_type, passport_payment_id
        FROM dbo.passport_webhook_events
        WHERE event_type IS NOT NULL AND passport_payment_id IS NOT NULL
        GROUP BY event_type, passport_payment_id
        HAVING COUNT(*) > 1)
        THROW 59472, N'Migracion 047 abortada: passport_webhook_events tiene duplicados (event_type, passport_payment_id). Revisar manualmente antes de crear uq_pwh_evento_pago.', 1;

    CREATE UNIQUE INDEX uq_pwh_evento_pago
        ON dbo.passport_webhook_events (event_type, passport_payment_id)
        WHERE event_type IS NOT NULL AND passport_payment_id IS NOT NULL;
    PRINT 'OK: uq_pwh_evento_pago creado.';
END
GO
IF COL_LENGTH('dbo.passport_webhook_events', 'id_lote_recaudo') IS NULL
BEGIN
    ALTER TABLE dbo.passport_webhook_events ADD id_lote_recaudo BIGINT NULL;
    PRINT 'OK: passport_webhook_events.id_lote_recaudo creada.';
END
GO

-- ── 3. passport_breb_retiros: payment_id unico + resolucion de inciertos ─────
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'uq_breb_retiro_payment_id'
               AND object_id = OBJECT_ID('dbo.passport_breb_retiros'))
BEGIN
    IF EXISTS (
        SELECT passport_payment_id FROM dbo.passport_breb_retiros
        WHERE passport_payment_id IS NOT NULL
        GROUP BY passport_payment_id HAVING COUNT(*) > 1)
        THROW 59473, N'Migracion 047 abortada: passport_breb_retiros tiene passport_payment_id duplicados. Revisar manualmente.', 1;

    CREATE UNIQUE INDEX uq_breb_retiro_payment_id
        ON dbo.passport_breb_retiros (passport_payment_id)
        WHERE passport_payment_id IS NOT NULL;
    PRINT 'OK: uq_breb_retiro_payment_id creado.';
END
GO
IF COL_LENGTH('dbo.passport_breb_retiros', 'resolucion_incierta_tipo') IS NULL
BEGIN
    ALTER TABLE dbo.passport_breb_retiros ADD
        resolucion_incierta_tipo      VARCHAR(30)   NULL,
        resolucion_incierta_evidencia NVARCHAR(500) NULL,
        resolucion_incierta_por       BIGINT        NULL,
        resolucion_incierta_fecha     DATETIME2     NULL;
    PRINT 'OK: columnas de resolucion de envio incierto creadas.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_breb_retiro_resolucion_incierta')
    EXEC sp_executesql N'ALTER TABLE dbo.passport_breb_retiros
        ADD CONSTRAINT ck_breb_retiro_resolucion_incierta CHECK (
            (resolucion_incierta_tipo IS NULL
                AND resolucion_incierta_evidencia IS NULL
                AND resolucion_incierta_por IS NULL
                AND resolucion_incierta_fecha IS NULL)
            OR (resolucion_incierta_tipo IN (''PAGO_ENCONTRADO'', ''RECHAZO_CONFIRMADO'')
                AND resolucion_incierta_evidencia IS NOT NULL
                AND resolucion_incierta_por IS NOT NULL
                AND resolucion_incierta_fecha IS NOT NULL));';
GO

-- ── 4. libranza_lotes_recaudo ───────────────────────────────────────────────
IF OBJECT_ID('dbo.libranza_lotes_recaudo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.libranza_lotes_recaudo (
        id_lote                         BIGINT IDENTITY(1,1) NOT NULL,
        id_convenio                     BIGINT          NOT NULL,
        fecha_corte                     DATE            NOT NULL,
        estado                          VARCHAR(30)     NOT NULL,
        -- Referencia opaca, unica e inmutable. Es exactamente el
        -- qr_code_reference enviado a Passport (<= 17, alfanumerico, sin 'P').
        referencia_publica              VARCHAR(17)     NOT NULL,
        monto_total                     DECIMAL(18,2)   NOT NULL,
        moneda                          CHAR(3)         NOT NULL CONSTRAINT df_llr_moneda DEFAULT ('COP'),
        cantidad_detalles               INT             NOT NULL,
        fecha_creacion                  DATETIME2       NOT NULL,
        creado_por_usuario              BIGINT          NOT NULL,
        fecha_cierre                    DATETIME2       NULL,
        cerrado_por_usuario             BIGINT          NULL,
        -- QR dinamico Passport
        qr_estado                       VARCHAR(20)     NULL,
        fecha_qr_intento                DATETIME2       NULL,
        passport_qr_id                  NVARCHAR(100)   NULL,
        qr_code_data                    NVARCHAR(1000)  NULL,
        fecha_qr_generado               DATETIME2       NULL,
        -- Pago entrante Passport
        passport_payment_id             NVARCHAR(100)   NULL,
        end_to_end_identification       NVARCHAR(100)   NULL,
        fecha_pago_recibido             DATETIME2       NULL,
        fecha_fondos_confirmados        DATETIME2       NULL,
        fecha_aplicacion                DATETIME2       NULL,
        id_transaccion_ledger_aplicacion BIGINT         NULL,
        id_evento_settled               BIGINT          NULL,
        ultimo_rechazo_codigo           NVARCHAR(50)    NULL,
        fecha_ultimo_rechazo            DATETIME2       NULL,
        motivo_revision                 NVARCHAR(500)   NULL,
        CONSTRAINT pk_llr PRIMARY KEY (id_lote),
        CONSTRAINT fk_llr_convenio FOREIGN KEY (id_convenio)
            REFERENCES dbo.libranza_empresas_convenio (id_convenio),
        CONSTRAINT uq_llr_referencia UNIQUE (referencia_publica),
        CONSTRAINT ck_llr_estado CHECK (estado IN (
            'BORRADOR', 'PENDIENTE_PAGO', 'PAGO_EN_PROCESO',
            'APLICADO', 'REQUIERE_REVISION', 'CANCELADO')),
        CONSTRAINT ck_llr_referencia CHECK (
            LEN(referencia_publica) BETWEEN 8 AND 17
            AND referencia_publica NOT LIKE '%[^0-9A-Z]%'
            AND referencia_publica NOT LIKE '%P%'),
        CONSTRAINT ck_llr_monto CHECK (monto_total > 0),
        CONSTRAINT ck_llr_moneda CHECK (moneda = 'COP'),
        CONSTRAINT ck_llr_cantidad CHECK (cantidad_detalles > 0),
        CONSTRAINT ck_llr_qr_estado CHECK (qr_estado IS NULL OR qr_estado IN ('EN_CURSO', 'GENERADO')),
        CONSTRAINT ck_llr_qr_generado CHECK (
            qr_estado IS NULL OR qr_estado <> 'GENERADO'
            OR (passport_qr_id IS NOT NULL AND qr_code_data IS NOT NULL AND fecha_qr_generado IS NOT NULL)),
        CONSTRAINT ck_llr_aplicado CHECK (
            estado <> 'APLICADO'
            OR (passport_payment_id IS NOT NULL
                AND id_transaccion_ledger_aplicacion IS NOT NULL
                AND fecha_aplicacion IS NOT NULL
                AND fecha_cierre IS NOT NULL))
    );

    -- Un solo lote abierto por convenio y corte (creacion idempotente).
    CREATE UNIQUE INDEX uq_llr_convenio_corte_abierto
        ON dbo.libranza_lotes_recaudo (id_convenio, fecha_corte)
        WHERE estado IN ('BORRADOR', 'PENDIENTE_PAGO', 'PAGO_EN_PROCESO', 'REQUIERE_REVISION');
    -- Un pago Passport corresponde a lo sumo a un lote.
    CREATE UNIQUE INDEX uq_llr_payment_id
        ON dbo.libranza_lotes_recaudo (passport_payment_id)
        WHERE passport_payment_id IS NOT NULL;
    CREATE INDEX ix_llr_convenio_estado ON dbo.libranza_lotes_recaudo (id_convenio, estado);
    PRINT 'OK: dbo.libranza_lotes_recaudo creada.';
END
ELSE
BEGIN
    IF COL_LENGTH('dbo.libranza_lotes_recaudo', 'referencia_publica') IS NULL
       OR COL_LENGTH('dbo.libranza_lotes_recaudo', 'monto_total') IS NULL
       OR COL_LENGTH('dbo.libranza_lotes_recaudo', 'passport_payment_id') IS NULL
       OR NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'uq_llr_referencia')
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'uq_llr_payment_id')
       OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'uq_llr_convenio_corte_abierto')
        THROW 59474, N'Migracion 047 abortada: dbo.libranza_lotes_recaudo existe sin la estructura critica esperada.', 1;
    PRINT 'OK: dbo.libranza_lotes_recaudo ya existe y fue verificada.';
END
GO

-- ── 5. libranza_lote_recaudo_detalles ───────────────────────────────────────
IF OBJECT_ID('dbo.libranza_lote_recaudo_detalles', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.libranza_lote_recaudo_detalles (
        id_detalle        BIGINT IDENTITY(1,1) NOT NULL,
        id_lote           BIGINT          NOT NULL,
        id_anticipo       BIGINT          NOT NULL,
        id_empleado       BIGINT          NOT NULL,
        monto_capital     DECIMAL(18,2)   NOT NULL,
        monto_comision    DECIMAL(18,2)   NOT NULL,
        monto_iva         DECIMAL(18,2)   NOT NULL,
        monto_total       DECIMAL(18,2)   NOT NULL,
        estado            VARCHAR(20)     NOT NULL,
        fecha_creacion    DATETIME2       NOT NULL,
        fecha_aplicacion  DATETIME2       NULL,
        CONSTRAINT pk_llrd PRIMARY KEY (id_detalle),
        CONSTRAINT fk_llrd_lote FOREIGN KEY (id_lote) REFERENCES dbo.libranza_lotes_recaudo (id_lote),
        CONSTRAINT fk_llrd_anticipo FOREIGN KEY (id_anticipo) REFERENCES dbo.libranza_anticipos (id_anticipo),
        CONSTRAINT fk_llrd_empleado FOREIGN KEY (id_empleado) REFERENCES dbo.libranza_empleados (id_empleado),
        CONSTRAINT uq_llrd_lote_anticipo UNIQUE (id_lote, id_anticipo),
        CONSTRAINT ck_llrd_estado CHECK (estado IN ('VIGENTE', 'APLICADO', 'LIBERADO')),
        CONSTRAINT ck_llrd_montos CHECK (
            monto_capital > 0 AND monto_comision >= 0 AND monto_iva >= 0
            AND monto_total = monto_capital + monto_comision + monto_iva),
        CONSTRAINT ck_llrd_aplicado CHECK (estado <> 'APLICADO' OR fecha_aplicacion IS NOT NULL)
    );

    -- Una obligacion no puede pertenecer simultaneamente a dos lotes
    -- vigentes o aplicados (LIBERADO = lote cancelado: puede re-agruparse).
    CREATE UNIQUE INDEX uq_llrd_anticipo_activo
        ON dbo.libranza_lote_recaudo_detalles (id_anticipo)
        WHERE estado IN ('VIGENTE', 'APLICADO');
    CREATE INDEX ix_llrd_lote ON dbo.libranza_lote_recaudo_detalles (id_lote, estado);
    PRINT 'OK: dbo.libranza_lote_recaudo_detalles creada.';
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'uq_llrd_anticipo_activo')
       OR NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'uq_llrd_lote_anticipo')
        THROW 59475, N'Migracion 047 abortada: dbo.libranza_lote_recaudo_detalles existe sin sus restricciones UNIQUE.', 1;
    PRINT 'OK: dbo.libranza_lote_recaudo_detalles ya existe y fue verificada.';
END
GO

PRINT '=== FIN MIGRACION 047 ===';
GO
