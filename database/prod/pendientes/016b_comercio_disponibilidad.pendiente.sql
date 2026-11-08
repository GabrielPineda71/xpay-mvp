-- =====================================================================
-- XPAY PROD - PENDIENTE_DATOS 016b
-- Origen: database/016_comercio_disponibilidad.sql
-- SHA-256 del origen: 109eef755417d2f9c198ec274f5b93f3162c67dfaf932b331fefd8a427f55552
-- Lineas conservadas del origen: 1-2, 143-158
-- Estado en el manifiesto: BLOQUEADO_DECISION_NEGOCIO
-- Generado por database/prod/tools/construir_paquete.py.
-- NO EDITAR A MANO: modificar la especificacion y regenerar.
-- El contenido despues de la marca es copia exacta de esas lineas.
-- =====================================================================
-- @@FIN_ENCABEZADO_VARIANTE_PROD@@
SET QUOTED_IDENTIFIER ON;
GO
-- ── 7. Parámetros liquidacion anticipada 0-60 dias ───────────
-- Tasa: dia 0 = 0%, dia 1 = 0.5%, dia 2 = 1%, ..., dia 60 = 30%
IF NOT EXISTS (SELECT 1 FROM xpay_parametros_liquidacion_anticipada)
BEGIN
    DECLARE @d INT = 0;
    WHILE @d <= 60
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM xpay_parametros_liquidacion_anticipada WHERE dias_faltantes = @d AND estado = 'ACTIVO')
            INSERT INTO xpay_parametros_liquidacion_anticipada (dias_faltantes, porcentaje_descuento, estado, created_by_usuario)
            VALUES (@d, CAST(@d AS DECIMAL(9,4)) * 0.5000, 'ACTIVO', 1);
        SET @d = @d + 1;
    END
    PRINT 'Parametros liquidacion anticipada 0-60 dias insertados';
END
ELSE PRINT 'Parametros liquidacion anticipada ya existen';
GO
