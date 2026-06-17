/* XPAY MVP V1 - 004_liquidacion_qr.sql */
/* Fase 4: Liquidación de ventas QR al comercio */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Columna id_transaccion_liquidacion en ventas_qr
--    Permite registrar la tx ledger de liquidación separada del pago original.
ALTER TABLE ventas_qr
ADD id_transaccion_liquidacion BIGINT NULL
    CONSTRAINT FK_ventas_qr_tx_liquidacion FOREIGN KEY
    REFERENCES ledger_transacciones(id_transaccion_ledger);
GO

-- 2. Columna id_wallet_comercio en comercios
ALTER TABLE comercios
ADD id_wallet_comercio BIGINT NULL
    CONSTRAINT FK_comercios_wallet FOREIGN KEY
    REFERENCES wallets(id_wallet);
GO

-- 3. Tabla liquidaciones_comercio
CREATE TABLE liquidaciones_comercio (
    id_liquidacion      BIGINT IDENTITY(1,1) NOT NULL,
    id_unidad_negocio   BIGINT        NOT NULL,
    id_comercio         BIGINT        NOT NULL,
    id_wallet_comercio  BIGINT        NOT NULL,
    id_transaccion_ledger BIGINT      NULL,
    valor_bruto         DECIMAL(18,2) NOT NULL,
    valor_comision      DECIMAL(18,2) NOT NULL CONSTRAINT DF_liqcom_comision     DEFAULT 0,
    valor_iva_comision  DECIMAL(18,2) NOT NULL CONSTRAINT DF_liqcom_iva          DEFAULT 0,
    valor_neto          DECIMAL(18,2) NOT NULL,
    estado              NVARCHAR(30)  NOT NULL CONSTRAINT DF_liqcom_estado       DEFAULT 'APLICADA',
    fecha_liquidacion   DATETIME2     NOT NULL CONSTRAINT DF_liqcom_fecha        DEFAULT SYSUTCDATETIME(),
    creado_por          BIGINT        NULL,
    CONSTRAINT PK_liquidaciones_comercio     PRIMARY KEY (id_liquidacion),
    CONSTRAINT FK_liqcom_unidad              FOREIGN KEY (id_unidad_negocio)      REFERENCES unidades_negocio(id_unidad_negocio),
    CONSTRAINT FK_liqcom_comercio            FOREIGN KEY (id_comercio)            REFERENCES comercios(id_comercio),
    CONSTRAINT FK_liqcom_wallet              FOREIGN KEY (id_wallet_comercio)     REFERENCES wallets(id_wallet),
    CONSTRAINT FK_liqcom_ledger              FOREIGN KEY (id_transaccion_ledger)  REFERENCES ledger_transacciones(id_transaccion_ledger)
);
GO
CREATE INDEX IX_liqcom_comercio ON liquidaciones_comercio (id_comercio);
CREATE INDEX IX_liqcom_estado   ON liquidaciones_comercio (estado);
GO

-- 4. Tabla liquidacion_comercio_detalle
CREATE TABLE liquidacion_comercio_detalle (
    id_detalle          BIGINT IDENTITY(1,1) NOT NULL,
    id_liquidacion      BIGINT        NOT NULL,
    id_venta_qr         BIGINT        NOT NULL,
    valor_bruto         DECIMAL(18,2) NOT NULL,
    valor_comision      DECIMAL(18,2) NOT NULL CONSTRAINT DF_liqdet_comision     DEFAULT 0,
    valor_iva_comision  DECIMAL(18,2) NOT NULL CONSTRAINT DF_liqdet_iva          DEFAULT 0,
    valor_neto          DECIMAL(18,2) NOT NULL,
    CONSTRAINT PK_liquidacion_comercio_detalle  PRIMARY KEY (id_detalle),
    CONSTRAINT FK_liqdet_liquidacion            FOREIGN KEY (id_liquidacion) REFERENCES liquidaciones_comercio(id_liquidacion),
    CONSTRAINT FK_liqdet_venta                  FOREIGN KEY (id_venta_qr)    REFERENCES ventas_qr(id_venta_qr)
);
GO
CREATE INDEX IX_liqdet_liquidacion ON liquidacion_comercio_detalle (id_liquidacion);
CREATE INDEX IX_liqdet_venta       ON liquidacion_comercio_detalle (id_venta_qr);
GO

-- 5. Cuenta ledger 210202 — Obligación Wallet Comercios
INSERT INTO ledger_cuentas (id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta, naturaleza, permite_movimiento)
SELECT u.id_unidad_negocio,
       '210202',
       'Obligación Wallet Comercios',
       'PASIVO',
       'WALLET_COMERCIOS_LIQ',
       'C',
       1
FROM   unidades_negocio u
WHERE  u.codigo = 'XPAY_COL'
  AND  NOT EXISTS (
         SELECT 1 FROM ledger_cuentas lc
         WHERE  lc.id_unidad_negocio = u.id_unidad_negocio
           AND  lc.codigo = '210202'
       );
GO

-- 6. Wallet COMERCIO para el comercio demo + wallet_saldos inicial + enlace
DECLARE @idComercio       BIGINT;
DECLARE @idUnidad         BIGINT;
DECLARE @idWalletComercio BIGINT;

SELECT @idComercio = id_comercio,
       @idUnidad   = id_unidad_negocio
FROM   comercios
WHERE  nombre_comercial = 'Comercio Demo XPAY';

IF @idComercio IS NULL
BEGIN
    RAISERROR ('Seed: comercio demo no encontrado — verifique que 003_comercios_qr.sql se ejecutó primero.', 16, 1);
    RETURN;
END

INSERT INTO wallets (id_unidad_negocio, tipo_wallet, id_comercio, nombre_wallet, estado, fecha_creacion)
VALUES (@idUnidad, 'COMERCIO', @idComercio, 'Wallet Comercio Demo XPAY', 'ACTIVA', SYSUTCDATETIME());
SET @idWalletComercio = SCOPE_IDENTITY();

INSERT INTO wallet_saldos (id_wallet, saldo_disponible, saldo_retenido, saldo_transito, saldo_contingencia, fecha_actualizacion)
VALUES (@idWalletComercio, 0, 0, 0, 0, SYSUTCDATETIME());

UPDATE comercios
SET    id_wallet_comercio = @idWalletComercio
WHERE  id_comercio = @idComercio;

PRINT 'Wallet COMERCIO creada: id_wallet = ' + CAST(@idWalletComercio AS NVARCHAR(20)) + ' — comercio id = ' + CAST(@idComercio AS NVARCHAR(20));
GO
