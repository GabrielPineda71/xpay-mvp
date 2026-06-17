/* XPAY MVP V1 - 003_comercios_qr.sql */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ─── comercios ───────────────────────────────────────────────────────────────
CREATE TABLE comercios (
    id_comercio      BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    id_unidad_negocio BIGINT NOT NULL,
    nombre_comercial NVARCHAR(150) NOT NULL,
    razon_social     NVARCHAR(200) NULL,
    nit              NVARCHAR(30)  NULL,
    estado           NVARCHAR(20)  NOT NULL CONSTRAINT DF_comercios_estado DEFAULT 'ACTIVO',
    fecha_creacion   DATETIME2     NOT NULL CONSTRAINT DF_comercios_fecha   DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_comercios_unidad FOREIGN KEY (id_unidad_negocio) REFERENCES unidades_negocio(id_unidad_negocio)
);
GO
CREATE INDEX IX_comercios_unidad ON comercios(id_unidad_negocio);
GO
CREATE INDEX IX_comercios_estado ON comercios(estado);
GO

-- ─── comercio_tiendas ────────────────────────────────────────────────────────
CREATE TABLE comercio_tiendas (
    id_tienda      BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    id_comercio    BIGINT        NOT NULL,
    nombre_tienda  NVARCHAR(150) NOT NULL,
    ciudad         NVARCHAR(100) NULL,
    direccion      NVARCHAR(250) NULL,
    estado         NVARCHAR(20)  NOT NULL CONSTRAINT DF_comercio_tiendas_estado DEFAULT 'ACTIVO',
    fecha_creacion DATETIME2     NOT NULL CONSTRAINT DF_comercio_tiendas_fecha  DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_comercio_tiendas_comercio FOREIGN KEY (id_comercio) REFERENCES comercios(id_comercio)
);
GO
CREATE INDEX IX_comercio_tiendas_comercio ON comercio_tiendas(id_comercio);
GO

-- ─── qr_comercios ────────────────────────────────────────────────────────────
CREATE TABLE qr_comercios (
    id_qr          BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    id_comercio    BIGINT        NOT NULL,
    id_tienda      BIGINT        NOT NULL,
    codigo_qr      NVARCHAR(100) NOT NULL,
    estado         NVARCHAR(20)  NOT NULL CONSTRAINT DF_qr_comercios_estado DEFAULT 'ACTIVO',
    fecha_creacion DATETIME2     NOT NULL CONSTRAINT DF_qr_comercios_fecha  DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_qr_comercios_comercio FOREIGN KEY (id_comercio) REFERENCES comercios(id_comercio),
    CONSTRAINT FK_qr_comercios_tienda   FOREIGN KEY (id_tienda)   REFERENCES comercio_tiendas(id_tienda)
);
GO
CREATE UNIQUE INDEX IX_qr_comercios_codigo  ON qr_comercios(codigo_qr);
GO
CREATE INDEX IX_qr_comercios_comercio ON qr_comercios(id_comercio);
GO

-- ─── ventas_qr ───────────────────────────────────────────────────────────────
CREATE TABLE ventas_qr (
    id_venta_qr           BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    id_unidad_negocio     BIGINT        NOT NULL,
    id_comercio           BIGINT        NOT NULL,
    id_tienda             BIGINT        NOT NULL,
    id_qr                 BIGINT        NOT NULL,
    id_wallet_usuario     BIGINT        NOT NULL,
    id_transaccion_ledger BIGINT        NULL,
    valor_bruto           DECIMAL(18,2) NOT NULL,
    valor_comision        DECIMAL(18,2) NOT NULL CONSTRAINT DF_ventas_qr_comision DEFAULT 0,
    valor_iva_comision    DECIMAL(18,2) NOT NULL CONSTRAINT DF_ventas_qr_iva     DEFAULT 0,
    valor_neto_comercio   DECIMAL(18,2) NOT NULL,
    estado                NVARCHAR(30)  NOT NULL CONSTRAINT DF_ventas_qr_estado  DEFAULT 'CONTINGENCIA',
    referencia            NVARCHAR(100) NULL,
    descripcion           NVARCHAR(300) NULL,
    fecha_venta           DATETIME2     NOT NULL CONSTRAINT DF_ventas_qr_fecha   DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ventas_qr_unidad  FOREIGN KEY (id_unidad_negocio)     REFERENCES unidades_negocio(id_unidad_negocio),
    CONSTRAINT FK_ventas_qr_comercio FOREIGN KEY (id_comercio)          REFERENCES comercios(id_comercio),
    CONSTRAINT FK_ventas_qr_tienda  FOREIGN KEY (id_tienda)             REFERENCES comercio_tiendas(id_tienda),
    CONSTRAINT FK_ventas_qr_qr      FOREIGN KEY (id_qr)                 REFERENCES qr_comercios(id_qr),
    CONSTRAINT FK_ventas_qr_wallet  FOREIGN KEY (id_wallet_usuario)     REFERENCES wallets(id_wallet),
    CONSTRAINT FK_ventas_qr_ledger  FOREIGN KEY (id_transaccion_ledger) REFERENCES ledger_transacciones(id_transaccion_ledger)
);
GO
CREATE INDEX IX_ventas_qr_comercio ON ventas_qr(id_comercio);
GO
CREATE INDEX IX_ventas_qr_wallet   ON ventas_qr(id_wallet_usuario);
GO
CREATE INDEX IX_ventas_qr_estado   ON ventas_qr(estado);
GO
CREATE INDEX IX_ventas_qr_fecha    ON ventas_qr(fecha_venta DESC);
GO

-- ─── ledger: cuenta 210201 (solo si no existe) ───────────────────────────────
INSERT INTO ledger_cuentas (id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta, naturaleza, permite_movimiento)
SELECT u.id_unidad_negocio,
       '210201',
       'Ventas QR en Contingencia Comercios',
       'PASIVO',
       'CONTINGENCIA_QR_COMERCIOS',
       'C',
       1
FROM unidades_negocio u
WHERE u.codigo = 'XPAY_COL'
  AND NOT EXISTS (
      SELECT 1 FROM ledger_cuentas lc
      WHERE lc.id_unidad_negocio = u.id_unidad_negocio AND lc.codigo = '210201'
  );
GO

-- ─── seed: comercio demo ─────────────────────────────────────────────────────
DECLARE @idComercio BIGINT, @idTienda BIGINT;

INSERT INTO comercios (id_unidad_negocio, nombre_comercial, razon_social, nit)
SELECT id_unidad_negocio, 'Comercio Demo XPAY', 'Comercio Demo XPAY SAS', '900123456-1'
FROM unidades_negocio WHERE codigo = 'XPAY_COL';
SET @idComercio = SCOPE_IDENTITY();

INSERT INTO comercio_tiendas (id_comercio, nombre_tienda, ciudad, direccion)
VALUES (@idComercio, 'Tienda Principal Demo', N'Bogotá', 'Calle 100 #15-20');
SET @idTienda = SCOPE_IDENTITY();

INSERT INTO qr_comercios (id_comercio, id_tienda, codigo_qr)
VALUES (@idComercio, @idTienda, 'QR-DEMO-XPAY-001');
GO
