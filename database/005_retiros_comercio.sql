/* XPAY MVP V1 - 005_retiros_comercio.sql */
/* Fase 5: Solicitud de retiro del comercio */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Tabla retiros_comercio
CREATE TABLE retiros_comercio (
    id_retiro           BIGINT IDENTITY(1,1) NOT NULL,
    id_unidad_negocio   BIGINT        NOT NULL,
    id_comercio         BIGINT        NOT NULL,
    id_wallet_comercio  BIGINT        NOT NULL,
    id_transaccion_ledger BIGINT      NULL,
    valor               DECIMAL(18,2) NOT NULL,
    estado              NVARCHAR(30)  NOT NULL CONSTRAINT DF_retcom_estado  DEFAULT 'PENDIENTE',
    medio_retiro        NVARCHAR(50)  NULL,
    banco               NVARCHAR(100) NULL,
    tipo_cuenta         NVARCHAR(50)  NULL,
    numero_cuenta       NVARCHAR(80)  NULL,
    titular_cuenta      NVARCHAR(150) NULL,
    documento_titular   NVARCHAR(30)  NULL,
    observacion         NVARCHAR(300) NULL,
    creado_por          BIGINT        NULL,
    fecha_solicitud     DATETIME2     NOT NULL CONSTRAINT DF_retcom_fecha   DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_retiros_comercio  PRIMARY KEY (id_retiro),
    CONSTRAINT FK_retcom_unidad     FOREIGN KEY (id_unidad_negocio)      REFERENCES unidades_negocio(id_unidad_negocio),
    CONSTRAINT FK_retcom_comercio   FOREIGN KEY (id_comercio)            REFERENCES comercios(id_comercio),
    CONSTRAINT FK_retcom_wallet     FOREIGN KEY (id_wallet_comercio)     REFERENCES wallets(id_wallet),
    CONSTRAINT FK_retcom_ledger     FOREIGN KEY (id_transaccion_ledger)  REFERENCES ledger_transacciones(id_transaccion_ledger)
);
GO
CREATE INDEX IX_retcom_comercio ON retiros_comercio (id_comercio);
CREATE INDEX IX_retcom_estado   ON retiros_comercio (estado);
GO

-- 2. Cuenta ledger 210203 — Retiros Comercios Pendientes de Pago
INSERT INTO ledger_cuentas (id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta, naturaleza, permite_movimiento)
SELECT u.id_unidad_negocio,
       '210203',
       'Retiros Comercios Pendientes de Pago',
       'PASIVO',
       'RETIROS_COMERCIOS',
       'C',
       1
FROM   unidades_negocio u
WHERE  u.codigo = 'XPAY_COL'
  AND  NOT EXISTS (
         SELECT 1 FROM ledger_cuentas lc
         WHERE  lc.id_unidad_negocio = u.id_unidad_negocio
           AND  lc.codigo = '210203'
       );
GO
