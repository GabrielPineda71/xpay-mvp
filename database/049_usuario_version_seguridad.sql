-- XPAY P0-B - Migracion 049: usuarios.version_seguridad (revocacion de JWT).
-- Contador monotonico por usuario. El JWT lleva el claim "vs" y cada request
-- autenticado exige que coincida con este valor; toda operacion sensible
-- (contrasena, estado, roles) lo incrementa de forma atomica.
-- Idempotente: crea solo lo que falta y verifica lo existente; ante
-- cualquier incompatibilidad aborta con THROW (no corrige nada).
-- Los usuarios existentes quedan con version_seguridad = 0 (DEFAULT).
-- Solo toca dbo.usuarios.version_seguridad y sus dos constraints.
-- No modifica 045/046/047/048. No se aplica en este ticket a QA ni a PROD.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

PRINT '=== INICIO MIGRACION 049 ===';
IF OBJECT_ID('dbo.usuarios', 'U') IS NULL
    THROW 59490, N'Migracion 049 abortada: falta dbo.usuarios.', 1;
GO

IF COL_LENGTH('dbo.usuarios', 'version_seguridad') IS NULL
BEGIN
    ALTER TABLE dbo.usuarios ADD version_seguridad INT NOT NULL
        CONSTRAINT df_usuarios_version_seguridad DEFAULT 0;
    PRINT 'usuarios.version_seguridad creada';
END
ELSE
    PRINT 'usuarios.version_seguridad ya existe';
GO

-- 1. Columna: int de sistema, 4 bytes, NOT NULL, sin IDENTITY, no calculada.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID('dbo.usuarios')
      AND c.name = 'version_seguridad'
      AND t.name = 'int'
      AND t.is_user_defined = 0
      AND c.max_length = 4
      AND c.is_nullable = 0
      AND c.is_identity = 0
      AND c.is_computed = 0)
    THROW 59491, N'Migracion 049 abortada: usuarios.version_seguridad incompatible.', 1;
GO

-- 2. DEFAULT: df_usuarios_version_seguridad sobre esa columna, valor 0.
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    WHERE dc.parent_object_id = OBJECT_ID('dbo.usuarios')
      AND dc.name = 'df_usuarios_version_seguridad'
      AND dc.parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.usuarios'), 'version_seguridad', 'ColumnId')
      AND dc.definition = N'((0))')
    THROW 59492, N'Migracion 049 abortada: df_usuarios_version_seguridad ausente o incompatible.', 1;
GO

-- 3. CHECK: se crea si falta. WITH CHECK valida las filas existentes y deja
--    la constraint confiable.
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = 'ck_usuarios_version_seguridad'
      AND parent_object_id = OBJECT_ID('dbo.usuarios'))
    ALTER TABLE dbo.usuarios WITH CHECK
        ADD CONSTRAINT ck_usuarios_version_seguridad CHECK (version_seguridad >= 0);
GO

-- 4. CHECK: habilitado, confiable y con la condicion exacta tal como la
--    normaliza SQL Server.
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints cc
    WHERE cc.name = 'ck_usuarios_version_seguridad'
      AND cc.parent_object_id = OBJECT_ID('dbo.usuarios')
      AND cc.is_disabled = 0
      AND cc.is_not_trusted = 0
      AND cc.definition = N'([version_seguridad]>=(0))')
    THROW 59493, N'Migracion 049 abortada: ck_usuarios_version_seguridad ausente o incompatible.', 1;
GO

PRINT '=== FIN MIGRACION 049 ===';
GO
