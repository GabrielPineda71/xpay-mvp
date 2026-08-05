-- =====================================================================
-- Migración 028: Apertura, Cuadre y Cierre de Caja (Fase 70.4.1)
-- Idempotente — no borra datos, no altera columnas existentes, no toca
-- wallet_recargas_comercio, wallet_liquidaciones_recaudo_comercio,
-- wallet_cierres_diarios_comercio(_detalle), Wallet ni Ledger.
--
-- Dos tablas 100% nuevas (wallet_cajas_comercio, wallet_caja_movimientos),
-- fuera del baseline de CI (migraciones 001-010), más una columna aditiva
-- y nullable sobre comercio_establecimientos (tabla creada en la migración
-- 015, también fuera de ese baseline).
--
-- El vínculo recarga↔caja se resuelve por completo desde
-- wallet_caja_movimientos.id_recarga (FK real + UNIQUE filtrado) — no se
-- agrega ninguna columna a wallet_recargas_comercio, mismo criterio que
-- usó la migración 027 para el vínculo recarga↔cierre diario.
--
-- Esta migración NO crea: caja_notificaciones_internas (diferida a
-- 70.4.2), columnas de reapertura (retiradas del diseño), jobs,
-- procedimientos almacenados, triggers, ni ningún objeto relacionado con
-- sp_getapplock (es una llamada en tiempo de ejecución del servicio, no
-- un objeto de esquema).
--
-- Envuelta en transacción explícita (SET XACT_ABORT ON + BEGIN TRY/BEGIN
-- TRANSACTION + BEGIN CATCH/ROLLBACK/THROW), mismo patrón que la
-- migración 027, para que un fallo a mitad de la migración no deje
-- tablas o constraints a medio crear.
-- =====================================================================

SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT '=== INICIO MIGRACIÓN 028: Apertura, Cuadre y Cierre de Caja ===';

    -- ── 1. Tabla wallet_cajas_comercio ───────────────────────────────────

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='wallet_cajas_comercio')
    BEGIN
        CREATE TABLE wallet_cajas_comercio (
            id_caja                   BIGINT          IDENTITY(1,1) PRIMARY KEY,
            id_unidad_negocio         BIGINT          NOT NULL,               -- derivado de comercios.id_unidad_negocio en la app, sin DEFAULT fijo
            id_comercio               BIGINT          NOT NULL,
            id_comercio_aliado        BIGINT          NULL,
            id_establecimiento        BIGINT          NOT NULL,               -- fija de por vida, nunca cambia tras abrir
            id_usuario_cajero         BIGINT          NOT NULL,
            revisado_por_usuario      BIGINT          NULL,
            fecha_operativa           DATE            NOT NULL,               -- día operativo Colombia (mismo criterio que 70.3)
            fecha_apertura_utc        DATETIME2       NOT NULL,
            fecha_cierre_utc          DATETIME2       NULL,
            estado                    VARCHAR(30)     NOT NULL DEFAULT 'ABIERTA',
            hora_limite_cierre        TIME            NOT NULL,               -- congelada al abrir — nunca se relee la config. de la sede en vivo
            fondo_inicial             DECIMAL(18,2)   NOT NULL DEFAULT 0,
            efectivo_esperado         DECIMAL(18,2)   NULL,                   -- snapshot al iniciar cuadre o al vencer estando ABIERTA
            efectivo_contado          DECIMAL(18,2)   NULL,                   -- informado por el cajero al cerrar manualmente
            diferencia                DECIMAL(18,2)   NULL,                   -- efectivo_contado - efectivo_esperado
            cerrada_automaticamente   BIT             NOT NULL DEFAULT 0,     -- distingue el origen incluso tras pasar a REVISADA
            observaciones_cajero      VARCHAR(500)    NULL,
            fecha_revision            DATETIME2       NULL,
            observaciones_revision    VARCHAR(500)    NULL,
            created_at                DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),

            CONSTRAINT ck_wcc_estado CHECK (estado IN (
                'ABIERTA','EN_CUADRE','CERRADA','CON_DIFERENCIA',
                'CERRADA_AUTOMATICAMENTE','REVISADA')),

            CONSTRAINT ck_wcc_fondo_inicial CHECK (fondo_inicial >= 0),

            CONSTRAINT ck_wcc_efectivo_contado CHECK (
                efectivo_contado IS NULL OR efectivo_contado >= 0),

            -- Consistencia estado↔columnas dependientes — una combinación exacta
            -- por estado; REVISADA no impone condición adicional (conserva los
            -- valores de su estado de origen, distinguible por cerrada_automaticamente).
            CONSTRAINT ck_wcc_consistencia_estado CHECK (
                   (estado = 'ABIERTA'
                        AND fecha_cierre_utc IS NULL
                        AND efectivo_contado IS NULL
                        AND diferencia IS NULL)
                OR (estado = 'EN_CUADRE'
                        AND efectivo_esperado IS NOT NULL
                        AND fecha_cierre_utc IS NULL
                        AND efectivo_contado IS NULL
                        AND diferencia IS NULL)
                OR (estado = 'CERRADA'
                        AND efectivo_esperado IS NOT NULL
                        AND efectivo_contado IS NOT NULL
                        AND diferencia = 0
                        AND fecha_cierre_utc IS NOT NULL
                        AND cerrada_automaticamente = 0)
                OR (estado = 'CON_DIFERENCIA'
                        AND efectivo_esperado IS NOT NULL
                        AND efectivo_contado IS NOT NULL
                        AND diferencia <> 0
                        AND fecha_cierre_utc IS NOT NULL
                        AND cerrada_automaticamente = 0)
                OR (estado = 'CERRADA_AUTOMATICAMENTE'
                        AND efectivo_esperado IS NOT NULL
                        AND efectivo_contado IS NULL
                        AND diferencia IS NULL
                        AND fecha_cierre_utc IS NOT NULL
                        AND cerrada_automaticamente = 1)
                OR (estado = 'REVISADA')
            ),

            CONSTRAINT uq_wcc_usuario_comercio_fecha
                UNIQUE (id_usuario_cajero, id_comercio, fecha_operativa),

            CONSTRAINT fk_wcc_comercio          FOREIGN KEY (id_comercio)          REFERENCES comercios(id_comercio),
            CONSTRAINT fk_wcc_comercio_aliado    FOREIGN KEY (id_comercio_aliado)   REFERENCES comercios_aliados(id_comercio_aliado),
            CONSTRAINT fk_wcc_establecimiento    FOREIGN KEY (id_establecimiento)   REFERENCES comercio_establecimientos(id_establecimiento),
            CONSTRAINT fk_wcc_usuario_cajero     FOREIGN KEY (id_usuario_cajero)    REFERENCES usuarios(id_usuario),
            CONSTRAINT fk_wcc_revisado_por       FOREIGN KEY (revisado_por_usuario) REFERENCES usuarios(id_usuario)
        );
        PRINT 'OK: tabla wallet_cajas_comercio creada';
    END
    ELSE
        PRINT 'SKIP: tabla wallet_cajas_comercio ya existe';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wcc_comercio_fecha' AND object_id=OBJECT_ID('wallet_cajas_comercio'))
        CREATE INDEX ix_wcc_comercio_fecha ON wallet_cajas_comercio (id_comercio, fecha_operativa);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wcc_establecimiento_fecha_estado' AND object_id=OBJECT_ID('wallet_cajas_comercio'))
        CREATE INDEX ix_wcc_establecimiento_fecha_estado ON wallet_cajas_comercio (id_establecimiento, fecha_operativa, estado);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wcc_estado_establecimiento' AND object_id=OBJECT_ID('wallet_cajas_comercio'))
        CREATE INDEX ix_wcc_estado_establecimiento ON wallet_cajas_comercio (estado, id_establecimiento);

    PRINT 'OK: índices de wallet_cajas_comercio verificados/creados';

    -- ── 2. Tabla wallet_caja_movimientos ─────────────────────────────────

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='wallet_caja_movimientos')
    BEGIN
        CREATE TABLE wallet_caja_movimientos (
            id_movimiento     BIGINT          IDENTITY(1,1) PRIMARY KEY,
            id_caja           BIGINT          NOT NULL,
            id_recarga        BIGINT          NULL,                          -- poblado únicamente cuando tipo_movimiento='RECARGA_EFECTIVO'
            tipo_movimiento   VARCHAR(30)     NOT NULL,                      -- 6 valores permitidos por esquema; solo 2 insertables en 70.4.1
            naturaleza        CHAR(1)         NOT NULL,                      -- 'E' entrada / 'S' salida de efectivo
            valor             DECIMAL(18,2)   NOT NULL,
            observaciones     VARCHAR(500)    NULL,
            created_at        DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),

            CONSTRAINT ck_wcjm_tipo_movimiento CHECK (tipo_movimiento IN (
                'FONDO_INICIAL','RECARGA_EFECTIVO','OTRO_INGRESO',
                'RETIRO','DEVOLUCION','ENTREGA_PARCIAL')),

            CONSTRAINT ck_wcjm_naturaleza CHECK (naturaleza IN ('E','S')),
            CONSTRAINT ck_wcjm_valor      CHECK (valor >= 0),

            CONSTRAINT fk_wcjm_caja    FOREIGN KEY (id_caja)    REFERENCES wallet_cajas_comercio(id_caja),
            CONSTRAINT fk_wcjm_recarga FOREIGN KEY (id_recarga) REFERENCES wallet_recargas_comercio(id_recarga)
        );
        PRINT 'OK: tabla wallet_caja_movimientos creada';
    END
    ELSE
        PRINT 'SKIP: tabla wallet_caja_movimientos ya existe';

    -- Índice único filtrado — una recarga no puede vincularse a más de una
    -- caja. Mismo patrón ya probado en el proyecto (IX_wallets_persona_activa,
    -- migración 002, dentro del baseline de CI).
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='uq_wcjm_recarga' AND object_id=OBJECT_ID('wallet_caja_movimientos'))
        CREATE UNIQUE INDEX uq_wcjm_recarga ON wallet_caja_movimientos (id_recarga) WHERE id_recarga IS NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wcjm_caja_created' AND object_id=OBJECT_ID('wallet_caja_movimientos'))
        CREATE INDEX ix_wcjm_caja_created ON wallet_caja_movimientos (id_caja, created_at);

    PRINT 'OK: índices de wallet_caja_movimientos verificados/creados';

    -- ── 3. Columna aditiva sobre comercio_establecimientos ───────────────
    -- NULL = usa el valor por defecto del sistema (21:00 hora Colombia),
    -- resuelto en la aplicación al abrir una caja, nunca en esta migración.

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('comercio_establecimientos') AND name='hora_cierre_automatico_caja')
    BEGIN
        ALTER TABLE comercio_establecimientos ADD hora_cierre_automatico_caja TIME NULL;
        PRINT 'OK: columna hora_cierre_automatico_caja agregada a comercio_establecimientos';
    END
    ELSE
        PRINT 'SKIP: columna hora_cierre_automatico_caja ya existe';

    COMMIT TRANSACTION;
    PRINT '=== FIN MIGRACIÓN 028 (COMMIT OK) ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '=== ERROR EN MIGRACIÓN 028 — ROLLBACK EJECUTADO ===';
    THROW;
END CATCH;

-- ── 4. Verificación final ────────────────────────────────────────────────

SELECT name AS tabla FROM sys.tables
WHERE name IN ('wallet_cajas_comercio', 'wallet_caja_movimientos');

SELECT t.name AS tabla, c.name AS columna, ty.name AS tipo, c.is_nullable
FROM sys.columns c
JOIN sys.tables t ON t.object_id = c.object_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE t.name IN ('wallet_cajas_comercio', 'wallet_caja_movimientos')
ORDER BY t.name, c.column_id;

SELECT t.name AS tabla, cc.name AS check_constraint, cc.definition
FROM sys.check_constraints cc
JOIN sys.tables t ON t.object_id = cc.parent_object_id
WHERE t.name IN ('wallet_cajas_comercio', 'wallet_caja_movimientos');

SELECT t.name AS tabla, fk.name AS foreign_key, rt.name AS tabla_referenciada
FROM sys.foreign_keys fk
JOIN sys.tables t  ON t.object_id  = fk.parent_object_id
JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
WHERE t.name IN ('wallet_cajas_comercio', 'wallet_caja_movimientos');

SELECT t.name AS tabla, i.name AS indice, i.is_unique, i.has_filter, i.filter_definition
FROM sys.indexes i
JOIN sys.tables t ON t.object_id = i.object_id
WHERE t.name IN ('wallet_cajas_comercio', 'wallet_caja_movimientos')
  AND i.name IS NOT NULL;

SELECT name AS columna FROM sys.columns
WHERE object_id=OBJECT_ID('comercio_establecimientos')
  AND name='hora_cierre_automatico_caja';

PRINT '=== VERIFICACIÓN COMPLETA ===';
