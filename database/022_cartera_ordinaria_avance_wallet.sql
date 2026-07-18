-- =====================================================================
-- Migración 022: Cartera Ordinaria — cuenta de ledger para AVANCE_WALLET
-- Idempotente — no toca saldos, ledger, wallets reales, ni producción
-- =====================================================================

PRINT '=== INICIO MIGRACIÓN 022: Cartera Ordinaria — Avance Wallet ===';

IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE codigo='130105' AND id_unidad_negocio=1)
BEGIN
    INSERT INTO ledger_cuentas (
        id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta,
        naturaleza, permite_movimiento, estado, fecha_creacion)
    VALUES (1, '130105', 'Cartera Ordinaria - Avance Wallet',
        'ACTIVO', 'CARTERA', 'D', 1, 'ACTIVA', SYSUTCDATETIME());
    PRINT 'OK: cuenta ledger 130105 (Cartera Ordinaria - Avance Wallet) creada';
END
ELSE
    PRINT 'SKIP: cuenta ledger 130105 ya existe';

SELECT codigo, nombre, tipo_cuenta, naturaleza, estado
FROM ledger_cuentas
WHERE codigo IN ('130105','210101') AND id_unidad_negocio=1;

PRINT '=== FIN MIGRACIÓN 022 ===';
