-- =====================================================================
-- Migración 026: Liquidación de recaudos de comercio hacia XPAY
-- Idempotente — no borra datos, no toca saldos de Wallet, no recalcula
-- recargas existentes.
--
-- Dos tablas 100% nuevas (wallet_liquidaciones_recaudo_comercio y su
-- detalle) — nunca tocadas por el pipeline de CI (que solo aplica
-- migraciones 001-010 sobre una base nueva). Las 3 columnas nuevas se
-- agregan a wallet_recargas_comercio, tabla creada en la migración 025
-- (también fuera del baseline de CI), así que tampoco afectan ninguna
-- consulta existente probada en CI.
--
-- Lección aplicada de Fase 69.3/70.1: cualquier sentencia que referencie
-- una columna agregada por ALTER TABLE ADD dentro del mismo batch falla
-- con "Invalid column name" porque SQL Server resuelve nombres de columna
-- contra el esquema compilado al inicio del batch. Como el índice nuevo
-- de wallet_recargas_comercio referencia la columna id_liquidacion_recaudo
-- agregada más arriba en este mismo script, ese CREATE INDEX va en SQL
-- dinámico (sp_executesql) para compilarse en tiempo de ejecución, ya con
-- la columna visible.
-- =====================================================================

PRINT '=== INICIO MIGRACIÓN 026: Liquidación de recaudos de comercio hacia XPAY ===';

-- ── 1. Tabla wallet_liquidaciones_recaudo_comercio ─────────────────────

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='wallet_liquidaciones_recaudo_comercio')
BEGIN
    CREATE TABLE wallet_liquidaciones_recaudo_comercio (
        id_liquidacion          BIGINT          IDENTITY(1,1) PRIMARY KEY,
        id_unidad_negocio       BIGINT          NOT NULL DEFAULT 1,
        id_comercio             BIGINT          NOT NULL,
        id_comercio_aliado      BIGINT          NULL,
        id_tienda               BIGINT          NULL,
        id_usuario_admin        BIGINT          NOT NULL,
        id_transaccion_ledger   BIGINT          NULL,
        metodo_liquidacion      VARCHAR(40)     NOT NULL,
        valor_total             DECIMAL(18,2)   NOT NULL,
        cantidad_recargas       INT             NOT NULL,
        estado                  VARCHAR(30)     NOT NULL DEFAULT 'APLICADA',
        referencia_externa      VARCHAR(100)    NULL,
        observaciones           VARCHAR(500)    NULL,
        fecha_liquidacion       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
        created_at              DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
    PRINT 'OK: tabla wallet_liquidaciones_recaudo_comercio creada';
END
ELSE
    PRINT 'SKIP: tabla wallet_liquidaciones_recaudo_comercio ya existe';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wallet_liquidaciones_recaudo_comercio_comercio_fecha' AND object_id=OBJECT_ID('wallet_liquidaciones_recaudo_comercio'))
    CREATE INDEX ix_wallet_liquidaciones_recaudo_comercio_comercio_fecha ON wallet_liquidaciones_recaudo_comercio (id_comercio, fecha_liquidacion);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wallet_liquidaciones_recaudo_comercio_admin_fecha' AND object_id=OBJECT_ID('wallet_liquidaciones_recaudo_comercio'))
    CREATE INDEX ix_wallet_liquidaciones_recaudo_comercio_admin_fecha ON wallet_liquidaciones_recaudo_comercio (id_usuario_admin, fecha_liquidacion);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wallet_liquidaciones_recaudo_comercio_ledger' AND object_id=OBJECT_ID('wallet_liquidaciones_recaudo_comercio'))
    CREATE INDEX ix_wallet_liquidaciones_recaudo_comercio_ledger ON wallet_liquidaciones_recaudo_comercio (id_transaccion_ledger);

PRINT 'OK: índices de wallet_liquidaciones_recaudo_comercio verificados/creados';

-- ── 2. Tabla wallet_liquidaciones_recaudo_comercio_detalle ─────────────

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='wallet_liquidaciones_recaudo_comercio_detalle')
BEGIN
    CREATE TABLE wallet_liquidaciones_recaudo_comercio_detalle (
        id_detalle       BIGINT          IDENTITY(1,1) PRIMARY KEY,
        id_liquidacion   BIGINT          NOT NULL,
        id_recarga       BIGINT          NOT NULL,
        valor            DECIMAL(18,2)   NOT NULL,
        created_at       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
    PRINT 'OK: tabla wallet_liquidaciones_recaudo_comercio_detalle creada';
END
ELSE
    PRINT 'SKIP: tabla wallet_liquidaciones_recaudo_comercio_detalle ya existe';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wallet_liquidaciones_recaudo_comercio_detalle_liquidacion' AND object_id=OBJECT_ID('wallet_liquidaciones_recaudo_comercio_detalle'))
    CREATE INDEX ix_wallet_liquidaciones_recaudo_comercio_detalle_liquidacion ON wallet_liquidaciones_recaudo_comercio_detalle (id_liquidacion);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wallet_liquidaciones_recaudo_comercio_detalle_recarga' AND object_id=OBJECT_ID('wallet_liquidaciones_recaudo_comercio_detalle'))
    CREATE INDEX ix_wallet_liquidaciones_recaudo_comercio_detalle_recarga ON wallet_liquidaciones_recaudo_comercio_detalle (id_recarga);

PRINT 'OK: índices de wallet_liquidaciones_recaudo_comercio_detalle verificados/creados';

-- ── 3. Columnas nuevas en wallet_recargas_comercio (creada en 025) ─────

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('wallet_recargas_comercio') AND name='id_liquidacion_recaudo')
BEGIN
    ALTER TABLE wallet_recargas_comercio ADD id_liquidacion_recaudo BIGINT NULL;
    PRINT 'OK: columna id_liquidacion_recaudo agregada a wallet_recargas_comercio';
END
ELSE
    PRINT 'SKIP: columna id_liquidacion_recaudo ya existe';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('wallet_recargas_comercio') AND name='fecha_liquidacion')
BEGIN
    ALTER TABLE wallet_recargas_comercio ADD fecha_liquidacion DATETIME2 NULL;
    PRINT 'OK: columna fecha_liquidacion agregada a wallet_recargas_comercio';
END
ELSE
    PRINT 'SKIP: columna fecha_liquidacion ya existe';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('wallet_recargas_comercio') AND name='liquidado_por_usuario')
BEGIN
    ALTER TABLE wallet_recargas_comercio ADD liquidado_por_usuario BIGINT NULL;
    PRINT 'OK: columna liquidado_por_usuario agregada a wallet_recargas_comercio';
END
ELSE
    PRINT 'SKIP: columna liquidado_por_usuario ya existe';

-- Índice sobre estado + id_liquidacion_recaudo (para el listado de pendientes).
-- Referencia una columna agregada arriba en este mismo batch → SQL dinámico.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wallet_recargas_comercio_estado_liquidacion' AND object_id=OBJECT_ID('wallet_recargas_comercio'))
BEGIN
    EXEC sp_executesql N'
        CREATE INDEX ix_wallet_recargas_comercio_estado_liquidacion
        ON wallet_recargas_comercio (estado, id_liquidacion_recaudo);
    ';
    PRINT 'OK: índice ix_wallet_recargas_comercio_estado_liquidacion creado';
END
ELSE
    PRINT 'SKIP: índice ix_wallet_recargas_comercio_estado_liquidacion ya existe';

-- ── 4. Verificar resultado ──────────────────────────────────────────────
SELECT name AS tabla FROM sys.tables
WHERE name IN ('wallet_liquidaciones_recaudo_comercio', 'wallet_liquidaciones_recaudo_comercio_detalle');

SELECT name AS columna FROM sys.columns
WHERE object_id=OBJECT_ID('wallet_recargas_comercio')
  AND name IN ('id_liquidacion_recaudo', 'fecha_liquidacion', 'liquidado_por_usuario');

PRINT '=== FIN MIGRACIÓN 026 ===';
