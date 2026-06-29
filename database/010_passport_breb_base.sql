/* XPAY MVP V1 - 010_passport_breb_base.sql */
/* Fase 64: Modelo base Passport/Bre-B Sandbox para retiros propios */
/* Idempotente: usa IF NOT EXISTS / IF OBJECT_ID para re-ejecución segura */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- =========================================================================
-- 1. Tabla passport_breb_llaves
--    Registra la llave Bre-B propia de cada usuario/comercio.
--    Se guarda hash y máscara, NUNCA el valor en claro.
--    Una sola llave activa por wallet (índice filtrado es_activa = 1).
-- =========================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'passport_breb_llaves')
BEGIN
    CREATE TABLE passport_breb_llaves (
        id_breb_llave                    BIGINT          IDENTITY(1,1) NOT NULL,
        tipo_sujeto                      NVARCHAR(10)    NOT NULL,   -- USUARIO / COMERCIO
        id_usuario                       BIGINT          NULL,
        id_comercio                      BIGINT          NULL,
        id_wallet                        BIGINT          NOT NULL,
        key_type                         NVARCHAR(10)    NOT NULL,   -- ID / PHONE / EMAIL / ALPHA / BCODE
        key_value_masked                 NVARCHAR(100)   NOT NULL,
        key_value_hash                   NVARCHAR(64)    NOT NULL,   -- SHA-256 hex lowercase
        key_value_encrypted              NVARCHAR(500)   NULL,       -- PENDIENTE Fase 65: cifrado at-rest
        passport_customer_id             NVARCHAR(100)   NULL,
        passport_account_id              NVARCHAR(100)   NULL,
        passport_key_id                  NVARCHAR(100)   NULL,
        owner_identification_type        NVARCHAR(20)    NULL,
        owner_identification_number_masked NVARCHAR(30)  NULL,
        owner_name_masked                NVARCHAR(100)   NULL,
        participant_name                 NVARCHAR(150)   NULL,
        participant_identification_number NVARCHAR(30)   NULL,
        account_type                     NVARCHAR(50)    NULL,
        account_number_masked            NVARCHAR(50)    NULL,
        estado                           NVARCHAR(30)    NOT NULL CONSTRAINT DF_breb_llave_estado DEFAULT 'PENDIENTE_VALIDACION',
        fecha_registro                   DATETIME2       NOT NULL CONSTRAINT DF_breb_llave_fecha_reg DEFAULT SYSUTCDATETIME(),
        fecha_validacion                 DATETIME2       NULL,
        fecha_actualizacion              DATETIME2       NULL,
        es_activa                        BIT             NOT NULL CONSTRAINT DF_breb_llave_activa DEFAULT 1,
        created_by_usuario               BIGINT          NULL,
        updated_by_usuario               BIGINT          NULL,
        CONSTRAINT PK_breb_llaves        PRIMARY KEY (id_breb_llave),
        CONSTRAINT FK_breb_llave_wallet  FOREIGN KEY (id_wallet) REFERENCES wallets(id_wallet),
        CONSTRAINT CHK_breb_llave_sujeto CHECK (tipo_sujeto IN ('USUARIO', 'COMERCIO')),
        CONSTRAINT CHK_breb_llave_keytype CHECK (key_type IN ('ID', 'PHONE', 'EMAIL', 'ALPHA', 'BCODE')),
        CONSTRAINT CHK_breb_llave_usuario_comercio CHECK (
            (tipo_sujeto = 'USUARIO' AND id_usuario IS NOT NULL AND id_comercio IS NULL) OR
            (tipo_sujeto = 'COMERCIO' AND id_comercio IS NOT NULL AND id_usuario IS NULL)
        )
    );

    CREATE INDEX IX_breb_llave_wallet    ON passport_breb_llaves (id_wallet);
    CREATE INDEX IX_breb_llave_usuario   ON passport_breb_llaves (id_usuario) WHERE id_usuario IS NOT NULL;
    CREATE INDEX IX_breb_llave_comercio  ON passport_breb_llaves (id_comercio) WHERE id_comercio IS NOT NULL;
    CREATE INDEX IX_breb_llave_estado    ON passport_breb_llaves (estado);

    -- Solo una llave activa por wallet a la vez
    CREATE UNIQUE INDEX UIX_breb_llave_wallet_activa
        ON passport_breb_llaves (id_wallet)
        WHERE es_activa = 1;

    PRINT 'Tabla passport_breb_llaves creada.';
END
ELSE
    PRINT 'Tabla passport_breb_llaves ya existe.';
GO

-- =========================================================================
-- 2. Tabla passport_breb_retiros
--    Retiro desde wallet hacia cuenta bancaria via llave Bre-B.
--    referencia_interna e idempotency_key son únicos.
--    En Fase 64: se crean en estado CREADO sin tocar ledger ni saldo.
--    En Fase 65: el servicio Passport real iniciará el pago.
-- =========================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'passport_breb_retiros')
BEGIN
    CREATE TABLE passport_breb_retiros (
        id_breb_retiro                   BIGINT          IDENTITY(1,1) NOT NULL,
        tipo_sujeto                      NVARCHAR(10)    NOT NULL,   -- USUARIO / COMERCIO
        id_usuario                       BIGINT          NULL,
        id_comercio                      BIGINT          NULL,
        id_wallet                        BIGINT          NOT NULL,
        id_breb_llave                    BIGINT          NOT NULL,
        valor                            DECIMAL(18,2)   NOT NULL,
        moneda                           NVARCHAR(3)     NOT NULL CONSTRAINT DF_breb_retiro_moneda DEFAULT 'COP',
        estado                           NVARCHAR(40)    NOT NULL CONSTRAINT DF_breb_retiro_estado DEFAULT 'CREADO',
        passport_payment_id              NVARCHAR(100)   NULL,
        passport_resolution_id           NVARCHAR(100)   NULL,
        passport_recipient_id            NVARCHAR(100)   NULL,
        referencia_interna               NVARCHAR(50)    NOT NULL,   -- GUID único por retiro
        idempotency_key                  NVARCHAR(100)   NOT NULL,   -- cliente genera, único
        fecha_solicitud                  DATETIME2       NOT NULL CONSTRAINT DF_breb_retiro_fecha DEFAULT SYSUTCDATETIME(),
        fecha_envio_passport             DATETIME2       NULL,
        fecha_confirmacion               DATETIME2       NULL,
        fecha_liquidacion                DATETIME2       NULL,
        fecha_rechazo                    DATETIME2       NULL,
        motivo_rechazo                   NVARCHAR(500)   NULL,
        id_transaccion_ledger            BIGINT          NULL,   -- se llena en Fase 65 al afectar ledger
        created_by_usuario               BIGINT          NULL,
        updated_by_usuario               BIGINT          NULL,
        CONSTRAINT PK_breb_retiros         PRIMARY KEY (id_breb_retiro),
        CONSTRAINT FK_breb_retiro_wallet   FOREIGN KEY (id_wallet)      REFERENCES wallets(id_wallet),
        CONSTRAINT FK_breb_retiro_llave    FOREIGN KEY (id_breb_llave)  REFERENCES passport_breb_llaves(id_breb_llave),
        CONSTRAINT UQ_breb_retiro_ref      UNIQUE (referencia_interna),
        CONSTRAINT UQ_breb_retiro_idem     UNIQUE (idempotency_key),
        CONSTRAINT CHK_breb_retiro_sujeto  CHECK (tipo_sujeto IN ('USUARIO', 'COMERCIO')),
        CONSTRAINT CHK_breb_retiro_valor   CHECK (valor > 0),
        CONSTRAINT CHK_breb_retiro_estado  CHECK (estado IN (
            'CREADO', 'PENDIENTE_VALIDACION_LLAVE', 'LLAVE_VALIDADA',
            'PENDIENTE_ENVIO_PASSPORT', 'ENVIADO_PASSPORT',
            'CONFIRMADO', 'LIQUIDADO', 'RECHAZADO', 'CANCELADO', 'ERROR'
        ))
    );

    CREATE INDEX IX_breb_retiro_wallet   ON passport_breb_retiros (id_wallet);
    CREATE INDEX IX_breb_retiro_usuario  ON passport_breb_retiros (id_usuario)  WHERE id_usuario IS NOT NULL;
    CREATE INDEX IX_breb_retiro_comercio ON passport_breb_retiros (id_comercio) WHERE id_comercio IS NOT NULL;
    CREATE INDEX IX_breb_retiro_estado   ON passport_breb_retiros (estado);
    CREATE INDEX IX_breb_retiro_llave    ON passport_breb_retiros (id_breb_llave);

    PRINT 'Tabla passport_breb_retiros creada.';
END
ELSE
    PRINT 'Tabla passport_breb_retiros ya existe.';
GO

-- =========================================================================
-- 3. Tabla passport_webhook_events
--    Log de todos los webhooks recibidos de Passport/Bre-B.
--    No guardar payload completo si contiene datos bancarios/PII.
--    Se guarda hash del payload y versión sanitizada (sin cuentas en claro).
-- =========================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'passport_webhook_events')
BEGIN
    CREATE TABLE passport_webhook_events (
        id_event                         BIGINT          IDENTITY(1,1) NOT NULL,
        provider                         NVARCHAR(30)    NOT NULL CONSTRAINT DF_pwh_provider DEFAULT 'PASSPORT',
        event_type                       NVARCHAR(100)   NULL,
        passport_payment_id              NVARCHAR(100)   NULL,
        id_breb_retiro                   BIGINT          NULL,
        payload_hash                     NVARCHAR(64)    NULL,   -- SHA-256 hex del raw body
        payload_sanitized_json           NVARCHAR(MAX)   NULL,   -- sin cuentas ni documentos en claro
        signature_valid                  BIT             NOT NULL CONSTRAINT DF_pwh_sig DEFAULT 0,
        processed                        BIT             NOT NULL CONSTRAINT DF_pwh_proc DEFAULT 0,
        fecha_recibido                   DATETIME2       NOT NULL CONSTRAINT DF_pwh_recibido DEFAULT SYSUTCDATETIME(),
        fecha_procesado                  DATETIME2       NULL,
        error_message                    NVARCHAR(500)   NULL,
        CONSTRAINT PK_passport_webhook   PRIMARY KEY (id_event)
    );

    CREATE INDEX IX_pwh_payment    ON passport_webhook_events (passport_payment_id) WHERE passport_payment_id IS NOT NULL;
    CREATE INDEX IX_pwh_retiro     ON passport_webhook_events (id_breb_retiro)      WHERE id_breb_retiro IS NOT NULL;
    CREATE INDEX IX_pwh_processed  ON passport_webhook_events (processed, fecha_recibido);

    PRINT 'Tabla passport_webhook_events creada.';
END
ELSE
    PRINT 'Tabla passport_webhook_events ya existe.';
GO

-- =========================================================================
-- 4. Cuenta ledger 110102 — Banco Coopcentral XPAY
--    Activo bancario. Representa la cuenta operacional de XPAY en Coopcentral.
--    En QA: no existe cuenta real. Se usa solo para documentar asientos futuros.
--    En Fase 65 (Liquidado): DR 210204 / CR 110102.
-- =========================================================================
INSERT INTO ledger_cuentas (id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta, naturaleza, permite_movimiento)
SELECT u.id_unidad_negocio,
       '110102',
       'Banco Coopcentral XPAY',
       'ACTIVO',
       'BANCO',
       'D',
       1
FROM   unidades_negocio u
WHERE  u.codigo = 'XPAY_COL'
  AND  NOT EXISTS (
         SELECT 1 FROM ledger_cuentas lc
         WHERE  lc.id_unidad_negocio = u.id_unidad_negocio
           AND  lc.codigo = '110102'
       );
GO

-- =========================================================================
-- 5. Cuenta ledger 210204 — Retiros Bre-B Pendientes de Pago
--    Pasivo transitorio. Registra la salida de fondos wallet hasta que
--    Passport/Coopcentral confirme la liquidación.
--    En solicitud retiro usuario:  DR 210101 / CR 210204
--    En solicitud retiro comercio: DR 210202 / CR 210204
--    En liquidación (LIQUIDADO):   DR 210204 / CR 110102
--    En rechazo (RECHAZADO):       DR 210204 / CR 210101 o 210202
-- =========================================================================
INSERT INTO ledger_cuentas (id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta, naturaleza, permite_movimiento)
SELECT u.id_unidad_negocio,
       '210204',
       'Retiros Bre-B Pendientes de Pago',
       'PASIVO',
       'RETIROS_BREB',
       'C',
       1
FROM   unidades_negocio u
WHERE  u.codigo = 'XPAY_COL'
  AND  NOT EXISTS (
         SELECT 1 FROM ledger_cuentas lc
         WHERE  lc.id_unidad_negocio = u.id_unidad_negocio
           AND  lc.codigo = '210204'
       );
GO

PRINT '010_passport_breb_base.sql ejecutado correctamente.';
GO
