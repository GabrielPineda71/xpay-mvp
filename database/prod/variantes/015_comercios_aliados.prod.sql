-- =====================================================================
-- XPAY PROD - VARIANTE_PROD 015
-- Origen: database/015_comercios_aliados.sql
-- SHA-256 del origen: e97bf8883fc9a934ee222eadf3b82a02881103b624fc7a370829e58f640efaa9
-- Lineas conservadas del origen: 1-148
-- Estado en el manifiesto: LISTO
-- Generado por database/prod/tools/construir_paquete.py.
-- NO EDITAR A MANO: modificar la especificacion y regenerar.
-- El contenido despues de la marca es copia exacta de esas lineas.
-- =====================================================================
-- @@FIN_ENCABEZADO_VARIANTE_PROD@@
-- ============================================================
-- Fase 67.1 — Comercios Aliados: tablas + demo data
-- Idempotente — ejecutar con sqlcmd
-- ============================================================
SET QUOTED_IDENTIFIER ON;
GO

-- 1. comercios_aliados
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'comercios_aliados')
BEGIN
    CREATE TABLE comercios_aliados (
        id_comercio_aliado       BIGINT IDENTITY(1,1)  PRIMARY KEY,
        id_comercio_existente    BIGINT                NULL,
        razon_social             NVARCHAR(200)         NOT NULL,
        nombre_comercial         NVARCHAR(200)         NOT NULL,
        nit                      NVARCHAR(50)          NOT NULL,
        tipo_persona             NVARCHAR(30)          NOT NULL,
        actividad_economica      NVARCHAR(300)         NULL,
        codigo_ciiu              NVARCHAR(20)          NULL,
        direccion_principal      NVARCHAR(300)         NULL,
        ciudad                   NVARCHAR(100)         NULL,
        departamento             NVARCHAR(100)         NULL,
        telefono                 NVARCHAR(50)          NULL,
        correo                   NVARCHAR(200)         NULL,
        sitio_web                NVARCHAR(200)         NULL,
        estado                   NVARCHAR(30)          NOT NULL CONSTRAINT df_ca_estado DEFAULT 'BORRADOR',
        condiciones_comerciales  NVARCHAR(2000)        NULL,
        fecha_solicitud          DATETIME2             NOT NULL CONSTRAINT df_ca_fecha_sol DEFAULT SYSUTCDATETIME(),
        fecha_aprobacion         DATETIME2             NULL,
        fecha_inicio_convenio    DATE                  NULL,
        fecha_fin_convenio       DATE                  NULL,
        observaciones            NVARCHAR(1000)        NULL,
        created_at               DATETIME2             NOT NULL CONSTRAINT df_ca_created DEFAULT SYSUTCDATETIME(),
        updated_at               DATETIME2             NULL,
        created_by_usuario       BIGINT                NULL,
        updated_by_usuario       BIGINT                NULL,
        CONSTRAINT ck_ca_tipo_persona CHECK (tipo_persona IN ('NATURAL','JURIDICA')),
        CONSTRAINT ck_ca_estado       CHECK (estado IN ('BORRADOR','EN_REVISION','APROBADO','RECHAZADO','ACTIVO','INACTIVO')),
        CONSTRAINT uq_ca_nit          UNIQUE (nit)
    );
    PRINT 'Tabla comercios_aliados creada';
END
ELSE
    PRINT 'Tabla comercios_aliados ya existe';
GO

-- 2. comercio_representantes_legales
SET QUOTED_IDENTIFIER ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'comercio_representantes_legales')
BEGIN
    CREATE TABLE comercio_representantes_legales (
        id_representante             BIGINT IDENTITY(1,1) PRIMARY KEY,
        id_comercio_aliado           BIGINT               NOT NULL,
        tipo_documento               NVARCHAR(20)         NOT NULL,
        numero_documento             NVARCHAR(50)         NOT NULL,
        nombres                      NVARCHAR(150)        NOT NULL,
        apellidos                    NVARCHAR(150)        NULL,
        celular                      NVARCHAR(50)         NULL,
        correo                       NVARCHAR(200)        NULL,
        cargo                        NVARCHAR(100)        NULL,
        fecha_expedicion_documento   DATE                 NULL,
        estado                       NVARCHAR(30)         NOT NULL CONSTRAINT df_crl_estado DEFAULT 'ACTIVO',
        created_at                   DATETIME2            NOT NULL CONSTRAINT df_crl_created DEFAULT SYSUTCDATETIME(),
        updated_at                   DATETIME2            NULL,
        CONSTRAINT ck_crl_estado CHECK (estado IN ('ACTIVO','INACTIVO'))
    );
    PRINT 'Tabla comercio_representantes_legales creada';
END
ELSE
    PRINT 'Tabla comercio_representantes_legales ya existe';
GO

-- 3. comercio_establecimientos
SET QUOTED_IDENTIFIER ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'comercio_establecimientos')
BEGIN
    CREATE TABLE comercio_establecimientos (
        id_establecimiento        BIGINT IDENTITY(1,1) PRIMARY KEY,
        id_comercio_aliado        BIGINT               NOT NULL,
        nombre_establecimiento    NVARCHAR(200)        NOT NULL,
        direccion                 NVARCHAR(300)        NULL,
        ciudad                    NVARCHAR(100)        NULL,
        telefono                  NVARCHAR(50)         NULL,
        responsable               NVARCHAR(150)        NULL,
        estado                    NVARCHAR(30)         NOT NULL CONSTRAINT df_ce_estado DEFAULT 'ACTIVO',
        created_at                DATETIME2            NOT NULL CONSTRAINT df_ce_created DEFAULT SYSUTCDATETIME(),
        updated_at                DATETIME2            NULL,
        CONSTRAINT ck_ce_estado CHECK (estado IN ('ACTIVO','INACTIVO'))
    );
    PRINT 'Tabla comercio_establecimientos creada';
END
ELSE
    PRINT 'Tabla comercio_establecimientos ya existe';
GO

-- 4. comercio_usuarios_solicitados
SET QUOTED_IDENTIFIER ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'comercio_usuarios_solicitados')
BEGIN
    CREATE TABLE comercio_usuarios_solicitados (
        id_usuario_solicitado    BIGINT IDENTITY(1,1) PRIMARY KEY,
        id_comercio_aliado       BIGINT               NOT NULL,
        id_establecimiento       BIGINT               NULL,
        id_usuario               BIGINT               NULL,
        nombres                  NVARCHAR(150)        NOT NULL,
        correo                   NVARCHAR(200)        NULL,
        celular                  NVARCHAR(50)         NULL,
        rol_solicitado           NVARCHAR(30)         NOT NULL,
        estado                   NVARCHAR(30)         NOT NULL CONSTRAINT df_cus_estado DEFAULT 'PENDIENTE_CREACION',
        created_at               DATETIME2            NOT NULL CONSTRAINT df_cus_created DEFAULT SYSUTCDATETIME(),
        updated_at               DATETIME2            NULL,
        CONSTRAINT ck_cus_rol    CHECK (rol_solicitado IN ('ADMIN_COMERCIO','CAJERO')),
        CONSTRAINT ck_cus_estado CHECK (estado IN ('PENDIENTE_CREACION','CREADO','INACTIVO'))
    );
    PRINT 'Tabla comercio_usuarios_solicitados creada';
END
ELSE
    PRINT 'Tabla comercio_usuarios_solicitados ya existe';
GO

-- 5. comercio_documentos
SET QUOTED_IDENTIFIER ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'comercio_documentos')
BEGIN
    CREATE TABLE comercio_documentos (
        id_documento             BIGINT IDENTITY(1,1)  PRIMARY KEY,
        id_comercio_aliado       BIGINT                NOT NULL,
        tipo_documento           NVARCHAR(50)          NOT NULL,
        nombre_archivo_original  NVARCHAR(300)         NOT NULL,
        storage_path             NVARCHAR(500)         NOT NULL,
        content_type             NVARCHAR(150)         NULL,
        size_bytes               BIGINT                NULL,
        estado                   NVARCHAR(30)          NOT NULL CONSTRAINT df_cd_estado DEFAULT 'ACTIVO',
        observaciones            NVARCHAR(500)         NULL,
        uploaded_at              DATETIME2             NOT NULL CONSTRAINT df_cd_uploaded DEFAULT SYSUTCDATETIME(),
        uploaded_by_usuario      BIGINT                NULL,
        CONSTRAINT ck_cd_tipo    CHECK (tipo_documento IN ('CONTRATO','CAMARA_COMERCIO','RUT','DOCUMENTO_REPRESENTANTE','FORMULARIO_SOLICITUD')),
        CONSTRAINT ck_cd_estado  CHECK (estado IN ('ACTIVO','REEMPLAZADO','ELIMINADO'))
    );
    PRINT 'Tabla comercio_documentos creada';
END
ELSE
    PRINT 'Tabla comercio_documentos ya existe';
GO
