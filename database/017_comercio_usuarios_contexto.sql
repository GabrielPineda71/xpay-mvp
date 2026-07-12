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

-- ── 3. Seed QA: qa.comercio1 como ADMIN_COMERCIO ──────────────────────────────
DECLARE @id_usuario       BIGINT = (SELECT id_usuario FROM usuarios WHERE usuario = 'qa.comercio1');
DECLARE @id_comercio_aliado BIGINT = 1;
DECLARE @id_comercio_existente BIGINT = 2;

IF @id_usuario IS NOT NULL AND NOT EXISTS (
    SELECT 1 FROM comercio_usuarios
    WHERE id_usuario = @id_usuario AND id_comercio_aliado = @id_comercio_aliado AND estado = 'ACTIVO'
)
BEGIN
    INSERT INTO comercio_usuarios
        (id_comercio_aliado, id_comercio_existente, id_establecimiento, id_usuario, rol_comercio, estado)
    VALUES (@id_comercio_aliado, @id_comercio_existente, NULL, @id_usuario, 'ADMIN_COMERCIO', 'ACTIVO');
    PRINT 'SEED: qa.comercio1 insertado como ADMIN_COMERCIO';
END
ELSE
    PRINT 'SEED: qa.comercio1 ya existe en comercio_usuarios (no se duplica)';
GO

-- ── 4. Backfill ventas QR existentes → comercio_ventas_qr_contexto ───────────
-- Asignar todas las ventas del comercio operativo 2 a Comercio Aliado 1 / Establecimiento 1 (Tienda Principal Demo)
DECLARE @id_aliado    BIGINT = 1;
DECLARE @id_operativo BIGINT = 2;
DECLARE @id_sede      BIGINT = (SELECT id_establecimiento FROM comercio_establecimientos WHERE id_comercio_aliado = 1 AND nombre_establecimiento = 'Tienda Principal Demo');

INSERT INTO comercio_ventas_qr_contexto
    (id_venta_qr, id_comercio_aliado, id_comercio_existente, id_establecimiento, id_cajero_usuario)
SELECT
    v.id_venta_qr,
    @id_aliado,
    @id_operativo,
    @id_sede,
    NULL   -- cajero desconocido para ventas anteriores
FROM ventas_qr v
WHERE v.id_comercio = @id_operativo
  AND NOT EXISTS (
    SELECT 1 FROM comercio_ventas_qr_contexto c WHERE c.id_venta_qr = v.id_venta_qr
  );
PRINT CAST(@@ROWCOUNT AS NVARCHAR) + ' ventas QR backfilled en contexto';
GO

-- ── 5. Verificación ───────────────────────────────────────────────────────────
SELECT 'comercio_usuarios' AS tabla, COUNT(*) AS filas FROM comercio_usuarios
UNION ALL
SELECT 'comercio_ventas_qr_contexto', COUNT(*) FROM comercio_ventas_qr_contexto;
GO
