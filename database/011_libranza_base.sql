/* XPAY MVP V1 - 011_libranza_base.sql */
/* Fase 66.1: Base módulo Libranza / Anticipo de Nómina */
/* Idempotente: usa IF NOT EXISTS / IF OBJECT_ID para re-ejecución segura */
/* No mueve saldos. No crea movimientos ledger. Solo estructura y datos demo. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- =========================================================================
-- 1. Tabla libranza_empresas_convenio
--    Convenio de libranza/anticipo de nómina por empresa.
-- =========================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'libranza_empresas_convenio')
BEGIN
    CREATE TABLE libranza_empresas_convenio (
        id_convenio             BIGINT          IDENTITY(1,1) NOT NULL,
        nombre_empresa          NVARCHAR(200)   NOT NULL,
        nit                     NVARCHAR(50)    NOT NULL,
        representante_legal     NVARCHAR(200)   NULL,
        email_contacto          NVARCHAR(200)   NULL,
        telefono_contacto       NVARCHAR(50)    NULL,
        direccion               NVARCHAR(300)   NULL,
        estado                  NVARCHAR(30)    NOT NULL CONSTRAINT df_lec_estado DEFAULT 'ACTIVO',
        dia_pago_1              INT             NULL,
        dia_pago_2              INT             NULL,
        periodicidad_pago       NVARCHAR(30)    NOT NULL,
        porcentaje_maximo_cupo  DECIMAL(5,2)    NOT NULL,
        observaciones           NVARCHAR(1000)  NULL,
        fecha_inicio            DATETIME2       NOT NULL CONSTRAINT df_lec_fecha_inicio DEFAULT SYSUTCDATETIME(),
        fecha_fin               DATETIME2       NULL,
        created_at              DATETIME2       NOT NULL CONSTRAINT df_lec_created_at DEFAULT SYSUTCDATETIME(),
        updated_at              DATETIME2       NULL,
        created_by_usuario      BIGINT          NULL,
        updated_by_usuario      BIGINT          NULL,
        CONSTRAINT pk_libranza_empresas_convenio PRIMARY KEY CLUSTERED (id_convenio),
        CONSTRAINT ck_lec_estado CHECK (estado IN ('ACTIVO','SUSPENDIDO','CANCELADO')),
        CONSTRAINT ck_lec_periodicidad CHECK (periodicidad_pago IN ('MENSUAL','QUINCENAL')),
        CONSTRAINT ck_lec_cupo CHECK (porcentaje_maximo_cupo BETWEEN 1 AND 100),
        CONSTRAINT ck_lec_dia_pago_1 CHECK (dia_pago_1 IS NULL OR (dia_pago_1 BETWEEN 1 AND 31)),
        CONSTRAINT ck_lec_dia_pago_2 CHECK (dia_pago_2 IS NULL OR (dia_pago_2 BETWEEN 1 AND 31))
    );
    PRINT 'CREATED TABLE libranza_empresas_convenio';
END
ELSE
    PRINT 'TABLE libranza_empresas_convenio already exists — skip';
GO

-- Índice único por NIT
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'uix_libranza_convenio_nit' AND object_id = OBJECT_ID('libranza_empresas_convenio'))
BEGIN
    CREATE UNIQUE INDEX uix_libranza_convenio_nit ON libranza_empresas_convenio (nit);
    PRINT 'CREATED INDEX uix_libranza_convenio_nit';
END
GO

-- Índice por estado
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_libranza_convenio_estado' AND object_id = OBJECT_ID('libranza_empresas_convenio'))
BEGIN
    CREATE INDEX ix_libranza_convenio_estado ON libranza_empresas_convenio (estado);
    PRINT 'CREATED INDEX ix_libranza_convenio_estado';
END
GO

-- Índice por nombre_empresa
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_libranza_convenio_nombre' AND object_id = OBJECT_ID('libranza_empresas_convenio'))
BEGIN
    CREATE INDEX ix_libranza_convenio_nombre ON libranza_empresas_convenio (nombre_empresa);
    PRINT 'CREATED INDEX ix_libranza_convenio_nombre';
END
GO

-- =========================================================================
-- 2. Tabla libranza_parametros_empresa
--    Parámetros operativos por convenio de libranza.
-- =========================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'libranza_parametros_empresa')
BEGIN
    CREATE TABLE libranza_parametros_empresa (
        id_parametro                BIGINT          IDENTITY(1,1) NOT NULL,
        id_convenio                 BIGINT          NOT NULL,
        porcentaje_maximo_cupo      DECIMAL(5,2)    NOT NULL,
        salario_minimo_empleado     DECIMAL(18,2)   NULL,
        salario_maximo_empleado     DECIMAL(18,2)   NULL,
        requiere_validacion_empresa BIT             NOT NULL CONSTRAINT df_lpe_requiere_val DEFAULT 1,
        permite_anticipo_multiple   BIT             NOT NULL CONSTRAINT df_lpe_multiple DEFAULT 0,
        max_anticipos_activos       INT             NOT NULL CONSTRAINT df_lpe_max_anticipos DEFAULT 1,
        iva_porcentaje              DECIMAL(5,2)    NOT NULL CONSTRAINT df_lpe_iva DEFAULT 19.00,
        momento_cobro_comision      NVARCHAR(20)    NOT NULL CONSTRAINT df_lpe_momento DEFAULT 'VENCIDO',
        estado                      NVARCHAR(30)    NOT NULL CONSTRAINT df_lpe_estado DEFAULT 'ACTIVO',
        created_at                  DATETIME2       NOT NULL CONSTRAINT df_lpe_created_at DEFAULT SYSUTCDATETIME(),
        updated_at                  DATETIME2       NULL,
        created_by_usuario          BIGINT          NULL,
        updated_by_usuario          BIGINT          NULL,
        CONSTRAINT pk_libranza_parametros_empresa PRIMARY KEY CLUSTERED (id_parametro),
        CONSTRAINT fk_lpe_convenio FOREIGN KEY (id_convenio)
            REFERENCES libranza_empresas_convenio (id_convenio),
        CONSTRAINT ck_lpe_cupo CHECK (porcentaje_maximo_cupo BETWEEN 1 AND 100),
        CONSTRAINT ck_lpe_iva CHECK (iva_porcentaje >= 0),
        CONSTRAINT ck_lpe_max_anticipos CHECK (max_anticipos_activos >= 1),
        CONSTRAINT ck_lpe_momento CHECK (momento_cobro_comision IN ('ANTICIPADO','VENCIDO')),
        CONSTRAINT ck_lpe_estado CHECK (estado IN ('ACTIVO','INACTIVO'))
    );
    PRINT 'CREATED TABLE libranza_parametros_empresa';
END
ELSE
    PRINT 'TABLE libranza_parametros_empresa already exists — skip';
GO

-- =========================================================================
-- 3. Tabla libranza_rangos_cobro
--    Rangos de comisión por valor del anticipo.
-- =========================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'libranza_rangos_cobro')
BEGIN
    CREATE TABLE libranza_rangos_cobro (
        id_rango            BIGINT          IDENTITY(1,1) NOT NULL,
        id_convenio         BIGINT          NOT NULL,
        valor_desde         DECIMAL(18,2)   NOT NULL,
        valor_hasta         DECIMAL(18,2)   NOT NULL,
        tipo_cobro          NVARCHAR(30)    NOT NULL,
        valor_cobro         DECIMAL(18,2)   NOT NULL,
        aplica_iva          BIT             NOT NULL CONSTRAINT df_lrc_aplica_iva DEFAULT 1,
        estado              NVARCHAR(30)    NOT NULL CONSTRAINT df_lrc_estado DEFAULT 'ACTIVO',
        created_at          DATETIME2       NOT NULL CONSTRAINT df_lrc_created_at DEFAULT SYSUTCDATETIME(),
        updated_at          DATETIME2       NULL,
        created_by_usuario  BIGINT          NULL,
        updated_by_usuario  BIGINT          NULL,
        CONSTRAINT pk_libranza_rangos_cobro PRIMARY KEY CLUSTERED (id_rango),
        CONSTRAINT fk_lrc_convenio FOREIGN KEY (id_convenio)
            REFERENCES libranza_empresas_convenio (id_convenio),
        CONSTRAINT ck_lrc_valor_desde CHECK (valor_desde >= 0),
        CONSTRAINT ck_lrc_valor_hasta CHECK (valor_hasta > valor_desde),
        CONSTRAINT ck_lrc_valor_cobro CHECK (valor_cobro >= 0),
        CONSTRAINT ck_lrc_tipo_cobro CHECK (tipo_cobro IN ('FIJO','PORCENTAJE')),
        CONSTRAINT ck_lrc_estado CHECK (estado IN ('ACTIVO','INACTIVO'))
    );
    PRINT 'CREATED TABLE libranza_rangos_cobro';
END
ELSE
    PRINT 'TABLE libranza_rangos_cobro already exists — skip';
GO

-- Índice por convenio para validación de rangos cruzados
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_libranza_rangos_convenio' AND object_id = OBJECT_ID('libranza_rangos_cobro'))
BEGIN
    CREATE INDEX ix_libranza_rangos_convenio ON libranza_rangos_cobro (id_convenio, estado);
    PRINT 'CREATED INDEX ix_libranza_rangos_convenio';
END
GO

-- =========================================================================
-- 4. Datos demo QA — Empresa Demo Libranza XPAY
--    Idempotente por NIT. No duplica si ya existe.
-- =========================================================================
IF NOT EXISTS (SELECT 1 FROM libranza_empresas_convenio WHERE nit = '900123456-7')
BEGIN
    DECLARE @id_conv BIGINT;

    INSERT INTO libranza_empresas_convenio (
        nombre_empresa, nit, representante_legal, email_contacto,
        telefono_contacto, periodicidad_pago, dia_pago_1,
        estado, porcentaje_maximo_cupo, fecha_inicio
    ) VALUES (
        'Empresa Demo Libranza XPAY', '900123456-7', 'Representante Demo',
        'empresa.demo.libranza@xpay.qa', '3000000000',
        'MENSUAL', 30, 'ACTIVO', 30.00, SYSUTCDATETIME()
    );

    SET @id_conv = SCOPE_IDENTITY();

    INSERT INTO libranza_parametros_empresa (
        id_convenio, porcentaje_maximo_cupo, salario_minimo_empleado,
        requiere_validacion_empresa, permite_anticipo_multiple,
        max_anticipos_activos, iva_porcentaje, momento_cobro_comision, estado
    ) VALUES (
        @id_conv, 30.00, 1000000.00, 1, 0, 1, 19.00, 'VENCIDO', 'ACTIVO'
    );

    INSERT INTO libranza_rangos_cobro (id_convenio, valor_desde, valor_hasta, tipo_cobro, valor_cobro, aplica_iva, estado)
    VALUES
        (@id_conv,  50000.00,  100000.00, 'FIJO',  5000.00, 1, 'ACTIVO'),
        (@id_conv, 100001.00,  300000.00, 'FIJO',  9000.00, 1, 'ACTIVO'),
        (@id_conv, 300001.00,  600000.00, 'FIJO', 15000.00, 1, 'ACTIVO');

    PRINT 'INSERTED demo convenio id=' + CAST(@id_conv AS NVARCHAR);
END
ELSE
    PRINT 'Demo convenio 900123456-7 already exists — skip';
GO
