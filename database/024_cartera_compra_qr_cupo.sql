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

-- ── 3. (retirado) ventas_qr.id_utilizacion_cartera ─────────────────────
-- Se planeó como referencia inversa venta→utilización, pero el código final
-- no la necesita (basta con cartera_utilizaciones.id_venta_qr) y mapearla en
-- EF Core rompía CI: el pipeline de CI solo aplica migraciones 001-010 sobre
-- una base nueva, así que cualquier columna nueva mapeada en el modelo
-- VentaQr (una tabla que sí toca el flujo QR normal ya probado en CI) hace
-- fallar toda consulta EF sobre esa tabla, incluido /api/qr/pagar. Se
-- retiró del modelo C# antes de este commit. Si ya aplicaste la versión
-- anterior de este script en QA, la columna queda como campo inerte sin
-- uso — no hace falta eliminarla.

-- ── 4. Verificar resultado ──────────────────────────────────────────────
SELECT codigo, nombre, tipo_cuenta, naturaleza, estado
FROM ledger_cuentas
WHERE codigo = '130106' AND id_unidad_negocio=1;

PRINT '=== FIN MIGRACIÓN 024 ===';
