-- operacion/inventario_estado.sql - XPAY PROD, B1.
-- Inventario de estado de la base. SOLO LECTURA: SET NOCOUNT y SELECT
-- sobre el catalogo y conteos. Salida ordenada y sin identificadores
-- internos, para comparar con diff (procedimiento, 6.6 y 6.7). Los
-- nombres generados por el sistema se enmascaran. Las columnas
-- NVARCHAR(MAX) se convierten a NVARCHAR(4000) para que sqlcmd no las
-- recorte a 256 caracteres; CONTROL_LARGO debe valer 0.
-- Uso: sqlcmd ... -h -1 -W -s "|" -i inventario_estado.sql -o <salida>
-- Probado solo en SQL Server 2022 local (T0-T4); logica identica a la
-- version 2 de laboratorio. No validado en Azure SQL.
SET NOCOUNT ON;

SELECT N'SERVIDOR',
       CAST(SERVERPROPERTY('EngineEdition') AS INT),
       CAST(SERVERPROPERTY('Edition') AS NVARCHAR(128)),
       CAST(SERVERPROPERTY('MachineName') AS NVARCHAR(128)),
       CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR(128));

SELECT N'BASE',
       CONVERT(NVARCHAR(128),
               DATABASEPROPERTYEX(DB_NAME(), 'Collation')),
       d.compatibility_level,
       d.is_read_committed_snapshot_on
FROM sys.databases AS d
WHERE d.name = DB_NAME();

SELECT N'ESQ', s.name, USER_NAME(s.principal_id)
FROM sys.schemas AS s
ORDER BY 2;

SELECT N'PRIN', p.name, p.type_desc,
       ISNULL(p.default_schema_name, N'-')
FROM sys.database_principals AS p
ORDER BY 2;

SELECT N'ROL', USER_NAME(m.role_principal_id),
       USER_NAME(m.member_principal_id)
FROM sys.database_role_members AS m
ORDER BY 2, 3;

SELECT N'PERM', p.class_desc,
       CASE p.class
           WHEN 0 THEN N'(base)'
           WHEN 3 THEN SCHEMA_NAME(p.major_id)
           WHEN 1 THEN CONCAT(OBJECT_SCHEMA_NAME(p.major_id),
                              N'.', OBJECT_NAME(p.major_id))
           ELSE CONCAT(N'id=', p.major_id)
       END,
       p.minor_id, p.permission_name, p.state_desc,
       USER_NAME(p.grantee_principal_id)
FROM sys.database_permissions AS p
ORDER BY 2, 3, 4, 5, 7, 6;

SELECT N'EXT', ep.class_desc,
       CASE ep.class
           WHEN 0 THEN N'(base)'
           WHEN 1 THEN CONCAT(OBJECT_SCHEMA_NAME(ep.major_id),
                              N'.', OBJECT_NAME(ep.major_id))
           WHEN 3 THEN SCHEMA_NAME(ep.major_id)
           ELSE CONCAT(N'id=', ep.major_id)
       END,
       ep.minor_id, ep.name,
       CONVERT(NVARCHAR(4000), ep.value)
FROM sys.extended_properties AS ep
ORDER BY 2, 3, 4, 5;

SELECT N'TIPO', SCHEMA_NAME(t.schema_id), t.name
FROM sys.types AS t
WHERE t.is_user_defined = 1
ORDER BY 2, 3;

SELECT N'TRG_BASE', t.name, t.is_disabled
FROM sys.triggers AS t
WHERE t.parent_class = 0
ORDER BY 2;

SELECT N'OBJ', o.type, SCHEMA_NAME(o.schema_id),
       CASE WHEN o.type IN ('PK', 'UQ', 'F', 'C', 'D')
            THEN N'(restriccion)' ELSE o.name END,
       ISNULL(OBJECT_NAME(o.parent_object_id), N'-')
FROM sys.objects AS o
WHERE o.is_ms_shipped = 0
ORDER BY 2, 3, 5, 4;

SELECT N'COL', OBJECT_SCHEMA_NAME(c.object_id),
       OBJECT_NAME(c.object_id), c.column_id, c.name,
       TYPE_NAME(c.user_type_id), c.max_length, c.precision,
       c.scale, c.is_nullable, c.is_identity,
       ISNULL(c.collation_name, N'-')
FROM sys.columns AS c
JOIN sys.tables AS t ON t.object_id = c.object_id
WHERE t.is_ms_shipped = 0
ORDER BY 2, 3, 4;

SELECT N'IDX', OBJECT_SCHEMA_NAME(i.object_id),
       OBJECT_NAME(i.object_id),
       CASE WHEN i.is_primary_key = 1 OR i.is_unique_constraint = 1
            THEN N'(restriccion)' ELSE i.name END,
       i.type_desc, i.is_unique, i.is_primary_key,
       ISNULL(CONVERT(NVARCHAR(4000), i.filter_definition), N'-'),
       (SELECT STRING_AGG(
                   CONCAT(COL_NAME(k.object_id, k.column_id),
                          CASE WHEN k.is_descending_key = 1
                               THEN N' DESC' END,
                          CASE WHEN k.is_included_column = 1
                               THEN N' INCL' END), N',')
               WITHIN GROUP (ORDER BY k.is_included_column,
                                      k.key_ordinal, k.column_id)
        FROM sys.index_columns AS k
        WHERE k.object_id = i.object_id
          AND k.index_id = i.index_id)
FROM sys.indexes AS i
JOIN sys.tables AS t ON t.object_id = i.object_id
WHERE t.is_ms_shipped = 0 AND i.index_id > 0
ORDER BY 2, 3, 4, 9;

SELECT N'FK', OBJECT_SCHEMA_NAME(f.parent_object_id),
       OBJECT_NAME(f.parent_object_id),
       CASE WHEN f.is_system_named = 1
            THEN N'(sistema)' ELSE f.name END,
       OBJECT_NAME(f.referenced_object_id),
       f.delete_referential_action_desc,
       f.update_referential_action_desc,
       f.is_disabled, f.is_not_trusted,
       (SELECT STRING_AGG(
                   CONCAT(COL_NAME(x.parent_object_id,
                                   x.parent_column_id),
                          N'>',
                          COL_NAME(x.referenced_object_id,
                                   x.referenced_column_id)), N',')
               WITHIN GROUP (ORDER BY x.constraint_column_id)
        FROM sys.foreign_key_columns AS x
        WHERE x.constraint_object_id = f.object_id)
FROM sys.foreign_keys AS f
ORDER BY 2, 3, 10, 4;

SELECT N'CK', OBJECT_SCHEMA_NAME(c.parent_object_id),
       OBJECT_NAME(c.parent_object_id),
       CASE WHEN c.is_system_named = 1
            THEN N'(sistema)' ELSE c.name END,
       CONVERT(NVARCHAR(4000), c.definition),
       c.is_disabled, c.is_not_trusted
FROM sys.check_constraints AS c
ORDER BY 2, 3, 5, 4;

SELECT N'DF', OBJECT_SCHEMA_NAME(d.parent_object_id),
       OBJECT_NAME(d.parent_object_id),
       COL_NAME(d.parent_object_id, d.parent_column_id),
       CASE WHEN d.is_system_named = 1
            THEN N'(sistema)' ELSE d.name END,
       CONVERT(NVARCHAR(4000), d.definition)
FROM sys.default_constraints AS d
ORDER BY 2, 3, 4;

SELECT N'FILAS', SCHEMA_NAME(t.schema_id), t.name,
       SUM(p.rows)
FROM sys.tables AS t
JOIN sys.partitions AS p
  ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.is_ms_shipped = 0
GROUP BY t.schema_id, t.name
ORDER BY 2, 3;

SELECT N'CONTROL_LARGO',
       (SELECT COUNT(*) FROM sys.check_constraints
        WHERE LEN(definition) > 4000)
     + (SELECT COUNT(*) FROM sys.default_constraints
        WHERE LEN(definition) > 4000)
     + (SELECT COUNT(*) FROM sys.indexes
        WHERE LEN(filter_definition) > 4000)
     + (SELECT COUNT(*) FROM sys.extended_properties
        WHERE LEN(CONVERT(NVARCHAR(MAX), value)) > 4000);

SELECT N'RESUMEN',
       (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0),
       (SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped = 0),
       (SELECT COUNT(*) FROM sys.foreign_keys);
