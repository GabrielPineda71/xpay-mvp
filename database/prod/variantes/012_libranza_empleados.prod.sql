-- =====================================================================
-- XPAY PROD - VARIANTE_PROD 012
-- Origen: database/012_libranza_empleados.sql
-- SHA-256 del origen: 0af1d2a8d8fb710768881457629026f626fed3a15b1f58c9f597ae3e51672d64
-- Lineas conservadas del origen: 1-154
-- Estado en el manifiesto: LISTO
-- Generado por database/prod/tools/construir_paquete.py.
-- NO EDITAR A MANO: modificar la especificacion y regenerar.
-- El contenido despues de la marca es copia exacta de esas lineas.
-- =====================================================================
-- @@FIN_ENCABEZADO_VARIANTE_PROD@@
/* XPAY MVP V1 - 012_libranza_empleados.sql */
/* Fase 66.2: Empleados y carga Excel módulo Libranza */
/* Idempotente. No mueve saldos. No toca ledger. Solo estructura y datos demo. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- =========================================================================
-- 1. libranza_usuarios_empresa
--    Asocia un usuario XPAY a un convenio de empresa como operador.
-- =========================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'libranza_usuarios_empresa')
BEGIN
    CREATE TABLE libranza_usuarios_empresa (
        id_usuario_empresa  BIGINT          IDENTITY(1,1) NOT NULL,
        id_usuario          BIGINT          NOT NULL,
        id_convenio         BIGINT          NOT NULL,
        rol_empresa         NVARCHAR(30)    NOT NULL CONSTRAINT df_lue_rol DEFAULT 'ADMIN_EMPRESA',
        estado              NVARCHAR(30)    NOT NULL CONSTRAINT df_lue_estado DEFAULT 'ACTIVO',
        created_at          DATETIME2       NOT NULL CONSTRAINT df_lue_created_at DEFAULT SYSUTCDATETIME(),
        updated_at          DATETIME2       NULL,
        created_by_usuario  BIGINT          NULL,
        updated_by_usuario  BIGINT          NULL,
        CONSTRAINT pk_libranza_usuarios_empresa PRIMARY KEY CLUSTERED (id_usuario_empresa),
        CONSTRAINT fk_lue_convenio FOREIGN KEY (id_convenio)
            REFERENCES libranza_empresas_convenio (id_convenio),
        CONSTRAINT ck_lue_rol   CHECK (rol_empresa IN ('ADMIN_EMPRESA','OPERADOR_EMPRESA','CONSULTA_EMPRESA')),
        CONSTRAINT ck_lue_estado CHECK (estado IN ('ACTIVO','INACTIVO'))
    );
    PRINT 'CREATED TABLE libranza_usuarios_empresa';
END
ELSE
    PRINT 'TABLE libranza_usuarios_empresa already exists — skip';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_lue_usuario_convenio' AND object_id = OBJECT_ID('libranza_usuarios_empresa'))
BEGIN
    CREATE INDEX ix_lue_usuario_convenio ON libranza_usuarios_empresa (id_usuario, id_convenio, estado);
    PRINT 'CREATED INDEX ix_lue_usuario_convenio';
END
GO

-- =========================================================================
-- 2. libranza_empleados
-- =========================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'libranza_empleados')
BEGIN
    CREATE TABLE libranza_empleados (
        id_empleado                     BIGINT          IDENTITY(1,1) NOT NULL,
        id_convenio                     BIGINT          NOT NULL,
        tipo_documento                  NVARCHAR(20)    NOT NULL,
        numero_documento                NVARCHAR(50)    NOT NULL,
        nombres                         NVARCHAR(150)   NOT NULL,
        apellidos                       NVARCHAR(150)   NULL,
        celular                         NVARCHAR(50)    NULL,
        correo                          NVARCHAR(200)   NULL,
        cargo                           NVARCHAR(150)   NULL,
        salario_mensual                 DECIMAL(18,2)   NOT NULL,
        periodicidad_pago               NVARCHAR(30)    NOT NULL,
        dia_pago_1                      INT             NULL,
        dia_pago_2                      INT             NULL,
        fecha_ingreso                   DATE            NULL,
        estado                          NVARCHAR(30)    NOT NULL CONSTRAINT df_le_estado DEFAULT 'ACTIVO',
        cupo_preliminar                 DECIMAL(18,2)   NOT NULL CONSTRAINT df_le_cupo DEFAULT 0,
        fecha_ultimo_calculo_cupo       DATETIME2       NULL,
        origen_carga                    NVARCHAR(30)    NOT NULL CONSTRAINT df_le_origen DEFAULT 'MANUAL',
        lote_importacion                NVARCHAR(100)   NULL,
        observaciones                   NVARCHAR(1000)  NULL,
        created_at                      DATETIME2       NOT NULL CONSTRAINT df_le_created_at DEFAULT SYSUTCDATETIME(),
        updated_at                      DATETIME2       NULL,
        created_by_usuario              BIGINT          NULL,
        updated_by_usuario              BIGINT          NULL,
        CONSTRAINT pk_libranza_empleados PRIMARY KEY CLUSTERED (id_empleado),
        CONSTRAINT fk_le_convenio FOREIGN KEY (id_convenio)
            REFERENCES libranza_empresas_convenio (id_convenio),
        CONSTRAINT ck_le_tipo_doc      CHECK (tipo_documento IN ('CC','CE','NIT','PASAPORTE','OTRO')),
        CONSTRAINT ck_le_salario       CHECK (salario_mensual > 0),
        CONSTRAINT ck_le_periodicidad  CHECK (periodicidad_pago IN ('MENSUAL','QUINCENAL')),
        CONSTRAINT ck_le_dia_pago_1    CHECK (dia_pago_1 IS NULL OR (dia_pago_1 BETWEEN 1 AND 31)),
        CONSTRAINT ck_le_dia_pago_2    CHECK (dia_pago_2 IS NULL OR (dia_pago_2 BETWEEN 1 AND 31)),
        CONSTRAINT ck_le_estado        CHECK (estado IN ('ACTIVO','INACTIVO','RETIRADO','SUSPENDIDO')),
        CONSTRAINT ck_le_cupo          CHECK (cupo_preliminar >= 0),
        CONSTRAINT ck_le_origen        CHECK (origen_carga IN ('EXCEL','MANUAL'))
    );
    PRINT 'CREATED TABLE libranza_empleados';
END
ELSE
    PRINT 'TABLE libranza_empleados already exists — skip';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_le_convenio' AND object_id = OBJECT_ID('libranza_empleados'))
BEGIN
    CREATE INDEX ix_le_convenio ON libranza_empleados (id_convenio, estado);
    PRINT 'CREATED INDEX ix_le_convenio';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_le_documento' AND object_id = OBJECT_ID('libranza_empleados'))
BEGIN
    CREATE INDEX ix_le_documento ON libranza_empleados (numero_documento);
    PRINT 'CREATED INDEX ix_le_documento';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_le_estado' AND object_id = OBJECT_ID('libranza_empleados'))
BEGIN
    CREATE INDEX ix_le_estado ON libranza_empleados (estado);
    PRINT 'CREATED INDEX ix_le_estado';
END
GO
-- Unique activo por convenio+tipo+doc
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'uix_le_convenio_doc_activo' AND object_id = OBJECT_ID('libranza_empleados'))
BEGIN
    CREATE UNIQUE INDEX uix_le_convenio_doc_activo
        ON libranza_empleados (id_convenio, tipo_documento, numero_documento)
        WHERE estado = 'ACTIVO';
    PRINT 'CREATED INDEX uix_le_convenio_doc_activo';
END
GO

-- =========================================================================
-- 3. libranza_importaciones_empleados
-- =========================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'libranza_importaciones_empleados')
BEGIN
    CREATE TABLE libranza_importaciones_empleados (
        id_importacion      BIGINT          IDENTITY(1,1) NOT NULL,
        id_convenio         BIGINT          NOT NULL,
        nombre_archivo      NVARCHAR(300)   NULL,
        lote_importacion    NVARCHAR(100)   NOT NULL,
        total_filas         INT             NOT NULL CONSTRAINT df_lie_total DEFAULT 0,
        filas_validas       INT             NOT NULL CONSTRAINT df_lie_validas DEFAULT 0,
        filas_error         INT             NOT NULL CONSTRAINT df_lie_error DEFAULT 0,
        empleados_creados   INT             NOT NULL CONSTRAINT df_lie_creados DEFAULT 0,
        empleados_actualizados INT          NOT NULL CONSTRAINT df_lie_actualizados DEFAULT 0,
        estado              NVARCHAR(30)    NOT NULL CONSTRAINT df_lie_estado DEFAULT 'PROCESADA',
        errores_json        NVARCHAR(MAX)   NULL,
        created_at          DATETIME2       NOT NULL CONSTRAINT df_lie_created_at DEFAULT SYSUTCDATETIME(),
        created_by_usuario  BIGINT          NULL,
        CONSTRAINT pk_libranza_importaciones_empleados PRIMARY KEY CLUSTERED (id_importacion),
        CONSTRAINT fk_lie_convenio FOREIGN KEY (id_convenio)
            REFERENCES libranza_empresas_convenio (id_convenio),
        CONSTRAINT ck_lie_estado CHECK (estado IN ('PROCESADA','PROCESADA_CON_ERRORES','ERROR'))
    );
    PRINT 'CREATED TABLE libranza_importaciones_empleados';
END
ELSE
    PRINT 'TABLE libranza_importaciones_empleados already exists — skip';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_lie_convenio' AND object_id = OBJECT_ID('libranza_importaciones_empleados'))
BEGIN
    CREATE INDEX ix_lie_convenio ON libranza_importaciones_empleados (id_convenio);
    PRINT 'CREATED INDEX ix_lie_convenio';
END
GO
