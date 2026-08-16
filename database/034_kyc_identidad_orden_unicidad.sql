-- =====================================================================
-- Migración 034: soporte de orden/unicidad para identidad verificada
-- Veriff (Commit 4 — Motor de Evaluación de Crédito Datacrédito /
-- onboarding móvil, Fase 0). Dos responsabilidades relacionadas, en
-- secciones separadas — mismo patrón que 009_kyc_verificaciones.sql
-- (esa migración también tocó dos tablas, usuarios + kyc_verificaciones,
-- en un solo script).
--
-- Idempotente y fail-fast — no borra datos, no transforma valores
-- existentes, no hace backfill, no toca ninguna otra columna/tabla/índice
-- fuera de lo descrito aquí.
--
-- SECCIÓN 1 — dbo.kyc_verificaciones: 2 columnas nuevas
--   attempt_id_veriff    VARCHAR(200) NULL
--   decision_time_veriff DATETIME2    NULL
--
-- Motivo: la regla de orden entre decisiones de Veriff para una misma
-- sesión (attemptId distinto, decisionTime posterior) es imposible de
-- aplicar de forma durable sin persistir estos dos valores — hoy no
-- existe ningún mecanismo equivalente (confirmado por precheck exhaustivo
-- del código y del esquema real antes de esta migración).
--
-- IMPORTANTE — dos relojes distintos, NUNCA confundir ni reutilizar uno
-- por el otro:
--   fecha_decision (ya existente, sin cambios)
--     = instante en que XPAY procesa la decisión (DateTime.UtcNow del
--       propio servidor XPAY en KycService.ProcessVeriffWebhookAsync).
--   decision_time_veriff (nueva, esta migración)
--     = verification.decisionTime entregado por Veriff — el instante en
--       que VERIFF tomó la decisión, documentado en formato ISO 8601
--       ("UTC YYYY-MM-DDTHH:MM:SS.SSS+Timezone Offset").
-- Ambos pueden diferir por latencia de red/reintentos de entrega del
-- webhook — nunca deben tratarse como intercambiables.
--
-- Sin DEFAULT, sin backfill: las filas históricas de kyc_verificaciones
-- (creadas antes de esta migración) quedan con ambas columnas NULL. Esto
-- es intencional — no hay evidencia real del attemptId/decisionTime
-- original de esas filas, y XPAY nunca debe inventar un valor. La regla
-- de transición para estos NULL históricos vive en el código de
-- KycService (Commit 4, no en esta migración): un AttemptIdVeriff NULL
-- persistido se trata como "sin intento previo rastreado", no como una
-- ambigüedad de orden que bloquee al usuario indefinidamente.
--
-- SECCIÓN 2 — dbo.personas: índice UNIQUE FILTRADO nuevo
--   UX_personas_documento_verificado
--   ON (id_unidad_negocio, numero_documento_verificado)
--   WHERE numero_documento_verificado IS NOT NULL AND identidad_verificada = 1
--
-- Motivo: impedir que dos Personas de la misma unidad de negocio queden
-- con IdentidadVerificada=true sobre el mismo NumeroDocumentoVerificado —
-- es el backstop real de base de datos para el chequeo de aplicación
-- (AnyAsync + AppLockHelper) que se implementará en KycService. Antes de
-- crear el índice, esta migración valida con un guard fail-fast que no
-- existan ya duplicados que impedirían crearlo — si los hubiera, aborta
-- con THROW sin intentar reparar/fusionar/desverificar nada.
--
-- NumeroDocumento y TipoDocumento legado NO se tocan en esta migración —
-- decisión ya aprobada de mantenerlos NULL hasta que exista un
-- TipoDocumento XPAY inequívoco (fuera de alcance de Commit 4).
-- IX_personas_documento (el índice legado sobre tipo_documento/
-- numero_documento) permanece completamente intacto, sin modificarse.
--
-- Esta migración NO crea: lógica de Datacrédito, reglas de mapeo de
-- documento, backfill de identidad, ni ningún cambio en otras tablas.
--
-- NO EJECUTADA POR EL AGENTE — preparada para revisión y ejecución manual
-- del usuario.
-- =====================================================================

SET XACT_ABORT ON;

-- Requeridas por SQL Server para crear el índice filtrado de la Sección 2
-- (mismo motivo documentado en 029/031/032).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT '=== INICIO MIGRACIÓN 034: identidad Veriff — orden y unicidad ===';

    -- ═══════════════════════════════════════════════════════════════════
    -- SECCIÓN 1: dbo.kyc_verificaciones
    -- ═══════════════════════════════════════════════════════════════════
    DECLARE @objIdKyc INT = OBJECT_ID('dbo.kyc_verificaciones', 'U');
    IF @objIdKyc IS NULL
        THROW 55000, N'Migración 034 abortada: dbo.kyc_verificaciones no existe. No se puede continuar.', 1;

    -- 1.1) attempt_id_veriff VARCHAR(200) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdKyc AND name = 'attempt_id_veriff')
    BEGIN
        ALTER TABLE dbo.kyc_verificaciones ADD attempt_id_veriff VARCHAR(200) NULL;
        PRINT 'OK: columna attempt_id_veriff agregada (NULL para filas históricas — sin backfill)';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdKyc AND c.name = 'attempt_id_veriff' AND ty.name = 'varchar' AND c.max_length = 200 AND c.is_nullable = 1
        )
            THROW 55001, N'Migración 034 abortada: dbo.kyc_verificaciones.attempt_id_veriff ya existe pero no es varchar(200) NULL — revisar manualmente.', 1;
        PRINT 'INFO: dbo.kyc_verificaciones.attempt_id_veriff ya existe con la estructura esperada — omitida';
    END

    -- 1.2) decision_time_veriff DATETIME2 NULL (sin DEFAULT — nunca autogenerado por la BD)
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdKyc AND name = 'decision_time_veriff')
    BEGIN
        ALTER TABLE dbo.kyc_verificaciones ADD decision_time_veriff DATETIME2 NULL;
        PRINT 'OK: columna decision_time_veriff agregada (NULL para filas históricas — sin backfill)';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdKyc AND c.name = 'decision_time_veriff' AND ty.name = 'datetime2' AND c.is_nullable = 1
        )
            THROW 55002, N'Migración 034 abortada: dbo.kyc_verificaciones.decision_time_veriff ya existe pero no es datetime2 NULL — revisar manualmente.', 1;
        PRINT 'INFO: dbo.kyc_verificaciones.decision_time_veriff ya existe con la estructura esperada — omitida';
    END

    -- ═══════════════════════════════════════════════════════════════════
    -- SECCIÓN 2: dbo.personas — índice UNIQUE FILTRADO
    -- ═══════════════════════════════════════════════════════════════════
    DECLARE @objIdPersonas INT = OBJECT_ID('dbo.personas', 'U');
    IF @objIdPersonas IS NULL
        THROW 55010, N'Migración 034 abortada: dbo.personas no existe. No se puede continuar.', 1;

    -- Validación estructural previa de las columnas involucradas — deben
    -- coincidir exactamente con lo aprobado en los Commits 2/3, sin
    -- suponer nada.
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objIdPersonas AND c.name = 'identidad_verificada' AND ty.name = 'bit' AND c.is_nullable = 0
    )
        THROW 55011, N'Migración 034 abortada: dbo.personas.identidad_verificada no es bit NOT NULL — revisar manualmente.', 1;

    IF NOT EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objIdPersonas AND c.name = 'numero_documento_verificado' AND ty.name = 'varchar' AND c.max_length = 30 AND c.is_nullable = 1
    )
        THROW 55012, N'Migración 034 abortada: dbo.personas.numero_documento_verificado no es varchar(30) NULL — revisar manualmente.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdPersonas AND name = 'id_unidad_negocio')
        THROW 55013, N'Migración 034 abortada: dbo.personas.id_unidad_negocio no existe — estructura inesperada.', 1;

    -- Confirmar que IX_personas_documento (índice legado) sigue existiendo
    -- intacto — esta migración nunca debe tocarlo.
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdPersonas AND name = 'IX_personas_documento')
        THROW 55014, N'Migración 034 abortada: dbo.personas no tiene el índice legado IX_personas_documento esperado — estructura inesperada, no se continúa.', 1;

    -- Buscar el índice por el nombre exacto propuesto.
    DECLARE @ixExacto INT = (SELECT index_id FROM sys.indexes WHERE object_id = @objIdPersonas AND name = 'UX_personas_documento_verificado');

    IF @ixExacto IS NOT NULL
    BEGIN
        -- Ya existe con ese nombre — validar que su estructura coincide
        -- exactamente con lo esperado antes de tratarlo como idempotente.
        DECLARE @ixUnique BIT, @ixCols NVARCHAR(400), @ixFiltro NVARCHAR(MAX);
        SELECT
            @ixUnique = i.is_unique,
            @ixCols   = STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal),
            @ixFiltro = i.filter_definition
        FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = @objIdPersonas AND i.name = 'UX_personas_documento_verificado'
        GROUP BY i.is_unique, i.filter_definition;

        -- Comparación tolerante a formato exacto de filter_definition (esta
        -- es la primera vez que este índice se crea — no hay una cadena
        -- exacta ya observada empíricamente como sí la hubo para
        -- IX_personas_documento en 032). Se exige presencia inequívoca de
        -- ambas columnas involucradas y de la semántica NOT NULL + = 1,
        -- en vez de una igualdad de cadena frágil.
        IF @ixUnique <> 1
           OR @ixCols <> 'id_unidad_negocio,numero_documento_verificado'
           OR @ixFiltro IS NULL
           OR @ixFiltro NOT LIKE '%[numero_documento_verificado]%IS NOT NULL%'
           OR @ixFiltro NOT LIKE '%[identidad_verificada]%1%'
            THROW 55020, N'Migración 034 abortada: UX_personas_documento_verificado ya existe pero su estructura (unicidad/columnas/filtro) no coincide con lo esperado — revisar manualmente, no se recrea automáticamente.', 1;

        PRINT 'INFO: UX_personas_documento_verificado ya existe con la estructura esperada — omitido';
    END
    ELSE
    BEGIN
        -- No existe con ese nombre exacto. Verificar que no exista ya un
        -- índice EQUIVALENTE bajo otro nombre (mismas columnas/orden,
        -- único, con algún filtro que involucre ambas columnas) — si lo
        -- hubiera, es una situación ambigua que no se resuelve
        -- automáticamente (no se renombra, no se crea un duplicado).
        IF EXISTS (
            SELECT 1
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = @objIdPersonas
              AND i.is_unique = 1
              AND i.filter_definition IS NOT NULL
              AND i.filter_definition LIKE '%[numero_documento_verificado]%'
              AND i.filter_definition LIKE '%[identidad_verificada]%'
              AND i.name <> 'UX_personas_documento_verificado'
            GROUP BY i.name
            HAVING STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) = 'id_unidad_negocio,numero_documento_verificado'
        )
            THROW 55021, N'Migración 034 abortada: ya existe un índice UNIQUE filtrado equivalente sobre (id_unidad_negocio, numero_documento_verificado) con un nombre distinto a UX_personas_documento_verificado — revisar manualmente antes de crear uno nuevo.', 1;

        -- Guard fail-fast: no crear el índice si ya existen duplicados que
        -- lo harían fallar. Nunca se repara, desverifica o fusiona nada —
        -- solo se aborta con un mensaje claro.
        IF EXISTS (
            SELECT 1
            FROM dbo.personas
            WHERE numero_documento_verificado IS NOT NULL AND identidad_verificada = 1
            GROUP BY id_unidad_negocio, numero_documento_verificado
            HAVING COUNT(*) > 1
        )
            THROW 55022, N'Migración 034 abortada: existen dos o más Personas con IdentidadVerificada=1 y el mismo NumeroDocumentoVerificado dentro de la misma unidad de negocio. No se crea el índice. Revisar manualmente los registros implicados antes de reintentar — no se repara ni fusiona automáticamente.', 1;

        CREATE UNIQUE INDEX UX_personas_documento_verificado
            ON dbo.personas (id_unidad_negocio, numero_documento_verificado)
            WHERE numero_documento_verificado IS NOT NULL AND identidad_verificada = 1;

        PRINT 'OK: UX_personas_documento_verificado creado (UNIQUE FILTRADO — documentos verificados únicos por unidad de negocio)';
    END

    COMMIT TRANSACTION;
    PRINT '=== FIN MIGRACIÓN 034 (COMMIT OK) ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '=== ERROR EN MIGRACIÓN 034 — ROLLBACK EJECUTADO ===';
    THROW;
END CATCH;

-- ── Verificación final ───────────────────────────────────────────────────
-- Banderas booleanas independientes + conteos — confirmación legible para
-- quien ejecute el script, no el mecanismo de aplicación de la política
-- (eso ya ocurrió vía THROW dentro de la transacción).
--
-- SQL dinámico deliberado para la parte de kyc_verificaciones: igual que
-- en 032, attempt_id_veriff/decision_time_veriff pueden ser columnas
-- recién creadas por ESTE mismo batch — referenciarlas directamente como
-- columnas reales de dbo.kyc_verificaciones en un SELECT normal haría que
-- SQL Server intente enlazar sus nombres al compilar el batch completo,
-- antes de que el ALTER TABLE de la Sección 1 llegue a ejecutarse. El
-- texto de sp_executesql es una cadena para el compilador del batch
-- externo — solo se compila/enlaza en tiempo de ejecución, después del
-- COMMIT. La parte de personas NO necesita esto porque
-- numero_documento_verificado/identidad_verificada ya existían antes de
-- esta migración (Commits 2/3).

DECLARE @sqlVerificacionKyc NVARCHAR(MAX) = N'
SELECT
    CASE WHEN OBJECT_ID(''dbo.kyc_verificaciones'',''U'') IS NOT NULL THEN 1 ELSE 0 END AS tabla_kyc_ok,
    CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(''dbo.kyc_verificaciones'',''U'') AND name = ''attempt_id_veriff'') THEN 1 ELSE 0 END AS attempt_id_veriff_existe,
    CASE WHEN (SELECT ty.name FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID(''dbo.kyc_verificaciones'',''U'') AND c.name = ''attempt_id_veriff'') = ''varchar'' THEN 1 ELSE 0 END AS attempt_id_veriff_tipo_ok,
    CASE WHEN (SELECT c.max_length FROM sys.columns c WHERE c.object_id = OBJECT_ID(''dbo.kyc_verificaciones'',''U'') AND c.name = ''attempt_id_veriff'') = 200 THEN 1 ELSE 0 END AS attempt_id_veriff_longitud_ok,
    CASE WHEN (SELECT c.is_nullable FROM sys.columns c WHERE c.object_id = OBJECT_ID(''dbo.kyc_verificaciones'',''U'') AND c.name = ''attempt_id_veriff'') = 1 THEN 1 ELSE 0 END AS attempt_id_veriff_nullable_ok,
    CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(''dbo.kyc_verificaciones'',''U'') AND name = ''decision_time_veriff'') THEN 1 ELSE 0 END AS decision_time_veriff_existe,
    CASE WHEN (SELECT ty.name FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID(''dbo.kyc_verificaciones'',''U'') AND c.name = ''decision_time_veriff'') = ''datetime2'' THEN 1 ELSE 0 END AS decision_time_veriff_tipo_ok,
    CASE WHEN (SELECT c.is_nullable FROM sys.columns c WHERE c.object_id = OBJECT_ID(''dbo.kyc_verificaciones'',''U'') AND c.name = ''decision_time_veriff'') = 1 THEN 1 ELSE 0 END AS decision_time_veriff_nullable_ok,
    (SELECT COUNT(*) FROM dbo.kyc_verificaciones) AS filas_historicas_totales,
    (SELECT COUNT(*) FROM dbo.kyc_verificaciones WHERE attempt_id_veriff IS NOT NULL) AS filas_con_attempt_id_veriff,
    (SELECT COUNT(*) FROM dbo.kyc_verificaciones WHERE decision_time_veriff IS NOT NULL) AS filas_con_decision_time_veriff;';
EXEC sp_executesql @sqlVerificacionKyc;

-- Verificación de personas — no necesita SQL dinámico (columnas
-- preexistentes desde antes de esta migración).
DECLARE @objIdPersonasFinal INT = OBJECT_ID('dbo.personas', 'U');

SELECT
    CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdPersonasFinal AND name = 'numero_documento_verificado') THEN 1 ELSE 0 END AS numero_documento_verificado_existe,
    CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdPersonasFinal AND name = 'identidad_verificada') THEN 1 ELSE 0 END AS identidad_verificada_existe,
    CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdPersonasFinal AND name = 'UX_personas_documento_verificado') THEN 1 ELSE 0 END AS indice_nuevo_existe,
    (SELECT is_unique FROM sys.indexes WHERE object_id = @objIdPersonasFinal AND name = 'UX_personas_documento_verificado') AS indice_nuevo_is_unique,
    (SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
     FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
     JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
     WHERE i.object_id = @objIdPersonasFinal AND i.name = 'UX_personas_documento_verificado') AS indice_nuevo_columnas,
    (SELECT filter_definition FROM sys.indexes WHERE object_id = @objIdPersonasFinal AND name = 'UX_personas_documento_verificado') AS indice_nuevo_filtro,
    (SELECT COUNT(*) FROM (
        SELECT id_unidad_negocio, numero_documento_verificado
        FROM dbo.personas
        WHERE numero_documento_verificado IS NOT NULL AND identidad_verificada = 1
        GROUP BY id_unidad_negocio, numero_documento_verificado
        HAVING COUNT(*) > 1
    ) dup) AS duplicados_en_subconjunto_indexado,
    CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdPersonasFinal AND name = 'IX_personas_documento') THEN 1 ELSE 0 END AS indice_legado_intacto,
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = @objIdPersonasFinal OR referenced_object_id = @objIdPersonasFinal) AS total_fks_personas_sin_cambio;

DECLARE @resultadoFinal NVARCHAR(20) =
    CASE WHEN
        OBJECT_ID('dbo.kyc_verificaciones', 'U') IS NOT NULL
        AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.kyc_verificaciones','U') AND name = 'attempt_id_veriff')
        AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.kyc_verificaciones','U') AND name = 'decision_time_veriff')
        AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdPersonasFinal AND name = 'UX_personas_documento_verificado')
        AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdPersonasFinal AND name = 'IX_personas_documento')
    THEN N'OK — migración 034 aplicada y verificada'
    ELSE N'REVISAR — algún componente no coincide con el esperado; ver columnas anteriores'
    END;
SELECT @resultadoFinal AS resultado;

PRINT '=== VERIFICACIÓN COMPLETA ===';
