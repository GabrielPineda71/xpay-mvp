/* ================================================================
   XPAY MVP — Fixture Comercio/Caja exclusivo de CI (Fase 70.4-C)
   scripts/ci/ci_comercio_caja_fixture.sql

   !! USO EXCLUSIVO: pipeline de GitHub Actions (Backend Validation) !!
   !! NO EJECUTAR CONTRA QA NI PRODUCCION                            !!
   !! NO CONTIENE NI GENERA DATOS REALES — NO INVOLUCRA DINERO REAL  !!

   Objetivo: hasta la Fase 70.4-C, scripts/validate-backend.sh nunca
   ejercitó el dominio comercios_aliados/comercio_usuarios (ADMIN_COMERCIO/
   ADMIN_SEDE_COMERCIO/CAJERO) — cero fixtures, cero llamadas a ningún
   endpoint bajo /api/comercio/. Este fixture crea, por primera vez, un
   comercio aliado con 4 usuarios operativos (uno por escenario de prueba)
   para poder
   ejercitar WalletRecargaComercioService/WalletCajaComercioService end to
   end en CI.

   Reutiliza el id_comercio (tabla "comercios", dominio QR histórico) que
   scripts/validate-backend.sh ya captura en su FASE 4 como $ID_COMERCIO —
   se referencia aquí vía la variable de scripting sqlcmd $(ID_COMERCIO)
   (pasada con -v ID_COMERCIO="$ID_COMERCIO"), nunca hardcodeado.

   Mismo criterio de aislamiento que ci_admin_xpay_fixture.sql: documentos,
   usuarios y contraseñas exclusivos de CI (rango 999000010-999000013,
   distinto de 999000001-999000002 ya usados por ci_admin_xpay/ci_admin_guard
   y de 900000001-900000004 de 008_seed_qa_dataset.sql), hashes BCrypt
   reales y verificados, nunca reutilizados de QA/producción.

   Idempotencia: cada bloque usa IF NOT EXISTS, mismo criterio que
   ci_admin_xpay_fixture.sql — puede ejecutarse más de una vez sin duplicar.

   hora_cierre_automatico_caja se fija explícitamente en 23:59 — nunca se
   deja NULL (que caería al default 21:00 de la aplicación) para que la
   prueba nunca falle por "hora límite de cierre" según la hora real a la
   que corra el pipeline de CI.
   ================================================================ */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- $(ID_COMERCIO) es una variable de scripting de sqlcmd — se pasa desde
-- scripts/validate-backend.sh con: sqlcmd -v ID_COMERCIO="$ID_COMERCIO" ...
-- Nunca se hardcodea aquí ni se concatena como texto (evita inyección).

PRINT '--- Fixture CI: comercio aliado + caja (Fase 70.4-C) ---';
GO

IF NOT EXISTS (SELECT 1 FROM unidades_negocio WHERE codigo = 'XPAY_COL')
BEGIN
    RAISERROR ('ERROR: No se encontro XPAY_COL en unidades_negocio. Ejecutar 001 primero.', 16, 1);
    RETURN;
END
GO

DECLARE @idUnidad BIGINT = (SELECT id_unidad_negocio FROM unidades_negocio WHERE codigo = 'XPAY_COL');

-- ── Personas fixture (4, una por rol/escenario) ──────────────────────────
IF NOT EXISTS (SELECT 1 FROM personas WHERE tipo_documento='CC' AND numero_documento = '999000010')
    INSERT INTO personas (id_unidad_negocio, tipo_documento, numero_documento, primer_nombre, primer_apellido, celular, email, estado)
    VALUES (@idUnidad, 'CC', '999000010', 'CI Fixture', 'CajeroComercio', '3000000010', 'ci.cajero.comercio@ci-test.local', 'ACTIVA');

IF NOT EXISTS (SELECT 1 FROM personas WHERE tipo_documento='CC' AND numero_documento = '999000011')
    INSERT INTO personas (id_unidad_negocio, tipo_documento, numero_documento, primer_nombre, primer_apellido, celular, email, estado)
    VALUES (@idUnidad, 'CC', '999000011', 'CI Fixture', 'AdminSedeComercio', '3000000011', 'ci.adminsede.comercio@ci-test.local', 'ACTIVA');

IF NOT EXISTS (SELECT 1 FROM personas WHERE tipo_documento='CC' AND numero_documento = '999000012')
    INSERT INTO personas (id_unidad_negocio, tipo_documento, numero_documento, primer_nombre, primer_apellido, celular, email, estado)
    VALUES (@idUnidad, 'CC', '999000012', 'CI Fixture', 'AdminComercio', '3000000012', 'ci.admincomercio.comercio@ci-test.local', 'ACTIVA');

IF NOT EXISTS (SELECT 1 FROM personas WHERE tipo_documento='CC' AND numero_documento = '999000013')
    INSERT INTO personas (id_unidad_negocio, tipo_documento, numero_documento, primer_nombre, primer_apellido, celular, email, estado)
    VALUES (@idUnidad, 'CC', '999000013', 'CI Fixture', 'CajeroAmbiguo', '3000000013', 'ci.cajero.ambiguo@ci-test.local', 'ACTIVA');

-- Mejora operativa pre-lanzamiento (apertura sin restricción de hora / vencimiento
-- por fecha_operativa) — 2 personas dedicadas, mismo criterio de aislamiento.
IF NOT EXISTS (SELECT 1 FROM personas WHERE tipo_documento='CC' AND numero_documento = '999000014')
    INSERT INTO personas (id_unidad_negocio, tipo_documento, numero_documento, primer_nombre, primer_apellido, celular, email, estado)
    VALUES (@idUnidad, 'CC', '999000014', 'CI Fixture', 'CajeroVencimiento', '3000000014', 'ci.cajero.vencimiento@ci-test.local', 'ACTIVA');

IF NOT EXISTS (SELECT 1 FROM personas WHERE tipo_documento='CC' AND numero_documento = '999000015')
    INSERT INTO personas (id_unidad_negocio, tipo_documento, numero_documento, primer_nombre, primer_apellido, celular, email, estado)
    VALUES (@idUnidad, 'CC', '999000015', 'CI Fixture', 'CajeroVigente', '3000000015', 'ci.cajero.vigente@ci-test.local', 'ACTIVA');
GO

-- ── Usuarios fixture (contraseñas exclusivas de CI, hash BCrypt $2a$11$ real) ──
IF NOT EXISTS (SELECT 1 FROM usuarios WHERE usuario = 'ci_cajero_comercio')
    INSERT INTO usuarios (id_persona, usuario, password_hash, estado)
    SELECT p.id_persona, 'ci_cajero_comercio', '$2a$11$JfjNtqKT2OAJihWdLayRAOsGQbTeCQQCs0JPwjHA4U.Gji740lBdO', 'ACTIVO'
    FROM personas p WHERE p.tipo_documento='CC' AND p.numero_documento = '999000010';

IF NOT EXISTS (SELECT 1 FROM usuarios WHERE usuario = 'ci_admin_sede_comercio')
    INSERT INTO usuarios (id_persona, usuario, password_hash, estado)
    SELECT p.id_persona, 'ci_admin_sede_comercio', '$2a$11$aNj9xsZkLLL3le7oGyH5X.QhpFMCa/WSI41OCUWbUaN.wErEDnsN2', 'ACTIVO'
    FROM personas p WHERE p.tipo_documento='CC' AND p.numero_documento = '999000011';

IF NOT EXISTS (SELECT 1 FROM usuarios WHERE usuario = 'ci_admin_comercio')
    INSERT INTO usuarios (id_persona, usuario, password_hash, estado)
    SELECT p.id_persona, 'ci_admin_comercio', '$2a$11$w1.NoghduCRuNLDbmFH.nOyDtJGmG0GjOZI.Vx9bURTKa3M9XWIWu', 'ACTIVO'
    FROM personas p WHERE p.tipo_documento='CC' AND p.numero_documento = '999000012';

IF NOT EXISTS (SELECT 1 FROM usuarios WHERE usuario = 'ci_cajero_ambiguo')
    INSERT INTO usuarios (id_persona, usuario, password_hash, estado)
    SELECT p.id_persona, 'ci_cajero_ambiguo', '$2a$11$KqsOVCQN3mVet9MChHSfhuNlMvfFn6jHH5lC1vr8HnFD8PT6luKF.', 'ACTIVO'
    FROM personas p WHERE p.tipo_documento='CC' AND p.numero_documento = '999000013';

IF NOT EXISTS (SELECT 1 FROM usuarios WHERE usuario = 'ci_cajero_vencimiento')
    INSERT INTO usuarios (id_persona, usuario, password_hash, estado)
    SELECT p.id_persona, 'ci_cajero_vencimiento', '$2a$11$oZqaK.qvtwnx58rqLwm26e5UXNf0NZHanGnuN6Do1r4lHY3KMPv3K', 'ACTIVO'
    FROM personas p WHERE p.tipo_documento='CC' AND p.numero_documento = '999000014';

IF NOT EXISTS (SELECT 1 FROM usuarios WHERE usuario = 'ci_cajero_vigente')
    INSERT INTO usuarios (id_persona, usuario, password_hash, estado)
    SELECT p.id_persona, 'ci_cajero_vigente', '$2a$11$3Auw4dUtfPN.T2lXkFg1.O5jDnoyGvXHM.gsuylH3tEihTzk/e4ke', 'ACTIVO'
    FROM personas p WHERE p.tipo_documento='CC' AND p.numero_documento = '999000015';
GO

-- ── Rol JWT "COMERCIO" (gate de [Authorize(Roles="COMERCIO")]) para los 6 ──
INSERT INTO usuario_roles (id_usuario, id_rol)
SELECT u.id_usuario, r.id_rol
FROM usuarios u
JOIN roles r ON r.codigo = 'COMERCIO'
WHERE u.usuario IN ('ci_cajero_comercio','ci_admin_sede_comercio','ci_admin_comercio','ci_cajero_ambiguo','ci_cajero_vencimiento','ci_cajero_vigente')
  AND NOT EXISTS (SELECT 1 FROM usuario_roles ur WHERE ur.id_usuario = u.id_usuario AND ur.id_rol = r.id_rol);
GO

-- ── Comercio aliado, vinculado al comercio existente ya usado por el ────
-- dominio QR histórico (mismo id_comercio que liquidar-venta-qr) ────────
-- @idComercioExistente se redeclara aquí (no sobrevive el GO anterior —
-- las variables T-SQL no cruzan límites de batch; $(ID_COMERCIO), al ser
-- una variable de scripting de sqlcmd, sí persiste y se reevalúa igual).
DECLARE @idComercioExistente BIGINT = $(ID_COMERCIO);

IF NOT EXISTS (SELECT 1 FROM comercios_aliados WHERE nit = '900999001-1')
    INSERT INTO comercios_aliados
        (id_comercio_existente, razon_social, nombre_comercial, nit, tipo_persona, estado)
    VALUES
        (@idComercioExistente, 'CI Fixture Comercio Aliado SAS', 'CI Fixture Comercio', '900999001-1', 'JURIDICA', 'ACTIVO');
GO

DECLARE @idComercioAliado BIGINT = (SELECT id_comercio_aliado FROM comercios_aliados WHERE nit = '900999001-1');

-- ── Establecimiento — hora_cierre_automatico_caja fija en 23:59 para que ──
-- la prueba nunca dependa de a qué hora del día corre el pipeline de CI. ──
IF NOT EXISTS (SELECT 1 FROM comercio_establecimientos WHERE id_comercio_aliado = @idComercioAliado AND nombre_establecimiento = 'CI Fixture Sede Principal')
    INSERT INTO comercio_establecimientos
        (id_comercio_aliado, nombre_establecimiento, estado, hora_cierre_automatico_caja)
    VALUES
        (@idComercioAliado, 'CI Fixture Sede Principal', 'ACTIVO', '23:59:00');

-- Mejora operativa pre-lanzamiento: sede dedicada SIN override de hora
-- (NULL = cae al default del sistema, hoy 21:00, ya sin efecto sobre la
-- decisión de negocio) — usada por ci_cajero_vencimiento/ci_cajero_vigente
-- para probar que abrir ya no depende de la hora, sea cual sea la hora real
-- a la que corra el pipeline.
IF NOT EXISTS (SELECT 1 FROM comercio_establecimientos WHERE id_comercio_aliado = @idComercioAliado AND nombre_establecimiento = 'CI Fixture Sede Sin Override Hora')
    INSERT INTO comercio_establecimientos
        (id_comercio_aliado, nombre_establecimiento, estado, hora_cierre_automatico_caja)
    VALUES
        (@idComercioAliado, 'CI Fixture Sede Sin Override Hora', 'ACTIVO', NULL);
GO

DECLARE @idComercioAliado2 BIGINT = (SELECT id_comercio_aliado FROM comercios_aliados WHERE nit = '900999001-1');
DECLARE @idEstablecimiento BIGINT = (SELECT id_establecimiento FROM comercio_establecimientos WHERE id_comercio_aliado = @idComercioAliado2 AND nombre_establecimiento = 'CI Fixture Sede Principal');
DECLARE @idEstablecimientoSinHora BIGINT = (SELECT id_establecimiento FROM comercio_establecimientos WHERE id_comercio_aliado = @idComercioAliado2 AND nombre_establecimiento = 'CI Fixture Sede Sin Override Hora');
DECLARE @idComercioExistente2 BIGINT = $(ID_COMERCIO);

-- ── comercio_usuarios — un scope por usuario, salvo ci_cajero_ambiguo (2) ──
INSERT INTO comercio_usuarios (id_comercio_aliado, id_comercio_existente, id_establecimiento, id_usuario, rol_comercio, estado)
SELECT @idComercioAliado2, @idComercioExistente2, @idEstablecimiento, u.id_usuario, 'CAJERO', 'ACTIVO'
FROM usuarios u WHERE u.usuario = 'ci_cajero_comercio'
  AND NOT EXISTS (SELECT 1 FROM comercio_usuarios cu WHERE cu.id_usuario = u.id_usuario AND cu.rol_comercio='CAJERO' AND cu.id_establecimiento=@idEstablecimiento);

INSERT INTO comercio_usuarios (id_comercio_aliado, id_comercio_existente, id_establecimiento, id_usuario, rol_comercio, estado)
SELECT @idComercioAliado2, @idComercioExistente2, @idEstablecimiento, u.id_usuario, 'ADMIN_SEDE_COMERCIO', 'ACTIVO'
FROM usuarios u WHERE u.usuario = 'ci_admin_sede_comercio'
  AND NOT EXISTS (SELECT 1 FROM comercio_usuarios cu WHERE cu.id_usuario = u.id_usuario AND cu.rol_comercio='ADMIN_SEDE_COMERCIO' AND cu.id_establecimiento=@idEstablecimiento);

-- ADMIN_COMERCIO — sin sede fija a propósito (mismo criterio que AbrirAsync exige explícita en el body).
INSERT INTO comercio_usuarios (id_comercio_aliado, id_comercio_existente, id_establecimiento, id_usuario, rol_comercio, estado)
SELECT @idComercioAliado2, @idComercioExistente2, NULL, u.id_usuario, 'ADMIN_COMERCIO', 'ACTIVO'
FROM usuarios u WHERE u.usuario = 'ci_admin_comercio'
  AND NOT EXISTS (SELECT 1 FROM comercio_usuarios cu WHERE cu.id_usuario = u.id_usuario AND cu.rol_comercio='ADMIN_COMERCIO');

-- ci_cajero_ambiguo — DOS filas ACTIVAS a propósito (dispara ScopeComercioAmbiguoException).
INSERT INTO comercio_usuarios (id_comercio_aliado, id_comercio_existente, id_establecimiento, id_usuario, rol_comercio, estado)
SELECT @idComercioAliado2, @idComercioExistente2, @idEstablecimiento, u.id_usuario, 'CAJERO', 'ACTIVO'
FROM usuarios u WHERE u.usuario = 'ci_cajero_ambiguo'
  AND (SELECT COUNT(*) FROM comercio_usuarios cu WHERE cu.id_usuario = u.id_usuario AND cu.estado='ACTIVO') < 1;

INSERT INTO comercio_usuarios (id_comercio_aliado, id_comercio_existente, id_establecimiento, id_usuario, rol_comercio, estado)
SELECT @idComercioAliado2, @idComercioExistente2, @idEstablecimiento, u.id_usuario, 'CAJERO', 'ACTIVO'
FROM usuarios u WHERE u.usuario = 'ci_cajero_ambiguo'
  AND (SELECT COUNT(*) FROM comercio_usuarios cu WHERE cu.id_usuario = u.id_usuario AND cu.estado='ACTIVO') < 2;

-- ci_cajero_vencimiento / ci_cajero_vigente — sede sin override de hora.
INSERT INTO comercio_usuarios (id_comercio_aliado, id_comercio_existente, id_establecimiento, id_usuario, rol_comercio, estado)
SELECT @idComercioAliado2, @idComercioExistente2, @idEstablecimientoSinHora, u.id_usuario, 'CAJERO', 'ACTIVO'
FROM usuarios u WHERE u.usuario = 'ci_cajero_vencimiento'
  AND NOT EXISTS (SELECT 1 FROM comercio_usuarios cu WHERE cu.id_usuario = u.id_usuario AND cu.rol_comercio='CAJERO' AND cu.id_establecimiento=@idEstablecimientoSinHora);

INSERT INTO comercio_usuarios (id_comercio_aliado, id_comercio_existente, id_establecimiento, id_usuario, rol_comercio, estado)
SELECT @idComercioAliado2, @idComercioExistente2, @idEstablecimientoSinHora, u.id_usuario, 'CAJERO', 'ACTIVO'
FROM usuarios u WHERE u.usuario = 'ci_cajero_vigente'
  AND NOT EXISTS (SELECT 1 FROM comercio_usuarios cu WHERE cu.id_usuario = u.id_usuario AND cu.rol_comercio='CAJERO' AND cu.id_establecimiento=@idEstablecimientoSinHora);
GO

PRINT '--- Fixture CI: comercio aliado + caja listo (Fase 70.4-C) ---';
GO
