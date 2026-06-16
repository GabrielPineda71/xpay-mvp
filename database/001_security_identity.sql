/* XPAY MVP V1 - 001_security_identity.sql */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE TABLE unidades_negocio (
    id_unidad_negocio BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    codigo VARCHAR(50) NOT NULL,
    nombre VARCHAR(200) NOT NULL,
    descripcion VARCHAR(500) NULL,
    estado VARCHAR(30) NOT NULL CONSTRAINT DF_unidades_negocio_estado DEFAULT 'ACTIVA',
    fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_unidades_negocio_fecha_creacion DEFAULT SYSDATETIME(),
    fecha_actualizacion DATETIME2 NULL
);
GO
CREATE UNIQUE INDEX IX_unidades_negocio_codigo ON unidades_negocio(codigo);
GO
INSERT INTO unidades_negocio (codigo,nombre,descripcion) VALUES ('XPAY_COL','XPAY Colombia','Unidad principal de operación XPAY en Colombia');
GO
CREATE TABLE personas (
    id_persona BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    id_unidad_negocio BIGINT NOT NULL,
    tipo_documento VARCHAR(20) NOT NULL,
    numero_documento VARCHAR(30) NOT NULL,
    primer_nombre VARCHAR(100) NOT NULL,
    segundo_nombre VARCHAR(100) NULL,
    primer_apellido VARCHAR(100) NOT NULL,
    segundo_apellido VARCHAR(100) NULL,
    fecha_nacimiento DATE NULL,
    celular VARCHAR(30) NOT NULL,
    email VARCHAR(200) NULL,
    direccion VARCHAR(300) NULL,
    ciudad VARCHAR(100) NULL,
    departamento VARCHAR(100) NULL,
    pais VARCHAR(100) NOT NULL CONSTRAINT DF_personas_pais DEFAULT 'Colombia',
    estado VARCHAR(30) NOT NULL CONSTRAINT DF_personas_estado DEFAULT 'ACTIVA',
    fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_personas_fecha_creacion DEFAULT SYSDATETIME(),
    fecha_actualizacion DATETIME2 NULL,
    CONSTRAINT FK_personas_unidades_negocio FOREIGN KEY (id_unidad_negocio) REFERENCES unidades_negocio(id_unidad_negocio)
);
GO
CREATE UNIQUE INDEX IX_personas_documento ON personas(id_unidad_negocio, tipo_documento, numero_documento);
GO
CREATE INDEX IX_personas_celular ON personas(celular);
GO
CREATE INDEX IX_personas_email ON personas(email);
GO
CREATE TABLE usuarios (
    id_usuario BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    id_persona BIGINT NOT NULL,
    usuario VARCHAR(100) NOT NULL,
    password_hash VARCHAR(500) NOT NULL,
    email_verificado BIT NOT NULL CONSTRAINT DF_usuarios_email_verificado DEFAULT 0,
    celular_verificado BIT NOT NULL CONSTRAINT DF_usuarios_celular_verificado DEFAULT 0,
    requiere_cambio_clave BIT NOT NULL CONSTRAINT DF_usuarios_requiere_cambio DEFAULT 0,
    intentos_fallidos INT NOT NULL CONSTRAINT DF_usuarios_intentos DEFAULT 0,
    ultimo_ingreso DATETIME2 NULL,
    fecha_bloqueo DATETIME2 NULL,
    motivo_bloqueo VARCHAR(500) NULL,
    estado VARCHAR(30) NOT NULL CONSTRAINT DF_usuarios_estado DEFAULT 'ACTIVO',
    fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_usuarios_fecha_creacion DEFAULT SYSDATETIME(),
    fecha_actualizacion DATETIME2 NULL,
    CONSTRAINT FK_usuarios_personas FOREIGN KEY (id_persona) REFERENCES personas(id_persona)
);
GO
CREATE UNIQUE INDEX IX_usuarios_usuario ON usuarios(usuario);
GO
CREATE INDEX IX_usuarios_persona ON usuarios(id_persona);
GO
CREATE TABLE roles (
    id_rol BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    codigo VARCHAR(80) NOT NULL,
    nombre VARCHAR(150) NOT NULL,
    descripcion VARCHAR(500) NULL,
    tipo_rol VARCHAR(50) NOT NULL,
    estado VARCHAR(30) NOT NULL CONSTRAINT DF_roles_estado DEFAULT 'ACTIVO',
    fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_roles_fecha_creacion DEFAULT SYSDATETIME()
);
GO
CREATE UNIQUE INDEX IX_roles_codigo ON roles(codigo);
GO
INSERT INTO roles (codigo,nombre,descripcion,tipo_rol) VALUES
('SUPERUSUARIO','Superusuario XPAY','Acceso total a la plataforma','XPAY'),('GERENTE_XPAY','Gerente XPAY','Gestión general de la operación','XPAY'),('COMERCIAL_XPAY','Comercial XPAY','Gestión de empresas y comercios','XPAY'),('CARTERA_XPAY','Cartera XPAY','Gestión de cartera y pagos','XPAY'),('TESORERIA_XPAY','Tesorería XPAY','Gestión de liquidez, fondos y conciliaciones','XPAY'),('SOPORTE_XPAY','Soporte XPAY','Atención y soporte operativo','XPAY'),('GERENTE_EMPRESA','Gerente Empresa','Autoriza acciones de empresa convenio','EMPRESA'),('AUXILIAR_NOMINA','Auxiliar de Nómina','Carga empleados y archivos de nómina','EMPRESA'),('PAGADOR_EMPRESA','Pagador Empresa','Gestiona pagos de empresa a XPAY','EMPRESA'),('CONSULTA_EMPRESA','Consulta Empresa','Consulta información de empresa','EMPRESA'),('GERENTE_COMERCIO','Gerente Comercio','Administra comercio completo','COMERCIO'),('ADMIN_TIENDA','Administrador de Tienda','Administra una tienda del comercio','COMERCIO'),('CAJERO','Cajero','Genera QR dinámicos y consulta ventas propias','COMERCIO'),('CONSULTA_COMERCIO','Consulta Comercio','Consulta información del comercio','COMERCIO'),('USUARIO_FINAL','Usuario Final','Usuario final de wallet XPAY','USUARIO_FINAL');
GO
CREATE TABLE permisos (id_permiso BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY, modulo VARCHAR(100) NOT NULL, codigo VARCHAR(120) NOT NULL, descripcion VARCHAR(500) NULL, estado VARCHAR(30) NOT NULL CONSTRAINT DF_permisos_estado DEFAULT 'ACTIVO', fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_permisos_fecha_creacion DEFAULT SYSDATETIME());
GO
CREATE UNIQUE INDEX IX_permisos_codigo ON permisos(codigo);
GO
INSERT INTO permisos (modulo,codigo,descripcion) VALUES
('ADMIN','ADMIN_DASHBOARD_VER','Ver dashboard administrativo'),('PERSONAS','PERSONAS_CREAR','Crear personas'),('PERSONAS','PERSONAS_VER','Consultar personas'),('PERSONAS','PERSONAS_EDITAR','Editar personas'),('PERSONAS','PERSONAS_BLOQUEAR','Bloquear personas'),('USUARIOS','USUARIOS_CREAR','Crear usuarios'),('USUARIOS','USUARIOS_VER','Consultar usuarios'),('USUARIOS','USUARIOS_EDITAR','Editar usuarios'),('USUARIOS','USUARIOS_BLOQUEAR','Bloquear usuarios'),('ROLES','ROLES_VER','Consultar roles'),('ROLES','ROLES_ASIGNAR','Asignar roles'),('ROLES','PERMISOS_VER','Consultar permisos'),('WALLET','WALLET_VER','Consultar wallets'),('WALLET','WALLET_MOVIMIENTOS_VER','Consultar movimientos wallet'),('COMERCIOS','COMERCIOS_CREAR','Crear comercios'),('COMERCIOS','COMERCIOS_VER','Consultar comercios'),('COMERCIOS','COMERCIOS_EDITAR','Editar comercios'),('COMERCIOS','COMERCIOS_CONDICIONES_EDITAR','Editar condiciones comerciales'),('EMPRESAS','EMPRESAS_CREAR','Crear empresas'),('EMPRESAS','EMPRESAS_VER','Consultar empresas'),('EMPRESAS','EMPRESAS_EDITAR','Editar empresas'),('CARTERA','CARTERA_VER','Consultar cartera'),('CARTERA','PAGOS_CREAR','Registrar pagos'),('CARTERA','PAGOS_ANULAR','Anular pagos'),('TESORERIA','TESORERIA_VER','Consultar tesorería'),('TESORERIA','LIQUIDEZ_VER','Consultar liquidez'),('TESORERIA','CONCILIACION_VER','Consultar conciliaciones');
GO
CREATE TABLE usuario_roles (id_usuario BIGINT NOT NULL, id_rol BIGINT NOT NULL, fecha_asignacion DATETIME2 NOT NULL CONSTRAINT DF_usuario_roles_fecha DEFAULT SYSDATETIME(), asignado_por BIGINT NULL, estado VARCHAR(30) NOT NULL CONSTRAINT DF_usuario_roles_estado DEFAULT 'ACTIVO', CONSTRAINT PK_usuario_roles PRIMARY KEY (id_usuario,id_rol), CONSTRAINT FK_usuario_roles_usuario FOREIGN KEY (id_usuario) REFERENCES usuarios(id_usuario), CONSTRAINT FK_usuario_roles_rol FOREIGN KEY (id_rol) REFERENCES roles(id_rol), CONSTRAINT FK_usuario_roles_asignado_por FOREIGN KEY (asignado_por) REFERENCES usuarios(id_usuario));
GO
CREATE TABLE rol_permisos (id_rol BIGINT NOT NULL, id_permiso BIGINT NOT NULL, fecha_asignacion DATETIME2 NOT NULL CONSTRAINT DF_rol_permisos_fecha DEFAULT SYSDATETIME(), CONSTRAINT PK_rol_permisos PRIMARY KEY (id_rol,id_permiso), CONSTRAINT FK_rol_permisos_rol FOREIGN KEY (id_rol) REFERENCES roles(id_rol), CONSTRAINT FK_rol_permisos_permiso FOREIGN KEY (id_permiso) REFERENCES permisos(id_permiso));
GO
CREATE TABLE dispositivos_usuario (id_dispositivo BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY, id_usuario BIGINT NOT NULL, identificador_dispositivo VARCHAR(300) NOT NULL, nombre_dispositivo VARCHAR(200) NULL, sistema_operativo VARCHAR(100) NULL, version_app VARCHAR(50) NULL, ip_ultimo_ingreso VARCHAR(100) NULL, ubicacion_ultima VARCHAR(300) NULL, fecha_registro DATETIME2 NOT NULL CONSTRAINT DF_dispositivos_fecha DEFAULT SYSDATETIME(), fecha_ultimo_ingreso DATETIME2 NULL, confiable BIT NOT NULL CONSTRAINT DF_dispositivos_confiable DEFAULT 0, estado VARCHAR(30) NOT NULL CONSTRAINT DF_dispositivos_estado DEFAULT 'ACTIVO', CONSTRAINT FK_dispositivos_usuario FOREIGN KEY (id_usuario) REFERENCES usuarios(id_usuario));
GO
CREATE INDEX IX_dispositivos_usuario_usuario ON dispositivos_usuario(id_usuario);
GO
CREATE INDEX IX_dispositivos_usuario_identificador ON dispositivos_usuario(identificador_dispositivo);
GO
CREATE TABLE pin_transaccional (id_pin BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY, id_usuario BIGINT NOT NULL, pin_hash VARCHAR(500) NOT NULL, intentos_fallidos INT NOT NULL CONSTRAINT DF_pin_intentos DEFAULT 0, bloqueado BIT NOT NULL CONSTRAINT DF_pin_bloqueado DEFAULT 0, fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_pin_fecha_creacion DEFAULT SYSDATETIME(), fecha_actualizacion DATETIME2 NULL, fecha_bloqueo DATETIME2 NULL, estado VARCHAR(30) NOT NULL CONSTRAINT DF_pin_estado DEFAULT 'ACTIVO', CONSTRAINT FK_pin_transaccional_usuario FOREIGN KEY (id_usuario) REFERENCES usuarios(id_usuario));
GO
CREATE UNIQUE INDEX IX_pin_transaccional_usuario ON pin_transaccional(id_usuario);
GO
CREATE TABLE auditoria (id_auditoria BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY, id_usuario BIGINT NULL, id_persona BIGINT NULL, modulo VARCHAR(100) NOT NULL, accion VARCHAR(100) NOT NULL, entidad VARCHAR(100) NULL, id_entidad VARCHAR(100) NULL, valor_anterior NVARCHAR(MAX) NULL, valor_nuevo NVARCHAR(MAX) NULL, ip VARCHAR(100) NULL, dispositivo VARCHAR(300) NULL, resultado VARCHAR(50) NOT NULL CONSTRAINT DF_auditoria_resultado DEFAULT 'EXITOSO', observacion VARCHAR(1000) NULL, fecha_evento DATETIME2 NOT NULL CONSTRAINT DF_auditoria_fecha DEFAULT SYSDATETIME(), CONSTRAINT FK_auditoria_usuario FOREIGN KEY (id_usuario) REFERENCES usuarios(id_usuario), CONSTRAINT FK_auditoria_persona FOREIGN KEY (id_persona) REFERENCES personas(id_persona));
GO
CREATE INDEX IX_auditoria_usuario ON auditoria(id_usuario);
GO
CREATE INDEX IX_auditoria_persona ON auditoria(id_persona);
GO
CREATE INDEX IX_auditoria_modulo_accion ON auditoria(modulo,accion);
GO
CREATE INDEX IX_auditoria_fecha ON auditoria(fecha_evento);
GO
CREATE INDEX IX_auditoria_entidad ON auditoria(entidad,id_entidad);
GO
