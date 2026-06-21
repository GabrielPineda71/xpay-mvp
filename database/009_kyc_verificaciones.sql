/* ================================================================
   XPAY MVP — 009_kyc_verificaciones.sql
   ================================================================

   !! USO EXCLUSIVO: QA / DESARROLLO                           !!
   !! NO EJECUTAR EN PRODUCCIÓN                                !!
   !! NO CONTIENE NI GENERA DATOS REALES                       !!
   !! NO INVOLUCRA DINERO REAL                                 !!

   Prerrequisito: scripts 001 a 008 ejecutados correctamente.

   Idempotencia: el script puede ejecutarse más de una vez.
   Usa IF NOT EXISTS / IF COL_LENGTH para evitar errores en
   ejecuciones repetidas.

   Descripción:
   - Agrega columnas de resumen KYC a la tabla usuarios.
   - Crea la tabla kyc_verificaciones para historial y auditoría.
   - Los usuarios existentes quedan con estado_kyc_actual =
     'NO_INICIADO' gracias al DEFAULT de la columna nueva.
   - No genera filas en kyc_verificaciones para estado inicial
     (NO_INICIADO no requiere sesión Veriff).

   Fase 61 — Preparar modelo KYC y estados de verificación.
   ================================================================ */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT '================================================================';
PRINT ' XPAY 009_kyc_verificaciones — Inicio';
PRINT ' !! SOLO QA / DEV  —  NO PRODUCCION  —  NO DINERO REAL !!';
PRINT '================================================================';
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

-- Verificar que la columna existe y el DEFAULT funciona
SELECT TOP 5
    u.id_usuario,
    u.usuario,
    u.estado_kyc_actual,
    u.fecha_kyc_actualizacion
FROM   usuarios u
WHERE  u.usuario IN ('qa.usuario1','qa.usuario2','qa.admin.xpay','qa.operador.xpay')
ORDER  BY u.usuario;
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

PRINT '';
PRINT '================================================================';
PRINT ' XPAY 009_kyc_verificaciones — Completado.';
PRINT '';
PRINT ' Próximos pasos:';
PRINT '   1. Verificar que qa.usuario1/qa.usuario2 muestran NO_INICIADO.';
PRINT '   2. Usar POST /api/kyc/qa/simular-estado para simular estados.';
PRINT '   3. Fase 62: conectar SDK Veriff sandbox.';
PRINT '';
PRINT ' !! SOLO QA / DEV  —  NO PRODUCCION  —  NO DINERO REAL !!';
PRINT '================================================================';
GO
