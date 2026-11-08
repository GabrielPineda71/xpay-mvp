-- =====================================================================
-- XPAY PROD - VARIANTE_PROD 016
-- Origen: database/016_comercio_disponibilidad.sql
-- SHA-256 del origen: 109eef755417d2f9c198ec274f5b93f3162c67dfaf932b331fefd8a427f55552
-- Lineas conservadas del origen: 1-120
-- Estado en el manifiesto: LISTO
-- Generado por database/prod/tools/construir_paquete.py.
-- NO EDITAR A MANO: modificar la especificacion y regenerar.
-- El contenido despues de la marca es copia exacta de esas lineas.
-- =====================================================================
-- @@FIN_ENCABEZADO_VARIANTE_PROD@@
SET QUOTED_IDENTIFIER ON;
GO
-- ============================================================
-- Fase 67.2 — Disponibilidad y liquidación anticipada comercio
-- Idempotente. No modifica ventas existentes ni ledger.
-- ============================================================

-- ── 1. Nuevas cuentas contables ──────────────────────────────
IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE codigo = '410201' AND id_unidad_negocio = 1)
    INSERT INTO ledger_cuentas (id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta, naturaleza, permite_movimiento, estado, fecha_creacion)
    VALUES (1, '410201', 'Ingreso Descuento Comercio', 'INGRESO', 'DESCUENTO_COMERCIO', 'C', 1, 'ACTIVA', SYSUTCDATETIME());
PRINT '410201 Ingreso Descuento Comercio — ok';
GO

IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE codigo = '410202' AND id_unidad_negocio = 1)
    INSERT INTO ledger_cuentas (id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta, naturaleza, permite_movimiento, estado, fecha_creacion)
    VALUES (1, '410202', 'Ingreso Descuento Liquidacion Anticipada', 'INGRESO', 'DESCUENTO_ANTICIPADO', 'C', 1, 'ACTIVA', SYSUTCDATETIME());
PRINT '410202 Ingreso Descuento Liquidacion Anticipada — ok';
GO

IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE codigo = '240802' AND id_unidad_negocio = 1)
    INSERT INTO ledger_cuentas (id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta, naturaleza, permite_movimiento, estado, fecha_creacion)
    VALUES (1, '240802', 'IVA Descuento Comercio', 'PASIVO', 'IVA', 'C', 1, 'ACTIVA', SYSUTCDATETIME());
PRINT '240802 IVA Descuento Comercio — ok (reservada para futuro)';
GO

-- ── 2. comercio_condiciones_negociacion ──────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'comercio_condiciones_negociacion')
BEGIN
    CREATE TABLE comercio_condiciones_negociacion (
        id_condicion            BIGINT          IDENTITY(1,1) NOT NULL,
        id_comercio_aliado      BIGINT          NOT NULL,
        dias_disponibilidad     INT             NOT NULL,
        porcentaje_descuento    DECIMAL(9,4)    NOT NULL,
        aplica_iva              BIT             NOT NULL DEFAULT 0,
        estado                  NVARCHAR(30)    NOT NULL DEFAULT 'ACTIVO',
        fecha_inicio            DATE            NOT NULL,
        fecha_fin               DATE            NULL,
        observaciones           NVARCHAR(1000)  NULL,
        created_at              DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at              DATETIME2       NULL,
        created_by_usuario      BIGINT          NULL,
        updated_by_usuario      BIGINT          NULL,

        CONSTRAINT pk_ccn          PRIMARY KEY CLUSTERED (id_condicion),
        CONSTRAINT fk_ccn_aliado   FOREIGN KEY (id_comercio_aliado) REFERENCES comercios_aliados (id_comercio_aliado),
        CONSTRAINT ck_ccn_dias     CHECK (dias_disponibilidad >= 0),
        CONSTRAINT ck_ccn_pct      CHECK (porcentaje_descuento >= 0 AND porcentaje_descuento <= 100),
        CONSTRAINT ck_ccn_estado   CHECK (estado IN ('ACTIVO','INACTIVO'))
    );
    PRINT 'Tabla comercio_condiciones_negociacion creada';
END
ELSE PRINT 'Tabla comercio_condiciones_negociacion ya existe';
GO

-- ── 3. xpay_parametros_liquidacion_anticipada ────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'xpay_parametros_liquidacion_anticipada')
BEGIN
    CREATE TABLE xpay_parametros_liquidacion_anticipada (
        id_parametro            BIGINT          IDENTITY(1,1) NOT NULL,
        dias_faltantes          INT             NOT NULL,
        porcentaje_descuento    DECIMAL(9,4)    NOT NULL,
        estado                  NVARCHAR(30)    NOT NULL DEFAULT 'ACTIVO',
        created_at              DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at              DATETIME2       NULL,
        created_by_usuario      BIGINT          NULL,
        updated_by_usuario      BIGINT          NULL,

        CONSTRAINT pk_xpla         PRIMARY KEY CLUSTERED (id_parametro),
        CONSTRAINT ck_xpla_dias    CHECK (dias_faltantes >= 0 AND dias_faltantes <= 60),
        CONSTRAINT ck_xpla_pct     CHECK (porcentaje_descuento >= 0 AND porcentaje_descuento <= 100),
        CONSTRAINT ck_xpla_estado  CHECK (estado IN ('ACTIVO','INACTIVO')),
        CONSTRAINT uq_xpla_dias    UNIQUE (dias_faltantes, estado)
    );
    PRINT 'Tabla xpay_parametros_liquidacion_anticipada creada';
END
ELSE PRINT 'Tabla xpay_parametros_liquidacion_anticipada ya existe';
GO

-- ── 4. comercio_ventas_qr_disponibilidad ─────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'comercio_ventas_qr_disponibilidad')
BEGIN
    CREATE TABLE comercio_ventas_qr_disponibilidad (
        id_disponibilidad                   BIGINT          IDENTITY(1,1) NOT NULL,
        id_venta_qr                         BIGINT          NOT NULL,
        id_comercio_aliado                  BIGINT          NOT NULL,
        id_comercio_existente               BIGINT          NOT NULL,
        id_wallet_comercio                  BIGINT          NOT NULL,
        valor_bruto                         DECIMAL(18,2)   NOT NULL,
        dias_disponibilidad                 INT             NOT NULL,
        porcentaje_descuento                DECIMAL(9,4)    NOT NULL,
        valor_descuento                     DECIMAL(18,2)   NOT NULL,
        valor_neto_programado               DECIMAL(18,2)   NOT NULL,
        fecha_venta                         DATETIME2       NOT NULL,
        fecha_disponible_programada         DATETIME2       NOT NULL,
        estado                              NVARCHAR(30)    NOT NULL DEFAULT 'NO_DISPONIBLE',
        tipo_liberacion                     NVARCHAR(30)    NULL,
        fecha_liberacion                    DATETIME2       NULL,
        porcentaje_descuento_anticipado     DECIMAL(9,4)    NULL,
        valor_descuento_anticipado          DECIMAL(18,2)   NULL,
        valor_neto_liberado                 DECIMAL(18,2)   NULL,
        id_transaccion_ledger_liberacion    BIGINT          NULL,
        observaciones                       NVARCHAR(1000)  NULL,
        created_at                          DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at                          DATETIME2       NULL,

        CONSTRAINT pk_cvqd             PRIMARY KEY CLUSTERED (id_disponibilidad),
        CONSTRAINT fk_cvqd_venta       FOREIGN KEY (id_venta_qr)          REFERENCES ventas_qr          (id_venta_qr),
        CONSTRAINT fk_cvqd_aliado      FOREIGN KEY (id_comercio_aliado)   REFERENCES comercios_aliados  (id_comercio_aliado),
        CONSTRAINT fk_cvqd_comercio    FOREIGN KEY (id_comercio_existente) REFERENCES comercios          (id_comercio),
        CONSTRAINT fk_cvqd_wallet      FOREIGN KEY (id_wallet_comercio)   REFERENCES wallets            (id_wallet),
        CONSTRAINT ck_cvqd_estado      CHECK (estado IN ('NO_DISPONIBLE','DISPONIBLE','LIQUIDADA_ANTICIPADA','CANCELADA')),
        CONSTRAINT ck_cvqd_tipo_lib    CHECK (tipo_liberacion IS NULL OR tipo_liberacion IN ('AUTOMATICA','MANUAL_ADMIN','ANTICIPADA_COMERCIO')),
        CONSTRAINT uq_cvqd_venta       UNIQUE (id_venta_qr)  -- una sola fila por venta
    );
    CREATE INDEX ix_cvqd_comercio_estado ON comercio_ventas_qr_disponibilidad (id_comercio_existente, estado);
    PRINT 'Tabla comercio_ventas_qr_disponibilidad creada';
END
ELSE PRINT 'Tabla comercio_ventas_qr_disponibilidad ya existe';
GO
