/* XPAY MVP V1 - 006_gestion_retiros_comercio.sql */
/* Fase 6: Gestión de retiros — confirmar pago o rechazar */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Cuenta 110101 Efectivo en Bóveda (no incluida en seed inicial de 002)
INSERT INTO ledger_cuentas (id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta, naturaleza, permite_movimiento)
SELECT u.id_unidad_negocio, '110101', 'Efectivo en Bóveda', 'ACTIVO', 'CAJA_BOVEDA', 'D', 1
FROM unidades_negocio u WHERE u.codigo = 'XPAY_COL';
GO

-- Nuevas columnas en retiros_comercio para gestión post-solicitud
ALTER TABLE retiros_comercio
ADD fecha_pago             DATETIME2      NULL,
    referencia_pago        NVARCHAR(100)  NULL,
    fecha_rechazo          DATETIME2      NULL,
    motivo_rechazo         NVARCHAR(300)  NULL,
    id_transaccion_gestion BIGINT         NULL;
GO

ALTER TABLE retiros_comercio
ADD CONSTRAINT FK_retcom_tx_gestion
    FOREIGN KEY (id_transaccion_gestion)
    REFERENCES ledger_transacciones(id_transaccion_ledger);
GO
