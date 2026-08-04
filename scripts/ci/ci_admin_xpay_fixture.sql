/* ================================================================
   XPAY MVP — Fixture ADMIN_XPAY exclusivo de CI
   scripts/ci/ci_admin_xpay_fixture.sql

   !! USO EXCLUSIVO: pipeline de GitHub Actions (Backend Validation) !!
   !! NO EJECUTAR CONTRA QA NI PRODUCCION                            !!
   !! NO CONTIENE NI GENERA DATOS REALES — NO INVOLUCRA DINERO REAL  !!

   Objetivo: crear el UNICO usuario con roles ADMIN_XPAY y SUPERUSUARIO
   que necesita scripts/validate-backend.sh para ejercitar los endpoints
   administrativos restringidos a ADMIN_XPAY,SUPERUSUARIO (commit 13f4aa2)
   — incluye recarga-manual, liquidar-venta-qr, retiros, listados admin,
   y (Fase USUARIOS-ADMIN-2) el nuevo GET /api/admin/usuarios. Ambos
   roles sobre el MISMO usuario permiten probar en un solo token que el
   endpoint acepta cualquiera de los dos, sin crear un segundo fixture.
   No crea wallets, comercio ni QR — nada de eso es necesario aquí.

   Nombre de usuario y contraseña son EXCLUSIVOS de este fixture,
   distintos de cualquier usuario de QA real (qa.admin.xpay, etc.) o
   de los usuarios auto-registrados dinámicamente por el propio
   script (carlos_ci_test, maria_ci_test). El hash es un BCrypt
   ($2a$11$, 60 caracteres) real y verificable — no un placeholder.
   Contraseña en texto plano documentada aquí a propósito: este
   fixture solo existe en la base de datos efímera del contenedor
   SQL Server de GitHub Actions, destruida al final de cada
   ejecución — igual que SA_PASSWORD ya hardcodeado en el workflow.

   Prerrequisitos: 001_security_identity.sql (unidad de negocio
   XPAY_COL) y 007_security_roles_jwt.sql (rol ADMIN_XPAY) ya
   ejecutados — ambos ya forman parte del pipeline de CI.

   Idempotencia: cada bloque usa IF NOT EXISTS, igual que
   008_seed_qa_dataset.sql — el script puede ejecutarse más de una
   vez sin duplicar registros.
   ================================================================ */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT '--- Fixture CI: ci_admin_xpay (roles ADMIN_XPAY + SUPERUSUARIO) ---';
GO

IF NOT EXISTS (SELECT 1 FROM unidades_negocio WHERE codigo = 'XPAY_COL')
BEGIN
    RAISERROR ('ERROR: No se encontro XPAY_COL en unidades_negocio. Ejecutar 001 primero.', 16, 1);
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM roles WHERE codigo = 'ADMIN_XPAY')
BEGIN
    RAISERROR ('ERROR: Rol ADMIN_XPAY no encontrado. Ejecutar 007 primero.', 16, 1);
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM roles WHERE codigo = 'SUPERUSUARIO')
BEGIN
    RAISERROR ('ERROR: Rol SUPERUSUARIO no encontrado. Ejecutar 001 primero.', 16, 1);
    RETURN;
END
GO

DECLARE @idUnidad BIGINT;
SELECT @idUnidad = id_unidad_negocio FROM unidades_negocio WHERE codigo = 'XPAY_COL';

-- Persona fixture (documento claramente ficticio, fuera del rango
-- 900000001-900000004 usado por 008_seed_qa_dataset.sql, y distinto
-- de los documentos que genera dinámicamente validate-backend.sh).
IF NOT EXISTS (
    SELECT 1 FROM personas
    WHERE id_unidad_negocio = @idUnidad
      AND tipo_documento = 'CC'
      AND numero_documento = '999000001'
)
BEGIN
    INSERT INTO personas
        (id_unidad_negocio, tipo_documento, numero_documento,
         primer_nombre, primer_apellido, celular, email, estado)
    VALUES
        (@idUnidad, 'CC', '999000001',
         'CI Fixture', 'AdminXpay',
         '3000000099', 'ci.admin.xpay@ci-test.local', 'ACTIVA');
    PRINT '  Persona fixture CI Admin XPAY creada.';
END
ELSE PRINT '  Persona fixture CI Admin XPAY ya existe — omitida.';
GO

-- Usuario ci_admin_xpay — hash BCrypt real ($2a$11$, cost 11) de la
-- contraseña exclusiva de CI: CI-Fixture-AdminXpay#2026
-- (no reutilizada en QA/produccion; solo vive en esta base efimera).
IF NOT EXISTS (SELECT 1 FROM usuarios WHERE usuario = 'ci_admin_xpay')
BEGIN
    INSERT INTO usuarios (id_persona, usuario, password_hash, estado)
    SELECT p.id_persona, 'ci_admin_xpay',
           '$2a$11$CWOHImZ7H56k9CDDCJ4ZauB46d5QPpPmzJN4TUmk69trFrQGVlhEm',
           'ACTIVO'
    FROM   personas p
    WHERE  p.numero_documento = '999000001'
      AND  p.tipo_documento = 'CC';
    PRINT '  Usuario ci_admin_xpay creado.';
END
ELSE PRINT '  Usuario ci_admin_xpay ya existe — omitido.';
GO

-- Asignacion del rol ADMIN_XPAY (ya existente en fixtures previos —
-- se conserva sin cambios, NOT EXISTS evita duplicar si ya estaba).
INSERT INTO usuario_roles (id_usuario, id_rol)
SELECT u.id_usuario, r.id_rol
FROM   usuarios u
JOIN   roles r ON r.codigo = 'ADMIN_XPAY'
WHERE  u.usuario = 'ci_admin_xpay'
  AND  NOT EXISTS (
           SELECT 1 FROM usuario_roles ur
           WHERE  ur.id_usuario = u.id_usuario
             AND  ur.id_rol = r.id_rol
       );

PRINT '  Rol verificado: ci_admin_xpay -> ADMIN_XPAY.';
GO

-- Asignacion del rol SUPERUSUARIO (Fase USUARIOS-ADMIN-2) — segundo rol
-- activo sobre el MISMO usuario, sin tocar la asignacion de ADMIN_XPAY
-- ni la persona ni el password_hash. NOT EXISTS = idempotente.
INSERT INTO usuario_roles (id_usuario, id_rol)
SELECT u.id_usuario, r.id_rol
FROM   usuarios u
JOIN   roles r ON r.codigo = 'SUPERUSUARIO'
WHERE  u.usuario = 'ci_admin_xpay'
  AND  NOT EXISTS (
           SELECT 1 FROM usuario_roles ur
           WHERE  ur.id_usuario = u.id_usuario
             AND  ur.id_rol = r.id_rol
       );

PRINT '  Rol verificado: ci_admin_xpay -> SUPERUSUARIO.';
GO

PRINT '--- Fixture CI: ci_admin_xpay listo (ADMIN_XPAY + SUPERUSUARIO) ---';
GO
