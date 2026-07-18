-- =====================================================================
-- Migración 023: Cartera Ordinaria — pago manual de cuotas desde Wallet
-- Idempotente — no borra datos, no toca saldos ni recalcula pagos existentes
-- (no existen pagos previos: solo inicializa columnas nuevas a su valor
-- pendiente real, que hoy es el valor total de cada cuota).
-- =====================================================================

PRINT '=== INICIO MIGRACIÓN 023: Cartera Ordinaria — pago manual de cuotas ===';

-- ── 1. Cuentas ledger nuevas ───────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE codigo='410301' AND id_unidad_negocio=1)
BEGIN
    INSERT INTO ledger_cuentas (
        id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta,
        naturaleza, permite_movimiento, estado, fecha_creacion)
    VALUES (1, '410301', 'Ingreso Intereses Cartera Ordinaria',
        'INGRESO', 'CARTERA_ORDINARIA', 'C', 1, 'ACTIVA', SYSUTCDATETIME());
    PRINT 'OK: cuenta ledger 410301 (Ingreso Intereses Cartera Ordinaria) creada';
END
ELSE
    PRINT 'SKIP: cuenta ledger 410301 ya existe';

IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE codigo='410302' AND id_unidad_negocio=1)
BEGIN
    INSERT INTO ledger_cuentas (
        id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta,
        naturaleza, permite_movimiento, estado, fecha_creacion)
    VALUES (1, '410302', 'Ingreso Aval Cartera Ordinaria',
        'INGRESO', 'CARTERA_ORDINARIA', 'C', 1, 'ACTIVA', SYSUTCDATETIME());
    PRINT 'OK: cuenta ledger 410302 (Ingreso Aval Cartera Ordinaria) creada';
END
ELSE
    PRINT 'SKIP: cuenta ledger 410302 ya existe';

IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE codigo='410303' AND id_unidad_negocio=1)
BEGIN
    INSERT INTO ledger_cuentas (
        id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta,
        naturaleza, permite_movimiento, estado, fecha_creacion)
    VALUES (1, '410303', 'Ingreso Administración Cartera Ordinaria',
        'INGRESO', 'CARTERA_ORDINARIA', 'C', 1, 'ACTIVA', SYSUTCDATETIME());
    PRINT 'OK: cuenta ledger 410303 (Ingreso Administración Cartera Ordinaria) creada';
END
ELSE
    PRINT 'SKIP: cuenta ledger 410303 ya existe';

IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE codigo='240803' AND id_unidad_negocio=1)
BEGIN
    INSERT INTO ledger_cuentas (
        id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta,
        naturaleza, permite_movimiento, estado, fecha_creacion)
    VALUES (1, '240803', 'IVA Cartera Ordinaria por Pagar',
        'PASIVO', 'CARTERA_ORDINARIA', 'C', 1, 'ACTIVA', SYSUTCDATETIME());
    PRINT 'OK: cuenta ledger 240803 (IVA Cartera Ordinaria por Pagar) creada';
END
ELSE
    PRINT 'SKIP: cuenta ledger 240803 ya existe';

-- ── 2. cartera_pagos — columnas nuevas ─────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_pagos') AND name='id_transaccion_ledger')
    ALTER TABLE cartera_pagos ADD id_transaccion_ledger BIGINT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_pagos') AND name='saldo_wallet_antes')
    ALTER TABLE cartera_pagos ADD saldo_wallet_antes DECIMAL(18,2) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_pagos') AND name='saldo_wallet_despues')
    ALTER TABLE cartera_pagos ADD saldo_wallet_despues DECIMAL(18,2) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_pagos') AND name='cupo_usado_antes')
    ALTER TABLE cartera_pagos ADD cupo_usado_antes DECIMAL(18,2) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_pagos') AND name='cupo_usado_despues')
    ALTER TABLE cartera_pagos ADD cupo_usado_despues DECIMAL(18,2) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_pagos') AND name='cupo_disponible_antes')
    ALTER TABLE cartera_pagos ADD cupo_disponible_antes DECIMAL(18,2) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_pagos') AND name='cupo_disponible_despues')
    ALTER TABLE cartera_pagos ADD cupo_disponible_despues DECIMAL(18,2) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_pagos') AND name='metodo_pago')
    ALTER TABLE cartera_pagos ADD metodo_pago VARCHAR(20) NOT NULL CONSTRAINT df_cp_metodo_pago DEFAULT 'WALLET';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_pagos') AND name='pin_validado_qa')
    ALTER TABLE cartera_pagos ADD pin_validado_qa BIT NOT NULL CONSTRAINT df_cp_pin_validado DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_pagos') AND name='referencia')
    ALTER TABLE cartera_pagos ADD referencia VARCHAR(100) NULL;

PRINT 'OK: columnas nuevas de cartera_pagos verificadas/creadas';

-- ── 3. cartera_pagos_detalle — columnas nuevas ─────────────────────────

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_pagos_detalle') AND name='valor_aplicado_admin')
    ALTER TABLE cartera_pagos_detalle ADD valor_aplicado_admin DECIMAL(18,2) NOT NULL CONSTRAINT df_cpd_aplicado_admin DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_pagos_detalle') AND name='valor_aplicado_iva')
    ALTER TABLE cartera_pagos_detalle ADD valor_aplicado_iva DECIMAL(18,2) NOT NULL CONSTRAINT df_cpd_aplicado_iva DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_pagos_detalle') AND name='valor_aplicado_gastos_cobranza')
    ALTER TABLE cartera_pagos_detalle ADD valor_aplicado_gastos_cobranza DECIMAL(18,2) NOT NULL CONSTRAINT df_cpd_aplicado_gc DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_pagos_detalle') AND name='valor_aplicado_iva_gastos_cobranza')
    ALTER TABLE cartera_pagos_detalle ADD valor_aplicado_iva_gastos_cobranza DECIMAL(18,2) NOT NULL CONSTRAINT df_cpd_aplicado_iva_gc DEFAULT 0;

PRINT 'OK: columnas nuevas de cartera_pagos_detalle verificadas/creadas';

-- ── 4. cartera_cuotas — columnas nuevas ────────────────────────────────
-- Nota: pagado_capital/interes/aval no fueron pedidas explícitamente en el
-- encargo, pero se agregan para sostener de forma consistente el estado
-- PARCIAL (reanudable) y el DTO de cuotas, que sí las expone. Mismo estilo
-- de "total corriente denormalizado" que wallet_saldos / cartera_cupos_ordinarios.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_cuotas') AND name='pagado_capital')
    ALTER TABLE cartera_cuotas ADD pagado_capital DECIMAL(18,2) NOT NULL CONSTRAINT df_cc_pagado_capital DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_cuotas') AND name='pagado_interes')
    ALTER TABLE cartera_cuotas ADD pagado_interes DECIMAL(18,2) NOT NULL CONSTRAINT df_cc_pagado_interes DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_cuotas') AND name='pagado_aval')
    ALTER TABLE cartera_cuotas ADD pagado_aval DECIMAL(18,2) NOT NULL CONSTRAINT df_cc_pagado_aval DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_cuotas') AND name='pagado_admin')
    ALTER TABLE cartera_cuotas ADD pagado_admin DECIMAL(18,2) NOT NULL CONSTRAINT df_cc_pagado_admin DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_cuotas') AND name='pagado_iva')
    ALTER TABLE cartera_cuotas ADD pagado_iva DECIMAL(18,2) NOT NULL CONSTRAINT df_cc_pagado_iva DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_cuotas') AND name='pagado_gastos_cobranza')
    ALTER TABLE cartera_cuotas ADD pagado_gastos_cobranza DECIMAL(18,2) NOT NULL CONSTRAINT df_cc_pagado_gc DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_cuotas') AND name='pagado_iva_gastos_cobranza')
    ALTER TABLE cartera_cuotas ADD pagado_iva_gastos_cobranza DECIMAL(18,2) NOT NULL CONSTRAINT df_cc_pagado_iva_gc DEFAULT 0;

-- saldo_cuota: se agrega nullable, se respalda al valor total (nada se ha
-- pagado todavía en ninguna cuota existente) y luego se fija NOT NULL.
-- El respaldo y el ALTER COLUMN NOT NULL se guardan en un IF independiente
-- de la existencia de la columna: si una corrida previa se interrumpió justo
-- después del ADD (columna ya existe pero sigue NULL-able), un reintento debe
-- seguir completando el respaldo/NOT NULL en vez de saltarse todo el bloque.
--
-- El UPDATE va en SQL dinámico (sp_executesql) porque SQL Server compila el
-- batch completo antes de ejecutar cualquier sentencia: si el ALTER TABLE ADD
-- y el UPDATE que referencia esa misma columna nueva están en el mismo batch,
-- el UPDATE falla con "Invalid column name" aunque el ADD ya se haya
-- ejecutado línea arriba (fallo real observado al aplicar en Azure SQL QA).
-- El SQL dinámico se compila en su propio contexto, en tiempo de ejecución,
-- así que ya ve la columna agregada por la sentencia anterior.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_cuotas') AND name='saldo_cuota')
BEGIN
    ALTER TABLE cartera_cuotas ADD saldo_cuota DECIMAL(18,2) NULL;
    PRINT 'OK: columna saldo_cuota agregada (nullable)';
END
ELSE
    PRINT 'SKIP: columna saldo_cuota ya existe';

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id=OBJECT_ID('cartera_cuotas') AND name='saldo_cuota' AND is_nullable = 1
)
BEGIN
    EXEC sp_executesql N'
        UPDATE cartera_cuotas
        SET saldo_cuota = valor_total
        WHERE saldo_cuota IS NULL;
    ';

    ALTER TABLE cartera_cuotas ALTER COLUMN saldo_cuota DECIMAL(18,2) NOT NULL;
    PRINT 'OK: saldo_cuota respaldada a valor_total y fijada NOT NULL';
END
ELSE
    PRINT 'SKIP: saldo_cuota ya existe y ya es NOT NULL';

-- fecha_pago ya existe desde la migración 021 — no se toca.

PRINT 'OK: columnas nuevas de cartera_cuotas verificadas/creadas';

-- ── 5. Índices ──────────────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_cartera_cuotas_util_estado_venc' AND object_id=OBJECT_ID('cartera_cuotas'))
    CREATE INDEX ix_cartera_cuotas_util_estado_venc ON cartera_cuotas (id_utilizacion, estado, fecha_vencimiento);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_cartera_pagos_usuario_util_fecha' AND object_id=OBJECT_ID('cartera_pagos'))
    CREATE INDEX ix_cartera_pagos_usuario_util_fecha ON cartera_pagos (id_usuario, id_utilizacion, fecha_pago);

PRINT 'OK: índices verificados/creados';

-- ── 6. Verificar resultado ──────────────────────────────────────────────
SELECT codigo, nombre, tipo_cuenta, naturaleza, estado
FROM ledger_cuentas
WHERE codigo IN ('410301','410302','410303','240803') AND id_unidad_negocio=1;

PRINT '=== FIN MIGRACIÓN 023 ===';
