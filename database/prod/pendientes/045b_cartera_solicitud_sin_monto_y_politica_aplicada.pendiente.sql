-- =====================================================================
-- XPAY PROD - PENDIENTE_DATOS 045b
-- Origen: database/045_cartera_solicitud_sin_monto_y_politica_aplicada.sql
-- SHA-256 del origen: 8f6909891af7b4e1bd62f3c8a05b229ca7cdfbe32b32edc168c322fc4774b42a
-- Lineas conservadas del origen: 44-60, 120-152, 168-174
-- Estado en el manifiesto: BLOQUEADO_DECISION_NEGOCIO
-- Generado por database/prod/tools/construir_paquete.py.
-- NO EDITAR A MANO: modificar la especificacion y regenerar.
-- El contenido despues de la marca es copia exacta de esas lineas.
-- =====================================================================
-- @@FIN_ENCABEZADO_VARIANTE_PROD@@
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT '=== INICIO MIGRACIÓN 045: solicitud sin monto + política aplicada ===';

    DECLARE @objIdSolicitud INT = OBJECT_ID('dbo.cartera_solicitudes_cupo', 'U');
    IF @objIdSolicitud IS NULL
        THROW 57000, N'Migración 045 abortada: dbo.cartera_solicitudes_cupo no existe. No se puede continuar.', 1;

    DECLARE @objIdPolitica INT = OBJECT_ID('dbo.cartera_politicas_credito', 'U');
    IF @objIdPolitica IS NULL
        THROW 57001, N'Migración 045 abortada: dbo.cartera_politicas_credito no existe. No se puede continuar.', 1;

    -- ═══════════════════════════════════════════════════════════════════
    -- 3) Fila ACTIVA de cartera_politicas_credito = política aprobada del motor
    -- ═══════════════════════════════════════════════════════════════════
    DECLARE @activas INT = (SELECT COUNT(*) FROM dbo.cartera_politicas_credito WHERE estado = 'ACTIVO');
    IF @activas <> 1
        THROW 57006, N'Migración 045 abortada: se esperaba exactamente UNA fila ACTIVA en dbo.cartera_politicas_credito — revisar manualmente.', 1;

    IF EXISTS (
        SELECT 1 FROM dbo.cartera_politicas_credito
        WHERE estado = 'ACTIVO'
          AND (cupo_minimo <> 200000 OR cupo_maximo <> 700000 OR edad_minima <> 18 OR edad_maxima <> 65)
    )
    BEGIN
        UPDATE dbo.cartera_politicas_credito
        SET cupo_minimo = 200000,
            cupo_maximo = 700000,
            edad_minima = 18,
            edad_maxima = 65,
            updated_at  = SYSUTCDATETIME()
        WHERE estado = 'ACTIVO';
        PRINT 'OK: fila ACTIVA de cartera_politicas_credito alineada con la política aprobada del motor';
    END
    ELSE
        PRINT 'INFO: fila ACTIVA de cartera_politicas_credito ya alineada — omitido';

    COMMIT TRANSACTION;
    PRINT '=== FIN MIGRACIÓN 045 (COMMIT OK) ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '=== ERROR EN MIGRACIÓN 045 — ROLLBACK EJECUTADO ===';
    THROW;
END CATCH;

SELECT id_politica, score_datacredito_minimo, requiere_veriff, cupo_minimo, cupo_maximo,
       edad_minima, edad_maxima, estado, vigente_desde, updated_at
FROM dbo.cartera_politicas_credito
WHERE estado = 'ACTIVO';

PRINT '=== VERIFICACIÓN 045 COMPLETA ===';
