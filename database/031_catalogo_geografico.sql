-- =====================================================================
-- Migración 031: Catálogo geográfico (Commit 1 — Motor de Evaluación de
-- Crédito Datacrédito / onboarding móvil, Fase 0)
-- Idempotente y fail-fast — no borra datos, no altera columnas existentes,
-- no agrega columnas automáticamente, no toca ninguna otra tabla. Tres
-- tablas 100% nuevas (dbo.catalogo_paises, dbo.catalogo_departamentos,
-- dbo.catalogo_ciudades), sin relación con ninguna tabla preexistente.
--
-- Fuente de los datos sembrados: DANE, Geoportal oficial —
--   https://geoportal.dane.gov.co/descargas/divipola/DIVIPOLA_Departamentos.xlsx
--   https://geoportal.dane.gov.co/descargas/divipola/DIVIPOLA_Municipios.xlsx
-- Versión: "Codificación de la División Político Administrativa de Colombia
-- - DIVIPOLA junio 2026", actualizado al 30 de junio de 2026. Códigos
-- DIVIPOLA preservados exactamente como los publica el DANE (texto, sin
-- reinterpretar, sin renumerar). Conteos verificados contra la nota oficial
-- del propio archivo de municipios ("1.103 municipios, 18 áreas no
-- municipalizadas y la isla de San Andrés") antes de generar este script.
--
-- Comportamiento:
--   1) Si una tabla NO existe: la crea completa, con PK, FK, UNIQUE y CHECK.
--   2) Si YA existe: NO la modifica ni la repara silenciosamente. Verifica
--      columnas y tipos críticos, PK, FKs, UNIQUE y CHECK — si CUALQUIERA de
--      estas verificaciones falla, aborta con THROW (rollback automático por
--      XACT_ABORT), sin alterar nada.
--   3) Semilla (países/departamentos/ciudades): idempotente por diseño —
--      cada INSERT usa WHERE NOT EXISTS por clave natural (codigo /
--      codigo_divipola), de modo que volver a ejecutar este script sobre una
--      base ya sembrada no duplica ninguna fila.
--
-- Modelo:
--   dbo.catalogo_paises        — 1 fila sembrada (Colombia, codigo='COL').
--                                 Tabla preparada para más países a futuro,
--                                 sin sembrar ninguno más en este commit.
--   dbo.catalogo_departamentos — 33 filas (32 departamentos reales + Bogotá
--                                 D.C., codificada como departamento por el
--                                 propio DANE para fines estadísticos, según
--                                 la nota oficial del archivo de origen).
--   dbo.catalogo_ciudades      — 1.122 filas. Columna tipo (discriminador)
--                                 preserva la clasificación oficial del DANE
--                                 sin inventar una taxonomía nueva:
--                                   'MUNICIPIO'               (1.103 filas)
--                                   'ISLA'                     (1 fila — San Andrés, código 88001)
--                                   'AREA_NO_MUNICIPALIZADA'  (18 filas — Amazonas/Guainía/Vaupés/Vichada)
--                                 Las tres son igualmente seleccionables en
--                                 el onboarding — el discriminador es solo
--                                 informativo/de trazabilidad frente al DANE,
--                                 no restringe selección.
--
-- Esta migración NO crea: índices adicionales de búsqueda por nombre
-- (no requeridos por el Commit 1 — los endpoints de solo lectura devuelven
-- catálogos completos por departamento, sin búsqueda libre todavía), ni
-- ninguna tabla relacionada con Persona/Datacrédito/Veriff (eso corresponde
-- a commits posteriores, no autorizados todavía).
--
-- NO EJECUTADA POR EL AGENTE — preparada para revisión y ejecución manual
-- del usuario.
-- =====================================================================

SET XACT_ABORT ON;

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT '=== INICIO MIGRACIÓN 031: Catálogo geográfico ===';

    -- ═══════════════════════════════════════════════════════════════════
    -- 1) dbo.catalogo_paises
    -- ═══════════════════════════════════════════════════════════════════
    DECLARE @objIdPaises INT = OBJECT_ID('dbo.catalogo_paises', 'U');

    IF @objIdPaises IS NULL
    BEGIN
        CREATE TABLE dbo.catalogo_paises (
            id_pais                 BIGINT          IDENTITY(1,1) NOT NULL,
            codigo                  VARCHAR(3)      NOT NULL,   -- ISO 3166-1 alpha-3, p.ej. 'COL'
            nombre                  VARCHAR(100)    NOT NULL,
            estado                  VARCHAR(20)     NOT NULL DEFAULT 'ACTIVO',
            fecha_creacion          DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
            fecha_actualizacion     DATETIME2(3)    NULL,

            CONSTRAINT pk_catalogo_paises PRIMARY KEY CLUSTERED (id_pais),
            CONSTRAINT uq_catalogo_paises_codigo UNIQUE (codigo),
            CONSTRAINT ck_catalogo_paises_estado CHECK (estado IN ('ACTIVO', 'INACTIVO'))
        );

        SET @objIdPaises = OBJECT_ID('dbo.catalogo_paises', 'U');
        PRINT 'OK: tabla dbo.catalogo_paises creada';
    END
    ELSE
    BEGIN
        PRINT 'INFO: dbo.catalogo_paises ya existe — verificando estructura crítica...';

        IF EXISTS (
            SELECT 1 FROM (VALUES ('id_pais'), ('codigo'), ('nombre'), ('estado'), ('fecha_creacion'), ('fecha_actualizacion')) AS req(col)
            WHERE NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = @objIdPaises AND c.name = req.col)
        )
            THROW 52000, N'Migración 031 abortada: dbo.catalogo_paises ya existe pero le faltan columnas requeridas. No se agregan automáticamente — revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id WHERE c.object_id = @objIdPaises AND c.name = 'codigo' AND ty.name = 'varchar' AND c.max_length = 3)
            THROW 52001, N'Migración 031 abortada: dbo.catalogo_paises.codigo no es varchar(3) — revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = @objIdPaises AND type = 'PK')
            THROW 52002, N'Migración 031 abortada: dbo.catalogo_paises existe pero no tiene clave primaria — revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = @objIdPaises AND type = 'UQ' AND name = 'uq_catalogo_paises_codigo')
            THROW 52003, N'Migración 031 abortada: dbo.catalogo_paises existe pero falta la restricción UNIQUE uq_catalogo_paises_codigo — revisar manualmente.', 1;

        PRINT 'OK: estructura crítica de dbo.catalogo_paises verificada';
    END

    -- ═══════════════════════════════════════════════════════════════════
    -- 2) dbo.catalogo_departamentos
    -- ═══════════════════════════════════════════════════════════════════
    DECLARE @objIdDeptos INT = OBJECT_ID('dbo.catalogo_departamentos', 'U');

    IF @objIdDeptos IS NULL
    BEGIN
        CREATE TABLE dbo.catalogo_departamentos (
            id_departamento         BIGINT          IDENTITY(1,1) NOT NULL,
            id_pais                 BIGINT          NOT NULL,
            codigo_divipola         VARCHAR(2)      NOT NULL,   -- código DIVIPOLA de departamento, p.ej. '05', '11', '88'
            nombre                  VARCHAR(100)    NOT NULL,
            estado                  VARCHAR(20)     NOT NULL DEFAULT 'ACTIVO',
            fecha_creacion          DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
            fecha_actualizacion     DATETIME2(3)    NULL,

            CONSTRAINT pk_catalogo_departamentos PRIMARY KEY CLUSTERED (id_departamento),
            CONSTRAINT fk_catalogo_departamentos_pais FOREIGN KEY (id_pais) REFERENCES dbo.catalogo_paises (id_pais),
            CONSTRAINT uq_catalogo_departamentos_pais_codigo UNIQUE (id_pais, codigo_divipola),
            CONSTRAINT ck_catalogo_departamentos_estado CHECK (estado IN ('ACTIVO', 'INACTIVO'))
        );

        SET @objIdDeptos = OBJECT_ID('dbo.catalogo_departamentos', 'U');
        PRINT 'OK: tabla dbo.catalogo_departamentos creada';
    END
    ELSE
    BEGIN
        PRINT 'INFO: dbo.catalogo_departamentos ya existe — verificando estructura crítica...';

        IF EXISTS (
            SELECT 1 FROM (VALUES ('id_departamento'), ('id_pais'), ('codigo_divipola'), ('nombre'), ('estado'), ('fecha_creacion'), ('fecha_actualizacion')) AS req(col)
            WHERE NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = @objIdDeptos AND c.name = req.col)
        )
            THROW 52010, N'Migración 031 abortada: dbo.catalogo_departamentos ya existe pero le faltan columnas requeridas — revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id WHERE c.object_id = @objIdDeptos AND c.name = 'codigo_divipola' AND ty.name = 'varchar' AND c.max_length = 2)
            THROW 52011, N'Migración 031 abortada: dbo.catalogo_departamentos.codigo_divipola no es varchar(2) — revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @objIdDeptos AND name = 'fk_catalogo_departamentos_pais')
            THROW 52012, N'Migración 031 abortada: dbo.catalogo_departamentos existe pero falta la FK fk_catalogo_departamentos_pais — revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = @objIdDeptos AND type = 'UQ' AND name = 'uq_catalogo_departamentos_pais_codigo')
            THROW 52013, N'Migración 031 abortada: dbo.catalogo_departamentos existe pero falta la restricción UNIQUE uq_catalogo_departamentos_pais_codigo — revisar manualmente.', 1;

        PRINT 'OK: estructura crítica de dbo.catalogo_departamentos verificada';
    END

    -- ═══════════════════════════════════════════════════════════════════
    -- 3) dbo.catalogo_ciudades
    -- ═══════════════════════════════════════════════════════════════════
    DECLARE @objIdCiudades INT = OBJECT_ID('dbo.catalogo_ciudades', 'U');

    IF @objIdCiudades IS NULL
    BEGIN
        CREATE TABLE dbo.catalogo_ciudades (
            id_ciudad               BIGINT          IDENTITY(1,1) NOT NULL,
            id_departamento         BIGINT          NOT NULL,
            codigo_divipola         VARCHAR(5)      NOT NULL,   -- código DIVIPOLA completo de municipio/isla/área, p.ej. '05001'
            nombre                  VARCHAR(100)    NOT NULL,
            tipo                    VARCHAR(30)     NOT NULL,   -- clasificación oficial DANE: MUNICIPIO / ISLA / AREA_NO_MUNICIPALIZADA
            estado                  VARCHAR(20)     NOT NULL DEFAULT 'ACTIVO',
            fecha_creacion          DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
            fecha_actualizacion     DATETIME2(3)    NULL,

            CONSTRAINT pk_catalogo_ciudades PRIMARY KEY CLUSTERED (id_ciudad),
            CONSTRAINT fk_catalogo_ciudades_departamento FOREIGN KEY (id_departamento) REFERENCES dbo.catalogo_departamentos (id_departamento),
            CONSTRAINT uq_catalogo_ciudades_departamento_codigo UNIQUE (id_departamento, codigo_divipola),
            CONSTRAINT ck_catalogo_ciudades_tipo CHECK (tipo IN ('MUNICIPIO', 'ISLA', 'AREA_NO_MUNICIPALIZADA')),
            CONSTRAINT ck_catalogo_ciudades_estado CHECK (estado IN ('ACTIVO', 'INACTIVO'))
        );

        SET @objIdCiudades = OBJECT_ID('dbo.catalogo_ciudades', 'U');
        PRINT 'OK: tabla dbo.catalogo_ciudades creada';
    END
    ELSE
    BEGIN
        PRINT 'INFO: dbo.catalogo_ciudades ya existe — verificando estructura crítica...';

        IF EXISTS (
            SELECT 1 FROM (VALUES ('id_ciudad'), ('id_departamento'), ('codigo_divipola'), ('nombre'), ('tipo'), ('estado'), ('fecha_creacion'), ('fecha_actualizacion')) AS req(col)
            WHERE NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = @objIdCiudades AND c.name = req.col)
        )
            THROW 52020, N'Migración 031 abortada: dbo.catalogo_ciudades ya existe pero le faltan columnas requeridas — revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id WHERE c.object_id = @objIdCiudades AND c.name = 'codigo_divipola' AND ty.name = 'varchar' AND c.max_length = 5)
            THROW 52021, N'Migración 031 abortada: dbo.catalogo_ciudades.codigo_divipola no es varchar(5) — revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = @objIdCiudades AND name = 'fk_catalogo_ciudades_departamento')
            THROW 52022, N'Migración 031 abortada: dbo.catalogo_ciudades existe pero falta la FK fk_catalogo_ciudades_departamento — revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = @objIdCiudades AND type = 'UQ' AND name = 'uq_catalogo_ciudades_departamento_codigo')
            THROW 52023, N'Migración 031 abortada: dbo.catalogo_ciudades existe pero falta la restricción UNIQUE uq_catalogo_ciudades_departamento_codigo — revisar manualmente.', 1;

        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = @objIdCiudades AND name = 'ck_catalogo_ciudades_tipo')
            THROW 52024, N'Migración 031 abortada: dbo.catalogo_ciudades existe pero falta el CHECK ck_catalogo_ciudades_tipo — revisar manualmente.', 1;

        PRINT 'OK: estructura crítica de dbo.catalogo_ciudades verificada';
    END

    -- ═══════════════════════════════════════════════════════════════════
    -- 4) Semilla — idempotente por clave natural (WHERE NOT EXISTS)
    -- ═══════════════════════════════════════════════════════════════════

    -- 4.1) País
    IF NOT EXISTS (SELECT 1 FROM dbo.catalogo_paises WHERE codigo = 'COL')
    BEGIN
        INSERT INTO dbo.catalogo_paises (codigo, nombre) VALUES ('COL', N'Colombia');
        PRINT 'OK: país COL sembrado';
    END
    ELSE
        PRINT 'INFO: país COL ya existía — no se duplicó';

    DECLARE @idPaisCol BIGINT = (SELECT id_pais FROM dbo.catalogo_paises WHERE codigo = 'COL');

    -- 4.2) Departamentos (33 filas — 32 departamentos + Bogotá D.C., fuente DANE)
    INSERT INTO dbo.catalogo_departamentos (id_pais, codigo_divipola, nombre)
    SELECT @idPaisCol, v.codigo, v.nombre
    FROM (VALUES
        ('05', N'ANTIOQUIA'),
        ('08', N'ATLÁNTICO'),
        ('11', N'BOGOTÁ, D.C.'),
        ('13', N'BOLÍVAR'),
        ('15', N'BOYACÁ'),
        ('17', N'CALDAS'),
        ('18', N'CAQUETÁ'),
        ('19', N'CAUCA'),
        ('20', N'CESAR'),
        ('23', N'CÓRDOBA'),
        ('25', N'CUNDINAMARCA'),
        ('27', N'CHOCÓ'),
        ('41', N'HUILA'),
        ('44', N'LA GUAJIRA'),
        ('47', N'MAGDALENA'),
        ('50', N'META'),
        ('52', N'NARIÑO'),
        ('54', N'NORTE DE SANTANDER'),
        ('63', N'QUINDÍO'),
        ('66', N'RISARALDA'),
        ('68', N'SANTANDER'),
        ('70', N'SUCRE'),
        ('73', N'TOLIMA'),
        ('76', N'VALLE DEL CAUCA'),
        ('81', N'ARAUCA'),
        ('85', N'CASANARE'),
        ('86', N'PUTUMAYO'),
        ('88', N'ARCHIPIÉLAGO DE SAN ANDRÉS, PROVIDENCIA Y SANTA CATALINA'),
        ('91', N'AMAZONAS'),
        ('94', N'GUAINÍA'),
        ('95', N'GUAVIARE'),
        ('97', N'VAUPÉS'),
        ('99', N'VICHADA')
    ) AS v(codigo, nombre)
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.catalogo_departamentos d
        WHERE d.id_pais = @idPaisCol AND d.codigo_divipola = v.codigo
    );

    PRINT 'OK: departamentos sembrados/verificados (33 esperados)';

    -- 4.3) Ciudades (1.122 filas — municipios + islas + áreas no
    -- municipalizadas, fuente DANE, particionadas en lotes por límite de
    -- 1000 filas por cláusula VALUES de SQL Server)
    -- Lote 1/3 de ciudades (400 filas)
    INSERT INTO dbo.catalogo_ciudades (id_departamento, codigo_divipola, nombre, tipo)
    SELECT d.id_departamento, v.codigo_ciudad, v.nombre_ciudad, v.tipo
    FROM (VALUES
        ('05', '05001', N'MEDELLÍN', 'MUNICIPIO'),
        ('05', '05002', N'ABEJORRAL', 'MUNICIPIO'),
        ('05', '05004', N'ABRIAQUÍ', 'MUNICIPIO'),
        ('05', '05021', N'ALEJANDRÍA', 'MUNICIPIO'),
        ('05', '05030', N'AMAGÁ', 'MUNICIPIO'),
        ('05', '05031', N'AMALFI', 'MUNICIPIO'),
        ('05', '05034', N'ANDES', 'MUNICIPIO'),
        ('05', '05036', N'ANGELÓPOLIS', 'MUNICIPIO'),
        ('05', '05038', N'ANGOSTURA', 'MUNICIPIO'),
        ('05', '05040', N'ANORÍ', 'MUNICIPIO'),
        ('05', '05042', N'SANTA FÉ DE ANTIOQUIA', 'MUNICIPIO'),
        ('05', '05044', N'ANZÁ', 'MUNICIPIO'),
        ('05', '05045', N'APARTADÓ', 'MUNICIPIO'),
        ('05', '05051', N'ARBOLETES', 'MUNICIPIO'),
        ('05', '05055', N'ARGELIA', 'MUNICIPIO'),
        ('05', '05059', N'ARMENIA', 'MUNICIPIO'),
        ('05', '05079', N'BARBOSA', 'MUNICIPIO'),
        ('05', '05086', N'BELMIRA', 'MUNICIPIO'),
        ('05', '05088', N'BELLO', 'MUNICIPIO'),
        ('05', '05091', N'BETANIA', 'MUNICIPIO'),
        ('05', '05093', N'BETULIA', 'MUNICIPIO'),
        ('05', '05101', N'CIUDAD BOLÍVAR', 'MUNICIPIO'),
        ('05', '05107', N'BRICEÑO', 'MUNICIPIO'),
        ('05', '05113', N'BURITICÁ', 'MUNICIPIO'),
        ('05', '05120', N'CÁCERES', 'MUNICIPIO'),
        ('05', '05125', N'CAICEDO', 'MUNICIPIO'),
        ('05', '05129', N'CALDAS', 'MUNICIPIO'),
        ('05', '05134', N'CAMPAMENTO', 'MUNICIPIO'),
        ('05', '05138', N'CAÑASGORDAS', 'MUNICIPIO'),
        ('05', '05142', N'CARACOLÍ', 'MUNICIPIO'),
        ('05', '05145', N'CARAMANTA', 'MUNICIPIO'),
        ('05', '05147', N'CAREPA', 'MUNICIPIO'),
        ('05', '05148', N'EL CARMEN DE VIBORAL', 'MUNICIPIO'),
        ('05', '05150', N'CAROLINA', 'MUNICIPIO'),
        ('05', '05154', N'CAUCASIA', 'MUNICIPIO'),
        ('05', '05172', N'CHIGORODÓ', 'MUNICIPIO'),
        ('05', '05190', N'CISNEROS', 'MUNICIPIO'),
        ('05', '05197', N'COCORNÁ', 'MUNICIPIO'),
        ('05', '05206', N'CONCEPCIÓN', 'MUNICIPIO'),
        ('05', '05209', N'CONCORDIA', 'MUNICIPIO'),
        ('05', '05212', N'COPACABANA', 'MUNICIPIO'),
        ('05', '05234', N'DABEIBA', 'MUNICIPIO'),
        ('05', '05237', N'DONMATÍAS', 'MUNICIPIO'),
        ('05', '05240', N'EBÉJICO', 'MUNICIPIO'),
        ('05', '05250', N'EL BAGRE', 'MUNICIPIO'),
        ('05', '05264', N'ENTRERRÍOS', 'MUNICIPIO'),
        ('05', '05266', N'ENVIGADO', 'MUNICIPIO'),
        ('05', '05282', N'FREDONIA', 'MUNICIPIO'),
        ('05', '05284', N'FRONTINO', 'MUNICIPIO'),
        ('05', '05306', N'GIRALDO', 'MUNICIPIO'),
        ('05', '05308', N'GIRARDOTA', 'MUNICIPIO'),
        ('05', '05310', N'GÓMEZ PLATA', 'MUNICIPIO'),
        ('05', '05313', N'GRANADA', 'MUNICIPIO'),
        ('05', '05315', N'GUADALUPE', 'MUNICIPIO'),
        ('05', '05318', N'GUARNE', 'MUNICIPIO'),
        ('05', '05321', N'GUATAPÉ', 'MUNICIPIO'),
        ('05', '05347', N'HELICONIA', 'MUNICIPIO'),
        ('05', '05353', N'HISPANIA', 'MUNICIPIO'),
        ('05', '05360', N'ITAGÜÍ', 'MUNICIPIO'),
        ('05', '05361', N'ITUANGO', 'MUNICIPIO'),
        ('05', '05364', N'JARDÍN', 'MUNICIPIO'),
        ('05', '05368', N'JERICÓ', 'MUNICIPIO'),
        ('05', '05376', N'LA CEJA', 'MUNICIPIO'),
        ('05', '05380', N'LA ESTRELLA', 'MUNICIPIO'),
        ('05', '05390', N'LA PINTADA', 'MUNICIPIO'),
        ('05', '05400', N'LA UNIÓN', 'MUNICIPIO'),
        ('05', '05411', N'LIBORINA', 'MUNICIPIO'),
        ('05', '05425', N'MACEO', 'MUNICIPIO'),
        ('05', '05440', N'MARINILLA', 'MUNICIPIO'),
        ('05', '05467', N'MONTEBELLO', 'MUNICIPIO'),
        ('05', '05475', N'MURINDÓ', 'MUNICIPIO'),
        ('05', '05480', N'MUTATÁ', 'MUNICIPIO'),
        ('05', '05483', N'NARIÑO', 'MUNICIPIO'),
        ('05', '05490', N'NECOCLÍ', 'MUNICIPIO'),
        ('05', '05495', N'NECHÍ', 'MUNICIPIO'),
        ('05', '05501', N'OLAYA', 'MUNICIPIO'),
        ('05', '05541', N'PEÑOL', 'MUNICIPIO'),
        ('05', '05543', N'PEQUE', 'MUNICIPIO'),
        ('05', '05576', N'PUEBLORRICO', 'MUNICIPIO'),
        ('05', '05579', N'PUERTO BERRÍO', 'MUNICIPIO'),
        ('05', '05585', N'PUERTO NARE', 'MUNICIPIO'),
        ('05', '05591', N'PUERTO TRIUNFO', 'MUNICIPIO'),
        ('05', '05604', N'REMEDIOS', 'MUNICIPIO'),
        ('05', '05607', N'RETIRO', 'MUNICIPIO'),
        ('05', '05615', N'RIONEGRO', 'MUNICIPIO'),
        ('05', '05628', N'SABANALARGA', 'MUNICIPIO'),
        ('05', '05631', N'SABANETA', 'MUNICIPIO'),
        ('05', '05642', N'SALGAR', 'MUNICIPIO'),
        ('05', '05647', N'SAN ANDRÉS DE CUERQUÍA', 'MUNICIPIO'),
        ('05', '05649', N'SAN CARLOS', 'MUNICIPIO'),
        ('05', '05652', N'SAN FRANCISCO', 'MUNICIPIO'),
        ('05', '05656', N'SAN JERÓNIMO', 'MUNICIPIO'),
        ('05', '05658', N'SAN JOSÉ DE LA MONTAÑA', 'MUNICIPIO'),
        ('05', '05659', N'SAN JUAN DE URABÁ', 'MUNICIPIO'),
        ('05', '05660', N'SAN LUIS', 'MUNICIPIO'),
        ('05', '05664', N'SAN PEDRO DE LOS MILAGROS', 'MUNICIPIO'),
        ('05', '05665', N'SAN PEDRO DE URABÁ', 'MUNICIPIO'),
        ('05', '05667', N'SAN RAFAEL', 'MUNICIPIO'),
        ('05', '05670', N'SAN ROQUE', 'MUNICIPIO'),
        ('05', '05674', N'SAN VICENTE FERRER', 'MUNICIPIO'),
        ('05', '05679', N'SANTA BÁRBARA', 'MUNICIPIO'),
        ('05', '05686', N'SANTA ROSA DE OSOS', 'MUNICIPIO'),
        ('05', '05690', N'SANTO DOMINGO', 'MUNICIPIO'),
        ('05', '05697', N'EL SANTUARIO', 'MUNICIPIO'),
        ('05', '05736', N'SEGOVIA', 'MUNICIPIO'),
        ('05', '05756', N'SONSÓN', 'MUNICIPIO'),
        ('05', '05761', N'SOPETRÁN', 'MUNICIPIO'),
        ('05', '05789', N'TÁMESIS', 'MUNICIPIO'),
        ('05', '05790', N'TARAZÁ', 'MUNICIPIO'),
        ('05', '05792', N'TARSO', 'MUNICIPIO'),
        ('05', '05809', N'TITIRIBÍ', 'MUNICIPIO'),
        ('05', '05819', N'TOLEDO', 'MUNICIPIO'),
        ('05', '05837', N'TURBO', 'MUNICIPIO'),
        ('05', '05842', N'URAMITA', 'MUNICIPIO'),
        ('05', '05847', N'URRAO', 'MUNICIPIO'),
        ('05', '05854', N'VALDIVIA', 'MUNICIPIO'),
        ('05', '05856', N'VALPARAÍSO', 'MUNICIPIO'),
        ('05', '05858', N'VEGACHÍ', 'MUNICIPIO'),
        ('05', '05861', N'VENECIA', 'MUNICIPIO'),
        ('05', '05873', N'VIGÍA DEL FUERTE', 'MUNICIPIO'),
        ('05', '05885', N'YALÍ', 'MUNICIPIO'),
        ('05', '05887', N'YARUMAL', 'MUNICIPIO'),
        ('05', '05890', N'YOLOMBÓ', 'MUNICIPIO'),
        ('05', '05893', N'YONDÓ', 'MUNICIPIO'),
        ('05', '05895', N'ZARAGOZA', 'MUNICIPIO'),
        ('08', '08001', N'BARRANQUILLA', 'MUNICIPIO'),
        ('08', '08078', N'BARANOA', 'MUNICIPIO'),
        ('08', '08137', N'CAMPO DE LA CRUZ', 'MUNICIPIO'),
        ('08', '08141', N'CANDELARIA', 'MUNICIPIO'),
        ('08', '08296', N'GALAPA', 'MUNICIPIO'),
        ('08', '08372', N'JUAN DE ACOSTA', 'MUNICIPIO'),
        ('08', '08421', N'LURUACO', 'MUNICIPIO'),
        ('08', '08433', N'MALAMBO', 'MUNICIPIO'),
        ('08', '08436', N'MANATÍ', 'MUNICIPIO'),
        ('08', '08520', N'PALMAR DE VARELA', 'MUNICIPIO'),
        ('08', '08549', N'PIOJÓ', 'MUNICIPIO'),
        ('08', '08558', N'POLONUEVO', 'MUNICIPIO'),
        ('08', '08560', N'PONEDERA', 'MUNICIPIO'),
        ('08', '08573', N'PUERTO COLOMBIA', 'MUNICIPIO'),
        ('08', '08606', N'REPELÓN', 'MUNICIPIO'),
        ('08', '08634', N'SABANAGRANDE', 'MUNICIPIO'),
        ('08', '08638', N'SABANALARGA', 'MUNICIPIO'),
        ('08', '08675', N'SANTA LUCÍA', 'MUNICIPIO'),
        ('08', '08685', N'SANTO TOMÁS', 'MUNICIPIO'),
        ('08', '08758', N'SOLEDAD', 'MUNICIPIO'),
        ('08', '08770', N'SUAN', 'MUNICIPIO'),
        ('08', '08832', N'TUBARÁ', 'MUNICIPIO'),
        ('08', '08849', N'USIACURÍ', 'MUNICIPIO'),
        ('11', '11001', N'BOGOTÁ, D.C.', 'MUNICIPIO'),
        ('13', '13001', N'CARTAGENA DE INDIAS', 'MUNICIPIO'),
        ('13', '13006', N'ACHÍ', 'MUNICIPIO'),
        ('13', '13030', N'ALTOS DEL ROSARIO', 'MUNICIPIO'),
        ('13', '13042', N'ARENAL', 'MUNICIPIO'),
        ('13', '13052', N'ARJONA', 'MUNICIPIO'),
        ('13', '13062', N'ARROYOHONDO', 'MUNICIPIO'),
        ('13', '13074', N'BARRANCO DE LOBA', 'MUNICIPIO'),
        ('13', '13140', N'CALAMAR', 'MUNICIPIO'),
        ('13', '13160', N'CANTAGALLO', 'MUNICIPIO'),
        ('13', '13188', N'CICUCO', 'MUNICIPIO'),
        ('13', '13212', N'CÓRDOBA', 'MUNICIPIO'),
        ('13', '13222', N'CLEMENCIA', 'MUNICIPIO'),
        ('13', '13244', N'EL CARMEN DE BOLÍVAR', 'MUNICIPIO'),
        ('13', '13248', N'EL GUAMO', 'MUNICIPIO'),
        ('13', '13268', N'EL PEÑÓN', 'MUNICIPIO'),
        ('13', '13300', N'HATILLO DE LOBA', 'MUNICIPIO'),
        ('13', '13430', N'MAGANGUÉ', 'MUNICIPIO'),
        ('13', '13433', N'MAHATES', 'MUNICIPIO'),
        ('13', '13440', N'MARGARITA', 'MUNICIPIO'),
        ('13', '13442', N'MARÍA LA BAJA', 'MUNICIPIO'),
        ('13', '13458', N'MONTECRISTO', 'MUNICIPIO'),
        ('13', '13468', N'SANTA CRUZ DE MOMPOX', 'MUNICIPIO'),
        ('13', '13473', N'MORALES', 'MUNICIPIO'),
        ('13', '13490', N'NOROSÍ', 'MUNICIPIO'),
        ('13', '13549', N'PINILLOS', 'MUNICIPIO'),
        ('13', '13580', N'REGIDOR', 'MUNICIPIO'),
        ('13', '13600', N'RÍO VIEJO', 'MUNICIPIO'),
        ('13', '13620', N'SAN CRISTÓBAL', 'MUNICIPIO'),
        ('13', '13647', N'SAN ESTANISLAO', 'MUNICIPIO'),
        ('13', '13650', N'SAN FERNANDO', 'MUNICIPIO'),
        ('13', '13654', N'SAN JACINTO', 'MUNICIPIO'),
        ('13', '13655', N'SAN JACINTO DEL CAUCA', 'MUNICIPIO'),
        ('13', '13657', N'SAN JUAN NEPOMUCENO', 'MUNICIPIO'),
        ('13', '13667', N'SAN MARTÍN DE LOBA', 'MUNICIPIO'),
        ('13', '13670', N'SAN PABLO', 'MUNICIPIO'),
        ('13', '13673', N'SANTA CATALINA', 'MUNICIPIO'),
        ('13', '13683', N'SANTA ROSA', 'MUNICIPIO'),
        ('13', '13688', N'SANTA ROSA DEL SUR', 'MUNICIPIO'),
        ('13', '13744', N'SIMITÍ', 'MUNICIPIO'),
        ('13', '13760', N'SOPLAVIENTO', 'MUNICIPIO'),
        ('13', '13780', N'TALAIGUA NUEVO', 'MUNICIPIO'),
        ('13', '13810', N'TIQUISIO', 'MUNICIPIO'),
        ('13', '13836', N'TURBACO', 'MUNICIPIO'),
        ('13', '13838', N'TURBANA', 'MUNICIPIO'),
        ('13', '13873', N'VILLANUEVA', 'MUNICIPIO'),
        ('13', '13894', N'ZAMBRANO', 'MUNICIPIO'),
        ('15', '15001', N'TUNJA', 'MUNICIPIO'),
        ('15', '15022', N'ALMEIDA', 'MUNICIPIO'),
        ('15', '15047', N'AQUITANIA', 'MUNICIPIO'),
        ('15', '15051', N'ARCABUCO', 'MUNICIPIO'),
        ('15', '15087', N'BELÉN', 'MUNICIPIO'),
        ('15', '15090', N'BERBEO', 'MUNICIPIO'),
        ('15', '15092', N'BETÉITIVA', 'MUNICIPIO'),
        ('15', '15097', N'BOAVITA', 'MUNICIPIO'),
        ('15', '15104', N'BOYACÁ', 'MUNICIPIO'),
        ('15', '15106', N'BRICEÑO', 'MUNICIPIO'),
        ('15', '15109', N'BUENAVISTA', 'MUNICIPIO'),
        ('15', '15114', N'BUSBANZÁ', 'MUNICIPIO'),
        ('15', '15131', N'CALDAS', 'MUNICIPIO'),
        ('15', '15135', N'CAMPOHERMOSO', 'MUNICIPIO'),
        ('15', '15162', N'CERINZA', 'MUNICIPIO'),
        ('15', '15172', N'CHINAVITA', 'MUNICIPIO'),
        ('15', '15176', N'CHIQUINQUIRÁ', 'MUNICIPIO'),
        ('15', '15180', N'CHISCAS', 'MUNICIPIO'),
        ('15', '15183', N'CHITA', 'MUNICIPIO'),
        ('15', '15185', N'CHITARAQUE', 'MUNICIPIO'),
        ('15', '15187', N'CHIVATÁ', 'MUNICIPIO'),
        ('15', '15189', N'CIÉNEGA', 'MUNICIPIO'),
        ('15', '15204', N'CÓMBITA', 'MUNICIPIO'),
        ('15', '15212', N'COPER', 'MUNICIPIO'),
        ('15', '15215', N'CORRALES', 'MUNICIPIO'),
        ('15', '15218', N'COVARACHÍA', 'MUNICIPIO'),
        ('15', '15223', N'CUBARÁ', 'MUNICIPIO'),
        ('15', '15224', N'CUCAITA', 'MUNICIPIO'),
        ('15', '15226', N'CUÍTIVA', 'MUNICIPIO'),
        ('15', '15232', N'CHÍQUIZA', 'MUNICIPIO'),
        ('15', '15236', N'CHIVOR', 'MUNICIPIO'),
        ('15', '15238', N'DUITAMA', 'MUNICIPIO'),
        ('15', '15244', N'EL COCUY', 'MUNICIPIO'),
        ('15', '15248', N'EL ESPINO', 'MUNICIPIO'),
        ('15', '15272', N'FIRAVITOBA', 'MUNICIPIO'),
        ('15', '15276', N'FLORESTA', 'MUNICIPIO'),
        ('15', '15293', N'GACHANTIVÁ', 'MUNICIPIO'),
        ('15', '15296', N'GÁMEZA', 'MUNICIPIO'),
        ('15', '15299', N'GARAGOA', 'MUNICIPIO'),
        ('15', '15317', N'GUACAMAYAS', 'MUNICIPIO'),
        ('15', '15322', N'GUATEQUE', 'MUNICIPIO'),
        ('15', '15325', N'GUAYATÁ', 'MUNICIPIO'),
        ('15', '15332', N'GÜICÁN DE LA SIERRA', 'MUNICIPIO'),
        ('15', '15362', N'IZA', 'MUNICIPIO'),
        ('15', '15367', N'JENESANO', 'MUNICIPIO'),
        ('15', '15368', N'JERICÓ', 'MUNICIPIO'),
        ('15', '15377', N'LABRANZAGRANDE', 'MUNICIPIO'),
        ('15', '15380', N'LA CAPILLA', 'MUNICIPIO'),
        ('15', '15401', N'LA VICTORIA', 'MUNICIPIO'),
        ('15', '15403', N'LA UVITA', 'MUNICIPIO'),
        ('15', '15407', N'VILLA DE LEYVA', 'MUNICIPIO'),
        ('15', '15425', N'MACANAL', 'MUNICIPIO'),
        ('15', '15442', N'MARIPÍ', 'MUNICIPIO'),
        ('15', '15455', N'MIRAFLORES', 'MUNICIPIO'),
        ('15', '15464', N'MONGUA', 'MUNICIPIO'),
        ('15', '15466', N'MONGUÍ', 'MUNICIPIO'),
        ('15', '15469', N'MONIQUIRÁ', 'MUNICIPIO'),
        ('15', '15476', N'MOTAVITA', 'MUNICIPIO'),
        ('15', '15480', N'MUZO', 'MUNICIPIO'),
        ('15', '15491', N'NOBSA', 'MUNICIPIO'),
        ('15', '15494', N'NUEVO COLÓN', 'MUNICIPIO'),
        ('15', '15500', N'OICATÁ', 'MUNICIPIO'),
        ('15', '15507', N'OTANCHE', 'MUNICIPIO'),
        ('15', '15511', N'PACHAVITA', 'MUNICIPIO'),
        ('15', '15514', N'PÁEZ', 'MUNICIPIO'),
        ('15', '15516', N'PAIPA', 'MUNICIPIO'),
        ('15', '15518', N'PAJARITO', 'MUNICIPIO'),
        ('15', '15522', N'PANQUEBA', 'MUNICIPIO'),
        ('15', '15531', N'PAUNA', 'MUNICIPIO'),
        ('15', '15533', N'PAYA', 'MUNICIPIO'),
        ('15', '15537', N'PAZ DE RÍO', 'MUNICIPIO'),
        ('15', '15542', N'PESCA', 'MUNICIPIO'),
        ('15', '15550', N'PISBA', 'MUNICIPIO'),
        ('15', '15572', N'PUERTO BOYACÁ', 'MUNICIPIO'),
        ('15', '15580', N'QUÍPAMA', 'MUNICIPIO'),
        ('15', '15599', N'RAMIRIQUÍ', 'MUNICIPIO'),
        ('15', '15600', N'RÁQUIRA', 'MUNICIPIO'),
        ('15', '15621', N'RONDÓN', 'MUNICIPIO'),
        ('15', '15632', N'SABOYÁ', 'MUNICIPIO'),
        ('15', '15638', N'SÁCHICA', 'MUNICIPIO'),
        ('15', '15646', N'SAMACÁ', 'MUNICIPIO'),
        ('15', '15660', N'SAN EDUARDO', 'MUNICIPIO'),
        ('15', '15664', N'SAN JOSÉ DE PARE', 'MUNICIPIO'),
        ('15', '15667', N'SAN LUIS DE GACENO', 'MUNICIPIO'),
        ('15', '15673', N'SAN MATEO', 'MUNICIPIO'),
        ('15', '15676', N'SAN MIGUEL DE SEMA', 'MUNICIPIO'),
        ('15', '15681', N'SAN PABLO DE BORBUR', 'MUNICIPIO'),
        ('15', '15686', N'SANTANA', 'MUNICIPIO'),
        ('15', '15690', N'SANTA MARÍA', 'MUNICIPIO'),
        ('15', '15693', N'SANTA ROSA DE VITERBO', 'MUNICIPIO'),
        ('15', '15696', N'SANTA SOFÍA', 'MUNICIPIO'),
        ('15', '15720', N'SATIVANORTE', 'MUNICIPIO'),
        ('15', '15723', N'SATIVASUR', 'MUNICIPIO'),
        ('15', '15740', N'SIACHOQUE', 'MUNICIPIO'),
        ('15', '15753', N'SOATÁ', 'MUNICIPIO'),
        ('15', '15755', N'SOCOTÁ', 'MUNICIPIO'),
        ('15', '15757', N'SOCHA', 'MUNICIPIO'),
        ('15', '15759', N'SOGAMOSO', 'MUNICIPIO'),
        ('15', '15761', N'SOMONDOCO', 'MUNICIPIO'),
        ('15', '15762', N'SORA', 'MUNICIPIO'),
        ('15', '15763', N'SOTAQUIRÁ', 'MUNICIPIO'),
        ('15', '15764', N'SORACÁ', 'MUNICIPIO'),
        ('15', '15774', N'SUSACÓN', 'MUNICIPIO'),
        ('15', '15776', N'SUTAMARCHÁN', 'MUNICIPIO'),
        ('15', '15778', N'SUTATENZA', 'MUNICIPIO'),
        ('15', '15790', N'TASCO', 'MUNICIPIO'),
        ('15', '15798', N'TENZA', 'MUNICIPIO'),
        ('15', '15804', N'TIBANÁ', 'MUNICIPIO'),
        ('15', '15806', N'TIBASOSA', 'MUNICIPIO'),
        ('15', '15808', N'TINJACÁ', 'MUNICIPIO'),
        ('15', '15810', N'TIPACOQUE', 'MUNICIPIO'),
        ('15', '15814', N'TOCA', 'MUNICIPIO'),
        ('15', '15816', N'TOGÜÍ', 'MUNICIPIO'),
        ('15', '15820', N'TÓPAGA', 'MUNICIPIO'),
        ('15', '15822', N'TOTA', 'MUNICIPIO'),
        ('15', '15832', N'TUNUNGUÁ', 'MUNICIPIO'),
        ('15', '15835', N'TURMEQUÉ', 'MUNICIPIO'),
        ('15', '15837', N'TUTA', 'MUNICIPIO'),
        ('15', '15839', N'TUTAZÁ', 'MUNICIPIO'),
        ('15', '15842', N'ÚMBITA', 'MUNICIPIO'),
        ('15', '15861', N'VENTAQUEMADA', 'MUNICIPIO'),
        ('15', '15879', N'VIRACACHÁ', 'MUNICIPIO'),
        ('15', '15897', N'ZETAQUIRA', 'MUNICIPIO'),
        ('17', '17001', N'MANIZALES', 'MUNICIPIO'),
        ('17', '17013', N'AGUADAS', 'MUNICIPIO'),
        ('17', '17042', N'ANSERMA', 'MUNICIPIO'),
        ('17', '17050', N'ARANZAZU', 'MUNICIPIO'),
        ('17', '17088', N'BELALCÁZAR', 'MUNICIPIO'),
        ('17', '17174', N'CHINCHINÁ', 'MUNICIPIO'),
        ('17', '17272', N'FILADELFIA', 'MUNICIPIO'),
        ('17', '17380', N'LA DORADA', 'MUNICIPIO'),
        ('17', '17388', N'LA MERCED', 'MUNICIPIO'),
        ('17', '17433', N'MANZANARES', 'MUNICIPIO'),
        ('17', '17442', N'MARMATO', 'MUNICIPIO'),
        ('17', '17444', N'MARQUETALIA', 'MUNICIPIO'),
        ('17', '17446', N'MARULANDA', 'MUNICIPIO'),
        ('17', '17486', N'NEIRA', 'MUNICIPIO'),
        ('17', '17495', N'NORCASIA', 'MUNICIPIO'),
        ('17', '17513', N'PÁCORA', 'MUNICIPIO'),
        ('17', '17524', N'PALESTINA', 'MUNICIPIO'),
        ('17', '17541', N'PENSILVANIA', 'MUNICIPIO'),
        ('17', '17614', N'RIOSUCIO', 'MUNICIPIO'),
        ('17', '17616', N'RISARALDA', 'MUNICIPIO'),
        ('17', '17653', N'SALAMINA', 'MUNICIPIO'),
        ('17', '17662', N'SAMANÁ', 'MUNICIPIO'),
        ('17', '17665', N'SAN JOSÉ', 'MUNICIPIO'),
        ('17', '17777', N'SUPÍA', 'MUNICIPIO'),
        ('17', '17867', N'VICTORIA', 'MUNICIPIO'),
        ('17', '17873', N'VILLAMARÍA', 'MUNICIPIO'),
        ('17', '17877', N'VITERBO', 'MUNICIPIO'),
        ('18', '18001', N'FLORENCIA', 'MUNICIPIO'),
        ('18', '18029', N'ALBANIA', 'MUNICIPIO'),
        ('18', '18094', N'BELÉN DE LOS ANDAQUÍES', 'MUNICIPIO'),
        ('18', '18150', N'CARTAGENA DEL CHAIRÁ', 'MUNICIPIO'),
        ('18', '18205', N'CURILLO', 'MUNICIPIO'),
        ('18', '18247', N'EL DONCELLO', 'MUNICIPIO'),
        ('18', '18256', N'EL PAUJÍL', 'MUNICIPIO'),
        ('18', '18410', N'LA MONTAÑITA', 'MUNICIPIO'),
        ('18', '18460', N'MILÁN', 'MUNICIPIO'),
        ('18', '18479', N'MORELIA', 'MUNICIPIO'),
        ('18', '18592', N'PUERTO RICO', 'MUNICIPIO'),
        ('18', '18610', N'SAN JOSÉ DEL FRAGUA', 'MUNICIPIO'),
        ('18', '18753', N'SAN VICENTE DEL CAGUÁN', 'MUNICIPIO'),
        ('18', '18756', N'SOLANO', 'MUNICIPIO'),
        ('18', '18785', N'SOLITA', 'MUNICIPIO'),
        ('18', '18860', N'VALPARAÍSO', 'MUNICIPIO'),
        ('19', '19001', N'POPAYÁN', 'MUNICIPIO'),
        ('19', '19022', N'ALMAGUER', 'MUNICIPIO'),
        ('19', '19050', N'ARGELIA', 'MUNICIPIO'),
        ('19', '19075', N'BALBOA', 'MUNICIPIO'),
        ('19', '19100', N'BOLÍVAR', 'MUNICIPIO'),
        ('19', '19110', N'BUENOS AIRES', 'MUNICIPIO'),
        ('19', '19130', N'CAJIBÍO', 'MUNICIPIO'),
        ('19', '19137', N'CALDONO', 'MUNICIPIO'),
        ('19', '19142', N'CALOTO', 'MUNICIPIO'),
        ('19', '19212', N'CORINTO', 'MUNICIPIO'),
        ('19', '19256', N'EL TAMBO', 'MUNICIPIO'),
        ('19', '19290', N'FLORENCIA', 'MUNICIPIO'),
        ('19', '19300', N'GUACHENÉ', 'MUNICIPIO'),
        ('19', '19318', N'GUAPI', 'MUNICIPIO'),
        ('19', '19355', N'INZÁ', 'MUNICIPIO'),
        ('19', '19364', N'JAMBALÓ', 'MUNICIPIO'),
        ('19', '19392', N'LA SIERRA', 'MUNICIPIO'),
        ('19', '19397', N'LA VEGA', 'MUNICIPIO'),
        ('19', '19418', N'LÓPEZ DE MICAY', 'MUNICIPIO'),
        ('19', '19450', N'MERCADERES', 'MUNICIPIO'),
        ('19', '19455', N'MIRANDA', 'MUNICIPIO'),
        ('19', '19473', N'MORALES', 'MUNICIPIO'),
        ('19', '19513', N'PADILLA', 'MUNICIPIO'),
        ('19', '19517', N'PÁEZ', 'MUNICIPIO'),
        ('19', '19532', N'PATÍA', 'MUNICIPIO'),
        ('19', '19533', N'PIAMONTE', 'MUNICIPIO'),
        ('19', '19548', N'PIENDAMÓ - TUNÍA', 'MUNICIPIO'),
        ('19', '19573', N'PUERTO TEJADA', 'MUNICIPIO'),
        ('19', '19585', N'PURACÉ', 'MUNICIPIO'),
        ('19', '19622', N'ROSAS', 'MUNICIPIO'),
        ('19', '19693', N'SAN SEBASTIÁN', 'MUNICIPIO'),
        ('19', '19698', N'SANTANDER DE QUILICHAO', 'MUNICIPIO'),
        ('19', '19701', N'SANTA ROSA', 'MUNICIPIO'),
        ('19', '19743', N'SILVIA', 'MUNICIPIO'),
        ('19', '19760', N'SOTARÁ', 'MUNICIPIO'),
        ('19', '19780', N'SUÁREZ', 'MUNICIPIO'),
        ('19', '19785', N'SUCRE', 'MUNICIPIO'),
        ('19', '19807', N'TIMBÍO', 'MUNICIPIO'),
        ('19', '19809', N'TIMBIQUÍ', 'MUNICIPIO')
    ) AS v(codigo_departamento, codigo_ciudad, nombre_ciudad, tipo)
    JOIN dbo.catalogo_departamentos d
        ON d.id_pais = @idPaisCol AND d.codigo_divipola = v.codigo_departamento
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.catalogo_ciudades c
        WHERE c.id_departamento = d.id_departamento AND c.codigo_divipola = v.codigo_ciudad
    );

    -- Lote 2/3 de ciudades (400 filas)
    INSERT INTO dbo.catalogo_ciudades (id_departamento, codigo_divipola, nombre, tipo)
    SELECT d.id_departamento, v.codigo_ciudad, v.nombre_ciudad, v.tipo
    FROM (VALUES
        ('19', '19821', N'TORIBÍO', 'MUNICIPIO'),
        ('19', '19824', N'TOTORÓ', 'MUNICIPIO'),
        ('19', '19845', N'VILLA RICA', 'MUNICIPIO'),
        ('20', '20001', N'VALLEDUPAR', 'MUNICIPIO'),
        ('20', '20011', N'AGUACHICA', 'MUNICIPIO'),
        ('20', '20013', N'AGUSTÍN CODAZZI', 'MUNICIPIO'),
        ('20', '20032', N'ASTREA', 'MUNICIPIO'),
        ('20', '20045', N'BECERRIL', 'MUNICIPIO'),
        ('20', '20060', N'BOSCONIA', 'MUNICIPIO'),
        ('20', '20175', N'CHIMICHAGUA', 'MUNICIPIO'),
        ('20', '20178', N'CHIRIGUANÁ', 'MUNICIPIO'),
        ('20', '20228', N'CURUMANÍ', 'MUNICIPIO'),
        ('20', '20238', N'EL COPEY', 'MUNICIPIO'),
        ('20', '20250', N'EL PASO', 'MUNICIPIO'),
        ('20', '20295', N'GAMARRA', 'MUNICIPIO'),
        ('20', '20310', N'GONZÁLEZ', 'MUNICIPIO'),
        ('20', '20383', N'LA GLORIA', 'MUNICIPIO'),
        ('20', '20400', N'LA JAGUA DE IBIRICO', 'MUNICIPIO'),
        ('20', '20443', N'MANAURE BALCÓN DEL CESAR', 'MUNICIPIO'),
        ('20', '20517', N'PAILITAS', 'MUNICIPIO'),
        ('20', '20550', N'PELAYA', 'MUNICIPIO'),
        ('20', '20570', N'PUEBLO BELLO', 'MUNICIPIO'),
        ('20', '20614', N'RÍO DE ORO', 'MUNICIPIO'),
        ('20', '20621', N'LA PAZ', 'MUNICIPIO'),
        ('20', '20710', N'SAN ALBERTO', 'MUNICIPIO'),
        ('20', '20750', N'SAN DIEGO', 'MUNICIPIO'),
        ('20', '20770', N'SAN MARTÍN', 'MUNICIPIO'),
        ('20', '20787', N'TAMALAMEQUE', 'MUNICIPIO'),
        ('23', '23001', N'MONTERÍA', 'MUNICIPIO'),
        ('23', '23068', N'AYAPEL', 'MUNICIPIO'),
        ('23', '23079', N'BUENAVISTA', 'MUNICIPIO'),
        ('23', '23090', N'CANALETE', 'MUNICIPIO'),
        ('23', '23162', N'CERETÉ', 'MUNICIPIO'),
        ('23', '23168', N'CHIMÁ', 'MUNICIPIO'),
        ('23', '23182', N'CHINÚ', 'MUNICIPIO'),
        ('23', '23189', N'CIÉNAGA DE ORO', 'MUNICIPIO'),
        ('23', '23300', N'COTORRA', 'MUNICIPIO'),
        ('23', '23350', N'LA APARTADA', 'MUNICIPIO'),
        ('23', '23417', N'LORICA', 'MUNICIPIO'),
        ('23', '23419', N'LOS CÓRDOBAS', 'MUNICIPIO'),
        ('23', '23464', N'MOMIL', 'MUNICIPIO'),
        ('23', '23466', N'MONTELÍBANO', 'MUNICIPIO'),
        ('23', '23500', N'MOÑITOS', 'MUNICIPIO'),
        ('23', '23555', N'PLANETA RICA', 'MUNICIPIO'),
        ('23', '23570', N'PUEBLO NUEVO', 'MUNICIPIO'),
        ('23', '23574', N'PUERTO ESCONDIDO', 'MUNICIPIO'),
        ('23', '23580', N'PUERTO LIBERTADOR', 'MUNICIPIO'),
        ('23', '23586', N'PURÍSIMA DE LA CONCEPCIÓN', 'MUNICIPIO'),
        ('23', '23660', N'SAHAGÚN', 'MUNICIPIO'),
        ('23', '23670', N'SAN ANDRÉS DE SOTAVENTO', 'MUNICIPIO'),
        ('23', '23672', N'SAN ANTERO', 'MUNICIPIO'),
        ('23', '23675', N'SAN BERNARDO DEL VIENTO', 'MUNICIPIO'),
        ('23', '23678', N'SAN CARLOS', 'MUNICIPIO'),
        ('23', '23682', N'SAN JOSÉ DE URÉ', 'MUNICIPIO'),
        ('23', '23686', N'SAN PELAYO', 'MUNICIPIO'),
        ('23', '23807', N'TIERRALTA', 'MUNICIPIO'),
        ('23', '23815', N'TUCHÍN', 'MUNICIPIO'),
        ('23', '23855', N'VALENCIA', 'MUNICIPIO'),
        ('25', '25001', N'AGUA DE DIOS', 'MUNICIPIO'),
        ('25', '25019', N'ALBÁN', 'MUNICIPIO'),
        ('25', '25035', N'ANAPOIMA', 'MUNICIPIO'),
        ('25', '25040', N'ANOLAIMA', 'MUNICIPIO'),
        ('25', '25053', N'ARBELÁEZ', 'MUNICIPIO'),
        ('25', '25086', N'BELTRÁN', 'MUNICIPIO'),
        ('25', '25095', N'BITUIMA', 'MUNICIPIO'),
        ('25', '25099', N'BOJACÁ', 'MUNICIPIO'),
        ('25', '25120', N'CABRERA', 'MUNICIPIO'),
        ('25', '25123', N'CACHIPAY', 'MUNICIPIO'),
        ('25', '25126', N'CAJICÁ', 'MUNICIPIO'),
        ('25', '25148', N'CAPARRAPÍ', 'MUNICIPIO'),
        ('25', '25151', N'CÁQUEZA', 'MUNICIPIO'),
        ('25', '25154', N'CARMEN DE CARUPA', 'MUNICIPIO'),
        ('25', '25168', N'CHAGUANÍ', 'MUNICIPIO'),
        ('25', '25175', N'CHÍA', 'MUNICIPIO'),
        ('25', '25178', N'CHIPAQUE', 'MUNICIPIO'),
        ('25', '25181', N'CHOACHÍ', 'MUNICIPIO'),
        ('25', '25183', N'CHOCONTÁ', 'MUNICIPIO'),
        ('25', '25200', N'COGUA', 'MUNICIPIO'),
        ('25', '25214', N'COTA', 'MUNICIPIO'),
        ('25', '25224', N'CUCUNUBÁ', 'MUNICIPIO'),
        ('25', '25245', N'EL COLEGIO', 'MUNICIPIO'),
        ('25', '25258', N'EL PEÑÓN', 'MUNICIPIO'),
        ('25', '25260', N'EL ROSAL', 'MUNICIPIO'),
        ('25', '25269', N'FACATATIVÁ', 'MUNICIPIO'),
        ('25', '25279', N'FÓMEQUE', 'MUNICIPIO'),
        ('25', '25281', N'FOSCA', 'MUNICIPIO'),
        ('25', '25286', N'FUNZA', 'MUNICIPIO'),
        ('25', '25288', N'FÚQUENE', 'MUNICIPIO'),
        ('25', '25290', N'FUSAGASUGÁ', 'MUNICIPIO'),
        ('25', '25293', N'GACHALÁ', 'MUNICIPIO'),
        ('25', '25295', N'GACHANCIPÁ', 'MUNICIPIO'),
        ('25', '25297', N'GACHETÁ', 'MUNICIPIO'),
        ('25', '25299', N'GAMA', 'MUNICIPIO'),
        ('25', '25307', N'GIRARDOT', 'MUNICIPIO'),
        ('25', '25312', N'GRANADA', 'MUNICIPIO'),
        ('25', '25317', N'GUACHETÁ', 'MUNICIPIO'),
        ('25', '25320', N'GUADUAS', 'MUNICIPIO'),
        ('25', '25322', N'GUASCA', 'MUNICIPIO'),
        ('25', '25324', N'GUATAQUÍ', 'MUNICIPIO'),
        ('25', '25326', N'GUATAVITA', 'MUNICIPIO'),
        ('25', '25328', N'GUAYABAL DE SÍQUIMA', 'MUNICIPIO'),
        ('25', '25335', N'GUAYABETAL', 'MUNICIPIO'),
        ('25', '25339', N'GUTIÉRREZ', 'MUNICIPIO'),
        ('25', '25368', N'JERUSALÉN', 'MUNICIPIO'),
        ('25', '25372', N'JUNÍN', 'MUNICIPIO'),
        ('25', '25377', N'LA CALERA', 'MUNICIPIO'),
        ('25', '25386', N'LA MESA', 'MUNICIPIO'),
        ('25', '25394', N'LA PALMA', 'MUNICIPIO'),
        ('25', '25398', N'LA PEÑA', 'MUNICIPIO'),
        ('25', '25402', N'LA VEGA', 'MUNICIPIO'),
        ('25', '25407', N'LENGUAZAQUE', 'MUNICIPIO'),
        ('25', '25426', N'MACHETÁ', 'MUNICIPIO'),
        ('25', '25430', N'MADRID', 'MUNICIPIO'),
        ('25', '25436', N'MANTA', 'MUNICIPIO'),
        ('25', '25438', N'MEDINA', 'MUNICIPIO'),
        ('25', '25473', N'MOSQUERA', 'MUNICIPIO'),
        ('25', '25483', N'NARIÑO', 'MUNICIPIO'),
        ('25', '25486', N'NEMOCÓN', 'MUNICIPIO'),
        ('25', '25488', N'NILO', 'MUNICIPIO'),
        ('25', '25489', N'NIMAIMA', 'MUNICIPIO'),
        ('25', '25491', N'NOCAIMA', 'MUNICIPIO'),
        ('25', '25506', N'VENECIA', 'MUNICIPIO'),
        ('25', '25513', N'PACHO', 'MUNICIPIO'),
        ('25', '25518', N'PAIME', 'MUNICIPIO'),
        ('25', '25524', N'PANDI', 'MUNICIPIO'),
        ('25', '25530', N'PARATEBUENO', 'MUNICIPIO'),
        ('25', '25535', N'PASCA', 'MUNICIPIO'),
        ('25', '25572', N'PUERTO SALGAR', 'MUNICIPIO'),
        ('25', '25580', N'PULÍ', 'MUNICIPIO'),
        ('25', '25592', N'QUEBRADANEGRA', 'MUNICIPIO'),
        ('25', '25594', N'QUETAME', 'MUNICIPIO'),
        ('25', '25596', N'QUIPILE', 'MUNICIPIO'),
        ('25', '25599', N'APULO', 'MUNICIPIO'),
        ('25', '25612', N'RICAURTE', 'MUNICIPIO'),
        ('25', '25645', N'SAN ANTONIO DEL TEQUENDAMA', 'MUNICIPIO'),
        ('25', '25649', N'SAN BERNARDO', 'MUNICIPIO'),
        ('25', '25653', N'SAN CAYETANO', 'MUNICIPIO'),
        ('25', '25658', N'SAN FRANCISCO', 'MUNICIPIO'),
        ('25', '25662', N'SAN JUAN DE RIOSECO', 'MUNICIPIO'),
        ('25', '25718', N'SASAIMA', 'MUNICIPIO'),
        ('25', '25736', N'SESQUILÉ', 'MUNICIPIO'),
        ('25', '25740', N'SIBATÉ', 'MUNICIPIO'),
        ('25', '25743', N'SILVANIA', 'MUNICIPIO'),
        ('25', '25745', N'SIMIJACA', 'MUNICIPIO'),
        ('25', '25754', N'SOACHA', 'MUNICIPIO'),
        ('25', '25758', N'SOPÓ', 'MUNICIPIO'),
        ('25', '25769', N'SUBACHOQUE', 'MUNICIPIO'),
        ('25', '25772', N'SUESCA', 'MUNICIPIO'),
        ('25', '25777', N'SUPATÁ', 'MUNICIPIO'),
        ('25', '25779', N'SUSA', 'MUNICIPIO'),
        ('25', '25781', N'SUTATAUSA', 'MUNICIPIO'),
        ('25', '25785', N'TABIO', 'MUNICIPIO'),
        ('25', '25793', N'TAUSA', 'MUNICIPIO'),
        ('25', '25797', N'TENA', 'MUNICIPIO'),
        ('25', '25799', N'TENJO', 'MUNICIPIO'),
        ('25', '25805', N'TIBACUY', 'MUNICIPIO'),
        ('25', '25807', N'TIBIRITA', 'MUNICIPIO'),
        ('25', '25815', N'TOCAIMA', 'MUNICIPIO'),
        ('25', '25817', N'TOCANCIPÁ', 'MUNICIPIO'),
        ('25', '25823', N'TOPAIPÍ', 'MUNICIPIO'),
        ('25', '25839', N'UBALÁ', 'MUNICIPIO'),
        ('25', '25841', N'UBAQUE', 'MUNICIPIO'),
        ('25', '25843', N'VILLA DE SAN DIEGO DE UBATÉ', 'MUNICIPIO'),
        ('25', '25845', N'UNE', 'MUNICIPIO'),
        ('25', '25851', N'ÚTICA', 'MUNICIPIO'),
        ('25', '25862', N'VERGARA', 'MUNICIPIO'),
        ('25', '25867', N'VIANÍ', 'MUNICIPIO'),
        ('25', '25871', N'VILLAGÓMEZ', 'MUNICIPIO'),
        ('25', '25873', N'VILLAPINZÓN', 'MUNICIPIO'),
        ('25', '25875', N'VILLETA', 'MUNICIPIO'),
        ('25', '25878', N'VIOTÁ', 'MUNICIPIO'),
        ('25', '25885', N'YACOPÍ', 'MUNICIPIO'),
        ('25', '25898', N'ZIPACÓN', 'MUNICIPIO'),
        ('25', '25899', N'ZIPAQUIRÁ', 'MUNICIPIO'),
        ('27', '27001', N'QUIBDÓ', 'MUNICIPIO'),
        ('27', '27006', N'ACANDÍ', 'MUNICIPIO'),
        ('27', '27025', N'ALTO BAUDÓ', 'MUNICIPIO'),
        ('27', '27050', N'ATRATO', 'MUNICIPIO'),
        ('27', '27073', N'BAGADÓ', 'MUNICIPIO'),
        ('27', '27075', N'BAHÍA SOLANO', 'MUNICIPIO'),
        ('27', '27077', N'BAJO BAUDÓ', 'MUNICIPIO'),
        ('27', '27099', N'BOJAYÁ', 'MUNICIPIO'),
        ('27', '27135', N'EL CANTÓN DEL SAN PABLO', 'MUNICIPIO'),
        ('27', '27150', N'CARMEN DEL DARIÉN', 'MUNICIPIO'),
        ('27', '27160', N'CÉRTEGUI', 'MUNICIPIO'),
        ('27', '27205', N'CONDOTO', 'MUNICIPIO'),
        ('27', '27245', N'EL CARMEN DE ATRATO', 'MUNICIPIO'),
        ('27', '27250', N'EL LITORAL DEL SAN JUAN', 'MUNICIPIO'),
        ('27', '27361', N'ISTMINA', 'MUNICIPIO'),
        ('27', '27372', N'JURADÓ', 'MUNICIPIO'),
        ('27', '27413', N'LLORÓ', 'MUNICIPIO'),
        ('27', '27425', N'MEDIO ATRATO', 'MUNICIPIO'),
        ('27', '27430', N'MEDIO BAUDÓ', 'MUNICIPIO'),
        ('27', '27450', N'MEDIO SAN JUAN', 'MUNICIPIO'),
        ('27', '27491', N'NÓVITA', 'MUNICIPIO'),
        ('27', '27493', N'NUEVO BELÉN DE BAJIRÁ', 'MUNICIPIO'),
        ('27', '27495', N'NUQUÍ', 'MUNICIPIO'),
        ('27', '27580', N'RÍO IRÓ', 'MUNICIPIO'),
        ('27', '27600', N'RÍO QUITO', 'MUNICIPIO'),
        ('27', '27615', N'RIOSUCIO', 'MUNICIPIO'),
        ('27', '27660', N'SAN JOSÉ DEL PALMAR', 'MUNICIPIO'),
        ('27', '27745', N'SIPÍ', 'MUNICIPIO'),
        ('27', '27787', N'TADÓ', 'MUNICIPIO'),
        ('27', '27800', N'UNGUÍA', 'MUNICIPIO'),
        ('27', '27810', N'UNIÓN PANAMERICANA', 'MUNICIPIO'),
        ('41', '41001', N'NEIVA', 'MUNICIPIO'),
        ('41', '41006', N'ACEVEDO', 'MUNICIPIO'),
        ('41', '41013', N'AGRADO', 'MUNICIPIO'),
        ('41', '41016', N'AIPE', 'MUNICIPIO'),
        ('41', '41020', N'ALGECIRAS', 'MUNICIPIO'),
        ('41', '41026', N'ALTAMIRA', 'MUNICIPIO'),
        ('41', '41078', N'BARAYA', 'MUNICIPIO'),
        ('41', '41132', N'CAMPOALEGRE', 'MUNICIPIO'),
        ('41', '41206', N'COLOMBIA', 'MUNICIPIO'),
        ('41', '41244', N'ELÍAS', 'MUNICIPIO'),
        ('41', '41298', N'GARZÓN', 'MUNICIPIO'),
        ('41', '41306', N'GIGANTE', 'MUNICIPIO'),
        ('41', '41319', N'GUADALUPE', 'MUNICIPIO'),
        ('41', '41349', N'HOBO', 'MUNICIPIO'),
        ('41', '41357', N'ÍQUIRA', 'MUNICIPIO'),
        ('41', '41359', N'ISNOS', 'MUNICIPIO'),
        ('41', '41378', N'LA ARGENTINA', 'MUNICIPIO'),
        ('41', '41396', N'LA PLATA', 'MUNICIPIO'),
        ('41', '41483', N'NÁTAGA', 'MUNICIPIO'),
        ('41', '41503', N'OPORAPA', 'MUNICIPIO'),
        ('41', '41518', N'PAICOL', 'MUNICIPIO'),
        ('41', '41524', N'PALERMO', 'MUNICIPIO'),
        ('41', '41530', N'PALESTINA', 'MUNICIPIO'),
        ('41', '41548', N'PITAL', 'MUNICIPIO'),
        ('41', '41551', N'PITALITO', 'MUNICIPIO'),
        ('41', '41615', N'RIVERA', 'MUNICIPIO'),
        ('41', '41660', N'SALADOBLANCO', 'MUNICIPIO'),
        ('41', '41668', N'SAN AGUSTÍN', 'MUNICIPIO'),
        ('41', '41676', N'SANTA MARÍA', 'MUNICIPIO'),
        ('41', '41770', N'SUAZA', 'MUNICIPIO'),
        ('41', '41791', N'TARQUI', 'MUNICIPIO'),
        ('41', '41797', N'TESALIA', 'MUNICIPIO'),
        ('41', '41799', N'TELLO', 'MUNICIPIO'),
        ('41', '41801', N'TERUEL', 'MUNICIPIO'),
        ('41', '41807', N'TIMANÁ', 'MUNICIPIO'),
        ('41', '41872', N'VILLAVIEJA', 'MUNICIPIO'),
        ('41', '41885', N'YAGUARÁ', 'MUNICIPIO'),
        ('44', '44001', N'RIOHACHA', 'MUNICIPIO'),
        ('44', '44035', N'ALBANIA', 'MUNICIPIO'),
        ('44', '44078', N'BARRANCAS', 'MUNICIPIO'),
        ('44', '44090', N'DIBULLA', 'MUNICIPIO'),
        ('44', '44098', N'DISTRACCIÓN', 'MUNICIPIO'),
        ('44', '44110', N'EL MOLINO', 'MUNICIPIO'),
        ('44', '44279', N'FONSECA', 'MUNICIPIO'),
        ('44', '44378', N'HATONUEVO', 'MUNICIPIO'),
        ('44', '44420', N'LA JAGUA DEL PILAR', 'MUNICIPIO'),
        ('44', '44430', N'MAICAO', 'MUNICIPIO'),
        ('44', '44560', N'MANAURE', 'MUNICIPIO'),
        ('44', '44650', N'SAN JUAN DEL CESAR', 'MUNICIPIO'),
        ('44', '44847', N'URIBIA', 'MUNICIPIO'),
        ('44', '44855', N'URUMITA', 'MUNICIPIO'),
        ('44', '44874', N'VILLANUEVA', 'MUNICIPIO'),
        ('47', '47001', N'SANTA MARTA', 'MUNICIPIO'),
        ('47', '47030', N'ALGARROBO', 'MUNICIPIO'),
        ('47', '47053', N'ARACATACA', 'MUNICIPIO'),
        ('47', '47058', N'ARIGUANÍ', 'MUNICIPIO'),
        ('47', '47161', N'CERRO DE SAN ANTONIO', 'MUNICIPIO'),
        ('47', '47170', N'CHIVOLO', 'MUNICIPIO'),
        ('47', '47189', N'CIÉNAGA', 'MUNICIPIO'),
        ('47', '47205', N'CONCORDIA', 'MUNICIPIO'),
        ('47', '47245', N'EL BANCO', 'MUNICIPIO'),
        ('47', '47258', N'EL PIÑÓN', 'MUNICIPIO'),
        ('47', '47268', N'EL RETÉN', 'MUNICIPIO'),
        ('47', '47288', N'FUNDACIÓN', 'MUNICIPIO'),
        ('47', '47318', N'GUAMAL', 'MUNICIPIO'),
        ('47', '47460', N'NUEVA GRANADA', 'MUNICIPIO'),
        ('47', '47541', N'PEDRAZA', 'MUNICIPIO'),
        ('47', '47545', N'PIJIÑO DEL CARMEN', 'MUNICIPIO'),
        ('47', '47551', N'PIVIJAY', 'MUNICIPIO'),
        ('47', '47555', N'PLATO', 'MUNICIPIO'),
        ('47', '47570', N'PUEBLOVIEJO', 'MUNICIPIO'),
        ('47', '47605', N'REMOLINO', 'MUNICIPIO'),
        ('47', '47660', N'SABANAS DE SAN ÁNGEL', 'MUNICIPIO'),
        ('47', '47675', N'SALAMINA', 'MUNICIPIO'),
        ('47', '47692', N'SAN SEBASTIÁN DE BUENAVISTA', 'MUNICIPIO'),
        ('47', '47703', N'SAN ZENÓN', 'MUNICIPIO'),
        ('47', '47707', N'SANTA ANA', 'MUNICIPIO'),
        ('47', '47720', N'SANTA BÁRBARA DE PINTO', 'MUNICIPIO'),
        ('47', '47745', N'SITIONUEVO', 'MUNICIPIO'),
        ('47', '47798', N'TENERIFE', 'MUNICIPIO'),
        ('47', '47960', N'ZAPAYÁN', 'MUNICIPIO'),
        ('47', '47980', N'ZONA BANANERA', 'MUNICIPIO'),
        ('50', '50001', N'VILLAVICENCIO', 'MUNICIPIO'),
        ('50', '50006', N'ACACÍAS', 'MUNICIPIO'),
        ('50', '50110', N'BARRANCA DE UPÍA', 'MUNICIPIO'),
        ('50', '50124', N'CABUYARO', 'MUNICIPIO'),
        ('50', '50150', N'CASTILLA LA NUEVA', 'MUNICIPIO'),
        ('50', '50223', N'CUBARRAL', 'MUNICIPIO'),
        ('50', '50226', N'CUMARAL', 'MUNICIPIO'),
        ('50', '50245', N'EL CALVARIO', 'MUNICIPIO'),
        ('50', '50251', N'EL CASTILLO', 'MUNICIPIO'),
        ('50', '50270', N'EL DORADO', 'MUNICIPIO'),
        ('50', '50287', N'FUENTE DE ORO', 'MUNICIPIO'),
        ('50', '50313', N'GRANADA', 'MUNICIPIO'),
        ('50', '50318', N'GUAMAL', 'MUNICIPIO'),
        ('50', '50325', N'MAPIRIPÁN', 'MUNICIPIO'),
        ('50', '50330', N'MESETAS', 'MUNICIPIO'),
        ('50', '50350', N'LA MACARENA', 'MUNICIPIO'),
        ('50', '50370', N'URIBE', 'MUNICIPIO'),
        ('50', '50400', N'LEJANÍAS', 'MUNICIPIO'),
        ('50', '50450', N'PUERTO CONCORDIA', 'MUNICIPIO'),
        ('50', '50568', N'PUERTO GAITÁN', 'MUNICIPIO'),
        ('50', '50573', N'PUERTO LÓPEZ', 'MUNICIPIO'),
        ('50', '50577', N'PUERTO LLERAS', 'MUNICIPIO'),
        ('50', '50590', N'PUERTO RICO', 'MUNICIPIO'),
        ('50', '50606', N'RESTREPO', 'MUNICIPIO'),
        ('50', '50680', N'SAN CARLOS DE GUAROA', 'MUNICIPIO'),
        ('50', '50683', N'SAN JUAN DE ARAMA', 'MUNICIPIO'),
        ('50', '50686', N'SAN JUANITO', 'MUNICIPIO'),
        ('50', '50689', N'SAN MARTÍN', 'MUNICIPIO'),
        ('50', '50711', N'VISTAHERMOSA', 'MUNICIPIO'),
        ('52', '52001', N'PASTO', 'MUNICIPIO'),
        ('52', '52019', N'ALBÁN', 'MUNICIPIO'),
        ('52', '52022', N'ALDANA', 'MUNICIPIO'),
        ('52', '52036', N'ANCUYA', 'MUNICIPIO'),
        ('52', '52051', N'ARBOLEDA', 'MUNICIPIO'),
        ('52', '52079', N'BARBACOAS', 'MUNICIPIO'),
        ('52', '52083', N'BELÉN', 'MUNICIPIO'),
        ('52', '52110', N'BUESACO', 'MUNICIPIO'),
        ('52', '52203', N'COLÓN', 'MUNICIPIO'),
        ('52', '52207', N'CONSACÁ', 'MUNICIPIO'),
        ('52', '52210', N'CONTADERO', 'MUNICIPIO'),
        ('52', '52215', N'CÓRDOBA', 'MUNICIPIO'),
        ('52', '52224', N'CUASPUD CARLOSAMA', 'MUNICIPIO'),
        ('52', '52227', N'CUMBAL', 'MUNICIPIO'),
        ('52', '52233', N'CUMBITARA', 'MUNICIPIO'),
        ('52', '52240', N'CHACHAGÜÍ', 'MUNICIPIO'),
        ('52', '52250', N'EL CHARCO', 'MUNICIPIO'),
        ('52', '52254', N'EL PEÑOL', 'MUNICIPIO'),
        ('52', '52256', N'EL ROSARIO', 'MUNICIPIO'),
        ('52', '52258', N'EL TABLÓN DE GÓMEZ', 'MUNICIPIO'),
        ('52', '52260', N'EL TAMBO', 'MUNICIPIO'),
        ('52', '52287', N'FUNES', 'MUNICIPIO'),
        ('52', '52317', N'GUACHUCAL', 'MUNICIPIO'),
        ('52', '52320', N'GUAITARILLA', 'MUNICIPIO'),
        ('52', '52323', N'GUALMATÁN', 'MUNICIPIO'),
        ('52', '52352', N'ILES', 'MUNICIPIO'),
        ('52', '52354', N'IMUÉS', 'MUNICIPIO'),
        ('52', '52356', N'IPIALES', 'MUNICIPIO'),
        ('52', '52378', N'LA CRUZ', 'MUNICIPIO'),
        ('52', '52381', N'LA FLORIDA', 'MUNICIPIO'),
        ('52', '52385', N'LA LLANADA', 'MUNICIPIO'),
        ('52', '52390', N'LA TOLA', 'MUNICIPIO'),
        ('52', '52399', N'LA UNIÓN', 'MUNICIPIO'),
        ('52', '52405', N'LEIVA', 'MUNICIPIO'),
        ('52', '52411', N'LINARES', 'MUNICIPIO'),
        ('52', '52418', N'LOS ANDES', 'MUNICIPIO'),
        ('52', '52427', N'MAGÜÍ', 'MUNICIPIO'),
        ('52', '52435', N'MALLAMA', 'MUNICIPIO'),
        ('52', '52473', N'MOSQUERA', 'MUNICIPIO'),
        ('52', '52480', N'NARIÑO', 'MUNICIPIO'),
        ('52', '52490', N'OLAYA HERRERA', 'MUNICIPIO'),
        ('52', '52506', N'OSPINA', 'MUNICIPIO'),
        ('52', '52520', N'FRANCISCO PIZARRO', 'MUNICIPIO'),
        ('52', '52540', N'POLICARPA', 'MUNICIPIO'),
        ('52', '52560', N'POTOSÍ', 'MUNICIPIO'),
        ('52', '52565', N'PROVIDENCIA', 'MUNICIPIO'),
        ('52', '52573', N'PUERRES', 'MUNICIPIO'),
        ('52', '52585', N'PUPIALES', 'MUNICIPIO'),
        ('52', '52612', N'RICAURTE', 'MUNICIPIO'),
        ('52', '52621', N'ROBERTO PAYÁN', 'MUNICIPIO'),
        ('52', '52678', N'SAMANIEGO', 'MUNICIPIO'),
        ('52', '52683', N'SANDONÁ', 'MUNICIPIO'),
        ('52', '52685', N'SAN BERNARDO', 'MUNICIPIO'),
        ('52', '52687', N'SAN LORENZO', 'MUNICIPIO'),
        ('52', '52693', N'SAN PABLO', 'MUNICIPIO'),
        ('52', '52694', N'SAN PEDRO DE CARTAGO', 'MUNICIPIO'),
        ('52', '52696', N'SANTA BÁRBARA', 'MUNICIPIO'),
        ('52', '52699', N'SANTACRUZ', 'MUNICIPIO'),
        ('52', '52720', N'SAPUYES', 'MUNICIPIO'),
        ('52', '52786', N'TAMINANGO', 'MUNICIPIO'),
        ('52', '52788', N'TANGUA', 'MUNICIPIO'),
        ('52', '52835', N'SAN ANDRÉS DE TUMACO', 'MUNICIPIO'),
        ('52', '52838', N'TÚQUERRES', 'MUNICIPIO'),
        ('52', '52885', N'YACUANQUER', 'MUNICIPIO'),
        ('54', '54001', N'SAN JOSÉ DE CÚCUTA', 'MUNICIPIO'),
        ('54', '54003', N'ÁBREGO', 'MUNICIPIO'),
        ('54', '54051', N'ARBOLEDAS', 'MUNICIPIO'),
        ('54', '54099', N'BOCHALEMA', 'MUNICIPIO'),
        ('54', '54109', N'BUCARASICA', 'MUNICIPIO'),
        ('54', '54125', N'CÁCOTA', 'MUNICIPIO'),
        ('54', '54128', N'CÁCHIRA', 'MUNICIPIO'),
        ('54', '54172', N'CHINÁCOTA', 'MUNICIPIO'),
        ('54', '54174', N'CHITAGÁ', 'MUNICIPIO'),
        ('54', '54206', N'CONVENCIÓN', 'MUNICIPIO'),
        ('54', '54223', N'CUCUTILLA', 'MUNICIPIO'),
        ('54', '54239', N'DURANIA', 'MUNICIPIO'),
        ('54', '54245', N'EL CARMEN', 'MUNICIPIO'),
        ('54', '54250', N'EL TARRA', 'MUNICIPIO'),
        ('54', '54261', N'EL ZULIA', 'MUNICIPIO'),
        ('54', '54313', N'GRAMALOTE', 'MUNICIPIO'),
        ('54', '54344', N'HACARÍ', 'MUNICIPIO'),
        ('54', '54347', N'HERRÁN', 'MUNICIPIO'),
        ('54', '54377', N'LABATECA', 'MUNICIPIO'),
        ('54', '54385', N'LA ESPERANZA', 'MUNICIPIO')
    ) AS v(codigo_departamento, codigo_ciudad, nombre_ciudad, tipo)
    JOIN dbo.catalogo_departamentos d
        ON d.id_pais = @idPaisCol AND d.codigo_divipola = v.codigo_departamento
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.catalogo_ciudades c
        WHERE c.id_departamento = d.id_departamento AND c.codigo_divipola = v.codigo_ciudad
    );

    -- Lote 3/3 de ciudades (322 filas)
    INSERT INTO dbo.catalogo_ciudades (id_departamento, codigo_divipola, nombre, tipo)
    SELECT d.id_departamento, v.codigo_ciudad, v.nombre_ciudad, v.tipo
    FROM (VALUES
        ('54', '54398', N'LA PLAYA', 'MUNICIPIO'),
        ('54', '54405', N'LOS PATIOS', 'MUNICIPIO'),
        ('54', '54418', N'LOURDES', 'MUNICIPIO'),
        ('54', '54480', N'MUTISCUA', 'MUNICIPIO'),
        ('54', '54498', N'OCAÑA', 'MUNICIPIO'),
        ('54', '54518', N'PAMPLONA', 'MUNICIPIO'),
        ('54', '54520', N'PAMPLONITA', 'MUNICIPIO'),
        ('54', '54553', N'PUERTO SANTANDER', 'MUNICIPIO'),
        ('54', '54599', N'RAGONVALIA', 'MUNICIPIO'),
        ('54', '54660', N'SALAZAR', 'MUNICIPIO'),
        ('54', '54670', N'SAN CALIXTO', 'MUNICIPIO'),
        ('54', '54673', N'SAN CAYETANO', 'MUNICIPIO'),
        ('54', '54680', N'SANTIAGO', 'MUNICIPIO'),
        ('54', '54720', N'SARDINATA', 'MUNICIPIO'),
        ('54', '54743', N'SILOS', 'MUNICIPIO'),
        ('54', '54800', N'TEORAMA', 'MUNICIPIO'),
        ('54', '54810', N'TIBÚ', 'MUNICIPIO'),
        ('54', '54820', N'TOLEDO', 'MUNICIPIO'),
        ('54', '54871', N'VILLA CARO', 'MUNICIPIO'),
        ('54', '54874', N'VILLA DEL ROSARIO', 'MUNICIPIO'),
        ('63', '63001', N'ARMENIA', 'MUNICIPIO'),
        ('63', '63111', N'BUENAVISTA', 'MUNICIPIO'),
        ('63', '63130', N'CALARCÁ', 'MUNICIPIO'),
        ('63', '63190', N'CIRCASIA', 'MUNICIPIO'),
        ('63', '63212', N'CÓRDOBA', 'MUNICIPIO'),
        ('63', '63272', N'FILANDIA', 'MUNICIPIO'),
        ('63', '63302', N'GÉNOVA', 'MUNICIPIO'),
        ('63', '63401', N'LA TEBAIDA', 'MUNICIPIO'),
        ('63', '63470', N'MONTENEGRO', 'MUNICIPIO'),
        ('63', '63548', N'PIJAO', 'MUNICIPIO'),
        ('63', '63594', N'QUIMBAYA', 'MUNICIPIO'),
        ('63', '63690', N'SALENTO', 'MUNICIPIO'),
        ('66', '66001', N'PEREIRA', 'MUNICIPIO'),
        ('66', '66045', N'APÍA', 'MUNICIPIO'),
        ('66', '66075', N'BALBOA', 'MUNICIPIO'),
        ('66', '66088', N'BELÉN DE UMBRÍA', 'MUNICIPIO'),
        ('66', '66170', N'DOSQUEBRADAS', 'MUNICIPIO'),
        ('66', '66318', N'GUÁTICA', 'MUNICIPIO'),
        ('66', '66383', N'LA CELIA', 'MUNICIPIO'),
        ('66', '66400', N'LA VIRGINIA', 'MUNICIPIO'),
        ('66', '66440', N'MARSELLA', 'MUNICIPIO'),
        ('66', '66456', N'MISTRATÓ', 'MUNICIPIO'),
        ('66', '66572', N'PUEBLO RICO', 'MUNICIPIO'),
        ('66', '66594', N'QUINCHÍA', 'MUNICIPIO'),
        ('66', '66682', N'SANTA ROSA DE CABAL', 'MUNICIPIO'),
        ('66', '66687', N'SANTUARIO', 'MUNICIPIO'),
        ('68', '68001', N'BUCARAMANGA', 'MUNICIPIO'),
        ('68', '68013', N'AGUADA', 'MUNICIPIO'),
        ('68', '68020', N'ALBANIA', 'MUNICIPIO'),
        ('68', '68051', N'ARATOCA', 'MUNICIPIO'),
        ('68', '68077', N'BARBOSA', 'MUNICIPIO'),
        ('68', '68079', N'BARICHARA', 'MUNICIPIO'),
        ('68', '68081', N'BARRANCABERMEJA', 'MUNICIPIO'),
        ('68', '68092', N'BETULIA', 'MUNICIPIO'),
        ('68', '68101', N'BOLÍVAR', 'MUNICIPIO'),
        ('68', '68121', N'CABRERA', 'MUNICIPIO'),
        ('68', '68132', N'CALIFORNIA', 'MUNICIPIO'),
        ('68', '68147', N'CAPITANEJO', 'MUNICIPIO'),
        ('68', '68152', N'CARCASÍ', 'MUNICIPIO'),
        ('68', '68160', N'CEPITÁ', 'MUNICIPIO'),
        ('68', '68162', N'CERRITO', 'MUNICIPIO'),
        ('68', '68167', N'CHARALÁ', 'MUNICIPIO'),
        ('68', '68169', N'CHARTA', 'MUNICIPIO'),
        ('68', '68176', N'CHIMA', 'MUNICIPIO'),
        ('68', '68179', N'CHIPATÁ', 'MUNICIPIO'),
        ('68', '68190', N'CIMITARRA', 'MUNICIPIO'),
        ('68', '68207', N'CONCEPCIÓN', 'MUNICIPIO'),
        ('68', '68209', N'CONFINES', 'MUNICIPIO'),
        ('68', '68211', N'CONTRATACIÓN', 'MUNICIPIO'),
        ('68', '68217', N'COROMORO', 'MUNICIPIO'),
        ('68', '68229', N'CURITÍ', 'MUNICIPIO'),
        ('68', '68235', N'EL CARMEN DE CHUCURÍ', 'MUNICIPIO'),
        ('68', '68245', N'EL GUACAMAYO', 'MUNICIPIO'),
        ('68', '68250', N'EL PEÑÓN', 'MUNICIPIO'),
        ('68', '68255', N'EL PLAYÓN', 'MUNICIPIO'),
        ('68', '68264', N'ENCINO', 'MUNICIPIO'),
        ('68', '68266', N'ENCISO', 'MUNICIPIO'),
        ('68', '68271', N'FLORIÁN', 'MUNICIPIO'),
        ('68', '68276', N'FLORIDABLANCA', 'MUNICIPIO'),
        ('68', '68296', N'GALÁN', 'MUNICIPIO'),
        ('68', '68298', N'GÁMBITA', 'MUNICIPIO'),
        ('68', '68307', N'GIRÓN', 'MUNICIPIO'),
        ('68', '68318', N'GUACA', 'MUNICIPIO'),
        ('68', '68320', N'GUADALUPE', 'MUNICIPIO'),
        ('68', '68322', N'GUAPOTÁ', 'MUNICIPIO'),
        ('68', '68324', N'GUAVATÁ', 'MUNICIPIO'),
        ('68', '68327', N'GÜEPSA', 'MUNICIPIO'),
        ('68', '68344', N'HATO', 'MUNICIPIO'),
        ('68', '68368', N'JESÚS MARÍA', 'MUNICIPIO'),
        ('68', '68370', N'JORDÁN', 'MUNICIPIO'),
        ('68', '68377', N'LA BELLEZA', 'MUNICIPIO'),
        ('68', '68385', N'LANDÁZURI', 'MUNICIPIO'),
        ('68', '68397', N'LA PAZ', 'MUNICIPIO'),
        ('68', '68406', N'LEBRIJA', 'MUNICIPIO'),
        ('68', '68418', N'LOS SANTOS', 'MUNICIPIO'),
        ('68', '68425', N'MACARAVITA', 'MUNICIPIO'),
        ('68', '68432', N'MÁLAGA', 'MUNICIPIO'),
        ('68', '68444', N'MATANZA', 'MUNICIPIO'),
        ('68', '68464', N'MOGOTES', 'MUNICIPIO'),
        ('68', '68468', N'MOLAGAVITA', 'MUNICIPIO'),
        ('68', '68498', N'OCAMONTE', 'MUNICIPIO'),
        ('68', '68500', N'OIBA', 'MUNICIPIO'),
        ('68', '68502', N'ONZAGA', 'MUNICIPIO'),
        ('68', '68522', N'PALMAR', 'MUNICIPIO'),
        ('68', '68524', N'PALMAS DEL SOCORRO', 'MUNICIPIO'),
        ('68', '68533', N'PÁRAMO', 'MUNICIPIO'),
        ('68', '68547', N'PIEDECUESTA', 'MUNICIPIO'),
        ('68', '68549', N'PINCHOTE', 'MUNICIPIO'),
        ('68', '68572', N'PUENTE NACIONAL', 'MUNICIPIO'),
        ('68', '68573', N'PUERTO PARRA', 'MUNICIPIO'),
        ('68', '68575', N'PUERTO WILCHES', 'MUNICIPIO'),
        ('68', '68615', N'RIONEGRO', 'MUNICIPIO'),
        ('68', '68655', N'SABANA DE TORRES', 'MUNICIPIO'),
        ('68', '68669', N'SAN ANDRÉS', 'MUNICIPIO'),
        ('68', '68673', N'SAN BENITO', 'MUNICIPIO'),
        ('68', '68679', N'SAN GIL', 'MUNICIPIO'),
        ('68', '68682', N'SAN JOAQUÍN', 'MUNICIPIO'),
        ('68', '68684', N'SAN JOSÉ DE MIRANDA', 'MUNICIPIO'),
        ('68', '68686', N'SAN MIGUEL', 'MUNICIPIO'),
        ('68', '68689', N'SAN VICENTE DE CHUCURÍ', 'MUNICIPIO'),
        ('68', '68705', N'SANTA BÁRBARA', 'MUNICIPIO'),
        ('68', '68720', N'SANTA HELENA DEL OPÓN', 'MUNICIPIO'),
        ('68', '68745', N'SIMACOTA', 'MUNICIPIO'),
        ('68', '68755', N'SOCORRO', 'MUNICIPIO'),
        ('68', '68770', N'SUAITA', 'MUNICIPIO'),
        ('68', '68773', N'SUCRE', 'MUNICIPIO'),
        ('68', '68780', N'SURATÁ', 'MUNICIPIO'),
        ('68', '68820', N'TONA', 'MUNICIPIO'),
        ('68', '68855', N'VALLE DE SAN JOSÉ', 'MUNICIPIO'),
        ('68', '68861', N'VÉLEZ', 'MUNICIPIO'),
        ('68', '68867', N'VETAS', 'MUNICIPIO'),
        ('68', '68872', N'VILLANUEVA', 'MUNICIPIO'),
        ('68', '68895', N'ZAPATOCA', 'MUNICIPIO'),
        ('70', '70001', N'SINCELEJO', 'MUNICIPIO'),
        ('70', '70110', N'BUENAVISTA', 'MUNICIPIO'),
        ('70', '70124', N'CAIMITO', 'MUNICIPIO'),
        ('70', '70204', N'COLOSÓ', 'MUNICIPIO'),
        ('70', '70215', N'COROZAL', 'MUNICIPIO'),
        ('70', '70221', N'COVEÑAS', 'MUNICIPIO'),
        ('70', '70230', N'CHALÁN', 'MUNICIPIO'),
        ('70', '70233', N'EL ROBLE', 'MUNICIPIO'),
        ('70', '70235', N'GALERAS', 'MUNICIPIO'),
        ('70', '70265', N'GUARANDA', 'MUNICIPIO'),
        ('70', '70400', N'LA UNIÓN', 'MUNICIPIO'),
        ('70', '70418', N'LOS PALMITOS', 'MUNICIPIO'),
        ('70', '70429', N'MAJAGUAL', 'MUNICIPIO'),
        ('70', '70473', N'MORROA', 'MUNICIPIO'),
        ('70', '70508', N'OVEJAS', 'MUNICIPIO'),
        ('70', '70523', N'PALMITO', 'MUNICIPIO'),
        ('70', '70670', N'SAMPUÉS', 'MUNICIPIO'),
        ('70', '70678', N'SAN BENITO ABAD', 'MUNICIPIO'),
        ('70', '70702', N'SAN JUAN DE BETULIA', 'MUNICIPIO'),
        ('70', '70708', N'SAN MARCOS', 'MUNICIPIO'),
        ('70', '70713', N'SAN ONOFRE', 'MUNICIPIO'),
        ('70', '70717', N'SAN PEDRO', 'MUNICIPIO'),
        ('70', '70742', N'SAN LUIS DE SINCÉ', 'MUNICIPIO'),
        ('70', '70771', N'SUCRE', 'MUNICIPIO'),
        ('70', '70820', N'SANTIAGO DE TOLÚ', 'MUNICIPIO'),
        ('70', '70823', N'SAN JOSÉ DE TOLUVIEJO', 'MUNICIPIO'),
        ('73', '73001', N'IBAGUÉ', 'MUNICIPIO'),
        ('73', '73024', N'ALPUJARRA', 'MUNICIPIO'),
        ('73', '73026', N'ALVARADO', 'MUNICIPIO'),
        ('73', '73030', N'AMBALEMA', 'MUNICIPIO'),
        ('73', '73043', N'ANZOÁTEGUI', 'MUNICIPIO'),
        ('73', '73055', N'ARMERO', 'MUNICIPIO'),
        ('73', '73067', N'ATACO', 'MUNICIPIO'),
        ('73', '73124', N'CAJAMARCA', 'MUNICIPIO'),
        ('73', '73148', N'CARMEN DE APICALÁ', 'MUNICIPIO'),
        ('73', '73152', N'CASABIANCA', 'MUNICIPIO'),
        ('73', '73168', N'CHAPARRAL', 'MUNICIPIO'),
        ('73', '73200', N'COELLO', 'MUNICIPIO'),
        ('73', '73217', N'COYAIMA', 'MUNICIPIO'),
        ('73', '73226', N'CUNDAY', 'MUNICIPIO'),
        ('73', '73236', N'DOLORES', 'MUNICIPIO'),
        ('73', '73268', N'ESPINAL', 'MUNICIPIO'),
        ('73', '73270', N'FALAN', 'MUNICIPIO'),
        ('73', '73275', N'FLANDES', 'MUNICIPIO'),
        ('73', '73283', N'FRESNO', 'MUNICIPIO'),
        ('73', '73319', N'GUAMO', 'MUNICIPIO'),
        ('73', '73347', N'HERVEO', 'MUNICIPIO'),
        ('73', '73349', N'HONDA', 'MUNICIPIO'),
        ('73', '73352', N'ICONONZO', 'MUNICIPIO'),
        ('73', '73408', N'LÉRIDA', 'MUNICIPIO'),
        ('73', '73411', N'LÍBANO', 'MUNICIPIO'),
        ('73', '73443', N'SAN SEBASTIÁN DE MARIQUITA', 'MUNICIPIO'),
        ('73', '73449', N'MELGAR', 'MUNICIPIO'),
        ('73', '73461', N'MURILLO', 'MUNICIPIO'),
        ('73', '73483', N'NATAGAIMA', 'MUNICIPIO'),
        ('73', '73504', N'ORTEGA', 'MUNICIPIO'),
        ('73', '73520', N'PALOCABILDO', 'MUNICIPIO'),
        ('73', '73547', N'PIEDRAS', 'MUNICIPIO'),
        ('73', '73555', N'PLANADAS', 'MUNICIPIO'),
        ('73', '73563', N'PRADO', 'MUNICIPIO'),
        ('73', '73585', N'PURIFICACIÓN', 'MUNICIPIO'),
        ('73', '73616', N'RIOBLANCO', 'MUNICIPIO'),
        ('73', '73622', N'RONCESVALLES', 'MUNICIPIO'),
        ('73', '73624', N'ROVIRA', 'MUNICIPIO'),
        ('73', '73671', N'SALDAÑA', 'MUNICIPIO'),
        ('73', '73675', N'SAN ANTONIO', 'MUNICIPIO'),
        ('73', '73678', N'SAN LUIS', 'MUNICIPIO'),
        ('73', '73686', N'SANTA ISABEL', 'MUNICIPIO'),
        ('73', '73770', N'SUÁREZ', 'MUNICIPIO'),
        ('73', '73854', N'VALLE DE SAN JUAN', 'MUNICIPIO'),
        ('73', '73861', N'VENADILLO', 'MUNICIPIO'),
        ('73', '73870', N'VILLAHERMOSA', 'MUNICIPIO'),
        ('73', '73873', N'VILLARRICA', 'MUNICIPIO'),
        ('76', '76001', N'SANTIAGO DE CALI', 'MUNICIPIO'),
        ('76', '76020', N'ALCALÁ', 'MUNICIPIO'),
        ('76', '76036', N'ANDALUCÍA', 'MUNICIPIO'),
        ('76', '76041', N'ANSERMANUEVO', 'MUNICIPIO'),
        ('76', '76054', N'ARGELIA', 'MUNICIPIO'),
        ('76', '76100', N'BOLÍVAR', 'MUNICIPIO'),
        ('76', '76109', N'BUENAVENTURA', 'MUNICIPIO'),
        ('76', '76111', N'GUADALAJARA DE BUGA', 'MUNICIPIO'),
        ('76', '76113', N'BUGALAGRANDE', 'MUNICIPIO'),
        ('76', '76122', N'CAICEDONIA', 'MUNICIPIO'),
        ('76', '76126', N'CALIMA', 'MUNICIPIO'),
        ('76', '76130', N'CANDELARIA', 'MUNICIPIO'),
        ('76', '76147', N'CARTAGO', 'MUNICIPIO'),
        ('76', '76233', N'DAGUA', 'MUNICIPIO'),
        ('76', '76243', N'EL ÁGUILA', 'MUNICIPIO'),
        ('76', '76246', N'EL CAIRO', 'MUNICIPIO'),
        ('76', '76248', N'EL CERRITO', 'MUNICIPIO'),
        ('76', '76250', N'EL DOVIO', 'MUNICIPIO'),
        ('76', '76275', N'FLORIDA', 'MUNICIPIO'),
        ('76', '76306', N'GINEBRA', 'MUNICIPIO'),
        ('76', '76318', N'GUACARÍ', 'MUNICIPIO'),
        ('76', '76364', N'JAMUNDÍ', 'MUNICIPIO'),
        ('76', '76377', N'LA CUMBRE', 'MUNICIPIO'),
        ('76', '76400', N'LA UNIÓN', 'MUNICIPIO'),
        ('76', '76403', N'LA VICTORIA', 'MUNICIPIO'),
        ('76', '76497', N'OBANDO', 'MUNICIPIO'),
        ('76', '76520', N'PALMIRA', 'MUNICIPIO'),
        ('76', '76563', N'PRADERA', 'MUNICIPIO'),
        ('76', '76606', N'RESTREPO', 'MUNICIPIO'),
        ('76', '76616', N'RIOFRÍO', 'MUNICIPIO'),
        ('76', '76622', N'ROLDANILLO', 'MUNICIPIO'),
        ('76', '76670', N'SAN PEDRO', 'MUNICIPIO'),
        ('76', '76736', N'SEVILLA', 'MUNICIPIO'),
        ('76', '76823', N'TORO', 'MUNICIPIO'),
        ('76', '76828', N'TRUJILLO', 'MUNICIPIO'),
        ('76', '76834', N'TULUÁ', 'MUNICIPIO'),
        ('76', '76845', N'ULLOA', 'MUNICIPIO'),
        ('76', '76863', N'VERSALLES', 'MUNICIPIO'),
        ('76', '76869', N'VIJES', 'MUNICIPIO'),
        ('76', '76890', N'YOTOCO', 'MUNICIPIO'),
        ('76', '76892', N'YUMBO', 'MUNICIPIO'),
        ('76', '76895', N'ZARZAL', 'MUNICIPIO'),
        ('81', '81001', N'ARAUCA', 'MUNICIPIO'),
        ('81', '81065', N'ARAUQUITA', 'MUNICIPIO'),
        ('81', '81220', N'CRAVO NORTE', 'MUNICIPIO'),
        ('81', '81300', N'FORTUL', 'MUNICIPIO'),
        ('81', '81591', N'PUERTO RONDÓN', 'MUNICIPIO'),
        ('81', '81736', N'SARAVENA', 'MUNICIPIO'),
        ('81', '81794', N'TAME', 'MUNICIPIO'),
        ('85', '85001', N'YOPAL', 'MUNICIPIO'),
        ('85', '85010', N'AGUAZUL', 'MUNICIPIO'),
        ('85', '85015', N'CHÁMEZA', 'MUNICIPIO'),
        ('85', '85125', N'HATO COROZAL', 'MUNICIPIO'),
        ('85', '85136', N'LA SALINA', 'MUNICIPIO'),
        ('85', '85139', N'MANÍ', 'MUNICIPIO'),
        ('85', '85162', N'MONTERREY', 'MUNICIPIO'),
        ('85', '85225', N'NUNCHÍA', 'MUNICIPIO'),
        ('85', '85230', N'OROCUÉ', 'MUNICIPIO'),
        ('85', '85250', N'PAZ DE ARIPORO', 'MUNICIPIO'),
        ('85', '85263', N'PORE', 'MUNICIPIO'),
        ('85', '85279', N'RECETOR', 'MUNICIPIO'),
        ('85', '85300', N'SABANALARGA', 'MUNICIPIO'),
        ('85', '85315', N'SÁCAMA', 'MUNICIPIO'),
        ('85', '85325', N'SAN LUIS DE PALENQUE', 'MUNICIPIO'),
        ('85', '85400', N'TÁMARA', 'MUNICIPIO'),
        ('85', '85410', N'TAURAMENA', 'MUNICIPIO'),
        ('85', '85430', N'TRINIDAD', 'MUNICIPIO'),
        ('85', '85440', N'VILLANUEVA', 'MUNICIPIO'),
        ('86', '86001', N'MOCOA', 'MUNICIPIO'),
        ('86', '86219', N'COLÓN', 'MUNICIPIO'),
        ('86', '86320', N'ORITO', 'MUNICIPIO'),
        ('86', '86568', N'PUERTO ASÍS', 'MUNICIPIO'),
        ('86', '86569', N'PUERTO CAICEDO', 'MUNICIPIO'),
        ('86', '86571', N'PUERTO GUZMÁN', 'MUNICIPIO'),
        ('86', '86573', N'PUERTO LEGUÍZAMO', 'MUNICIPIO'),
        ('86', '86749', N'SIBUNDOY', 'MUNICIPIO'),
        ('86', '86755', N'SAN FRANCISCO', 'MUNICIPIO'),
        ('86', '86757', N'SAN MIGUEL', 'MUNICIPIO'),
        ('86', '86760', N'SANTIAGO', 'MUNICIPIO'),
        ('86', '86865', N'VALLE DEL GUAMUEZ', 'MUNICIPIO'),
        ('86', '86885', N'VILLAGARZÓN', 'MUNICIPIO'),
        ('88', '88001', N'SAN ANDRÉS', 'ISLA'),
        ('88', '88564', N'PROVIDENCIA', 'MUNICIPIO'),
        ('91', '91001', N'LETICIA', 'MUNICIPIO'),
        ('91', '91263', N'EL ENCANTO', 'AREA_NO_MUNICIPALIZADA'),
        ('91', '91405', N'LA CHORRERA', 'AREA_NO_MUNICIPALIZADA'),
        ('91', '91407', N'LA PEDRERA', 'AREA_NO_MUNICIPALIZADA'),
        ('91', '91430', N'LA VICTORIA', 'AREA_NO_MUNICIPALIZADA'),
        ('91', '91460', N'MIRITÍ - PARANÁ', 'AREA_NO_MUNICIPALIZADA'),
        ('91', '91530', N'PUERTO ALEGRÍA', 'AREA_NO_MUNICIPALIZADA'),
        ('91', '91536', N'PUERTO ARICA', 'AREA_NO_MUNICIPALIZADA'),
        ('91', '91540', N'PUERTO NARIÑO', 'MUNICIPIO'),
        ('91', '91669', N'PUERTO SANTANDER', 'AREA_NO_MUNICIPALIZADA'),
        ('91', '91798', N'TARAPACÁ', 'AREA_NO_MUNICIPALIZADA'),
        ('94', '94001', N'INÍRIDA', 'MUNICIPIO'),
        ('94', '94343', N'BARRANCOMINAS', 'MUNICIPIO'),
        ('94', '94883', N'SAN FELIPE', 'AREA_NO_MUNICIPALIZADA'),
        ('94', '94884', N'PUERTO COLOMBIA', 'AREA_NO_MUNICIPALIZADA'),
        ('94', '94885', N'LA GUADALUPE', 'AREA_NO_MUNICIPALIZADA'),
        ('94', '94886', N'CACAHUAL', 'AREA_NO_MUNICIPALIZADA'),
        ('94', '94887', N'PANA PANA', 'AREA_NO_MUNICIPALIZADA'),
        ('94', '94888', N'MORICHAL', 'AREA_NO_MUNICIPALIZADA'),
        ('95', '95001', N'SAN JOSÉ DEL GUAVIARE', 'MUNICIPIO'),
        ('95', '95015', N'CALAMAR', 'MUNICIPIO'),
        ('95', '95025', N'EL RETORNO', 'MUNICIPIO'),
        ('95', '95200', N'MIRAFLORES', 'MUNICIPIO'),
        ('97', '97001', N'MITÚ', 'MUNICIPIO'),
        ('97', '97161', N'CARURÚ', 'MUNICIPIO'),
        ('97', '97511', N'PACOA', 'AREA_NO_MUNICIPALIZADA'),
        ('97', '97666', N'TARAIRA', 'MUNICIPIO'),
        ('97', '97777', N'PAPUNAHUA', 'AREA_NO_MUNICIPALIZADA'),
        ('97', '97889', N'YAVARATÉ', 'AREA_NO_MUNICIPALIZADA'),
        ('99', '99001', N'PUERTO CARREÑO', 'MUNICIPIO'),
        ('99', '99524', N'LA PRIMAVERA', 'MUNICIPIO'),
        ('99', '99624', N'SANTA ROSALÍA', 'MUNICIPIO'),
        ('99', '99773', N'CUMARIBO', 'MUNICIPIO')
    ) AS v(codigo_departamento, codigo_ciudad, nombre_ciudad, tipo)
    JOIN dbo.catalogo_departamentos d
        ON d.id_pais = @idPaisCol AND d.codigo_divipola = v.codigo_departamento
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.catalogo_ciudades c
        WHERE c.id_departamento = d.id_departamento AND c.codigo_divipola = v.codigo_ciudad
    );

    PRINT 'OK: ciudades sembradas/verificadas (1.122 esperadas)';

    COMMIT TRANSACTION;
    PRINT '=== FIN MIGRACIÓN 031 (COMMIT OK) ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '=== ERROR EN MIGRACIÓN 031 — ROLLBACK EJECUTADO ===';
    THROW;
END CATCH;

-- ═══════════════════════════════════════════════════════════════════════
-- Verificación final — banderas booleanas independientes + conteos reales
-- comparados contra los valores oficiales derivados del DANE (33 / 1.122 /
-- 1.103 / 1 / 18). No es el mecanismo de aplicación de la política (eso ya
-- ocurrió vía THROW dentro de la transacción) — es una confirmación legible
-- para quien ejecute el script.
-- ═══════════════════════════════════════════════════════════════════════

DECLARE @objIdPaisesFinal INT = OBJECT_ID('dbo.catalogo_paises', 'U');
DECLARE @objIdDeptosFinal INT = OBJECT_ID('dbo.catalogo_departamentos', 'U');
DECLARE @objIdCiudadesFinal INT = OBJECT_ID('dbo.catalogo_ciudades', 'U');

SELECT
    CASE WHEN @objIdPaisesFinal IS NOT NULL THEN 1 ELSE 0 END AS tabla_paises_ok,
    CASE WHEN @objIdDeptosFinal IS NOT NULL THEN 1 ELSE 0 END AS tabla_departamentos_ok,
    CASE WHEN @objIdCiudadesFinal IS NOT NULL THEN 1 ELSE 0 END AS tabla_ciudades_ok,
    (SELECT COUNT(*) FROM dbo.catalogo_paises) AS conteo_paises,
    (SELECT COUNT(*) FROM dbo.catalogo_departamentos) AS conteo_departamentos,
    (SELECT COUNT(*) FROM dbo.catalogo_ciudades) AS conteo_ciudades,
    (SELECT COUNT(*) FROM dbo.catalogo_ciudades WHERE tipo = 'MUNICIPIO') AS conteo_municipios,
    (SELECT COUNT(*) FROM dbo.catalogo_ciudades WHERE tipo = 'ISLA') AS conteo_islas,
    (SELECT COUNT(*) FROM dbo.catalogo_ciudades WHERE tipo = 'AREA_NO_MUNICIPALIZADA') AS conteo_areas_no_municipalizadas,
    (SELECT COUNT(*) FROM dbo.catalogo_ciudades c
        WHERE NOT EXISTS (SELECT 1 FROM dbo.catalogo_departamentos d WHERE d.id_departamento = c.id_departamento)
    ) AS ciudades_huerfanas,
    CASE WHEN
        @objIdPaisesFinal IS NOT NULL AND @objIdDeptosFinal IS NOT NULL AND @objIdCiudadesFinal IS NOT NULL
        AND (SELECT COUNT(*) FROM dbo.catalogo_paises) >= 1
        AND (SELECT COUNT(*) FROM dbo.catalogo_departamentos) = 33
        AND (SELECT COUNT(*) FROM dbo.catalogo_ciudades) = 1122
        AND (SELECT COUNT(*) FROM dbo.catalogo_ciudades WHERE tipo = 'MUNICIPIO') = 1103
        AND (SELECT COUNT(*) FROM dbo.catalogo_ciudades WHERE tipo = 'ISLA') = 1
        AND (SELECT COUNT(*) FROM dbo.catalogo_ciudades WHERE tipo = 'AREA_NO_MUNICIPALIZADA') = 18
    THEN N'OK — catálogo geográfico completo y verificado contra los conteos oficiales del DANE'
    ELSE N'REVISAR — algún conteo no coincide con el esperado; ver columnas anteriores'
    END AS resultado;

SELECT c.name AS columna, ty.name AS tipo, c.max_length, c.scale, c.is_nullable
FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE t.name = 'catalogo_paises' ORDER BY c.column_id;

SELECT c.name AS columna, ty.name AS tipo, c.max_length, c.scale, c.is_nullable
FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE t.name = 'catalogo_departamentos' ORDER BY c.column_id;

SELECT c.name AS columna, ty.name AS tipo, c.max_length, c.scale, c.is_nullable
FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE t.name = 'catalogo_ciudades' ORDER BY c.column_id;

PRINT '=== VERIFICACIÓN COMPLETA ===';
