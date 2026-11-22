-- Domicilio DANE del registro guiado. Aplicar antes del backend que mapea
-- id_ciudad_dane. Aditiva e idempotente; sin backfill, sin modificar domicilio
-- histórico, identidad, cuentas, verificaciones, Wallets o saldos.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.personas', 'U') IS NULL OR OBJECT_ID('dbo.catalogo_ciudades', 'U') IS NULL
    THROW 59550, N'Migracion 055 abortada: falta personas o catalogo_ciudades.', 1;
GO

IF COL_LENGTH('dbo.personas', 'id_ciudad_dane') IS NULL
    ALTER TABLE dbo.personas ADD id_ciudad_dane BIGINT NULL;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID('dbo.personas') AND c.name = 'id_ciudad_dane'
      AND t.name = 'bigint' AND t.is_user_defined = 0 AND c.max_length = 8
      AND c.is_nullable = 1 AND c.is_identity = 0 AND c.is_computed = 0 AND c.default_object_id = 0)
    THROW 59551, N'Migracion 055 abortada: personas.id_ciudad_dane incompatible.', 1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('dbo.personas')
               AND name = 'fk_personas_ciudad_dane')
    ALTER TABLE dbo.personas WITH CHECK ADD CONSTRAINT fk_personas_ciudad_dane
        FOREIGN KEY (id_ciudad_dane) REFERENCES dbo.catalogo_ciudades(id_ciudad);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c ON c.constraint_object_id = f.object_id
    WHERE f.parent_object_id = OBJECT_ID('dbo.personas') AND f.name = 'fk_personas_ciudad_dane'
      AND f.referenced_object_id = OBJECT_ID('dbo.catalogo_ciudades')
      AND c.parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.personas'), 'id_ciudad_dane', 'ColumnId')
      AND c.referenced_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.catalogo_ciudades'), 'id_ciudad', 'ColumnId')
      AND f.is_disabled = 0 AND f.is_not_trusted = 0
      AND f.delete_referential_action = 0 AND f.update_referential_action = 0
      AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id = f.object_id) = 1)
    THROW 59552, N'Migracion 055 abortada: FK de domicilio incompatible o sin validar.', 1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.personas') AND name = 'ix_personas_id_ciudad_dane')
    CREATE INDEX ix_personas_id_ciudad_dane ON dbo.personas(id_ciudad_dane);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i JOIN sys.index_columns c ON c.object_id = i.object_id AND c.index_id = i.index_id
    WHERE i.object_id = OBJECT_ID('dbo.personas') AND i.name = 'ix_personas_id_ciudad_dane'
      AND i.type = 2 AND i.is_unique = 0 AND i.is_disabled = 0 AND i.has_filter = 0
      AND c.column_id = COLUMNPROPERTY(OBJECT_ID('dbo.personas'), 'id_ciudad_dane', 'ColumnId')
      AND c.key_ordinal = 1 AND c.is_included_column = 0
      AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id = i.object_id AND x.index_id = i.index_id) = 1)
    THROW 59553, N'Migracion 055 abortada: indice de domicilio incompatible.', 1;
GO
