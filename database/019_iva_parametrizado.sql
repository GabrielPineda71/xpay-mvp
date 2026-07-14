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

-- ═══════════════════════════════════════════════════════════════════
-- 5. Condición activa Comercio Aliado Demo: aplica_iva=1, porcentaje_iva=19
-- ═══════════════════════════════════════════════════════════════════
UPDATE comercio_condiciones_negociacion
SET    aplica_iva    = 1,
       porcentaje_iva = 19.0000,
       updated_at   = SYSUTCDATETIME()
WHERE  id_comercio_aliado = 1
  AND  estado = 'ACTIVO';

PRINT 'Condición activa Comercio Aliado Demo: aplica_iva=1, porcentaje_iva=19 — ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' fila(s)';
GO

-- ═══════════════════════════════════════════════════════════════════
-- 6. Parámetros específicos Comercio Aliado Demo (id_comercio_aliado=1), días 0-60
--    aplica_iva=1, porcentaje_iva=19, porcentaje_descuento = dia * 0.5 (igual que globales)
-- ═══════════════════════════════════════════════════════════════════
DECLARE @d INT = 0;
WHILE @d <= 60
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM xpay_parametros_liquidacion_anticipada
        WHERE  id_comercio_aliado = 1
          AND  dias_faltantes     = @d
          AND  estado             = 'ACTIVO'
    )
    BEGIN
        INSERT INTO xpay_parametros_liquidacion_anticipada
            (id_comercio_aliado, dias_faltantes, porcentaje_descuento, aplica_iva, porcentaje_iva, estado, created_by_usuario, created_at)
        VALUES
            (1, @d, CAST(@d AS DECIMAL(9,4)) * 0.5000, 1, 19.0000, 'ACTIVO', 1, SYSUTCDATETIME());
    END
    SET @d = @d + 1;
END
PRINT 'Parámetros específicos Comercio Aliado Demo creados/verificados (0-60 días)';
GO

-- ═══════════════════════════════════════════════════════════════════
-- 7. Recalcular disponibilidad venta #17 (NO_DISPONIBLE) con IVA convenio
--    Solo ventas NO_DISPONIBLE. No toca DISPONIBLE ni LIQUIDADA_ANTICIPADA.
-- ═══════════════════════════════════════════════════════════════════
UPDATE comercio_ventas_qr_disponibilidad
SET    aplica_iva_convenio    = 1,
       porcentaje_iva_convenio = 19.0000,
       valor_iva_convenio      = ROUND(valor_descuento * 19.0000 / 100.0, 2),
       valor_neto_programado   = valor_bruto
                               - valor_descuento
                               - ROUND(valor_descuento * 19.0000 / 100.0, 2),
       updated_at             = SYSUTCDATETIME()
WHERE  estado = 'NO_DISPONIBLE'
  AND  id_comercio_aliado = 1;

PRINT 'Recalculadas disponibilidades NO_DISPONIBLE del Comercio Aliado Demo: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + ' fila(s)';
GO

-- ═══════════════════════════════════════════════════════════════════
-- Verificación final
-- ═══════════════════════════════════════════════════════════════════
SELECT 'condicion_activa'           AS tipo,
       id_condicion, id_comercio_aliado,
       dias_disponibilidad, porcentaje_descuento,
       CAST(aplica_iva AS INT)      AS aplica_iva,
       porcentaje_iva
FROM   comercio_condiciones_negociacion
WHERE  id_comercio_aliado = 1 AND estado = 'ACTIVO';

SELECT 'parametros_ca1_muestra'     AS tipo,
       id_parametro, id_comercio_aliado, dias_faltantes,
       porcentaje_descuento,
       CAST(aplica_iva AS INT)      AS aplica_iva,
       porcentaje_iva
FROM   xpay_parametros_liquidacion_anticipada
WHERE  id_comercio_aliado = 1
ORDER  BY dias_faltantes;

SELECT 'disponibilidad_venta17'     AS tipo,
       id_disponibilidad, id_venta_qr, estado,
       valor_bruto, valor_descuento,
       CAST(aplica_iva_convenio AS INT) AS aplica_iva_convenio,
       porcentaje_iva_convenio,
       valor_iva_convenio,
       valor_neto_programado
FROM   comercio_ventas_qr_disponibilidad
WHERE  id_venta_qr = 17;
GO
