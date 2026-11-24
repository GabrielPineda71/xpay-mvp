-- Permiso MANUAL de producto, independiente del vínculo y de cualquier cupo.
-- Aditiva, sin backfill, sin habilitación automática ni cambios de dinero.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF OBJECT_ID('dbo.usuarios','U') IS NULL OR OBJECT_ID('dbo.wallets','U') IS NULL
 OR OBJECT_ID('dbo.libranza_empleado_usuario','U') IS NULL OR OBJECT_ID('dbo.libranza_empleados','U') IS NULL
 OR OBJECT_ID('dbo.libranza_empresas_convenio','U') IS NULL
 THROW 59570, N'Migracion057: faltan tablas base.', 1;
GO
IF OBJECT_ID('dbo.libranza_autorizaciones_anticipo','U') IS NULL
BEGIN
 CREATE TABLE dbo.libranza_autorizaciones_anticipo (
  id_empleado_usuario BIGINT NOT NULL,
  id_usuario BIGINT NOT NULL,
  id_empleado BIGINT NOT NULL,
  id_convenio BIGINT NOT NULL,
  id_wallet BIGINT NULL,
  estado VARCHAR(20) NOT NULL,
  revision BIGINT NOT NULL,
  huella_contexto VARCHAR(64) NOT NULL,
  creado_por_usuario BIGINT NOT NULL,
  fecha_creacion DATETIME2 NOT NULL,
  modificado_por_usuario BIGINT NOT NULL,
  fecha_cambio DATETIME2 NOT NULL,
  fecha_autorizacion DATETIME2 NULL,
  fecha_revocacion DATETIME2 NULL,
  referencia_revision NVARCHAR(200) NOT NULL,
  CONSTRAINT pk_libranza_autorizaciones_anticipo PRIMARY KEY(id_empleado_usuario,id_usuario),
  CONSTRAINT fk_libranza_aut_anticipo_vinculo FOREIGN KEY(id_empleado_usuario) REFERENCES dbo.libranza_empleado_usuario(id_empleado_usuario),
  CONSTRAINT fk_libranza_aut_anticipo_usuario FOREIGN KEY(id_usuario) REFERENCES dbo.usuarios(id_usuario),
  CONSTRAINT fk_libranza_aut_anticipo_empleado FOREIGN KEY(id_empleado) REFERENCES dbo.libranza_empleados(id_empleado),
  CONSTRAINT fk_libranza_aut_anticipo_convenio FOREIGN KEY(id_convenio) REFERENCES dbo.libranza_empresas_convenio(id_convenio),
  CONSTRAINT fk_libranza_aut_anticipo_wallet FOREIGN KEY(id_wallet) REFERENCES dbo.wallets(id_wallet),
  CONSTRAINT fk_libranza_aut_anticipo_creado FOREIGN KEY(creado_por_usuario) REFERENCES dbo.usuarios(id_usuario),
  CONSTRAINT fk_libranza_aut_anticipo_modificado FOREIGN KEY(modificado_por_usuario) REFERENCES dbo.usuarios(id_usuario),
  CONSTRAINT ck_libranza_aut_anticipo_decision CHECK (
   revision>0 AND LEN(huella_contexto)=64 AND huella_contexto NOT LIKE '%[^0-9a-f]%'
   AND LEN(LTRIM(RTRIM(referencia_revision))) BETWEEN 5 AND 200
   AND ((estado='AUTORIZADO' AND id_wallet IS NOT NULL AND fecha_autorizacion IS NOT NULL AND fecha_revocacion IS NULL)
     OR (estado='REVOCADO' AND fecha_revocacion IS NOT NULL)))
 );
 CREATE INDEX ix_libranza_aut_anticipo_usuario ON dbo.libranza_autorizaciones_anticipo(id_usuario);
END;
GO
DECLARE @columnas TABLE(nombre SYSNAME,tipo SYSNAME,longitud SMALLINT,nulo BIT);
INSERT INTO @columnas VALUES
 ('id_empleado_usuario','bigint',8,0),('id_usuario','bigint',8,0),('id_empleado','bigint',8,0),('id_convenio','bigint',8,0),
 ('id_wallet','bigint',8,1),('estado','varchar',20,0),('revision','bigint',8,0),('huella_contexto','varchar',64,0),
 ('creado_por_usuario','bigint',8,0),('fecha_creacion','datetime2',8,0),('modificado_por_usuario','bigint',8,0),
 ('fecha_cambio','datetime2',8,0),('fecha_autorizacion','datetime2',8,1),('fecha_revocacion','datetime2',8,1),('referencia_revision','nvarchar',400,0);
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.libranza_autorizaciones_anticipo'))<>15
 OR EXISTS(SELECT 1 FROM @columnas e WHERE NOT EXISTS(
  SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
  WHERE c.object_id=OBJECT_ID('dbo.libranza_autorizaciones_anticipo') AND c.name=e.nombre AND t.name=e.tipo
   AND t.is_user_defined=0 AND c.max_length=e.longitud AND c.is_nullable=e.nulo
   AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0 AND (t.name<>'datetime2' OR c.scale=7)))
 THROW 59571, N'Migracion057: columnas incompatibles.', 1;
DECLARE @fks TABLE(nombre SYSNAME,columna SYSNAME,tabla SYSNAME,referencia SYSNAME);
INSERT INTO @fks VALUES
 ('fk_libranza_aut_anticipo_vinculo','id_empleado_usuario','libranza_empleado_usuario','id_empleado_usuario'),
 ('fk_libranza_aut_anticipo_usuario','id_usuario','usuarios','id_usuario'),
 ('fk_libranza_aut_anticipo_empleado','id_empleado','libranza_empleados','id_empleado'),
 ('fk_libranza_aut_anticipo_convenio','id_convenio','libranza_empresas_convenio','id_convenio'),
 ('fk_libranza_aut_anticipo_wallet','id_wallet','wallets','id_wallet'),
 ('fk_libranza_aut_anticipo_creado','creado_por_usuario','usuarios','id_usuario'),
 ('fk_libranza_aut_anticipo_modificado','modificado_por_usuario','usuarios','id_usuario');
IF EXISTS(SELECT 1 FROM @fks e WHERE NOT EXISTS(
 SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c ON c.constraint_object_id=f.object_id
 WHERE f.parent_object_id=OBJECT_ID('dbo.libranza_autorizaciones_anticipo') AND f.name=e.nombre
  AND f.referenced_object_id=OBJECT_ID('dbo.'+e.tabla) AND f.is_disabled=0 AND f.is_not_trusted=0
  AND f.delete_referential_action=0 AND f.update_referential_action=0
  AND c.parent_column_id=COLUMNPROPERTY(OBJECT_ID('dbo.libranza_autorizaciones_anticipo'),e.columna,'ColumnId')
  AND c.referenced_column_id=COLUMNPROPERTY(OBJECT_ID('dbo.'+e.tabla),e.referencia,'ColumnId')
  AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1))
 THROW 59572, N'Migracion057: claves foraneas incompatibles.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID('dbo.libranza_autorizaciones_anticipo')
 AND i.name='pk_libranza_autorizaciones_anticipo' AND i.is_primary_key=1 AND i.is_unique=1 AND i.is_disabled=0 AND i.type=1
 AND (SELECT COUNT(*) FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id)=2
 AND EXISTS(SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal=1 AND c.column_id=COLUMNPROPERTY(i.object_id,'id_empleado_usuario','ColumnId'))
 AND EXISTS(SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal=2 AND c.column_id=COLUMNPROPERTY(i.object_id,'id_usuario','ColumnId')))
 THROW 59573, N'Migracion057: clave primaria incompatible.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID('dbo.libranza_autorizaciones_anticipo')
 AND i.name='ix_libranza_aut_anticipo_usuario' AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND i.type=2
 AND (SELECT COUNT(*) FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal>0)=1
 AND EXISTS(SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal=1 AND c.column_id=COLUMNPROPERTY(i.object_id,'id_usuario','ColumnId')))
 THROW 59574, N'Migracion057: indice de usuario incompatible.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.libranza_autorizaciones_anticipo')
 AND name='ck_libranza_aut_anticipo_decision' AND is_disabled=0 AND is_not_trusted=0
 AND CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',definition),2)='59897318AF36E3254E41ED10B7D64B1533A84783628F93924118AF5399EB0CAB')
 THROW 59575, N'Migracion057: comprobacion incompatible.', 1;
GO
