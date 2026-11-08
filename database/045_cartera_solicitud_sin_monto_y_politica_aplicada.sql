-- =====================================================================
-- Migración 045: Cartera Ordinaria — solicitud de cupo SIN monto elegido
-- por el cliente + alineación de cartera_politicas_credito con la política
-- aprobada del motor (XPAY-676B).
--
-- Regla funcional: el cliente solicita ser EVALUADO para obtener un cupo;
-- NO solicita ni selecciona un monto. XPAY determina el cupo exclusivamente
-- con CarteraDecisionEngine + CarteraPolicyParameters.Vigente().
--
-- Idempotente y fail-fast (mismo patrón que 032/036): no borra filas, no
-- borra columnas, no transforma valores históricos. Toca únicamente:
--   1) dbo.cartera_solicitudes_cupo.monto_solicitado:
--        DECIMAL(18,2) NOT NULL -> DECIMAL(18,2) NULL  (columna LEGADO).
--        Las solicitudes anteriores conservan su valor; las nuevas quedan
--        NULL porque el código ya no conoce el campo.
--   2) ck_cartera_solicitudes_cupo_monto_solicitado:
--        CHECK (monto_solicitado > 0)
--        -> CHECK (monto_solicitado IS NULL OR monto_solicitado > 0)
--        Se recrea con el MISMO nombre, así la verificación de la 035
--        (que sólo exige que la columna y el CHECK existan) sigue válida.
--        Los valores históricos positivos siguen cumpliendo el CHECK.
--   3) dbo.cartera_politicas_credito, fila ACTIVA única:
--        cupo_minimo 200000, cupo_maximo 700000, edad_minima 18,
--        edad_maxima 65 — la política aprobada que aplica el motor. No toca
--        score_datacredito_minimo ni requiere_veriff. La tabla deja de ser
--        editable vía API (PUT admin/politica → 409); sólo conserva la
--        trazabilidad de id_politica_aplicada.
--
-- Comportamiento por paso:
--   1) monto_solicitado NOT NULL decimal(18,2) -> se altera a NULL; ya NULL
--      -> se omite; cualquier otro tipo/precisión -> THROW.
--   2) CHECK con la definición legada exacta -> DROP + ADD con la nueva;
--      ya con la nueva -> se omite; ausente o con otra definición -> THROW.
--      El DROP ocurre antes del ALTER COLUMN (SQL Server no permite alterar
--      una columna referenciada por un CHECK) y todo va en UNA transacción.
--   3) Exactamente UNA fila ACTIVA; si sus valores difieren de la política
--      aprobada se actualizan; si ya coinciden se omite; 0 o >1 filas
--      activas -> THROW (no se adivina cuál alinear).
--
-- NO EJECUTADA POR EL AGENTE — preparada para revisión y ejecución manual
-- controlada, con respaldo previo, en una fase autorizada específica.
-- =====================================================================

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

    -- ═══════════════════════════════════════════════════════════════════
    -- 3) Fila ACTIVA de cartera_politicas_credito = política aprobada del motor
    -- ═══════════════════════════════════════════════════════════════════
    DECLARE @activas INT = (SELECT COUNT(*) FROM dbo.cartera_politicas_credito WHERE estado = 'ACTIVO');
    IF @activas <> 1
        THROW 57006, N'Migración 045 abortada: se esperaba exactamente UNA fila ACTIVA en dbo.cartera_politicas_credito — revisar manualmente.', 1;

    IF EXISTS (
        SELECT 1 FROM dbo.cartera_politicas_credito
        WHERE estado = 'ACTIVO'
          AND (cupo_minimo <> 200000 OR cupo_maximo <> 700000 OR edad_minima <> 18 OR edad_maxima <> 65)
    )
    BEGIN
        UPDATE dbo.cartera_politicas_credito
        SET cupo_minimo = 200000,
            cupo_maximo = 700000,
            edad_minima = 18,
            edad_maxima = 65,
            updated_at  = SYSUTCDATETIME()
        WHERE estado = 'ACTIVO';
        PRINT 'OK: fila ACTIVA de cartera_politicas_credito alineada con la política aprobada del motor';
    END
    ELSE
        PRINT 'INFO: fila ACTIVA de cartera_politicas_credito ya alineada — omitido';

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

SELECT
    (SELECT COUNT(*) FROM dbo.cartera_solicitudes_cupo)                                  AS total_solicitudes,
    (SELECT COUNT(*) FROM dbo.cartera_solicitudes_cupo WHERE monto_solicitado IS NOT NULL) AS solicitudes_con_monto_legado_preservado,
    (SELECT COUNT(*) FROM dbo.cartera_solicitudes_cupo WHERE monto_solicitado IS NULL)     AS solicitudes_sin_monto;

SELECT id_politica, score_datacredito_minimo, requiere_veriff, cupo_minimo, cupo_maximo,
       edad_minima, edad_maxima, estado, vigente_desde, updated_at
FROM dbo.cartera_politicas_credito
WHERE estado = 'ACTIVO';

PRINT '=== VERIFICACIÓN 045 COMPLETA ===';
