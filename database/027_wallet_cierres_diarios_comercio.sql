-- =====================================================================
-- Migración 027: Cierre Diario de Comercios (Fase 70.3)
-- Idempotente — no borra datos, no modifica wallet_recargas_comercio,
-- wallet_liquidaciones_recaudo_comercio, comercios ni usuarios (las FK
-- de esta migración solo referencian esas tablas, nunca las alteran).
-- No crea movimientos de Wallet ni Ledger.
--
-- Dos tablas 100% nuevas, fuera del baseline de CI (migraciones 001-010).
-- A diferencia de 025/026, no se agrega ninguna columna a ninguna tabla
-- existente — el vínculo con wallet_recargas_comercio se resuelve por
-- completo desde la tabla de detalle nueva (FK + UNIQUE sobre id_recarga),
-- así que una recarga solo puede pertenecer a un cierre.
--
-- Envuelta en transacción explícita (SET XACT_ABORT ON + BEGIN TRY/BEGIN
-- TRANSACTION + BEGIN CATCH/ROLLBACK/THROW) para que un fallo a mitad de
-- la migración no deje tablas o constraints a medio crear — lección
-- aplicada de los parches operativos de Fase 69.3/70.1/70.2, donde el
-- editor SQL externo reportó éxito sin que todo quedara persistido.
-- =====================================================================

SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT '=== INICIO MIGRACIÓN 027: Cierre Diario de Comercios ===';

    -- ── 1. Tabla wallet_cierres_diarios_comercio ────────────────────────

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='wallet_cierres_diarios_comercio')
    BEGIN
        CREATE TABLE wallet_cierres_diarios_comercio (
            id_cierre                   BIGINT          IDENTITY(1,1) PRIMARY KEY,
            id_unidad_negocio            BIGINT         NOT NULL,               -- derivado de comercios.id_unidad_negocio en la app, sin DEFAULT fijo
            id_comercio                  BIGINT         NOT NULL,
            id_comercio_aliado           BIGINT         NULL,
            fecha_cierre                 DATE           NOT NULL,               -- fecha operativa Colombia (UTC-5) consolidada
            fecha_hora_corte_utc         DATETIME2      NOT NULL,               -- corte exacto usado para incluir/excluir recargas
            codigo_unico                 VARCHAR(60)    NOT NULL,               -- comprobante / espacio reservado para QR futuro
            cantidad_recargas            INT            NOT NULL,
            valor_total_recaudado        DECIMAL(18,2)  NOT NULL,
            valor_liquidado_al_generar   DECIMAL(18,2)  NOT NULL,               -- snapshot — no se recalcula tras generar
            valor_pendiente_al_generar   DECIMAL(18,2)  NOT NULL,               -- snapshot — no se recalcula tras generar
            estado                       VARCHAR(20)    NOT NULL DEFAULT 'GENERADO',
            generado_por_usuario         BIGINT         NOT NULL,
            fecha_generacion             DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
            revisado_por_usuario         BIGINT         NULL,
            fecha_revision               DATETIME2      NULL,
            cerrado_por_usuario          BIGINT         NULL,
            fecha_cerrado                DATETIME2      NULL,
            observaciones_admin          VARCHAR(500)   NULL,
            created_at                   DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),

            CONSTRAINT ck_wcdc_estado             CHECK (estado IN ('GENERADO','REVISADO','CERRADO')),
            CONSTRAINT ck_wcdc_valores_no_neg      CHECK (valor_total_recaudado >= 0 AND valor_liquidado_al_generar >= 0 AND valor_pendiente_al_generar >= 0),
            CONSTRAINT ck_wcdc_suma_valores        CHECK (valor_total_recaudado = valor_liquidado_al_generar + valor_pendiente_al_generar),
            CONSTRAINT ck_wcdc_cantidad_recargas   CHECK (cantidad_recargas > 0),
            CONSTRAINT uq_wcdc_comercio_fecha      UNIQUE (id_comercio, fecha_cierre),
            CONSTRAINT uq_wcdc_codigo_unico        UNIQUE (codigo_unico),
            CONSTRAINT fk_wcdc_comercio            FOREIGN KEY (id_comercio)          REFERENCES comercios(id_comercio),
            CONSTRAINT fk_wcdc_generado_por        FOREIGN KEY (generado_por_usuario) REFERENCES usuarios(id_usuario),
            CONSTRAINT fk_wcdc_revisado_por        FOREIGN KEY (revisado_por_usuario) REFERENCES usuarios(id_usuario),
            CONSTRAINT fk_wcdc_cerrado_por         FOREIGN KEY (cerrado_por_usuario)  REFERENCES usuarios(id_usuario)
        );
        PRINT 'OK: tabla wallet_cierres_diarios_comercio creada';
    END
    ELSE
        PRINT 'SKIP: tabla wallet_cierres_diarios_comercio ya existe';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wcdc_fecha_cierre' AND object_id=OBJECT_ID('wallet_cierres_diarios_comercio'))
        CREATE INDEX ix_wcdc_fecha_cierre ON wallet_cierres_diarios_comercio (fecha_cierre);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wcdc_estado' AND object_id=OBJECT_ID('wallet_cierres_diarios_comercio'))
        CREATE INDEX ix_wcdc_estado ON wallet_cierres_diarios_comercio (estado);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wcdc_comercio' AND object_id=OBJECT_ID('wallet_cierres_diarios_comercio'))
        CREATE INDEX ix_wcdc_comercio ON wallet_cierres_diarios_comercio (id_comercio);

    PRINT 'OK: índices de wallet_cierres_diarios_comercio verificados/creados';

    -- ── 2. Tabla wallet_cierres_diarios_comercio_detalle ────────────────

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='wallet_cierres_diarios_comercio_detalle')
    BEGIN
        CREATE TABLE wallet_cierres_diarios_comercio_detalle (
            id_detalle                  BIGINT          IDENTITY(1,1) PRIMARY KEY,
            id_cierre                   BIGINT          NOT NULL,
            id_recarga                  BIGINT          NOT NULL,
            valor                       DECIMAL(18,2)   NOT NULL,
            estaba_liquidada_al_generar BIT             NOT NULL,               -- snapshot del estado de liquidación al momento del corte
            created_at                  DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),

            CONSTRAINT ck_wcdcd_valor_no_neg  CHECK (valor >= 0),
            CONSTRAINT uq_wcdcd_id_recarga    UNIQUE (id_recarga),               -- una recarga solo puede estar en un cierre
            CONSTRAINT fk_wcdcd_cierre        FOREIGN KEY (id_cierre)  REFERENCES wallet_cierres_diarios_comercio(id_cierre),
            CONSTRAINT fk_wcdcd_recarga       FOREIGN KEY (id_recarga) REFERENCES wallet_recargas_comercio(id_recarga)
        );
        PRINT 'OK: tabla wallet_cierres_diarios_comercio_detalle creada';
    END
    ELSE
        PRINT 'SKIP: tabla wallet_cierres_diarios_comercio_detalle ya existe';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wcdcd_cierre' AND object_id=OBJECT_ID('wallet_cierres_diarios_comercio_detalle'))
        CREATE INDEX ix_wcdcd_cierre ON wallet_cierres_diarios_comercio_detalle (id_cierre);

    PRINT 'OK: índices de wallet_cierres_diarios_comercio_detalle verificados/creados';

    COMMIT TRANSACTION;
    PRINT '=== FIN MIGRACIÓN 027 (COMMIT OK) ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '=== ERROR EN MIGRACIÓN 027 — ROLLBACK EJECUTADO ===';
    THROW;
END CATCH;

-- ── 3. Verificación final ────────────────────────────────────────────────

SELECT name AS tabla FROM sys.tables
WHERE name IN ('wallet_cierres_diarios_comercio', 'wallet_cierres_diarios_comercio_detalle');

SELECT t.name AS tabla, c.name AS columna
FROM sys.columns c
JOIN sys.tables t ON t.object_id = c.object_id
WHERE t.name IN ('wallet_cierres_diarios_comercio', 'wallet_cierres_diarios_comercio_detalle')
ORDER BY t.name, c.column_id;

SELECT t.name AS tabla, cc.name AS check_constraint, cc.definition
FROM sys.check_constraints cc
JOIN sys.tables t ON t.object_id = cc.parent_object_id
WHERE t.name IN ('wallet_cierres_diarios_comercio', 'wallet_cierres_diarios_comercio_detalle');

SELECT t.name AS tabla, fk.name AS foreign_key, rt.name AS tabla_referenciada
FROM sys.foreign_keys fk
JOIN sys.tables t  ON t.object_id  = fk.parent_object_id
JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
WHERE t.name IN ('wallet_cierres_diarios_comercio', 'wallet_cierres_diarios_comercio_detalle');

SELECT t.name AS tabla, i.name AS indice, i.is_unique
FROM sys.indexes i
JOIN sys.tables t ON t.object_id = i.object_id
WHERE t.name IN ('wallet_cierres_diarios_comercio', 'wallet_cierres_diarios_comercio_detalle')
  AND i.name IS NOT NULL;

PRINT '=== VERIFICACIÓN COMPLETA ===';
