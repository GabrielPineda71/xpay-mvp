-- =====================================================================
-- Migración 020: Habilitar anticipos múltiples por período en libranza
-- Idempotente — aplica solo sobre convenio demo (id_convenio = 1)
-- NO cambia contraseñas, NO toca producción, NO borra anticipos existentes
-- =====================================================================

PRINT '=== INICIO MIGRACIÓN 020: Libranza anticipo múltiple ===';

-- ── 1. Habilitar permite_anticipo_multiple para convenio demo ──────────────
IF EXISTS (SELECT 1 FROM libranza_parametros_empresa WHERE id_convenio = 1)
BEGIN
    UPDATE libranza_parametros_empresa
    SET permite_anticipo_multiple = 1,
        max_anticipos_activos     = 99,
        updated_at                = GETUTCDATE()
    WHERE id_convenio = 1;

    PRINT 'OK: libranza_parametros_empresa convenio #1 actualizado → permite_anticipo_multiple=1, max_anticipos_activos=99';
END
ELSE
BEGIN
    PRINT 'WARN: No se encontró parámetro para id_convenio=1 — nada que actualizar.';
END

-- ── 2. Verificar resultado ─────────────────────────────────────────────────
SELECT
    id_parametro,
    id_convenio,
    porcentaje_maximo_cupo,
    permite_anticipo_multiple,
    max_anticipos_activos,
    momento_cobro_comision,
    estado
FROM libranza_parametros_empresa
WHERE id_convenio = 1;

PRINT '=== FIN MIGRACIÓN 020 ===';
