-- Permiso de producto del piloto: no es aprobación de crédito, cupo ni consentimiento.
-- Aditiva, sin backfill ni clientes habilitados por el despliegue de la tabla.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF OBJECT_ID('dbo.usuarios','U') IS NULL OR OBJECT_ID('dbo.personas','U') IS NULL OR OBJECT_ID('dbo.wallets','U') IS NULL
    THROW 59590, N'Migracion059: faltan tablas base.', 1;
GO
IF OBJECT_ID('dbo.cartera_piloto_habilitaciones','U') IS NULL
BEGIN
    CREATE TABLE dbo.cartera_piloto_habilitaciones (
        id_usuario BIGINT NOT NULL,
        id_persona BIGINT NOT NULL,
        id_wallet BIGINT NOT NULL,
        fecha_habilitacion DATETIME2 NOT NULL,
        fuente VARCHAR(20) NOT NULL,
        CONSTRAINT pk_cartera_piloto_habilitaciones PRIMARY KEY (id_usuario),
        CONSTRAINT fk_cartera_piloto_usuario FOREIGN KEY (id_usuario) REFERENCES dbo.usuarios(id_usuario),
        CONSTRAINT fk_cartera_piloto_persona FOREIGN KEY (id_persona) REFERENCES dbo.personas(id_persona),
        CONSTRAINT fk_cartera_piloto_wallet FOREIGN KEY (id_wallet) REFERENCES dbo.wallets(id_wallet),
        CONSTRAINT ck_cartera_piloto_fuente CHECK (fuente = 'PUBLICA' OR fuente = 'LEGACY')
    );
    CREATE UNIQUE INDEX ux_cartera_piloto_persona ON dbo.cartera_piloto_habilitaciones(id_persona);
    CREATE UNIQUE INDEX ux_cartera_piloto_wallet ON dbo.cartera_piloto_habilitaciones(id_wallet);
END;
GO
DECLARE @tabla INT=OBJECT_ID('dbo.cartera_piloto_habilitaciones');
DECLARE @columnas TABLE(nombre SYSNAME,tipo SYSNAME,longitud SMALLINT);
INSERT INTO @columnas VALUES ('id_usuario','bigint',8),('id_persona','bigint',8),('id_wallet','bigint',8),
    ('fecha_habilitacion','datetime2',8),('fuente','varchar',20);
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=@tabla)<>5
 OR EXISTS(SELECT 1 FROM @columnas e WHERE NOT EXISTS(
    SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=@tabla AND c.name=e.nombre AND t.name=e.tipo AND t.is_user_defined=0
      AND c.max_length=e.longitud AND c.is_nullable=0 AND c.is_identity=0 AND c.is_computed=0
      AND c.default_object_id=0 AND (t.name<>'datetime2' OR c.scale=7)))
    THROW 59591, N'Migracion059: columnas incompatibles.', 1;
DECLARE @fks TABLE(nombre SYSNAME,columna SYSNAME,tabla SYSNAME,referencia SYSNAME);
INSERT INTO @fks VALUES ('fk_cartera_piloto_usuario','id_usuario','usuarios','id_usuario'),
    ('fk_cartera_piloto_persona','id_persona','personas','id_persona'),('fk_cartera_piloto_wallet','id_wallet','wallets','id_wallet');
IF EXISTS(SELECT 1 FROM @fks e WHERE NOT EXISTS(
    SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c ON c.constraint_object_id=f.object_id
    WHERE f.parent_object_id=@tabla AND f.name=e.nombre AND f.referenced_object_id=OBJECT_ID('dbo.'+e.tabla)
      AND f.is_disabled=0 AND f.is_not_trusted=0 AND f.delete_referential_action=0 AND f.update_referential_action=0
      AND c.parent_column_id=COLUMNPROPERTY(@tabla,e.columna,'ColumnId')
      AND c.referenced_column_id=COLUMNPROPERTY(OBJECT_ID('dbo.'+e.tabla),e.referencia,'ColumnId')
      AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1))
    THROW 59592, N'Migracion059: claves foraneas incompatibles.', 1;
DECLARE @indices TABLE(nombre SYSNAME,columna SYSNAME,primaria BIT);
INSERT INTO @indices VALUES ('pk_cartera_piloto_habilitaciones','id_usuario',1),
    ('ux_cartera_piloto_persona','id_persona',0),('ux_cartera_piloto_wallet','id_wallet',0);
IF EXISTS(SELECT 1 FROM @indices e WHERE NOT EXISTS(
    SELECT 1 FROM sys.indexes i JOIN sys.index_columns c ON c.object_id=i.object_id AND c.index_id=i.index_id
    WHERE i.object_id=@tabla AND i.name=e.nombre AND i.is_primary_key=e.primaria AND i.is_unique=1
      AND i.is_disabled=0 AND i.has_filter=0 AND i.is_hypothetical=0
      AND c.key_ordinal=1 AND c.is_included_column=0
      AND c.column_id=COLUMNPROPERTY(@tabla,e.columna,'ColumnId')
      AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id=i.object_id AND x.index_id=i.index_id)=1))
    THROW 59593, N'Migracion059: indices incompatibles.', 1;
DECLARE @definicion NVARCHAR(MAX)=(SELECT definition FROM sys.check_constraints
    WHERE parent_object_id=@tabla AND name='ck_cartera_piloto_fuente' AND is_disabled=0 AND is_not_trusted=0);
SET @definicion=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(UPPER(@definicion),
    N'[',N''),N']',N''),N'(',N''),N')',N''),N' ',N''),CHAR(13),N''),CHAR(10),N'');
IF @definicion IS NULL OR @definicion NOT IN (N'FUENTE=''PUBLICA''ORFUENTE=''LEGACY''',N'FUENTE=''LEGACY''ORFUENTE=''PUBLICA''')
    THROW 59594, N'Migracion059: comprobacion de fuente incompatible.', 1;
GO
