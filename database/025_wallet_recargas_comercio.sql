-- =====================================================================
-- Migración 025: Recarga de Wallet en efectivo por Cajero de Comercio
-- Idempotente — no borra datos, no toca saldos ni tablas existentes.
--
-- Tabla 100% nueva (wallet_recargas_comercio) — nunca tocada por el
-- pipeline de CI (que solo aplica migraciones 001-010 sobre una base
-- nueva). La única cuenta ledger nueva se agrega como fila de datos en
-- ledger_cuentas (sin cambiar columnas ni el modelo EF de esa tabla),
-- así que no afecta ninguna consulta existente probada en CI. Lección
-- aplicada de Fase 69.4A: nunca agregar columnas nuevas a tablas que
-- el baseline de CI ya usa (wallets, wallet_saldos, wallet_movimientos,
-- ledger_cuentas, ledger_transacciones, ledger_movimientos, usuarios,
-- personas).
-- =====================================================================

PRINT '=== INICIO MIGRACIÓN 025: Recarga de Wallet en efectivo por Cajero de Comercio ===';

-- ── 1. Cuenta ledger nueva ──────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE codigo='130107' AND id_unidad_negocio=1)
BEGIN
    INSERT INTO ledger_cuentas (
        id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta,
        naturaleza, permite_movimiento, estado, fecha_creacion)
    VALUES (1, '130107', 'Efectivo por Recaudar en Comercios',
        'ACTIVO', 'RECAUDO_COMERCIO', 'D', 1, 'ACTIVA', SYSUTCDATETIME());
    PRINT 'OK: cuenta ledger 130107 (Efectivo por Recaudar en Comercios) creada';
END
ELSE
    PRINT 'SKIP: cuenta ledger 130107 ya existe';

-- ── 2. Tabla wallet_recargas_comercio ───────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='wallet_recargas_comercio')
BEGIN
    CREATE TABLE wallet_recargas_comercio (
        id_recarga              BIGINT          IDENTITY(1,1) PRIMARY KEY,
        id_unidad_negocio       BIGINT          NOT NULL DEFAULT 1,
        id_comercio             BIGINT          NOT NULL,
        id_comercio_aliado      BIGINT          NULL,
        id_tienda               BIGINT          NULL,
        id_usuario_cajero       BIGINT          NOT NULL,
        id_usuario_wallet       BIGINT          NOT NULL,
        id_wallet               BIGINT          NOT NULL,
        id_transaccion_ledger   BIGINT          NULL,
        valor                   DECIMAL(18,2)   NOT NULL,
        estado                  VARCHAR(30)     NOT NULL DEFAULT 'APLICADA',
        metodo_recaudo          VARCHAR(30)     NOT NULL DEFAULT 'EFECTIVO',
        referencia              VARCHAR(100)    NULL,
        pin_validado_qa         BIT             NOT NULL DEFAULT 0,
        saldo_wallet_antes      DECIMAL(18,2)   NOT NULL,
        saldo_wallet_despues    DECIMAL(18,2)   NOT NULL,
        observaciones           VARCHAR(500)    NULL,
        fecha_recarga           DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
        created_at              DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
    PRINT 'OK: tabla wallet_recargas_comercio creada';
END
ELSE
    PRINT 'SKIP: tabla wallet_recargas_comercio ya existe';

-- ── 3. Índices ──────────────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wallet_recargas_comercio_comercio_fecha' AND object_id=OBJECT_ID('wallet_recargas_comercio'))
    CREATE INDEX ix_wallet_recargas_comercio_comercio_fecha ON wallet_recargas_comercio (id_comercio, fecha_recarga);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wallet_recargas_comercio_cajero_fecha' AND object_id=OBJECT_ID('wallet_recargas_comercio'))
    CREATE INDEX ix_wallet_recargas_comercio_cajero_fecha ON wallet_recargas_comercio (id_usuario_cajero, fecha_recarga);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wallet_recargas_comercio_wallet_fecha' AND object_id=OBJECT_ID('wallet_recargas_comercio'))
    CREATE INDEX ix_wallet_recargas_comercio_wallet_fecha ON wallet_recargas_comercio (id_wallet, fecha_recarga);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wallet_recargas_comercio_ledger' AND object_id=OBJECT_ID('wallet_recargas_comercio'))
    CREATE INDEX ix_wallet_recargas_comercio_ledger ON wallet_recargas_comercio (id_transaccion_ledger);

PRINT 'OK: índices verificados/creados';

-- ── 4. Verificar resultado ──────────────────────────────────────────────
SELECT codigo, nombre, tipo_cuenta, naturaleza, estado
FROM ledger_cuentas
WHERE codigo = '130107' AND id_unidad_negocio=1;

PRINT '=== FIN MIGRACIÓN 025 ===';
