-- =====================================================================
-- XPAY PROD - VARIANTE_PROD 017
-- Origen: database/017_comercio_usuarios_contexto.sql
-- SHA-256 del origen: 95ced065961038160d11402285f8fcf622adde9dd9a5ca3465bd271a94722ab2
-- Lineas conservadas del origen: 1-61
-- Estado en el manifiesto: LISTO
-- Generado por database/prod/tools/construir_paquete.py.
-- NO EDITAR A MANO: modificar la especificacion y regenerar.
-- El contenido despues de la marca es copia exacta de esas lineas.
-- =====================================================================
-- @@FIN_ENCABEZADO_VARIANTE_PROD@@
SET QUOTED_IDENTIFIER ON;
GO

-- ── 1. Tabla comercio_usuarios ────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'comercio_usuarios')
BEGIN
    CREATE TABLE comercio_usuarios (
        id_comercio_usuario BIGINT        IDENTITY(1,1) PRIMARY KEY,
        id_comercio_aliado  BIGINT        NOT NULL,
        id_comercio_existente BIGINT      NULL,
        id_establecimiento  BIGINT        NULL,
        id_usuario          BIGINT        NOT NULL,
        rol_comercio        NVARCHAR(30)  NOT NULL,
        estado              NVARCHAR(30)  NOT NULL DEFAULT 'ACTIVO',
        created_at          DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at          DATETIME2     NULL,
        created_by_usuario  BIGINT        NULL,
        updated_by_usuario  BIGINT        NULL,
        CONSTRAINT fk_cu_aliado      FOREIGN KEY (id_comercio_aliado)    REFERENCES comercios_aliados(id_comercio_aliado),
        CONSTRAINT fk_cu_usuario     FOREIGN KEY (id_usuario)             REFERENCES usuarios(id_usuario),
        CONSTRAINT ck_cu_rol         CHECK (rol_comercio IN ('ADMIN_COMERCIO','ADMIN_SEDE_COMERCIO','CAJERO')),
        CONSTRAINT ck_cu_estado      CHECK (estado IN ('ACTIVO','INACTIVO'))
    );
    PRINT 'TABLE comercio_usuarios created';
END
ELSE
    PRINT 'TABLE comercio_usuarios already exists';
GO

-- Índice para búsqueda por usuario
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_cu_usuario' AND object_id = OBJECT_ID('comercio_usuarios'))
    CREATE INDEX ix_cu_usuario  ON comercio_usuarios(id_usuario, estado);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_cu_aliado' AND object_id = OBJECT_ID('comercio_usuarios'))
    CREATE INDEX ix_cu_aliado   ON comercio_usuarios(id_comercio_aliado, estado);
GO

-- ── 2. Tabla comercio_ventas_qr_contexto ──────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'comercio_ventas_qr_contexto')
BEGIN
    CREATE TABLE comercio_ventas_qr_contexto (
        id_contexto           BIGINT    IDENTITY(1,1) PRIMARY KEY,
        id_venta_qr           BIGINT    NOT NULL,
        id_comercio_aliado    BIGINT    NOT NULL,
        id_comercio_existente BIGINT    NOT NULL,
        id_establecimiento    BIGINT    NULL,
        id_cajero_usuario     BIGINT    NULL,
        created_at            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT uq_cvqc_venta  UNIQUE (id_venta_qr),
        CONSTRAINT fk_cvqc_venta  FOREIGN KEY (id_venta_qr)        REFERENCES ventas_qr(id_venta_qr),
        CONSTRAINT fk_cvqc_aliado FOREIGN KEY (id_comercio_aliado) REFERENCES comercios_aliados(id_comercio_aliado)
    );
    PRINT 'TABLE comercio_ventas_qr_contexto created';
END
ELSE
    PRINT 'TABLE comercio_ventas_qr_contexto already exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_cvqc_aliado' AND object_id = OBJECT_ID('comercio_ventas_qr_contexto'))
    CREATE INDEX ix_cvqc_aliado ON comercio_ventas_qr_contexto(id_comercio_aliado, id_establecimiento);
GO
