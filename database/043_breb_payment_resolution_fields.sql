/* XPAY MVP V1 - 043_breb_payment_resolution_fields.sql */
/* XPAY-373 — columnas mínimas para ligar una resolución Passport vigente
   a la llave y, de forma inmutable, al retiro concreto que la consume,
   permitiendo verificar expiración ANTES de intentar un Payment real sin
   depender de una llamada nueva a Passport sólo para ese chequeo. */
/* Idempotente: usa IF NOT EXISTS / sys.columns para re-ejecución segura. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- =========================================================================
-- 1. passport_breb_llaves — última resolución conocida de la llave.
--    Se actualiza cada vez que POST /api/breb/mi-llave/resolver (XPAY-371)
--    resuelve exitosamente. Es una CACHÉ de "la resolución más reciente",
--    no un historial — cada resolve exitoso la sobreescribe.
-- =========================================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.passport_breb_llaves') AND name = 'passport_resolution_id')
BEGIN
    ALTER TABLE dbo.passport_breb_llaves ADD passport_resolution_id NVARCHAR(100) NULL;
    PRINT 'OK: passport_breb_llaves.passport_resolution_id agregada';
END
ELSE
    PRINT 'passport_breb_llaves.passport_resolution_id ya existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.passport_breb_llaves') AND name = 'passport_resolution_expires_at')
BEGIN
    ALTER TABLE dbo.passport_breb_llaves ADD passport_resolution_expires_at DATETIME2 NULL;
    PRINT 'OK: passport_breb_llaves.passport_resolution_expires_at agregada';
END
ELSE
    PRINT 'passport_breb_llaves.passport_resolution_expires_at ya existe.';
GO

-- =========================================================================
-- 2. passport_breb_retiros — snapshot INMUTABLE de la resolución realmente
--    usada por ESTE retiro (distinta de la caché mutable de la llave, que
--    puede haber sido refrescada por otra operación después de crear este
--    retiro). passport_resolution_id YA EXISTE desde 010_passport_breb_base
--    (Fase 64) — sólo falta su vencimiento.
-- =========================================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.passport_breb_retiros') AND name = 'passport_resolution_expires_at')
BEGIN
    ALTER TABLE dbo.passport_breb_retiros ADD passport_resolution_expires_at DATETIME2 NULL;
    PRINT 'OK: passport_breb_retiros.passport_resolution_expires_at agregada';
END
ELSE
    PRINT 'passport_breb_retiros.passport_resolution_expires_at ya existe.';
GO

PRINT '043_breb_payment_resolution_fields.sql ejecutado correctamente.';
GO
