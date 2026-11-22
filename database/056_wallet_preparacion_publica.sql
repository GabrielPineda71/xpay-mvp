-- Preparación y evidencia durable del cierre solicitado por el titular.
-- Aditiva, sin backfill: no crea Wallets, saldos, cupos ni movimientos.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF OBJECT_ID('dbo.usuarios','U') IS NULL OR OBJECT_ID('dbo.personas','U') IS NULL
   OR OBJECT_ID('dbo.wallets','U') IS NULL
    THROW 59560, N'Migracion 056 abortada: faltan tablas base.', 1;
GO
IF OBJECT_ID('dbo.wallet_preparaciones','U') IS NULL
BEGIN
    CREATE TABLE dbo.wallet_preparaciones (
        id_usuario BIGINT NOT NULL,
        id_persona BIGINT NOT NULL,
        opcion_llave VARCHAR(20) NOT NULL,
        llave_tipo VARCHAR(10) NULL,
        llave_hash VARCHAR(64) NULL,
        llave_masked NVARCHAR(128) NULL,
        propiedad_confirmada BIT NOT NULL,
        id_wallet BIGINT NULL,
        fecha_activacion DATETIME2 NULL,
        criterios_version VARCHAR(40) NULL,
        condiciones_version VARCHAR(20) NULL,
        condiciones_sha256 VARCHAR(64) NULL,
        fecha_creacion DATETIME2 NOT NULL,
        fecha_actualizacion DATETIME2 NOT NULL,
        CONSTRAINT pk_wallet_preparaciones PRIMARY KEY(id_usuario),
        CONSTRAINT fk_wallet_preparaciones_usuario FOREIGN KEY(id_usuario) REFERENCES dbo.usuarios(id_usuario),
        CONSTRAINT fk_wallet_preparaciones_persona FOREIGN KEY(id_persona) REFERENCES dbo.personas(id_persona),
        CONSTRAINT fk_wallet_preparaciones_wallet FOREIGN KEY(id_wallet) REFERENCES dbo.wallets(id_wallet),
        CONSTRAINT ck_wallet_preparaciones_llave CHECK (
            (opcion_llave IN ('PENDIENTE','OMITIDA') AND llave_tipo IS NULL AND llave_hash IS NULL
                AND llave_masked IS NULL AND propiedad_confirmada=0)
            OR (opcion_llave='REGISTRADA' AND llave_tipo IN ('ID','PHONE','EMAIL','ALPHA','BCODE')
                AND llave_hash IS NOT NULL AND LEN(llave_hash)=64 AND llave_hash NOT LIKE '%[^0-9a-f]%'
                AND llave_masked IS NOT NULL AND LEN(llave_masked)>0 AND propiedad_confirmada=1)),
        CONSTRAINT ck_wallet_preparaciones_activacion CHECK (
            (id_wallet IS NULL AND fecha_activacion IS NULL AND criterios_version IS NULL
                AND condiciones_version IS NULL AND condiciones_sha256 IS NULL)
            OR (id_wallet IS NOT NULL AND fecha_activacion IS NOT NULL AND criterios_version IS NOT NULL
                AND LEN(criterios_version)>0 AND condiciones_version IS NOT NULL AND LEN(condiciones_version)>0
                AND condiciones_sha256 IS NOT NULL AND LEN(condiciones_sha256)=64
                AND condiciones_sha256 NOT LIKE '%[^0-9a-f]%' AND opcion_llave IN ('REGISTRADA','OMITIDA')))
    );
    CREATE UNIQUE INDEX ux_wallet_preparaciones_wallet ON dbo.wallet_preparaciones(id_wallet) WHERE id_wallet IS NOT NULL;
END;
GO
DECLARE @columnas TABLE(nombre SYSNAME, tipo SYSNAME, longitud SMALLINT, nullable BIT);
INSERT INTO @columnas VALUES
('id_usuario','bigint',8,0),('id_persona','bigint',8,0),('opcion_llave','varchar',20,0),
('llave_tipo','varchar',10,1),('llave_hash','varchar',64,1),('llave_masked','nvarchar',256,1),
('propiedad_confirmada','bit',1,0),('id_wallet','bigint',8,1),('fecha_activacion','datetime2',8,1),
('criterios_version','varchar',40,1),('condiciones_version','varchar',20,1),('condiciones_sha256','varchar',64,1),
('fecha_creacion','datetime2',8,0),('fecha_actualizacion','datetime2',8,0);
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.wallet_preparaciones'))<>14
 OR EXISTS(SELECT 1 FROM @columnas e WHERE NOT EXISTS(
    SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id
    WHERE c.object_id=OBJECT_ID('dbo.wallet_preparaciones') AND c.name=e.nombre AND t.name=e.tipo
        AND t.is_user_defined=0 AND c.max_length=e.longitud AND c.is_nullable=e.nullable
        AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0
        AND (t.name<>'datetime2' OR c.scale=7)))
    THROW 59561, N'Migracion 056 abortada: columnas incompatibles.', 1;
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('dbo.wallet_preparaciones')
    AND name IN ('fk_wallet_preparaciones_usuario','fk_wallet_preparaciones_persona','fk_wallet_preparaciones_wallet')
    AND is_disabled=0 AND is_not_trusted=0 AND delete_referential_action=0 AND update_referential_action=0)<>3
    THROW 59562, N'Migracion 056 abortada: claves foraneas incompatibles.', 1;
DECLARE @fks TABLE(nombre SYSNAME,columna SYSNAME,tabla SYSNAME,referencia SYSNAME);
INSERT INTO @fks VALUES
('fk_wallet_preparaciones_usuario','id_usuario','usuarios','id_usuario'),
('fk_wallet_preparaciones_persona','id_persona','personas','id_persona'),
('fk_wallet_preparaciones_wallet','id_wallet','wallets','id_wallet');
IF EXISTS(SELECT 1 FROM @fks e WHERE NOT EXISTS(
    SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c ON c.constraint_object_id=f.object_id
    WHERE f.parent_object_id=OBJECT_ID('dbo.wallet_preparaciones') AND f.name=e.nombre
        AND f.referenced_object_id=OBJECT_ID('dbo.'+e.tabla)
        AND c.parent_column_id=COLUMNPROPERTY(OBJECT_ID('dbo.wallet_preparaciones'),e.columna,'ColumnId')
        AND c.referenced_column_id=COLUMNPROPERTY(OBJECT_ID('dbo.'+e.tabla),e.referencia,'ColumnId')
        AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1))
    THROW 59562, N'Migracion 056 abortada: destinos de claves foraneas incompatibles.', 1;
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.wallet_preparaciones')
    AND name IN ('ck_wallet_preparaciones_llave','ck_wallet_preparaciones_activacion') AND is_disabled=0 AND is_not_trusted=0)<>2
    THROW 59563, N'Migracion 056 abortada: comprobaciones incompatibles.', 1;
-- Huellas de la definición que genera SQL Server para estos CHECKs exactos:
-- no basta que una tabla anterior reutilice los mismos nombres.
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.wallet_preparaciones')
    AND name='ck_wallet_preparaciones_llave'
    AND CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',definition),2)='792BFDEA7AA82CE24185476CF5580295C1F1A1CB8AFE7037D41D72E68A150544')
 OR NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.wallet_preparaciones')
    AND name='ck_wallet_preparaciones_activacion'
    AND CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',definition),2)='35A729AD3ACE38EA42D65B6B5389993D9367420C77C9B85B2C52B33EBACFC6A5')
    THROW 59563, N'Migracion 056 abortada: definicion de comprobaciones incompatible.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes i JOIN sys.index_columns c ON c.object_id=i.object_id AND c.index_id=i.index_id
    WHERE i.object_id=OBJECT_ID('dbo.wallet_preparaciones') AND i.is_primary_key=1 AND i.is_unique=1 AND i.is_disabled=0
        AND c.key_ordinal=1 AND c.is_included_column=0
        AND c.column_id=COLUMNPROPERTY(OBJECT_ID('dbo.wallet_preparaciones'),'id_usuario','ColumnId')
        AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id=i.object_id AND x.index_id=i.index_id)=1)
    THROW 59565, N'Migracion 056 abortada: clave primaria incompatible.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes i JOIN sys.index_columns c ON c.object_id=i.object_id AND c.index_id=i.index_id
    WHERE i.object_id=OBJECT_ID('dbo.wallet_preparaciones') AND i.name='ux_wallet_preparaciones_wallet'
        AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=1 AND c.key_ordinal=1 AND c.is_included_column=0
        AND c.column_id=COLUMNPROPERTY(OBJECT_ID('dbo.wallet_preparaciones'),'id_wallet','ColumnId')
        AND REPLACE(REPLACE(REPLACE(REPLACE(i.filter_definition,'[',''),']',''),'(',''),')','')='id_wallet IS NOT NULL'
        AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id=i.object_id AND x.index_id=i.index_id)=1)
    THROW 59564, N'Migracion 056 abortada: indice de Wallet incompatible.', 1;
GO
