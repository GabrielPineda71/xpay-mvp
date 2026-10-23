-- =====================================================================
-- XPAY PROD - VARIANTE_PROD 045a
-- Origen: database/045_cartera_solicitud_sin_monto_y_politica_aplicada.sql
-- SHA-256 del origen: 8f6909891af7b4e1bd62f3c8a05b229ca7cdfbe32b32edc168c322fc4774b42a
-- Lineas conservadas del origen: 44-119, 145-162
-- Estado en el manifiesto: BLOQUEADO_PENDIENTE_AUTORIZACION
-- Generado por database/prod/tools/construir_paquete.py.
-- NO EDITAR A MANO: modificar la especificacion y regenerar.
-- El contenido despues de la marca es copia exacta de esas lineas.
-- =====================================================================
-- @@FIN_ENCABEZADO_VARIANTE_PROD@@
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT '=== INICIO MIGRACIÓN 045: solicitud sin monto + política aplicada ===';

    DECLARE @objIdSolicitud INT = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U');
    IF @objIdSolicitud IS NULL
        THROW 57000, N'Migración 045 abortada: dbo.cartera_solicitudes_cupo no existe. No se puede continuar.', 1;

    DECLARE @objIdPolitica INT = OBJECT_ID('dbo.cartera_politicas_credito', 'U');
    IF @objIdPolitica IS NULL
        THROW 57001, N'Migración 045 abortada: dbo.cartera_politicas_credito no existe. No se puede continuar.', 1;

    -- ═══════════════════════════════════════════════════════════════════
    -- Estado actual de la columna y del CHECK
    -- ═══════════════════════════════════════════════════════════════════
    DECLARE @msNullable BIT, @msTipo SYSNAME, @msPrecision TINYINT, @msScale TINYINT;
    SELECT @msNullable = c.is_nullable, @msTipo = ty.name, @msPrecision = c.precision, @msScale = c.scale
    FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
    WHERE c.object_id = @objIdSolicitud AND c.name = 'monto_solicitado';

    IF @msTipo IS NULL
        THROW 57002, N'Migración 045 abortada: dbo.cartera_solicitudes_cupo.monto_solicitado no existe — estructura inesperada.', 1;
    IF @msTipo <> 'decimal' OR @msPrecision <> 18 OR @msScale <> 2
        THROW 57003, N'Migración 045 abortada: monto_solicitado no es decimal(18,2) — revisar manualmente antes de continuar.', 1;

    -- Definición normalizada por SQL Server, comparada sin espacios.
    DECLARE @ckDef NVARCHAR(MAX) = (
        SELECT REPLACE(cc.definition, ' ', '')
        FROM sys.check_constraints cc
        WHERE cc.parent_object_id = @objIdSolicitud AND cc.name = 'ck_cartera_solicitudes_cupo_monto_solicitado');

    DECLARE @ckLegado NVARCHAR(200) = N'([monto_solicitado]>(0))';
    DECLARE @ckNuevo  NVARCHAR(200) = N'([monto_solicitado]ISNULLOR[monto_solicitado]>(0))';

    IF @ckDef IS NULL
        THROW 57004, N'Migración 045 abortada: falta ck_cartera_solicitudes_cupo_monto_solicitado — revisar manualmente.', 1;
    IF @ckDef <> @ckLegado AND @ckDef <> @ckNuevo
        THROW 57005, N'Migración 045 abortada: ck_cartera_solicitudes_cupo_monto_solicitado tiene una definición inesperada — revisar manualmente.', 1;

    -- ═══════════════════════════════════════════════════════════════════
    -- 1+2) CHECK legado -> DROP; columna NOT NULL -> NULL; CHECK nuevo -> ADD
    -- ═══════════════════════════════════════════════════════════════════
    IF @ckDef = @ckLegado
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo DROP CONSTRAINT ck_cartera_solicitudes_cupo_monto_solicitado;
        PRINT 'OK: CHECK legado ck_cartera_solicitudes_cupo_monto_solicitado eliminado (se recrea abajo)';
    END

    IF @msNullable = 0
    BEGIN
        ALTER TABLE dbo.cartera_solicitudes_cupo ALTER COLUMN monto_solicitado DECIMAL(18,2) NULL;
        PRINT 'OK: monto_solicitado alterado a NULL (columna LEGADO, valores históricos preservados)';
    END
    ELSE
        PRINT 'INFO: monto_solicitado ya es NULL — omitido';

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = @objIdSolicitud AND name = 'ck_cartera_solicitudes_cupo_monto_solicitado'
    )
    BEGIN
        EXEC (N'
            ALTER TABLE dbo.cartera_solicitudes_cupo
                ADD CONSTRAINT ck_cartera_solicitudes_cupo_monto_solicitado
                CHECK (monto_solicitado IS NULL OR monto_solicitado > 0);
        ');
        PRINT 'OK: CHECK ck_cartera_solicitudes_cupo_monto_solicitado recreado (NULL o > 0)';
    END
    ELSE
        PRINT 'INFO: CHECK ck_cartera_solicitudes_cupo_monto_solicitado ya tiene la definición nueva — omitido';

    COMMIT TRANSACTION;
    PRINT '=== FIN MIGRACIÓN 045 (COMMIT OK) ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '=== ERROR EN MIGRACIÓN 045 — ROLLBACK EJECUTADO ===';
    THROW;
END CATCH;

-- ── Verificación final (sólo lectura) ────────────────────────────────────
SELECT c.name AS columna, ty.name AS tipo, c.precision, c.scale, c.is_nullable
FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U') AND c.name = 'monto_solicitado';

SELECT cc.name AS check_constraint, cc.definition
FROM sys.check_constraints cc
WHERE cc.parent_object_id = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U')
  AND cc.name = 'ck_cartera_solicitudes_cupo_monto_solicitado';
