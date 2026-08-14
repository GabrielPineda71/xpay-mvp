-- =====================================================================
-- Migración 032: Persona — nullability de documento legado + columnas de
-- identidad verificada (Commit 2, Motor de Evaluación de Crédito
-- Datacrédito / onboarding móvil, Fase 0)
--
-- Idempotente y fail-fast — no borra datos, no transforma valores
-- existentes, no hace backfill de identidad. Toca únicamente dbo.personas:
--   1) tipo_documento  VARCHAR(20) NOT NULL -> VARCHAR(20) NULL
--   2) numero_documento VARCHAR(30) NOT NULL -> VARCHAR(30) NULL
--   3) 7 columnas nuevas de identidad verificada (todas NULL salvo
--      identidad_verificada, que es NOT NULL DEFAULT 0)
--   4) IX_personas_documento: de UNIQUE sin filtro a UNIQUE FILTRADO
--      (WHERE tipo_documento IS NOT NULL AND numero_documento IS NOT NULL)
--      — necesario porque un índice UNIQUE sin filtro en SQL Server trata
--      múltiples combinaciones NULL como duplicadas entre sí, lo que
--      impediría crear más de una Persona sin documento por unidad de
--      negocio una vez que el Commit 3 empiece a insertarlas. La intención
--      funcional original (documento único por unidad de negocio) se
--      preserva exactamente igual para las personas que sí tienen
--      documento.
--
-- Separación de dominios (decisión explícita, ver diseño aprobado):
-- Persona almacena identidad verificada cruda (lo que Veriff entrega),
-- NUNCA una decisión crediticia. Por eso esta migración NO agrega
-- ApellidoDatacreditoConfirmado, IdentidadListaParaDatacredito, códigos
-- Datacrédito, ni ninguna regla de interpretación — eso pertenece
-- exclusivamente a cartera_politicas_credito / MotorEvaluacionCredito en
-- commits posteriores, no autorizados todavía.
--
-- Comportamiento:
--   1) Si dbo.personas.tipo_documento/numero_documento están en el estado
--      "legado" (NOT NULL, mismo tipo/longitud): se alteran a NULL,
--      preservando todos los valores actuales (ALTER COLUMN no toca datos).
--      Si ya están en el estado "migrado" (NULL, mismo tipo/longitud): se
--      omite, sin error. Cualquier otro estado (tipo/longitud distinto):
--      THROW — no se repara en silencio.
--   2) Cada columna nueva: si no existe, se agrega con el tipo/nullability/
--      default exacto de esta migración. Si ya existe: se valida tipo/
--      longitud/nullability/default — cualquier discrepancia aborta con
--      THROW.
--   3) IX_personas_documento: se valida su estructura actual EXACTA
--      (columnas, orden, unicidad, ausencia de filtro) contra el estado
--      "antes" conocido. Si coincide exactamente: DROP + CREATE con el
--      filtro nuevo. Si ya tiene el filtro nuevo exacto: se omite, sin
--      error. Cualquier otra estructura (columnas distintas, sin filtro
--      pero con menos/más columnas, etc.): THROW — nunca se hace DROP de
--      un índice cuya estructura no se pudo verificar exactamente primero.
--
-- Esta migración NO crea: reglas de Datacrédito, lógica de división de
-- apellidos, backfill de identidad_verificada basado en documento/KYC
-- histórico, ni ninguna escritura de identidad (eso corresponde a
-- KycService en el Commit 4, no tocado aquí).
--
-- NO EJECUTADA POR EL AGENTE — preparada para revisión y ejecución manual
-- del usuario.
-- =====================================================================

SET XACT_ABORT ON;

-- Requeridas por SQL Server para crear el índice filtrado más abajo
-- (mismo motivo documentado en 029_wallet_idempotencia.sql).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT '=== INICIO MIGRACIÓN 032: Persona — identidad verificada ===';

    DECLARE @objIdPersonas INT = OBJECT_ID('dbo.personas', 'U');
    IF @objIdPersonas IS NULL
        THROW 53000, N'Migración 032 abortada: dbo.personas no existe. No se puede continuar.', 1;

    -- ═══════════════════════════════════════════════════════════════════
    -- 1) tipo_documento: NOT NULL -> NULL (mismo tipo/longitud, sin tocar datos)
    -- ═══════════════════════════════════════════════════════════════════
    DECLARE @tdNullable BIT, @tdTipo SYSNAME, @tdLen SMALLINT;
    SELECT @tdNullable = c.is_nullable, @tdTipo = ty.name, @tdLen = c.max_length
    FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
    WHERE c.object_id = @objIdPersonas AND c.name = 'tipo_documento';

    IF @tdTipo IS NULL
        THROW 53001, N'Migración 032 abortada: dbo.personas.tipo_documento no existe — estructura inesperada.', 1;
    ELSE IF @tdTipo <> 'varchar' OR @tdLen <> 20
        THROW 53002, N'Migración 032 abortada: dbo.personas.tipo_documento no es varchar(20) — revisar manualmente antes de continuar.', 1;
    ELSE IF @tdNullable = 0
    BEGIN
        ALTER TABLE dbo.personas ALTER COLUMN tipo_documento VARCHAR(20) NULL;
        PRINT 'OK: dbo.personas.tipo_documento alterado a NULL (valores existentes preservados)';
    END
    ELSE
        PRINT 'INFO: dbo.personas.tipo_documento ya es NULL — omitido';

    -- ═══════════════════════════════════════════════════════════════════
    -- 2) numero_documento: NOT NULL -> NULL (mismo tipo/longitud, sin tocar datos)
    -- ═══════════════════════════════════════════════════════════════════
    DECLARE @ndNullable BIT, @ndTipo SYSNAME, @ndLen SMALLINT;
    SELECT @ndNullable = c.is_nullable, @ndTipo = ty.name, @ndLen = c.max_length
    FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
    WHERE c.object_id = @objIdPersonas AND c.name = 'numero_documento';

    IF @ndTipo IS NULL
        THROW 53003, N'Migración 032 abortada: dbo.personas.numero_documento no existe — estructura inesperada.', 1;
    ELSE IF @ndTipo <> 'varchar' OR @ndLen <> 30
        THROW 53004, N'Migración 032 abortada: dbo.personas.numero_documento no es varchar(30) — revisar manualmente antes de continuar.', 1;
    ELSE IF @ndNullable = 0
    BEGIN
        ALTER TABLE dbo.personas ALTER COLUMN numero_documento VARCHAR(30) NULL;
        PRINT 'OK: dbo.personas.numero_documento alterado a NULL (valores existentes preservados)';
    END
    ELSE
        PRINT 'INFO: dbo.personas.numero_documento ya es NULL — omitido';

    -- ═══════════════════════════════════════════════════════════════════
    -- 3) Columnas nuevas de identidad verificada
    -- ═══════════════════════════════════════════════════════════════════

    -- 3.1) identidad_verificada BIT NOT NULL DEFAULT 0
    -- ADD ... NOT NULL DEFAULT 0 sobre una tabla ya poblada hace que SQL
    -- Server rellene automáticamente 0 en TODAS las filas existentes como
    -- parte del propio ALTER TABLE — no es una inferencia de negocio, es
    -- el valor constante por defecto de una columna nueva, igual para
    -- cualquier fila histórica sin excepción.
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdPersonas AND name = 'identidad_verificada')
    BEGIN
        ALTER TABLE dbo.personas ADD identidad_verificada BIT NOT NULL CONSTRAINT df_personas_identidad_verificada DEFAULT (0);
        PRINT 'OK: columna identidad_verificada agregada (DEFAULT 0 aplicado a todas las filas existentes)';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdPersonas AND c.name = 'identidad_verificada' AND ty.name = 'bit' AND c.is_nullable = 0
        )
            THROW 53010, N'Migración 032 abortada: dbo.personas.identidad_verificada ya existe pero no es bit NOT NULL — revisar manualmente.', 1;
        PRINT 'INFO: dbo.personas.identidad_verificada ya existe con la estructura esperada — omitida';
    END

    -- 3.2) identidad_verificada_proveedor VARCHAR(50) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdPersonas AND name = 'identidad_verificada_proveedor')
    BEGIN
        ALTER TABLE dbo.personas ADD identidad_verificada_proveedor VARCHAR(50) NULL;
        PRINT 'OK: columna identidad_verificada_proveedor agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdPersonas AND c.name = 'identidad_verificada_proveedor' AND ty.name = 'varchar' AND c.max_length = 50 AND c.is_nullable = 1
        )
            THROW 53011, N'Migración 032 abortada: dbo.personas.identidad_verificada_proveedor ya existe pero no es varchar(50) NULL — revisar manualmente.', 1;
        PRINT 'INFO: dbo.personas.identidad_verificada_proveedor ya existe con la estructura esperada — omitida';
    END

    -- 3.3) identidad_verificada_fecha DATETIME2(3) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdPersonas AND name = 'identidad_verificada_fecha')
    BEGIN
        ALTER TABLE dbo.personas ADD identidad_verificada_fecha DATETIME2(3) NULL;
        PRINT 'OK: columna identidad_verificada_fecha agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdPersonas AND c.name = 'identidad_verificada_fecha' AND ty.name = 'datetime2' AND c.scale = 3 AND c.is_nullable = 1
        )
            THROW 53012, N'Migración 032 abortada: dbo.personas.identidad_verificada_fecha ya existe pero no es datetime2(3) NULL — revisar manualmente.', 1;
        PRINT 'INFO: dbo.personas.identidad_verificada_fecha ya existe con la estructura esperada — omitida';
    END

    -- 3.4) nombre_verificado_completo VARCHAR(200) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdPersonas AND name = 'nombre_verificado_completo')
    BEGIN
        ALTER TABLE dbo.personas ADD nombre_verificado_completo VARCHAR(200) NULL;
        PRINT 'OK: columna nombre_verificado_completo agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdPersonas AND c.name = 'nombre_verificado_completo' AND ty.name = 'varchar' AND c.max_length = 200 AND c.is_nullable = 1
        )
            THROW 53013, N'Migración 032 abortada: dbo.personas.nombre_verificado_completo ya existe pero no es varchar(200) NULL — revisar manualmente.', 1;
        PRINT 'INFO: dbo.personas.nombre_verificado_completo ya existe con la estructura esperada — omitida';
    END

    -- 3.5) apellido_verificado_completo VARCHAR(200) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdPersonas AND name = 'apellido_verificado_completo')
    BEGIN
        ALTER TABLE dbo.personas ADD apellido_verificado_completo VARCHAR(200) NULL;
        PRINT 'OK: columna apellido_verificado_completo agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdPersonas AND c.name = 'apellido_verificado_completo' AND ty.name = 'varchar' AND c.max_length = 200 AND c.is_nullable = 1
        )
            THROW 53014, N'Migración 032 abortada: dbo.personas.apellido_verificado_completo ya existe pero no es varchar(200) NULL — revisar manualmente.', 1;
        PRINT 'INFO: dbo.personas.apellido_verificado_completo ya existe con la estructura esperada — omitida';
    END

    -- 3.6) tipo_documento_veriff_raw VARCHAR(50) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdPersonas AND name = 'tipo_documento_veriff_raw')
    BEGIN
        ALTER TABLE dbo.personas ADD tipo_documento_veriff_raw VARCHAR(50) NULL;
        PRINT 'OK: columna tipo_documento_veriff_raw agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdPersonas AND c.name = 'tipo_documento_veriff_raw' AND ty.name = 'varchar' AND c.max_length = 50 AND c.is_nullable = 1
        )
            THROW 53015, N'Migración 032 abortada: dbo.personas.tipo_documento_veriff_raw ya existe pero no es varchar(50) NULL — revisar manualmente.', 1;
        PRINT 'INFO: dbo.personas.tipo_documento_veriff_raw ya existe con la estructura esperada — omitida';
    END

    -- 3.7) numero_documento_verificado VARCHAR(30) NULL
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = @objIdPersonas AND name = 'numero_documento_verificado')
    BEGIN
        ALTER TABLE dbo.personas ADD numero_documento_verificado VARCHAR(30) NULL;
        PRINT 'OK: columna numero_documento_verificado agregada';
    END
    ELSE
    BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = @objIdPersonas AND c.name = 'numero_documento_verificado' AND ty.name = 'varchar' AND c.max_length = 30 AND c.is_nullable = 1
        )
            THROW 53016, N'Migración 032 abortada: dbo.personas.numero_documento_verificado ya existe pero no es varchar(30) NULL — revisar manualmente.', 1;
        PRINT 'INFO: dbo.personas.numero_documento_verificado ya existe con la estructura esperada — omitida';
    END

    -- ═══════════════════════════════════════════════════════════════════
    -- 4) IX_personas_documento: UNIQUE sin filtro -> UNIQUE FILTRADO
    -- ═══════════════════════════════════════════════════════════════════
    DECLARE @ixId INT = (SELECT object_id FROM sys.indexes WHERE object_id = @objIdPersonas AND name = 'IX_personas_documento');

    IF @ixId IS NULL
        THROW 53020, N'Migración 032 abortada: dbo.personas no tiene el índice IX_personas_documento esperado — estructura inesperada, revisar manualmente antes de continuar.', 1;

    DECLARE @ixUnique BIT, @ixFiltro NVARCHAR(MAX), @ixCols NVARCHAR(400);
    SELECT
        @ixUnique = i.is_unique,
        @ixFiltro = i.filter_definition,
        @ixCols = STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
    FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE i.object_id = @objIdPersonas AND i.name = 'IX_personas_documento'
    GROUP BY i.is_unique, i.filter_definition;

    IF @ixUnique <> 1 OR @ixCols <> 'id_unidad_negocio,tipo_documento,numero_documento'
        THROW 53021, N'Migración 032 abortada: IX_personas_documento no es UNIQUE sobre (id_unidad_negocio, tipo_documento, numero_documento) en ese orden — estructura inesperada, no se toca el índice.', 1;

    IF @ixFiltro IS NULL
    BEGIN
        -- Estado "antes" exacto conocido: UNIQUE, mismas 3 columnas, sin filtro.
        DROP INDEX IX_personas_documento ON dbo.personas;
        CREATE UNIQUE INDEX IX_personas_documento
            ON dbo.personas (id_unidad_negocio, tipo_documento, numero_documento)
            WHERE tipo_documento IS NOT NULL AND numero_documento IS NOT NULL;
        PRINT 'OK: IX_personas_documento recreado como UNIQUE FILTRADO (documentos NULL ya no colisionan entre sí)';
    END
    ELSE IF @ixFiltro = '([tipo_documento] IS NOT NULL AND [numero_documento] IS NOT NULL)'
        PRINT 'INFO: IX_personas_documento ya tiene el filtro esperado — omitido';
    ELSE
        THROW 53022, N'Migración 032 abortada: IX_personas_documento tiene un filtro distinto al esperado — revisar manualmente, no se modifica automáticamente.', 1;

    COMMIT TRANSACTION;
    PRINT '=== FIN MIGRACIÓN 032 (COMMIT OK) ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '=== ERROR EN MIGRACIÓN 032 — ROLLBACK EJECUTADO ===';
    THROW;
END CATCH;

-- ── Verificación final ───────────────────────────────────────────────────
-- Banderas booleanas independientes + conteos — confirmación legible para
-- quien ejecute el script, no el mecanismo de aplicación de la política
-- (eso ya ocurrió vía THROW dentro de la transacción).
--
-- SQL dinámico deliberado (sp_executesql), no una preferencia estilística:
-- este SELECT referencia dbo.personas.identidad_verificada como columna
-- real (no vía sys.columns). En un batch T-SQL sin GO, SQL Server enlaza
-- (bind) los nombres de columna de TODO el batch en tiempo de compilación,
-- antes de ejecutar ninguna sentencia — incluida la que la crea más arriba.
-- Sin este envoltorio, el batch completo falla con "Invalid column name
-- 'identidad_verificada'" ANTES de que el ALTER TABLE ADD llegue a
-- ejecutarse (así falló el primer intento de esta migración). El texto de
-- sp_executesql es una cadena para el compilador del batch externo — su
-- contenido solo se compila/enlaza cuando se ejecuta, en tiempo de
-- ejecución, momento en el que la columna ya existe porque este bloque
-- corre después de COMMIT TRANSACTION. Ver 029/031: ninguno de los dos
-- necesita este envoltorio porque sus verificaciones finales solo leen
-- sys.columns/sys.indexes (metadatos, nunca la columna real de la tabla).
DECLARE @objIdFinal INT = OBJECT_ID('dbo.personas', 'U');

DECLARE @sqlVerificacionFinal NVARCHAR(MAX) = N'
SELECT
    CASE WHEN (SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(''dbo.personas'',''U'') AND name = ''tipo_documento'') = 1 THEN 1 ELSE 0 END AS tipo_documento_nullable_ok,
    CASE WHEN (SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(''dbo.personas'',''U'') AND name = ''numero_documento'') = 1 THEN 1 ELSE 0 END AS numero_documento_nullable_ok,
    CASE WHEN (
        SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(''dbo.personas'',''U'') AND name IN (
            ''identidad_verificada'',''identidad_verificada_proveedor'',''identidad_verificada_fecha'',
            ''nombre_verificado_completo'',''apellido_verificado_completo'',
            ''tipo_documento_veriff_raw'',''numero_documento_verificado'')
    ) = 7 THEN 1 ELSE 0 END AS columnas_nuevas_ok,
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(''dbo.personas'',''U'') AND i.name = ''IX_personas_documento'' AND i.is_unique = 1
          AND i.filter_definition = ''([tipo_documento] IS NOT NULL AND [numero_documento] IS NOT NULL)''
    ) THEN 1 ELSE 0 END AS indice_filtrado_ok,
    (SELECT COUNT(*) FROM dbo.personas) AS total_personas,
    (SELECT COUNT(*) FROM dbo.personas WHERE identidad_verificada = 0) AS personas_identidad_no_verificada,
    (SELECT COUNT(*) FROM dbo.personas WHERE identidad_verificada = 1) AS personas_identidad_verificada_debe_ser_0,
    (SELECT COUNT(*) FROM dbo.personas WHERE tipo_documento IS NOT NULL) AS personas_con_tipo_documento_preservado,
    (SELECT COUNT(*) FROM dbo.personas WHERE numero_documento IS NOT NULL) AS personas_con_numero_documento_preservado;';
EXEC sp_executesql @sqlVerificacionFinal;

-- Las dos consultas siguientes solo leen sys.columns/sys.indexes (metadatos
-- del catálogo, no la tabla real) — no tienen el problema de enlace
-- prematuro y no necesitan SQL dinámico, igual que en 029/031.
SELECT c.name AS columna, ty.name AS tipo, c.max_length, c.scale, c.is_nullable
FROM sys.columns c
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE c.object_id = @objIdFinal
  AND c.name IN (
      'tipo_documento','numero_documento','identidad_verificada','identidad_verificada_proveedor',
      'identidad_verificada_fecha','nombre_verificado_completo','apellido_verificado_completo',
      'tipo_documento_veriff_raw','numero_documento_verificado')
ORDER BY c.column_id;

SELECT i.name AS indice, i.is_unique, i.filter_definition
FROM sys.indexes i
WHERE i.object_id = @objIdFinal AND i.name = 'IX_personas_documento';

PRINT '=== VERIFICACIÓN COMPLETA ===';
