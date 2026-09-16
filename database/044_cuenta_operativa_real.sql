/* XPAY MVP V1 - 044_cuenta_operativa_real.sql */
/* XPAY-385 — base estructural de CAPA 2 (tesorería real de XPAY),
   distinta de la Wallet individual del usuario (CAPA 1). Ver
   XPAY-383/384 para el diagnóstico y diseño que motivan este ticket.

   Esta migración crea SOLO estructura + catálogo (nunca secretos):
     1. Nueva cuenta ledger 110103 — "Banco Coopcentral XPAY REAL
        (Passport)". 110102 ("Banco Coopcentral XPAY") queda LEGACY/
        HISTÓRICA — NO se toca, NO se corrige, NO se reclasifica. Ningún
        asiento histórico se modifica.
     2. Tabla cuentas_operativas (estructura vacía — la fila inicial de
        Sandbox se crea por código de bootstrap idempotente al arrancar
        la app, XPAY-385 FASE 3, NUNCA por esta migración: el fingerprint
        depende de PASSPORT_ACCOUNT_ID, un valor de configuración/secreto
        en tiempo de ejecución que una migración SQL estática no debe
        depender de ni hardcodear).

   Idempotente: usa IF NOT EXISTS / INFORMATION_SCHEMA para re-ejecución
   segura, mismo criterio que 010_passport_breb_base.sql/
   043_breb_payment_resolution_fields.sql. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- =========================================================================
-- 1. Cuenta ledger 110103 — Banco Coopcentral XPAY REAL (Passport)
--    Activo bancario. Representa la tesorería real de XPAY en Sandbox
--    Passport/Coopcentral, distinta de 110102 (legacy/histórica/
--    simulada, XPAY-383/384 — permanece intacta, sin cambios).
--    XPAY-385 NO modifica AplicarLiquidacionAsync todavía — esta cuenta
--    queda creada pero SIN USO en el flujo financiero real hasta
--    XPAY-386. Cero movimientos esperados al finalizar este ticket.
-- =========================================================================
INSERT INTO ledger_cuentas (id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta, naturaleza, permite_movimiento)
SELECT u.id_unidad_negocio,
       '110103',
       'Banco Coopcentral XPAY REAL (Passport)',
       'ACTIVO',
       'BANCO',
       'D',
       1
FROM   unidades_negocio u
WHERE  u.codigo = 'XPAY_COL'
  AND  NOT EXISTS (
         SELECT 1 FROM ledger_cuentas lc
         WHERE  lc.id_unidad_negocio = u.id_unidad_negocio
           AND  lc.codigo = '110103'
       );

IF @@ROWCOUNT > 0
    PRINT 'OK: cuenta ledger 110103 creada.';
ELSE
    PRINT 'Cuenta ledger 110103 ya existe — sin cambios.';
GO

-- =========================================================================
-- 2. Tabla cuentas_operativas — CAPA 2, entidad local que representa una
--    cuenta operativa real de XPAY en un proveedor externo (hoy:
--    Passport/Coopcentral). NUNCA almacena el account_id completo — solo
--    su fingerprint (SHA-256 truncado, mismo criterio que Xpay.Api.Common.
--    Fingerprint en todo el sistema) y el NOMBRE de la variable de
--    configuración donde vive el valor real (config_key_reference), nunca
--    el valor.
--
--    id_cuenta_ledger es FK REAL hacia ledger_cuentas.id_cuenta (no un
--    código de texto duplicado) — vínculo con integridad referencial
--    hacia 110103.
--
--    UQ_cuenta_operativa_combinacion impide crear dos filas para la misma
--    combinación proveedor+institución+moneda+ambiente — esto es lo que
--    hace que el bootstrap idempotente (XPAY-385 FASE 3, en código) sea
--    seguro ante reinicios repetidos de la app: si dos instancias
--    intentaran insertar simultáneamente, la segunda recibe una
--    violación UNIQUE que el código atrapa como no-op (mismo patrón ya
--    usado en wallet_idempotencia vía SqlExceptionHelper.IsUniqueViolation).
-- =========================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'cuentas_operativas')
BEGIN
    CREATE TABLE cuentas_operativas (
        id_cuenta_operativa      BIGINT          IDENTITY(1,1) NOT NULL,
        proveedor                NVARCHAR(30)    NOT NULL,
        institucion              NVARCHAR(100)   NOT NULL,
        moneda                   NVARCHAR(3)     NOT NULL,
        ambiente                 NVARCHAR(20)    NOT NULL,
        account_id_fingerprint   NVARCHAR(12)    NOT NULL,
        config_key_reference     NVARCHAR(100)   NOT NULL,
        id_cuenta_ledger         BIGINT          NOT NULL,
        estado                   NVARCHAR(20)    NOT NULL CONSTRAINT DF_cuenta_operativa_estado DEFAULT 'ACTIVA',
        fecha_creacion           DATETIME2       NOT NULL CONSTRAINT DF_cuenta_operativa_fecha_creacion DEFAULT SYSUTCDATETIME(),
        fecha_actualizacion      DATETIME2       NULL,
        CONSTRAINT PK_cuentas_operativas       PRIMARY KEY (id_cuenta_operativa),
        CONSTRAINT FK_cuenta_operativa_ledger  FOREIGN KEY (id_cuenta_ledger) REFERENCES ledger_cuentas(id_cuenta),
        CONSTRAINT CHK_cuenta_operativa_proveedor CHECK (LEN(LTRIM(RTRIM(proveedor))) > 0),
        CONSTRAINT CHK_cuenta_operativa_moneda    CHECK (LEN(moneda) = 3),
        CONSTRAINT CHK_cuenta_operativa_ambiente  CHECK (ambiente IN ('SANDBOX', 'PRODUCCION')),
        CONSTRAINT CHK_cuenta_operativa_estado    CHECK (estado IN ('ACTIVA', 'INACTIVA')),
        CONSTRAINT UQ_cuenta_operativa_combinacion UNIQUE (proveedor, institucion, moneda, ambiente)
    );

    CREATE INDEX IX_cuenta_operativa_ledger ON cuentas_operativas (id_cuenta_ledger);

    PRINT 'Tabla cuentas_operativas creada.';
END
ELSE
    PRINT 'Tabla cuentas_operativas ya existe.';
GO

PRINT '044_cuenta_operativa_real.sql ejecutado correctamente.';
GO
