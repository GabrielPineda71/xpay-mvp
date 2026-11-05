-- operacion/verificacion_b1.sql - XPAY PROD, B1.
-- Verificacion posterior a B1. SOLO LECTURA: SET NOCOUNT y SELECT.
-- Una linea por control, etiqueta|valor. Esperado tras B1
-- (procedimiento, 6.7): TABLAS 24, UNIDADES 1, ROLES 18, PERMISOS 27,
-- CUENTAS 25, USUARIOS 0, COL_KYC 30, ACENTOS 1. NULO solo muestra
-- como representa sqlcmd un valor NULL; no se compara.
-- Probado solo en SQL Server 2022 local (T1 y T4). No validado en
-- Azure SQL; ACENTOS depende de la intercalacion real de la base.
SET NOCOUNT ON;
SELECT N'TABLAS', COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0;
SELECT N'UNIDADES', COUNT(*) FROM dbo.unidades_negocio;
SELECT N'ROLES', COUNT(*) FROM dbo.roles;
SELECT N'PERMISOS', COUNT(*) FROM dbo.permisos;
SELECT N'CUENTAS', COUNT(*) FROM dbo.ledger_cuentas;
SELECT N'USUARIOS', COUNT(*) FROM dbo.usuarios;
SELECT N'COL_KYC',
       ISNULL(COL_LENGTH('dbo.usuarios', 'estado_kyc_actual'), -1);
SELECT N'ACENTOS', COUNT(*)
FROM dbo.ledger_cuentas
WHERE codigo = '130104'
  AND CAST(nombre AS NVARCHAR(200)) COLLATE Latin1_General_BIN2
      = N'Cartera Anticipo N' + NCHAR(243) + N'mina';
SELECT N'NULO', NULL;
