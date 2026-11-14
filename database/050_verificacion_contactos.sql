-- XPAY - Migracion 050: verificacion de contactos y consentimientos de comunicacion.
-- contacto_codigos_verificacion: codigos de un solo uso para verificar el
--   correo (EMAIL) o el celular (WHATSAPP). Solo se guarda SHA-256(sal || codigo);
--   nunca el codigo en claro. destino guarda el contacto al que se envio el
--   codigo: si el contacto cambia, el codigo deja de servir.
-- usuario_consentimientos_comunicacion: historial (solo insercion) de
--   autorizaciones por canal y finalidad (SERVICIO / PUBLICIDAD). Es
--   independiente de la verificacion: verificar un contacto no otorga ningun
--   consentimiento y retirar un consentimiento no revoca la verificacion.
-- Idempotente: crea solo lo que falta y verifica lo existente; ante cualquier
-- incompatibilidad aborta con THROW (no corrige nada). No modifica 001-049.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

PRINT '=== INICIO MIGRACION 050 ===';
IF OBJECT_ID('dbo.usuarios', 'U') IS NULL
    THROW 59500, N'Migracion 050 abortada: falta dbo.usuarios.', 1;
GO

IF OBJECT_ID('dbo.contacto_codigos_verificacion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.contacto_codigos_verificacion (
        id_codigo BIGINT IDENTITY(1,1) NOT NULL,
        id_usuario BIGINT NOT NULL,
        canal VARCHAR(20) NOT NULL,
        destino VARCHAR(200) NOT NULL,
        codigo_sal BINARY(16) NOT NULL,
        codigo_hash BINARY(32) NOT NULL,
        intentos_fallidos INT NOT NULL
            CONSTRAINT df_ccv_intentos DEFAULT 0,
        fecha_creacion DATETIME2 NOT NULL,
        fecha_expiracion DATETIME2 NOT NULL,
        fecha_uso DATETIME2 NULL,
        fecha_invalidacion DATETIME2 NULL,
        motivo_invalidacion VARCHAR(30) NULL,
        ip_solicitud VARCHAR(100) NULL,
        CONSTRAINT pk_contacto_codigos_verificacion PRIMARY KEY (id_codigo),
        CONSTRAINT fk_ccv_usuario FOREIGN KEY (id_usuario)
            REFERENCES dbo.usuarios(id_usuario),
        CONSTRAINT ck_ccv_canal CHECK (canal IN ('EMAIL', 'WHATSAPP')),
        CONSTRAINT ck_ccv_intentos CHECK (intentos_fallidos >= 0),
        CONSTRAINT ck_ccv_expiracion CHECK (fecha_expiracion > fecha_creacion)
    );
    PRINT 'contacto_codigos_verificacion creada';
END
ELSE
    PRINT 'contacto_codigos_verificacion ya existe';
GO

IF (SELECT COUNT(*) FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.contacto_codigos_verificacion')) <> 13
    THROW 59501, N'Migracion 050 abortada: contacto_codigos_verificacion no tiene exactamente 13 columnas.', 1;
IF (SELECT COUNT(*)
    FROM (VALUES
        ('id_codigo',           'bigint',    8,   0),
        ('id_usuario',          'bigint',    8,   0),
        ('canal',               'varchar',   20,  0),
        ('destino',             'varchar',   200, 0),
        ('codigo_sal',          'binary',    16,  0),
        ('codigo_hash',         'binary',    32,  0),
        ('intentos_fallidos',   'int',       4,   0),
        ('fecha_creacion',      'datetime2', 8,   0),
        ('fecha_expiracion',    'datetime2', 8,   0),
        ('fecha_uso',           'datetime2', 8,   1),
        ('fecha_invalidacion',  'datetime2', 8,   1),
        ('motivo_invalidacion', 'varchar',   30,  1),
        ('ip_solicitud',        'varchar',   100, 1)
    ) AS e(nombre, tipo, longitud, nulos)
    JOIN sys.columns c
      ON c.object_id = OBJECT_ID('dbo.contacto_codigos_verificacion')
     AND c.name = e.nombre
    JOIN sys.types t
      ON t.user_type_id = c.user_type_id
    WHERE t.name = e.tipo
      AND c.max_length = e.longitud
      AND c.is_nullable = e.nulos) <> 13
    THROW 59501, N'Migracion 050 abortada: columnas de contacto_codigos_verificacion incompatibles.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys fk
    WHERE fk.name = 'fk_ccv_usuario'
      AND fk.parent_object_id = OBJECT_ID('dbo.contacto_codigos_verificacion')
      AND fk.referenced_object_id = OBJECT_ID('dbo.usuarios')
      AND fk.is_disabled = 0)
    THROW 59501, N'Migracion 050 abortada: falta fk_ccv_usuario.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints cc
    WHERE cc.name = 'ck_ccv_canal'
      AND cc.parent_object_id = OBJECT_ID('dbo.contacto_codigos_verificacion')
      AND cc.is_disabled = 0
      AND CHARINDEX(N'''EMAIL''', cc.definition) > 0
      AND CHARINDEX(N'''WHATSAPP''', cc.definition) > 0)
    THROW 59501, N'Migracion 050 abortada: ck_ccv_canal ausente o incompatible.', 1;
GO

-- Codigos de un usuario y canal (vigente, enfriamiento y cupo diario).
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'ix_ccv_usuario_canal'
      AND object_id = OBJECT_ID('dbo.contacto_codigos_verificacion'))
    CREATE INDEX ix_ccv_usuario_canal
        ON dbo.contacto_codigos_verificacion(id_usuario, canal, fecha_creacion)
        INCLUDE (fecha_uso, fecha_invalidacion, fecha_expiracion);
GO

-- Cupo diario por destino (evita usar varias cuentas para inundar un contacto).
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'ix_ccv_canal_destino'
      AND object_id = OBJECT_ID('dbo.contacto_codigos_verificacion'))
    CREATE INDEX ix_ccv_canal_destino
        ON dbo.contacto_codigos_verificacion(canal, destino, fecha_creacion);
GO

IF OBJECT_ID('dbo.usuario_consentimientos_comunicacion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.usuario_consentimientos_comunicacion (
        id_consentimiento BIGINT IDENTITY(1,1) NOT NULL,
        id_usuario BIGINT NOT NULL,
        canal VARCHAR(20) NOT NULL,
        finalidad VARCHAR(20) NOT NULL,
        otorgado BIT NOT NULL,
        origen VARCHAR(40) NOT NULL,
        fecha_registro DATETIME2 NOT NULL,
        ip_solicitud VARCHAR(100) NULL,
        CONSTRAINT pk_usuario_consentimientos_comunicacion PRIMARY KEY (id_consentimiento),
        CONSTRAINT fk_ucc_usuario FOREIGN KEY (id_usuario)
            REFERENCES dbo.usuarios(id_usuario),
        CONSTRAINT ck_ucc_canal CHECK (canal IN ('EMAIL', 'WHATSAPP')),
        CONSTRAINT ck_ucc_finalidad CHECK (finalidad IN ('SERVICIO', 'PUBLICIDAD'))
    );
    PRINT 'usuario_consentimientos_comunicacion creada';
END
ELSE
    PRINT 'usuario_consentimientos_comunicacion ya existe';
GO

IF (SELECT COUNT(*) FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.usuario_consentimientos_comunicacion')) <> 8
    THROW 59502, N'Migracion 050 abortada: usuario_consentimientos_comunicacion no tiene exactamente 8 columnas.', 1;
IF (SELECT COUNT(*)
    FROM (VALUES
        ('id_consentimiento', 'bigint',    8,   0),
        ('id_usuario',        'bigint',    8,   0),
        ('canal',             'varchar',   20,  0),
        ('finalidad',         'varchar',   20,  0),
        ('otorgado',          'bit',       1,   0),
        ('origen',            'varchar',   40,  0),
        ('fecha_registro',    'datetime2', 8,   0),
        ('ip_solicitud',      'varchar',   100, 1)
    ) AS e(nombre, tipo, longitud, nulos)
    JOIN sys.columns c
      ON c.object_id = OBJECT_ID('dbo.usuario_consentimientos_comunicacion')
     AND c.name = e.nombre
    JOIN sys.types t
      ON t.user_type_id = c.user_type_id
    WHERE t.name = e.tipo
      AND c.max_length = e.longitud
      AND c.is_nullable = e.nulos) <> 8
    THROW 59502, N'Migracion 050 abortada: columnas de usuario_consentimientos_comunicacion incompatibles.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints cc
    WHERE cc.name = 'ck_ucc_finalidad'
      AND cc.parent_object_id = OBJECT_ID('dbo.usuario_consentimientos_comunicacion')
      AND cc.is_disabled = 0
      AND CHARINDEX(N'''SERVICIO''', cc.definition) > 0
      AND CHARINDEX(N'''PUBLICIDAD''', cc.definition) > 0)
    THROW 59502, N'Migracion 050 abortada: ck_ucc_finalidad ausente o incompatible.', 1;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'ix_ucc_usuario_canal_finalidad'
      AND object_id = OBJECT_ID('dbo.usuario_consentimientos_comunicacion'))
    CREATE INDEX ix_ucc_usuario_canal_finalidad
        ON dbo.usuario_consentimientos_comunicacion(id_usuario, canal, finalidad, id_consentimiento)
        INCLUDE (otorgado);
GO

PRINT '=== FIN MIGRACION 050 ===';
GO
