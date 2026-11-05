-- operacion/crear_primer_admin.sql - XPAY PROD. Piloto, correccion 2a.
-- Crea el PRIMER administrador (SUPERUSUARIO + ADMIN_XPAY). UN SOLO USO:
-- aborta si ya existe cualquier usuario con SUPERUSUARIO o ADMIN_XPAY
-- activo. Requiere autorizacion expresa e individual para ejecutarse.
--
-- Variables sqlcmd (obligatorias; nunca se guardan en el repositorio):
--   ADMIN_USUARIO  nombre de usuario (minusculas, sin prefijo "qa.")
--   ADMIN_EMAIL    email de contacto
--   ADMIN_CELULAR  celular de contacto
--   ADMIN_HASH     hash BCrypt de tools/hash_clave (la clave no pasa por aqui)
--
-- Ejemplo (un -v por variable; comillas simples: el hash contiene "$").
-- Los valores no deben contener comillas simples:
--   sqlcmd ... -b -i database/prod/operacion/crear_primer_admin.sql \
--     -v ADMIN_USUARIO='...' -v ADMIN_EMAIL='...' -v ADMIN_CELULAR='...' -v ADMIN_HASH='...'
-- Probado solo en SQL Server 2022 local (base con 001-049 sin 008).
--
-- Resultado: usuario ACTIVO con requiere_cambio_clave = 1 (cambio
-- obligatorio en el primer ingreso), email_verificado = 0, sin wallet, sin
-- documento, y una fila de auditoria PRIMER_ADMIN_CREADO sin datos
-- personales ni hash. Todo en una transaccion: o se crea todo o nada.
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Obligatorias para insertar en tablas con indices filtrados
-- (personas: IX_personas_documento, UX_personas_documento_verificado).
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @usuario VARCHAR(100) = LOWER(LTRIM(RTRIM(N'$(ADMIN_USUARIO)')));
DECLARE @email   VARCHAR(200) = LOWER(LTRIM(RTRIM(N'$(ADMIN_EMAIL)')));
DECLARE @celular VARCHAR(30)  = LTRIM(RTRIM(N'$(ADMIN_CELULAR)'));
DECLARE @hash    VARCHAR(500) = N'$(ADMIN_HASH)';

IF LEN(@usuario) = 0 OR LEN(@usuario) > 100 OR @usuario LIKE 'qa.%'
    THROW 51001, 'ADMIN_USUARIO invalido.', 1;
IF LEN(@email) = 0 OR @email NOT LIKE '_%@_%._%'
    THROW 51002, 'ADMIN_EMAIL invalido.', 1;
IF LEN(@celular) = 0
    THROW 51003, 'ADMIN_CELULAR invalido.', 1;
-- BCrypt: $2a$/$2b$/$2x$/$2y$ + coste de dos digitos + 53 caracteres = 60.
IF LEN(@hash) <> 60 OR @hash NOT LIKE '$2[abxy]$[0-9][0-9]$%'
    THROW 51004, 'ADMIN_HASH no es un hash BCrypt valido.', 1;

BEGIN TRANSACTION;

-- Serializa ejecuciones simultaneas del script.
DECLARE @lock INT;
EXEC @lock = sp_getapplock @Resource = 'XPAY:PRIMER_ADMIN', @LockMode = 'Exclusive',
                           @LockOwner = 'Transaction', @LockTimeout = 5000;
IF @lock < 0
    THROW 51005, 'No se pudo obtener el bloqueo XPAY:PRIMER_ADMIN.', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.usuario_roles ur
    JOIN dbo.roles r ON r.id_rol = ur.id_rol
    WHERE r.codigo IN ('SUPERUSUARIO', 'ADMIN_XPAY') AND ur.estado = 'ACTIVO')
    THROW 51006, 'Ya existe un administrador: este script es de un solo uso.', 1;

IF EXISTS (SELECT 1 FROM dbo.usuarios WHERE usuario = @usuario)
    THROW 51007, 'El nombre de usuario ya existe.', 1;

DECLARE @idUnidad BIGINT = (SELECT id_unidad_negocio FROM dbo.unidades_negocio WHERE codigo = 'XPAY_COL' AND estado = 'ACTIVA');
DECLARE @idSuper  BIGINT = (SELECT id_rol FROM dbo.roles WHERE codigo = 'SUPERUSUARIO' AND estado = 'ACTIVO');
DECLARE @idAdmin  BIGINT = (SELECT id_rol FROM dbo.roles WHERE codigo = 'ADMIN_XPAY'   AND estado = 'ACTIVO');
IF @idUnidad IS NULL OR @idSuper IS NULL OR @idAdmin IS NULL
    THROW 51008, 'Falta la unidad XPAY_COL o los roles SUPERUSUARIO/ADMIN_XPAY.', 1;

INSERT INTO dbo.personas (id_unidad_negocio, celular, email, pais, estado)
VALUES (@idUnidad, @celular, @email, 'Colombia', 'ACTIVA');
DECLARE @idPersona BIGINT = SCOPE_IDENTITY();

INSERT INTO dbo.usuarios (id_persona, usuario, password_hash, email_verificado, requiere_cambio_clave, estado)
VALUES (@idPersona, @usuario, @hash, 0, 1, 'ACTIVO');
DECLARE @idUsuario BIGINT = SCOPE_IDENTITY();

INSERT INTO dbo.usuario_roles (id_usuario, id_rol, estado) VALUES (@idUsuario, @idSuper, 'ACTIVO');
INSERT INTO dbo.usuario_roles (id_usuario, id_rol, estado) VALUES (@idUsuario, @idAdmin, 'ACTIVO');

INSERT INTO dbo.auditoria (id_usuario, id_persona, modulo, accion, entidad, id_entidad, resultado, observacion)
VALUES (@idUsuario, @idPersona, 'USUARIOS', 'PRIMER_ADMIN_CREADO', 'usuarios', CAST(@idUsuario AS VARCHAR(100)),
        'EXITOSO', 'Primer administrador creado por script operativo; cambio de clave obligatorio en el primer ingreso.');

COMMIT TRANSACTION;

SELECT N'PRIMER_ADMIN_ID_USUARIO', @idUsuario;
