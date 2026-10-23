-- =====================================================================
-- XPAY PROD - PENDIENTE_DATOS 021b
-- Origen: database/021_cartera_ordinaria.sql
-- SHA-256 del origen: 53d06557e0be23d06f93c9a882832090162384f7649fa0d11595078f662931f4
-- Lineas conservadas del origen: 207-245
-- Estado en el manifiesto: BLOQUEADO_DECISION_NEGOCIO
-- Generado por database/prod/tools/construir_paquete.py.
-- NO EDITAR A MANO: modificar la especificacion y regenerar.
-- El contenido despues de la marca es copia exacta de esas lineas.
-- =====================================================================
-- @@FIN_ENCABEZADO_VARIANTE_PROD@@
-- ── 9. Seed parámetros de utilización ─────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM cartera_parametros_utilizacion WHERE tipo_utilizacion='COMPRA_COMERCIO')
BEGIN
    INSERT INTO cartera_parametros_utilizacion
        (tipo_utilizacion, tasa_emv, porc_aval, porc_admin, aplica_iva, porc_iva, plazo_min, plazo_max, frecuencia, monto_min, monto_max, estado)
    VALUES
        ('COMPRA_COMERCIO', 2.5000, 1.2000, 0.8000, 1, 19.0000, 1, 24, 'MENSUAL',   50000, 3000000, 'ACTIVO'),
        ('AVANCE_WALLET',   3.0000, 1.5000, 1.0000, 1, 19.0000, 1, 12, 'MENSUAL',  100000, 1000000, 'ACTIVO');
    PRINT 'OK: parámetros de utilización seed insertados';
END
ELSE
    PRINT 'SKIP: parámetros de utilización ya existen';

-- ── 10. Seed gastos de cobranza ───────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM cartera_parametros_gastos_cobranza WHERE dias_desde=0)
BEGIN
    INSERT INTO cartera_parametros_gastos_cobranza
        (dias_desde, dias_hasta, tipo_cobro, valor_cobro, descripcion, estado)
    VALUES
        (0,  30, 'PORCENTAJE', 0.5000, 'Gestión preventiva 0-30 días',  'ACTIVO'),
        (31, 60, 'PORCENTAJE', 1.5000, 'Gestión temprana 31-60 días',   'ACTIVO'),
        (61, 90, 'PORCENTAJE', 3.0000, 'Gestión media 61-90 días',      'ACTIVO'),
        (91, NULL,'FIJO',     50000,   'Cobro jurídico >90 días',       'ACTIVO');
    PRINT 'OK: gastos de cobranza seed insertados';
END
ELSE
    PRINT 'SKIP: gastos de cobranza ya existen';

-- ── 11. Seed política de crédito ──────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM cartera_politicas_credito WHERE estado='ACTIVO')
BEGIN
    INSERT INTO cartera_politicas_credito
        (score_datacredito_minimo, requiere_veriff, cupo_minimo, cupo_maximo, edad_minima, edad_maxima, estado, vigente_desde)
    VALUES
        (NULL, 0, 100000, 5000000, 18, 70, 'ACTIVO', GETUTCDATE());
    PRINT 'OK: política de crédito seed insertada';
END
ELSE
    PRINT 'SKIP: política de crédito ya existe';
