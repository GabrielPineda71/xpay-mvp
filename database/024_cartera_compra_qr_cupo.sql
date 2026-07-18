-- =====================================================================
-- Migración 024: Cartera Ordinaria — compra QR con Cupo Ordinario
-- Idempotente — no borra datos, no toca saldos, no recalcula ventas
-- existentes. Ambas columnas nuevas quedan NULL para filas existentes
-- (ninguna venta/utilización previa fue financiada por cupo).
-- =====================================================================

PRINT '=== INICIO MIGRACIÓN 024: Cartera Ordinaria — compra QR con cupo ===';

-- ── 1. Cuenta ledger nueva ──────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE codigo='130106' AND id_unidad_negocio=1)
BEGIN
    INSERT INTO ledger_cuentas (
        id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta,
        naturaleza, permite_movimiento, estado, fecha_creacion)
    VALUES (1, '130106', 'Cartera Ordinaria - Compra Comercio',
        'ACTIVO', 'CARTERA_ORDINARIA', 'D', 1, 'ACTIVA', SYSUTCDATETIME());
    PRINT 'OK: cuenta ledger 130106 (Cartera Ordinaria - Compra Comercio) creada';
END
ELSE
    PRINT 'SKIP: cuenta ledger 130106 ya existe';

-- ── 2. cartera_utilizaciones — columna nueva ────────────────────────────
-- id_comercio_aliado ya existe desde la migración 021 — no se toca.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('cartera_utilizaciones') AND name='id_venta_qr')
    ALTER TABLE cartera_utilizaciones ADD id_venta_qr BIGINT NULL;

PRINT 'OK: columna id_venta_qr de cartera_utilizaciones verificada/creada';

-- ── 3. ventas_qr — columna nueva ────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('ventas_qr') AND name='id_utilizacion_cartera')
    ALTER TABLE ventas_qr ADD id_utilizacion_cartera BIGINT NULL;

PRINT 'OK: columna id_utilizacion_cartera de ventas_qr verificada/creada';

-- ── 4. Verificar resultado ──────────────────────────────────────────────
SELECT codigo, nombre, tipo_cuenta, naturaleza, estado
FROM ledger_cuentas
WHERE codigo = '130106' AND id_unidad_negocio=1;

PRINT '=== FIN MIGRACIÓN 024 ===';
