/* XPAY MVP V1 - 002_wallet_ledger.sql */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE TABLE wallets (id_wallet BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY, id_unidad_negocio BIGINT NOT NULL, tipo_wallet VARCHAR(30) NOT NULL, id_persona BIGINT NULL, id_comercio BIGINT NULL, nombre_wallet VARCHAR(200) NULL, estado VARCHAR(30) NOT NULL CONSTRAINT DF_wallets_estado DEFAULT 'ACTIVA', fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_wallets_fecha_creacion DEFAULT SYSDATETIME(), fecha_actualizacion DATETIME2 NULL, CONSTRAINT FK_wallets_unidad_negocio FOREIGN KEY (id_unidad_negocio) REFERENCES unidades_negocio(id_unidad_negocio), CONSTRAINT FK_wallets_persona FOREIGN KEY (id_persona) REFERENCES personas(id_persona));
GO
CREATE INDEX IX_wallets_persona ON wallets(id_persona);
GO
CREATE INDEX IX_wallets_comercio ON wallets(id_comercio);
GO
CREATE INDEX IX_wallets_tipo_estado ON wallets(tipo_wallet,estado);
GO
CREATE UNIQUE INDEX IX_wallets_persona_activa ON wallets(id_persona) WHERE id_persona IS NOT NULL AND tipo_wallet = 'PERSONA' AND estado = 'ACTIVA';
GO
CREATE TABLE wallet_saldos (id_wallet BIGINT NOT NULL PRIMARY KEY, saldo_disponible DECIMAL(18,2) NOT NULL CONSTRAINT DF_wallet_saldos_disponible DEFAULT 0, saldo_retenido DECIMAL(18,2) NOT NULL CONSTRAINT DF_wallet_saldos_retenido DEFAULT 0, saldo_transito DECIMAL(18,2) NOT NULL CONSTRAINT DF_wallet_saldos_transito DEFAULT 0, saldo_contingencia DECIMAL(18,2) NOT NULL CONSTRAINT DF_wallet_saldos_contingencia DEFAULT 0, fecha_actualizacion DATETIME2 NOT NULL CONSTRAINT DF_wallet_saldos_fecha DEFAULT SYSDATETIME(), CONSTRAINT FK_wallet_saldos_wallet FOREIGN KEY (id_wallet) REFERENCES wallets(id_wallet));
GO
CREATE TABLE ledger_cuentas (id_cuenta BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY, id_unidad_negocio BIGINT NOT NULL, codigo VARCHAR(50) NOT NULL, nombre VARCHAR(200) NOT NULL, tipo_cuenta VARCHAR(50) NOT NULL, subtipo_cuenta VARCHAR(80) NULL, naturaleza CHAR(1) NOT NULL, permite_movimiento BIT NOT NULL CONSTRAINT DF_ledger_cuentas_permite DEFAULT 1, estado VARCHAR(30) NOT NULL CONSTRAINT DF_ledger_cuentas_estado DEFAULT 'ACTIVA', fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_ledger_cuentas_fecha_creacion DEFAULT SYSDATETIME(), fecha_actualizacion DATETIME2 NULL, CONSTRAINT FK_ledger_cuentas_unidad FOREIGN KEY (id_unidad_negocio) REFERENCES unidades_negocio(id_unidad_negocio));
GO
CREATE UNIQUE INDEX IX_ledger_cuentas_codigo ON ledger_cuentas(id_unidad_negocio,codigo);
GO
CREATE INDEX IX_ledger_cuentas_tipo ON ledger_cuentas(tipo_cuenta,subtipo_cuenta);
GO
INSERT INTO ledger_cuentas (id_unidad_negocio,codigo,nombre,tipo_cuenta,subtipo_cuenta,naturaleza,permite_movimiento)
SELECT u.id_unidad_negocio,c.codigo,c.nombre,c.tipo_cuenta,c.subtipo_cuenta,c.naturaleza,c.permite_movimiento
FROM unidades_negocio u CROSS APPLY (VALUES
('110201','Banco Operativo','ACTIVO','BANCO','D',1),('110202','Banco Liquidez Usuarios','ACTIVO','BANCO','D',1),('110203','Banco Liquidez Comercios','ACTIVO','BANCO','D',1),('110204','Banco Fondo Cartera','ACTIVO','BANCO','D',1),('110205','Banco Fondo Impuestos','ACTIVO','BANCO','D',1),('130101','Cartera Capital Vigente','ACTIVO','CARTERA','D',1),('130102','Cartera Capital Vencida','ACTIVO','CARTERA','D',1),('130104','Cartera Anticipo Nómina','ACTIVO','CARTERA','D',1),('210101','Obligación Wallet Usuarios','PASIVO','WALLET_USUARIOS','C',1),('210102','Obligación Wallet Comercios','PASIVO','WALLET_COMERCIOS','C',1),('210103','Retiros Pendientes','PASIVO','RETIROS','C',1),('220101','Ventas QR en Contingencia','PASIVO','CONTINGENCIA_QR','C',1),('220102','Fondos Retenidos','PASIVO','RETENCIONES','C',1),('230101','IVA Comisión QR','PASIVO','IVA','C',1),('230102','IVA Gastos Administrativos','PASIVO','IVA','C',1),('310101','Comisión QR','INGRESO','COMISION_QR','C',1),('310102','Gastos Administrativos Anticipos','INGRESO','GASTO_ADMIN','C',1),('410101','Costos Bre-B','GASTO','BREB','D',1),('410102','Costos Veriff','GASTO','VERIFF','D',1),('410104','Costos Bancarios','GASTO','BANCARIO','D',1),('410105','Costos Cloud Azure','GASTO','CLOUD','D',1)
) AS c(codigo,nombre,tipo_cuenta,subtipo_cuenta,naturaleza,permite_movimiento) WHERE u.codigo='XPAY_COL';
GO
CREATE TABLE ledger_transacciones (id_transaccion_ledger BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY, id_unidad_negocio BIGINT NOT NULL, tipo_transaccion VARCHAR(80) NOT NULL, referencia_tipo VARCHAR(80) NULL, referencia_id BIGINT NULL, descripcion VARCHAR(500) NULL, valor_total DECIMAL(18,2) NOT NULL CONSTRAINT DF_ledger_transacciones_valor DEFAULT 0, estado VARCHAR(30) NOT NULL CONSTRAINT DF_ledger_transacciones_estado DEFAULT 'REGISTRADA', creado_por BIGINT NULL, fecha_transaccion DATETIME2 NOT NULL CONSTRAINT DF_ledger_transacciones_fecha DEFAULT SYSDATETIME(), fecha_actualizacion DATETIME2 NULL, CONSTRAINT FK_ledger_transacciones_unidad FOREIGN KEY (id_unidad_negocio) REFERENCES unidades_negocio(id_unidad_negocio), CONSTRAINT FK_ledger_transacciones_creado_por FOREIGN KEY (creado_por) REFERENCES usuarios(id_usuario));
GO
CREATE INDEX IX_ledger_transacciones_tipo_fecha ON ledger_transacciones(tipo_transaccion,fecha_transaccion DESC);
GO
CREATE INDEX IX_ledger_transacciones_referencia ON ledger_transacciones(referencia_tipo,referencia_id);
GO
CREATE INDEX IX_ledger_transacciones_estado ON ledger_transacciones(estado);
GO
CREATE TABLE ledger_movimientos (id_movimiento_ledger BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY, id_transaccion_ledger BIGINT NOT NULL, id_cuenta BIGINT NOT NULL, naturaleza CHAR(1) NOT NULL, valor DECIMAL(18,2) NOT NULL, concepto VARCHAR(100) NULL, referencia_tipo VARCHAR(80) NULL, referencia_id BIGINT NULL, descripcion VARCHAR(500) NULL, fecha_movimiento DATETIME2 NOT NULL CONSTRAINT DF_ledger_movimientos_fecha DEFAULT SYSDATETIME(), CONSTRAINT FK_ledger_movimientos_transaccion FOREIGN KEY (id_transaccion_ledger) REFERENCES ledger_transacciones(id_transaccion_ledger), CONSTRAINT FK_ledger_movimientos_cuenta FOREIGN KEY (id_cuenta) REFERENCES ledger_cuentas(id_cuenta));
GO
CREATE INDEX IX_ledger_movimientos_transaccion ON ledger_movimientos(id_transaccion_ledger);
GO
CREATE INDEX IX_ledger_movimientos_cuenta_fecha ON ledger_movimientos(id_cuenta,fecha_movimiento DESC);
GO
CREATE INDEX IX_ledger_movimientos_referencia ON ledger_movimientos(referencia_tipo,referencia_id);
GO
CREATE TABLE wallet_movimientos (id_movimiento_wallet BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY, id_wallet BIGINT NOT NULL, id_transaccion_ledger BIGINT NULL, tipo_movimiento VARCHAR(80) NOT NULL, naturaleza CHAR(1) NOT NULL, valor DECIMAL(18,2) NOT NULL, saldo_antes DECIMAL(18,2) NULL, saldo_despues DECIMAL(18,2) NULL, descripcion VARCHAR(500) NULL, referencia_tipo VARCHAR(80) NULL, referencia_id BIGINT NULL, estado VARCHAR(30) NOT NULL CONSTRAINT DF_wallet_movimientos_estado DEFAULT 'APLICADO', creado_por BIGINT NULL, fecha_movimiento DATETIME2 NOT NULL CONSTRAINT DF_wallet_movimientos_fecha DEFAULT SYSDATETIME(), CONSTRAINT FK_wallet_movimientos_wallet FOREIGN KEY (id_wallet) REFERENCES wallets(id_wallet), CONSTRAINT FK_wallet_movimientos_ledger FOREIGN KEY (id_transaccion_ledger) REFERENCES ledger_transacciones(id_transaccion_ledger), CONSTRAINT FK_wallet_movimientos_creado_por FOREIGN KEY (creado_por) REFERENCES usuarios(id_usuario));
GO
CREATE INDEX IX_wallet_movimientos_wallet_fecha ON wallet_movimientos(id_wallet,fecha_movimiento DESC);
GO
CREATE INDEX IX_wallet_movimientos_referencia ON wallet_movimientos(referencia_tipo,referencia_id);
GO
CREATE INDEX IX_wallet_movimientos_tipo ON wallet_movimientos(tipo_movimiento);
GO
ALTER TABLE ledger_movimientos ADD CONSTRAINT CK_ledger_movimientos_naturaleza CHECK (naturaleza IN ('D','C'));
GO
ALTER TABLE wallet_movimientos ADD CONSTRAINT CK_wallet_movimientos_naturaleza CHECK (naturaleza IN ('D','C'));
GO
ALTER TABLE wallets ADD CONSTRAINT CK_wallets_tipo CHECK (tipo_wallet IN ('PERSONA','COMERCIO','XPAY'));
GO
