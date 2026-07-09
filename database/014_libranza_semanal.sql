-- ============================================================
-- Fase 66.4 — Libranza: SEMANAL + dia_pago_4 + demo semanal
-- Idempotente — ejecutar con sqlcmd
-- ============================================================

SET QUOTED_IDENTIFIER ON;
GO

-- 0. Ampliar ck_lecp_corte para permitir 4 cortes (SEMANAL)
SET QUOTED_IDENTIFIER ON;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_lecp_corte'
           AND definition LIKE '%<=(3)%')
BEGIN
    ALTER TABLE libranza_empleado_cortes_pago DROP CONSTRAINT ck_lecp_corte;
    ALTER TABLE libranza_empleado_cortes_pago
        ADD CONSTRAINT ck_lecp_corte CHECK (numero_corte >= 1 AND numero_corte <= 4);
    PRINT 'ck_lecp_corte ampliado a 1-4';
END
ELSE
    PRINT 'ck_lecp_corte ya permite hasta 4 cortes';
GO

-- 1. dia_pago_4 en libranza_empresas_convenio
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('libranza_empresas_convenio') AND name = 'dia_pago_4'
)
    ALTER TABLE libranza_empresas_convenio ADD dia_pago_4 INT NULL;
GO

-- 2. dia_pago_4 en libranza_empleados
SET QUOTED_IDENTIFIER ON;
GO
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('libranza_empleados') AND name = 'dia_pago_4'
)
    ALTER TABLE libranza_empleados ADD dia_pago_4 INT NULL;
GO

-- 3. Ampliar check constraint periodicidad en libranza_empresas_convenio
SET QUOTED_IDENTIFIER ON;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_lec_periodicidad')
    ALTER TABLE libranza_empresas_convenio DROP CONSTRAINT ck_lec_periodicidad;
GO
SET QUOTED_IDENTIFIER ON;
GO
ALTER TABLE libranza_empresas_convenio
    ADD CONSTRAINT ck_lec_periodicidad
    CHECK (periodicidad_pago IN ('MENSUAL','QUINCENAL','DECADAL','SEMANAL'));
GO

-- 4. Ampliar check constraint periodicidad en libranza_empleados
SET QUOTED_IDENTIFIER ON;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_le_periodicidad')
    ALTER TABLE libranza_empleados DROP CONSTRAINT ck_le_periodicidad;
GO
SET QUOTED_IDENTIFIER ON;
GO
ALTER TABLE libranza_empleados
    ADD CONSTRAINT ck_le_periodicidad
    CHECK (periodicidad_pago IN ('MENSUAL','QUINCENAL','DECADAL','SEMANAL'));
GO

-- 5. Insertar convenio SEMANAL (idempotente)
SET QUOTED_IDENTIFIER ON;
GO
IF NOT EXISTS (SELECT 1 FROM libranza_empresas_convenio WHERE nit = '901888888-1')
BEGIN
    INSERT INTO libranza_empresas_convenio (
        nombre_empresa, nit, representante_legal, email_contacto,
        estado, periodicidad_pago,
        dia_pago_1, dia_pago_2, dia_pago_3, dia_pago_4,
        permite_anticipo_dia_pago, porcentaje_maximo_cupo,
        fecha_inicio, created_at
    )
    VALUES (
        'Empresa Semanal Demo XPAY', '901888888-1', NULL, NULL,
        'ACTIVO', 'SEMANAL',
        7, 14, 21, 28,
        0, 30.00,
        GETUTCDATE(), GETUTCDATE()
    );
    PRINT 'Convenio SEMANAL creado';
END
ELSE
    PRINT 'Convenio SEMANAL ya existe';
GO

-- 6. Parametros para convenio SEMANAL
SET QUOTED_IDENTIFIER ON;
GO
DECLARE @id_conv_sem BIGINT;
SELECT @id_conv_sem = id_convenio FROM libranza_empresas_convenio WHERE nit = '901888888-1';

IF NOT EXISTS (SELECT 1 FROM libranza_parametros_empresa WHERE id_convenio = @id_conv_sem AND estado = 'ACTIVO')
BEGIN
    INSERT INTO libranza_parametros_empresa (
        id_convenio, porcentaje_maximo_cupo,
        requiere_validacion_empresa, permite_anticipo_multiple, max_anticipos_activos,
        iva_porcentaje, momento_cobro_comision, estado, created_at
    )
    VALUES (
        @id_conv_sem, 30.00,
        1, 0, 1,
        19.00, 'VENCIDO', 'ACTIVO', GETUTCDATE()
    );
    PRINT 'Parametros SEMANAL creados';
END
ELSE
    PRINT 'Parametros SEMANAL ya existen';
GO

-- 7. Rangos de cobro para convenio SEMANAL
SET QUOTED_IDENTIFIER ON;
GO
DECLARE @id_conv_sem2 BIGINT;
SELECT @id_conv_sem2 = id_convenio FROM libranza_empresas_convenio WHERE nit = '901888888-1';

IF NOT EXISTS (SELECT 1 FROM libranza_rangos_cobro WHERE id_convenio = @id_conv_sem2 AND estado = 'ACTIVO')
BEGIN
    INSERT INTO libranza_rangos_cobro (id_convenio, valor_desde, valor_hasta, tipo_cobro, valor_cobro, aplica_iva, estado, created_at)
    VALUES
        (@id_conv_sem2, 50000.00, 200000.00, 'FIJO', 5000.00, 1, 'ACTIVO', GETUTCDATE()),
        (@id_conv_sem2, 200001.00, 500000.00, 'FIJO', 9000.00, 1, 'ACTIVO', GETUTCDATE()),
        (@id_conv_sem2, 500001.00, 1000000.00, 'FIJO', 15000.00, 1, 'ACTIVO', GETUTCDATE());
    PRINT 'Rangos SEMANAL creados';
END
ELSE
    PRINT 'Rangos SEMANAL ya existen';
GO

-- 8. Asociar qa.empresa1 (id=12) al convenio SEMANAL
SET QUOTED_IDENTIFIER ON;
GO
DECLARE @id_conv_sem3 BIGINT;
SELECT @id_conv_sem3 = id_convenio FROM libranza_empresas_convenio WHERE nit = '901888888-1';

IF NOT EXISTS (
    SELECT 1 FROM libranza_usuarios_empresa
    WHERE id_usuario = 12 AND id_convenio = @id_conv_sem3 AND estado = 'ACTIVO'
)
BEGIN
    INSERT INTO libranza_usuarios_empresa (id_convenio, id_usuario, rol_empresa, estado, created_at)
    VALUES (@id_conv_sem3, 12, 'ADMIN_EMPRESA', 'ACTIVO', GETUTCDATE());
    PRINT 'qa.empresa1 asociada a convenio SEMANAL';
END
ELSE
    PRINT 'qa.empresa1 ya asociada';
GO

-- 9. Empleado semanal demo (CC 1000000400)
SET QUOTED_IDENTIFIER ON;
GO
DECLARE @id_conv_sem4 BIGINT;
SELECT @id_conv_sem4 = id_convenio FROM libranza_empresas_convenio WHERE nit = '901888888-1';

IF NOT EXISTS (
    SELECT 1 FROM libranza_empleados
    WHERE id_convenio = @id_conv_sem4 AND tipo_documento = 'CC'
      AND numero_documento = '1000000400' AND estado = 'ACTIVO'
)
BEGIN
    INSERT INTO libranza_empleados (
        id_convenio, tipo_documento, numero_documento,
        nombres, apellidos, celular, correo, cargo,
        salario_mensual, periodicidad_pago,
        dia_pago_1, dia_pago_2, dia_pago_3, dia_pago_4,
        estado, cupo_preliminar, fecha_ultimo_calculo_cupo,
        origen_carga, lote_importacion, created_at
    )
    VALUES (
        @id_conv_sem4, 'CC', '1000000400',
        'Empleado Semanal', 'Demo', '3004004000', 'empleado.semanal@xpay.qa', 'Operario Semanal',
        2350000.00, 'SEMANAL',
        7, 14, 21, 28,
        'ACTIVO', 705000.00, GETUTCDATE(),
        'MANUAL', 'fase66-semanal', GETUTCDATE()
    );
    PRINT 'Empleado SEMANAL creado';
END
ELSE
    PRINT 'Empleado SEMANAL ya existe';
GO

-- 10. Cortes de pago empleado semanal
SET QUOTED_IDENTIFIER ON;
GO
DECLARE @id_conv_sem5 BIGINT;
DECLARE @id_emp_sem   BIGINT;
SELECT @id_conv_sem5 = id_convenio FROM libranza_empresas_convenio WHERE nit = '901888888-1';
SELECT @id_emp_sem   = id_empleado FROM libranza_empleados
WHERE id_convenio = @id_conv_sem5 AND numero_documento = '1000000400' AND estado = 'ACTIVO';

IF NOT EXISTS (SELECT 1 FROM libranza_empleado_cortes_pago WHERE id_empleado = @id_emp_sem AND estado = 'ACTIVO')
BEGIN
    INSERT INTO libranza_empleado_cortes_pago (id_empleado, numero_corte, dia_pago, valor_pago_programado, estado, created_at)
    VALUES
        (@id_emp_sem, 1,  7, 500000.00, 'ACTIVO', GETUTCDATE()),
        (@id_emp_sem, 2, 14, 600000.00, 'ACTIVO', GETUTCDATE()),
        (@id_emp_sem, 3, 21, 550000.00, 'ACTIVO', GETUTCDATE()),
        (@id_emp_sem, 4, 28, 700000.00, 'ACTIVO', GETUTCDATE());
    PRINT 'Cortes de pago SEMANAL creados';
END
ELSE
    PRINT 'Cortes de pago SEMANAL ya existen';
GO

-- 11. Verificar resultado final
SET QUOTED_IDENTIFIER ON;
GO
SELECT c.id_convenio, c.nombre_empresa, c.periodicidad_pago,
       c.dia_pago_1, c.dia_pago_2, c.dia_pago_3, c.dia_pago_4,
       c.porcentaje_maximo_cupo, c.estado
FROM libranza_empresas_convenio c WHERE c.nit = '901888888-1';

SELECT e.id_empleado, e.numero_documento, e.nombres, e.apellidos,
       e.periodicidad_pago, e.dia_pago_1, e.dia_pago_2, e.dia_pago_3, e.dia_pago_4,
       e.salario_mensual, e.estado
FROM libranza_empleados e
JOIN libranza_empresas_convenio c ON c.id_convenio = e.id_convenio
WHERE c.nit = '901888888-1' AND e.estado = 'ACTIVO';

SELECT lcp.id_corte_pago, lcp.numero_corte, lcp.dia_pago, lcp.valor_pago_programado,
       lcp.valor_pago_programado * 30.0 / 100 AS cupo_base_esperado, lcp.estado
FROM libranza_empleado_cortes_pago lcp
JOIN libranza_empleados e ON e.id_empleado = lcp.id_empleado
JOIN libranza_empresas_convenio c ON c.id_convenio = e.id_convenio
WHERE c.nit = '901888888-1' AND lcp.estado = 'ACTIVO'
ORDER BY lcp.numero_corte;
GO
