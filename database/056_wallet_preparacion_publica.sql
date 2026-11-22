-- Activación expresa y prueba de las aceptaciones A/B; la llave propia
-- se registra después, únicamente como requisito de retiro al banco.
-- Aditiva y sin backfill: este script no crea Wallets, dinero ni aceptaciones.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF OBJECT_ID('dbo.usuarios','U') IS NULL OR OBJECT_ID('dbo.personas','U') IS NULL
    OR OBJECT_ID('dbo.wallets','U') IS NULL
    THROW 59560, N'Migracion056: faltan tablas base.', 1;
GO
IF OBJECT_ID('dbo.wallet_preparaciones','U') IS NULL
BEGIN
    CREATE TABLE dbo.wallet_preparaciones (
        id_usuario BIGINT NOT NULL,
        id_persona BIGINT NOT NULL,
        id_wallet BIGINT NOT NULL,
        fecha_activacion DATETIME2 NOT NULL,
        criterios_version VARCHAR(40) NOT NULL,
        terminos_version VARCHAR(20) NOT NULL,
        terminos_sha256 VARCHAR(64) NOT NULL,
        politica_datos_version VARCHAR(20) NOT NULL,
        politica_datos_sha256 VARCHAR(64) NOT NULL,
        fecha_creacion DATETIME2 NOT NULL,
        fecha_actualizacion DATETIME2 NOT NULL,
        CONSTRAINT pk_wallet_preparaciones PRIMARY KEY(id_usuario),
        CONSTRAINT fk_wallet_preparaciones_usuario FOREIGN KEY(id_usuario) REFERENCES dbo.usuarios(id_usuario),
        CONSTRAINT fk_wallet_preparaciones_persona FOREIGN KEY(id_persona) REFERENCES dbo.personas(id_persona),
        CONSTRAINT fk_wallet_preparaciones_wallet FOREIGN KEY(id_wallet) REFERENCES dbo.wallets(id_wallet),
        CONSTRAINT ck_wallet_preparaciones_documentos CHECK (criterios_version='WALLET_PUBLICA_V2'
            AND LEN(terminos_version)>0 AND LEN(politica_datos_version)>0
            AND LEN(terminos_sha256)=64 AND terminos_sha256 NOT LIKE '%[^0-9a-f]%'
            AND LEN(politica_datos_sha256)=64 AND politica_datos_sha256 NOT LIKE '%[^0-9a-f]%')
    );
    CREATE UNIQUE INDEX ux_wallet_preparaciones_wallet ON dbo.wallet_preparaciones(id_wallet);
END;
GO
DECLARE @columnas TABLE(nombre SYSNAME,tipo SYSNAME,longitud SMALLINT);
INSERT INTO @columnas VALUES ('id_usuario','bigint',8),('id_persona','bigint',8),('id_wallet','bigint',8),
('fecha_activacion','datetime2',8),('criterios_version','varchar',40),('terminos_version','varchar',20),
('terminos_sha256','varchar',64),('politica_datos_version','varchar',20),('politica_datos_sha256','varchar',64),
('fecha_creacion','datetime2',8),('fecha_actualizacion','datetime2',8);
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.wallet_preparaciones'))<>11
 OR EXISTS(SELECT 1 FROM @columnas e WHERE NOT EXISTS(
    SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=OBJECT_ID('dbo.wallet_preparaciones') AND c.name=e.nombre AND t.name=e.tipo
        AND t.is_user_defined=0 AND c.max_length=e.longitud AND c.is_nullable=0
        AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0 AND (t.name<>'datetime2' OR c.scale=7)))
    THROW 59561, N'Migracion056: columnas incompatibles.', 1;
DECLARE @fks TABLE(nombre SYSNAME,columna SYSNAME,tabla SYSNAME,referencia SYSNAME);
INSERT INTO @fks VALUES
('fk_wallet_preparaciones_usuario','id_usuario','usuarios','id_usuario'),
('fk_wallet_preparaciones_persona','id_persona','personas','id_persona'),
('fk_wallet_preparaciones_wallet','id_wallet','wallets','id_wallet');
IF EXISTS(SELECT 1 FROM @fks e WHERE NOT EXISTS(
    SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c ON c.constraint_object_id=f.object_id
    WHERE f.parent_object_id=OBJECT_ID('dbo.wallet_preparaciones') AND f.name=e.nombre
        AND f.referenced_object_id=OBJECT_ID('dbo.'+e.tabla) AND f.is_disabled=0 AND f.is_not_trusted=0
        AND f.delete_referential_action=0 AND f.update_referential_action=0
        AND c.parent_column_id=COLUMNPROPERTY(OBJECT_ID('dbo.wallet_preparaciones'),e.columna,'ColumnId')
        AND c.referenced_column_id=COLUMNPROPERTY(OBJECT_ID('dbo.'+e.tabla),e.referencia,'ColumnId')
        AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1))
    THROW 59562, N'Migracion056: claves foraneas incompatibles.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.wallet_preparaciones')
    AND name='ck_wallet_preparaciones_documentos' AND is_disabled=0 AND is_not_trusted=0
    AND CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',definition),2)='C5E10F025B51116B7EA269B687E7AAA9E0CD454C052FC6A331EDC2679B9037D5')
    THROW 59563, N'Migracion056: comprobacion incompatible.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes i JOIN sys.index_columns c ON c.object_id=i.object_id AND c.index_id=i.index_id
    WHERE i.object_id=OBJECT_ID('dbo.wallet_preparaciones') AND i.is_primary_key=1 AND i.is_unique=1 AND i.is_disabled=0
        AND c.key_ordinal=1 AND c.is_included_column=0
        AND c.column_id=COLUMNPROPERTY(OBJECT_ID('dbo.wallet_preparaciones'),'id_usuario','ColumnId')
        AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id=i.object_id AND x.index_id=i.index_id)=1)
    THROW 59565, N'Migracion056: clave primaria incompatible.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes i JOIN sys.index_columns c ON c.object_id=i.object_id AND c.index_id=i.index_id
    WHERE i.object_id=OBJECT_ID('dbo.wallet_preparaciones') AND i.name='ux_wallet_preparaciones_wallet'
        AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=0 AND i.type=2
        AND c.key_ordinal=1 AND c.is_included_column=0
        AND c.column_id=COLUMNPROPERTY(OBJECT_ID('dbo.wallet_preparaciones'),'id_wallet','ColumnId')
        AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id=i.object_id AND x.index_id=i.index_id)=1)
    THROW 59564, N'Migracion056: indice de Wallet incompatible.', 1;
GO
