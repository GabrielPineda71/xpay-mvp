#!/usr/bin/env bash
# Valida los endpoints del backend XPAY MVP — Fase 1 y Fase 2.
# Variables: API_URL, DB_HOST, DB_NAME, SA_PASSWORD
set -euo pipefail

API_URL="${API_URL:-http://localhost:5000}"
DB_HOST="${DB_HOST:-localhost,1433}"
DB_NAME="${DB_NAME:-XPAY_MVP}"
SA_PASS="${SA_PASSWORD:-XpayCI@2024!}"
SQLCMD="/opt/mssql-tools18/bin/sqlcmd"

GREEN='\033[0;32m'
RED='\033[0;31m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
NC='\033[0m'

ok()    { echo -e "${GREEN}[OK]${NC}   $*"; }
fail()  { echo -e "${RED}[FAIL]${NC} $*"; exit 1; }
info()  { echo -e "${CYAN}━━━ $* ━━━${NC}"; }
phase() { echo -e "\n${YELLOW}═══ $* ═══${NC}"; }

post_json() {
  local url="$1" body="$2"
  curl -sf -X POST "$url" -H "Content-Type: application/json" --max-time 15 -d "$body"
}

get_json() {
  curl -sf --max-time 15 "$1"
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

# ════════════════════════════════════════════════════
# FASE 1 — Registro, login, wallet, recarga
# ════════════════════════════════════════════════════
phase "FASE 1: Registro, login, wallet, recarga"

# 1. Registro usuario origen (carlos)
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

# 2. Login usuario origen
info "POST /api/auth/login (carlos)"
LOGIN_A=$(post_json "$API_URL/api/auth/login" '{
  "usuario": "carlos_ci_test",
  "password": "Xpay@Test1!"
}') || fail "POST login carlos no respondió"
echo "$LOGIN_A" | jq .
assert_ok "$LOGIN_A" "login carlos"
ID_PERSONA_A=$(echo "$LOGIN_A" | jq -r '.data.idPersona')
ok "Login carlos → idPersona=$ID_PERSONA_A"

# 3. Wallet usuario origen
info "GET /api/wallets/persona/$ID_PERSONA_A"
WALLET_A=$(get_json "$API_URL/api/wallets/persona/$ID_PERSONA_A") \
  || fail "GET wallet carlos no respondió"
echo "$WALLET_A" | jq .
assert_ok "$WALLET_A" "wallet carlos"
ID_WALLET_A=$(echo "$WALLET_A" | jq -r '.data.idWallet')
ok "Wallet carlos → idWallet=$ID_WALLET_A"

# 4. Saldo inicial carlos (debe ser 0)
info "GET /api/wallets/$ID_WALLET_A/saldo (inicial)"
SALDO_A_INICIAL=$(get_json "$API_URL/api/wallets/$ID_WALLET_A/saldo") \
  || fail "GET saldo carlos inicial no respondió"
echo "$SALDO_A_INICIAL" | jq .
assert_ok "$SALDO_A_INICIAL" "saldo carlos inicial"
assert_saldo "$SALDO_A_INICIAL" 0 "Saldo inicial carlos"

# 5. Recarga manual 100.000 a carlos
info "POST /api/wallets/$ID_WALLET_A/recarga-manual"
RECARGA=$(post_json "$API_URL/api/wallets/$ID_WALLET_A/recarga-manual" \
  "{\"valor\": 100000, \"creadoPor\": $ID_USUARIO_A, \"observacion\": \"Recarga CI fase 1\"}") \
  || fail "POST recarga carlos no respondió"
echo "$RECARGA" | jq .
assert_ok "$RECARGA" "recarga carlos"
ok "Recarga carlos 100.000 → OK"

# 6. Verificar saldo tras recarga (debe ser 100.000)
info "GET /api/wallets/$ID_WALLET_A/saldo (tras recarga)"
SALDO_A_RECARGADO=$(get_json "$API_URL/api/wallets/$ID_WALLET_A/saldo") \
  || fail "GET saldo carlos post-recarga no respondió"
echo "$SALDO_A_RECARGADO" | jq .
assert_saldo "$SALDO_A_RECARGADO" 100000 "Saldo carlos tras recarga"

# ════════════════════════════════════════════════════
# FASE 2 — Transferencias XPAY a XPAY
# ════════════════════════════════════════════════════
phase "FASE 2: Transferencias XPAY a XPAY"

# 7. Registro usuario destino (maria)
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

# 8. Login usuario destino
info "POST /api/auth/login (maria)"
LOGIN_B=$(post_json "$API_URL/api/auth/login" '{
  "usuario": "maria_ci_test",
  "password": "Xpay@Test2!"
}') || fail "POST login maria no respondió"
echo "$LOGIN_B" | jq .
assert_ok "$LOGIN_B" "login maria"
ID_PERSONA_B=$(echo "$LOGIN_B" | jq -r '.data.idPersona')
ok "Login maria → idPersona=$ID_PERSONA_B"

# 9. Wallet usuario destino
info "GET /api/wallets/persona/$ID_PERSONA_B"
WALLET_B=$(get_json "$API_URL/api/wallets/persona/$ID_PERSONA_B") \
  || fail "GET wallet maria no respondió"
echo "$WALLET_B" | jq .
assert_ok "$WALLET_B" "wallet maria"
ID_WALLET_B=$(echo "$WALLET_B" | jq -r '.data.idWallet')
ok "Wallet maria → idWallet=$ID_WALLET_B"

# 10. Transferencia 25.000: carlos → maria
info "POST /api/wallets/transferencia (25.000: wallet $ID_WALLET_A → wallet $ID_WALLET_B)"
TRANSFERENCIA=$(post_json "$API_URL/api/wallets/transferencia" \
  "{\"idWalletOrigen\": $ID_WALLET_A, \"idWalletDestino\": $ID_WALLET_B, \"valor\": 25000, \"creadoPor\": $ID_USUARIO_A, \"descripcion\": \"Transferencia CI fase 2\"}") \
  || fail "POST transferencia no respondió"
echo "$TRANSFERENCIA" | jq .
assert_ok "$TRANSFERENCIA" "transferencia"
ID_TRANSACCION=$(echo "$TRANSFERENCIA" | jq -r '.data.idTransaccion')
ok "Transferencia → idTransaccion=$ID_TRANSACCION"

# 11. Verificar saldo origen tras transferencia (debe ser 75.000)
info "GET /api/wallets/$ID_WALLET_A/saldo (tras transferencia)"
SALDO_A_FINAL=$(get_json "$API_URL/api/wallets/$ID_WALLET_A/saldo") \
  || fail "GET saldo carlos tras transferencia no respondió"
echo "$SALDO_A_FINAL" | jq .
assert_saldo "$SALDO_A_FINAL" 75000 "Saldo carlos tras transferencia"

# 12. Verificar saldo destino tras transferencia (debe ser 25.000)
info "GET /api/wallets/$ID_WALLET_B/saldo (tras transferencia)"
SALDO_B_FINAL=$(get_json "$API_URL/api/wallets/$ID_WALLET_B/saldo") \
  || fail "GET saldo maria tras transferencia no respondió"
echo "$SALDO_B_FINAL" | jq .
assert_saldo "$SALDO_B_FINAL" 25000 "Saldo maria tras transferencia"

# ════════════════════════════════════════════════════
# Validaciones en base de datos
# ════════════════════════════════════════════════════
phase "Validaciones SQL en base de datos"

check_count() {
  local table="$1" min="${2:-1}"
  local count
  count=$("$SQLCMD" -S "$DB_HOST" -U sa -P "$SA_PASS" \
    -d "$DB_NAME" -b -C \
    -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM $table" \
    -h -1 | tr -d ' \r\n')
  [[ "$count" =~ ^[0-9]+$ ]] || fail "No se pudo obtener conteo de $table (resultado: '$count')"
  [[ "$count" -ge "$min" ]] || fail "Tabla $table tiene $count registro(s), se esperaban >= $min"
  ok "Tabla $table → $count registro(s) (esperado >= $min) ✓"
}

# 2 wallets registradas (carlos + maria)
check_count "wallet_saldos"         2
# 1 recarga + 2 movimientos de transferencia (débito origen + crédito destino)
check_count "wallet_movimientos"    3
# 1 transacción recarga + 1 transacción transferencia
check_count "ledger_transacciones"  2
# 2 movimientos ledger recarga + 2 movimientos ledger transferencia
check_count "ledger_movimientos"    4
# 2 registros de usuario + 1 recarga + 1 transferencia
check_count "auditoria"             4

echo ""
ok "═══ VALIDACIÓN COMPLETA FASES 1 y 2: todos los endpoints y tablas OK ═══"
