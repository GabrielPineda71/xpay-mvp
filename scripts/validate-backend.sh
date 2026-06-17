#!/usr/bin/env bash
# Valida los endpoints del backend XPAY MVP y los registros en base de datos.
# Variables esperadas: API_URL, DB_HOST, DB_NAME, SA_PASSWORD
set -euo pipefail

API_URL="${API_URL:-http://localhost:5000}"
DB_HOST="${DB_HOST:-localhost,1433}"
DB_NAME="${DB_NAME:-XPAY_MVP}"
SA_PASS="${SA_PASSWORD:-XpayCI@2024!}"
SQLCMD="/opt/mssql-tools18/bin/sqlcmd"

GREEN='\033[0;32m'
RED='\033[0;31m'
CYAN='\033[0;36m'
NC='\033[0m'

ok()   { echo -e "${GREEN}[OK]${NC}   $*"; }
fail() { echo -e "${RED}[FAIL]${NC} $*"; exit 1; }
info() { echo -e "${CYAN}━━━ $* ━━━${NC}"; }

post_json() {
  local url="$1"
  local body="$2"
  curl -sf -X POST "$url" \
    -H "Content-Type: application/json" \
    --max-time 15 \
    -d "$body"
}

get_json() {
  curl -sf --max-time 15 "$1"
}

assert_ok() {
  local resp="$1"
  local label="$2"
  local success
  success=$(echo "$resp" | jq -r '.success')
  [[ "$success" == "true" ]] || fail "$label devolvió success=false. Respuesta: $resp"
}

# ─────────────────────────────────────────────
# 1. POST /api/usuarios/registro-final
# ─────────────────────────────────────────────
info "POST /api/usuarios/registro-final"
REGISTRO=$(post_json "$API_URL/api/usuarios/registro-final" '{
  "idUnidadNegocio": 1,
  "tipoDocumento": "CC",
  "numeroDocumento": "1099001234",
  "primerNombre": "Carlos",
  "primerApellido": "Gomez",
  "celular": "3001112233",
  "email": "carlos.gomez@ci-test.com",
  "usuario": "carlos_ci_test",
  "password": "Xpay@Test1!"
}') || fail "POST /api/usuarios/registro-final no respondió (curl falló)"
echo "$REGISTRO" | jq .
assert_ok "$REGISTRO" "registro-final"
ID_USUARIO=$(echo "$REGISTRO" | jq -r '.idUsuario')
ok "registro-final → idUsuario=$ID_USUARIO"

# ─────────────────────────────────────────────
# 2. POST /api/auth/login
# ─────────────────────────────────────────────
info "POST /api/auth/login"
LOGIN=$(post_json "$API_URL/api/auth/login" '{
  "usuario": "carlos_ci_test",
  "password": "Xpay@Test1!"
}') || fail "POST /api/auth/login no respondió"
echo "$LOGIN" | jq .
assert_ok "$LOGIN" "login"
ID_PERSONA=$(echo "$LOGIN" | jq -r '.data.idPersona')
ok "login → idPersona=$ID_PERSONA"

# ─────────────────────────────────────────────
# 3. GET /api/wallets/persona/{idPersona}
# ─────────────────────────────────────────────
info "GET /api/wallets/persona/$ID_PERSONA"
WALLET=$(get_json "$API_URL/api/wallets/persona/$ID_PERSONA") \
  || fail "GET /api/wallets/persona/$ID_PERSONA no respondió"
echo "$WALLET" | jq .
assert_ok "$WALLET" "wallets/persona"
ID_WALLET=$(echo "$WALLET" | jq -r '.data.idWallet')
ok "wallets/persona/$ID_PERSONA → idWallet=$ID_WALLET"

# ─────────────────────────────────────────────
# 4. GET /api/wallets/{idWallet}/saldo
# ─────────────────────────────────────────────
info "GET /api/wallets/$ID_WALLET/saldo"
SALDO=$(get_json "$API_URL/api/wallets/$ID_WALLET/saldo") \
  || fail "GET /api/wallets/$ID_WALLET/saldo no respondió"
echo "$SALDO" | jq .
assert_ok "$SALDO" "wallets/saldo"
SALDO_DISP=$(echo "$SALDO" | jq -r '.data.saldoDisponible')
ok "wallets/$ID_WALLET/saldo → saldoDisponible=$SALDO_DISP"

# ─────────────────────────────────────────────
# 5. POST /api/wallets/{idWallet}/recarga-manual
# ─────────────────────────────────────────────
info "POST /api/wallets/$ID_WALLET/recarga-manual"
RECARGA=$(post_json "$API_URL/api/wallets/$ID_WALLET/recarga-manual" \
  "{\"valor\": 100000, \"creadoPor\": $ID_USUARIO, \"observacion\": \"Recarga automatica CI\"}") \
  || fail "POST /api/wallets/$ID_WALLET/recarga-manual no respondió"
echo "$RECARGA" | jq .
assert_ok "$RECARGA" "recarga-manual"
ID_MOVIMIENTO=$(echo "$RECARGA" | jq -r '.idMovimientoWallet')
ok "recarga-manual → idMovimientoWallet=$ID_MOVIMIENTO"

# ─────────────────────────────────────────────
# 6. Validaciones en base de datos
# ─────────────────────────────────────────────
info "Validaciones SQL en base de datos"

check_table() {
  local table="$1"
  local count
  count=$("$SQLCMD" -S "$DB_HOST" -U sa -P "$SA_PASS" \
    -d "$DB_NAME" -b -C \
    -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM $table" \
    -h -1 | tr -d ' \r\n')
  [[ "$count" =~ ^[0-9]+$ ]] || fail "No se pudo obtener el conteo de la tabla $table (resultado: '$count')"
  [[ "$count" -gt 0 ]] || fail "Tabla $table sin registros"
  ok "Tabla $table → $count registro(s)"
}

check_table "wallet_saldos"
check_table "wallet_movimientos"
check_table "ledger_transacciones"
check_table "ledger_movimientos"
check_table "auditoria"

echo ""
ok "═══ VALIDACIÓN COMPLETA: todos los endpoints y tablas OK ═══"
