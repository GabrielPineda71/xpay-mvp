-- XPAY P0-B - Migracion 048: usuario_tokens (tokens de un solo uso).
-- Propositos: VERIFICAR_EMAIL y RECUPERAR_CLAVE.
-- Solo se guarda el SHA-256 del token; nunca el token en claro.
-- Idempotente: crea solo lo que falta y verifica lo existente; ante
-- cualquier incompatibilidad aborta con THROW (no corrige nada).
-- No crea unicidad sobre personas.email.
-- No modifica 045/046/047. No se aplica en este ticket a QA ni a PROD.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

PRINT '=== INICIO MIGRACION 048 ===';
IF OBJECT_ID('dbo.usuarios', 'U') IS NULL
    THROW 59480, N'Migracion 048 abortada: falta dbo.usuarios.', 1;
IF COLUMNPROPERTY(OBJECT_ID('dbo.usuarios'), 'id_usuario', 'ColumnId') IS NULL
    THROW 59480, N'Migracion 048 abortada: falta dbo.usuarios.id_usuario.', 1;
GO

IF OBJECT_ID('dbo.usuario_tokens', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.usuario_tokens (
        id_token BIGINT IDENTITY(1,1) NOT NULL,
        id_usuario BIGINT NOT NULL,
        proposito VARCHAR(30) NOT NULL,
        token_hash BINARY(32) NOT NULL,
        email_destino VARCHAR(200) NOT NULL,
        fecha_creacion DATETIME2 NOT NULL
            CONSTRAINT df_ut_fecha_creacion DEFAULT SYSUTCDATETIME(),
        fecha_expiracion DATETIME2 NOT NULL,
        fecha_uso DATETIME2 NULL,
        fecha_invalidacion DATETIME2 NULL,
        ip_solicitud VARCHAR(100) NULL,
        CONSTRAINT pk_usuario_tokens PRIMARY KEY (id_token),
        CONSTRAINT fk_ut_usuario FOREIGN KEY (id_usuario)
            REFERENCES dbo.usuarios(id_usuario),
        CONSTRAINT ck_ut_proposito
            CHECK (proposito IN ('VERIFICAR_EMAIL', 'RECUPERAR_CLAVE')),
        CONSTRAINT ck_ut_expiracion
            CHECK (fecha_expiracion > fecha_creacion)
    );
    PRINT 'usuario_tokens creada';
END
ELSE
    PRINT 'usuario_tokens ya existe';
GO

-- 1. Columnas: exactamente las 10 esperadas, con tipo, longitud en bytes,
--    escala (datetime2) y nulabilidad exactas.
IF (SELECT COUNT(*) FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.usuario_tokens')) <> 10
    THROW 59481, N'Migracion 048 abortada: usuario_tokens no tiene exactamente 10 columnas.', 1;
IF (SELECT COUNT(*)
    FROM (VALUES
        ('id_token',           'bigint',    8,   0),
        ('id_usuario',         'bigint',    8,   0),
        ('proposito',          'varchar',   30,  0),
        ('token_hash',         'binary',    32,  0),
        ('email_destino',      'varchar',   200, 0),
        ('fecha_creacion',     'datetime2', 8,   0),
        ('fecha_expiracion',   'datetime2', 8,   0),
        ('fecha_uso',          'datetime2', 8,   1),
        ('fecha_invalidacion', 'datetime2', 8,   1),
        ('ip_solicitud',       'varchar',   100, 1)
    ) AS e(nombre, tipo, longitud, nulos)
    JOIN sys.columns c
      ON c.object_id = OBJECT_ID('dbo.usuario_tokens')
     AND c.name = e.nombre
    JOIN sys.types t
      ON t.user_type_id = c.user_type_id
    WHERE t.name = e.tipo
      AND c.max_length = e.longitud
      AND c.is_nullable = e.nulos
      AND (e.tipo <> 'datetime2' OR c.scale = 7)) <> 10
    THROW 59481, N'Migracion 048 abortada: columnas de usuario_tokens incompatibles.', 1;
IF ISNULL(COLUMNPROPERTY(OBJECT_ID('dbo.usuario_tokens'), 'id_token', 'IsIdentity'), 0) <> 1
    THROW 59481, N'Migracion 048 abortada: id_token no es IDENTITY.', 1;
GO

-- 2. PK: pk_usuario_tokens, una sola columna clave y es id_token.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID('dbo.usuario_tokens')
      AND i.name = 'pk_usuario_tokens'
      AND i.is_primary_key = 1)
    THROW 59481, N'Migracion 048 abortada: falta pk_usuario_tokens.', 1;
IF (SELECT COUNT(*) FROM sys.indexes i
    JOIN sys.index_columns ic
      ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    WHERE i.object_id = OBJECT_ID('dbo.usuario_tokens')
      AND i.name = 'pk_usuario_tokens'
      AND ic.is_included_column = 0) <> 1
 OR NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    JOIN sys.index_columns ic
      ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    WHERE i.object_id = OBJECT_ID('dbo.usuario_tokens')
      AND i.name = 'pk_usuario_tokens'
      AND ic.key_ordinal = 1
      AND ic.column_id = COLUMNPROPERTY(OBJECT_ID('dbo.usuario_tokens'), 'id_token', 'ColumnId'))
    THROW 59481, N'Migracion 048 abortada: pk_usuario_tokens no es (id_token).', 1;
GO

-- 3. FK: fk_ut_usuario, una sola columna, usuario_tokens.id_usuario ->
--    usuarios.id_usuario, habilitada y confiable.
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys fk
    WHERE fk.name = 'fk_ut_usuario'
      AND fk.parent_object_id = OBJECT_ID('dbo.usuario_tokens')
      AND fk.referenced_object_id = OBJECT_ID('dbo.usuarios')
      AND fk.is_disabled = 0
      AND fk.is_not_trusted = 0)
    THROW 59481, N'Migracion 048 abortada: falta fk_ut_usuario hacia dbo.usuarios.', 1;
IF (SELECT COUNT(*) FROM sys.foreign_key_columns fkc
    JOIN sys.foreign_keys fk ON fk.object_id = fkc.constraint_object_id
    WHERE fk.name = 'fk_ut_usuario'
      AND fk.parent_object_id = OBJECT_ID('dbo.usuario_tokens')) <> 1
 OR NOT EXISTS (
    SELECT 1 FROM sys.foreign_key_columns fkc
    JOIN sys.foreign_keys fk ON fk.object_id = fkc.constraint_object_id
    WHERE fk.name = 'fk_ut_usuario'
      AND fk.parent_object_id = OBJECT_ID('dbo.usuario_tokens')
      AND fkc.parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.usuario_tokens'), 'id_usuario', 'ColumnId')
      AND fkc.referenced_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.usuarios'), 'id_usuario', 'ColumnId'))
    THROW 59481, N'Migracion 048 abortada: fk_ut_usuario no es id_usuario -> usuarios.id_usuario.', 1;
GO

-- 4. CHECKs: existentes, habilitados, confiables y con la condicion esperada.
--    CHARINDEX (sin comodines) sobre la definicion normalizada por SQL Server.
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints cc
    WHERE cc.name = 'ck_ut_proposito'
      AND cc.parent_object_id = OBJECT_ID('dbo.usuario_tokens')
      AND cc.is_disabled = 0
      AND cc.is_not_trusted = 0
      AND CHARINDEX(N'''VERIFICAR_EMAIL''', cc.definition) > 0
      AND CHARINDEX(N'''RECUPERAR_CLAVE''', cc.definition) > 0)
    THROW 59481, N'Migracion 048 abortada: ck_ut_proposito ausente o incompatible.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints cc
    WHERE cc.name = 'ck_ut_expiracion'
      AND cc.parent_object_id = OBJECT_ID('dbo.usuario_tokens')
      AND cc.is_disabled = 0
      AND cc.is_not_trusted = 0
      AND CHARINDEX(N'[fecha_expiracion]>[fecha_creacion]', cc.definition) > 0)
    THROW 59481, N'Migracion 048 abortada: ck_ut_expiracion ausente o incompatible.', 1;
GO

-- 5. Indices: se crean si faltan.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'uq_ut_token_hash'
      AND object_id = OBJECT_ID('dbo.usuario_tokens'))
    CREATE UNIQUE INDEX uq_ut_token_hash
        ON dbo.usuario_tokens(token_hash);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'ix_ut_usuario_proposito'
      AND object_id = OBJECT_ID('dbo.usuario_tokens'))
    CREATE INDEX ix_ut_usuario_proposito
        ON dbo.usuario_tokens(id_usuario, proposito)
        INCLUDE (fecha_uso, fecha_invalidacion, fecha_expiracion);
GO

-- 6. Indices: verificacion de definicion (nuevos o preexistentes).
--    uq_ut_token_hash: UNIQUE, sin filtro, clave exactamente (token_hash),
--    sin columnas incluidas.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID('dbo.usuario_tokens')
      AND i.name = 'uq_ut_token_hash'
      AND i.is_unique = 1
      AND i.has_filter = 0
      AND i.is_disabled = 0)
 OR (SELECT COUNT(*) FROM sys.indexes i
     JOIN sys.index_columns ic
       ON ic.object_id = i.object_id AND ic.index_id = i.index_id
     WHERE i.object_id = OBJECT_ID('dbo.usuario_tokens')
       AND i.name = 'uq_ut_token_hash') <> 1
 OR NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    JOIN sys.index_columns ic
      ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    WHERE i.object_id = OBJECT_ID('dbo.usuario_tokens')
      AND i.name = 'uq_ut_token_hash'
      AND ic.key_ordinal = 1
      AND ic.is_included_column = 0
      AND ic.column_id = COLUMNPROPERTY(OBJECT_ID('dbo.usuario_tokens'), 'token_hash', 'ColumnId'))
    THROW 59482, N'Migracion 048 abortada: uq_ut_token_hash incompatible.', 1;

--    ix_ut_usuario_proposito: no unico, sin filtro, clave exactamente
--    (id_usuario, proposito) en ese orden e INCLUDE exactamente
--    (fecha_uso, fecha_invalidacion, fecha_expiracion).
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID('dbo.usuario_tokens')
      AND i.name = 'ix_ut_usuario_proposito'
      AND i.is_unique = 0
      AND i.has_filter = 0
      AND i.is_disabled = 0)
 OR (SELECT COUNT(*)
     FROM (VALUES
         ('id_usuario',         1, 0),
         ('proposito',          2, 0),
         ('fecha_uso',          0, 1),
         ('fecha_invalidacion', 0, 1),
         ('fecha_expiracion',   0, 1)
     ) AS e(columna, ordinal, incluida)
     JOIN sys.indexes i
       ON i.object_id = OBJECT_ID('dbo.usuario_tokens')
      AND i.name = 'ix_ut_usuario_proposito'
     JOIN sys.index_columns ic
       ON ic.object_id = i.object_id AND ic.index_id = i.index_id
      AND ic.column_id = COLUMNPROPERTY(OBJECT_ID('dbo.usuario_tokens'), e.columna, 'ColumnId')
     WHERE ic.key_ordinal = e.ordinal
       AND ic.is_included_column = e.incluida) <> 5
 OR (SELECT COUNT(*) FROM sys.indexes i
     JOIN sys.index_columns ic
       ON ic.object_id = i.object_id AND ic.index_id = i.index_id
     WHERE i.object_id = OBJECT_ID('dbo.usuario_tokens')
       AND i.name = 'ix_ut_usuario_proposito') <> 5
    THROW 59482, N'Migracion 048 abortada: ix_ut_usuario_proposito incompatible.', 1;
GO

PRINT '=== FIN MIGRACION 048 ===';
GO
