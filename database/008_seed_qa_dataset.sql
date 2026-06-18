/* ================================================================
   XPAY MVP — QA Seed Dataset
   database/008_seed_qa_dataset.sql
   ================================================================

   !! USO EXCLUSIVO: QA / DESARROLLO                           !!
   !! NO EJECUTAR EN PRODUCCIÓN                                !!
   !! NO CONTIENE NI GENERA DATOS REALES                       !!
   !! NO INVOLUCRA DINERO REAL                                 !!

   Se recomienda confirmar que la base de datos activa sea
   XPAY_MVP_QA antes de ejecutar:
       USE XPAY_MVP_QA;
   -- GO

   Prerrequisito: scripts 001 a 007 ejecutados correctamente.

   Idempotencia: el script puede ejecutarse más de una vez.
   Cada bloque usa NOT EXISTS / IF NOT EXISTS para evitar
   duplicados de personas, usuarios, wallets, comercio y QR.
   ================================================================ */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

PRINT '================================================================';
PRINT ' XPAY QA SEED — Iniciando carga de dataset QA';
PRINT ' !! SOLO QA / DEV  —  NO PRODUCCION  —  NO DINERO REAL !!';
PRINT '================================================================';
GO

-- ================================================================
-- SECCION 1: VERIFICACION DE PRERREQUISITOS
-- ================================================================
PRINT '';
PRINT '--- Seccion 1: Verificando prerrequisitos ---';
GO

IF NOT EXISTS (SELECT 1 FROM unidades_negocio WHERE codigo = 'XPAY_COL')
BEGIN
    RAISERROR ('ERROR: No se encontro XPAY_COL en unidades_negocio. Ejecutar script 001 primero.', 16, 1);
    RETURN;
END
PRINT 'OK: unidades_negocio XPAY_COL existe.';

IF NOT EXISTS (SELECT 1 FROM roles WHERE codigo = 'ADMIN_XPAY')
   OR NOT EXISTS (SELECT 1 FROM roles WHERE codigo = 'OPERADOR_XPAY')
   OR NOT EXISTS (SELECT 1 FROM roles WHERE codigo = 'COMERCIO')
BEGIN
    RAISERROR ('ERROR: Roles ADMIN_XPAY / OPERADOR_XPAY / COMERCIO no encontrados. Ejecutar script 007 primero.', 16, 1);
    RETURN;
END
PRINT 'OK: roles ADMIN_XPAY, OPERADOR_XPAY y COMERCIO existen.';
GO

-- ================================================================
-- SECCION 2: PERSONAS QA
-- Documentos de prueba: CC 900000001 a 900000004
-- Son claramente ficticios (no pertenecen a ningun ciudadano real)
-- ================================================================
PRINT '';
PRINT '--- Seccion 2: Personas QA ---';
GO

DECLARE @idUnidad BIGINT;
SELECT @idUnidad = id_unidad_negocio FROM unidades_negocio WHERE codigo = 'XPAY_COL';

-- Persona 1: QA Admin XPAY
IF NOT EXISTS (
    SELECT 1 FROM personas
    WHERE id_unidad_negocio = @idUnidad
      AND tipo_documento = 'CC'
      AND numero_documento = '900000001'
)
BEGIN
    INSERT INTO personas
        (id_unidad_negocio, tipo_documento, numero_documento,
         primer_nombre, primer_apellido, celular, email, estado)
    VALUES
        (@idUnidad, 'CC', '900000001',
         'QA Admin', 'XPAY',
         '3000000001', 'qa.admin@xpay.test', 'ACTIVA');
    PRINT '  Persona QA Admin XPAY creada.';
END
ELSE PRINT '  Persona QA Admin XPAY ya existe — omitida.';

-- Persona 2: QA Operador XPAY
IF NOT EXISTS (
    SELECT 1 FROM personas
    WHERE id_unidad_negocio = @idUnidad
      AND tipo_documento = 'CC'
      AND numero_documento = '900000002'
)
BEGIN
    INSERT INTO personas
        (id_unidad_negocio, tipo_documento, numero_documento,
         primer_nombre, primer_apellido, celular, email, estado)
    VALUES
        (@idUnidad, 'CC', '900000002',
         'QA Operador', 'XPAY',
         '3000000002', 'qa.operador@xpay.test', 'ACTIVA');
    PRINT '  Persona QA Operador XPAY creada.';
END
ELSE PRINT '  Persona QA Operador XPAY ya existe — omitida.';

-- Persona 3: QA Usuario Uno
IF NOT EXISTS (
    SELECT 1 FROM personas
    WHERE id_unidad_negocio = @idUnidad
      AND tipo_documento = 'CC'
      AND numero_documento = '900000003'
)
BEGIN
    INSERT INTO personas
        (id_unidad_negocio, tipo_documento, numero_documento,
         primer_nombre, primer_apellido, celular, email, estado)
    VALUES
        (@idUnidad, 'CC', '900000003',
         'QA Usuario', 'Uno',
         '3000000003', 'qa.usuario1@xpay.test', 'ACTIVA');
    PRINT '  Persona QA Usuario Uno creada.';
END
ELSE PRINT '  Persona QA Usuario Uno ya existe — omitida.';

-- Persona 4: QA Usuario Dos
IF NOT EXISTS (
    SELECT 1 FROM personas
    WHERE id_unidad_negocio = @idUnidad
      AND tipo_documento = 'CC'
      AND numero_documento = '900000004'
)
BEGIN
    INSERT INTO personas
        (id_unidad_negocio, tipo_documento, numero_documento,
         primer_nombre, primer_apellido, celular, email, estado)
    VALUES
        (@idUnidad, 'CC', '900000004',
         'QA Usuario', 'Dos',
         '3000000004', 'qa.usuario2@xpay.test', 'ACTIVA');
    PRINT '  Persona QA Usuario Dos creada.';
END
ELSE PRINT '  Persona QA Usuario Dos ya existe — omitida.';
GO

-- ================================================================
-- SECCION 3: USUARIOS QA
--
-- AVISO IMPORTANTE — password_hash:
--   El hash incluido es un PLACEHOLDER con formato BCrypt valido
--   ($2a$11$, 60 caracteres) pero NO es el hash de ninguna
--   contrasena real. Los usuarios creados con este hash NO
--   podran iniciar sesion hasta que se actualice el hash.
--
-- Para habilitar el login de usuarios QA:
--
--   Opcion 1 (recomendada): Crear usuarios via API antes de
--   ejecutar la Seccion 4 de este script:
--     POST /api/usuarios/registro-final
--     (ver pattern en scripts/validate-backend.sh)
--   Luego saltar al final del script para verificar.
--
--   Opcion 2: Generar hash BCrypt cost-11 con el proyecto .NET:
--     using BCrypt.Net;
--     Console.WriteLine(BCrypt.HashPassword("XpayQA@Test1!"));
--   Y ejecutar:
--     UPDATE usuarios SET password_hash = '<hash_generado>'
--     WHERE usuario IN (
--       'qa.admin.xpay','qa.operador.xpay','qa.usuario1','qa.usuario2'
--     );
--
-- Contrasena sugerida para ambiente QA: XpayQA@Test1!
-- ================================================================
PRINT '';
PRINT '--- Seccion 3: Usuarios QA (con hash placeholder) ---';
PRINT '  AVISO: Usuarios creados con hash placeholder.';
PRINT '  Requieren actualizacion de password_hash para login.';
PRINT '  Ver comentarios de Seccion 3 en el script para instrucciones.';
GO

DECLARE @placeholder_hash NVARCHAR(500);
-- Hash placeholder: formato BCrypt valido ($2a$11$ + 53 chars), no es hash de ninguna contrasena.
SET @placeholder_hash = N'$2a$11$QAseedPlaceholder.XPAY.QA.NOVALID.00000000000000000000';

-- Usuario qa.admin.xpay
IF NOT EXISTS (SELECT 1 FROM usuarios WHERE usuario = 'qa.admin.xpay')
BEGIN
    INSERT INTO usuarios (id_persona, usuario, password_hash, estado)
    SELECT p.id_persona, 'qa.admin.xpay', @placeholder_hash, 'ACTIVO'
    FROM   personas p
    WHERE  p.numero_documento = '900000001'
      AND  p.tipo_documento = 'CC';
    PRINT '  Usuario qa.admin.xpay creado.';
END
ELSE PRINT '  Usuario qa.admin.xpay ya existe — omitido.';

-- Usuario qa.operador.xpay
IF NOT EXISTS (SELECT 1 FROM usuarios WHERE usuario = 'qa.operador.xpay')
BEGIN
    INSERT INTO usuarios (id_persona, usuario, password_hash, estado)
    SELECT p.id_persona, 'qa.operador.xpay', @placeholder_hash, 'ACTIVO'
    FROM   personas p
    WHERE  p.numero_documento = '900000002'
      AND  p.tipo_documento = 'CC';
    PRINT '  Usuario qa.operador.xpay creado.';
END
ELSE PRINT '  Usuario qa.operador.xpay ya existe — omitido.';

-- Usuario qa.usuario1
IF NOT EXISTS (SELECT 1 FROM usuarios WHERE usuario = 'qa.usuario1')
BEGIN
    INSERT INTO usuarios (id_persona, usuario, password_hash, estado)
    SELECT p.id_persona, 'qa.usuario1', @placeholder_hash, 'ACTIVO'
    FROM   personas p
    WHERE  p.numero_documento = '900000003'
      AND  p.tipo_documento = 'CC';
    PRINT '  Usuario qa.usuario1 creado.';
END
ELSE PRINT '  Usuario qa.usuario1 ya existe — omitido.';

-- Usuario qa.usuario2
IF NOT EXISTS (SELECT 1 FROM usuarios WHERE usuario = 'qa.usuario2')
BEGIN
    INSERT INTO usuarios (id_persona, usuario, password_hash, estado)
    SELECT p.id_persona, 'qa.usuario2', @placeholder_hash, 'ACTIVO'
    FROM   personas p
    WHERE  p.numero_documento = '900000004'
      AND  p.tipo_documento = 'CC';
    PRINT '  Usuario qa.usuario2 creado.';
END
ELSE PRINT '  Usuario qa.usuario2 ya existe — omitido.';
GO

-- ================================================================
-- SECCION 4: ASIGNACION DE ROLES QA
-- Roles definidos en 007_security_roles_jwt.sql
-- ================================================================
PRINT '';
PRINT '--- Seccion 4: Asignacion de roles QA ---';
GO

-- qa.admin.xpay → ADMIN_XPAY
INSERT INTO usuario_roles (id_usuario, id_rol)
SELECT u.id_usuario, r.id_rol
FROM   usuarios u
JOIN   roles r ON r.codigo = 'ADMIN_XPAY'
WHERE  u.usuario = 'qa.admin.xpay'
  AND  NOT EXISTS (
           SELECT 1 FROM usuario_roles ur
           WHERE  ur.id_usuario = u.id_usuario
             AND  ur.id_rol = r.id_rol
       );

-- qa.operador.xpay → OPERADOR_XPAY
INSERT INTO usuario_roles (id_usuario, id_rol)
SELECT u.id_usuario, r.id_rol
FROM   usuarios u
JOIN   roles r ON r.codigo = 'OPERADOR_XPAY'
WHERE  u.usuario = 'qa.operador.xpay'
  AND  NOT EXISTS (
           SELECT 1 FROM usuario_roles ur
           WHERE  ur.id_usuario = u.id_usuario
             AND  ur.id_rol = r.id_rol
       );

PRINT '  Roles asignados: qa.admin.xpay → ADMIN_XPAY, qa.operador.xpay → OPERADOR_XPAY.';
GO

-- ================================================================
-- SECCION 5: WALLETS QA (TIPO PERSONA)
-- Wallets para QA Usuario Uno y QA Usuario Dos con saldo 0.
-- Los saldos se generan via endpoint /api/wallets/{id}/recarga-manual.
-- ================================================================
PRINT '';
PRINT '--- Seccion 5: Wallets QA (tipo PERSONA) ---';
GO

DECLARE @idUnidad5 BIGINT;
SELECT @idUnidad5 = id_unidad_negocio FROM unidades_negocio WHERE codigo = 'XPAY_COL';

-- Wallet PERSONA para QA Usuario Uno (CC 900000003)
IF NOT EXISTS (
    SELECT 1 FROM wallets w
    JOIN   personas p ON p.id_persona = w.id_persona
    WHERE  p.numero_documento = '900000003'
      AND  p.tipo_documento = 'CC'
      AND  w.tipo_wallet = 'PERSONA'
      AND  w.estado = 'ACTIVA'
)
BEGIN
    INSERT INTO wallets (id_unidad_negocio, tipo_wallet, id_persona, nombre_wallet, estado)
    SELECT @idUnidad5, 'PERSONA', p.id_persona, 'Wallet QA Usuario Uno', 'ACTIVA'
    FROM   personas p
    WHERE  p.numero_documento = '900000003'
      AND  p.tipo_documento = 'CC';

    INSERT INTO wallet_saldos
        (id_wallet, saldo_disponible, saldo_retenido, saldo_transito, saldo_contingencia)
    SELECT w.id_wallet, 0, 0, 0, 0
    FROM   wallets w
    JOIN   personas p ON p.id_persona = w.id_persona
    WHERE  p.numero_documento = '900000003'
      AND  p.tipo_documento = 'CC'
      AND  w.tipo_wallet = 'PERSONA'
      AND  w.estado = 'ACTIVA'
      AND  NOT EXISTS (
               SELECT 1 FROM wallet_saldos ws WHERE ws.id_wallet = w.id_wallet
           );

    PRINT '  Wallet QA Usuario Uno creada (saldo 0).';
END
ELSE PRINT '  Wallet QA Usuario Uno ya existe — omitida.';

-- Wallet PERSONA para QA Usuario Dos (CC 900000004)
IF NOT EXISTS (
    SELECT 1 FROM wallets w
    JOIN   personas p ON p.id_persona = w.id_persona
    WHERE  p.numero_documento = '900000004'
      AND  p.tipo_documento = 'CC'
      AND  w.tipo_wallet = 'PERSONA'
      AND  w.estado = 'ACTIVA'
)
BEGIN
    INSERT INTO wallets (id_unidad_negocio, tipo_wallet, id_persona, nombre_wallet, estado)
    SELECT @idUnidad5, 'PERSONA', p.id_persona, 'Wallet QA Usuario Dos', 'ACTIVA'
    FROM   personas p
    WHERE  p.numero_documento = '900000004'
      AND  p.tipo_documento = 'CC';

    INSERT INTO wallet_saldos
        (id_wallet, saldo_disponible, saldo_retenido, saldo_transito, saldo_contingencia)
    SELECT w.id_wallet, 0, 0, 0, 0
    FROM   wallets w
    JOIN   personas p ON p.id_persona = w.id_persona
    WHERE  p.numero_documento = '900000004'
      AND  p.tipo_documento = 'CC'
      AND  w.tipo_wallet = 'PERSONA'
      AND  w.estado = 'ACTIVA'
      AND  NOT EXISTS (
               SELECT 1 FROM wallet_saldos ws WHERE ws.id_wallet = w.id_wallet
           );

    PRINT '  Wallet QA Usuario Dos creada (saldo 0).';
END
ELSE PRINT '  Wallet QA Usuario Dos ya existe — omitida.';
GO

-- ================================================================
-- SECCION 6: COMERCIO DEMO QA
-- Separado del comercio creado por 003_comercios_qr.sql.
-- Comercio Demo XPAY     → creado por migracion 003 (no tocar)
-- Comercio Demo XPAY QA  → creado por este seed (QA only)
-- ================================================================
PRINT '';
PRINT '--- Seccion 6: Comercio Demo XPAY QA ---';
GO

DECLARE @idUnidad6 BIGINT;
SELECT @idUnidad6 = id_unidad_negocio FROM unidades_negocio WHERE codigo = 'XPAY_COL';

-- Comercio
IF NOT EXISTS (SELECT 1 FROM comercios WHERE nombre_comercial = N'Comercio Demo XPAY QA')
BEGIN
    INSERT INTO comercios (id_unidad_negocio, nombre_comercial, razon_social, nit, estado)
    VALUES (@idUnidad6, N'Comercio Demo XPAY QA', N'Comercio Demo XPAY QA SAS', N'900999001-1', 'ACTIVO');
    PRINT '  Comercio Demo XPAY QA creado.';
END
ELSE PRINT '  Comercio Demo XPAY QA ya existe — omitido.';

-- Tienda
IF NOT EXISTS (
    SELECT 1 FROM comercio_tiendas ct
    JOIN   comercios c ON c.id_comercio = ct.id_comercio
    WHERE  c.nombre_comercial = N'Comercio Demo XPAY QA'
      AND  ct.nombre_tienda   = N'Tienda Principal QA'
)
BEGIN
    INSERT INTO comercio_tiendas (id_comercio, nombre_tienda, ciudad, direccion, estado)
    SELECT c.id_comercio, N'Tienda Principal QA', N'Bogota', N'Calle QA Test 1 #0-0', 'ACTIVO'
    FROM   comercios c
    WHERE  c.nombre_comercial = N'Comercio Demo XPAY QA';
    PRINT '  Tienda Principal QA creada.';
END
ELSE PRINT '  Tienda Principal QA ya existe — omitida.';

-- QR Demo QA
IF NOT EXISTS (SELECT 1 FROM qr_comercios WHERE codigo_qr = N'QR-DEMO-XPAY-QA-001')
BEGIN
    INSERT INTO qr_comercios (id_comercio, id_tienda, codigo_qr, estado)
    SELECT c.id_comercio, ct.id_tienda, N'QR-DEMO-XPAY-QA-001', 'ACTIVO'
    FROM   comercios c
    JOIN   comercio_tiendas ct ON ct.id_comercio = c.id_comercio
    WHERE  c.nombre_comercial = N'Comercio Demo XPAY QA'
      AND  ct.nombre_tienda   = N'Tienda Principal QA';
    PRINT '  QR QR-DEMO-XPAY-QA-001 creado.';
END
ELSE PRINT '  QR QR-DEMO-XPAY-QA-001 ya existe — omitido.';
GO

-- Wallet COMERCIO para Comercio Demo XPAY QA
DECLARE @idUnidad7  BIGINT;
DECLARE @idComercioQA BIGINT;
DECLARE @idWalletComercioQA BIGINT;

SELECT @idUnidad7    = id_unidad_negocio FROM unidades_negocio WHERE codigo = 'XPAY_COL';
SELECT @idComercioQA = id_comercio       FROM comercios        WHERE nombre_comercial = N'Comercio Demo XPAY QA';

IF @idComercioQA IS NULL
BEGIN
    RAISERROR ('ERROR: Comercio Demo XPAY QA no encontrado — revisar seccion anterior.', 16, 1);
    RETURN;
END

IF NOT EXISTS (
    SELECT 1 FROM wallets
    WHERE  id_comercio = @idComercioQA
      AND  tipo_wallet = 'COMERCIO'
      AND  estado      = 'ACTIVA'
)
BEGIN
    INSERT INTO wallets (id_unidad_negocio, tipo_wallet, id_comercio, nombre_wallet, estado)
    VALUES (@idUnidad7, 'COMERCIO', @idComercioQA, 'Wallet Comercio Demo XPAY QA', 'ACTIVA');
    SET @idWalletComercioQA = SCOPE_IDENTITY();

    INSERT INTO wallet_saldos
        (id_wallet, saldo_disponible, saldo_retenido, saldo_transito, saldo_contingencia)
    VALUES (@idWalletComercioQA, 0, 0, 0, 0);

    UPDATE comercios
    SET    id_wallet_comercio = @idWalletComercioQA
    WHERE  id_comercio = @idComercioQA;

    PRINT '  Wallet COMERCIO QA creada y enlazada al comercio.';
END
ELSE PRINT '  Wallet COMERCIO QA ya existe — omitida.';
GO

-- ================================================================
-- SECCION 7: VERIFICACION DE CUENTAS LEDGER REQUERIDAS
-- Las cuentas deben existir tras ejecutar 002 a 006.
-- 110101 se inserta con NOT EXISTS por si 006 no se habia corrido.
-- ================================================================
PRINT '';
PRINT '--- Seccion 7: Verificando cuentas ledger requeridas ---';
GO

DECLARE @idUnidad8 BIGINT;
SELECT @idUnidad8 = id_unidad_negocio FROM unidades_negocio WHERE codigo = 'XPAY_COL';

-- 110101 — Efectivo en Boveda (creada en 006 sin NOT EXISTS; insertar si falta)
IF NOT EXISTS (
    SELECT 1 FROM ledger_cuentas
    WHERE  id_unidad_negocio = @idUnidad8 AND codigo = '110101'
)
BEGIN
    INSERT INTO ledger_cuentas
        (id_unidad_negocio, codigo, nombre, tipo_cuenta, subtipo_cuenta, naturaleza, permite_movimiento)
    VALUES
        (@idUnidad8, '110101', 'Efectivo en Boveda', 'ACTIVO', 'CAJA_BOVEDA', 'D', 1);
    PRINT '  Cuenta 110101 creada (faltaba — verificar que 006 se ejecuto).';
END
ELSE PRINT '  Cuenta 110101 OK.';

-- 210101 — Obligacion Wallet Usuarios (creada en 002)
IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE id_unidad_negocio = @idUnidad8 AND codigo = '210101')
    PRINT '  ADVERTENCIA: Cuenta 210101 no encontrada — verificar script 002.';
ELSE PRINT '  Cuenta 210101 OK.';

-- 210201 — Ventas QR en Contingencia Comercios (creada en 003)
IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE id_unidad_negocio = @idUnidad8 AND codigo = '210201')
    PRINT '  ADVERTENCIA: Cuenta 210201 no encontrada — verificar script 003.';
ELSE PRINT '  Cuenta 210201 OK.';

-- 210202 — Obligacion Wallet Comercios (creada en 004)
IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE id_unidad_negocio = @idUnidad8 AND codigo = '210202')
    PRINT '  ADVERTENCIA: Cuenta 210202 no encontrada — verificar script 004.';
ELSE PRINT '  Cuenta 210202 OK.';

-- 210203 — Retiros Comercios Pendientes de Pago (creada en 005)
IF NOT EXISTS (SELECT 1 FROM ledger_cuentas WHERE id_unidad_negocio = @idUnidad8 AND codigo = '210203')
    PRINT '  ADVERTENCIA: Cuenta 210203 no encontrada — verificar script 005.';
ELSE PRINT '  Cuenta 210203 OK.';
GO

-- ================================================================
-- SECCION 8: TRANSACCIONES FINANCIERAS QA
--
-- DECISION DE DISENO: No se insertan transacciones financieras
-- directamente en SQL. Razon: cada operacion financiera requiere
-- movimientos de doble entrada sincronizados entre:
--   ledger_transacciones → ledger_movimientos
--   wallet_saldos → wallet_movimientos
--   ventas_qr / retiros_comercio
-- Insertar estos datos manualmente sin respetar ese invariante
-- crearía estados inconsistentes que dificultarian las pruebas QA.
--
-- Para generar datos financieros de prueba, usar los endpoints:
--
--   Recarga wallet usuario:
--     POST /api/wallets/{idWallet}/recarga-manual
--     {"valor": 50000, "creadoPor": {idUsuario}, "observacion": "Recarga QA"}
--
--   Pago QR (genera venta en CONTINGENCIA):
--     POST /api/qr/pagar
--     {"codigoQr": "QR-DEMO-XPAY-QA-001",
--      "idWalletUsuario": {idWallet}, "valor": 20000, "creadoPor": {idUsuario}}
--
--   Liquidacion QR (CONTINGENCIA → LIQUIDADA):
--     POST /api/qr/liquidar/{idVentaQr}
--
--   Solicitud retiro (genera retiro PENDIENTE):
--     POST /api/comercios/solicitar-retiro
--
--   Confirmar pago retiro (PENDIENTE → PAGADO):
--     PATCH /api/retiros/{idRetiro}/confirmar-pago
--
--   Rechazar retiro (PENDIENTE → RECHAZADO):
--     PATCH /api/retiros/{idRetiro}/rechazar
--
-- Ver scripts/validate-backend.sh para ejemplos completos.
-- ================================================================
PRINT '';
PRINT '--- Seccion 8: Transacciones financieras ---';
PRINT '  OMITIDAS por diseno — ver comentarios del script.';
PRINT '  Usar endpoints del backend para generar datos financieros QA.';
GO

-- ================================================================
-- SECCION 9: VALIDACIONES FINALES
-- ================================================================
PRINT '';
PRINT '--- Seccion 9: Validaciones finales ---';
PRINT '';
GO

PRINT '>> Personas QA creadas:';
SELECT
    p.id_persona,
    p.primer_nombre + ' ' + p.primer_apellido AS nombre_completo,
    p.tipo_documento,
    p.numero_documento,
    p.email,
    p.estado
FROM   personas p
WHERE  p.numero_documento IN ('900000001','900000002','900000003','900000004')
ORDER  BY p.numero_documento;
GO

PRINT '';
PRINT '>> Usuarios QA:';
SELECT
    u.id_usuario,
    u.usuario,
    u.estado,
    CASE WHEN u.password_hash LIKE '$2a$11$QAseedPlaceholder%'
         THEN 'PLACEHOLDER — reemplazar antes de login'
         ELSE 'Hash configurado'
    END AS estado_hash
FROM   usuarios u
WHERE  u.usuario IN ('qa.admin.xpay','qa.operador.xpay','qa.usuario1','qa.usuario2')
ORDER  BY u.usuario;
GO

PRINT '';
PRINT '>> Roles asignados a usuarios QA:';
SELECT
    u.usuario,
    r.codigo AS rol,
    ur.estado
FROM   usuario_roles ur
JOIN   usuarios u ON u.id_usuario = ur.id_usuario
JOIN   roles    r ON r.id_rol     = ur.id_rol
WHERE  u.usuario IN ('qa.admin.xpay','qa.operador.xpay','qa.usuario1','qa.usuario2')
ORDER  BY u.usuario;
GO

PRINT '';
PRINT '>> Wallets QA disponibles:';
SELECT
    w.id_wallet,
    w.tipo_wallet,
    w.nombre_wallet,
    w.estado,
    ISNULL(p.numero_documento, '') AS doc_persona,
    ISNULL(c.nombre_comercial, '') AS nombre_comercio,
    ws.saldo_disponible,
    ws.saldo_retenido
FROM       wallets w
LEFT JOIN  wallet_saldos ws ON ws.id_wallet   = w.id_wallet
LEFT JOIN  personas      p  ON p.id_persona   = w.id_persona
LEFT JOIN  comercios     c  ON c.id_comercio  = w.id_comercio
WHERE  ( p.numero_documento IN ('900000003','900000004') )
    OR ( c.nombre_comercial = N'Comercio Demo XPAY QA'  )
ORDER  BY w.tipo_wallet, w.id_wallet;
GO

PRINT '';
PRINT '>> Comercio / Tienda / QR QA:';
SELECT
    c.id_comercio,
    c.nombre_comercial,
    c.nit,
    c.estado,
    ct.nombre_tienda,
    q.codigo_qr,
    q.estado       AS estado_qr
FROM   comercios         c
LEFT JOIN comercio_tiendas  ct ON ct.id_comercio = c.id_comercio
LEFT JOIN qr_comercios      q  ON q.id_tienda    = ct.id_tienda
WHERE  c.nombre_comercial = N'Comercio Demo XPAY QA';
GO

PRINT '';
PRINT '>> Cuentas ledger requeridas:';
SELECT
    lc.codigo,
    lc.nombre,
    lc.tipo_cuenta,
    lc.naturaleza,
    lc.estado
FROM   ledger_cuentas   lc
JOIN   unidades_negocio u  ON u.id_unidad_negocio = lc.id_unidad_negocio
WHERE  u.codigo   = 'XPAY_COL'
  AND  lc.codigo IN ('110101','210101','210201','210202','210203')
ORDER  BY lc.codigo;
GO

PRINT '';
PRINT '>> Ventas QR vinculadas a datos QA (si existen):';
SELECT COUNT(*) AS ventas_qr_qa
FROM   ventas_qr vq
JOIN   qr_comercios q ON q.id_qr = vq.id_qr
WHERE  q.codigo_qr = N'QR-DEMO-XPAY-QA-001';
GO

PRINT '';
PRINT '>> Retiros vinculados al comercio QA (si existen):';
SELECT COUNT(*) AS retiros_qa
FROM   retiros_comercio rc
JOIN   comercios        c  ON c.id_comercio = rc.id_comercio
WHERE  c.nombre_comercial = N'Comercio Demo XPAY QA';
GO

PRINT '';
PRINT '================================================================';
PRINT ' XPAY QA SEED — Completado.';
PRINT '';
PRINT ' Proximos pasos para completar el dataset QA:';
PRINT '   1. Actualizar password_hash de usuarios QA (ver Seccion 3).';
PRINT '   2. Generar datos financieros via endpoints del backend.';
PRINT '   3. Ver scripts/validate-backend.sh para ejemplos.';
PRINT '';
PRINT ' !! SOLO QA / DEV  —  NO PRODUCCION  —  NO DINERO REAL !!';
PRINT '================================================================';
GO
