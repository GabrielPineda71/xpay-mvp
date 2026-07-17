-- =====================================================================
-- Migración 021: Cartera Ordinaria — tablas base, parámetros y cupos QA
-- Idempotente — no toca saldos, ledger, wallets reales, ni producción
-- =====================================================================

PRINT '=== INICIO MIGRACIÓN 021: Cartera Ordinaria ===';

-- ── 1. cartera_parametros_utilizacion ─────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='cartera_parametros_utilizacion')
BEGIN
    CREATE TABLE cartera_parametros_utilizacion (
        id_parametro        BIGINT          IDENTITY(1,1) PRIMARY KEY,
        tipo_utilizacion    VARCHAR(30)     NOT NULL,   -- COMPRA_COMERCIO | AVANCE_WALLET
        tasa_emv            DECIMAL(9,4)    NOT NULL,
        porc_aval           DECIMAL(9,4)    NOT NULL DEFAULT 0,
        porc_admin          DECIMAL(9,4)    NOT NULL DEFAULT 0,
        aplica_iva          BIT             NOT NULL DEFAULT 0,
        porc_iva            DECIMAL(9,4)    NOT NULL DEFAULT 0,
        plazo_min           INT             NOT NULL DEFAULT 1,
        plazo_max           INT             NOT NULL DEFAULT 36,
        frecuencia          VARCHAR(20)     NOT NULL DEFAULT 'MENSUAL',
        monto_min           DECIMAL(18,2)   NOT NULL DEFAULT 50000,
        monto_max           DECIMAL(18,2)   NOT NULL DEFAULT 5000000,
        estado              VARCHAR(20)     NOT NULL DEFAULT 'ACTIVO',
        created_at          DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
        updated_at          DATETIME2       NULL,
        created_by_usuario  BIGINT          NULL,
        updated_by_usuario  BIGINT          NULL,
        CONSTRAINT uq_cart_param_util_tipo UNIQUE (tipo_utilizacion)
    );
    PRINT 'OK: cartera_parametros_utilizacion creada';
END
ELSE
    PRINT 'SKIP: cartera_parametros_utilizacion ya existe';

-- ── 2. cartera_parametros_gastos_cobranza ─────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='cartera_parametros_gastos_cobranza')
BEGIN
    CREATE TABLE cartera_parametros_gastos_cobranza (
        id_gasto        BIGINT          IDENTITY(1,1) PRIMARY KEY,
        dias_desde      INT             NOT NULL,
        dias_hasta      INT             NULL,
        tipo_cobro      VARCHAR(20)     NOT NULL DEFAULT 'FIJO',
        valor_cobro     DECIMAL(18,4)   NOT NULL,
        descripcion     VARCHAR(200)    NULL,
        estado          VARCHAR(20)     NOT NULL DEFAULT 'ACTIVO',
        created_at      DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
        updated_at      DATETIME2       NULL
    );
    PRINT 'OK: cartera_parametros_gastos_cobranza creada';
END
ELSE
    PRINT 'SKIP: cartera_parametros_gastos_cobranza ya existe';

-- ── 3. cartera_politicas_credito ──────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='cartera_politicas_credito')
BEGIN
    CREATE TABLE cartera_politicas_credito (
        id_politica                 BIGINT          IDENTITY(1,1) PRIMARY KEY,
        score_datacredito_minimo    INT             NULL,
        requiere_veriff             BIT             NOT NULL DEFAULT 0,
        cupo_minimo                 DECIMAL(18,2)   NOT NULL DEFAULT 100000,
        cupo_maximo                 DECIMAL(18,2)   NOT NULL DEFAULT 5000000,
        edad_minima                 INT             NOT NULL DEFAULT 18,
        edad_maxima                 INT             NOT NULL DEFAULT 70,
        estado                      VARCHAR(20)     NOT NULL DEFAULT 'ACTIVO',
        vigente_desde               DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
        vigente_hasta               DATETIME2       NULL,
        created_at                  DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
        updated_at                  DATETIME2       NULL,
        created_by_usuario          BIGINT          NULL
    );
    PRINT 'OK: cartera_politicas_credito creada';
END
ELSE
    PRINT 'SKIP: cartera_politicas_credito ya existe';

-- ── 4. cartera_cupos_ordinarios ────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='cartera_cupos_ordinarios')
BEGIN
    CREATE TABLE cartera_cupos_ordinarios (
        id_cupo                 BIGINT          IDENTITY(1,1) PRIMARY KEY,
        id_usuario              BIGINT          NOT NULL,
        id_wallet               BIGINT          NOT NULL,
        cupo_aprobado           DECIMAL(18,2)   NOT NULL,
        cupo_usado              DECIMAL(18,2)   NOT NULL DEFAULT 0,
        estado                  VARCHAR(20)     NOT NULL DEFAULT 'ACTIVO',
        fecha_aprobacion        DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
        fecha_vencimiento       DATETIME2       NULL,
        aprobado_por_usuario    BIGINT          NULL,
        observaciones           VARCHAR(500)    NULL,
        created_at              DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
        updated_at              DATETIME2       NULL,
        CONSTRAINT uq_cart_cupo_usuario UNIQUE (id_usuario)
    );
    PRINT 'OK: cartera_cupos_ordinarios creada';
END
ELSE
    PRINT 'SKIP: cartera_cupos_ordinarios ya existe';

-- ── 5. cartera_utilizaciones ──────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='cartera_utilizaciones')
BEGIN
    CREATE TABLE cartera_utilizaciones (
        id_utilizacion          BIGINT          IDENTITY(1,1) PRIMARY KEY,
        id_cupo                 BIGINT          NOT NULL,
        id_usuario              BIGINT          NOT NULL,
        id_wallet               BIGINT          NOT NULL,
        tipo_utilizacion        VARCHAR(30)     NOT NULL,
        id_comercio_aliado      BIGINT          NULL,
        valor_capital           DECIMAL(18,2)   NOT NULL,
        tasa_emv                DECIMAL(9,4)    NOT NULL,
        porc_aval               DECIMAL(9,4)    NOT NULL DEFAULT 0,
        porc_admin              DECIMAL(9,4)    NOT NULL DEFAULT 0,
        aplica_iva              BIT             NOT NULL DEFAULT 0,
        porc_iva                DECIMAL(9,4)    NOT NULL DEFAULT 0,
        plazo_meses             INT             NOT NULL,
        frecuencia              VARCHAR(20)     NOT NULL DEFAULT 'MENSUAL',
        total_cuotas            INT             NOT NULL,
        valor_cuota             DECIMAL(18,2)   NOT NULL,
        valor_total_aval        DECIMAL(18,2)   NOT NULL DEFAULT 0,
        valor_total_admin       DECIMAL(18,2)   NOT NULL DEFAULT 0,
        valor_total_iva         DECIMAL(18,2)   NOT NULL DEFAULT 0,
        valor_total_intereses   DECIMAL(18,2)   NOT NULL DEFAULT 0,
        valor_total_pagar       DECIMAL(18,2)   NOT NULL,
        estado                  VARCHAR(20)     NOT NULL DEFAULT 'SIMULADO',
        fecha_solicitud         DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
        fecha_desembolso        DATETIME2       NULL,
        observaciones           VARCHAR(500)    NULL,
        created_at              DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
        updated_at              DATETIME2       NULL,
        created_by_usuario      BIGINT          NULL
    );
    PRINT 'OK: cartera_utilizaciones creada';
END
ELSE
    PRINT 'SKIP: cartera_utilizaciones ya existe';

-- ── 6. cartera_cuotas ─────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='cartera_cuotas')
BEGIN
    CREATE TABLE cartera_cuotas (
        id_cuota                BIGINT          IDENTITY(1,1) PRIMARY KEY,
        id_utilizacion          BIGINT          NOT NULL,
        numero_cuota            INT             NOT NULL,
        fecha_vencimiento       DATE            NOT NULL,
        valor_capital           DECIMAL(18,2)   NOT NULL,
        valor_interes           DECIMAL(18,2)   NOT NULL,
        valor_aval              DECIMAL(18,2)   NOT NULL DEFAULT 0,
        valor_admin             DECIMAL(18,2)   NOT NULL DEFAULT 0,
        valor_iva               DECIMAL(18,2)   NOT NULL DEFAULT 0,
        valor_total             DECIMAL(18,2)   NOT NULL,
        saldo_capital_antes     DECIMAL(18,2)   NOT NULL,
        saldo_capital_despues   DECIMAL(18,2)   NOT NULL,
        estado                  VARCHAR(20)     NOT NULL DEFAULT 'PENDIENTE',
        fecha_pago              DATETIME2       NULL,
        id_pago                 BIGINT          NULL,
        created_at              DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
        updated_at              DATETIME2       NULL
    );
    PRINT 'OK: cartera_cuotas creada';
END
ELSE
    PRINT 'SKIP: cartera_cuotas ya existe';

-- ── 7. cartera_pagos ──────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='cartera_pagos')
BEGIN
    CREATE TABLE cartera_pagos (
        id_pago             BIGINT          IDENTITY(1,1) PRIMARY KEY,
        id_utilizacion      BIGINT          NOT NULL,
        id_usuario          BIGINT          NOT NULL,
        id_wallet           BIGINT          NOT NULL,
        valor_pago          DECIMAL(18,2)   NOT NULL,
        fecha_pago          DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
        tipo_pago           VARCHAR(30)     NOT NULL DEFAULT 'CUOTA_NORMAL',
        estado              VARCHAR(20)     NOT NULL DEFAULT 'REGISTRADO',
        observaciones       VARCHAR(500)    NULL,
        created_at          DATETIME2       NOT NULL DEFAULT GETUTCDATE(),
        created_by_usuario  BIGINT          NULL
    );
    PRINT 'OK: cartera_pagos creada';
END
ELSE
    PRINT 'SKIP: cartera_pagos ya existe';

-- ── 8. cartera_pagos_detalle ──────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='cartera_pagos_detalle')
BEGIN
    CREATE TABLE cartera_pagos_detalle (
        id_detalle      BIGINT          IDENTITY(1,1) PRIMARY KEY,
        id_pago         BIGINT          NOT NULL,
        id_cuota        BIGINT          NOT NULL,
        valor_capital   DECIMAL(18,2)   NOT NULL DEFAULT 0,
        valor_interes   DECIMAL(18,2)   NOT NULL DEFAULT 0,
        valor_aval      DECIMAL(18,2)   NOT NULL DEFAULT 0,
        valor_admin     DECIMAL(18,2)   NOT NULL DEFAULT 0,
        valor_iva       DECIMAL(18,2)   NOT NULL DEFAULT 0,
        valor_total     DECIMAL(18,2)   NOT NULL,
        created_at      DATETIME2       NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT 'OK: cartera_pagos_detalle creada';
END
ELSE
    PRINT 'SKIP: cartera_pagos_detalle ya existe';

-- ── 9. Seed parámetros de utilización ─────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM cartera_parametros_utilizacion WHERE tipo_utilizacion='COMPRA_COMERCIO')
BEGIN
    INSERT INTO cartera_parametros_utilizacion
        (tipo_utilizacion, tasa_emv, porc_aval, porc_admin, aplica_iva, porc_iva, plazo_min, plazo_max, frecuencia, monto_min, monto_max, estado)
    VALUES
        ('COMPRA_COMERCIO', 2.5000, 1.2000, 0.8000, 1, 19.0000, 1, 24, 'MENSUAL',   50000, 3000000, 'ACTIVO'),
        ('AVANCE_WALLET',   3.0000, 1.5000, 1.0000, 1, 19.0000, 1, 12, 'MENSUAL',  100000, 1000000, 'ACTIVO');
    PRINT 'OK: parámetros de utilización seed insertados';
END
ELSE
    PRINT 'SKIP: parámetros de utilización ya existen';

-- ── 10. Seed gastos de cobranza ───────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM cartera_parametros_gastos_cobranza WHERE dias_desde=0)
BEGIN
    INSERT INTO cartera_parametros_gastos_cobranza
        (dias_desde, dias_hasta, tipo_cobro, valor_cobro, descripcion, estado)
    VALUES
        (0,  30, 'PORCENTAJE', 0.5000, 'Gestión preventiva 0-30 días',  'ACTIVO'),
        (31, 60, 'PORCENTAJE', 1.5000, 'Gestión temprana 31-60 días',   'ACTIVO'),
        (61, 90, 'PORCENTAJE', 3.0000, 'Gestión media 61-90 días',      'ACTIVO'),
        (91, NULL,'FIJO',     50000,   'Cobro jurídico >90 días',       'ACTIVO');
    PRINT 'OK: gastos de cobranza seed insertados';
END
ELSE
    PRINT 'SKIP: gastos de cobranza ya existen';

-- ── 11. Seed política de crédito ──────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM cartera_politicas_credito WHERE estado='ACTIVO')
BEGIN
    INSERT INTO cartera_politicas_credito
        (score_datacredito_minimo, requiere_veriff, cupo_minimo, cupo_maximo, edad_minima, edad_maxima, estado, vigente_desde)
    VALUES
        (NULL, 0, 100000, 5000000, 18, 70, 'ACTIVO', GETUTCDATE());
    PRINT 'OK: política de crédito seed insertada';
END
ELSE
    PRINT 'SKIP: política de crédito ya existe';

-- ── 12. Seed cupos QA (qa.usuario1 y qa.usuario2) ─────────────────────
IF NOT EXISTS (SELECT 1 FROM cartera_cupos_ordinarios WHERE id_usuario=3)
BEGIN
    INSERT INTO cartera_cupos_ordinarios
        (id_usuario, id_wallet, cupo_aprobado, cupo_usado, estado, fecha_aprobacion, observaciones)
    VALUES
        (3, 2, 1000000.00, 0, 'ACTIVO', GETUTCDATE(), 'Cupo QA para qa.usuario1');
    PRINT 'OK: cupo qa.usuario1 ($1.000.000) creado';
END
ELSE
    PRINT 'SKIP: cupo qa.usuario1 ya existe';

IF NOT EXISTS (SELECT 1 FROM cartera_cupos_ordinarios WHERE id_usuario=4)
BEGIN
    INSERT INTO cartera_cupos_ordinarios
        (id_usuario, id_wallet, cupo_aprobado, cupo_usado, estado, fecha_aprobacion, observaciones)
    VALUES
        (4, 3, 800000.00, 0, 'ACTIVO', GETUTCDATE(), 'Cupo QA para qa.usuario2');
    PRINT 'OK: cupo qa.usuario2 ($800.000) creado';
END
ELSE
    PRINT 'SKIP: cupo qa.usuario2 ya existe';

-- ── 13. Verificar resultado ────────────────────────────────────────────
SELECT 'parametros_utilizacion' AS tabla, COUNT(*) AS filas FROM cartera_parametros_utilizacion
UNION ALL
SELECT 'gastos_cobranza', COUNT(*) FROM cartera_parametros_gastos_cobranza
UNION ALL
SELECT 'politicas_credito', COUNT(*) FROM cartera_politicas_credito
UNION ALL
SELECT 'cupos_ordinarios', COUNT(*) FROM cartera_cupos_ordinarios;

PRINT '=== FIN MIGRACIÓN 021 ===';
