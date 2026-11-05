-- =====================================================================
-- XPAY PROD - VARIANTE_PROD 019
-- Origen: database/019_iva_parametrizado.sql
-- SHA-256 del origen: 7745dc78a86c8daf191b8a60d87f2b95fda6e35cfc66e3531a49377417573375
-- Lineas conservadas del origen: 1-140
-- Estado en el manifiesto: LISTO
-- Generado por database/prod/tools/construir_paquete.py.
-- NO EDITAR A MANO: modificar la especificacion y regenerar.
-- El contenido despues de la marca es copia exacta de esas lineas.
-- =====================================================================
-- @@FIN_ENCABEZADO_VARIANTE_PROD@@
/* XPAY MVP V1 — 019_iva_parametrizado.sql
   Fase 68.2 — IVA parametrizado en condiciones de comercio aliado y parámetros de liquidación anticipada.

   Cambios:
   1. Agregar porcentaje_iva a comercio_condiciones_negociacion
   2. Agregar id_comercio_aliado, aplica_iva, porcentaje_iva a xpay_parametros_liquidacion_anticipada
   3. Agregar aplica_iva_convenio, porcentaje_iva_convenio, valor_iva_convenio a comercio_ventas_qr_disponibilidad
   4. Cuenta 240802 ya existe (creada en 016 como reserva para futuro — ahora se activa)
   5. Actualizar condición activa Comercio Aliado Demo: aplica_iva=1, porcentaje_iva=19
   6. Crear parámetros específicos para Comercio Aliado Demo (0-60 días): aplica_iva=1, porcentaje_iva=19
   7. Recalcular disponibilidad venta #17 con IVA

   No modifica: ventas ya DISPONIBLE o LIQUIDADA_ANTICIPADA, ledger histórico, saldos, contraseñas, producción.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ═══════════════════════════════════════════════════════════════════
-- 1. porcentaje_iva en comercio_condiciones_negociacion
-- ═══════════════════════════════════════════════════════════════════
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('comercio_condiciones_negociacion') AND name = 'porcentaje_iva'
)
BEGIN
    ALTER TABLE comercio_condiciones_negociacion
        ADD porcentaje_iva DECIMAL(9,4) NULL;
    PRINT 'Columna porcentaje_iva agregada a comercio_condiciones_negociacion';
END
ELSE PRINT 'comercio_condiciones_negociacion.porcentaje_iva ya existe';
GO

-- ═══════════════════════════════════════════════════════════════════
-- 2. xpay_parametros_liquidacion_anticipada: id_comercio_aliado, aplica_iva, porcentaje_iva
-- ═══════════════════════════════════════════════════════════════════

-- 2a. Eliminar unique constraint existente uq_xpla_dias (sólo permite un ACTIVO por días sin distinción de aliado)
IF EXISTS (
    SELECT 1 FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID('xpay_parametros_liquidacion_anticipada')
      AND name = 'uq_xpla_dias'
)
BEGIN
    ALTER TABLE xpay_parametros_liquidacion_anticipada DROP CONSTRAINT uq_xpla_dias;
    PRINT 'Constraint uq_xpla_dias eliminado';
END
ELSE PRINT 'Constraint uq_xpla_dias no encontrado (ok)';
GO

-- 2b. Agregar columnas
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('xpay_parametros_liquidacion_anticipada') AND name = 'id_comercio_aliado'
)
BEGIN
    ALTER TABLE xpay_parametros_liquidacion_anticipada
        ADD id_comercio_aliado BIGINT NULL;
    PRINT 'Columna id_comercio_aliado agregada a xpay_parametros_liquidacion_anticipada';
END
ELSE PRINT 'xpay_parametros_liquidacion_anticipada.id_comercio_aliado ya existe';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('xpay_parametros_liquidacion_anticipada') AND name = 'aplica_iva'
)
BEGIN
    ALTER TABLE xpay_parametros_liquidacion_anticipada
        ADD aplica_iva BIT NOT NULL DEFAULT 0;
    PRINT 'Columna aplica_iva agregada a xpay_parametros_liquidacion_anticipada';
END
ELSE PRINT 'xpay_parametros_liquidacion_anticipada.aplica_iva ya existe';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('xpay_parametros_liquidacion_anticipada') AND name = 'porcentaje_iva'
)
BEGIN
    ALTER TABLE xpay_parametros_liquidacion_anticipada
        ADD porcentaje_iva DECIMAL(9,4) NULL;
    PRINT 'Columna porcentaje_iva agregada a xpay_parametros_liquidacion_anticipada';
END
ELSE PRINT 'xpay_parametros_liquidacion_anticipada.porcentaje_iva ya existe';
GO

-- ═══════════════════════════════════════════════════════════════════
-- 3. comercio_ventas_qr_disponibilidad: columnas IVA convenio
-- ═══════════════════════════════════════════════════════════════════
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('comercio_ventas_qr_disponibilidad') AND name = 'aplica_iva_convenio'
)
BEGIN
    ALTER TABLE comercio_ventas_qr_disponibilidad
        ADD aplica_iva_convenio BIT NOT NULL DEFAULT 0;
    PRINT 'Columna aplica_iva_convenio agregada a comercio_ventas_qr_disponibilidad';
END
ELSE PRINT 'comercio_ventas_qr_disponibilidad.aplica_iva_convenio ya existe';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('comercio_ventas_qr_disponibilidad') AND name = 'porcentaje_iva_convenio'
)
BEGIN
    ALTER TABLE comercio_ventas_qr_disponibilidad
        ADD porcentaje_iva_convenio DECIMAL(9,4) NULL;
    PRINT 'Columna porcentaje_iva_convenio agregada a comercio_ventas_qr_disponibilidad';
END
ELSE PRINT 'comercio_ventas_qr_disponibilidad.porcentaje_iva_convenio ya existe';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('comercio_ventas_qr_disponibilidad') AND name = 'valor_iva_convenio'
)
BEGIN
    ALTER TABLE comercio_ventas_qr_disponibilidad
        ADD valor_iva_convenio DECIMAL(18,2) NOT NULL DEFAULT 0;
    PRINT 'Columna valor_iva_convenio agregada a comercio_ventas_qr_disponibilidad';
END
ELSE PRINT 'comercio_ventas_qr_disponibilidad.valor_iva_convenio ya existe';
GO

-- ═══════════════════════════════════════════════════════════════════
-- 4. Cuenta 240802 ya existe (016 la creó). Verificar.
-- ═══════════════════════════════════════════════════════════════════
IF EXISTS (SELECT 1 FROM ledger_cuentas WHERE codigo = '240802' AND id_unidad_negocio = 1 AND estado = 'ACTIVA')
    PRINT '240802 IVA Descuento Comercio — activa (ok)';
ELSE
BEGIN
    INSERT INTO ledger_cuentas
        (id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta, naturaleza, permite_movimiento, estado, fecha_creacion)
    VALUES
        (1, '240802', 'IVA Descuento Comercio', 'PASIVO', 'IVA', 'C', 1, 'ACTIVA', SYSUTCDATETIME());
    PRINT '240802 IVA Descuento Comercio — creada';
END
GO
