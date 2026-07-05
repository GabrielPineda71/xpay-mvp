-- =============================================================================
-- 013_libranza_anticipos.sql
-- Fase 66.3: DECADAL, cortes de pago, empleado→usuario, anticipos de nómina
-- =============================================================================
SET QUOTED_IDENTIFIER ON;
GO

-- ── 1. Agregar dia_pago_3 y permite_anticipo_dia_pago a convenio ─────────────

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME='libranza_empresas_convenio' AND COLUMN_NAME='dia_pago_3')
    ALTER TABLE libranza_empresas_convenio ADD dia_pago_3 INT NULL;

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME='libranza_empresas_convenio' AND COLUMN_NAME='permite_anticipo_dia_pago')
    ALTER TABLE libranza_empresas_convenio
    ADD permite_anticipo_dia_pago BIT NOT NULL
        CONSTRAINT df_lec_permite_anticipo_dia_pago DEFAULT 0;
GO

-- ── 2. Ampliar check constraint de periodicidad (convenio) ────────────────────

SET QUOTED_IDENTIFIER ON;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name='ck_lec_periodicidad')
    ALTER TABLE libranza_empresas_convenio DROP CONSTRAINT ck_lec_periodicidad;
GO

ALTER TABLE libranza_empresas_convenio
    ADD CONSTRAINT ck_lec_periodicidad
    CHECK (periodicidad_pago IN ('MENSUAL','QUINCENAL','DECADAL'));
GO

-- Agregar check para dia_pago_3
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name='ck_lec_dia_pago_3')
    ALTER TABLE libranza_empresas_convenio
    ADD CONSTRAINT ck_lec_dia_pago_3
    CHECK (dia_pago_3 IS NULL OR (dia_pago_3 >= 1 AND dia_pago_3 <= 31));
GO

-- ── 3. Agregar dia_pago_3 a empleados ────────────────────────────────────────

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME='libranza_empleados' AND COLUMN_NAME='dia_pago_3')
    ALTER TABLE libranza_empleados ADD dia_pago_3 INT NULL;
GO

-- Ampliar check constraint de periodicidad (empleados)
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name='ck_le_periodicidad')
    ALTER TABLE libranza_empleados DROP CONSTRAINT ck_le_periodicidad;
GO

ALTER TABLE libranza_empleados
    ADD CONSTRAINT ck_le_periodicidad
    CHECK (periodicidad_pago IN ('MENSUAL','QUINCENAL','DECADAL'));
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name='ck_le_dia_pago_3')
    ALTER TABLE libranza_empleados
    ADD CONSTRAINT ck_le_dia_pago_3
    CHECK (dia_pago_3 IS NULL OR (dia_pago_3 >= 1 AND dia_pago_3 <= 31));
GO

-- ── 4. Tabla: cortes de pago por empleado ────────────────────────────────────

SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('libranza_empleado_cortes_pago', 'U') IS NULL
BEGIN
    CREATE TABLE libranza_empleado_cortes_pago (
        id_corte_pago         BIGINT IDENTITY(1,1) NOT NULL,
        id_empleado           BIGINT NOT NULL,
        numero_corte          INT    NOT NULL,
        dia_pago              INT    NOT NULL,
        valor_pago_programado DECIMAL(18,2) NOT NULL,
        estado                NVARCHAR(30)  NOT NULL
            CONSTRAINT df_lecp_estado DEFAULT 'ACTIVO',
        created_at            DATETIME2 NOT NULL
            CONSTRAINT df_lecp_created_at DEFAULT SYSUTCDATETIME(),
        updated_at            DATETIME2 NULL,
        created_by_usuario    BIGINT NULL,
        updated_by_usuario    BIGINT NULL,
        CONSTRAINT pk_lecp PRIMARY KEY (id_corte_pago),
        CONSTRAINT ck_lecp_dia    CHECK (dia_pago BETWEEN 1 AND 31),
        CONSTRAINT ck_lecp_valor  CHECK (valor_pago_programado > 0),
        CONSTRAINT ck_lecp_corte  CHECK (numero_corte BETWEEN 1 AND 3),
        CONSTRAINT ck_lecp_estado CHECK (estado IN ('ACTIVO','INACTIVO')),
        CONSTRAINT fk_lecp_empleado
            FOREIGN KEY (id_empleado) REFERENCES libranza_empleados(id_empleado)
    );
    CREATE UNIQUE INDEX uix_lecp_empleado_corte
        ON libranza_empleado_cortes_pago (id_empleado, numero_corte)
        WHERE estado = 'ACTIVO';
END;
GO

-- ── 5. Tabla: asociación empleado ↔ usuario cliente ──────────────────────────

SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('libranza_empleado_usuario', 'U') IS NULL
BEGIN
    CREATE TABLE libranza_empleado_usuario (
        id_empleado_usuario BIGINT IDENTITY(1,1) NOT NULL,
        id_empleado         BIGINT NOT NULL,
        id_usuario          BIGINT NOT NULL,
        id_wallet           BIGINT NULL,
        estado              NVARCHAR(30) NOT NULL
            CONSTRAINT df_leu2_estado DEFAULT 'ACTIVO',
        created_at          DATETIME2 NOT NULL
            CONSTRAINT df_leu2_created_at DEFAULT SYSUTCDATETIME(),
        created_by_usuario  BIGINT NULL,
        CONSTRAINT pk_leu2 PRIMARY KEY (id_empleado_usuario),
        CONSTRAINT ck_leu2_estado CHECK (estado IN ('ACTIVO','INACTIVO')),
        CONSTRAINT fk_leu2_empleado
            FOREIGN KEY (id_empleado) REFERENCES libranza_empleados(id_empleado)
    );
    CREATE UNIQUE INDEX uix_leu2_empleado_activo
        ON libranza_empleado_usuario (id_empleado) WHERE estado = 'ACTIVO';
    CREATE UNIQUE INDEX uix_leu2_usuario_activo
        ON libranza_empleado_usuario (id_usuario) WHERE estado = 'ACTIVO';
END;
GO

-- ── 6. Tabla: anticipos de nómina ────────────────────────────────────────────

IF OBJECT_ID('libranza_anticipos', 'U') IS NULL
BEGIN
    CREATE TABLE libranza_anticipos (
        id_anticipo                      BIGINT IDENTITY(1,1) NOT NULL,
        id_convenio                      BIGINT NOT NULL,
        id_empleado                      BIGINT NOT NULL,
        id_usuario                       BIGINT NOT NULL,
        id_wallet                        BIGINT NOT NULL,
        fecha_solicitud                  DATETIME2 NOT NULL,
        fecha_simulada                   DATE NULL,
        dia_pago_corte                   INT NOT NULL,
        fecha_pago_programada            DATE NULL,
        valor_pago_programado            DECIMAL(18,2) NOT NULL,
        porcentaje_cupo                  DECIMAL(5,2)  NOT NULL,
        valor_cupo_base                  DECIMAL(18,2) NOT NULL,
        valor_solicitado                 DECIMAL(18,2) NOT NULL,
        valor_comision                   DECIMAL(18,2) NOT NULL,
        valor_iva                        DECIMAL(18,2) NOT NULL,
        valor_total_a_cobrar             DECIMAL(18,2) NOT NULL,
        valor_neto_desembolsado          DECIMAL(18,2) NOT NULL,
        momento_cobro_comision           NVARCHAR(20)  NOT NULL,
        estado                           NVARCHAR(30)  NOT NULL
            CONSTRAINT df_la_estado DEFAULT 'CREADO',
        id_transaccion_ledger_desembolso BIGINT NULL,
        id_transaccion_ledger_pago       BIGINT NULL,
        referencia_pago                  NVARCHAR(200) NULL,
        observaciones                    NVARCHAR(1000) NULL,
        created_at                       DATETIME2 NOT NULL
            CONSTRAINT df_la_created_at DEFAULT SYSUTCDATETIME(),
        updated_at                       DATETIME2 NULL,
        created_by_usuario               BIGINT NULL,
        updated_by_usuario               BIGINT NULL,
        CONSTRAINT pk_la PRIMARY KEY (id_anticipo),
        CONSTRAINT ck_la_estado  CHECK (estado IN ('CREADO','DESEMBOLSADO','PAGADO','RECHAZADO','ANULADO')),
        CONSTRAINT ck_la_momento CHECK (momento_cobro_comision IN ('ANTICIPADO','VENCIDO')),
        CONSTRAINT fk_la_convenio FOREIGN KEY (id_convenio) REFERENCES libranza_empresas_convenio(id_convenio),
        CONSTRAINT fk_la_empleado FOREIGN KEY (id_empleado) REFERENCES libranza_empleados(id_empleado)
    );
    CREATE INDEX ix_la_empleado_estado ON libranza_anticipos (id_empleado, estado);
    CREATE INDEX ix_la_convenio_estado ON libranza_anticipos (id_convenio, estado);
END;
GO

-- ── 7. Cuentas ledger (nuevas) ────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE codigo='310103' AND id_unidad_negocio=1)
    INSERT INTO ledger_cuentas (
        id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta,
        naturaleza, permite_movimiento, estado, fecha_creacion)
    VALUES (1, '310103', 'Ingreso Comisión Anticipo Nómina',
        'INGRESO', 'LIBRANZA', 'C', 1, 'ACTIVA', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE codigo='230103' AND id_unidad_negocio=1)
    INSERT INTO ledger_cuentas (
        id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta,
        naturaleza, permite_movimiento, estado, fecha_creacion)
    VALUES (1, '230103', 'IVA Anticipo Nómina',
        'PASIVO', 'LIBRANZA', 'C', 1, 'ACTIVA', SYSUTCDATETIME());
GO

-- ── 8. Demo: Empresa Decadal Demo XPAY ───────────────────────────────────────

SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM libranza_empresas_convenio WHERE nit='901777777-1')
    INSERT INTO libranza_empresas_convenio (
        nombre_empresa, nit, representante_legal, email_contacto, telefono_contacto,
        estado, dia_pago_1, dia_pago_2, dia_pago_3, periodicidad_pago,
        porcentaje_maximo_cupo, permite_anticipo_dia_pago,
        fecha_inicio, created_at, created_by_usuario)
    VALUES (
        'Empresa Decadal Demo XPAY', '901777777-1', 'Admin Demo Decadal',
        'empresa.decadal@xpay.qa', '3009990000',
        'ACTIVO', 10, 20, 30, 'DECADAL', 30.00, 0,
        SYSUTCDATETIME(), SYSUTCDATETIME(), 1);
GO

-- ── 9. Demo: parámetros, rangos, empleado, cortes, asociaciones ──────────────

SET QUOTED_IDENTIFIER ON;
GO

DECLARE @idConvenioDecadal BIGINT =
    (SELECT id_convenio FROM libranza_empresas_convenio WHERE nit='901777777-1');

IF NOT EXISTS (
    SELECT 1 FROM libranza_parametros_empresa
    WHERE id_convenio=@idConvenioDecadal AND estado='ACTIVO')
    INSERT INTO libranza_parametros_empresa (
        id_convenio, porcentaje_maximo_cupo, salario_minimo_empleado, salario_maximo_empleado,
        requiere_validacion_empresa, permite_anticipo_multiple, max_anticipos_activos,
        iva_porcentaje, momento_cobro_comision, estado, created_at, created_by_usuario)
    VALUES (@idConvenioDecadal, 30.00, NULL, NULL, 1, 0, 1, 19.00, 'VENCIDO', 'ACTIVO', SYSUTCDATETIME(), 1);

IF NOT EXISTS (SELECT 1 FROM libranza_rangos_cobro WHERE id_convenio=@idConvenioDecadal)
BEGIN
    INSERT INTO libranza_rangos_cobro (
        id_convenio, valor_desde, valor_hasta, tipo_cobro, valor_cobro,
        aplica_iva, estado, created_at, created_by_usuario)
    VALUES
        (@idConvenioDecadal,  50000.00, 100000.00, 'FIJO',  5000.00, 1, 'ACTIVO', SYSUTCDATETIME(), 1),
        (@idConvenioDecadal, 100001.00, 300000.00, 'FIJO',  9000.00, 1, 'ACTIVO', SYSUTCDATETIME(), 1),
        (@idConvenioDecadal, 300001.00, 600000.00, 'FIJO', 15000.00, 1, 'ACTIVO', SYSUTCDATETIME(), 1);
END;

IF NOT EXISTS (
    SELECT 1 FROM libranza_empleados
    WHERE id_convenio=@idConvenioDecadal AND tipo_documento='CC' AND numero_documento='1000000100')
    INSERT INTO libranza_empleados (
        id_convenio, tipo_documento, numero_documento, nombres, apellidos,
        celular, correo, cargo,
        salario_mensual, periodicidad_pago, dia_pago_1, dia_pago_2, dia_pago_3,
        estado, cupo_preliminar, fecha_ultimo_calculo_cupo, origen_carga,
        created_at, created_by_usuario)
    VALUES (
        @idConvenioDecadal, 'CC', '1000000100', 'Empleado Decadal', 'Demo',
        '3001001000', 'empleado.decadal@xpay.qa', 'Operario Decadal',
        3700000.00, 'DECADAL', 10, 20, 30,
        'ACTIVO', 1110000.00, SYSUTCDATETIME(), 'MANUAL', SYSUTCDATETIME(), 1);

DECLARE @idEmpleadoDecadal BIGINT =
    (SELECT id_empleado FROM libranza_empleados
     WHERE id_convenio=@idConvenioDecadal AND numero_documento='1000000100');

IF NOT EXISTS (SELECT 1 FROM libranza_empleado_cortes_pago WHERE id_empleado=@idEmpleadoDecadal AND numero_corte=1)
    INSERT INTO libranza_empleado_cortes_pago (id_empleado, numero_corte, dia_pago, valor_pago_programado, created_at, created_by_usuario)
    VALUES (@idEmpleadoDecadal, 1, 10, 1200000.00, SYSUTCDATETIME(), 1);

IF NOT EXISTS (SELECT 1 FROM libranza_empleado_cortes_pago WHERE id_empleado=@idEmpleadoDecadal AND numero_corte=2)
    INSERT INTO libranza_empleado_cortes_pago (id_empleado, numero_corte, dia_pago, valor_pago_programado, created_at, created_by_usuario)
    VALUES (@idEmpleadoDecadal, 2, 20, 1000000.00, SYSUTCDATETIME(), 1);

IF NOT EXISTS (SELECT 1 FROM libranza_empleado_cortes_pago WHERE id_empleado=@idEmpleadoDecadal AND numero_corte=3)
    INSERT INTO libranza_empleado_cortes_pago (id_empleado, numero_corte, dia_pago, valor_pago_programado, created_at, created_by_usuario)
    VALUES (@idEmpleadoDecadal, 3, 30, 1500000.00, SYSUTCDATETIME(), 1);

IF NOT EXISTS (SELECT 1 FROM libranza_usuarios_empresa WHERE id_usuario=12 AND id_convenio=@idConvenioDecadal)
    INSERT INTO libranza_usuarios_empresa (id_usuario, id_convenio, rol_empresa, estado, created_at)
    VALUES (12, @idConvenioDecadal, 'ADMIN_EMPRESA', 'ACTIVO', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM libranza_empleado_usuario WHERE id_empleado=@idEmpleadoDecadal)
    INSERT INTO libranza_empleado_usuario (id_empleado, id_usuario, id_wallet, estado, created_at, created_by_usuario)
    VALUES (@idEmpleadoDecadal, 3, 2, 'ACTIVO', SYSUTCDATETIME(), 1);
GO

-- ── Verificación ──────────────────────────────────────────────────────────────

DECLARE @idC BIGINT = (SELECT id_convenio FROM libranza_empresas_convenio WHERE nit='901777777-1');
DECLARE @idE BIGINT = (SELECT id_empleado FROM libranza_empleados WHERE id_convenio=@idC AND numero_documento='1000000100');

SELECT 'convenio' AS ok, id_convenio, nombre_empresa, periodicidad_pago, dia_pago_1, dia_pago_2, dia_pago_3
FROM libranza_empresas_convenio WHERE nit='901777777-1';

SELECT 'cortes' AS ok, numero_corte, dia_pago, valor_pago_programado
FROM libranza_empleado_cortes_pago WHERE id_empleado=@idE;

SELECT 'emp_usuario' AS ok, id_empleado, id_usuario, id_wallet
FROM libranza_empleado_usuario WHERE id_empleado=@idE;

SELECT 'cuentas_nuevas' AS ok, codigo, nombre
FROM ledger_cuentas WHERE codigo IN ('310103','230103');
GO
