-- QR estático por Wallet y acreditación durable de pagos Bre-B entrantes.
-- 051 está reservada para la migración de identidad de la PR #36.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

IF OBJECT_ID('dbo.wallets', 'U') IS NULL
    OR OBJECT_ID('dbo.passport_webhook_events', 'U') IS NULL
    OR OBJECT_ID('dbo.ledger_transacciones', 'U') IS NULL
    THROW 59520, N'Migración 052 requiere Wallet, ledger y eventos Passport.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.ledger_cuentas WHERE id_unidad_negocio = 1 AND codigo = '110103')
    OR NOT EXISTS (SELECT 1 FROM dbo.ledger_cuentas WHERE id_unidad_negocio = 1 AND codigo = '210101')
    THROW 59520, N'Migración 052 requiere las cuentas contables 110103 y 210101.', 1;
GO

BEGIN TRANSACTION;
IF OBJECT_ID('dbo.wallet_recarga_breb_qrs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.wallet_recarga_breb_qrs (
        id_recarga_qr BIGINT IDENTITY(1,1) NOT NULL,
        id_wallet BIGINT NOT NULL,
        id_usuario BIGINT NOT NULL,
        referencia VARCHAR(17) NOT NULL,
        estado VARCHAR(20) NOT NULL,
        passport_account_id VARCHAR(36) NOT NULL,
        passport_customer_id VARCHAR(36) NOT NULL,
        passport_key_id VARCHAR(36) NOT NULL,
        passport_qr_id NVARCHAR(100) NULL,
        qr_code_data NVARCHAR(4000) NULL,
        fecha_creacion DATETIME2 NOT NULL,
        fecha_intento DATETIME2 NULL,
        fecha_generacion DATETIME2 NULL,
        CONSTRAINT pk_wrbq PRIMARY KEY (id_recarga_qr),
        CONSTRAINT uq_wrbq_wallet UNIQUE (id_wallet),
        CONSTRAINT uq_wrbq_referencia UNIQUE (referencia),
        CONSTRAINT uq_wrbq_qr_wallet UNIQUE (id_recarga_qr, id_wallet),
        CONSTRAINT fk_wrbq_wallet FOREIGN KEY (id_wallet) REFERENCES dbo.wallets(id_wallet),
        CONSTRAINT fk_wrbq_usuario FOREIGN KEY (id_usuario) REFERENCES dbo.usuarios(id_usuario),
        CONSTRAINT ck_wrbq_referencia CHECK (LEN(referencia) = 17 AND LEFT(referencia, 1) = 'W'
            AND referencia NOT LIKE '%[^0-9A-Z]%' AND referencia NOT LIKE '%P%'),
        CONSTRAINT ck_wrbq_estado CHECK (estado IN ('PENDIENTE', 'GENERANDO', 'ACTIVO')),
        CONSTRAINT ck_wrbq_activo CHECK (estado <> 'ACTIVO'
            OR (passport_qr_id IS NOT NULL AND qr_code_data IS NOT NULL AND fecha_generacion IS NOT NULL))
    );
END;

IF OBJECT_ID('dbo.wallet_recargas_breb', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.wallet_recargas_breb (
        id_recarga BIGINT IDENTITY(1,1) NOT NULL,
        id_recarga_qr BIGINT NOT NULL,
        id_wallet BIGINT NOT NULL,
        passport_payment_id VARCHAR(36) NOT NULL,
        valor DECIMAL(18,2) NOT NULL,
        moneda VARCHAR(3) NOT NULL,
        estado VARCHAR(20) NOT NULL,
        id_transaccion_ledger BIGINT NOT NULL,
        id_evento_settled BIGINT NOT NULL,
        fecha DATETIME2 NOT NULL,
        CONSTRAINT pk_wrb PRIMARY KEY (id_recarga),
        CONSTRAINT uq_wrb_pago UNIQUE (passport_payment_id),
        CONSTRAINT uq_wrb_ledger UNIQUE (id_transaccion_ledger),
        CONSTRAINT uq_wrb_evento UNIQUE (id_evento_settled),
        CONSTRAINT fk_wrb_qr_wallet FOREIGN KEY (id_recarga_qr, id_wallet)
            REFERENCES dbo.wallet_recarga_breb_qrs(id_recarga_qr, id_wallet),
        CONSTRAINT fk_wrb_ledger FOREIGN KEY (id_transaccion_ledger)
            REFERENCES dbo.ledger_transacciones(id_transaccion_ledger),
        CONSTRAINT fk_wrb_evento FOREIGN KEY (id_evento_settled)
            REFERENCES dbo.passport_webhook_events(id_event),
        CONSTRAINT ck_wrb_valor CHECK (valor > 0),
        CONSTRAINT ck_wrb_moneda CHECK (moneda = 'COP'),
        CONSTRAINT ck_wrb_estado CHECK (estado = 'APLICADA')
    );
END;

-- Una segunda ejecución valida las garantías críticas del esquema existente.
IF ISNULL(COL_LENGTH('dbo.wallet_recarga_breb_qrs', 'referencia'), -1) <> 17
    OR ISNULL(COL_LENGTH('dbo.wallet_recarga_breb_qrs', 'passport_account_id'), -1) <> 36
    OR ISNULL(COL_LENGTH('dbo.wallet_recarga_breb_qrs', 'qr_code_data'), -1) <> 8000
    OR ISNULL(COL_LENGTH('dbo.wallet_recargas_breb', 'passport_payment_id'), -1) <> 36
    THROW 59521, N'Migración 052 encontró columnas incompatibles en recargas Bre-B.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.wallet_recargas_breb')
    AND name = 'valor' AND precision = 18 AND scale = 2 AND is_nullable = 0)
    THROW 59521, N'Migración 052 requiere valor DECIMAL(18,2) NOT NULL.', 1;

IF EXISTS (
    SELECT 1 FROM (VALUES
        ('dbo.wallet_recarga_breb_qrs', 'uq_wrbq_wallet', 'id_wallet'),
        ('dbo.wallet_recarga_breb_qrs', 'uq_wrbq_referencia', 'referencia'),
        ('dbo.wallet_recargas_breb', 'uq_wrb_pago', 'passport_payment_id'),
        ('dbo.wallet_recargas_breb', 'uq_wrb_ledger', 'id_transaccion_ledger'),
        ('dbo.wallet_recargas_breb', 'uq_wrb_evento', 'id_evento_settled')
    ) esperado(tabla, indice, columna)
    LEFT JOIN sys.indexes i ON i.object_id = OBJECT_ID(esperado.tabla) AND i.name = esperado.indice
    WHERE i.index_id IS NULL OR i.is_unique = 0 OR i.is_disabled = 1 OR i.has_filter = 1
        OR ISNULL(INDEX_COL(esperado.tabla, i.index_id, 1), '') <> esperado.columna
        OR INDEX_COL(esperado.tabla, i.index_id, 2) IS NOT NULL)
    THROW 59522, N'Migración 052 requiere unicidad de QR, referencia, pago y asiento.', 1;

IF EXISTS (
    SELECT 1 FROM (VALUES
        ('dbo.wallet_recarga_breb_qrs', 'fk_wrbq_wallet'),
        ('dbo.wallet_recarga_breb_qrs', 'fk_wrbq_usuario'),
        ('dbo.wallet_recargas_breb', 'fk_wrb_qr_wallet'),
        ('dbo.wallet_recargas_breb', 'fk_wrb_ledger'),
        ('dbo.wallet_recargas_breb', 'fk_wrb_evento')
    ) esperado(tabla, nombre)
    LEFT JOIN sys.foreign_keys fk ON fk.parent_object_id = OBJECT_ID(esperado.tabla) AND fk.name = esperado.nombre
    WHERE fk.object_id IS NULL OR fk.is_disabled = 1 OR fk.is_not_trusted = 1)
    THROW 59523, N'Migración 052 requiere los vínculos de Wallet, pago y asiento.', 1;
COMMIT TRANSACTION;
GO
