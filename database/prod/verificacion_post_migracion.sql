-- verificacion_post_migracion.sql
-- XPAY PROD - verificacion posterior a las migraciones.
-- SOLO LECTURA: unicamente SELECT. No forma parte del manifiesto de migraciones.
-- Cada consulta indica el resultado esperado. Cualquier fila en una consulta
-- de "faltantes" o un conteo distinto del esperado es una desviacion a revisar.
SET NOCOUNT ON;

-- R1. Unidad de negocio exigida por el backend (se espera 1)
SELECT COUNT(*) AS xpay_col_activa
FROM dbo.unidades_negocio
WHERE codigo = 'XPAY_COL' AND estado = 'ACTIVA';

-- R2. Tablas que el backend necesita y no existen (se espera ninguna fila)
SELECT t.nombre AS tabla_faltante
FROM (VALUES
  ('unidades_negocio'), ('auditoria'), ('personas'), ('usuarios'), ('roles'),
  ('usuario_roles'), ('usuario_tokens'), ('kyc_verificaciones'),
  ('wallets'), ('wallet_saldos'), ('wallet_movimientos'),
  ('ledger_cuentas'), ('ledger_transacciones'), ('ledger_movimientos'),
  ('cuentas_operativas'), ('comercios'), ('comercio_tiendas'),
  ('qr_comercios'), ('ventas_qr'), ('liquidaciones_comercio'),
  ('liquidacion_comercio_detalle'), ('retiros_comercio'),
  ('passport_breb_llaves'), ('passport_breb_retiros'),
  ('passport_webhook_events'), ('libranza_empresas_convenio'),
  ('libranza_parametros_empresa'), ('libranza_rangos_cobro'),
  ('libranza_usuarios_empresa'), ('libranza_empleados'),
  ('libranza_importaciones_empleados'), ('libranza_empleado_cortes_pago'),
  ('libranza_empleado_usuario'), ('libranza_anticipos'),
  ('libranza_lotes_recaudo'), ('libranza_lote_recaudo_detalles'),
  ('comercios_aliados'), ('comercio_representantes_legales'),
  ('comercio_establecimientos'), ('comercio_usuarios_solicitados'),
  ('comercio_documentos'), ('comercio_condiciones_negociacion'),
  ('xpay_parametros_liquidacion_anticipada'),
  ('comercio_ventas_qr_disponibilidad'), ('comercio_usuarios'),
  ('comercio_ventas_qr_contexto'), ('cartera_parametros_utilizacion'),
  ('cartera_parametros_gastos_cobranza'), ('cartera_politicas_credito'),
  ('cartera_cupos_ordinarios'), ('cartera_utilizaciones'),
  ('cartera_cuotas'), ('cartera_pagos'), ('cartera_pagos_detalle'),
  ('cartera_solicitudes_cupo'), ('cartera_solicitud_cupo_intentos'),
  ('cartera_solicitud_cupo_motivos_decision'),
  ('cartera_autorizacion_consulta_riesgo'),
  ('cartera_solicitud_cupo_revisiones'), ('wallet_recargas_comercio'),
  ('wallet_liquidaciones_recaudo_comercio'),
  ('wallet_liquidaciones_recaudo_comercio_detalle'),
  ('wallet_cierres_diarios_comercio'),
  ('wallet_cierres_diarios_comercio_detalle'),
  ('wallet_cajas_comercio'), ('wallet_caja_movimientos'),
  ('wallet_idempotencia'), ('catalogo_paises'),
  ('catalogo_departamentos'), ('catalogo_ciudades')
) AS t(nombre)
WHERE OBJECT_ID(N'dbo.' + t.nombre, N'U') IS NULL;

-- R3. Roles exigidos por el backend que faltan (se espera ninguna fila)
SELECT r.codigo AS rol_faltante
FROM (VALUES
  ('USUARIO_FINAL'), ('SUPERUSUARIO'), ('ADMIN_XPAY'), ('OPERADOR_XPAY'),
  ('COMERCIO'), ('CARTERA_XPAY'), ('COMERCIAL_XPAY'), ('GERENTE_XPAY')
) AS r(codigo)
WHERE NOT EXISTS (SELECT 1 FROM dbo.roles x WHERE x.codigo = r.codigo);

-- R4. Cuentas contables exigidas por el backend que faltan (se espera ninguna fila)
SELECT c.codigo AS cuenta_faltante
FROM (VALUES
  ('110101'), ('110102'), ('110103'), ('110202'), ('130104'), ('130105'),
  ('130106'), ('130107'), ('210101'), ('210201'), ('210202'), ('210203'),
  ('210204'), ('230103'), ('240802'), ('240803'), ('310103'), ('410201'),
  ('410202'), ('410301'), ('410302'), ('410303')
) AS c(codigo)
WHERE NOT EXISTS (
  SELECT 1 FROM dbo.ledger_cuentas l
  JOIN dbo.unidades_negocio u ON u.id_unidad_negocio = l.id_unidad_negocio
  WHERE u.codigo = 'XPAY_COL' AND l.codigo = c.codigo);

-- R5. Estado de esas cuentas (informativo: se espera ACTIVA en todas)
SELECT l.codigo, l.nombre, l.estado
FROM dbo.ledger_cuentas l
WHERE l.codigo IN ('110101','110102','110103','110202','130104','130105',
  '130106','130107','210101','210201','210202','210203','210204','230103',
  '240802','240803','310103','410201','410202','410301','410302','410303')
ORDER BY l.codigo;

-- R6. Ausencia de datos de negocio, demo o QA (se esperan todos 0)
SELECT
  (SELECT COUNT(*) FROM dbo.personas)                       AS personas,
  (SELECT COUNT(*) FROM dbo.usuarios)                       AS usuarios,
  (SELECT COUNT(*) FROM dbo.usuario_roles)                  AS usuario_roles,
  (SELECT COUNT(*) FROM dbo.wallets)                        AS wallets,
  (SELECT COUNT(*) FROM dbo.wallet_saldos)                  AS wallet_saldos,
  (SELECT COUNT(*) FROM dbo.ledger_transacciones)           AS ledger_transacciones,
  (SELECT COUNT(*) FROM dbo.comercios)                      AS comercios,
  (SELECT COUNT(*) FROM dbo.comercio_tiendas)               AS comercio_tiendas,
  (SELECT COUNT(*) FROM dbo.qr_comercios)                   AS qr_comercios,
  (SELECT COUNT(*) FROM dbo.comercios_aliados)              AS comercios_aliados,
  (SELECT COUNT(*) FROM dbo.comercio_usuarios)              AS comercio_usuarios,
  (SELECT COUNT(*) FROM dbo.comercio_condiciones_negociacion) AS condiciones_negociacion,
  (SELECT COUNT(*) FROM dbo.libranza_empresas_convenio)     AS libranza_convenios,
  (SELECT COUNT(*) FROM dbo.libranza_usuarios_empresa)      AS libranza_usuarios_empresa,
  (SELECT COUNT(*) FROM dbo.libranza_empleados)             AS libranza_empleados,
  (SELECT COUNT(*) FROM dbo.libranza_empleado_usuario)      AS libranza_empleado_usuario,
  (SELECT COUNT(*) FROM dbo.cartera_cupos_ordinarios)       AS cartera_cupos,
  (SELECT COUNT(*) FROM dbo.cuentas_operativas)             AS cuentas_operativas;

-- R7. Parametros comerciales pendientes de decision
--     (se esperan 0 mientras 016b, 021b y 045b sigan bloqueados)
SELECT
  (SELECT COUNT(*) FROM dbo.xpay_parametros_liquidacion_anticipada) AS parametros_liquidacion,
  (SELECT COUNT(*) FROM dbo.cartera_parametros_utilizacion)         AS parametros_utilizacion,
  (SELECT COUNT(*) FROM dbo.cartera_parametros_gastos_cobranza)     AS gastos_cobranza,
  (SELECT COUNT(*) FROM dbo.cartera_politicas_credito)              AS politicas_credito;

-- R8. Catalogo geografico DANE (se espera 1 / 33 / 1122)
SELECT
  (SELECT COUNT(*) FROM dbo.catalogo_paises)        AS paises,
  (SELECT COUNT(*) FROM dbo.catalogo_departamentos) AS departamentos,
  (SELECT COUNT(*) FROM dbo.catalogo_ciudades)      AS ciudades;

-- R9. Restricciones de periodicidad de libranza (013 y 014).
--     Se espera que ambas incluyan SEMANAL.
SELECT name, definition
FROM sys.check_constraints
WHERE name IN ('ck_lec_periodicidad', 'ck_le_periodicidad', 'ck_lecp_corte');

-- R10. Restricciones unicas de xpay_parametros_liquidacion_anticipada (019).
--      Se espera que uq_xpla_dias NO exista (019 la elimina sin sustituta).
SELECT kc.name AS restriccion_unica
FROM sys.key_constraints kc
WHERE kc.parent_object_id = OBJECT_ID(N'dbo.xpay_parametros_liquidacion_anticipada')
  AND kc.type = 'UQ';

-- R11. Objetos de base de datos inesperados (se espera ninguna fila)
SELECT o.name, o.type_desc
FROM sys.objects o
WHERE o.is_ms_shipped = 0
  AND o.type IN ('P', 'V', 'TR', 'FN', 'IF', 'TF');
