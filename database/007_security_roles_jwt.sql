/* XPAY MVP V1 - 007_security_roles_jwt.sql */
/* Fase 8: Roles operacionales para JWT y control de acceso */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Roles requeridos por Fase 8 que no estaban en el seed inicial
INSERT INTO roles (codigo, nombre, descripcion, tipo_rol)
VALUES
('ADMIN_XPAY',    'Administrador XPAY',  'Administración total del sistema XPAY',            'XPAY'),
('OPERADOR_XPAY', 'Operador XPAY',       'Operaciones, reportes y gestión de retiros XPAY',  'XPAY'),
('COMERCIO',      'Comercio',            'Acceso de comercio a la plataforma',               'COMERCIO');
GO
