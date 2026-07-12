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

-- ── 5. Vincular comercio aliado 1 → comercio operativo 2 ─────
UPDATE comercios_aliados
SET    id_comercio_existente = 2,
       updated_at = SYSUTCDATETIME()
WHERE  id_comercio_aliado = 1
  AND  (id_comercio_existente IS NULL OR id_comercio_existente <> 2);
PRINT 'Comercio aliado 1 vinculado a comercio operativo 2';
GO

-- ── 6. Condicion de negociacion demo ─────────────────────────
IF NOT EXISTS (SELECT 1 FROM comercio_condiciones_negociacion WHERE id_comercio_aliado = 1 AND estado = 'ACTIVO')
BEGIN
    INSERT INTO comercio_condiciones_negociacion
        (id_comercio_aliado, dias_disponibilidad, porcentaje_descuento, aplica_iva, estado, fecha_inicio, observaciones, created_by_usuario)
    VALUES
        (1, 2, 3.0000, 0, 'ACTIVO', CAST(SYSUTCDATETIME() AS DATE), 'Condicion demo QA: 2 dias, 3% descuento, sin IVA', 1);
    PRINT 'Condicion negociacion demo creada';
END
ELSE PRINT 'Condicion negociacion demo ya existe';
GO

-- ── 7. Parámetros liquidacion anticipada 0-60 dias ───────────
-- Tasa: dia 0 = 0%, dia 1 = 0.5%, dia 2 = 1%, ..., dia 60 = 30%
IF NOT EXISTS (SELECT 1 FROM xpay_parametros_liquidacion_anticipada)
BEGIN
    DECLARE @d INT = 0;
    WHILE @d <= 60
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM xpay_parametros_liquidacion_anticipada WHERE dias_faltantes = @d AND estado = 'ACTIVO')
            INSERT INTO xpay_parametros_liquidacion_anticipada (dias_faltantes, porcentaje_descuento, estado, created_by_usuario)
            VALUES (@d, CAST(@d AS DECIMAL(9,4)) * 0.5000, 'ACTIVO', 1);
        SET @d = @d + 1;
    END
    PRINT 'Parametros liquidacion anticipada 0-60 dias insertados';
END
ELSE PRINT 'Parametros liquidacion anticipada ya existen';
GO

-- ── 8. Backfill: ventas CONTINGENCIA comercio 2 → disponibilidad ──
-- Solo inserta si no existe ya para esa venta.
DECLARE @id_ca    BIGINT = 1;   -- id_comercio_aliado
DECLARE @id_com   BIGINT = 2;   -- id_comercio_existente
DECLARE @id_wall  BIGINT = 4;   -- id_wallet_comercio
DECLARE @dias     INT    = 2;
DECLARE @pct      DECIMAL(9,4) = 3.0000;

INSERT INTO comercio_ventas_qr_disponibilidad
    (id_venta_qr, id_comercio_aliado, id_comercio_existente, id_wallet_comercio,
     valor_bruto, dias_disponibilidad, porcentaje_descuento,
     valor_descuento, valor_neto_programado,
     fecha_venta, fecha_disponible_programada, estado)
SELECT
    v.id_venta_qr,
    @id_ca,
    @id_com,
    @id_wall,
    v.valor_bruto,
    @dias,
    @pct,
    ROUND(v.valor_bruto * @pct / 100.0, 2),
    v.valor_bruto - ROUND(v.valor_bruto * @pct / 100.0, 2),
    v.fecha_venta,
    DATEADD(day, @dias, v.fecha_venta),
    'NO_DISPONIBLE'
FROM ventas_qr v
WHERE v.id_comercio = @id_com
  AND v.estado = 'CONTINGENCIA'
  AND NOT EXISTS (
      SELECT 1 FROM comercio_ventas_qr_disponibilidad d
      WHERE d.id_venta_qr = v.id_venta_qr
  );
PRINT 'Backfill ventas CONTINGENCIA completado — filas: ' + CAST(@@ROWCOUNT AS NVARCHAR(10));
GO

-- ── Verificacion final ────────────────────────────────────────
SELECT 'comercio_condiciones_negociacion'        AS tabla, COUNT(*) AS filas FROM comercio_condiciones_negociacion;
SELECT 'xpay_parametros_liquidacion_anticipada'  AS tabla, COUNT(*) AS filas FROM xpay_parametros_liquidacion_anticipada;
SELECT 'comercio_ventas_qr_disponibilidad'       AS tabla, COUNT(*) AS filas FROM comercio_ventas_qr_disponibilidad;

SELECT id_comercio_aliado, id_comercio_existente, nombre_comercial, nit
FROM comercios_aliados WHERE id_comercio_aliado = 1;

SELECT id_disponibilidad, id_venta_qr, valor_bruto, valor_descuento, valor_neto_programado, fecha_disponible_programada, estado
FROM comercio_ventas_qr_disponibilidad
ORDER BY id_venta_qr;
GO
