-- Metadata-only, append-only administrative import. No risk payload, consent,
-- credit application, decision or credit limit is created by this migration.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF OBJECT_ID('dbo.personas','U') IS NULL OR OBJECT_ID('dbo.usuarios','U') IS NULL
 THROW 59580, N'Migracion058: faltan tablas base.', 1;
GO
IF OBJECT_ID('dbo.midecisor_consultas_externas','U') IS NULL
BEGIN
 CREATE TABLE dbo.midecisor_consultas_externas (
  id_consulta_externa BIGINT IDENTITY(1,1) NOT NULL,
  huella_sujeto CHAR(64) NOT NULL,
  id_persona_origen BIGINT NOT NULL,
  fecha_referencia_utc DATETIME2 NOT NULL,
  fecha_exacta BIT NOT NULL,
  origen VARCHAR(40) NOT NULL,
  evidencia_sha256 CHAR(64) NOT NULL,
  registrado_por_usuario BIGINT NOT NULL,
  registrado_utc DATETIME2 NOT NULL,
  ambito VARCHAR(40) NOT NULL,
  CONSTRAINT pk_midecisor_consultas_externas PRIMARY KEY(id_consulta_externa),
  CONSTRAINT uq_midecisor_consulta_externa_evidencia UNIQUE(evidencia_sha256),
  CONSTRAINT fk_midecisor_consulta_externa_persona FOREIGN KEY(id_persona_origen) REFERENCES dbo.personas(id_persona),
  CONSTRAINT fk_midecisor_consulta_externa_admin FOREIGN KEY(registrado_por_usuario) REFERENCES dbo.usuarios(id_usuario),
  CONSTRAINT ck_midecisor_consulta_externa_origen CHECK(origen IN ('RECIBO_DIAGNOSTICO_TITULAR','CONFIRMACION_MANUAL_TITULAR')),
  CONSTRAINT ck_midecisor_consulta_externa_exacta CHECK(origen<>'CONFIRMACION_MANUAL_TITULAR' OR fecha_exacta=0),
  CONSTRAINT ck_midecisor_consulta_externa_ambito CHECK(ambito='CONSULTA_SIN_DECISION'),
  CONSTRAINT ck_midecisor_consulta_externa_fecha CHECK(fecha_referencia_utc<=registrado_utc),
  CONSTRAINT ck_midecisor_consulta_externa_huella CHECK(huella_sujeto COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
  CONSTRAINT ck_midecisor_consulta_externa_evidencia CHECK(evidencia_sha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%')
 );
 CREATE INDEX ix_midecisor_consulta_externa_sujeto_fecha ON dbo.midecisor_consultas_externas(huella_sujeto,fecha_referencia_utc DESC);
END;
GO
DECLARE @id INT=OBJECT_ID('dbo.midecisor_consultas_externas');
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=@id)<>10
 THROW 59581, N'Migracion058: estructura de ledger incompatible.', 1;
IF EXISTS (
 SELECT 1 FROM (VALUES
 ('id_consulta_externa','bigint',8),('huella_sujeto','char',64),('id_persona_origen','bigint',8),
 ('fecha_referencia_utc','datetime2',8),('fecha_exacta','bit',1),('origen','varchar',40),
 ('evidencia_sha256','char',64),('registrado_por_usuario','bigint',8),('registrado_utc','datetime2',8),('ambito','varchar',40)
 ) e(nombre,tipo,longitud)
 LEFT JOIN sys.columns c ON c.object_id=@id AND c.name=e.nombre
 LEFT JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.column_id IS NULL OR c.is_nullable<>0 OR t.name<>e.tipo OR c.max_length<>e.longitud
) THROW 59582, N'Migracion058: columnas de ledger incompatibles.', 1;
IF COLUMNPROPERTY(@id,'id_consulta_externa','IsIdentity')<>1
 OR NOT EXISTS(SELECT 1 FROM sys.key_constraints WHERE parent_object_id=@id AND name='pk_midecisor_consultas_externas' AND type='PK')
 OR NOT EXISTS(SELECT 1 FROM sys.key_constraints WHERE parent_object_id=@id AND name='uq_midecisor_consulta_externa_evidencia' AND type='UQ')
 OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=@id AND name='ix_midecisor_consulta_externa_sujeto_fecha' AND is_disabled=0)
 THROW 59583, N'Migracion058: claves o indice de ledger incompatibles.', 1;
IF EXISTS (SELECT 1 FROM (VALUES
 ('ck_midecisor_consulta_externa_origen'),('ck_midecisor_consulta_externa_exacta'),
 ('ck_midecisor_consulta_externa_ambito'),('ck_midecisor_consulta_externa_fecha'),
 ('ck_midecisor_consulta_externa_huella'),('ck_midecisor_consulta_externa_evidencia')
 ) e(nombre) LEFT JOIN sys.check_constraints c ON c.parent_object_id=@id AND c.name=e.nombre
 WHERE c.object_id IS NULL OR c.is_disabled=1 OR c.is_not_trusted=1)
 OR EXISTS (SELECT 1 FROM (VALUES ('fk_midecisor_consulta_externa_persona'),('fk_midecisor_consulta_externa_admin')) e(nombre)
 LEFT JOIN sys.foreign_keys f ON f.parent_object_id=@id AND f.name=e.nombre
 WHERE f.object_id IS NULL OR f.is_disabled=1 OR f.is_not_trusted=1)
 THROW 59584, N'Migracion058: restricciones de ledger incompatibles.', 1;
GO
