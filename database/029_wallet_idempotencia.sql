-- =====================================================================
-- Migración 029: Idempotencia de Wallet (Fase 71.2-E-G, endurecida en E-G.2)
-- Idempotente y fail-fast — no borra datos, no altera columnas existentes,
-- no agrega columnas automáticamente, no toca ninguna otra tabla. Una sola
-- tabla 100% nueva (dbo.wallet_idempotencia).
--
-- Diseño aprobado en docs/security/FASE_71.2_E_B_AUTORIZACION_IDOR.md,
-- secciones 16 (71.2-E-F, diseño de una sola transacción), 17 (71.2-E-G,
-- implementación) y 17.20-17.23 (71.2-E-G.1/G.2, correcciones).
--
-- Comportamiento:
--   1) Si dbo.wallet_idempotencia NO existe: la crea completa, con PK,
--      UNIQUE, CHECK y el índice de expiración.
--   2) Si YA existe: NO la modifica ni la repara silenciosamente. Verifica
--      que las 13 columnas requeridas existan, que los tipos/tamaños
--      críticos coincidan exactamente, y que existan la restricción UNIQUE
--      y el CHECK — si CUALQUIERA de estas verificaciones falla, aborta con
--      THROW y un mensaje explícito (rollback automático por XACT_ABORT),
--      sin agregar columnas, sin recrear constraints, sin borrar nada. El
--      índice de expiración se crea si falta, pero únicamente después de
--      que todas las verificaciones estructurales anteriores superaron.
--
-- Resumen del uso previsto (sin cambios respecto a versiones anteriores):
--   - EN_PROCESO se inserta como primer paso de la transacción financiera
--     existente (TransferirWalletAsync/PagarQrAsync) y se actualiza a
--     COMPLETADA justo antes del mismo COMMIT — nunca se confirma por
--     separado. Un rollback normal deshace la fila junto con el resto de
--     la operación: en operación normal NO deben quedar filas EN_PROCESO
--     persistidas (por eso el índice de limpieza está filtrado por
--     COMPLETADA, no por EN_PROCESO).
--   - La restricción UNIQUE (id_usuario, endpoint, idempotency_key) es el
--     mecanismo real de arbitraje entre solicitudes concurrentes con la
--     misma clave (ver sección 16.2 del documento de seguridad) — no un
--     candado aplicativo adicional. Es la razón por la que su ausencia en
--     una tabla preexistente aborta la migración en vez de continuar.
--   - Sin FALLIDA: un intento fallido no deja fila (rollback atómico), no
--     hay nada que cachear para un reintento con la misma clave.
--
-- Esta migración NO crea: job de limpieza automática (retención de 7 días
-- documentada, sin proceso programado todavía), tabla de auditoría de
-- intentos fallidos, ni ningún objeto relacionado con QR dinámico.
--
-- NO EJECUTADA POR EL AGENTE — preparada para revisión y ejecución manual
-- del usuario.
-- =====================================================================

SET XACT_ABORT ON;

-- Requeridas por SQL Server para crear el índice filtrado de la línea ~190
-- (ix_wallet_idempotencia_expiracion, WHERE estado = 'COMPLETADA'). sqlcmd
-- fija QUOTED_IDENTIFIER en OFF por defecto salvo -I, lo que rompe el CREATE
-- INDEX (Msg 1934). Las 7 opciones son las documentadas por Microsoft como
-- obligatorias para índices filtrados, no solo la que falló en este entorno.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT '=== INICIO MIGRACIÓN 029: Idempotencia de Wallet ===';

    DECLARE @objId INT = OBJECT_ID('dbo.wallet_idempotencia', 'U');

    IF @objId IS NULL
    BEGIN
        -- ── Primera ejecución: crear la tabla completa ───────────────────
        CREATE TABLE dbo.wallet_idempotencia (
            id_idempotencia        BIGINT           IDENTITY(1,1) NOT NULL,
            id_usuario              BIGINT           NOT NULL,
            endpoint                 VARCHAR(60)      NOT NULL,   -- identificador lógico interno, p.ej. 'wallets.transferencia' — nunca la URL ni el casing HTTP
            idempotency_key           UNIQUEIDENTIFIER NOT NULL,   -- generada por el cliente (crypto.randomUUID())
            request_hash               VARBINARY(32)    NOT NULL,   -- SHA-256 (32 bytes) del payload normalizado
            estado                      VARCHAR(12)      NOT NULL DEFAULT 'EN_PROCESO',
            http_status                  SMALLINT         NULL,        -- código HTTP de la respuesta original (200 en el único caso hoy soportado)
            id_recurso                    BIGINT           NULL,        -- p.ej. id_transaccion_ledger (mismo valor que la columna siguiente en esta primera versión)
            id_transaccion_ledger           BIGINT           NULL,
            respuesta_data_json               NVARCHAR(1000)   NULL,        -- solo el objeto "data" ya público de la respuesta — nunca JWT/documentos/nombres/correos/teléfonos
            fecha_creacion                     DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
            fecha_completado                     DATETIME2(3)     NULL,
            fecha_expiracion                       DATETIME2(3)     NOT NULL DEFAULT DATEADD(DAY, 7, SYSUTCDATETIME()),  -- retención de 7 días — ver justificación en docs/security §16.10

            CONSTRAINT pk_wallet_idempotencia
                PRIMARY KEY CLUSTERED (id_idempotencia),

            CONSTRAINT uq_wallet_idempotencia_usuario_endpoint_key
                UNIQUE (id_usuario, endpoint, idempotency_key),

            CONSTRAINT ck_wallet_idempotencia_estado
                CHECK (estado IN ('EN_PROCESO', 'COMPLETADA'))
        );

        SET @objId = OBJECT_ID('dbo.wallet_idempotencia', 'U');
        PRINT 'OK: tabla dbo.wallet_idempotencia creada';
    END
    ELSE
    BEGIN
        -- ── La tabla ya existe: verificar, nunca reparar en silencio ─────
        PRINT 'INFO: dbo.wallet_idempotencia ya existe — verificando estructura crítica...';

        -- 1) Existencia de las 13 columnas requeridas.
        DECLARE @columnasFaltantes NVARCHAR(400);
        SELECT @columnasFaltantes = STRING_AGG(req.col, ', ')
        FROM (VALUES
            ('id_idempotencia'), ('id_usuario'), ('endpoint'), ('idempotency_key'),
            ('request_hash'), ('estado'), ('http_status'), ('id_recurso'),
            ('id_transaccion_ledger'), ('respuesta_data_json'), ('fecha_creacion'),
            ('fecha_completado'), ('fecha_expiracion')
        ) AS req(col)
        WHERE NOT EXISTS (
            SELECT 1 FROM sys.columns c WHERE c.object_id = @objId AND c.name = req.col
        );

        IF @columnasFaltantes IS NOT NULL
        BEGIN
            DECLARE @msgCol NVARCHAR(600) =
                N'Migración 029 abortada: dbo.wallet_idempotencia ya existe pero le faltan columnas requeridas (' + @columnasFaltantes + N'). No se agregan columnas automáticamente — revisar manualmente.';
            THROW 51000, @msgCol, 1;
        END

        -- 2) Tipos/tamaños críticos — solo las columnas donde un tipo
        -- incorrecto rompería silenciosamente la lógica de la aplicación
        -- (idempotency_key como GUID, request_hash como 32 bytes exactos,
        -- límites de longitud que la app ya asume, precisión de fecha).
        DECLARE @Checks TABLE (columna SYSNAME NOT NULL, ok BIT NOT NULL, esperado NVARCHAR(60) NOT NULL);

        INSERT INTO @Checks
        SELECT 'id_usuario', CASE WHEN ty.name = 'bigint' THEN 1 ELSE 0 END, 'bigint'
        FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objId AND c.name = 'id_usuario'
        UNION ALL
        SELECT 'endpoint', CASE WHEN ty.name = 'varchar' AND c.max_length = 60 THEN 1 ELSE 0 END, 'varchar(60)'
        FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objId AND c.name = 'endpoint'
        UNION ALL
        SELECT 'idempotency_key', CASE WHEN ty.name = 'uniqueidentifier' THEN 1 ELSE 0 END, 'uniqueidentifier'
        FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objId AND c.name = 'idempotency_key'
        UNION ALL
        SELECT 'request_hash', CASE WHEN ty.name = 'varbinary' AND c.max_length = 32 THEN 1 ELSE 0 END, 'varbinary(32)'
        FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objId AND c.name = 'request_hash'
        UNION ALL
        SELECT 'estado', CASE WHEN ty.name = 'varchar' AND c.max_length = 12 THEN 1 ELSE 0 END, 'varchar(12)'
        FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objId AND c.name = 'estado'
        UNION ALL
        SELECT 'respuesta_data_json', CASE WHEN ty.name = 'nvarchar' AND c.max_length = 2000 THEN 1 ELSE 0 END, 'nvarchar(1000)'
        FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objId AND c.name = 'respuesta_data_json'
        UNION ALL
        SELECT 'fecha_creacion', CASE WHEN ty.name = 'datetime2' AND c.scale = 3 THEN 1 ELSE 0 END, 'datetime2(3)'
        FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objId AND c.name = 'fecha_creacion'
        UNION ALL
        SELECT 'fecha_completado', CASE WHEN ty.name = 'datetime2' AND c.scale = 3 THEN 1 ELSE 0 END, 'datetime2(3)'
        FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objId AND c.name = 'fecha_completado'
        UNION ALL
        SELECT 'fecha_expiracion', CASE WHEN ty.name = 'datetime2' AND c.scale = 3 THEN 1 ELSE 0 END, 'datetime2(3)'
        FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE c.object_id = @objId AND c.name = 'fecha_expiracion';

        IF EXISTS (SELECT 1 FROM @Checks WHERE ok = 0)
        BEGIN
            DECLARE @detalleTipos NVARCHAR(1000);
            SELECT @detalleTipos = STRING_AGG(columna + ' (esperado ' + esperado + ')', '; ')
            FROM @Checks WHERE ok = 0;

            DECLARE @msgTipo NVARCHAR(1200) =
                N'Migración 029 abortada: dbo.wallet_idempotencia ya existe pero columnas críticas no coinciden en tipo/tamaño: ' + @detalleTipos + N'. No se modifican columnas automáticamente — revisar manualmente.';
            THROW 51001, @msgTipo, 1;
        END

        -- 3) Objetos esenciales: PK, UNIQUE, CHECK. La restricción UNIQUE es
        -- el mecanismo real de arbitraje de concurrencia de Idempotency-Key
        -- (§16.2 del documento de seguridad) — su ausencia deja el diseño de
        -- idempotencia sin efecto real, por eso aborta en vez de continuar.
        IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = @objId AND type = 'PK')
            THROW 51002, N'Migración 029 abortada: dbo.wallet_idempotencia existe pero no tiene clave primaria. No se crea automáticamente — revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = @objId AND type = 'UQ' AND name = 'uq_wallet_idempotencia_usuario_endpoint_key')
            THROW 51003, N'Migración 029 abortada: dbo.wallet_idempotencia existe pero falta la restricción UNIQUE uq_wallet_idempotencia_usuario_endpoint_key — es el mecanismo real de arbitraje de concurrencia de Idempotency-Key, no un candado aplicativo. No se crea automáticamente sobre una tabla preexistente — revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @objId AND name = 'ck_wallet_idempotencia_estado')
            THROW 51004, N'Migración 029 abortada: dbo.wallet_idempotencia existe pero falta el CHECK ck_wallet_idempotencia_estado. No se crea automáticamente — revisar manualmente.', 1;

        PRINT 'OK: estructura crítica de dbo.wallet_idempotencia verificada — columnas, tipos, PK, UNIQUE y CHECK correctos';
    END

    -- ── Índice de limpieza/retención ──────────────────────────────────────
    -- Se llega aquí solo si la tabla se acaba de crear completa, o si ya
    -- existía y superó TODAS las verificaciones estructurales anteriores
    -- (de lo contrario ya se abortó con THROW). Filtrado por COMPLETADA, no
    -- por EN_PROCESO: bajo el diseño de una sola transacción, EN_PROCESO no
    -- queda persistido en operación normal — un índice filtrado por ese
    -- estado no tendría filas que indexar en el caso esperado.
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objId AND name = 'ix_wallet_idempotencia_expiracion')
        CREATE INDEX ix_wallet_idempotencia_expiracion
            ON dbo.wallet_idempotencia (fecha_expiracion)
            WHERE estado = 'COMPLETADA';

    PRINT 'OK: índice de expiración verificado/creado';

    COMMIT TRANSACTION;
    PRINT '=== FIN MIGRACIÓN 029 (COMMIT OK) ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '=== ERROR EN MIGRACIÓN 029 — ROLLBACK EJECUTADO ===';
    THROW;
END CATCH;

-- ── Verificación final ───────────────────────────────────────────────────
-- No se limita a listar objetos: calcula banderas booleanas independientes
-- y un resultado explícito. Si el bloque anterior completó sin error, todas
-- las banderas deben ser 1 — esta consulta es una confirmación legible para
-- el humano que ejecuta el script, no el mecanismo de aplicación de la
-- política (eso ya ocurrió vía THROW dentro de la transacción).

DECLARE @objIdFinal INT = OBJECT_ID('dbo.wallet_idempotencia', 'U');

SELECT
    CASE WHEN @objIdFinal IS NOT NULL THEN 1 ELSE 0 END AS tabla_ok,
    CASE WHEN (
        SELECT COUNT(*) FROM sys.columns
        WHERE object_id = @objIdFinal AND name IN (
            'id_idempotencia','id_usuario','endpoint','idempotency_key','request_hash','estado',
            'http_status','id_recurso','id_transaccion_ledger','respuesta_data_json',
            'fecha_creacion','fecha_completado','fecha_expiracion')
    ) = 13 THEN 1 ELSE 0 END AS columnas_ok,
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.key_constraints
        WHERE parent_object_id = @objIdFinal AND type = 'UQ' AND name = 'uq_wallet_idempotencia_usuario_endpoint_key'
    ) THEN 1 ELSE 0 END AS unique_ok,
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = @objIdFinal AND name = 'ck_wallet_idempotencia_estado'
    ) THEN 1 ELSE 0 END AS check_ok,
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = @objIdFinal AND name = 'ix_wallet_idempotencia_expiracion'
    ) THEN 1 ELSE 0 END AS indice_ok,
    CASE WHEN
        @objIdFinal IS NOT NULL
        AND (SELECT COUNT(*) FROM sys.columns WHERE object_id = @objIdFinal AND name IN (
                'id_idempotencia','id_usuario','endpoint','idempotency_key','request_hash','estado',
                'http_status','id_recurso','id_transaccion_ledger','respuesta_data_json',
                'fecha_creacion','fecha_completado','fecha_expiracion')) = 13
        AND EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = @objIdFinal AND type = 'UQ' AND name = 'uq_wallet_idempotencia_usuario_endpoint_key')
        AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @objIdFinal AND name = 'ck_wallet_idempotencia_estado')
        AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = @objIdFinal AND name = 'ix_wallet_idempotencia_expiracion')
    THEN N'OK — estructura completa verificada'
    ELSE N'FALLO — no debería llegar aquí si el bloque TRY completó sin error; revisar manualmente'
    END AS resultado;

SELECT c.name AS columna, ty.name AS tipo, c.max_length, c.scale, c.is_nullable
FROM sys.columns c
JOIN sys.tables t ON t.object_id = c.object_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE t.object_id = @objIdFinal
ORDER BY c.column_id;

PRINT '=== VERIFICACIÓN COMPLETA ===';
