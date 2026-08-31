#!/usr/bin/env bash
# Valida los endpoints del backend XPAY MVP — Fases 1 a 8.
# Variables: API_URL, DB_HOST, DB_NAME, SA_PASSWORD
set -euo pipefail

API_URL="${API_URL:-http://localhost:5000}"
DB_HOST="${DB_HOST:-localhost,1433}"
DB_NAME="${DB_NAME:-XPAY_MVP}"
SA_PASS="${SA_PASSWORD:-XpayCI@2024!}"
DB_USER="${DB_USER:-sa}"
# Auto-detect sqlcmd path: CI Linux path first, fallback to Homebrew (macOS)
SQLCMD="${SQLCMD_PATH:-$(command -v sqlcmd 2>/dev/null || echo /opt/mssql-tools18/bin/sqlcmd)}"

GREEN='\033[0;32m'
RED='\033[0;31m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
NC='\033[0m'

ok()    { echo -e "${GREEN}[OK]${NC}   $*"; }
fail()  { echo -e "${RED}[FAIL]${NC} $*"; exit 1; }
info()  { echo -e "${CYAN}━━━ $* ━━━${NC}"; }
phase() { echo -e "\n${YELLOW}═══ $* ═══${NC}"; }

# Endpoints públicos (sin token)
post_json() {
  local url="$1" body="$2"
  curl -sf -X POST "$url" -H "Content-Type: application/json" --max-time 15 -d "$body"
}

get_json() {
  curl -sf --max-time 15 "$1"
}

# Endpoints protegidos (con token JWT)
get_auth_json() {
  local token="$1" url="$2"
  curl -sf --max-time 15 -H "Authorization: Bearer $token" "$url"
}

post_auth_json() {
  local token="$1" url="$2" body="$3" idempotency_key="${4:-}"
  if [[ -n "$idempotency_key" ]]; then
    curl -sf -X POST "$url" -H "Content-Type: application/json" \
      -H "Authorization: Bearer $token" -H "Idempotency-Key: $idempotency_key" \
      --max-time 15 -d "$body"
  else
    curl -sf -X POST "$url" -H "Content-Type: application/json" \
      -H "Authorization: Bearer $token" --max-time 15 -d "$body"
  fi
}

# Fase 70.4-C: variante que NO usa -f — necesaria para inspeccionar el body
# de una respuesta de error (4xx), que curl -f descarta. Devuelve
# "<http_code>\n<body>" en una sola cadena, separados por salto de línea.
post_auth_json_status() {
  local token="$1" url="$2" body="$3"
  curl -s -w '\n%{http_code}' -X POST "$url" -H "Content-Type: application/json" \
    -H "Authorization: Bearer $token" --max-time 15 -d "$body"
}

assert_ok() {
  local resp="$1" label="$2"
  local success
  success=$(echo "$resp" | jq -r '.success')
  [[ "$success" == "true" ]] || fail "$label → success=false. Respuesta: $resp"
}

assert_saldo() {
  local resp="$1" esperado="$2" label="$3"
  echo "$resp" | jq -e ".data.saldoDisponible == $esperado" > /dev/null \
    || fail "$label → saldoDisponible esperado $esperado, obtenido $(echo "$resp" | jq -r '.data.saldoDisponible')"
  ok "$label → saldoDisponible=$esperado ✓"
}

check_count() {
  local table="$1" min="${2:-1}"
  local count
  count=$("$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" \
    -d "$DB_NAME" -b -C \
    -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM $table" \
    -h -1 | tr -d ' \r\n')
  [[ "$count" =~ ^[0-9]+$ ]] || fail "No se pudo obtener conteo de $table (resultado: '$count')"
  [[ "$count" -ge "$min" ]] || fail "Tabla $table tiene $count registro(s), se esperaban >= $min"
  ok "Tabla $table → $count registro(s) (esperado >= $min) ✓"
}

check_sql_value() {
  local label="$1" query="$2" esperado="$3"
  local resultado
  resultado=$("$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" \
    -d "$DB_NAME" -b -C \
    -Q "SET NOCOUNT ON; $query" \
    -h -1 | tr -d ' \r\n')
  [[ "$resultado" == "$esperado" ]] \
    || fail "$label → esperado='$esperado', obtenido='$resultado'"
  ok "$label → '$resultado' ✓"
}

# ════════════════════════════════════════════════════
# FASE 1 — Registro, login, wallet, recarga
# ════════════════════════════════════════════════════
phase "FASE 1: Registro, login, wallet, recarga"

info "POST /api/usuarios/registro-final (carlos)"
REGISTRO_A=$(post_json "$API_URL/api/usuarios/registro-final" '{
  "idUnidadNegocio": 1,
  "tipoDocumento": "CC",
  "numeroDocumento": "1099001234",
  "primerNombre": "Carlos",
  "primerApellido": "Gomez",
  "celular": "3001112233",
  "email": "carlos.gomez@ci-test.com",
  "usuario": "carlos_ci_test",
  "password": "Xpay@Test1!"
}') || fail "POST registro carlos no respondió"
echo "$REGISTRO_A" | jq .
assert_ok "$REGISTRO_A" "registro carlos"
ID_USUARIO_A=$(echo "$REGISTRO_A" | jq -r '.idUsuario')
ok "Registro carlos → idUsuario=$ID_USUARIO_A"

info "POST /api/auth/login (carlos) — captura token JWT"
LOGIN_A=$(post_json "$API_URL/api/auth/login" '{
  "usuario": "carlos_ci_test",
  "password": "Xpay@Test1!"
}') || fail "POST login carlos no respondió"
echo "$LOGIN_A" | jq .
assert_ok "$LOGIN_A" "login carlos"
ID_PERSONA_A=$(echo "$LOGIN_A" | jq -r '.data.idPersona')
TOKEN_A=$(echo "$LOGIN_A" | jq -r '.data.token')
[[ -n "$TOKEN_A" && "$TOKEN_A" != "null" ]] || fail "Token JWT vacío tras login de carlos"
ok "Login carlos → idPersona=$ID_PERSONA_A  token=${TOKEN_A:0:30}..."

info "GET /api/wallets/mi-wallet (carlos)"
WALLET_A=$(get_auth_json "$TOKEN_A" "$API_URL/api/wallets/mi-wallet") \
  || fail "GET mi-wallet carlos no respondió"
echo "$WALLET_A" | jq .
assert_ok "$WALLET_A" "wallet carlos"
ID_WALLET_A=$(echo "$WALLET_A" | jq -r '.data.idWallet')
ok "Wallet carlos → idWallet=$ID_WALLET_A"

info "GET /api/reportes/mi-estado-cuenta (carlos, inicial)"
SALDO_A_INICIAL=$(get_auth_json "$TOKEN_A" "$API_URL/api/reportes/mi-estado-cuenta") \
  || fail "GET mi-estado-cuenta carlos inicial no respondió"
assert_ok "$SALDO_A_INICIAL" "saldo carlos inicial"
assert_saldo "$SALDO_A_INICIAL" 0 "Saldo inicial carlos"

# Fase CI-WALLET-ADMIN-FIXTURE: recarga-manual quedó restringido a
# ADMIN_XPAY/SUPERUSUARIO (commit 13f4aa2, corrección de IDOR — antes
# cualquier autenticado podía recargar cualquier wallet arbitraria).
# carlos_ci_test (USUARIO_FINAL) ya no puede ejecutarlo — se usa el
# fixture ci_admin_xpay (scripts/ci/ci_admin_xpay_fixture.sql) solo
# para este paso puntual.
info "POST /api/auth/login (ci_admin_xpay) — fixture ADMIN_XPAY exclusivo de CI"
LOGIN_ADMIN=$(post_json "$API_URL/api/auth/login" '{
  "usuario": "ci_admin_xpay",
  "password": "CI-Fixture-AdminXpay#2026"
}') || fail "POST login ci_admin_xpay no respondió"
assert_ok "$LOGIN_ADMIN" "login ci_admin_xpay"
TOKEN_ADMIN=$(echo "$LOGIN_ADMIN" | jq -r '.data.token')
[[ -n "$TOKEN_ADMIN" && "$TOKEN_ADMIN" != "null" ]] || fail "Token JWT vacío tras login de ci_admin_xpay"
ok "Login ci_admin_xpay → token=${TOKEN_ADMIN:0:30}..."

info "POST /api/wallets/$ID_WALLET_A/recarga-manual con USUARIO_FINAL (carlos) → debe retornar 403"
STATUS_RECARGA_USUARIO_FINAL=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/wallets/$ID_WALLET_A/recarga-manual" \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN_A" \
  -d "{\"valor\": 100000, \"creadoPor\": $ID_USUARIO_A, \"observacion\": \"Recarga CI fase 1\"}")
[[ "$STATUS_RECARGA_USUARIO_FINAL" == "403" ]] \
  || fail "recarga-manual con USUARIO_FINAL esperado 403, obtenido $STATUS_RECARGA_USUARIO_FINAL"
ok "recarga-manual con USUARIO_FINAL (carlos) → 403 Forbidden ✓"

info "POST /api/wallets/$ID_WALLET_A/recarga-manual (100.000) con ADMIN_XPAY (ci_admin_xpay)"
RECARGA=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/wallets/$ID_WALLET_A/recarga-manual" \
  "{\"valor\": 100000, \"creadoPor\": $ID_USUARIO_A, \"observacion\": \"Recarga CI fase 1\"}") \
  || fail "POST recarga (ci_admin_xpay) no respondió"
echo "$RECARGA" | jq .
assert_ok "$RECARGA" "recarga carlos"
ok "Recarga carlos 100.000 (vía ci_admin_xpay) → OK"

info "GET /api/reportes/mi-estado-cuenta (carlos, tras recarga)"
SALDO_A_RECARGADO=$(get_auth_json "$TOKEN_A" "$API_URL/api/reportes/mi-estado-cuenta") \
  || fail "GET mi-estado-cuenta carlos post-recarga no respondió"
assert_saldo "$SALDO_A_RECARGADO" 100000 "Saldo carlos tras recarga"

# ════════════════════════════════════════════════════
# FASE 1.5 — KYC-GATING-001: usuario no verificado bloqueado,
# luego fixture de KYC aprobado SOLO en la BD efímera de este job de CI.
# No invoca Veriff, no genera decisión ni sesión real — es preparación de
# precondición para poder seguir probando los endpoints financieros
# protegidos por la policy KycAprobado en las fases siguientes.
# ════════════════════════════════════════════════════
phase "FASE 1.5: KYC Gating — bloqueo sin KYC, luego fixture aprobado (carlos_ci_test)"

info "POST /api/wallets/transferencia (carlos, SIN KYC aprobado) → debe retornar 403 KYC_REQUIRED"
RESP_KYC_NEG=$(post_auth_json_status "$TOKEN_A" "$API_URL/api/wallets/transferencia" \
  "{\"idWalletOrigen\": $ID_WALLET_A, \"idWalletDestino\": 999999999, \"valor\": 1000, \"creadoPor\": $ID_USUARIO_A, \"descripcion\": \"CI negative KYC test\"}")
STATUS_KYC_NEG=$(echo "$RESP_KYC_NEG" | tail -1)
BODY_KYC_NEG=$(echo "$RESP_KYC_NEG" | sed '$d')
[[ "$STATUS_KYC_NEG" == "403" ]] \
  || fail "KYC negativo (carlos sin aprobar) esperado 403, obtenido $STATUS_KYC_NEG ($BODY_KYC_NEG)"
echo "$BODY_KYC_NEG" | jq -e '.error == "KYC_REQUIRED"' > /dev/null \
  || fail "KYC negativo (carlos sin aprobar) esperado error=KYC_REQUIRED, obtenido $(echo "$BODY_KYC_NEG" | jq -r '.error // "null"')"
ok "POST transferencia (carlos, sin KYC) → 403 KYC_REQUIRED ✓"

# Fixture CI: aprueba KYC de carlos_ci_test directamente en la BD efímera de
# este job (localhost,1433, destruida al final del run — nunca QA/producción).
# No inserta filas en kyc_verificaciones: no existe constraint/trigger que lo
# requiera (verificado: sin CREATE TRIGGER en database/*.sql, columnas
# estado_kyc_actual/identidad_verificada solo tienen DEFAULT, sin CHECK). Solo
# ajusta las dos columnas que exige la condición canónica del guard —
# equivalente a cualquier otro fixture sintético ya usado en este script
# (p.ej. ci_admin_xpay), no una emulación de Veriff.
info "Fixture CI: aprobar KYC de carlos_ci_test en la BD efímera (sin Veriff, sin kyc_verificaciones)"
# CI-SQLCMD-CAPTURE-FIX-001: sqlcmd clásico imprime los errores de batch SQL
# (Msg N, Level X) por stdout, no por stderr — un `> /dev/null` los silencia
# por completo, dejando solo el mensaje genérico de fail() sin ninguna pista
# real. Se captura stdout+stderr (2>&1) y se incluye en fail() si falla,
# redactando SA_PASS de la salida como defensa adicional (sqlcmd no suele
# ecoar la contraseña en sus mensajes de error, pero no se asume).
SQL_KYC_FIXTURE_OUTPUT=$("$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C -Q "
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
UPDATE usuarios SET estado_kyc_actual = 'APROBADO' WHERE id_usuario = $ID_USUARIO_A;
UPDATE personas SET identidad_verificada = 1 WHERE id_persona = $ID_PERSONA_A;
" 2>&1) || fail "No se pudo preparar el fixture KYC aprobado (carlos_ci_test). Salida: ${SQL_KYC_FIXTURE_OUTPUT//$SA_PASS/***}"

check_sql_value \
  "usuarios.estado_kyc_actual tras fixture (carlos_ci_test)" \
  "SELECT estado_kyc_actual FROM usuarios WHERE id_usuario = $ID_USUARIO_A" \
  "APROBADO"
check_sql_value \
  "personas.identidad_verificada tras fixture (carlos_ci_test)" \
  "SELECT CAST(identidad_verificada AS INT) FROM personas WHERE id_persona = $ID_PERSONA_A" \
  "1"

# ════════════════════════════════════════════════════
# FASE 2 — Transferencias XPAY a XPAY
# ════════════════════════════════════════════════════
phase "FASE 2: Transferencias XPAY a XPAY"

info "POST /api/usuarios/registro-final (maria)"
REGISTRO_B=$(post_json "$API_URL/api/usuarios/registro-final" '{
  "idUnidadNegocio": 1,
  "tipoDocumento": "CC",
  "numeroDocumento": "2088005678",
  "primerNombre": "Maria",
  "primerApellido": "Lopez",
  "celular": "3119998877",
  "email": "maria.lopez@ci-test.com",
  "usuario": "maria_ci_test",
  "password": "Xpay@Test2!"
}') || fail "POST registro maria no respondió"
echo "$REGISTRO_B" | jq .
assert_ok "$REGISTRO_B" "registro maria"
ID_USUARIO_B=$(echo "$REGISTRO_B" | jq -r '.idUsuario')
ok "Registro maria → idUsuario=$ID_USUARIO_B"

info "POST /api/auth/login (maria)"
LOGIN_B=$(post_json "$API_URL/api/auth/login" '{
  "usuario": "maria_ci_test",
  "password": "Xpay@Test2!"
}') || fail "POST login maria no respondió"
echo "$LOGIN_B" | jq .
assert_ok "$LOGIN_B" "login maria"
ID_PERSONA_B=$(echo "$LOGIN_B" | jq -r '.data.idPersona')
TOKEN_B=$(echo "$LOGIN_B" | jq -r '.data.token')
[[ -n "$TOKEN_B" && "$TOKEN_B" != "null" ]] || fail "Token JWT vacío tras login de maria"
ok "Login maria → idPersona=$ID_PERSONA_B  token=${TOKEN_B:0:30}..."

# Fase 71.2-E-B: GET /api/wallets/persona/{idPersona} quedó restringido a
# ADMIN_XPAY/SUPERUSUARIO (era el IDOR original — cualquier autenticado podía
# consultar la wallet de cualquier persona). Este script no autentica ningún
# usuario admin, así que maria debe consultar su propia wallet con su propio
# token — nunca con el token de carlos.
info "GET /api/wallets/mi-wallet (maria)"
WALLET_B=$(get_auth_json "$TOKEN_B" "$API_URL/api/wallets/mi-wallet") \
  || fail "GET mi-wallet maria no respondió"
echo "$WALLET_B" | jq .
assert_ok "$WALLET_B" "wallet maria"
ID_WALLET_B=$(echo "$WALLET_B" | jq -r '.data.idWallet')
ok "Wallet maria → idWallet=$ID_WALLET_B"

# Fase CI-WALLET-IDEMPOTENCY-HEADER: /api/wallets/transferencia exige el
# header Idempotency-Key (Fase 71.2-E-G, commit 13f4aa2) — GUID nuevo e
# independiente por operación lógica, nunca reutilizado entre llamadas.
IDEMPOTENCY_KEY_TRANSFERENCIA=$(python3 -c 'import uuid; print(uuid.uuid4())')
info "POST /api/wallets/transferencia (25.000)"
TRANSFERENCIA=$(post_auth_json "$TOKEN_A" "$API_URL/api/wallets/transferencia" \
  "{\"idWalletOrigen\": $ID_WALLET_A, \"idWalletDestino\": $ID_WALLET_B, \"valor\": 25000, \"creadoPor\": $ID_USUARIO_A, \"descripcion\": \"Transferencia CI fase 2\"}" \
  "$IDEMPOTENCY_KEY_TRANSFERENCIA") \
  || fail "POST transferencia no respondió"
echo "$TRANSFERENCIA" | jq .
assert_ok "$TRANSFERENCIA" "transferencia"
ID_TRANSACCION_T=$(echo "$TRANSFERENCIA" | jq -r '.data.idTransaccion')
ok "Transferencia → idTransaccion=$ID_TRANSACCION_T"

info "GET /api/reportes/mi-estado-cuenta (carlos, tras transferencia)"
SALDO_A_POST_T=$(get_auth_json "$TOKEN_A" "$API_URL/api/reportes/mi-estado-cuenta") \
  || fail "GET mi-estado-cuenta carlos tras transferencia no respondió"
assert_saldo "$SALDO_A_POST_T" 75000 "Saldo carlos tras transferencia"

info "GET /api/reportes/mi-estado-cuenta (maria, tras transferencia)"
SALDO_B_POST_T=$(get_auth_json "$TOKEN_B" "$API_URL/api/reportes/mi-estado-cuenta") \
  || fail "GET mi-estado-cuenta maria tras transferencia no respondió"
assert_saldo "$SALDO_B_POST_T" 25000 "Saldo maria tras transferencia"

# ════════════════════════════════════════════════════
# FASE 3 — Pago a comercio por QR
# ════════════════════════════════════════════════════
phase "FASE 3: Pago a comercio por QR"

# Fase CI-WALLET-IDEMPOTENCY-HEADER: /api/qr/pagar también exige
# Idempotency-Key (mismo helper duplicado en QrController) — GUID propio,
# distinto del usado en la transferencia de FASE 2.
IDEMPOTENCY_KEY_PAGAR_QR=$(python3 -c 'import uuid; print(uuid.uuid4())')
info "POST /api/qr/pagar (30.000)"
PAGO_QR=$(post_auth_json "$TOKEN_A" "$API_URL/api/qr/pagar" \
  "{\"codigoQr\": \"QR-DEMO-XPAY-001\", \"idWalletUsuario\": $ID_WALLET_A, \"valor\": 30000, \"creadoPor\": $ID_USUARIO_A, \"descripcion\": \"Pago QR CI fase 3\"}" \
  "$IDEMPOTENCY_KEY_PAGAR_QR") \
  || fail "POST /api/qr/pagar no respondió"
echo "$PAGO_QR" | jq .
assert_ok "$PAGO_QR" "pago QR"

ID_VENTA_QR=$(echo "$PAGO_QR"      | jq -r '.data.idVentaQr')
ID_TRANSACCION_Q=$(echo "$PAGO_QR" | jq -r '.data.idTransaccion')
ESTADO_QR=$(echo "$PAGO_QR"        | jq -r '.data.estado')
[[ "$ESTADO_QR" == "CONTINGENCIA" ]] || fail "estado esperado CONTINGENCIA, obtenido $ESTADO_QR"
ok "Pago QR → idVentaQr=$ID_VENTA_QR  estado=$ESTADO_QR"

info "GET /api/reportes/mi-estado-cuenta (carlos, tras pago QR)"
SALDO_A_POST_QR=$(get_auth_json "$TOKEN_A" "$API_URL/api/reportes/mi-estado-cuenta") \
  || fail "GET mi-estado-cuenta carlos tras pago QR no respondió"
assert_saldo "$SALDO_A_POST_QR" 45000 "Saldo carlos tras pago QR"

check_sql_value \
  "ventas_qr.estado = CONTINGENCIA antes de liquidar" \
  "SELECT TOP 1 estado FROM ventas_qr ORDER BY id_venta_qr DESC" \
  "CONTINGENCIA"

# ════════════════════════════════════════════════════
# FASE 4 — Liquidación de ventas QR al comercio
# ════════════════════════════════════════════════════
phase "FASE 4: Liquidación de ventas QR al comercio"

info "POST /api/comercios/liquidar-venta-qr (idVentaQr=$ID_VENTA_QR)"
LIQUIDACION=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/comercios/liquidar-venta-qr" \
  "{\"idVentaQr\": $ID_VENTA_QR, \"creadoPor\": $ID_USUARIO_A, \"observacion\": \"Liquidacion CI fase 4\"}") \
  || fail "POST liquidar-venta-qr no respondió"
echo "$LIQUIDACION" | jq .
assert_ok "$LIQUIDACION" "liquidacion QR"

ID_COMERCIO=$(echo "$LIQUIDACION"        | jq -r '.data.idComercio')
ID_WALLET_COMERCIO=$(echo "$LIQUIDACION" | jq -r '.data.idWalletComercio')
ESTADO_VENTA_LIQ=$(echo "$LIQUIDACION"  | jq -r '.data.estadoVenta')
[[ "$ESTADO_VENTA_LIQ" == "LIQUIDADA" ]] || fail "estadoVenta esperado LIQUIDADA, obtenido $ESTADO_VENTA_LIQ"
ok "Liquidación → idComercio=$ID_COMERCIO  idWalletComercio=$ID_WALLET_COMERCIO  estadoVenta=$ESTADO_VENTA_LIQ"

info "Doble liquidación debe retornar error"
DOBLE_LIQ=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/comercios/liquidar-venta-qr" \
  "{\"idVentaQr\": $ID_VENTA_QR, \"creadoPor\": $ID_USUARIO_A}") || true
[[ "$(echo "$DOBLE_LIQ" | jq -r '.success' 2>/dev/null || echo false)" != "true" ]] \
  || fail "La doble liquidación debió fallar"
ok "Doble liquidación rechazada ✓"

# ════════════════════════════════════════════════════
# FASE 5 — Solicitud de retiro del comercio
# ════════════════════════════════════════════════════
phase "FASE 5: Solicitud de retiro del comercio"

check_sql_value \
  "Saldo wallet comercio antes de retiro = 30000" \
  "SELECT CAST(CAST(ws.saldo_disponible AS BIGINT) AS NVARCHAR(50)) FROM wallet_saldos ws WHERE ws.id_wallet = $ID_WALLET_COMERCIO" \
  "30000"

info "POST /api/comercios/solicitar-retiro (idComercio=$ID_COMERCIO, valor=20000)"
RETIRO=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/comercios/solicitar-retiro" \
  "{\"idComercio\": $ID_COMERCIO, \"valor\": 20000, \"medioRetiro\": \"TRANSFERENCIA_BANCARIA\", \"banco\": \"Banco Demo\", \"tipoCuenta\": \"AHORROS\", \"numeroCuenta\": \"1234567890\", \"titularCuenta\": \"Comercio Demo XPAY\", \"documentoTitular\": \"900123456\", \"observacion\": \"Retiro CI fase 5\", \"creadoPor\": $ID_USUARIO_A}") \
  || fail "POST solicitar-retiro no respondió"
echo "$RETIRO" | jq .
assert_ok "$RETIRO" "solicitar retiro"

ID_RETIRO=$(echo "$RETIRO"     | jq -r '.data.idRetiro')
ESTADO_RETIRO=$(echo "$RETIRO" | jq -r '.data.estado')
[[ "$ESTADO_RETIRO" == "PENDIENTE" ]] || fail "estado esperado PENDIENTE, obtenido $ESTADO_RETIRO"
ok "Retiro → idRetiro=$ID_RETIRO  valor=20000  estado=$ESTADO_RETIRO"

info "GET /api/comercios/retiros/$ID_RETIRO → debe retornar estado=PENDIENTE"
RETIRO_GET=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/comercios/retiros/$ID_RETIRO") \
  || fail "GET retiros/$ID_RETIRO no respondió"
echo "$RETIRO_GET" | jq .
assert_ok "$RETIRO_GET" "GET retiro por ID"
RETIRO_GET_ESTADO=$(echo "$RETIRO_GET" | jq -r '.data.estado')
[[ "$RETIRO_GET_ESTADO" == "PENDIENTE" ]] \
  || fail "GET retiro estado esperado PENDIENTE, obtenido $RETIRO_GET_ESTADO"
ok "GET /api/comercios/retiros/$ID_RETIRO → estado=$RETIRO_GET_ESTADO ✓"

info "GET /api/comercios/retiros/0 → debe retornar 400 (ID inválido)"
STATUS_RETIRO_INVALIDO=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -H "Authorization: Bearer $TOKEN_ADMIN" \
  "$API_URL/api/comercios/retiros/0")
[[ "$STATUS_RETIRO_INVALIDO" == "400" ]] \
  || fail "GET retiros/0 esperado 400, obtenido $STATUS_RETIRO_INVALIDO"
ok "GET retiros/0 → 400 (ID inválido) ✓"

info "GET /api/comercios/retiros/99999 → debe retornar 400 (no existe)"
STATUS_RETIRO_NO_EXISTE=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -H "Authorization: Bearer $TOKEN_ADMIN" \
  "$API_URL/api/comercios/retiros/99999")
[[ "$STATUS_RETIRO_NO_EXISTE" == "400" ]] \
  || fail "GET retiros/99999 esperado 400, obtenido $STATUS_RETIRO_NO_EXISTE"
ok "GET retiros/99999 → 400 (no existe) ✓"

info "GET /api/comercios/retiros/$ID_RETIRO sin token → debe retornar 401"
STATUS_RETIRO_NO_AUTH=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/api/comercios/retiros/$ID_RETIRO")
[[ "$STATUS_RETIRO_NO_AUTH" == "401" ]] \
  || fail "GET retiros sin token esperado 401, obtenido $STATUS_RETIRO_NO_AUTH"
ok "GET retiros sin token → 401 ✓"

info "GET /api/comercios/retiros (listado) → debe retornar success=true y total >= 1"
RETIROS_LIST=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/comercios/retiros") \
  || fail "GET /api/comercios/retiros (listado) no respondió"
echo "$RETIROS_LIST" | jq .
assert_ok "$RETIROS_LIST" "GET listado de retiros"
RETIROS_TOTAL=$(echo "$RETIROS_LIST" | jq -r '.data.total')
[[ "$RETIROS_TOTAL" -ge 1 ]] \
  || fail "Listado retiros: total esperado >= 1, obtenido $RETIROS_TOTAL"
RETIROS_ITEMS=$(echo "$RETIROS_LIST" | jq '.data.items | length')
[[ "$RETIROS_ITEMS" -ge 1 ]] \
  || fail "Listado retiros: items esperado >= 1, obtenido $RETIROS_ITEMS"
ok "GET /api/comercios/retiros → total=$RETIROS_TOTAL  items=$RETIROS_ITEMS ✓"

info "GET /api/comercios/retiros?estado=PENDIENTE → debe traer al menos 1"
RETIROS_PEND=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/comercios/retiros?estado=PENDIENTE") \
  || fail "GET retiros?estado=PENDIENTE no respondió"
echo "$RETIROS_PEND" | jq .
assert_ok "$RETIROS_PEND" "GET listado retiros PENDIENTE"
RETIROS_PEND_ITEMS=$(echo "$RETIROS_PEND" | jq '.data.items | length')
[[ "$RETIROS_PEND_ITEMS" -ge 1 ]] \
  || fail "Listado PENDIENTE: items esperado >= 1, obtenido $RETIROS_PEND_ITEMS"
ok "GET retiros?estado=PENDIENTE → items=$RETIROS_PEND_ITEMS ✓"

info "GET /api/comercios/retiros sin token → debe retornar 401"
STATUS_LISTA_NO_AUTH=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/api/comercios/retiros")
[[ "$STATUS_LISTA_NO_AUTH" == "401" ]] \
  || fail "GET listado retiros sin token esperado 401, obtenido $STATUS_LISTA_NO_AUTH"
ok "GET /api/comercios/retiros sin token → 401 ✓"

info "Retiro con saldo insuficiente debe retornar error"
RETIRO_INVALIDO=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/comercios/solicitar-retiro" \
  "{\"idComercio\": $ID_COMERCIO, \"valor\": 99999, \"creadoPor\": $ID_USUARIO_A}") || true
[[ "$(echo "$RETIRO_INVALIDO" | jq -r '.success' 2>/dev/null || echo false)" != "true" ]] \
  || fail "El retiro con saldo insuficiente debió fallar"
ok "Retiro con saldo insuficiente rechazado ✓"

# ════════════════════════════════════════════════════
# FASE 6 — Gestión de retiros: confirmar pago y rechazar
# ════════════════════════════════════════════════════
phase "FASE 6: Gestión de retiros del comercio"

info "POST /api/comercios/retiros/confirmar-pago (idRetiro=$ID_RETIRO)"
CONFIRMAR=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/comercios/retiros/confirmar-pago" \
  "{\"idRetiro\": $ID_RETIRO, \"referenciaPago\": \"PAGO-MANUAL-CI-001\", \"observacion\": \"Pago manual CI fase 6\", \"creadoPor\": $ID_USUARIO_A}") \
  || fail "POST confirmar-pago no respondió"
echo "$CONFIRMAR" | jq .
assert_ok "$CONFIRMAR" "confirmar pago"

ESTADO_PAGADO=$(echo "$CONFIRMAR" | jq -r '.data.estado')
[[ "$ESTADO_PAGADO" == "PAGADO" ]] || fail "estado esperado PAGADO, obtenido $ESTADO_PAGADO"
ok "Retiro confirmado como PAGADO → idRetiro=$ID_RETIRO  estado=$ESTADO_PAGADO"

info "GET /api/comercios/retiros/$ID_RETIRO tras confirmación → debe reflejar estado=PAGADO"
RETIRO_GET_PAGADO=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/comercios/retiros/$ID_RETIRO") \
  || fail "GET retiros/$ID_RETIRO tras confirmación no respondió"
echo "$RETIRO_GET_PAGADO" | jq .
assert_ok "$RETIRO_GET_PAGADO" "GET retiro pagado por ID"
RETIRO_GET_ESTADO_PAGADO=$(echo "$RETIRO_GET_PAGADO" | jq -r '.data.estado')
[[ "$RETIRO_GET_ESTADO_PAGADO" == "PAGADO" ]] \
  || fail "GET retiro estado esperado PAGADO, obtenido $RETIRO_GET_ESTADO_PAGADO"
RETIRO_GET_REF=$(echo "$RETIRO_GET_PAGADO" | jq -r '.data.referenciaPago // empty')
ok "GET retiro/$ID_RETIRO → estado=$RETIRO_GET_ESTADO_PAGADO  referenciaPago=${RETIRO_GET_REF:-—} ✓"

info "GET /api/comercios/retiros?estado=PAGADO → debe traer al menos 1"
RETIROS_PAG=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/comercios/retiros?estado=PAGADO") \
  || fail "GET retiros?estado=PAGADO no respondió"
echo "$RETIROS_PAG" | jq .
assert_ok "$RETIROS_PAG" "GET listado retiros PAGADO"
RETIROS_PAG_ITEMS=$(echo "$RETIROS_PAG" | jq '.data.items | length')
[[ "$RETIROS_PAG_ITEMS" -ge 1 ]] \
  || fail "Listado PAGADO: items esperado >= 1, obtenido $RETIROS_PAG_ITEMS"
ok "GET retiros?estado=PAGADO → items=$RETIROS_PAG_ITEMS ✓"

info "Doble confirmación debe retornar error"
DOBLE_CONF=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/comercios/retiros/confirmar-pago" \
  "{\"idRetiro\": $ID_RETIRO, \"creadoPor\": $ID_USUARIO_A}") || true
[[ "$(echo "$DOBLE_CONF" | jq -r '.success' 2>/dev/null || echo false)" != "true" ]] \
  || fail "La doble confirmación debió fallar"
ok "Doble confirmación rechazada ✓"

check_sql_value \
  "Saldo wallet comercio tras confirmar pago = 10000" \
  "SELECT CAST(CAST(ws.saldo_disponible AS BIGINT) AS NVARCHAR(50)) FROM wallet_saldos ws WHERE ws.id_wallet = $ID_WALLET_COMERCIO" \
  "10000"

check_sql_value \
  "retiro PAGADO tiene fecha_pago no nula" \
  "SELECT CASE WHEN fecha_pago IS NOT NULL THEN 'OK' ELSE 'NULL' END FROM retiros_comercio WHERE id_retiro = $ID_RETIRO" \
  "OK"

check_sql_value \
  "retiro PAGADO tiene referencia_pago correcta" \
  "SELECT referencia_pago FROM retiros_comercio WHERE id_retiro = $ID_RETIRO" \
  "PAGO-MANUAL-CI-001"

info "POST /api/comercios/solicitar-retiro (segundo retiro: valor=5000)"
RETIRO_2=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/comercios/solicitar-retiro" \
  "{\"idComercio\": $ID_COMERCIO, \"valor\": 5000, \"medioRetiro\": \"TRANSFERENCIA_BANCARIA\", \"observacion\": \"Segundo retiro CI fase 6\", \"creadoPor\": $ID_USUARIO_A}") \
  || fail "POST solicitar-retiro (segundo) no respondió"
echo "$RETIRO_2" | jq .
assert_ok "$RETIRO_2" "segundo retiro"

ID_RETIRO_2=$(echo "$RETIRO_2"     | jq -r '.data.idRetiro')
ESTADO_RETIRO_2=$(echo "$RETIRO_2" | jq -r '.data.estado')
[[ "$ESTADO_RETIRO_2" == "PENDIENTE" ]] || fail "estado esperado PENDIENTE, obtenido $ESTADO_RETIRO_2"
ok "Segundo retiro → idRetiro=$ID_RETIRO_2  valor=5000  estado=$ESTADO_RETIRO_2"

check_sql_value \
  "Saldo wallet comercio tras segundo retiro = 5000" \
  "SELECT CAST(CAST(ws.saldo_disponible AS BIGINT) AS NVARCHAR(50)) FROM wallet_saldos ws WHERE ws.id_wallet = $ID_WALLET_COMERCIO" \
  "5000"

info "POST /api/comercios/retiros/rechazar (idRetiro=$ID_RETIRO_2)"
RECHAZO=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/comercios/retiros/rechazar" \
  "{\"idRetiro\": $ID_RETIRO_2, \"motivoRechazo\": \"Cuenta bancaria inválida\", \"observacion\": \"Rechazo CI fase 6\", \"creadoPor\": $ID_USUARIO_A}") \
  || fail "POST rechazar no respondió"
echo "$RECHAZO" | jq .
assert_ok "$RECHAZO" "rechazar retiro"

ESTADO_RECHAZADO=$(echo "$RECHAZO" | jq -r '.data.estado')
[[ "$ESTADO_RECHAZADO" == "RECHAZADO" ]] || fail "estado esperado RECHAZADO, obtenido $ESTADO_RECHAZADO"
ok "Retiro rechazado → idRetiro=$ID_RETIRO_2  estado=$ESTADO_RECHAZADO"

info "Doble rechazo debe retornar error"
DOBLE_RECH=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/comercios/retiros/rechazar" \
  "{\"idRetiro\": $ID_RETIRO_2, \"creadoPor\": $ID_USUARIO_A}") || true
[[ "$(echo "$DOBLE_RECH" | jq -r '.success' 2>/dev/null || echo false)" != "true" ]] \
  || fail "El doble rechazo debió fallar"
ok "Doble rechazo rechazado ✓"

info "GET /api/comercios/retiros?estado=RECHAZADO → debe traer al menos 1"
RETIROS_RECH=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/comercios/retiros?estado=RECHAZADO") \
  || fail "GET retiros?estado=RECHAZADO no respondió"
echo "$RETIROS_RECH" | jq .
assert_ok "$RETIROS_RECH" "GET listado retiros RECHAZADO"
RETIROS_RECH_ITEMS=$(echo "$RETIROS_RECH" | jq '.data.items | length')
[[ "$RETIROS_RECH_ITEMS" -ge 1 ]] \
  || fail "Listado RECHAZADO: items esperado >= 1, obtenido $RETIROS_RECH_ITEMS"
ok "GET retiros?estado=RECHAZADO → items=$RETIROS_RECH_ITEMS ✓"

check_sql_value \
  "Saldo wallet comercio tras rechazo = 10000 (restaurado)" \
  "SELECT CAST(CAST(ws.saldo_disponible AS BIGINT) AS NVARCHAR(50)) FROM wallet_saldos ws WHERE ws.id_wallet = $ID_WALLET_COMERCIO" \
  "10000"

# ════════════════════════════════════════════════════
# Validaciones SQL — acumulado Fases 1 a 6
# ════════════════════════════════════════════════════
phase "Validaciones SQL — acumulado Fases 1 a 6"

check_count "wallet_saldos"                3
check_count "wallet_movimientos"           8
check_count "ledger_transacciones"         8
check_count "ledger_movimientos"          16
check_count "auditoria"                   10
check_count "ventas_qr"                    1
check_count "liquidaciones_comercio"       1
check_count "liquidacion_comercio_detalle" 1
check_count "retiros_comercio"             2

check_sql_value \
  "Primer retiro estado = PAGADO" \
  "SELECT estado FROM retiros_comercio WHERE id_retiro = $ID_RETIRO" \
  "PAGADO"

check_sql_value \
  "Segundo retiro estado = RECHAZADO" \
  "SELECT estado FROM retiros_comercio WHERE id_retiro = $ID_RETIRO_2" \
  "RECHAZADO"

check_sql_value \
  "Saldo final wallet comercio = 10000" \
  "SELECT CAST(CAST(ws.saldo_disponible AS BIGINT) AS NVARCHAR(50)) FROM wallet_saldos ws WHERE ws.id_wallet = $ID_WALLET_COMERCIO" \
  "10000"

check_sql_value \
  "Ledger PAGO_QR balanceado" \
  "SELECT CASE WHEN SUM(CASE WHEN naturaleza='D' THEN valor ELSE 0 END) = SUM(CASE WHEN naturaleza='C' THEN valor ELSE 0 END) THEN 'OK' ELSE 'DESBALANCEADO' END FROM ledger_movimientos WHERE id_transaccion_ledger = $ID_TRANSACCION_Q" \
  "OK"

check_sql_value \
  "Ledger RETIRO_COMERCIO_PAGADO balanceado" \
  "SELECT CASE WHEN SUM(CASE WHEN lm.naturaleza='D' THEN lm.valor ELSE 0 END) = SUM(CASE WHEN lm.naturaleza='C' THEN lm.valor ELSE 0 END) THEN 'OK' ELSE 'DESBALANCEADO' END FROM ledger_movimientos lm INNER JOIN ledger_transacciones lt ON lm.id_transaccion_ledger = lt.id_transaccion_ledger WHERE lt.tipo_transaccion = 'RETIRO_COMERCIO_PAGADO'" \
  "OK"

check_sql_value \
  "Ledger RETIRO_COMERCIO_RECHAZADO balanceado" \
  "SELECT CASE WHEN SUM(CASE WHEN lm.naturaleza='D' THEN lm.valor ELSE 0 END) = SUM(CASE WHEN lm.naturaleza='C' THEN lm.valor ELSE 0 END) THEN 'OK' ELSE 'DESBALANCEADO' END FROM ledger_movimientos lm INNER JOIN ledger_transacciones lt ON lm.id_transaccion_ledger = lt.id_transaccion_ledger WHERE lt.tipo_transaccion = 'RETIRO_COMERCIO_RECHAZADO'" \
  "OK"

# ════════════════════════════════════════════════════
# FASE 7 — Consultas y reportes transaccionales
# ════════════════════════════════════════════════════
phase "FASE 7: Consultas y reportes transaccionales"

info "GET /api/reportes/mi-estado-cuenta (carlos)"
EC_USUARIO=$(get_auth_json "$TOKEN_A" "$API_URL/api/reportes/mi-estado-cuenta") \
  || fail "GET mi-estado-cuenta usuario no respondió"
echo "$EC_USUARIO" | jq .
assert_ok "$EC_USUARIO" "estado-cuenta wallet usuario"
assert_saldo "$EC_USUARIO" 45000 "Estado cuenta carlos"

echo "$EC_USUARIO" | jq -e '.data.movimientos | length >= 3' > /dev/null \
  || fail "movimientos wallet usuario esperado >=3, obtenido $(echo "$EC_USUARIO" | jq '.data.movimientos | length')"
ok "Estado cuenta carlos → movimientos=$(echo "$EC_USUARIO" | jq '.data.movimientos | length') (>=3) ✓"

# Fase 71.2-E-B: los siguientes 4 endpoints son administrativos
# ([Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO"[,"OPERADOR_XPAY"])]) y no
# tienen equivalente self-service para USUARIO_FINAL (no consultan un
# wallet/comercio/transacción propios, sino datos de terceros o agregados
# cross-usuario). Este script no autentica ningún usuario con rol
# ADMIN_XPAY/SUPERUSUARIO/OPERADOR_XPAY, así que aquí solo se valida el
# bloqueo por rol (403 con token válido de USUARIO_FINAL). La prueba
# funcional positiva de estos reportes administrativos queda pendiente de
# contar con un fixture o usuario CI con rol ADMIN_XPAY/SUPERUSUARIO.

info "GET /api/reportes/wallet/$ID_WALLET_COMERCIO/estado-cuenta (USUARIO_FINAL) → 403"
STATUS_EC_COMERCIO=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -H "Authorization: Bearer $TOKEN_A" \
  "$API_URL/api/reportes/wallet/$ID_WALLET_COMERCIO/estado-cuenta")
[[ "$STATUS_EC_COMERCIO" == "403" ]] \
  || fail "estado-cuenta wallet comercio con USUARIO_FINAL esperado 403, obtenido $STATUS_EC_COMERCIO"
ok "GET estado-cuenta wallet comercio con USUARIO_FINAL → 403 ✓"

info "GET /api/reportes/comercios/$ID_COMERCIO/resumen (USUARIO_FINAL) → 403"
STATUS_RESUMEN_COMERCIO=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -H "Authorization: Bearer $TOKEN_A" \
  "$API_URL/api/reportes/comercios/$ID_COMERCIO/resumen")
[[ "$STATUS_RESUMEN_COMERCIO" == "403" ]] \
  || fail "resumen comercio con USUARIO_FINAL esperado 403, obtenido $STATUS_RESUMEN_COMERCIO"
ok "GET resumen comercio con USUARIO_FINAL → 403 ✓"

info "GET /api/reportes/ledger/transaccion/$ID_TRANSACCION_Q (USUARIO_FINAL) → 403"
STATUS_LEDGER_TX=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -H "Authorization: Bearer $TOKEN_A" \
  "$API_URL/api/reportes/ledger/transaccion/$ID_TRANSACCION_Q")
[[ "$STATUS_LEDGER_TX" == "403" ]] \
  || fail "ledger transaccion QR con USUARIO_FINAL esperado 403, obtenido $STATUS_LEDGER_TX"
ok "GET ledger transaccion QR con USUARIO_FINAL → 403 ✓"

info "GET /api/reportes/operaciones/resumen-general (USUARIO_FINAL) → 403"
STATUS_RESUMEN_GEN=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -H "Authorization: Bearer $TOKEN_A" \
  "$API_URL/api/reportes/operaciones/resumen-general")
[[ "$STATUS_RESUMEN_GEN" == "403" ]] \
  || fail "resumen-general con USUARIO_FINAL esperado 403, obtenido $STATUS_RESUMEN_GEN"
ok "GET resumen-general con USUARIO_FINAL → 403 ✓"

# ════════════════════════════════════════════════════
# FASE 8 — Seguridad JWT: validaciones de autenticación
# ════════════════════════════════════════════════════
phase "FASE 8: Seguridad JWT — autenticación y protección de endpoints"

# 8.1 Login devuelve token no vacío (ya validado en Fase 1, aquí es afirmación explícita)
[[ -n "$TOKEN_A" && "$TOKEN_A" != "null" ]] || fail "Token JWT de carlos debe ser no vacío"
ok "Login devuelve token JWT no vacío ✓"

# 8.2 Endpoint protegido sin token → 401
info "GET /api/wallets/mi-wallet sin token → debe retornar 401"
STATUS_NO_TOKEN=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/api/wallets/mi-wallet")
[[ "$STATUS_NO_TOKEN" == "401" ]] || fail "Sin token esperado 401, obtenido $STATUS_NO_TOKEN"
ok "Sin token → 401 Unauthorized ✓"

# 8.3 Mismo endpoint con token → 200 success=true
info "GET /api/wallets/mi-wallet con token → debe retornar 200"
RESP_CON_TOKEN=$(get_auth_json "$TOKEN_A" "$API_URL/api/wallets/mi-wallet") \
  || fail "GET mi-wallet con token no respondió"
assert_ok "$RESP_CON_TOKEN" "wallet con token valido"
ok "Con token → 200 success=true ✓"

# 8.4 Token con firma inválida → 401
info "Token con firma inválida → debe retornar 401"
TOKEN_INVALIDO="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkZha2UiLCJpYXQiOjE1MTYyMzkwMjJ9.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c"
STATUS_TOKEN_FAKE=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -H "Authorization: Bearer $TOKEN_INVALIDO" \
  "$API_URL/api/wallets/mi-wallet")
[[ "$STATUS_TOKEN_FAKE" == "401" ]] || fail "Token inválido esperado 401, obtenido $STATUS_TOKEN_FAKE"
ok "Token con firma inválida → 401 Unauthorized ✓"

# 8.5 Endpoint público sigue siendo accesible sin token
info "POST /api/auth/login (público) sigue accesible sin token"
TEST_LOGIN=$(post_json "$API_URL/api/auth/login" '{
  "usuario": "carlos_ci_test",
  "password": "Xpay@Test1!"
}') || fail "Login público no respondió"
assert_ok "$TEST_LOGIN" "login público accesible sin token"
ok "Endpoints públicos siguen sin requerir token ✓"

echo ""

# ════════════════════════════════════════════════════
# FASE 9 — Health check, versión API y Swagger con JWT
# ════════════════════════════════════════════════════
phase "FASE 9: Health check, versión API y Swagger JWT"

# 9.1 GET /health sin token → 200 + status=Healthy
info "GET /health sin token → debe retornar 200"
STATUS_HEALTH=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/health")
[[ "$STATUS_HEALTH" == "200" ]] || fail "GET /health esperado 200, obtenido $STATUS_HEALTH"
BODY_HEALTH=$(get_json "$API_URL/health") || fail "GET /health no respondió"
echo "$BODY_HEALTH" | jq .
HEALTH_STATUS=$(echo "$BODY_HEALTH" | jq -r '.status')
[[ "$HEALTH_STATUS" == "Healthy" ]] || fail "/health → status esperado 'Healthy', obtenido '$HEALTH_STATUS'"
ok "GET /health → 200 / status=Healthy ✓"

# 9.2 GET /api/version sin token → 200 + success=true + version no vacía
info "GET /api/version sin token → debe retornar 200"
STATUS_VERSION=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/api/version")
[[ "$STATUS_VERSION" == "200" ]] || fail "GET /api/version esperado 200, obtenido $STATUS_VERSION"
BODY_VERSION=$(get_json "$API_URL/api/version") || fail "GET /api/version no respondió"
echo "$BODY_VERSION" | jq .
assert_ok "$BODY_VERSION" "GET /api/version"
API_VERSION=$(echo "$BODY_VERSION" | jq -r '.data.version')
[[ -n "$API_VERSION" && "$API_VERSION" != "null" ]] \
  || fail "/api/version → campo version vacío"
ok "GET /api/version → 200 / success=true / version=$API_VERSION ✓"

# 9.3 /health no requiere token (ya probado sin él; validar que protegido tampoco lo exige)
STATUS_HEALTH_NOAUTH=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/health")
[[ "$STATUS_HEALTH_NOAUTH" == "200" ]] \
  || fail "/health sin token esperado 200 (no debe exigir JWT), obtenido $STATUS_HEALTH_NOAUTH"
ok "/health es público (no requiere JWT) ✓"

# 9.4 /api/version no requiere token
STATUS_VERSION_NOAUTH=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/api/version")
[[ "$STATUS_VERSION_NOAUTH" == "200" ]] \
  || fail "/api/version sin token esperado 200 (no debe exigir JWT), obtenido $STATUS_VERSION_NOAUTH"
ok "/api/version es público (no requiere JWT) ✓"

# 9.5 GET /swagger/index.html → 200 (Swagger habilitado en CI/QA vía ApiDocs:EnableSwagger=true)
info "GET /swagger/index.html → debe retornar 200 (Swagger esperado habilitado en CI/QA)"
STATUS_SWAGGER_UI=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/swagger/index.html")
[[ "$STATUS_SWAGGER_UI" == "200" ]] \
  || fail "GET /swagger/index.html esperado 200 — verificar que ApiDocs:EnableSwagger=true en el ambiente CI/QA, obtenido $STATUS_SWAGGER_UI"
ok "GET /swagger/index.html → 200 ✓"

# 9.6 GET /swagger/v1/swagger.json → 200 + contiene "Bearer"
info "GET /swagger/v1/swagger.json → debe retornar 200 y contener definición Bearer"
STATUS_SWAGGER_JSON=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/swagger/v1/swagger.json")
[[ "$STATUS_SWAGGER_JSON" == "200" ]] \
  || fail "GET /swagger/v1/swagger.json esperado 200 — verificar ApiDocs:EnableSwagger=true, obtenido $STATUS_SWAGGER_JSON"
SWAGGER_JSON=$(get_json "$API_URL/swagger/v1/swagger.json") \
  || fail "GET /swagger/v1/swagger.json no respondió"
grep -qi "bearer" <<< "$SWAGGER_JSON" \
  || fail "swagger.json no contiene configuración Bearer"
ok "GET /swagger/v1/swagger.json → 200 / contiene definición Bearer ✓"

echo ""

# ════════════════════════════════════════════════════
# FASE 10 — CORS, configuración por ambiente, preparación QA
# ════════════════════════════════════════════════════
phase "FASE 10: CORS para frontend y preparación QA"

# 10.1 Preflight CORS: OPTIONS /api/version con Origin: http://localhost:5173
info "OPTIONS /api/version con Origin: http://localhost:5173 → debe retornar 200/204 + Allow-Origin"
CORS_RESPONSE=$(curl -si -X OPTIONS \
  -H "Origin: http://localhost:5173" \
  -H "Access-Control-Request-Method: GET" \
  --max-time 15 \
  "$API_URL/api/version")
CORS_STATUS=$(echo "$CORS_RESPONSE" | head -1 | awk '{print $2}')
[[ "$CORS_STATUS" == "204" || "$CORS_STATUS" == "200" ]] \
  || fail "Preflight CORS esperado 200 o 204, obtenido $CORS_STATUS"
ok "Preflight CORS → HTTP $CORS_STATUS ✓"

CORS_ALLOW_ORIGIN=$(echo "$CORS_RESPONSE" | grep -i "access-control-allow-origin:" \
  | tr -d '\r' | awk '{print $2}')
[[ "$CORS_ALLOW_ORIGIN" == "http://localhost:5173" ]] \
  || fail "Access-Control-Allow-Origin esperado 'http://localhost:5173', obtenido '$CORS_ALLOW_ORIGIN'"
ok "Access-Control-Allow-Origin → $CORS_ALLOW_ORIGIN ✓"

# 10.2 GET /api/version sigue público y devuelve name + version no vacíos
info "GET /api/version sigue público con datos desde config"
VERS_10=$(get_json "$API_URL/api/version") \
  || fail "GET /api/version no respondió en Fase 10"
assert_ok "$VERS_10" "GET /api/version (Fase 10)"
API_NAME_10=$(echo "$VERS_10" | jq -r '.data.name')
API_VER_10=$(echo "$VERS_10"  | jq -r '.data.version')
[[ -n "$API_NAME_10" && "$API_NAME_10" != "null" ]] \
  || fail "data.name vacío en /api/version"
[[ -n "$API_VER_10"  && "$API_VER_10"  != "null" ]] \
  || fail "data.version vacío en /api/version"
ok "GET /api/version → name=$API_NAME_10  version=$API_VER_10 ✓"

# 10.3 GET /health sigue público
info "GET /health sigue público tras agregar CORS"
STATUS_HEALTH_10=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/health")
[[ "$STATUS_HEALTH_10" == "200" ]] \
  || fail "GET /health esperado 200, obtenido $STATUS_HEALTH_10"
ok "GET /health → $STATUS_HEALTH_10 ✓"

# 10.4 Endpoint protegido sin token sigue dando 401
STATUS_401_10=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/api/wallets/mi-wallet")
[[ "$STATUS_401_10" == "401" ]] \
  || fail "Endpoint protegido sin token esperado 401, obtenido $STATUS_401_10"
ok "Endpoint protegido sin token → 401 ✓"

# 10.5 El mismo endpoint protegido con token sigue funcionando
RESP_AUTH_10=$(get_auth_json "$TOKEN_A" "$API_URL/api/wallets/mi-wallet") \
  || fail "Endpoint protegido con token no respondió"
assert_ok "$RESP_AUTH_10" "endpoint protegido con token (Fase 10)"
ok "Endpoint protegido con token → success=true ✓"

echo ""

# ════════════════════════════════════════════════════
# FASE 14 — Listados administrativos: wallets y comercios
# ════════════════════════════════════════════════════
phase "FASE 14: Listados administrativos de wallets y comercios"

# 14.1 GET /api/admin/wallets → success=true, al menos 1 item
info "GET /api/admin/wallets (listado sin filtros) → success=true, items >= 1"
WALLETS_LIST=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/wallets") \
  || fail "GET /api/admin/wallets no respondió"
echo "$WALLETS_LIST" | jq .
assert_ok "$WALLETS_LIST" "GET listado de wallets"
WALLETS_TOTAL=$(echo "$WALLETS_LIST" | jq -r '.data.total')
[[ "$WALLETS_TOTAL" -ge 1 ]] \
  || fail "Listado wallets: total esperado >= 1, obtenido $WALLETS_TOTAL"
WALLETS_ITEMS=$(echo "$WALLETS_LIST" | jq '.data.items | length')
[[ "$WALLETS_ITEMS" -ge 1 ]] \
  || fail "Listado wallets: items esperado >= 1, obtenido $WALLETS_ITEMS"
ok "GET /api/admin/wallets → total=$WALLETS_TOTAL  items=$WALLETS_ITEMS ✓"

# 14.2 Filtro tipoWallet=PERSONA
info "GET /api/admin/wallets?tipoWallet=PERSONA → items >= 1"
WALLETS_PERS=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/wallets?tipoWallet=PERSONA") \
  || fail "GET wallets?tipoWallet=PERSONA no respondió"
echo "$WALLETS_PERS" | jq .
assert_ok "$WALLETS_PERS" "GET wallets filtro PERSONA"
WALLETS_PERS_ITEMS=$(echo "$WALLETS_PERS" | jq '.data.items | length')
[[ "$WALLETS_PERS_ITEMS" -ge 1 ]] \
  || fail "Listado wallets PERSONA: items esperado >= 1, obtenido $WALLETS_PERS_ITEMS"
ok "GET wallets?tipoWallet=PERSONA → items=$WALLETS_PERS_ITEMS ✓"

# 14.3 Filtro tipoWallet=COMERCIO
info "GET /api/admin/wallets?tipoWallet=COMERCIO → items >= 1"
WALLETS_COM=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/wallets?tipoWallet=COMERCIO") \
  || fail "GET wallets?tipoWallet=COMERCIO no respondió"
echo "$WALLETS_COM" | jq .
assert_ok "$WALLETS_COM" "GET wallets filtro COMERCIO"
WALLETS_COM_ITEMS=$(echo "$WALLETS_COM" | jq '.data.items | length')
[[ "$WALLETS_COM_ITEMS" -ge 1 ]] \
  || fail "Listado wallets COMERCIO: items esperado >= 1, obtenido $WALLETS_COM_ITEMS"
ok "GET wallets?tipoWallet=COMERCIO → items=$WALLETS_COM_ITEMS ✓"

# 14.4 Sin token → 401
info "GET /api/admin/wallets sin token → 401"
STATUS_WALLETS_401=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/api/admin/wallets")
[[ "$STATUS_WALLETS_401" == "401" ]] \
  || fail "GET wallets sin token esperado 401, obtenido $STATUS_WALLETS_401"
ok "GET /api/admin/wallets sin token → 401 ✓"

# 14.5 GET /api/admin/comercios → success=true, al menos 1 item
info "GET /api/admin/comercios (listado sin filtros) → success=true, items >= 1"
COMERCIOS_LIST=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/comercios") \
  || fail "GET /api/admin/comercios no respondió"
echo "$COMERCIOS_LIST" | jq .
assert_ok "$COMERCIOS_LIST" "GET listado de comercios"
COMERCIOS_TOTAL=$(echo "$COMERCIOS_LIST" | jq -r '.data.total')
[[ "$COMERCIOS_TOTAL" -ge 1 ]] \
  || fail "Listado comercios: total esperado >= 1, obtenido $COMERCIOS_TOTAL"
COMERCIOS_ITEMS=$(echo "$COMERCIOS_LIST" | jq '.data.items | length')
[[ "$COMERCIOS_ITEMS" -ge 1 ]] \
  || fail "Listado comercios: items esperado >= 1, obtenido $COMERCIOS_ITEMS"
ok "GET /api/admin/comercios → total=$COMERCIOS_TOTAL  items=$COMERCIOS_ITEMS ✓"

# 14.6 Filtro texto=Demo (seed: "Comercio Demo XPAY")
info "GET /api/admin/comercios?texto=Demo → items >= 1"
COMERCIOS_DEMO=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/comercios?texto=Demo") \
  || fail "GET comercios?texto=Demo no respondió"
echo "$COMERCIOS_DEMO" | jq .
assert_ok "$COMERCIOS_DEMO" "GET comercios filtro texto"
COMERCIOS_DEMO_ITEMS=$(echo "$COMERCIOS_DEMO" | jq '.data.items | length')
[[ "$COMERCIOS_DEMO_ITEMS" -ge 1 ]] \
  || fail "Listado comercios texto=Demo: items esperado >= 1, obtenido $COMERCIOS_DEMO_ITEMS"
ok "GET comercios?texto=Demo → items=$COMERCIOS_DEMO_ITEMS ✓"

# 14.7 Sin token → 401
info "GET /api/admin/comercios sin token → 401"
STATUS_COMS_401=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/api/admin/comercios")
[[ "$STATUS_COMS_401" == "401" ]] \
  || fail "GET comercios sin token esperado 401, obtenido $STATUS_COMS_401"
ok "GET /api/admin/comercios sin token → 401 ✓"

echo ""

# ════════════════════════════════════════════════════
# FASE 15 — Listados administrativos: ventas QR y transacciones ledger
# ════════════════════════════════════════════════════
phase "FASE 15: Listados administrativos de ventas QR y transacciones ledger"

# 15.1 GET /api/admin/ventas-qr → success=true, al menos 1 item
info "GET /api/admin/ventas-qr (sin filtros) → success=true, items >= 1"
VENTAS_LIST=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/ventas-qr") \
  || fail "GET /api/admin/ventas-qr no respondió"
echo "$VENTAS_LIST" | jq .
assert_ok "$VENTAS_LIST" "GET listado de ventas QR"
VENTAS_TOTAL=$(echo "$VENTAS_LIST" | jq -r '.data.total')
[[ "$VENTAS_TOTAL" -ge 1 ]] \
  || fail "Listado ventas QR: total esperado >= 1, obtenido $VENTAS_TOTAL"
VENTAS_ITEMS=$(echo "$VENTAS_LIST" | jq '.data.items | length')
[[ "$VENTAS_ITEMS" -ge 1 ]] \
  || fail "Listado ventas QR: items esperado >= 1, obtenido $VENTAS_ITEMS"
ok "GET /api/admin/ventas-qr → total=$VENTAS_TOTAL  items=$VENTAS_ITEMS ✓"

# 15.2 Filtro estado=LIQUIDADA (la venta de Fase 4 queda liquidada tras Fase 5)
info "GET /api/admin/ventas-qr?estado=LIQUIDADA → items >= 1"
VENTAS_LIQ=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/ventas-qr?estado=LIQUIDADA") \
  || fail "GET ventas-qr?estado=LIQUIDADA no respondió"
echo "$VENTAS_LIQ" | jq .
assert_ok "$VENTAS_LIQ" "GET ventas-qr filtro LIQUIDADA"
VENTAS_LIQ_ITEMS=$(echo "$VENTAS_LIQ" | jq '.data.items | length')
[[ "$VENTAS_LIQ_ITEMS" -ge 1 ]] \
  || fail "Listado ventas QR LIQUIDADA: items esperado >= 1, obtenido $VENTAS_LIQ_ITEMS"
ok "GET ventas-qr?estado=LIQUIDADA → items=$VENTAS_LIQ_ITEMS ✓"

# 15.3 Sin token → 401
info "GET /api/admin/ventas-qr sin token → 401"
STATUS_VENTAS_401=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/api/admin/ventas-qr")
[[ "$STATUS_VENTAS_401" == "401" ]] \
  || fail "GET ventas-qr sin token esperado 401, obtenido $STATUS_VENTAS_401"
ok "GET /api/admin/ventas-qr sin token → 401 ✓"

# 15.4 GET /api/admin/ledger-transacciones → success=true, al menos 1 item
info "GET /api/admin/ledger-transacciones (sin filtros) → success=true, items >= 1"
LEDGER_LIST=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/ledger-transacciones") \
  || fail "GET /api/admin/ledger-transacciones no respondió"
echo "$LEDGER_LIST" | jq .
assert_ok "$LEDGER_LIST" "GET listado de transacciones ledger"
LEDGER_TOTAL=$(echo "$LEDGER_LIST" | jq -r '.data.total')
[[ "$LEDGER_TOTAL" -ge 1 ]] \
  || fail "Listado ledger: total esperado >= 1, obtenido $LEDGER_TOTAL"
LEDGER_ITEMS=$(echo "$LEDGER_LIST" | jq '.data.items | length')
[[ "$LEDGER_ITEMS" -ge 1 ]] \
  || fail "Listado ledger: items esperado >= 1, obtenido $LEDGER_ITEMS"
ok "GET /api/admin/ledger-transacciones → total=$LEDGER_TOTAL  items=$LEDGER_ITEMS ✓"

# 15.5 Filtro tipoTransaccion=PAGO_QR
info "GET /api/admin/ledger-transacciones?tipoTransaccion=PAGO_QR → items >= 1"
LEDGER_PAGO=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/ledger-transacciones?tipoTransaccion=PAGO_QR") \
  || fail "GET ledger-transacciones?tipoTransaccion=PAGO_QR no respondió"
echo "$LEDGER_PAGO" | jq .
assert_ok "$LEDGER_PAGO" "GET ledger-transacciones filtro PAGO_QR"
LEDGER_PAGO_ITEMS=$(echo "$LEDGER_PAGO" | jq '.data.items | length')
[[ "$LEDGER_PAGO_ITEMS" -ge 1 ]] \
  || fail "Listado ledger PAGO_QR: items esperado >= 1, obtenido $LEDGER_PAGO_ITEMS"
ok "GET ledger-transacciones?tipoTransaccion=PAGO_QR → items=$LEDGER_PAGO_ITEMS ✓"

# 15.6 Sin token → 401
info "GET /api/admin/ledger-transacciones sin token → 401"
STATUS_LEDGER_401=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/api/admin/ledger-transacciones")
[[ "$STATUS_LEDGER_401" == "401" ]] \
  || fail "GET ledger-transacciones sin token esperado 401, obtenido $STATUS_LEDGER_401"
ok "GET /api/admin/ledger-transacciones sin token → 401 ✓"

echo ""

# ════════════════════════════════════════════════════
# FASE USUARIOS-ADMIN-2 — Listado y consulta de usuarios
# ════════════════════════════════════════════════════
phase "FASE USUARIOS-ADMIN-2: Listado y consulta de usuarios"

# UA.1 GET /api/admin/usuarios con ADMIN_XPAY → success=true, al menos 1 item
info "GET /api/admin/usuarios (ADMIN_XPAY) → success=true, items >= 1"
USUARIOS_LIST=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios") \
  || fail "GET /api/admin/usuarios no respondió"
echo "$USUARIOS_LIST" | jq .
assert_ok "$USUARIOS_LIST" "GET listado de usuarios"
USUARIOS_TOTAL=$(echo "$USUARIOS_LIST" | jq -r '.data.total')
[[ "$USUARIOS_TOTAL" -ge 1 ]] \
  || fail "Listado usuarios: total esperado >= 1, obtenido $USUARIOS_TOTAL"
ok "GET /api/admin/usuarios → total=$USUARIOS_TOTAL ✓"

# UA.2 El mismo TOKEN_ADMIN también satisface el rol SUPERUSUARIO — el
# fixture ci_admin_xpay tiene AMBOS roles activos (ver scripts/ci/
# ci_admin_xpay_fixture.sql), así que esta misma llamada valida en un
# solo token que el endpoint acepta indistintamente ADMIN_XPAY o
# SUPERUSUARIO, sin necesitar un segundo fixture/usuario.
info "GET /api/admin/usuarios (mismo TOKEN_ADMIN, valida también SUPERUSUARIO) → success=true"
[[ "$(echo "$USUARIOS_LIST" | jq -r '.success')" == "true" ]] \
  || fail "El token de ci_admin_xpay (ADMIN_XPAY+SUPERUSUARIO) debió recibir success=true"
ok "TOKEN_ADMIN (ADMIN_XPAY + SUPERUSUARIO) → GET /api/admin/usuarios → 200 ✓"

# UA.3 USUARIO_FINAL → 403
info "GET /api/admin/usuarios con USUARIO_FINAL (carlos) → 403"
STATUS_USUARIOS_403=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -H "Authorization: Bearer $TOKEN_A" \
  "$API_URL/api/admin/usuarios")
[[ "$STATUS_USUARIOS_403" == "403" ]] \
  || fail "GET usuarios con USUARIO_FINAL esperado 403, obtenido $STATUS_USUARIOS_403"
ok "GET /api/admin/usuarios con USUARIO_FINAL → 403 ✓"

# UA.4 Sin token → 401
info "GET /api/admin/usuarios sin token → 401"
STATUS_USUARIOS_401=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/api/admin/usuarios")
[[ "$STATUS_USUARIOS_401" == "401" ]] \
  || fail "GET usuarios sin token esperado 401, obtenido $STATUS_USUARIOS_401"
ok "GET /api/admin/usuarios sin token → 401 ✓"

# UA.5 Filtro texto
info "GET /api/admin/usuarios?texto=carlos_ci_test → coincide con carlos"
USUARIOS_TEXTO=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios?texto=carlos_ci_test") \
  || fail "GET usuarios?texto no respondió"
echo "$USUARIOS_TEXTO" | jq .
assert_ok "$USUARIOS_TEXTO" "GET usuarios filtro texto"
USUARIOS_TEXTO_ITEMS=$(echo "$USUARIOS_TEXTO" | jq '.data.items | length')
[[ "$USUARIOS_TEXTO_ITEMS" -ge 1 ]] \
  || fail "Filtro texto=carlos_ci_test: items esperado >= 1, obtenido $USUARIOS_TEXTO_ITEMS"
USUARIOS_TEXTO_USUARIO=$(echo "$USUARIOS_TEXTO" | jq -r '.data.items[0].usuario')
[[ "$USUARIOS_TEXTO_USUARIO" == "carlos_ci_test" ]] \
  || fail "Filtro texto: esperado usuario=carlos_ci_test, obtenido $USUARIOS_TEXTO_USUARIO"
ok "GET usuarios?texto=carlos_ci_test → items=$USUARIOS_TEXTO_ITEMS ✓"

# UA.6 Filtro estado
info "GET /api/admin/usuarios?estado=ACTIVO → todos los items en ACTIVO"
USUARIOS_ESTADO=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios?estado=ACTIVO") \
  || fail "GET usuarios?estado no respondió"
assert_ok "$USUARIOS_ESTADO" "GET usuarios filtro estado"
USUARIOS_ESTADO_NO_ACTIVO=$(echo "$USUARIOS_ESTADO" | jq '[.data.items[] | select(.estado != "ACTIVO")] | length')
[[ "$USUARIOS_ESTADO_NO_ACTIVO" == "0" ]] \
  || fail "Filtro estado=ACTIVO: se encontraron $USUARIOS_ESTADO_NO_ACTIVO usuarios con otro estado"
ok "GET usuarios?estado=ACTIVO → $(echo "$USUARIOS_ESTADO" | jq '.data.total') resultado(s), todos ACTIVO ✓"

# UA.7 Filtro rol
info "GET /api/admin/usuarios?rol=ADMIN_XPAY → incluye ci_admin_xpay"
USUARIOS_ROL=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios?rol=ADMIN_XPAY") \
  || fail "GET usuarios?rol no respondió"
echo "$USUARIOS_ROL" | jq .
assert_ok "$USUARIOS_ROL" "GET usuarios filtro rol"
USUARIOS_ROL_CONTIENE=$(echo "$USUARIOS_ROL" | jq '[.data.items[] | select(.usuario == "ci_admin_xpay")] | length')
[[ "$USUARIOS_ROL_CONTIENE" -ge 1 ]] \
  || fail "Filtro rol=ADMIN_XPAY: no se encontró ci_admin_xpay en los resultados"
ok "GET usuarios?rol=ADMIN_XPAY → incluye ci_admin_xpay ✓"

# UA.8 soloBloqueados
info "GET /api/admin/usuarios?soloBloqueados=true → forma correcta"
USUARIOS_BLOQ=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios?soloBloqueados=true") \
  || fail "GET usuarios?soloBloqueados no respondió"
assert_ok "$USUARIOS_BLOQ" "GET usuarios soloBloqueados"
USUARIOS_BLOQ_NO_BLOQ=$(echo "$USUARIOS_BLOQ" | jq '[.data.items[] | select(.estado != "BLOQUEADO")] | length')
[[ "$USUARIOS_BLOQ_NO_BLOQ" == "0" ]] \
  || fail "soloBloqueados=true: se encontraron $USUARIOS_BLOQ_NO_BLOQ usuarios no bloqueados"
ok "GET usuarios?soloBloqueados=true → $(echo "$USUARIOS_BLOQ" | jq '.data.total') resultado(s), forma correcta ✓"

# UA.9 Paginación
info "GET /api/admin/usuarios?page=1&pageSize=1 → items <= 1, pageSize=1"
USUARIOS_PAG=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios?page=1&pageSize=1") \
  || fail "GET usuarios paginación no respondió"
assert_ok "$USUARIOS_PAG" "GET usuarios paginación"
USUARIOS_PAG_ITEMS=$(echo "$USUARIOS_PAG" | jq '.data.items | length')
USUARIOS_PAG_SIZE=$(echo "$USUARIOS_PAG" | jq -r '.data.pageSize')
[[ "$USUARIOS_PAG_ITEMS" -le 1 ]] \
  || fail "Paginación pageSize=1: items esperado <=1, obtenido $USUARIOS_PAG_ITEMS"
[[ "$USUARIOS_PAG_SIZE" == "1" ]] \
  || fail "Paginación: pageSize esperado 1, obtenido $USUARIOS_PAG_SIZE"
ok "GET usuarios?page=1&pageSize=1 → items=$USUARIOS_PAG_ITEMS  pageSize=$USUARIOS_PAG_SIZE ✓"

# UA.10 Detalle existente — usa el idUsuario de carlos (ID_USUARIO_A, FASE 1)
info "GET /api/admin/usuarios/\$ID_USUARIO_A (existente) → success=true, sin password_hash expuesto"
USUARIO_DETALLE=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_A") \
  || fail "GET usuarios/{id} existente no respondió"
echo "$USUARIO_DETALLE" | jq .
assert_ok "$USUARIO_DETALLE" "GET usuario detalle"
USUARIO_DETALLE_ID=$(echo "$USUARIO_DETALLE" | jq -r '.data.idUsuario')
[[ "$USUARIO_DETALLE_ID" == "$ID_USUARIO_A" ]] \
  || fail "Detalle usuario: idUsuario esperado $ID_USUARIO_A, obtenido $USUARIO_DETALLE_ID"
echo "$USUARIO_DETALLE" | grep -iq "password" \
  && fail "SEGURIDAD: la respuesta de detalle de usuario contiene la palabra 'password' — posible fuga de password_hash"
ok "GET usuarios/$ID_USUARIO_A → idUsuario=$USUARIO_DETALLE_ID, sin 'password' en la respuesta ✓"

# UA.11 Detalle inexistente
info "GET /api/admin/usuarios/999999 (inexistente) → 404"
STATUS_USUARIO_404=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -H "Authorization: Bearer $TOKEN_ADMIN" \
  "$API_URL/api/admin/usuarios/999999")
[[ "$STATUS_USUARIO_404" == "404" ]] \
  || fail "GET usuarios/999999 esperado 404, obtenido $STATUS_USUARIO_404"
ok "GET /api/admin/usuarios/999999 → 404 ✓"

echo ""

# ════════════════════════════════════════════════════
# FASE USUARIOS-ADMIN-3 — Activar, inactivar y desbloquear usuarios
# ════════════════════════════════════════════════════
phase "FASE USUARIOS-ADMIN-3: Activar, inactivar y desbloquear usuarios"

# ID del propio ci_admin_xpay, reutilizando la respuesta de login ya
# capturada en FASE 1 (LOGIN_ADMIN) — necesario para el caso de auto-acción.
ID_USUARIO_ADMIN=$(echo "$LOGIN_ADMIN" | jq -r '.data.idUsuario')

# Usuario final CI dedicado a esta fase — nunca se reutiliza carlos/maria
# para no afectar los saldos/estados que otras fases ya validaron.
info "POST /api/usuarios/registro-final (usuario dedicado a USUARIOS-ADMIN-3)"
REGISTRO_ET=$(post_json "$API_URL/api/usuarios/registro-final" '{
  "idUnidadNegocio": 1,
  "tipoDocumento": "CC",
  "numeroDocumento": "1099009999",
  "primerNombre": "Estado",
  "primerApellido": "Test",
  "celular": "3009998888",
  "email": "estado.test@ci-test.com",
  "usuario": "estadotest_ci_test",
  "password": "Xpay@Test1!"
}') || fail "POST registro estadotest no respondió"
assert_ok "$REGISTRO_ET" "registro estadotest"
ID_USUARIO_ET=$(echo "$REGISTRO_ET" | jq -r '.idUsuario')
ok "Registro estadotest_ci_test → idUsuario=$ID_USUARIO_ET"

# UA3.1 ADMIN_XPAY inactiva otro usuario
info "POST /api/admin/usuarios/$ID_USUARIO_ET/inactivar (ADMIN_XPAY) → 200, estado=INACTIVO"
INACTIVAR_ET=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/inactivar" "{}") \
  || fail "POST inactivar estadotest no respondió"
echo "$INACTIVAR_ET" | jq .
assert_ok "$INACTIVAR_ET" "inactivar estadotest"
ESTADO_ET_INACTIVO=$(echo "$INACTIVAR_ET" | jq -r '.data.estado')
[[ "$ESTADO_ET_INACTIVO" == "INACTIVO" ]] \
  || fail "Tras inactivar: estado esperado INACTIVO, obtenido $ESTADO_ET_INACTIVO"
ok "Usuario inactivado → estado=$ESTADO_ET_INACTIVO ✓"

# UA3.2 Login rechazado estando INACTIVO
info "POST /api/auth/login (estadotest, INACTIVO) → 400"
STATUS_LOGIN_INACTIVO=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/auth/login" -H "Content-Type: application/json" \
  -d '{"usuario":"estadotest_ci_test","password":"Xpay@Test1!"}')
[[ "$STATUS_LOGIN_INACTIVO" == "400" ]] \
  || fail "Login usuario INACTIVO esperado 400, obtenido $STATUS_LOGIN_INACTIVO"
ok "Login con usuario INACTIVO → 400 ✓"

# UA3.3 Activar usuario inactivo
info "POST /api/admin/usuarios/$ID_USUARIO_ET/activar (ADMIN_XPAY) → 200, estado=ACTIVO"
ACTIVAR_ET=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/activar" "{}") \
  || fail "POST activar estadotest no respondió"
echo "$ACTIVAR_ET" | jq .
assert_ok "$ACTIVAR_ET" "activar estadotest"
ESTADO_ET_ACTIVO=$(echo "$ACTIVAR_ET" | jq -r '.data.estado')
[[ "$ESTADO_ET_ACTIVO" == "ACTIVO" ]] \
  || fail "Tras activar: estado esperado ACTIVO, obtenido $ESTADO_ET_ACTIVO"
ok "Usuario activado → estado=$ESTADO_ET_ACTIVO ✓"

# UA3.4 Login exitoso tras activar
info "POST /api/auth/login (estadotest, ACTIVO) → success=true"
LOGIN_ET_OK=$(post_json "$API_URL/api/auth/login" '{"usuario":"estadotest_ci_test","password":"Xpay@Test1!"}') \
  || fail "Login estadotest tras activar no respondió"
assert_ok "$LOGIN_ET_OK" "login estadotest tras activar"
ok "Login estadotest tras activar → success=true ✓"

# UA3.5 Bloqueo real por 5 intentos fallidos
info "5 intentos fallidos consecutivos → bloqueo automático (comportamiento ya existente de AuthService)"
for i in 1 2 3 4 5; do
  curl -s -o /dev/null --max-time 15 -X POST "$API_URL/api/auth/login" \
    -H "Content-Type: application/json" \
    -d '{"usuario":"estadotest_ci_test","password":"ClaveIncorrecta!"}'
done
DETALLE_ET_BLOQUEADO=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_ET") \
  || fail "GET detalle estadotest tras intentos fallidos no respondió"
ESTADO_ET_BLOQUEADO=$(echo "$DETALLE_ET_BLOQUEADO" | jq -r '.data.estado')
[[ "$ESTADO_ET_BLOQUEADO" == "BLOQUEADO" ]] \
  || fail "Tras 5 intentos fallidos: estado esperado BLOQUEADO, obtenido $ESTADO_ET_BLOQUEADO"
ok "5 intentos fallidos → estado=$ESTADO_ET_BLOQUEADO ✓"

# UA3.6 Desbloquear y limpiar campos
info "POST /api/admin/usuarios/$ID_USUARIO_ET/desbloquear (ADMIN_XPAY) → 200, campos limpiados"
DESBLOQUEAR_ET=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/desbloquear" "{}") \
  || fail "POST desbloquear estadotest no respondió"
echo "$DESBLOQUEAR_ET" | jq .
assert_ok "$DESBLOQUEAR_ET" "desbloquear estadotest"
ESTADO_ET_DESBLOQ=$(echo "$DESBLOQUEAR_ET" | jq -r '.data.estado')
INTENTOS_ET_DESBLOQ=$(echo "$DESBLOQUEAR_ET" | jq -r '.data.intentosFallidos')
FECHA_BLOQ_ET_DESBLOQ=$(echo "$DESBLOQUEAR_ET" | jq -r '.data.fechaBloqueo')
MOTIVO_BLOQ_ET_DESBLOQ=$(echo "$DESBLOQUEAR_ET" | jq -r '.data.motivoBloqueo')
[[ "$ESTADO_ET_DESBLOQ" == "ACTIVO" ]] \
  || fail "Tras desbloquear: estado esperado ACTIVO, obtenido $ESTADO_ET_DESBLOQ"
[[ "$INTENTOS_ET_DESBLOQ" == "0" ]] \
  || fail "Tras desbloquear: intentosFallidos esperado 0, obtenido $INTENTOS_ET_DESBLOQ"
[[ "$FECHA_BLOQ_ET_DESBLOQ" == "null" ]] \
  || fail "Tras desbloquear: fechaBloqueo esperado null, obtenido $FECHA_BLOQ_ET_DESBLOQ"
[[ "$MOTIVO_BLOQ_ET_DESBLOQ" == "null" ]] \
  || fail "Tras desbloquear: motivoBloqueo esperado null, obtenido $MOTIVO_BLOQ_ET_DESBLOQ"
ok "Usuario desbloqueado → estado=$ESTADO_ET_DESBLOQ intentosFallidos=$INTENTOS_ET_DESBLOQ fechaBloqueo=null motivoBloqueo=null ✓"

# UA3.7 Desbloquear un usuario ACTIVO → 409
info "POST /api/admin/usuarios/$ID_USUARIO_ET/desbloquear (ya ACTIVO) → 409"
STATUS_DESBLOQ_409=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/desbloquear" \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" -d '{}')
[[ "$STATUS_DESBLOQ_409" == "409" ]] \
  || fail "Desbloquear usuario ACTIVO esperado 409, obtenido $STATUS_DESBLOQ_409"
ok "Desbloquear usuario ACTIVO → 409 ✓"

# UA3.8 USUARIO_FINAL → 403 en los 3 endpoints
info "Los 3 endpoints con USUARIO_FINAL (carlos) → 403"
for accion in activar inactivar desbloquear; do
  STATUS_UF_403=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
    -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/$accion" \
    -H "Authorization: Bearer $TOKEN_A" -H "Content-Type: application/json" -d '{}')
  [[ "$STATUS_UF_403" == "403" ]] \
    || fail "$accion con USUARIO_FINAL esperado 403, obtenido $STATUS_UF_403"
done
ok "activar/inactivar/desbloquear con USUARIO_FINAL → 403 en los 3 ✓"

# UA3.9 Sin token → 401 en los 3 endpoints
info "Los 3 endpoints sin token → 401"
for accion in activar inactivar desbloquear; do
  STATUS_NOAUTH_401=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
    -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/$accion" \
    -H "Content-Type: application/json" -d '{}')
  [[ "$STATUS_NOAUTH_401" == "401" ]] \
    || fail "$accion sin token esperado 401, obtenido $STATUS_NOAUTH_401"
done
ok "activar/inactivar/desbloquear sin token → 401 en los 3 ✓"

# UA3.10 Auto-acción → 400 en los 3 endpoints (ci_admin_xpay sobre sí mismo)
info "Los 3 endpoints sobre la propia cuenta (ci_admin_xpay) → 400"
for accion in activar inactivar desbloquear; do
  STATUS_SELF_400=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
    -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ADMIN/$accion" \
    -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" -d '{}')
  [[ "$STATUS_SELF_400" == "400" ]] \
    || fail "$accion sobre la propia cuenta esperado 400, obtenido $STATUS_SELF_400"
done
ok "activar/inactivar/desbloquear sobre la propia cuenta → 400 en los 3 ✓"

# UA3.11 Protección del último administrador — dinámico: inactiva TODOS los
# administradores auxiliares activos (cualquier usuario distinto de
# ci_admin_xpay con ADMIN_XPAY/SUPERUSUARIO activo), sin hardcodear una
# lista fija de fixtures. Así la prueba sigue siendo válida sin importar
# cuántos fixtures administrativos de CI existan (hoy: ci_admin_guard,
# ci_admin_solo; cualquiera que se agregue después no requiere tocar esto).
info "POST /api/auth/login (ci_admin_guard) — segundo fixture ADMIN, exclusivo de esta prueba"
LOGIN_ADMIN_GUARD=$(post_json "$API_URL/api/auth/login" '{
  "usuario": "ci_admin_guard",
  "password": "CI-Fixture-AdminGuard#2026"
}') || fail "POST login ci_admin_guard no respondió"
assert_ok "$LOGIN_ADMIN_GUARD" "login ci_admin_guard"
TOKEN_ADMIN_GUARD=$(echo "$LOGIN_ADMIN_GUARD" | jq -r '.data.token')
[[ -n "$TOKEN_ADMIN_GUARD" && "$TOKEN_ADMIN_GUARD" != "null" ]] || fail "Token JWT vacío tras login de ci_admin_guard"
ok "Login ci_admin_guard → token=${TOKEN_ADMIN_GUARD:0:30}..."

# Descubrimiento dinámico — cualquier usuario ACTIVO distinto de
# ci_admin_xpay que tenga ADMIN_XPAY/SUPERUSUARIO activo en este momento,
# sin asumir nombres fijos de fixture.
ADMINS_AUXILIARES=$("$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET NOCOUNT ON; SELECT DISTINCT u.usuario FROM usuarios u JOIN usuario_roles ur ON ur.id_usuario = u.id_usuario JOIN roles r ON r.id_rol = ur.id_rol WHERE u.usuario <> 'ci_admin_xpay' AND u.estado = 'ACTIVO' AND ur.estado = 'ACTIVO' AND r.codigo IN ('ADMIN_XPAY','SUPERUSUARIO')" \
  -h -1 | tr -d '\r' | sed 's/[[:space:]]*$//' | sed '/^$/d')
[[ -n "$ADMINS_AUXILIARES" ]] \
  || fail "No se detectó ningún administrador auxiliar activo distinto de ci_admin_xpay — no se puede ejecutar la prueba del último administrador"
ADMINS_AUX_SQL_LIST=$(echo "$ADMINS_AUXILIARES" | sed "s/^/'/;s/$/'/" | paste -sd, -)
info "Administradores auxiliares detectados dinámicamente: $(echo "$ADMINS_AUXILIARES" | tr '\n' ',' | sed 's/,$//')"

# Se inactivan TODOS por SQL directo (no por el endpoint, para no depender de
# la regla que se está probando) DESPUES de obtener el token de ci_admin_guard
# — el JWT vigente se usa deliberadamente como mecanismo controlado de esta
# prueba (la invalidación inmediata de JWT al inactivar no se resuelve en
# esta fase, según lo autorizado). trap EXIT garantiza la restauración a
# ACTIVO incluso si algún assert posterior de esta fase (o de fases
# siguientes) falla y aborta el script.
restaurar_admins_auxiliares() {
  "$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
    -Q "SET NOCOUNT ON; UPDATE usuarios SET estado='ACTIVO' WHERE usuario IN ($ADMINS_AUX_SQL_LIST);" \
    > /dev/null 2>&1 || true
}
trap restaurar_admins_auxiliares EXIT

"$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET NOCOUNT ON; UPDATE usuarios SET estado='INACTIVO' WHERE usuario IN ($ADMINS_AUX_SQL_LIST);" \
  > /dev/null || fail "No se pudieron inactivar los administradores auxiliares por SQL directo"
ok "Administradores auxiliares inactivados por SQL directo (temporal, se restauran al finalizar) ✓"

check_sql_value \
  "Administradores ACTIVO con ADMIN_XPAY/SUPERUSUARIO tras inactivar auxiliares = 1 (solo ci_admin_xpay)" \
  "SELECT COUNT(DISTINCT u.id_usuario) FROM usuarios u JOIN usuario_roles ur ON ur.id_usuario = u.id_usuario JOIN roles r ON r.id_rol = ur.id_rol WHERE u.estado = 'ACTIVO' AND ur.estado = 'ACTIVO' AND r.codigo IN ('ADMIN_XPAY','SUPERUSUARIO')" \
  "1"

info "POST /api/admin/usuarios/$ID_USUARIO_ADMIN/inactivar (con TOKEN_ADMIN_GUARD, único admin restante) → 400"
STATUS_ULTIMO_ADMIN=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ADMIN/inactivar" \
  -H "Authorization: Bearer $TOKEN_ADMIN_GUARD" -H "Content-Type: application/json" -d '{}')
[[ "$STATUS_ULTIMO_ADMIN" == "400" ]] \
  || fail "Inactivar último administrador esperado 400, obtenido $STATUS_ULTIMO_ADMIN"
ok "Protección del último administrador → 400 ✓"

"$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET NOCOUNT ON; UPDATE usuarios SET estado='ACTIVO' WHERE usuario IN ($ADMINS_AUX_SQL_LIST);" \
  > /dev/null || fail "No se pudieron restaurar los administradores auxiliares a ACTIVO"
ok "Administradores auxiliares restaurados a ACTIVO ✓"
trap - EXIT

# UA3.12 El detalle refleja los estados finales
info "GET /api/admin/usuarios/$ID_USUARIO_ET (detalle final) → estado=ACTIVO"
DETALLE_ET_FINAL=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_ET") \
  || fail "GET detalle final estadotest no respondió"
ESTADO_ET_FINAL=$(echo "$DETALLE_ET_FINAL" | jq -r '.data.estado')
[[ "$ESTADO_ET_FINAL" == "ACTIVO" ]] \
  || fail "Detalle final estadotest: estado esperado ACTIVO, obtenido $ESTADO_ET_FINAL"
ok "Detalle final refleja estado=$ESTADO_ET_FINAL ✓"

# UA3.13 Auditoría persistente creada
check_sql_value \
  "Auditoría USUARIO_INACTIVAR registrada para id_entidad=$ID_USUARIO_ET" \
  "SELECT COUNT(*) FROM auditoria WHERE modulo = 'USUARIOS_ADMIN' AND accion = 'USUARIO_INACTIVAR' AND entidad = 'usuarios' AND id_entidad = '$ID_USUARIO_ET' AND resultado = 'EXITOSO'" \
  "1"

echo ""

# ════════════════════════════════════════════════════
# FASE USUARIOS-ADMIN-4 — Asignación y revocación de roles
# ════════════════════════════════════════════════════
phase "FASE USUARIOS-ADMIN-4: Asignación y revocación de roles"

info "POST /api/auth/login (ci_admin_solo) — ADMIN_XPAY únicamente, sin SUPERUSUARIO"
LOGIN_ADMIN_SOLO=$(post_json "$API_URL/api/auth/login" '{
  "usuario": "ci_admin_solo",
  "password": "CI-Fixture-AdminSolo#2026"
}') || fail "POST login ci_admin_solo no respondió"
assert_ok "$LOGIN_ADMIN_SOLO" "login ci_admin_solo"
TOKEN_ADMIN_SOLO=$(echo "$LOGIN_ADMIN_SOLO" | jq -r '.data.token')
[[ -n "$TOKEN_ADMIN_SOLO" && "$TOKEN_ADMIN_SOLO" != "null" ]] || fail "Token JWT vacío tras login de ci_admin_solo"
ok "Login ci_admin_solo → token=${TOKEN_ADMIN_SOLO:0:30}..."

# UA4.1 Roles asignables para ADMIN_XPAY sin SUPERUSUARIO — no incluyen SUPERUSUARIO
info "GET /api/admin/roles/asignables (ci_admin_solo) → sin SUPERUSUARIO"
ROLES_ASIG_SOLO=$(get_auth_json "$TOKEN_ADMIN_SOLO" "$API_URL/api/admin/roles/asignables") \
  || fail "GET roles/asignables (ci_admin_solo) no respondió"
echo "$ROLES_ASIG_SOLO" | jq .
assert_ok "$ROLES_ASIG_SOLO" "roles asignables ci_admin_solo"
CONTIENE_SUPER_SOLO=$(echo "$ROLES_ASIG_SOLO" | jq '[.data[] | select(.codigo=="SUPERUSUARIO")] | length')
[[ "$CONTIENE_SUPER_SOLO" == "0" ]] \
  || fail "roles/asignables para ADMIN_XPAY sin SUPERUSUARIO no debería incluir SUPERUSUARIO"
ok "roles/asignables (ADMIN_XPAY sin SUPERUSUARIO) → no incluye SUPERUSUARIO ✓"

# UA4.2 Roles asignables para un actor con SUPERUSUARIO — sí lo incluyen
info "GET /api/admin/roles/asignables (TOKEN_ADMIN, con SUPERUSUARIO) → incluye SUPERUSUARIO"
ROLES_ASIG_ADMIN=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/roles/asignables") \
  || fail "GET roles/asignables (TOKEN_ADMIN) no respondió"
assert_ok "$ROLES_ASIG_ADMIN" "roles asignables TOKEN_ADMIN"
CONTIENE_SUPER_ADMIN=$(echo "$ROLES_ASIG_ADMIN" | jq '[.data[] | select(.codigo=="SUPERUSUARIO")] | length')
[[ "$CONTIENE_SUPER_ADMIN" -ge 1 ]] \
  || fail "roles/asignables para un actor con SUPERUSUARIO debería incluir SUPERUSUARIO"
ok "roles/asignables (con SUPERUSUARIO) → incluye SUPERUSUARIO ✓"

# UA4.3 Asignar CARTERA_XPAY a estadotest (mismo usuario CI de FASE USUARIOS-ADMIN-3)
info "POST /api/admin/usuarios/$ID_USUARIO_ET/roles (CARTERA_XPAY) → 200"
ASIGNAR_CARTERA=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/roles" \
  '{"rolCodigo":"CARTERA_XPAY","observacion":"CI USUARIOS-ADMIN-4"}') \
  || fail "POST asignar CARTERA_XPAY no respondió"
echo "$ASIGNAR_CARTERA" | jq .
assert_ok "$ASIGNAR_CARTERA" "asignar CARTERA_XPAY"
ok "Rol CARTERA_XPAY asignado a estadotest ✓"

# UA4.4 El detalle refleja el rol — captura idRol para los pasos siguientes
info "GET /api/admin/usuarios/$ID_USUARIO_ET (detalle) → incluye CARTERA_XPAY"
DETALLE_CON_ROL=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_ET") \
  || fail "GET detalle tras asignar no respondió"
CONTIENE_CARTERA_DETALLE=$(echo "$DETALLE_CON_ROL" | jq '[.data.roles[] | select(.codigo=="CARTERA_XPAY")] | length')
[[ "$CONTIENE_CARTERA_DETALLE" == "1" ]] \
  || fail "Detalle esperado con CARTERA_XPAY, no encontrado"
ID_ROL_CARTERA=$(echo "$DETALLE_CON_ROL" | jq -r '.data.roles[] | select(.codigo=="CARTERA_XPAY") | .idRol')
ok "Detalle refleja CARTERA_XPAY (idRol=$ID_ROL_CARTERA) ✓"

# UA4.5 Nuevo login de estadotest → roles incluye CARTERA_XPAY
info "POST /api/auth/login (estadotest) → roles incluye CARTERA_XPAY"
LOGIN_ET_CON_ROL=$(post_json "$API_URL/api/auth/login" '{"usuario":"estadotest_ci_test","password":"Xpay@Test1!"}') \
  || fail "Login estadotest (con rol) no respondió"
assert_ok "$LOGIN_ET_CON_ROL" "login estadotest con rol"
CONTIENE_CARTERA_LOGIN=$(echo "$LOGIN_ET_CON_ROL" | jq '[.data.roles[] | select(.=="CARTERA_XPAY")] | length')
[[ "$CONTIENE_CARTERA_LOGIN" == "1" ]] \
  || fail "Login estadotest esperado con rol CARTERA_XPAY en la respuesta"
ok "Nuevo login de estadotest → roles incluye CARTERA_XPAY ✓"

# UA4.6 Asignar duplicado → 409
info "POST /api/admin/usuarios/$ID_USUARIO_ET/roles (CARTERA_XPAY duplicado) → 409"
STATUS_DUP_ROL=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/roles" \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" \
  -d '{"rolCodigo":"CARTERA_XPAY"}')
[[ "$STATUS_DUP_ROL" == "409" ]] \
  || fail "Asignar rol duplicado esperado 409, obtenido $STATUS_DUP_ROL"
ok "Asignar CARTERA_XPAY duplicado → 409 ✓"

# UA4.7 Revocar CARTERA_XPAY
info "POST /api/admin/usuarios/$ID_USUARIO_ET/roles/$ID_ROL_CARTERA/revocar → 200"
REVOCAR_CARTERA=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/roles/$ID_ROL_CARTERA/revocar" \
  '{"observacion":"CI USUARIOS-ADMIN-4 revocación"}') \
  || fail "POST revocar CARTERA_XPAY no respondió"
echo "$REVOCAR_CARTERA" | jq .
assert_ok "$REVOCAR_CARTERA" "revocar CARTERA_XPAY"
ok "Rol CARTERA_XPAY revocado ✓"

# UA4.8 Nuevo login de estadotest → CARTERA_XPAY ya no aparece
info "POST /api/auth/login (estadotest) → roles ya NO incluye CARTERA_XPAY"
LOGIN_ET_SIN_ROL=$(post_json "$API_URL/api/auth/login" '{"usuario":"estadotest_ci_test","password":"Xpay@Test1!"}') \
  || fail "Login estadotest (sin rol) no respondió"
assert_ok "$LOGIN_ET_SIN_ROL" "login estadotest sin rol"
CONTIENE_CARTERA_LOGIN_2=$(echo "$LOGIN_ET_SIN_ROL" | jq '[.data.roles[] | select(.=="CARTERA_XPAY")] | length')
[[ "$CONTIENE_CARTERA_LOGIN_2" == "0" ]] \
  || fail "Login estadotest tras revocar no debería incluir CARTERA_XPAY"
ok "Nuevo login de estadotest → CARTERA_XPAY ya no aparece ✓"

# UA4.9 Revocar nuevamente (ya inactivo) → 409
info "POST /api/admin/usuarios/$ID_USUARIO_ET/roles/$ID_ROL_CARTERA/revocar (ya inactivo) → 409"
STATUS_REVOCAR_409=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/roles/$ID_ROL_CARTERA/revocar" \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" -d '{}')
[[ "$STATUS_REVOCAR_409" == "409" ]] \
  || fail "Revocar rol ya inactivo esperado 409, obtenido $STATUS_REVOCAR_409"
ok "Revocar rol ya inactivo → 409 ✓"

# UA4.10 Reactivar una asignación previamente revocada — sin duplicar la PK
info "POST /api/admin/usuarios/$ID_USUARIO_ET/roles (reasignar CARTERA_XPAY tras revocación) → 200"
REASIGNAR_CARTERA=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/roles" \
  '{"rolCodigo":"CARTERA_XPAY","observacion":"CI reactivación"}') \
  || fail "POST reasignar CARTERA_XPAY no respondió"
assert_ok "$REASIGNAR_CARTERA" "reasignar CARTERA_XPAY"
check_sql_value \
  "usuario_roles: una sola fila para (estadotest, CARTERA_XPAY) tras revocar+reasignar" \
  "SELECT COUNT(*) FROM usuario_roles WHERE id_usuario = $ID_USUARIO_ET AND id_rol = $ID_ROL_CARTERA" \
  "1"
ok "Reasignación reactiva la fila existente sin duplicar la PK ✓"

# UA4.11 Autoasignación rechazada
info "POST /api/admin/usuarios/$ID_USUARIO_ADMIN/roles (ci_admin_xpay sobre sí mismo) → 400"
STATUS_AUTOASIG=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ADMIN/roles" \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" \
  -d '{"rolCodigo":"GERENTE_XPAY"}')
[[ "$STATUS_AUTOASIG" == "400" ]] \
  || fail "Autoasignación esperada 400, obtenido $STATUS_AUTOASIG"
ok "Autoasignación → 400 ✓"

# Detalle propio de ci_admin_xpay — captura los idRol de ADMIN_XPAY y
# SUPERUSUARIO para los casos de autorrevocación y último administrador.
info "GET /api/admin/usuarios/$ID_USUARIO_ADMIN (detalle propio) → capturar idRol de ADMIN_XPAY/SUPERUSUARIO"
DETALLE_ADMIN_PROPIO=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_ADMIN") \
  || fail "GET detalle propio (ci_admin_xpay) no respondió"
ID_ROL_ADMIN_XPAY_PROPIO=$(echo "$DETALLE_ADMIN_PROPIO" | jq -r '.data.roles[] | select(.codigo=="ADMIN_XPAY") | .idRol')
ID_ROL_SUPERUSUARIO_PROPIO=$(echo "$DETALLE_ADMIN_PROPIO" | jq -r '.data.roles[] | select(.codigo=="SUPERUSUARIO") | .idRol')
[[ -n "$ID_ROL_ADMIN_XPAY_PROPIO" && "$ID_ROL_ADMIN_XPAY_PROPIO" != "null" ]] \
  || fail "No se pudo obtener idRol de ADMIN_XPAY para ci_admin_xpay"
[[ -n "$ID_ROL_SUPERUSUARIO_PROPIO" && "$ID_ROL_SUPERUSUARIO_PROPIO" != "null" ]] \
  || fail "No se pudo obtener idRol de SUPERUSUARIO para ci_admin_xpay"
ok "idRol capturados: ADMIN_XPAY=$ID_ROL_ADMIN_XPAY_PROPIO SUPERUSUARIO=$ID_ROL_SUPERUSUARIO_PROPIO"

# UA4.12 Autorrevocación rechazada
info "POST /api/admin/usuarios/$ID_USUARIO_ADMIN/roles/$ID_ROL_ADMIN_XPAY_PROPIO/revocar (autorrevocación) → 400"
STATUS_AUTOREV=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ADMIN/roles/$ID_ROL_ADMIN_XPAY_PROPIO/revocar" \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" -d '{}')
[[ "$STATUS_AUTOREV" == "400" ]] \
  || fail "Autorrevocación esperada 400, obtenido $STATUS_AUTOREV"
ok "Autorrevocación → 400 ✓"

# UA4.13 ADMIN_XPAY sin SUPERUSUARIO intenta asignar SUPERUSUARIO → 403
info "POST /api/admin/usuarios/$ID_USUARIO_ET/roles (SUPERUSUARIO, actor sin ese privilegio) → 403"
STATUS_ASIG_SUPER_403=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/roles" \
  -H "Authorization: Bearer $TOKEN_ADMIN_SOLO" -H "Content-Type: application/json" \
  -d '{"rolCodigo":"SUPERUSUARIO"}')
[[ "$STATUS_ASIG_SUPER_403" == "403" ]] \
  || fail "Asignar SUPERUSUARIO sin privilegio esperado 403, obtenido $STATUS_ASIG_SUPER_403"
ok "ADMIN_XPAY sin SUPERUSUARIO intenta asignar SUPERUSUARIO → 403 ✓"

# UA4.14 ADMIN_XPAY sin SUPERUSUARIO intenta revocar SUPERUSUARIO → 403
info "POST /api/admin/usuarios/$ID_USUARIO_ADMIN/roles/$ID_ROL_SUPERUSUARIO_PROPIO/revocar (actor sin ese privilegio) → 403"
STATUS_REV_SUPER_403=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ADMIN/roles/$ID_ROL_SUPERUSUARIO_PROPIO/revocar" \
  -H "Authorization: Bearer $TOKEN_ADMIN_SOLO" -H "Content-Type: application/json" -d '{}')
[[ "$STATUS_REV_SUPER_403" == "403" ]] \
  || fail "Revocar SUPERUSUARIO sin privilegio esperado 403, obtenido $STATUS_REV_SUPER_403"
ok "ADMIN_XPAY sin SUPERUSUARIO intenta revocar SUPERUSUARIO → 403 ✓"

# UA4.15 Rol técnico heredado rechazado al asignar
info "POST /api/admin/usuarios/$ID_USUARIO_ET/roles (ADMIN_XPAY, rol técnico heredado) → 400"
STATUS_ROL_HEREDADO=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/roles" \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" \
  -d '{"rolCodigo":"ADMIN_XPAY"}')
[[ "$STATUS_ROL_HEREDADO" == "400" ]] \
  || fail "Asignar rol técnico heredado esperado 400, obtenido $STATUS_ROL_HEREDADO"
ok "Asignar rol técnico heredado (ADMIN_XPAY) → 400 ✓"

# UA4.16 USUARIO_FINAL → 403 en los 3 endpoints
info "Los 3 endpoints de roles con USUARIO_FINAL (carlos) → 403"
STATUS_UF_ASIGNABLES=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -H "Authorization: Bearer $TOKEN_A" "$API_URL/api/admin/roles/asignables")
[[ "$STATUS_UF_ASIGNABLES" == "403" ]] \
  || fail "GET roles/asignables con USUARIO_FINAL esperado 403, obtenido $STATUS_UF_ASIGNABLES"
STATUS_UF_ASIGNAR=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/roles" \
  -H "Authorization: Bearer $TOKEN_A" -H "Content-Type: application/json" \
  -d '{"rolCodigo":"CARTERA_XPAY"}')
[[ "$STATUS_UF_ASIGNAR" == "403" ]] \
  || fail "POST roles con USUARIO_FINAL esperado 403, obtenido $STATUS_UF_ASIGNAR"
STATUS_UF_REVOCAR=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/roles/$ID_ROL_CARTERA/revocar" \
  -H "Authorization: Bearer $TOKEN_A" -H "Content-Type: application/json" -d '{}')
[[ "$STATUS_UF_REVOCAR" == "403" ]] \
  || fail "POST revocar con USUARIO_FINAL esperado 403, obtenido $STATUS_UF_REVOCAR"
ok "Los 3 endpoints de roles con USUARIO_FINAL → 403 ✓"

# UA4.17 Sin token → 401 en los 3 endpoints
info "Los 3 endpoints de roles sin token → 401"
STATUS_NA_ASIGNABLES=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 "$API_URL/api/admin/roles/asignables")
[[ "$STATUS_NA_ASIGNABLES" == "401" ]] \
  || fail "GET roles/asignables sin token esperado 401, obtenido $STATUS_NA_ASIGNABLES"
STATUS_NA_ASIGNAR=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/roles" -H "Content-Type: application/json" \
  -d '{"rolCodigo":"CARTERA_XPAY"}')
[[ "$STATUS_NA_ASIGNAR" == "401" ]] \
  || fail "POST roles sin token esperado 401, obtenido $STATUS_NA_ASIGNAR"
STATUS_NA_REVOCAR=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/roles/$ID_ROL_CARTERA/revocar" \
  -H "Content-Type: application/json" -d '{}')
[[ "$STATUS_NA_REVOCAR" == "401" ]] \
  || fail "POST revocar sin token esperado 401, obtenido $STATUS_NA_REVOCAR"
ok "Los 3 endpoints de roles sin token → 401 ✓"

# UA4.18 No dejar a un usuario sin ningún rol activo
info "POST /api/usuarios/registro-final (usuario con un solo rol, para probar 'no dejar sin roles')"
REGISTRO_SR=$(post_json "$API_URL/api/usuarios/registro-final" '{
  "idUnidadNegocio": 1,
  "tipoDocumento": "CC",
  "numeroDocumento": "1099008888",
  "primerNombre": "Solo",
  "primerApellido": "Rol",
  "celular": "3008887777",
  "email": "solo.rol@ci-test.com",
  "usuario": "solorol_ci_test",
  "password": "Xpay@Test1!"
}') || fail "POST registro solorol no respondió"
assert_ok "$REGISTRO_SR" "registro solorol"
ID_USUARIO_SR=$(echo "$REGISTRO_SR" | jq -r '.idUsuario')

DETALLE_SR=$(get_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_SR") \
  || fail "GET detalle solorol no respondió"
ID_ROL_USUARIO_FINAL_SR=$(echo "$DETALLE_SR" | jq -r '.data.roles[] | select(.codigo=="USUARIO_FINAL") | .idRol')
[[ -n "$ID_ROL_USUARIO_FINAL_SR" && "$ID_ROL_USUARIO_FINAL_SR" != "null" ]] \
  || fail "No se pudo obtener idRol de USUARIO_FINAL para solorol"

info "POST /api/admin/usuarios/$ID_USUARIO_SR/roles/$ID_ROL_USUARIO_FINAL_SR/revocar (único rol) → 400"
STATUS_SIN_ROLES=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_SR/roles/$ID_ROL_USUARIO_FINAL_SR/revocar" \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" -d '{}')
[[ "$STATUS_SIN_ROLES" == "400" ]] \
  || fail "Revocar único rol esperado 400, obtenido $STATUS_SIN_ROLES"
ok "No dejar usuario sin roles → 400 ✓"

# UA4.19 Protección del último administrador (revocación de rol) — reutiliza
# el mecanismo de trap EXIT ya construido en FASE USUARIOS-ADMIN-3, esta vez
# inactivando temporalmente ci_admin_guard Y ci_admin_solo (ambos deben
# quedar fuera para que ci_admin_xpay sea el único administrador activo).
restaurar_admins_guard_solo() {
  "$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
    -Q "SET NOCOUNT ON; UPDATE usuarios SET estado='ACTIVO' WHERE usuario IN ('ci_admin_guard','ci_admin_solo');" \
    > /dev/null 2>&1 || true
}
trap restaurar_admins_guard_solo EXIT

"$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET NOCOUNT ON; UPDATE usuarios SET estado='INACTIVO' WHERE usuario IN ('ci_admin_guard','ci_admin_solo');" \
  > /dev/null || fail "No se pudo inactivar ci_admin_guard/ci_admin_solo por SQL directo"
ok "ci_admin_guard y ci_admin_solo inactivados temporalmente (se restauran al finalizar) ✓"

check_sql_value \
  "Administradores ACTIVO con ADMIN_XPAY/SUPERUSUARIO = 1 (solo ci_admin_xpay)" \
  "SELECT COUNT(DISTINCT u.id_usuario) FROM usuarios u JOIN usuario_roles ur ON ur.id_usuario = u.id_usuario JOIN roles r ON r.id_rol = ur.id_rol WHERE u.estado = 'ACTIVO' AND ur.estado = 'ACTIVO' AND r.codigo IN ('ADMIN_XPAY','SUPERUSUARIO')" \
  "1"

# ci_admin_guard ya está INACTIVO en este punto — un login nuevo fallaría con
# 400 ("El usuario no está activo."), comportamiento ya validado en FASE
# USUARIOS-ADMIN-3. Para esta prueba se reutiliza deliberadamente el JWT de
# ci_admin_guard obtenido en esa fase anterior (TOKEN_ADMIN_GUARD, todavía
# vigente dentro de la misma ejecución de CI) — documentado explícitamente
# como mecanismo controlado de CI, no como comportamiento a construir en
# producción.
[[ -n "${TOKEN_ADMIN_GUARD:-}" ]] || fail "TOKEN_ADMIN_GUARD (de FASE USUARIOS-ADMIN-3) no está disponible para esta prueba"

info "POST /api/admin/usuarios/$ID_USUARIO_ADMIN/roles/$ID_ROL_SUPERUSUARIO_PROPIO/revocar (TOKEN_ADMIN_GUARD, único admin restante) → 400"
STATUS_ULTIMO_ADMIN_ROL=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ADMIN/roles/$ID_ROL_SUPERUSUARIO_PROPIO/revocar" \
  -H "Authorization: Bearer $TOKEN_ADMIN_GUARD" -H "Content-Type: application/json" -d '{}')
[[ "$STATUS_ULTIMO_ADMIN_ROL" == "400" ]] \
  || fail "Revocar rol del último administrador esperado 400, obtenido $STATUS_ULTIMO_ADMIN_ROL"
ok "Protección del último administrador (revocar rol) → 400 ✓"

"$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET NOCOUNT ON; UPDATE usuarios SET estado='ACTIVO' WHERE usuario IN ('ci_admin_guard','ci_admin_solo');" \
  > /dev/null || fail "No se pudo restaurar ci_admin_guard/ci_admin_solo a ACTIVO"
ok "ci_admin_guard y ci_admin_solo restaurados a ACTIVO ✓"
trap - EXIT

# UA4.20 Auditoría persistente — asignación (>=1: hubo asignación inicial +
# reactivación tras revocar) y revocación (exactamente 1: solo un revocar
# exitoso; el segundo intento fue rechazado con 409 antes de auditar).
AUDIT_ASIGNAR_COUNT=$("$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM auditoria WHERE modulo='USUARIOS_ADMIN' AND accion='USUARIO_ROL_ASIGNAR' AND entidad='usuario_roles' AND id_entidad='$ID_USUARIO_ET:$ID_ROL_CARTERA' AND resultado='EXITOSO'" \
  -h -1 | tr -d ' \r\n')
[[ "$AUDIT_ASIGNAR_COUNT" -ge 1 ]] \
  || fail "Auditoría USUARIO_ROL_ASIGNAR esperada >=1, obtenida $AUDIT_ASIGNAR_COUNT"
ok "Auditoría USUARIO_ROL_ASIGNAR registrada ($AUDIT_ASIGNAR_COUNT fila(s)) ✓"

check_sql_value \
  "Auditoría USUARIO_ROL_REVOCAR registrada para id_entidad=$ID_USUARIO_ET:$ID_ROL_CARTERA" \
  "SELECT COUNT(*) FROM auditoria WHERE modulo='USUARIOS_ADMIN' AND accion='USUARIO_ROL_REVOCAR' AND entidad='usuario_roles' AND id_entidad='$ID_USUARIO_ET:$ID_ROL_CARTERA' AND resultado='EXITOSO'" \
  "1"

echo ""

# ════════════════════════════════════════════════════
# FASE USUARIOS-ADMIN-5 — Restablecimiento de contraseña
# ════════════════════════════════════════════════════
phase "FASE USUARIOS-ADMIN-5: Restablecimiento de contraseña"

# UA5.1 Restablecimiento exitoso — captura claveTemporal para los pasos siguientes
info "POST /api/admin/usuarios/$ID_USUARIO_ET/restablecer-clave (TOKEN_ADMIN) → 200"
RESET_ET=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/restablecer-clave" \
  '{"observacion":"CI USUARIOS-ADMIN-5"}') \
  || fail "POST restablecer-clave estadotest no respondió"
assert_ok "$RESET_ET" "restablecer-clave estadotest"
CLAVE_TEMPORAL_ET=$(echo "$RESET_ET" | jq -r '.data.claveTemporal')
[[ -n "$CLAVE_TEMPORAL_ET" && "$CLAVE_TEMPORAL_ET" != "null" ]] \
  || fail "claveTemporal vacía o ausente en la respuesta de restablecer-clave"
ok "Restablecimiento exitoso → claveTemporal capturada (longitud=${#CLAVE_TEMPORAL_ET}) ✓"

# UA5.2 La respuesta contiene claveTemporal pero NUNCA un hash ni passwordHash
RESET_ET_LEAK=$(echo "$RESET_ET" | grep -i "passwordHash\|\\\$2a\\\$" || true)
[[ -z "$RESET_ET_LEAK" ]] \
  || fail "La respuesta de restablecer-clave no debe contener hash ni passwordHash: $RESET_ET_LEAK"
ok "Respuesta de restablecer-clave → sin hash ni passwordHash ✓"

# UA5.3 El login anterior (Xpay@Test1!) deja de funcionar de inmediato
info "POST /api/auth/login (estadotest, clave anterior) → 400"
STATUS_LOGIN_CLAVE_ANTERIOR=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/auth/login" -H "Content-Type: application/json" \
  -d '{"usuario":"estadotest_ci_test","password":"Xpay@Test1!"}')
[[ "$STATUS_LOGIN_CLAVE_ANTERIOR" == "400" ]] \
  || fail "Login con clave anterior tras restablecer esperado 400, obtenido $STATUS_LOGIN_CLAVE_ANTERIOR"
ok "Login con clave anterior tras restablecer → 400 ✓"

# UA5.4 / UA5.5 Login con la clave temporal funciona y requiereCambioClave=true
info "POST /api/auth/login (estadotest, clave temporal) → 200, requiereCambioClave=true"
LOGIN_ET_TEMP=$(post_json "$API_URL/api/auth/login" \
  "{\"usuario\":\"estadotest_ci_test\",\"password\":\"$CLAVE_TEMPORAL_ET\"}") \
  || fail "Login estadotest con clave temporal no respondió"
assert_ok "$LOGIN_ET_TEMP" "login estadotest con clave temporal"
TOKEN_ET_TEMP=$(echo "$LOGIN_ET_TEMP" | jq -r '.data.token')
[[ -n "$TOKEN_ET_TEMP" && "$TOKEN_ET_TEMP" != "null" ]] || fail "Token JWT vacío tras login con clave temporal"
REQUIERE_CAMBIO_LOGIN=$(echo "$LOGIN_ET_TEMP" | jq -r '.data.requiereCambioClave')
[[ "$REQUIERE_CAMBIO_LOGIN" == "true" ]] \
  || fail "Login con clave temporal: requiereCambioClave esperado true, obtenido $REQUIERE_CAMBIO_LOGIN"
ok "Login con clave temporal → 200, requiereCambioClave=true ✓"

# UA5.6 Enforcement real: con el JWT de clave temporal, un endpoint normal → 403
info "GET /api/wallets/mi-wallet (TOKEN_ET_TEMP, requiere cambio de clave) → 403"
STATUS_ENDPOINT_BLOQUEADO=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -H "Authorization: Bearer $TOKEN_ET_TEMP" "$API_URL/api/wallets/mi-wallet")
[[ "$STATUS_ENDPOINT_BLOQUEADO" == "403" ]] \
  || fail "Endpoint normal con requiereCambioClave=true esperado 403, obtenido $STATUS_ENDPOINT_BLOQUEADO"
ok "Enforcement real (ClaveVigenteAuthorizationHandler) → endpoint normal bloqueado con 403 ✓"

# UA5.7 Cambio obligatorio de contraseña — exento del enforcement anterior
NUEVA_CLAVE_ET='Xpay@Nueva1!'
info "POST /api/auth/cambiar-clave-obligatoria (TOKEN_ET_TEMP) → 200"
CAMBIO_OBLIGATORIO_ET=$(curl -sf -X POST "$API_URL/api/auth/cambiar-clave-obligatoria" \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN_ET_TEMP" --max-time 15 \
  -d "{\"claveActual\":\"$CLAVE_TEMPORAL_ET\",\"claveNueva\":\"$NUEVA_CLAVE_ET\",\"confirmacionClaveNueva\":\"$NUEVA_CLAVE_ET\"}") \
  || fail "POST cambiar-clave-obligatoria no respondió"
assert_ok "$CAMBIO_OBLIGATORIO_ET" "cambiar-clave-obligatoria"
REQUIERE_CAMBIO_POST=$(echo "$CAMBIO_OBLIGATORIO_ET" | jq -r '.data.requiereCambioClave')
[[ "$REQUIERE_CAMBIO_POST" == "false" ]] \
  || fail "Tras cambiar-clave-obligatoria: requiereCambioClave esperado false, obtenido $REQUIERE_CAMBIO_POST"
ok "cambiar-clave-obligatoria (exento del enforcement) → 200, requiereCambioClave=false ✓"

# UA5.8 La clave temporal deja de funcionar tras el cambio obligatorio
info "POST /api/auth/login (estadotest, clave temporal ya usada) → 400"
STATUS_LOGIN_TEMP_USADA=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/auth/login" -H "Content-Type: application/json" \
  -d "{\"usuario\":\"estadotest_ci_test\",\"password\":\"$CLAVE_TEMPORAL_ET\"}")
[[ "$STATUS_LOGIN_TEMP_USADA" == "400" ]] \
  || fail "Login con clave temporal ya usada esperado 400, obtenido $STATUS_LOGIN_TEMP_USADA"
ok "Login con clave temporal ya usada → 400 ✓"

# UA5.9 / UA5.10 La clave nueva funciona y requiereCambioClave=false
info "POST /api/auth/login (estadotest, clave nueva) → 200, requiereCambioClave=false"
LOGIN_ET_NUEVA=$(post_json "$API_URL/api/auth/login" \
  "{\"usuario\":\"estadotest_ci_test\",\"password\":\"$NUEVA_CLAVE_ET\"}") \
  || fail "Login estadotest con clave nueva no respondió"
assert_ok "$LOGIN_ET_NUEVA" "login estadotest con clave nueva"
REQUIERE_CAMBIO_FINAL=$(echo "$LOGIN_ET_NUEVA" | jq -r '.data.requiereCambioClave')
[[ "$REQUIERE_CAMBIO_FINAL" == "false" ]] \
  || fail "Login con clave nueva: requiereCambioClave esperado false, obtenido $REQUIERE_CAMBIO_FINAL"
ok "Login con clave nueva → 200, requiereCambioClave=false ✓"

# UA5.11 ADMIN_XPAY sin SUPERUSUARIO no puede restablecer a un SUPERUSUARIO
ID_USUARIO_ADMIN_GUARD=$(echo "$LOGIN_ADMIN_GUARD" | jq -r '.data.idUsuario')
info "POST /api/admin/usuarios/$ID_USUARIO_ADMIN_GUARD/restablecer-clave (TOKEN_ADMIN_SOLO sobre ci_admin_guard, SUPERUSUARIO) → 403"
STATUS_RESET_SUPER_403=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ADMIN_GUARD/restablecer-clave" \
  -H "Authorization: Bearer $TOKEN_ADMIN_SOLO" -H "Content-Type: application/json" -d '{}')
[[ "$STATUS_RESET_SUPER_403" == "403" ]] \
  || fail "ADMIN_XPAY sin SUPERUSUARIO restableciendo a un SUPERUSUARIO esperado 403, obtenido $STATUS_RESET_SUPER_403"
ok "ADMIN_XPAY sin SUPERUSUARIO → restablecer clave de SUPERUSUARIO → 403 ✓"

# UA5.12 SUPERUSUARIO sí puede restablecer a otro SUPERUSUARIO
info "POST /api/admin/usuarios/$ID_USUARIO_ADMIN_GUARD/restablecer-clave (TOKEN_ADMIN, con SUPERUSUARIO) → 200"
RESET_GUARD=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_ADMIN_GUARD/restablecer-clave" '{}') \
  || fail "POST restablecer-clave ci_admin_guard no respondió"
assert_ok "$RESET_GUARD" "restablecer-clave ci_admin_guard"
ok "SUPERUSUARIO restablece clave de otro SUPERUSUARIO → 200 ✓"

# UA5.13 Auto-restablecimiento → 400
info "POST /api/admin/usuarios/$ID_USUARIO_ADMIN/restablecer-clave (ci_admin_xpay sobre sí mismo) → 400"
STATUS_AUTO_RESET=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ADMIN/restablecer-clave" \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" -d '{}')
[[ "$STATUS_AUTO_RESET" == "400" ]] \
  || fail "Auto-restablecimiento esperado 400, obtenido $STATUS_AUTO_RESET"
ok "Auto-restablecimiento → 400 ✓"

# UA5.14 INACTIVO/BLOQUEADO → 409 (inactiva temporalmente estadotest por el
# endpoint real, ya validado en FASE USUARIOS-ADMIN-3; se restaura al finalizar
# el bloque, incluso si algún assert posterior falla).
restaurar_estadotest_activo() {
  "$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
    -Q "SET NOCOUNT ON; UPDATE usuarios SET estado='ACTIVO' WHERE usuario='estadotest_ci_test';" \
    > /dev/null 2>&1 || true
}
trap restaurar_estadotest_activo EXIT

info "POST /api/admin/usuarios/$ID_USUARIO_ET/inactivar (para probar precondición ACTIVO) → 200"
INACTIVAR_ET_UA5=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/inactivar" "{}") \
  || fail "POST inactivar estadotest (UA5.14) no respondió"
assert_ok "$INACTIVAR_ET_UA5" "inactivar estadotest (UA5.14)"

info "POST /api/admin/usuarios/$ID_USUARIO_ET/restablecer-clave (usuario INACTIVO) → 409"
STATUS_RESET_INACTIVO=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/restablecer-clave" \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" -d '{}')
[[ "$STATUS_RESET_INACTIVO" == "409" ]] \
  || fail "Restablecer clave de usuario INACTIVO esperado 409, obtenido $STATUS_RESET_INACTIVO"
ok "Restablecer clave de usuario INACTIVO → 409 ✓"

info "POST /api/admin/usuarios/$ID_USUARIO_ET/activar (restaurar estado tras UA5.14) → 200"
ACTIVAR_ET_UA5=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/activar" "{}") \
  || fail "POST activar estadotest (UA5.14) no respondió"
assert_ok "$ACTIVAR_ET_UA5" "activar estadotest (UA5.14)"
trap - EXIT
ok "estadotest restaurado a ACTIVO ✓"

# UA5.15 USUARIO_FINAL (carlos) → 403
info "POST /api/admin/usuarios/$ID_USUARIO_ET/restablecer-clave (USUARIO_FINAL) → 403"
STATUS_RESET_UF_403=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/restablecer-clave" \
  -H "Authorization: Bearer $TOKEN_A" -H "Content-Type: application/json" -d '{}')
[[ "$STATUS_RESET_UF_403" == "403" ]] \
  || fail "Restablecer clave con USUARIO_FINAL esperado 403, obtenido $STATUS_RESET_UF_403"
ok "Restablecer clave con USUARIO_FINAL → 403 ✓"

# UA5.16 Sin token → 401
info "POST /api/admin/usuarios/$ID_USUARIO_ET/restablecer-clave (sin token) → 401"
STATUS_RESET_401=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/restablecer-clave" -H "Content-Type: application/json" -d '{}')
[[ "$STATUS_RESET_401" == "401" ]] \
  || fail "Restablecer clave sin token esperado 401, obtenido $STATUS_RESET_401"
ok "Restablecer clave sin token → 401 ✓"

# UA5.17 Auditoría persistente sin clave ni hash
check_sql_value \
  "Auditoría USUARIO_RESTABLECER_CLAVE registrada para id_entidad=$ID_USUARIO_ET (UA5.1)" \
  "SELECT COUNT(*) FROM auditoria WHERE modulo='USUARIOS_ADMIN' AND accion='USUARIO_RESTABLECER_CLAVE' AND entidad='usuarios' AND id_entidad='$ID_USUARIO_ET' AND resultado='EXITOSO'" \
  "1"
check_sql_value \
  "Auditoría USUARIO_RESTABLECER_CLAVE (UA5.1): valor_anterior y valor_nuevo son NULL" \
  "SELECT COUNT(*) FROM auditoria WHERE modulo='USUARIOS_ADMIN' AND accion='USUARIO_RESTABLECER_CLAVE' AND id_entidad='$ID_USUARIO_ET' AND valor_anterior IS NULL AND valor_nuevo IS NULL" \
  "1"
AUDIT_CLAVE_LEAK=$("$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM auditoria WHERE modulo='USUARIOS_ADMIN' AND accion='USUARIO_RESTABLECER_CLAVE' AND (observacion LIKE '%$CLAVE_TEMPORAL_ET%' OR CAST(valor_anterior AS NVARCHAR(MAX)) LIKE '%\$2a\$%' OR CAST(valor_nuevo AS NVARCHAR(MAX)) LIKE '%\$2a\$%')" \
  -h -1 | tr -d ' \r\n')
[[ "$AUDIT_CLAVE_LEAK" == "0" ]] \
  || fail "Auditoría USUARIO_RESTABLECER_CLAVE no debe contener la clave temporal ni ningún hash — filas encontradas: $AUDIT_CLAVE_LEAK"
ok "Auditoría USUARIO_RESTABLECER_CLAVE → sin clave temporal ni hash ✓"

# UA5.18 cambiar-clave-obligatoria rechaza política de contraseña inválida
info "POST /api/admin/usuarios/$ID_USUARIO_ET/restablecer-clave (para probar política de cambiar-clave-obligatoria)"
RESET_ET_UA518=$(post_auth_json "$TOKEN_ADMIN" "$API_URL/api/admin/usuarios/$ID_USUARIO_ET/restablecer-clave" '{}') \
  || fail "POST restablecer-clave (UA5.18) no respondió"
assert_ok "$RESET_ET_UA518" "restablecer-clave estadotest (UA5.18)"
CLAVE_TEMPORAL_UA518=$(echo "$RESET_ET_UA518" | jq -r '.data.claveTemporal')

LOGIN_ET_UA518=$(post_json "$API_URL/api/auth/login" \
  "{\"usuario\":\"estadotest_ci_test\",\"password\":\"$CLAVE_TEMPORAL_UA518\"}") \
  || fail "Login estadotest (UA5.18) no respondió"
assert_ok "$LOGIN_ET_UA518" "login estadotest (UA5.18)"
TOKEN_ET_UA518=$(echo "$LOGIN_ET_UA518" | jq -r '.data.token')

info "POST /api/auth/cambiar-clave-obligatoria (contraseña sin carácter especial) → 400"
STATUS_POLITICA_400=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/auth/cambiar-clave-obligatoria" \
  -H "Authorization: Bearer $TOKEN_ET_UA518" -H "Content-Type: application/json" \
  -d "{\"claveActual\":\"$CLAVE_TEMPORAL_UA518\",\"claveNueva\":\"Abc12345\",\"confirmacionClaveNueva\":\"Abc12345\"}")
[[ "$STATUS_POLITICA_400" == "400" ]] \
  || fail "cambiar-clave-obligatoria con política inválida esperado 400, obtenido $STATUS_POLITICA_400"
ok "cambiar-clave-obligatoria con contraseña sin carácter especial → 400 ✓"

echo ""

# ════════════════════════════════════════════════════
# FASE 35 — Observabilidad básica: diagnostics y correlation id
# ════════════════════════════════════════════════════
phase "FASE 35: Observabilidad básica — diagnostics/ping y X-Correlation-ID"

# 35.1 GET /api/diagnostics/ping → 200 + status=OK
info "GET /api/diagnostics/ping → debe retornar 200 + status OK"
STATUS_PING=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 "$API_URL/api/diagnostics/ping")
[[ "$STATUS_PING" == "200" ]] || fail "GET /api/diagnostics/ping esperado 200, obtenido $STATUS_PING"
BODY_PING=$(get_json "$API_URL/api/diagnostics/ping") || fail "GET /api/diagnostics/ping no respondió"
echo "$BODY_PING" | jq .
PING_STATUS=$(echo "$BODY_PING" | jq -r '.status')
[[ "$PING_STATUS" == "OK" ]] || fail "/api/diagnostics/ping → status esperado 'OK', obtenido '$PING_STATUS'"
ok "GET /api/diagnostics/ping → 200 / status=OK ✓"

# 35.2 Response incluye header X-Correlation-ID no vacío
info "GET /api/diagnostics/ping → response debe incluir header X-Correlation-ID"
PING_HEADERS=$(curl -si --max-time 15 "$API_URL/api/diagnostics/ping")
echo "$PING_HEADERS" | grep -qi "x-correlation-id:" || fail "Header X-Correlation-ID ausente en response"
PING_CID=$(echo "$PING_HEADERS" | grep -i "x-correlation-id:" | tr -d '\r' | awk '{print $2}')
[[ -n "$PING_CID" ]] || fail "X-Correlation-ID header vacío en response"
ok "X-Correlation-ID header presente → $PING_CID ✓"

# 35.3 Enviar X-Correlation-ID y verificar que el response lo devuelve igual (echo)
info "Enviar X-Correlation-ID: QA-CID-001 → response debe devolver el mismo valor"
PING_ECHO_HEADERS=$(curl -si --max-time 15 -H "X-Correlation-ID: QA-CID-001" "$API_URL/api/diagnostics/ping")
PING_ECHO_CID=$(echo "$PING_ECHO_HEADERS" | grep -i "x-correlation-id:" | tr -d '\r' | awk '{print $2}')
[[ "$PING_ECHO_CID" == "QA-CID-001" ]] \
  || fail "X-Correlation-ID echo esperado 'QA-CID-001', obtenido '$PING_ECHO_CID'"
ok "X-Correlation-ID echo → QA-CID-001 ✓"

echo ""

# ════════════════════════════════════════════════════
# FASE 37 — Security headers básicos
# ════════════════════════════════════════════════════
phase "FASE 37: Security headers — X-Content-Type-Options, X-Frame-Options, Referrer-Policy"

SEC_HEADERS=$(curl -si --max-time 15 "$API_URL/api/diagnostics/ping")

# 37.1 X-Content-Type-Options: nosniff
XCTO=$(echo "$SEC_HEADERS" | grep -i "^x-content-type-options:" | tr -d '\r' | awk '{print $2}')
[[ "$XCTO" == "nosniff" ]] \
  || fail "X-Content-Type-Options esperado 'nosniff', obtenido '${XCTO:-ausente}' — verificar SecurityHeaders:EnableSecurityHeaders=true"
ok "X-Content-Type-Options: nosniff ✓"

# 37.2 X-Frame-Options: DENY
XFO=$(echo "$SEC_HEADERS" | grep -i "^x-frame-options:" | tr -d '\r' | awk '{print $2}')
[[ "$XFO" == "DENY" ]] \
  || fail "X-Frame-Options esperado 'DENY', obtenido '${XFO:-ausente}'"
ok "X-Frame-Options: DENY ✓"

# 37.3 Referrer-Policy: no-referrer
RP=$(echo "$SEC_HEADERS" | grep -i "^referrer-policy:" | tr -d '\r' | awk '{print $2}')
[[ "$RP" == "no-referrer" ]] \
  || fail "Referrer-Policy esperado 'no-referrer', obtenido '${RP:-ausente}'"
ok "Referrer-Policy: no-referrer ✓"

echo ""

# ════════════════════════════════════════════════════
# FASE 38 — Rate limiting básico: login no bloqueado
# ════════════════════════════════════════════════════
phase "FASE 38: Rate limiting — login normal no bloqueado"

# 38.1 Login normal → 200 (no 429)
# Se verifica que una sola llamada a login no activa el rate limiter.
# Prueba agresiva (>20 reqs/min) se realiza manualmente; no se incluye en CI para evitar fragilidad.
info "POST /api/auth/login → debe retornar 200, no 429 (rate limiting activo con límite amplio)"
STATUS_LOGIN_RL=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"usuario":"carlos_ci_test","password":"Xpay@Test1!"}')
[[ "$STATUS_LOGIN_RL" == "200" ]] \
  || fail "POST /api/auth/login esperado 200 (no debe estar bloqueado por rate limiting), obtenido $STATUS_LOGIN_RL"
ok "Login normal → 200 (no bloqueado por rate limiting) ✓"

echo ""

# ════════════════════════════════════════════════════
# FASE 39 — Auditoría básica por logs
# ════════════════════════════════════════════════════
phase "FASE 39: Auditoría básica por logs (validación documental)"

# La auditoría emite eventos ILogger; no genera output HTTP observable en CI.
# Login exitoso y fallido ya se validan en FASE 1 y FASE 8 respectivamente.
# Los eventos de auditoría (LOGIN_SUCCESS, QR_PAYMENT_ATTEMPT, ADMIN_LEDGER_ACCESS, etc.)
# deben verificarse manualmente en los logs del backend durante una sesión QA activa.
# Búsqueda sugerida: grep 'AUDIT' en logs o filtrar por campo audit=True en log aggregator.
info "Auditoría por logs: validación manual — ver docs/QA_DEPLOYMENT_RUNBOOK.md sección smoke test"
ok "FASE 39 documentada — validación en logs manuales; funcionalidad cubierta por fases anteriores ✓"

echo ""

# ════════════════════════════════════════════════════
# FASE 40 — Hardening CORS por ambiente
# ════════════════════════════════════════════════════
phase "FASE 40: Hardening CORS — origen permitido y origen no permitido"

FRONTEND_ORIGIN="${FRONTEND_ORIGIN:-http://localhost:5173}"

# 40.1 Preflight desde origen permitido → Access-Control-Allow-Origin presente y correcto
info "OPTIONS /api/auth/login con Origin: $FRONTEND_ORIGIN → debe devolver Access-Control-Allow-Origin correcto"
CORS40_ALLOW_RESPONSE=$(curl -si -X OPTIONS \
  -H "Origin: $FRONTEND_ORIGIN" \
  -H "Access-Control-Request-Method: POST" \
  -H "Access-Control-Request-Headers: Content-Type,Authorization" \
  --max-time 15 \
  "$API_URL/api/auth/login")
CORS40_ALLOW=$(echo "$CORS40_ALLOW_RESPONSE" | grep -i "access-control-allow-origin:" \
  | tr -d '\r' | awk '{print $2}')
[[ "$CORS40_ALLOW" == "$FRONTEND_ORIGIN" ]] \
  || fail "CORS: Access-Control-Allow-Origin esperado '$FRONTEND_ORIGIN', obtenido '$CORS40_ALLOW'"
ok "CORS origen permitido → Access-Control-Allow-Origin: $CORS40_ALLOW ✓"

# 40.2 Preflight desde origen no permitido → sin Access-Control-Allow-Origin con ese valor
info "OPTIONS /api/auth/login con Origin: https://evil.example.com → NO debe devolver ese origen permitido"
CORS40_EVIL_RESPONSE=$(curl -si -X OPTIONS \
  -H "Origin: https://evil.example.com" \
  -H "Access-Control-Request-Method: POST" \
  --max-time 15 \
  "$API_URL/api/auth/login")
# grep puede retornar exit 1 si no encuentra el header (caso esperado); || true previene fallo del script
CORS40_EVIL=$(echo "$CORS40_EVIL_RESPONSE" | grep -i "access-control-allow-origin:" \
  | tr -d '\r' | awk '{print $2}' || true)
[[ "$CORS40_EVIL" != "https://evil.example.com" ]] \
  || fail "CORS: origen no permitido recibió Access-Control-Allow-Origin: $CORS40_EVIL"
ok "CORS origen no permitido → sin Access-Control-Allow-Origin: https://evil.example.com ✓"

echo ""

# ════════════════════════════════════════════════════
# FASE 41 — Manejo global de errores seguro
# ════════════════════════════════════════════════════
phase "FASE 41: Error handling global — JSON 500 seguro sin stack trace"

# 41.1 GET /api/diagnostics/error-test → HTTP 500
info "GET /api/diagnostics/error-test → debe retornar HTTP 500 con JSON seguro"
ERR41_STATUS=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/api/diagnostics/error-test")
[[ "$ERR41_STATUS" == "500" ]] \
  || fail "error-test esperado 500, obtenido $ERR41_STATUS — verificar Diagnostics:EnableErrorTestEndpoint=true"
ok "error-test → HTTP 500 ✓"

# 41.2 Verificar campos JSON del cuerpo
ERR41_BODY=$(curl -s --max-time 15 "$API_URL/api/diagnostics/error-test")

ERR41_SUCCESS=$(echo "$ERR41_BODY" | jq -r '.success')
[[ "$ERR41_SUCCESS" == "false" ]] \
  || fail "error-test: success esperado false, obtenido '$ERR41_SUCCESS'"
ok "error-test JSON → success=false ✓"

ERR41_CODE=$(echo "$ERR41_BODY" | jq -r '.error')
[[ "$ERR41_CODE" == "internal_server_error" ]] \
  || fail "error-test: error esperado 'internal_server_error', obtenido '$ERR41_CODE'"
ok "error-test JSON → error=internal_server_error ✓"

ERR41_CID=$(echo "$ERR41_BODY" | jq -r '.correlationId')
[[ -n "$ERR41_CID" && "$ERR41_CID" != "null" ]] \
  || fail "error-test: correlationId vacío o null en body 500"
ok "error-test JSON → correlationId presente: $ERR41_CID ✓"

# 41.3 Body NO debe contener stack trace ni mensaje interno
ERR41_LEAK=$(echo "$ERR41_BODY" | grep -i "Intentional\|StackTrace\|Exception\|InnerException" || true)
[[ -z "$ERR41_LEAK" ]] \
  || fail "error-test: body contiene información interna: $ERR41_LEAK"
ok "error-test → sin stack trace ni mensaje interno ✓"

# 41.4 X-Correlation-ID presente en headers de la respuesta 500
ERR41_XCID=$(curl -si --max-time 15 "$API_URL/api/diagnostics/error-test" \
  | grep -i "x-correlation-id:" | tr -d '\r' | awk '{print $2}' || true)
[[ -n "$ERR41_XCID" ]] \
  || fail "error-test: header X-Correlation-ID ausente en respuesta 500"
ok "error-test → X-Correlation-ID: $ERR41_XCID presente en headers ✓"

echo ""
echo ""

# ════════════════════════════════════════════════════
# FASE 42 — Política básica de sesión JWT
# ════════════════════════════════════════════════════
phase "FASE 42: Política de sesión JWT — token válido, claim exp presente, 401 sin token"

# 42.1 Token de FASE 1 (TOKEN_A) sigue siendo válido — confirmar en endpoint protegido
info "Token de sesión → /api/wallets/mi-wallet debe retornar 200"
STATUS_42_AUTH=$(curl -s -o /dev/null -w "%{http_code}" \
  -H "Authorization: Bearer $TOKEN_A" \
  --max-time 15 \
  "$API_URL/api/wallets/mi-wallet")
[[ "$STATUS_42_AUTH" == "200" ]] \
  || fail "FASE 42: endpoint protegido con token válido esperado 200, obtenido $STATUS_42_AUTH"
ok "FASE 42: token válido → /api/wallets/mi-wallet = 200 ✓"

# 42.2 Sin token → 401 en endpoint protegido (confirmación mínima; validado en fases anteriores)
STATUS_42_NOAUTH=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  "$API_URL/api/admin/wallets")
[[ "$STATUS_42_NOAUTH" == "401" ]] \
  || fail "FASE 42: endpoint protegido sin token esperado 401, obtenido $STATUS_42_NOAUTH"
ok "FASE 42: sin token → /api/admin/wallets = 401 ✓"

# 42.3 Claim 'exp' presente en payload JWT (decodificación local, sin verificar firma)
info "Decodificando payload JWT para verificar claim 'exp'"
JWT_RAW_PAYLOAD=$(echo "$TOKEN_A" | cut -d'.' -f2)
case $((${#JWT_RAW_PAYLOAD} % 4)) in
    2) JWT_PAD="${JWT_RAW_PAYLOAD}==" ;;
    3) JWT_PAD="${JWT_RAW_PAYLOAD}=" ;;
    *) JWT_PAD="$JWT_RAW_PAYLOAD" ;;
esac
JWT_PAYLOAD_42=$(echo "$JWT_PAD" | tr '_-' '/+' | base64 -d 2>/dev/null || true)
JWT_EXP=$(echo "$JWT_PAYLOAD_42" | jq -r '.exp // empty')
[[ -n "$JWT_EXP" && "$JWT_EXP" != "null" ]] \
  || fail "FASE 42: claim 'exp' ausente o null en payload JWT — verificar ExpirationHours > 0"
ok "FASE 42: claim exp presente en payload JWT (Unix: $JWT_EXP) ✓"

# FASE 46 — HTTPS/HSTS readiness (documental — HSTS requiere ambiente HTTPS real con certificado válido)
phase "FASE 46: HTTPS/HSTS readiness — confirmar que API responde correctamente"
PING46=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 "$API_URL/api/diagnostics/ping")
[[ "$PING46" == "200" ]] \
  || fail "FASE 46: /api/diagnostics/ping esperado 200, obtenido $PING46"
ok "FASE 46: /api/diagnostics/ping = 200 ✓"

EXPECT_HSTS="${EXPECT_HSTS:-false}"
if [[ "$EXPECT_HSTS" == "true" ]]; then
  HSTS_HEADER=$(curl -s -I --max-time 15 "$API_URL/api/diagnostics/ping" \
    | grep -i "strict-transport-security:" | tr -d '\r' || true)
  [[ -n "$HSTS_HEADER" ]] \
    || fail "FASE 46: EXPECT_HSTS=true pero 'Strict-Transport-Security' ausente — verificar Https__EnableHsts=true y ambiente HTTPS real"
  ok "FASE 46: Strict-Transport-Security presente ($HSTS_HEADER) ✓"
else
  ok "FASE 46: HSTS validación manual requerida en ambiente HTTPS real (Https__EnableHsts=true) ✓"
  info "FASE 46: Para validar HSTS → establecer EXPECT_HSTS=true y ejecutar contra API HTTPS"
fi

# FASE 47 — Readiness probe: /api/diagnostics/ready
phase "FASE 47: /api/diagnostics/ready — readiness probe"
READY_HTTP=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 "$API_URL/api/diagnostics/ready")
[[ "$READY_HTTP" == "200" ]] \
  || fail "FASE 47: /api/diagnostics/ready esperado 200, obtenido $READY_HTTP"
ok "FASE 47: /api/diagnostics/ready → HTTP 200 ✓"

READY_BODY=$(curl -sf --max-time 15 "$API_URL/api/diagnostics/ready" 2>/dev/null || true)
READY_STATUS=$(echo "$READY_BODY" | jq -r '.status // empty')
[[ "$READY_STATUS" == "READY" ]] \
  || fail "FASE 47: campo 'status' esperado 'READY', obtenido '$READY_STATUS'"
ok "FASE 47: status=READY ✓"

READY_CID=$(echo "$READY_BODY" | jq -r '.correlationId // empty')
[[ -n "$READY_CID" ]] \
  || fail "FASE 47: correlationId ausente en /api/diagnostics/ready"
ok "FASE 47: correlationId presente (cid=$READY_CID) ✓"

# ════════════════════════════════════════════════════
# FASE 70.4-C — Recargas en efectivo vinculadas a caja
# ════════════════════════════════════════════════════
# Hasta esta fase, scripts/validate-backend.sh nunca ejercitó el dominio
# comercios_aliados/comercio_usuarios (ADMIN_COMERCIO/ADMIN_SEDE_COMERCIO/
# CAJERO) ni ningún endpoint /api/comercio/cajas o /api/comercio/wallet-
# recargas — no existía ningún fixture para ello. El fixture de esta fase
# (scripts/ci/ci_comercio_caja_fixture.sql) lo crea por primera vez,
# reutilizando $ID_COMERCIO (capturado en FASE 4) como comercio existente
# del nuevo comercio aliado — no crea un comercio nuevo desde cero.
phase "FASE 70.4-C: Recargas en efectivo vinculadas a caja"

info "Aplicando fixture CI: comercio aliado + 4 usuarios operativos (Fase 70.4-C)"
[[ -n "${ID_COMERCIO:-}" ]] || fail "FASE 70.4-C: \$ID_COMERCIO no está disponible (se esperaba de FASE 4)"
"$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -v ID_COMERCIO="$ID_COMERCIO" \
  -i scripts/ci/ci_comercio_caja_fixture.sql \
  || fail "FASE 70.4-C: fixture SQL de comercio/caja falló"
ok "Fixture comercio aliado + caja aplicado (id_comercio_existente=$ID_COMERCIO)"

info "POST /api/auth/login (ci_cajero_comercio / ci_admin_sede_comercio / ci_admin_comercio / ci_cajero_ambiguo)"
LOGIN_CAJERO=$(post_json "$API_URL/api/auth/login" '{"usuario":"ci_cajero_comercio","password":"CI-Fixture-Cajero#2026"}') \
  || fail "POST login ci_cajero_comercio no respondió"
assert_ok "$LOGIN_CAJERO" "login ci_cajero_comercio"
TOKEN_CAJERO=$(echo "$LOGIN_CAJERO" | jq -r '.data.token')
[[ -n "$TOKEN_CAJERO" && "$TOKEN_CAJERO" != "null" ]] || fail "Token vacío para ci_cajero_comercio"

LOGIN_ADMIN_SEDE=$(post_json "$API_URL/api/auth/login" '{"usuario":"ci_admin_sede_comercio","password":"CI-Fixture-AdminSede#2026"}') \
  || fail "POST login ci_admin_sede_comercio no respondió"
assert_ok "$LOGIN_ADMIN_SEDE" "login ci_admin_sede_comercio"
TOKEN_ADMIN_SEDE=$(echo "$LOGIN_ADMIN_SEDE" | jq -r '.data.token')
[[ -n "$TOKEN_ADMIN_SEDE" && "$TOKEN_ADMIN_SEDE" != "null" ]] || fail "Token vacío para ci_admin_sede_comercio"

LOGIN_ADMIN_COMERCIO=$(post_json "$API_URL/api/auth/login" '{"usuario":"ci_admin_comercio","password":"CI-Fixture-AdminComercio#2026"}') \
  || fail "POST login ci_admin_comercio no respondió"
assert_ok "$LOGIN_ADMIN_COMERCIO" "login ci_admin_comercio"
TOKEN_ADMIN_COMERCIO=$(echo "$LOGIN_ADMIN_COMERCIO" | jq -r '.data.token')
[[ -n "$TOKEN_ADMIN_COMERCIO" && "$TOKEN_ADMIN_COMERCIO" != "null" ]] || fail "Token vacío para ci_admin_comercio"

LOGIN_CAJERO_AMBIGUO=$(post_json "$API_URL/api/auth/login" '{"usuario":"ci_cajero_ambiguo","password":"CI-Fixture-CajeroAmbiguo#2026"}') \
  || fail "POST login ci_cajero_ambiguo no respondió"
assert_ok "$LOGIN_CAJERO_AMBIGUO" "login ci_cajero_ambiguo"
TOKEN_CAJERO_AMBIGUO=$(echo "$LOGIN_CAJERO_AMBIGUO" | jq -r '.data.token')
[[ -n "$TOKEN_CAJERO_AMBIGUO" && "$TOKEN_CAJERO_AMBIGUO" != "null" ]] || fail "Token vacío para ci_cajero_ambiguo"
ok "4 logins de fixture Fase 70.4-C → OK"

# ── Caso 19: USUARIO_FINAL (carlos, sin ningún comercio_usuarios) → 403 ──
info "POST /api/comercio/wallet-recargas con USUARIO_FINAL (carlos) → debe retornar 403"
STATUS_C19=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/comercio/wallet-recargas" \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN_A" \
  -d "{\"idUsuarioWallet\": $ID_USUARIO_A, \"valor\": 10000, \"pin\": \"1234567\"}")
[[ "$STATUS_C19" == "403" ]] || fail "FASE 70.4-C caso 19: USUARIO_FINAL esperado 403, obtenido $STATUS_C19"
ok "FASE 70.4-C caso 19: USUARIO_FINAL (carlos) → 403 ✓"

# ── Caso 20: sin token → 401 ──
info "POST /api/comercio/wallet-recargas sin token → debe retornar 401"
STATUS_C20=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/comercio/wallet-recargas" \
  -H "Content-Type: application/json" \
  -d "{\"idUsuarioWallet\": $ID_USUARIO_A, \"valor\": 10000, \"pin\": \"1234567\"}")
[[ "$STATUS_C20" == "401" ]] || fail "FASE 70.4-C caso 20: sin token esperado 401, obtenido $STATUS_C20"
ok "FASE 70.4-C caso 20: sin token → 401 ✓"

# ── Caso 11: scope ambiguo (2 comercio_usuarios ACTIVOS) → 409 ──
info "POST /api/comercio/wallet-recargas con scope ambiguo (ci_cajero_ambiguo) → debe retornar 409"
RESP_C11=$(post_auth_json_status "$TOKEN_CAJERO_AMBIGUO" "$API_URL/api/comercio/wallet-recargas" \
  "{\"idUsuarioWallet\": $ID_USUARIO_A, \"valor\": 10000, \"pin\": \"1234567\"}")
STATUS_C11=$(echo "$RESP_C11" | tail -1)
BODY_C11=$(echo "$RESP_C11" | sed '$d')
[[ "$STATUS_C11" == "409" ]] || fail "FASE 70.4-C caso 11: scope ambiguo esperado 409, obtenido $STATUS_C11 ($BODY_C11)"
ok "FASE 70.4-C caso 11: scope ambiguo (ci_cajero_ambiguo) → 409 ScopeComercioAmbiguoException ✓"

# ── Caso 10: ADMIN_COMERCIO no autorizado a recargar → 403 ──
info "POST /api/comercio/wallet-recargas con ADMIN_COMERCIO (ci_admin_comercio) → debe retornar 403"
STATUS_C10=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/comercio/wallet-recargas" \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN_ADMIN_COMERCIO" \
  -d "{\"idUsuarioWallet\": $ID_USUARIO_A, \"valor\": 10000, \"pin\": \"1234567\"}")
[[ "$STATUS_C10" == "403" ]] || fail "FASE 70.4-C caso 10: ADMIN_COMERCIO esperado 403, obtenido $STATUS_C10"
ok "FASE 70.4-C caso 10: ADMIN_COMERCIO (ci_admin_comercio) → 403 ✓"

# ── Caso 7/12: recarga sin caja (ci_admin_sede_comercio, antes de abrir la ──
# suya) → 409, y prueba de aislamiento: la caja ABIERTA de ci_cajero_comercio
# (que se abre justo después) nunca podría reutilizarse aquí, sin fuga de
# información de esa caja ajena en el mensaje. ──────────────────────────────
info "POST /api/comercio/wallet-recargas sin caja abierta (ci_admin_sede_comercio) → debe retornar 409"
RESP_C7=$(post_auth_json_status "$TOKEN_ADMIN_SEDE" "$API_URL/api/comercio/wallet-recargas" \
  "{\"idUsuarioWallet\": $ID_USUARIO_A, \"valor\": 10000, \"pin\": \"1234567\"}")
STATUS_C7=$(echo "$RESP_C7" | tail -1)
BODY_C7=$(echo "$RESP_C7" | sed '$d')
[[ "$STATUS_C7" == "409" ]] || fail "FASE 70.4-C caso 7/12: sin caja esperado 409, obtenido $STATUS_C7 ($BODY_C7)"
echo "$BODY_C7" | jq -e '.message | contains("caja abierta")' > /dev/null \
  || fail "FASE 70.4-C caso 7/12: mensaje inesperado ($BODY_C7)"
ok "FASE 70.4-C caso 7/12: recarga sin caja (ci_admin_sede_comercio) → 409 CajaNoAbiertaException, sin fuga ✓"

# ── Caso 1: CAJERO abre su caja → 200 ──
info "POST /api/comercio/cajas/abrir (ci_cajero_comercio, fondoInicial=50000)"
ABRIR_CAJERO=$(post_auth_json "$TOKEN_CAJERO" "$API_URL/api/comercio/cajas/abrir" '{"fondoInicial": 50000}') \
  || fail "FASE 70.4-C caso 1: abrir caja (CAJERO) no respondió"
assert_ok "$ABRIR_CAJERO" "abrir caja CAJERO"
ID_CAJA_CAJERO=$(echo "$ABRIR_CAJERO" | jq -r '.data.idCaja')
ESTADO_CAJA_CAJERO=$(echo "$ABRIR_CAJERO" | jq -r '.data.estado')
[[ "$ESTADO_CAJA_CAJERO" == "ABIERTA" ]] || fail "FASE 70.4-C caso 1: estado esperado ABIERTA, obtenido $ESTADO_CAJA_CAJERO"
ok "FASE 70.4-C caso 1: CAJERO abre caja → idCaja=$ID_CAJA_CAJERO estado=ABIERTA ✓"

# ── Casos 2/3/4: recarga con caja ABIERTA propia → 200, vínculo completo ──
info "POST /api/comercio/wallet-recargas (20.000, CAJERO → wallet de carlos)"
RECARGA_1=$(post_auth_json "$TOKEN_CAJERO" "$API_URL/api/comercio/wallet-recargas" \
  "{\"idUsuarioWallet\": $ID_USUARIO_A, \"valor\": 20000, \"pin\": \"1234567\", \"observaciones\": \"Recarga CI Fase 70.4-C\"}") \
  || fail "FASE 70.4-C caso 2: recarga con caja abierta no respondió"
assert_ok "$RECARGA_1" "recarga con caja abierta"
ID_RECARGA_1=$(echo "$RECARGA_1" | jq -r '.data.idRecarga')
ok "FASE 70.4-C caso 2: recarga con caja ABIERTA propia → idRecarga=$ID_RECARGA_1 ✓"

check_sql_value \
  "FASE 70.4-C caso 3: wallet_caja_movimientos de la recarga $ID_RECARGA_1" \
  "SELECT CONCAT(tipo_movimiento,'|',naturaleza,'|',CAST(valor AS INT),'|',id_caja) FROM wallet_caja_movimientos WHERE id_recarga = $ID_RECARGA_1" \
  "RECARGA_EFECTIVO|E|20000|$ID_CAJA_CAJERO"

check_sql_value \
  "FASE 70.4-C caso 4: exactamente 1 wallet_caja_movimientos para la recarga $ID_RECARGA_1" \
  "SELECT COUNT(*) FROM wallet_caja_movimientos WHERE id_recarga = $ID_RECARGA_1" \
  "1"
check_sql_value \
  "FASE 70.4-C caso 4: exactamente 1 wallet_movimientos vinculado a la recarga $ID_RECARGA_1" \
  "SELECT COUNT(*) FROM wallet_movimientos WHERE referencia_tipo='wallet_recargas_comercio' AND referencia_id=$ID_RECARGA_1" \
  "1"
check_sql_value \
  "FASE 70.4-C caso 4: exactamente 2 ledger_movimientos (D+C) vinculados a la recarga $ID_RECARGA_1" \
  "SELECT COUNT(*) FROM ledger_movimientos WHERE referencia_tipo='wallet_recargas_comercio' AND referencia_id=$ID_RECARGA_1" \
  "2"
ok "FASE 70.4-C caso 4: recarga+ledger+wallet_movimiento+caja_movimiento consistentes en una sola transacción ✓"

# ── Caso 6: corregir fondo inicial con recargas vinculadas → 409 ──
info "PATCH /api/comercio/cajas/$ID_CAJA_CAJERO/fondo-inicial tras recarga real → debe retornar 409"
STATUS_C6=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X PATCH "$API_URL/api/comercio/cajas/$ID_CAJA_CAJERO/fondo-inicial" \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN_CAJERO" \
  -d '{"fondoInicial": 60000, "motivo": "Validación CI: corrección bloqueada después de una recarga"}')
[[ "$STATUS_C6" == "409" ]] || fail "FASE 70.4-C caso 6: corregir fondo tras recarga esperado 409, obtenido $STATUS_C6"
ok "FASE 70.4-C caso 6: corregir fondo inicial tras recarga real → 409 ✓"

# ── Caso 5: iniciar cuadre → EfectivoEsperado = FondoInicial + recargas ──
info "POST /api/comercio/cajas/$ID_CAJA_CAJERO/iniciar-cuadre"
CUADRE_CAJERO=$(post_auth_json "$TOKEN_CAJERO" "$API_URL/api/comercio/cajas/$ID_CAJA_CAJERO/iniciar-cuadre" '{}') \
  || fail "FASE 70.4-C caso 5: iniciar cuadre no respondió"
assert_ok "$CUADRE_CAJERO" "iniciar cuadre CAJERO"
EFECTIVO_ESPERADO_CAJERO=$(echo "$CUADRE_CAJERO" | jq -r '.data.efectivoEsperado')
echo "$CUADRE_CAJERO" | jq -e '(.data.efectivoEsperado | tonumber) == 70000' > /dev/null \
  || fail "FASE 70.4-C caso 5: efectivoEsperado esperado 70000 (50000+20000), obtenido $EFECTIVO_ESPERADO_CAJERO"
ok "FASE 70.4-C caso 5: iniciar cuadre → efectivoEsperado=70000 (50000 fondo + 20000 recarga) ✓"

# ── Caso 8: recarga con caja EN_CUADRE → 409 ──
info "POST /api/comercio/wallet-recargas con caja EN_CUADRE (ci_cajero_comercio) → debe retornar 409"
RESP_C8=$(post_auth_json_status "$TOKEN_CAJERO" "$API_URL/api/comercio/wallet-recargas" \
  "{\"idUsuarioWallet\": $ID_USUARIO_A, \"valor\": 10000, \"pin\": \"1234567\"}")
STATUS_C8=$(echo "$RESP_C8" | tail -1)
[[ "$STATUS_C8" == "409" ]] || fail "FASE 70.4-C caso 8: caja EN_CUADRE esperado 409, obtenido $STATUS_C8"
ok "FASE 70.4-C caso 8: recarga con caja EN_CUADRE → 409 ✓"

# ── Cerrar la caja del CAJERO exactamente en 70.000 (sin diferencia → CERRADA) ──
info "POST /api/comercio/cajas/$ID_CAJA_CAJERO/cerrar (efectivoContado=70000)"
CIERRE_CAJERO=$(post_auth_json "$TOKEN_CAJERO" "$API_URL/api/comercio/cajas/$ID_CAJA_CAJERO/cerrar" \
  '{"efectivoContado": 70000}') \
  || fail "FASE 70.4-C: cerrar caja CAJERO no respondió"
assert_ok "$CIERRE_CAJERO" "cerrar caja CAJERO"
ESTADO_CIERRE_CAJERO=$(echo "$CIERRE_CAJERO" | jq -r '.data.estado')
[[ "$ESTADO_CIERRE_CAJERO" == "CERRADA" ]] || fail "FASE 70.4-C: estado esperado CERRADA, obtenido $ESTADO_CIERRE_CAJERO"
ok "FASE 70.4-C: caja CAJERO cerrada sin diferencia → CERRADA ✓"

# ── Caso 9: recarga con caja en estado terminal (CERRADA) → 409 ──
info "POST /api/comercio/wallet-recargas con caja CERRADA (ci_cajero_comercio) → debe retornar 409"
RESP_C9=$(post_auth_json_status "$TOKEN_CAJERO" "$API_URL/api/comercio/wallet-recargas" \
  "{\"idUsuarioWallet\": $ID_USUARIO_A, \"valor\": 10000, \"pin\": \"1234567\"}")
STATUS_C9=$(echo "$RESP_C9" | tail -1)
[[ "$STATUS_C9" == "409" ]] || fail "FASE 70.4-C caso 9: caja terminal esperado 409, obtenido $STATUS_C9"
ok "FASE 70.4-C caso 9: recarga con caja en estado terminal (CERRADA) → 409 ✓"

# ── Caso 13: fallo posterior a la adquisición del lock de caja → rollback ──
# completo. ci_admin_sede_comercio abre su propia caja y luego se le hace
# fallar la recarga en un punto DESPUÉS del lock (usuario destino inexistente,
# resuelto ya dentro de la transacción) — no debe quedar wallet_recarga_comercio
# ni wallet_caja_movimientos huérfano para esa caja. ─────────────────────────
info "POST /api/comercio/cajas/abrir (ci_admin_sede_comercio, fondoInicial=30000)"
ABRIR_ADMINSEDE=$(post_auth_json "$TOKEN_ADMIN_SEDE" "$API_URL/api/comercio/cajas/abrir" '{"fondoInicial": 30000}') \
  || fail "FASE 70.4-C: abrir caja (ADMIN_SEDE_COMERCIO) no respondió"
assert_ok "$ABRIR_ADMINSEDE" "abrir caja ADMIN_SEDE_COMERCIO"
ID_CAJA_ADMINSEDE=$(echo "$ABRIR_ADMINSEDE" | jq -r '.data.idCaja')
ok "FASE 70.4-C: ADMIN_SEDE_COMERCIO abre caja → idCaja=$ID_CAJA_ADMINSEDE ✓"

MOVS_ANTES_C13=$("$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM wallet_caja_movimientos WHERE id_caja=$ID_CAJA_ADMINSEDE" -h -1 | tr -d ' \r\n')
RECARGAS_ANTES_C13=$("$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM wallet_recargas_comercio WHERE id_usuario_cajero=(SELECT id_usuario FROM usuarios WHERE usuario='ci_admin_sede_comercio')" -h -1 | tr -d ' \r\n')

info "POST /api/comercio/wallet-recargas con usuario destino inexistente → debe retornar 404 y hacer rollback completo"
STATUS_C13=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 \
  -X POST "$API_URL/api/comercio/wallet-recargas" \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN_ADMIN_SEDE" \
  -d '{"idUsuarioWallet": 999999999, "valor": 10000, "pin": "1234567"}')
[[ "$STATUS_C13" == "404" ]] || fail "FASE 70.4-C caso 13: usuario destino inexistente esperado 404, obtenido $STATUS_C13"

MOVS_DESPUES_C13=$("$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM wallet_caja_movimientos WHERE id_caja=$ID_CAJA_ADMINSEDE" -h -1 | tr -d ' \r\n')
RECARGAS_DESPUES_C13=$("$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM wallet_recargas_comercio WHERE id_usuario_cajero=(SELECT id_usuario FROM usuarios WHERE usuario='ci_admin_sede_comercio')" -h -1 | tr -d ' \r\n')
[[ "$MOVS_ANTES_C13" == "$MOVS_DESPUES_C13" ]] \
  || fail "FASE 70.4-C caso 13: wallet_caja_movimientos cambió tras un fallo (antes=$MOVS_ANTES_C13, después=$MOVS_DESPUES_C13) — rollback incompleto"
[[ "$RECARGAS_ANTES_C13" == "$RECARGAS_DESPUES_C13" ]] \
  || fail "FASE 70.4-C caso 13: wallet_recargas_comercio cambió tras un fallo (antes=$RECARGAS_ANTES_C13, después=$RECARGAS_DESPUES_C13) — rollback incompleto"
ok "FASE 70.4-C caso 13: fallo posterior al lock de caja → 404, rollback completo (0 filas huérfanas) ✓"

# ── Concurrencia — dos recargas simultáneas sobre la misma caja ABIERTA ──
# (ci_admin_sede_comercio). Caso determinista: bajo el lock UPDLOCK/ROWLOCK
# de la fila de caja, SQL Server serializa ambas transacciones — ambas deben
# tener éxito, sin update perdido. No se prueba aquí la carrera recarga vs.
# IniciarCuadreAsync/CerrarAsync (ver nota de "pendiente" en el informe final)
# porque su desenlace depende de qué transacción gana el lock primero — no
# hay una aserción determinista posible desde bash sin control fino de
# temporización, y no se va a fingir haberla probado.
info "Dos recargas concurrentes (5.000 y 7.000) sobre la misma caja ABIERTA (ci_admin_sede_comercio)"
# trap EXIT garantiza limpieza de archivos temporales y de los procesos curl
# en background incluso si un wait/fail posterior aborta el script — mismo
# patrón ya usado en FASE USUARIOS-ADMIN-3 (restaurar_admins_auxiliares).
# Sin esto, un "fail" entre el lanzamiento y el rm -f de más abajo (p. ej. si
# una de las dos llamadas concurrentes falla) dejaría /tmp/concurrente_*.json
# huérfanos y, potencialmente, la otra llamada aún corriendo en background.
limpiar_concurrencia_70_4_c() {
  kill "$PID_CONC_A" "$PID_CONC_B" 2>/dev/null || true
  rm -f /tmp/concurrente_a.json /tmp/concurrente_b.json
}
(post_auth_json "$TOKEN_ADMIN_SEDE" "$API_URL/api/comercio/wallet-recargas" \
  "{\"idUsuarioWallet\": $ID_USUARIO_A, \"valor\": 5000, \"pin\": \"1234567\"}" > /tmp/concurrente_a.json 2>&1) &
PID_CONC_A=$!
(post_auth_json "$TOKEN_ADMIN_SEDE" "$API_URL/api/comercio/wallet-recargas" \
  "{\"idUsuarioWallet\": $ID_USUARIO_A, \"valor\": 7000, \"pin\": \"1234567\"}" > /tmp/concurrente_b.json 2>&1) &
PID_CONC_B=$!
trap limpiar_concurrencia_70_4_c EXIT
wait "$PID_CONC_A" || fail "FASE 70.4-C: recarga concurrente A falló"
wait "$PID_CONC_B" || fail "FASE 70.4-C: recarga concurrente B falló"
assert_ok "$(cat /tmp/concurrente_a.json)" "recarga concurrente A"
assert_ok "$(cat /tmp/concurrente_b.json)" "recarga concurrente B"
rm -f /tmp/concurrente_a.json /tmp/concurrente_b.json
trap - EXIT
check_sql_value \
  "FASE 70.4-C: exactamente 2 wallet_caja_movimientos nuevos en la caja $ID_CAJA_ADMINSEDE tras la concurrencia" \
  "SELECT COUNT(*) FROM wallet_caja_movimientos WHERE id_caja=$ID_CAJA_ADMINSEDE" \
  "2"
ok "FASE 70.4-C: dos recargas concurrentes sobre la misma caja → ambas exitosas, sin update perdido ✓"

info "POST /api/comercio/cajas/$ID_CAJA_ADMINSEDE/iniciar-cuadre (ADMIN_SEDE_COMERCIO)"
CUADRE_ADMINSEDE=$(post_auth_json "$TOKEN_ADMIN_SEDE" "$API_URL/api/comercio/cajas/$ID_CAJA_ADMINSEDE/iniciar-cuadre" '{}') \
  || fail "FASE 70.4-C: iniciar cuadre ADMIN_SEDE_COMERCIO no respondió"
assert_ok "$CUADRE_ADMINSEDE" "iniciar cuadre ADMIN_SEDE_COMERCIO"
EFECTIVO_ESPERADO_ADMINSEDE=$(echo "$CUADRE_ADMINSEDE" | jq -r '.data.efectivoEsperado')
echo "$CUADRE_ADMINSEDE" | jq -e '(.data.efectivoEsperado | tonumber) == 42000' > /dev/null \
  || fail "FASE 70.4-C: efectivoEsperado ADMIN_SEDE_COMERCIO esperado 42000 (30000+5000+7000), obtenido $EFECTIVO_ESPERADO_ADMINSEDE"
ok "FASE 70.4-C: ADMIN_SEDE_COMERCIO — recarga concurrente reflejada íntegra en efectivoEsperado=42000 ✓"

info "POST /api/comercio/cajas/$ID_CAJA_ADMINSEDE/cerrar (efectivoContado=42000)"
CIERRE_ADMINSEDE=$(post_auth_json "$TOKEN_ADMIN_SEDE" "$API_URL/api/comercio/cajas/$ID_CAJA_ADMINSEDE/cerrar" \
  '{"efectivoContado": 42000}') \
  || fail "FASE 70.4-C: cerrar caja ADMIN_SEDE_COMERCIO no respondió"
assert_ok "$CIERRE_ADMINSEDE" "cerrar caja ADMIN_SEDE_COMERCIO"
ok "FASE 70.4-C: ADMIN_SEDE_COMERCIO — ciclo completo abrir→recargar→cuadrar→cerrar → OK ✓"

# ── Caso 14: uq_wcjm_recarga impide el doble vínculo defensivo a nivel de BD ──
info "INSERT directo duplicando id_recarga=$ID_RECARGA_1 en wallet_caja_movimientos → debe fallar por UNIQUE"
DUP_OUTPUT=$("$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; INSERT INTO wallet_caja_movimientos (id_caja, id_recarga, tipo_movimiento, naturaleza, valor, created_at) VALUES ($ID_CAJA_CAJERO, $ID_RECARGA_1, 'RECARGA_EFECTIVO', 'E', 1, SYSUTCDATETIME())" 2>&1) \
  && fail "FASE 70.4-C caso 14: el INSERT duplicado debía fallar por uq_wcjm_recarga y no falló"
echo "$DUP_OUTPUT" | grep -qi "unique\|duplicate" \
  || fail "FASE 70.4-C caso 14: el INSERT falló pero no por violación UNIQUE — salida: $DUP_OUTPUT"
check_sql_value \
  "FASE 70.4-C caso 14: sigue existiendo exactamente 1 wallet_caja_movimientos para la recarga $ID_RECARGA_1 tras el intento" \
  "SELECT COUNT(*) FROM wallet_caja_movimientos WHERE id_recarga = $ID_RECARGA_1" \
  "1"
ok "FASE 70.4-C caso 14: uq_wcjm_recarga rechaza el doble vínculo, sin corromper el existente ✓"

echo ""
ok "═══ FASE 70.4-C COMPLETA: caja obligatoria, vínculo wallet_caja_movimientos, EfectivoEsperado, bloqueo EN_CUADRE/terminal, scope estricto, roles CAJERO/ADMIN_SEDE_COMERCIO, rollback atómico, concurrencia de recargas simultáneas y UNIQUE defensivo — OK ═══"
info "PENDIENTE (no cubierto de forma determinista en este pipeline bash): carrera real recarga vs. IniciarCuadreAsync/CerrarAsync — su desenlace depende de cuál transacción gana el UPDLOCK primero; requiere un arnés de prueba con control explícito de temporización (p. ej. un test de integración .NET que fuerce el orden de adquisición del lock), no simulable de forma no frágil desde curl+bash."
info "PENDIENTE (fuera de alcance de esta subfase): cobertura CI de Fase 70.2 (liquidación de recaudos) y Fase 70.3 (cierre diario) — ninguna de las dos tiene fixture ni prueba en este script, con o sin Fase 70.4-C; gap preexistente, no introducido aquí."

# ════════════════════════════════════════════════════
# FASE 70.4-F — Apertura sin restricción de hora / vencimiento por fecha operativa
# ════════════════════════════════════════════════════
# Mejora operativa pre-lanzamiento: se elimina el corte fijo de apertura a
# las 21:00 (WalletCajaComercioService.AbrirAsync) y EstaVencida pasa a
# depender únicamente de fecha_operativa < HoyColombia(), no de la hora.
# ci_cajero_vencimiento/ci_cajero_vigente usan una sede SIN override de hora
# (hora_cierre_automatico_caja = NULL) — antes de este cambio, caía al
# default 21:00 y el caso T1 hubiera sido intermitente según a qué hora
# corriera el pipeline; con el cambio, es 100% determinista: abrir ya no
# puede fallar por hora, sea cual sea la hora real de ejecución de CI.
phase "FASE 70.4-F: Apertura sin restricción de hora / vencimiento por fecha operativa"

info "POST /api/auth/login (ci_cajero_vencimiento / ci_cajero_vigente)"
LOGIN_VENC=$(post_json "$API_URL/api/auth/login" '{"usuario":"ci_cajero_vencimiento","password":"CI-Fixture-CajeroVencimiento#2026"}') \
  || fail "POST login ci_cajero_vencimiento no respondió"
assert_ok "$LOGIN_VENC" "login ci_cajero_vencimiento"
TOKEN_VENC=$(echo "$LOGIN_VENC" | jq -r '.data.token')
[[ -n "$TOKEN_VENC" && "$TOKEN_VENC" != "null" ]] || fail "Token vacío para ci_cajero_vencimiento"

LOGIN_VIG=$(post_json "$API_URL/api/auth/login" '{"usuario":"ci_cajero_vigente","password":"CI-Fixture-CajeroVigente#2026"}') \
  || fail "POST login ci_cajero_vigente no respondió"
assert_ok "$LOGIN_VIG" "login ci_cajero_vigente"
TOKEN_VIG=$(echo "$LOGIN_VIG" | jq -r '.data.token')
[[ -n "$TOKEN_VIG" && "$TOKEN_VIG" != "null" ]] || fail "Token vacío para ci_cajero_vigente"
ok "2 logins de fixture Fase 70.4-F → OK"

# ── T1: abrir caja — ya no debe depender de la hora ──────────────────────
info "POST /api/comercio/cajas/abrir (ci_cajero_vencimiento, sede sin override de hora) → debe retornar 200 sin importar la hora real"
ABRIR_VENC=$(post_auth_json "$TOKEN_VENC" "$API_URL/api/comercio/cajas/abrir" '{"fondoInicial": 5000}') \
  || fail "FASE 70.4-F T1: abrir caja ci_cajero_vencimiento no respondió"
assert_ok "$ABRIR_VENC" "FASE 70.4-F T1: abrir caja ci_cajero_vencimiento"
ID_CAJA_VENC=$(echo "$ABRIR_VENC" | jq -r '.data.idCaja')
[[ -n "$ID_CAJA_VENC" && "$ID_CAJA_VENC" != "null" ]] || fail "FASE 70.4-F T1: idCaja vacío"
ok "FASE 70.4-F T1: apertura exitosa sin restricción de hora (idCaja=$ID_CAJA_VENC) ✓"

# ── T2: simular caja abierta "ayer" (SQL directo — no hay forma de mover el
# reloj del sistema desde bash de forma segura) y confirmar vencimiento por
# fecha, cierre automático vía la misma AutoSanarAsync que usará el nuevo
# BackgroundService, y ausencia total de efecto en ledger/wallet ──────────
info "UPDATE directo: fecha_operativa de la caja $ID_CAJA_VENC → ayer (simula caja abierta el día anterior, todavía ABIERTA)"
"$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; UPDATE wallet_cajas_comercio SET fecha_operativa = DATEADD(DAY,-1,fecha_operativa) WHERE id_caja = $ID_CAJA_VENC" \
  || fail "FASE 70.4-F T2: UPDATE de fecha_operativa falló"
ok "FASE 70.4-F T2: fecha_operativa retrocedida un día para la caja $ID_CAJA_VENC ✓"

# Conteos GLOBALES antes/después — wallet_movimientos/ledger_transacciones/
# ledger_movimientos no tienen id_caja para filtrar directamente, así que la
# única forma real de demostrar que AutoSanarAsync no les afecta es comparar
# el conteo total antes y después del disparo (comparar solo
# wallet_caja_movimientos WHERE id_caja=... no alcanza a cubrir estas 3 tablas).
WALLETMOV_ANTES_70_4_F=$("$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM wallet_movimientos" -h -1 | tr -d ' \r\n')
LEDGERTX_ANTES_70_4_F=$("$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM ledger_transacciones" -h -1 | tr -d ' \r\n')
LEDGERMOV_ANTES_70_4_F=$("$SQLCMD" -S "$DB_HOST" -U "$DB_USER" -P "$SA_PASS" -d "$DB_NAME" -b -C \
  -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM ledger_movimientos" -h -1 | tr -d ' \r\n')

info "GET /api/comercio/cajas/mi-caja-actual (ci_cajero_vencimiento) → debe auto-sanar en esta misma lectura y devolver data=null"
MI_CAJA_VENC=$(get_auth_json "$TOKEN_VENC" "$API_URL/api/comercio/cajas/mi-caja-actual") \
  || fail "FASE 70.4-F T2: GET mi-caja-actual no respondió"
assert_ok "$MI_CAJA_VENC" "FASE 70.4-F T2: GET mi-caja-actual tras vencimiento"
echo "$MI_CAJA_VENC" | jq -e '.data == null' > /dev/null \
  || fail "FASE 70.4-F T2: se esperaba data=null (caja de ayer ya no es 'la de hoy'), obtenido: $MI_CAJA_VENC"
ok "FASE 70.4-F T2: caja vencida por fecha (no por hora) auto-sanada en lectura, mi-caja-actual → null ✓"

check_sql_value \
  "FASE 70.4-F T2: caja $ID_CAJA_VENC quedó CERRADA_AUTOMATICAMENTE" \
  "SELECT estado FROM wallet_cajas_comercio WHERE id_caja=$ID_CAJA_VENC" \
  "CERRADA_AUTOMATICAMENTE"
check_sql_value \
  "FASE 70.4-F T2: cerrada_automaticamente=1" \
  "SELECT CAST(cerrada_automaticamente AS INT) FROM wallet_cajas_comercio WHERE id_caja=$ID_CAJA_VENC" \
  "1"
check_sql_value \
  "FASE 70.4-F T2: efectivo_esperado = fondo_inicial (5000, sin recargas)" \
  "SELECT CAST(efectivo_esperado AS INT) FROM wallet_cajas_comercio WHERE id_caja=$ID_CAJA_VENC" \
  "5000"
check_sql_value \
  "FASE 70.4-F T2: efectivo_contado sigue NULL (cierre automático, no manual)" \
  "SELECT ISNULL(CAST(efectivo_contado AS VARCHAR), 'NULL') FROM wallet_cajas_comercio WHERE id_caja=$ID_CAJA_VENC" \
  "NULL"
check_sql_value \
  "FASE 70.4-F T2: diferencia sigue NULL (cierre automático, no manual)" \
  "SELECT ISNULL(CAST(diferencia AS VARCHAR), 'NULL') FROM wallet_cajas_comercio WHERE id_caja=$ID_CAJA_VENC" \
  "NULL"
check_sql_value \
  "FASE 70.4-F: cierre automático no crea ningún wallet_caja_movimientos para esta caja" \
  "SELECT COUNT(*) FROM wallet_caja_movimientos WHERE id_caja=$ID_CAJA_VENC" \
  "0"

# Conteos globales DESPUÉS — deben ser idénticos a los de antes del disparo
# de AutoSanarAsync (evidencia real de "no toca wallet_movimientos/ledger",
# no solo una inferencia por lectura de código).
check_sql_value \
  "FASE 70.4-F: wallet_movimientos sin cambios tras el cierre automático" \
  "SELECT COUNT(*) FROM wallet_movimientos" \
  "$WALLETMOV_ANTES_70_4_F"
check_sql_value \
  "FASE 70.4-F: ledger_transacciones sin cambios tras el cierre automático" \
  "SELECT COUNT(*) FROM ledger_transacciones" \
  "$LEDGERTX_ANTES_70_4_F"
check_sql_value \
  "FASE 70.4-F: ledger_movimientos sin cambios tras el cierre automático" \
  "SELECT COUNT(*) FROM ledger_movimientos" \
  "$LEDGERMOV_ANTES_70_4_F"
ok "FASE 70.4-F T2: cierre automático reutiliza AutoSanarAsync sin afectar wallet_movimientos, ledger_transacciones, ledger_movimientos ni wallet_caja_movimientos ✓"

# ── T3: caja de hoy sigue vigente (no se auto-sana) ───────────────────────
info "POST /api/comercio/cajas/abrir (ci_cajero_vigente) → caja de hoy"
ABRIR_VIG=$(post_auth_json "$TOKEN_VIG" "$API_URL/api/comercio/cajas/abrir" '{"fondoInicial": 7000}') \
  || fail "FASE 70.4-F T3: abrir caja ci_cajero_vigente no respondió"
assert_ok "$ABRIR_VIG" "FASE 70.4-F T3: abrir caja ci_cajero_vigente"
ID_CAJA_VIG=$(echo "$ABRIR_VIG" | jq -r '.data.idCaja')
[[ -n "$ID_CAJA_VIG" && "$ID_CAJA_VIG" != "null" ]] || fail "FASE 70.4-F T3: idCaja vacío"

info "GET /api/comercio/cajas/mi-caja-actual (ci_cajero_vigente) → debe seguir ABIERTA, sin auto-sanar"
MI_CAJA_VIG=$(get_auth_json "$TOKEN_VIG" "$API_URL/api/comercio/cajas/mi-caja-actual") \
  || fail "FASE 70.4-F T3: GET mi-caja-actual no respondió"
assert_ok "$MI_CAJA_VIG" "FASE 70.4-F T3: GET mi-caja-actual caja vigente"
echo "$MI_CAJA_VIG" | jq -e ".data.idCaja == $ID_CAJA_VIG and .data.estado == \"ABIERTA\"" > /dev/null \
  || fail "FASE 70.4-F T3: se esperaba caja $ID_CAJA_VIG en estado ABIERTA, obtenido: $MI_CAJA_VIG"
ok "FASE 70.4-F T3: caja de hoy sigue vigente, no vence por hora ✓"

info "Cierre limpio de la caja vigente ($ID_CAJA_VIG) — orden de cuadre normal"
CUADRE_VIG=$(post_auth_json "$TOKEN_VIG" "$API_URL/api/comercio/cajas/$ID_CAJA_VIG/iniciar-cuadre" '{}') \
  || fail "FASE 70.4-F: iniciar cuadre ci_cajero_vigente no respondió"
assert_ok "$CUADRE_VIG" "FASE 70.4-F: iniciar cuadre ci_cajero_vigente"
CIERRE_VIG=$(post_auth_json "$TOKEN_VIG" "$API_URL/api/comercio/cajas/$ID_CAJA_VIG/cerrar" '{"efectivoContado": 7000}') \
  || fail "FASE 70.4-F: cerrar caja ci_cajero_vigente no respondió"
assert_ok "$CIERRE_VIG" "FASE 70.4-F: cerrar caja ci_cajero_vigente"
ok "FASE 70.4-F: caja vigente cerrada limpiamente (diferencia=0) ✓"

echo ""
ok "═══ FASE 70.4-F COMPLETA: apertura sin restricción de hora, vencimiento únicamente por fecha_operativa, cierre automático vía AutoSanarAsync sin afectar wallet/ledger — OK ═══"
info "PENDIENTE (no practicable dentro de un run de CI de duración normal): verificar el disparo real del BackgroundService cada 15 minutos — este bloque prueba exhaustivamente la lógica que ese servicio reutiliza (EstaVencida/AutoSanarAsync, idéntica a la vía perezosa), no el temporizador en sí. Verificación del temporizador queda para QA post-deploy (dejar una caja vencida y confirmar que se cierra sola en ≤15-20 min sin que nadie la toque)."

echo ""
ok "═══ VALIDACIÓN COMPLETA FASES 1 a 47 + 70.4-C + 70.4-F: listados ventas QR y ledger, admin wallets/comercios, retiros, gestión, CORS hardening, configuración QA, observabilidad básica, security headers, rate limiting, auditoría básica, error handling global, política JWT, HTTPS/HSTS readiness, readiness probe, recargas en efectivo vinculadas a caja, apertura sin restricción de hora y cierre automático por fecha operativa, y todos los endpoints OK ═══"
