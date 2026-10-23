-- =====================================================================
-- XPAY PROD - VARIANTE_PROD 014
-- Origen: database/014_libranza_semanal.sql
-- SHA-256 del origen: aa1e1f4f92c20411b4bdf6c078d880643621ff4f5471c01686730a42dfdbc844
-- Lineas conservadas del origen: 1-66
-- Estado en el manifiesto: LISTO
-- Generado por database/prod/tools/construir_paquete.py.
-- NO EDITAR A MANO: modificar la especificacion y regenerar.
-- El contenido despues de la marca es copia exacta de esas lineas.
-- =====================================================================
-- @@FIN_ENCABEZADO_VARIANTE_PROD@@
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
