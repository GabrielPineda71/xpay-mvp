/* XPAY MVP V1 — 018_backfill_disponibilidad_venta17.sql
   Backfill idempotente de comercio_ventas_qr_disponibilidad y comercio_ventas_qr_contexto
   para ventas QR en CONTINGENCIA de comercios aliados que no tienen registro de disponibilidad.

   Corrige venta #17 (y cualquier otra en la misma situación).
   Condición activa del Comercio Aliado 1: dias=30, pct=6%.

   No modifica wallets, ledger ni ventas ya liquidadas.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ── Backfill disponibilidad para ventas CONTINGENCIA sin entrada en disponibilidad ──
INSERT INTO comercio_ventas_qr_disponibilidad
    (id_venta_qr, id_comercio_aliado, id_comercio_existente, id_wallet_comercio,
     valor_bruto, dias_disponibilidad, porcentaje_descuento,
     valor_descuento, valor_neto_programado,
     fecha_venta, fecha_disponible_programada, estado, created_at)
SELECT
    v.id_venta_qr,
    ca.id_comercio_aliado,
    c.id_comercio                                                                AS id_comercio_existente,
    c.id_wallet_comercio,
    v.valor_bruto,
    cn.dias_disponibilidad,
    cn.porcentaje_descuento,
    ROUND(v.valor_bruto * cn.porcentaje_descuento / 100.0, 2)                   AS valor_descuento,
    v.valor_bruto - ROUND(v.valor_bruto * cn.porcentaje_descuento / 100.0, 2)   AS valor_neto_programado,
    v.fecha_venta,
    DATEADD(DAY, cn.dias_disponibilidad, v.fecha_venta)                         AS fecha_disponible_programada,
    'NO_DISPONIBLE',
    SYSUTCDATETIME()
FROM ventas_qr v
JOIN comercios c                       ON c.id_comercio             = v.id_comercio
JOIN comercios_aliados ca              ON ca.id_comercio_existente  = c.id_comercio
                                      AND ca.estado                  = 'ACTIVO'
JOIN comercio_condiciones_negociacion cn ON cn.id_comercio_aliado   = ca.id_comercio_aliado
                                        AND cn.estado                = 'ACTIVO'
WHERE v.estado = 'CONTINGENCIA'
  AND c.id_wallet_comercio IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM comercio_ventas_qr_disponibilidad d
      WHERE d.id_venta_qr = v.id_venta_qr
  );
GO
PRINT 'Backfill disponibilidad: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' filas insertadas.';
GO

-- ── Backfill contexto para ventas que tienen disponibilidad pero no contexto ──
INSERT INTO comercio_ventas_qr_contexto
    (id_venta_qr, id_comercio_aliado, id_comercio_existente,
     id_establecimiento, id_cajero_usuario, created_at)
SELECT
    d.id_venta_qr,
    d.id_comercio_aliado,
    d.id_comercio_existente,
    NULL,  -- sin sede específica en backfill
    NULL,  -- sin cajero específico en backfill
    SYSUTCDATETIME()
FROM comercio_ventas_qr_disponibilidad d
WHERE NOT EXISTS (
    SELECT 1 FROM comercio_ventas_qr_contexto ctx
    WHERE ctx.id_venta_qr = d.id_venta_qr
);
GO
PRINT 'Backfill contexto: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' filas insertadas.';
GO

-- ── Verificación ──
SELECT d.id_disponibilidad, d.id_venta_qr, d.estado,
       d.valor_bruto, d.valor_descuento, d.valor_neto_programado,
       d.fecha_venta, d.fecha_disponible_programada,
       ctx.id_contexto
FROM comercio_ventas_qr_disponibilidad d
LEFT JOIN comercio_ventas_qr_contexto ctx ON ctx.id_venta_qr = d.id_venta_qr
WHERE d.id_venta_qr = 17;
GO
