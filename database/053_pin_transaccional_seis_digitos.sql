-- PIN transaccional de seis digitos: metadata de la longitud conocida.
-- Aditiva, idempotente y sin backfill: NULL identifica los PIN previos cuya
-- longitud no puede inferirse de BCrypt. No modifica hashes, contadores,
-- bloqueos, fechas ni estados. Sin DEFAULT: solo crear/cambiar un PIN guarda 6.
-- Aplicar antes de publicar el backend que mapea longitud_pin.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

PRINT '=== INICIO MIGRACION 053 ===';
IF OBJECT_ID('dbo.pin_transaccional', 'U') IS NULL
    THROW 59530, N'Migracion 053 abortada: falta dbo.pin_transaccional.', 1;
GO

IF COL_LENGTH('dbo.pin_transaccional', 'longitud_pin') IS NULL
    ALTER TABLE dbo.pin_transaccional ADD longitud_pin TINYINT NULL;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID('dbo.pin_transaccional')
      AND c.name = 'longitud_pin'
      AND t.name = 'tinyint' AND t.is_user_defined = 0
      AND c.max_length = 1 AND c.is_nullable = 1
      AND c.is_identity = 0 AND c.is_computed = 0 AND c.default_object_id = 0)
    THROW 59531, N'Migracion 053 abortada: pin_transaccional.longitud_pin incompatible.', 1;
GO

PRINT '=== FIN MIGRACION 053 ===';
GO
