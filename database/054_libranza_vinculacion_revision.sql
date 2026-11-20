/* 054: aprobación manual de pertenencia laboral. Sin backfill, saldos ni ledger.
   Ejecutar antes de publicar el backend que lee estas columnas. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO
IF OBJECT_ID(N'dbo.libranza_empleado_usuario', N'U') IS NULL
    THROW 59550, N'Falta libranza_empleado_usuario; aplicar primero la estructura de Libranza.', 1;
GO
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.libranza_empleado_usuario', N'revision_huella') IS NULL
    ALTER TABLE dbo.libranza_empleado_usuario ADD revision_huella VARCHAR(64) NULL;
IF COL_LENGTH(N'dbo.libranza_empleado_usuario', N'revision_fecha') IS NULL
    ALTER TABLE dbo.libranza_empleado_usuario ADD revision_fecha DATETIME2 NULL;
IF COL_LENGTH(N'dbo.libranza_empleado_usuario', N'revision_por_usuario') IS NULL
    ALTER TABLE dbo.libranza_empleado_usuario ADD revision_por_usuario BIGINT NULL;
IF COL_LENGTH(N'dbo.libranza_empleado_usuario', N'revision_metodo') IS NULL
    ALTER TABLE dbo.libranza_empleado_usuario ADD revision_metodo VARCHAR(20) NULL;
IF COL_LENGTH(N'dbo.libranza_empleado_usuario', N'revision_referencia') IS NULL
    ALTER TABLE dbo.libranza_empleado_usuario ADD revision_referencia NVARCHAR(200) NULL;
COMMIT;
GO
-- Batch separado: SQL Server debe resolver las columnas nuevas al compilar.
IF (SELECT COUNT(*) FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
    JOIN (VALUES (N'revision_huella',N'varchar',64),(N'revision_fecha',N'datetime2',8),
                 (N'revision_por_usuario',N'bigint',8),(N'revision_metodo',N'varchar',20),
                 (N'revision_referencia',N'nvarchar',400)) esperado(nombre,tipo,longitud)
      ON esperado.nombre=c.name AND esperado.tipo=t.name AND esperado.longitud=c.max_length
    WHERE c.object_id=OBJECT_ID(N'dbo.libranza_empleado_usuario') AND c.is_nullable=1) <> 5
    THROW 59551, N'Las columnas existentes de revisión tienen una estructura inesperada; revisar antes de continuar.', 1;
GO
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.libranza_empleado_usuario') AND name=N'ck_leu_revision_manual')
    ALTER TABLE dbo.libranza_empleado_usuario WITH CHECK ADD CONSTRAINT ck_leu_revision_manual CHECK (
      (revision_huella IS NULL AND revision_fecha IS NULL AND revision_por_usuario IS NULL AND revision_metodo IS NULL AND revision_referencia IS NULL)
      OR
      (revision_huella IS NOT NULL AND LEN(revision_huella)=64 AND revision_huella COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
       AND revision_fecha IS NOT NULL AND revision_por_usuario IS NOT NULL AND revision_por_usuario>0
       AND revision_metodo IS NOT NULL AND revision_metodo='MANUAL'
       AND revision_referencia IS NOT NULL AND LEN(LTRIM(RTRIM(revision_referencia))) BETWEEN 5 AND 200));
COMMIT;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.libranza_empleado_usuario')
    AND name=N'ck_leu_revision_manual' AND (is_disabled=1 OR is_not_trusted=1))
    THROW 59552, N'El control de revisión debe estar habilitado y validado.', 1;
GO
