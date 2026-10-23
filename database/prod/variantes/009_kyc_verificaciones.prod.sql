-- =====================================================================
-- XPAY PROD - VARIANTE_PROD 009
-- Origen: database/009_kyc_verificaciones.sql
-- SHA-256 del origen: 6b54f23cefe9a5a1b3dd90f868c1effa709c33772a9e62533cecd3fcb66dd315
-- Lineas conservadas del origen: 26-28, 36-135, 137-142, 155-166
-- Estado en el manifiesto: LISTO
-- Generado por database/prod/tools/construir_paquete.py.
-- NO EDITAR A MANO: modificar la especificacion y regenerar.
-- El contenido despues de la marca es copia exacta de esas lineas.
-- =====================================================================
-- @@FIN_ENCABEZADO_VARIANTE_PROD@@
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- ================================================================
-- SECCIÓN 1: Columnas KYC en tabla usuarios
-- estado_kyc_actual     — resumen rápido sin JOIN
-- fecha_kyc_actualizacion — timestamp de último cambio de estado
-- ================================================================
PRINT '';
PRINT '--- Sección 1: Columnas KYC en usuarios ---';
GO

IF COL_LENGTH('usuarios', 'estado_kyc_actual') IS NULL
BEGIN
    ALTER TABLE usuarios
    ADD estado_kyc_actual VARCHAR(30) NOT NULL
        CONSTRAINT DF_usuarios_estado_kyc_actual DEFAULT 'NO_INICIADO';
    PRINT '  OK — columna estado_kyc_actual agregada (DEFAULT NO_INICIADO).';
END
ELSE
    PRINT '  SKIP — columna estado_kyc_actual ya existe.';
GO

IF COL_LENGTH('usuarios', 'fecha_kyc_actualizacion') IS NULL
BEGIN
    ALTER TABLE usuarios
    ADD fecha_kyc_actualizacion DATETIME2 NULL;
    PRINT '  OK — columna fecha_kyc_actualizacion agregada.';
END
ELSE
    PRINT '  SKIP — columna fecha_kyc_actualizacion ya existe.';
GO

-- ================================================================
-- SECCIÓN 2: Tabla kyc_verificaciones
-- Historial completo de verificaciones KYC por usuario.
-- Campos Veriff: session_id, session_url, decision, reason,
-- vendor_data se poblaran en fases posteriores cuando se
-- conecte el SDK de Veriff.
-- ================================================================
PRINT '';
PRINT '--- Sección 2: Tabla kyc_verificaciones ---';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.objects
    WHERE  name = 'kyc_verificaciones' AND type = 'U'
)
BEGIN
    CREATE TABLE kyc_verificaciones (
        id_kyc_verificacion   BIGINT        IDENTITY(1,1) NOT NULL PRIMARY KEY,
        id_usuario            BIGINT        NOT NULL,
        id_persona            BIGINT        NULL,
        proveedor             VARCHAR(50)   NOT NULL CONSTRAINT DF_kyc_proveedor           DEFAULT 'VERIFF',
        estado_kyc            VARCHAR(30)   NOT NULL CONSTRAINT DF_kyc_estado              DEFAULT 'NO_INICIADO',
        session_id            VARCHAR(200)  NULL,
        session_url           VARCHAR(1000) NULL,
        decision              VARCHAR(50)   NULL,
        reason                VARCHAR(500)  NULL,
        vendor_data           VARCHAR(500)  NULL,
        es_actual             BIT           NOT NULL CONSTRAINT DF_kyc_es_actual           DEFAULT 1,
        fecha_creacion        DATETIME2     NOT NULL CONSTRAINT DF_kyc_fecha_creacion      DEFAULT SYSDATETIME(),
        fecha_actualizacion   DATETIME2     NULL,
        fecha_decision        DATETIME2     NULL,
        CONSTRAINT FK_kyc_verificaciones_usuario
            FOREIGN KEY (id_usuario) REFERENCES usuarios(id_usuario),
        CONSTRAINT FK_kyc_verificaciones_persona
            FOREIGN KEY (id_persona) REFERENCES personas(id_persona)
    );
    PRINT '  OK — tabla kyc_verificaciones creada.';
END
ELSE
    PRINT '  SKIP — tabla kyc_verificaciones ya existe.';
GO

-- Índices
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE  name = 'IX_kyc_verificaciones_usuario'
      AND  object_id = OBJECT_ID('kyc_verificaciones')
)
BEGIN
    CREATE INDEX IX_kyc_verificaciones_usuario
        ON kyc_verificaciones(id_usuario);
    PRINT '  OK — índice IX_kyc_verificaciones_usuario creado.';
END
ELSE
    PRINT '  SKIP — índice IX_kyc_verificaciones_usuario ya existe.';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE  name = 'IX_kyc_verificaciones_usuario_actual'
      AND  object_id = OBJECT_ID('kyc_verificaciones')
)
BEGIN
    CREATE INDEX IX_kyc_verificaciones_usuario_actual
        ON kyc_verificaciones(id_usuario, es_actual);
    PRINT '  OK — índice IX_kyc_verificaciones_usuario_actual creado.';
END
ELSE
    PRINT '  SKIP — índice IX_kyc_verificaciones_usuario_actual ya existe.';
GO
-- ================================================================
-- SECCIÓN 3: Verificación del resultado
-- ================================================================
PRINT '';
PRINT '--- Sección 3: Verificación ---';
GO
-- Verificar que la tabla existe y su estructura
SELECT
    c.name          AS columna,
    t.name          AS tipo,
    c.max_length,
    c.is_nullable,
    c.is_identity
FROM   sys.columns      c
JOIN   sys.types        t ON t.user_type_id = c.user_type_id
WHERE  c.object_id = OBJECT_ID('kyc_verificaciones')
ORDER  BY c.column_id;
GO
