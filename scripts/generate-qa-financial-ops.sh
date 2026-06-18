#!/usr/bin/env bash
# ================================================================
# XPAY MVP — QA Financial Operations Script
# scripts/generate-qa-financial-ops.sh
# ================================================================
#
# !! XPAY QA only.                                              !!
# !! Do not use in production.                                  !!
# !! Do not use real money.                                     !!
# !! Do not use real customer data.                             !!
#
# Genera el flujo financiero QA completo usando los endpoints
# reales del backend. No ejecuta SQL. No hace deploy.
# No contiene secretos.
#
# Operaciones ejecutadas (en orden):
#   A. Recarga wallet usuario 1
#   B. Transferencia usuario 1 → usuario 2
#   C. Pago QR QA → venta en CONTINGENCIA
#   D. Liquidar venta QR → venta LIQUIDADA
#   E. Solicitar retiro QA 1 → retiro PENDIENTE
#   F. Confirmar pago retiro 1 → retiro PAGADO
#   G. Solicitar retiro QA 2 → retiro PENDIENTE (para rechazar)
#   H. Rechazar retiro 2 → retiro RECHAZADO
#
# Prerrequisitos:
#   - Scripts SQL 001-008 ejecutados
#   - Backend corriendo en $API_BASE
#   - Token JWT válido en $TOKEN
#   - Wallets, comercio y QR QA existentes en base de datos
#   - Usuario QA habilitado con hash BCrypt real
#
# Uso:
#   export API_BASE="http://localhost:5000"
#   export TOKEN="<jwt-token>"
#   export ID_WALLET_USUARIO_1="<id>"
#   export ID_WALLET_USUARIO_2="<id>"
#   export ID_USUARIO_QA="<id>"
#   export ID_COMERCIO_QA="<id>"
#
#   bash scripts/generate-qa-financial-ops.sh
#
# Variables opcionales (precargar si ya se conocen los IDs):
#   export ID_VENTA_QR="<id>"
#   export ID_RETIRO_1="<id>"
#   export ID_RETIRO_2="<id>"
#
# Documentación completa:
#   docs/QA_FINANCIAL_OPERATIONS_API.md
# ================================================================

set -euo pipefail

# ----------------------------------------------------------------
# Colores y helpers de salida
# ----------------------------------------------------------------
RED='\033[0;31m'
GRN='\033[0;32m'
YLW='\033[1;33m'
BLU='\033[0;34m'
NC='\033[0m'

print_header() { echo -e "\n${BLU}================================================================${NC}"; echo -e "${BLU}  $1${NC}"; echo -e "${BLU}================================================================${NC}"; }
print_step()   { echo -e "\n${YLW}--- $1 ---${NC}"; }
print_ok()     { echo -e "${GRN}  OK: $1${NC}"; }
print_warn()   { echo -e "${YLW}  WARN: $1${NC}"; }
print_err()    { echo -e "${RED}  ERROR: $1${NC}" >&2; }

# ----------------------------------------------------------------
# Aviso de seguridad
# ----------------------------------------------------------------
print_header "XPAY QA Financial Operations Script"
echo ""
echo -e "${RED}  !! XPAY QA only.                    !!"
echo -e "  !! Do not use in production.         !!"
echo -e "  !! Do not use real money.             !!"
echo -e "  !! Do not use real customer data.     !!${NC}"
echo ""

# ----------------------------------------------------------------
# Validación de variables de entorno requeridas
# ----------------------------------------------------------------
print_step "Validating required environment variables"

REQUIRED_VARS=(API_BASE TOKEN ID_WALLET_USUARIO_1 ID_WALLET_USUARIO_2 ID_USUARIO_QA ID_COMERCIO_QA)
MISSING=0
for VAR in "${REQUIRED_VARS[@]}"; do
    if [ -z "${!VAR:-}" ]; then
        print_err "Missing required environment variable: ${VAR}"
        MISSING=1
    else
        print_ok "${VAR} is set"
    fi
done

if [ "$MISSING" -eq 1 ]; then
    echo ""
    echo "  Set all required variables and re-run:"
    echo "    export API_BASE=\"http://localhost:5000\""
    echo "    export TOKEN=\"<jwt-token>\""
    echo "    export ID_WALLET_USUARIO_1=\"<id>\""
    echo "    export ID_WALLET_USUARIO_2=\"<id>\""
    echo "    export ID_USUARIO_QA=\"<id>\""
    echo "    export ID_COMERCIO_QA=\"<id>\""
    echo "    bash scripts/generate-qa-financial-ops.sh"
    exit 1
fi

# ----------------------------------------------------------------
# Detectar jq
# ----------------------------------------------------------------
HAS_JQ=0
if command -v jq &>/dev/null; then
    HAS_JQ=1
    print_ok "jq found — IDs will be extracted automatically from responses"
else
    print_warn "jq not found — IDs will not be extracted automatically."
    print_warn "If ID_VENTA_QR / ID_RETIRO_1 / ID_RETIRO_2 are not pre-set,"
    print_warn "the script will print the raw response and exit."
    print_warn "Install jq or export the IDs manually before each dependent step."
fi

# ----------------------------------------------------------------
# Helper: llamada API con Authorization header
# ----------------------------------------------------------------
api_get() {
    local endpoint="$1"
    curl -s "${API_BASE}${endpoint}" \
         -H "Authorization: Bearer ${TOKEN}"
}

api_post() {
    local endpoint="$1"
    local body="$2"
    curl -s -X POST "${API_BASE}${endpoint}" \
         -H "Authorization: Bearer ${TOKEN}" \
         -H "Content-Type: application/json" \
         -d "${body}"
}

# ----------------------------------------------------------------
# Helper: verificar "success": true en respuesta
# ----------------------------------------------------------------
check_ok() {
    local response="$1"
    local label="$2"
    if echo "${response}" | grep -qE '"success"\s*:\s*false'; then
        print_err "Step ${label} failed. Response:"
        echo "${response}"
        exit 1
    fi
    print_ok "${label} succeeded"
}

# ----------------------------------------------------------------
# Helper: extraer campo numérico de data.field con jq o grep
# ----------------------------------------------------------------
extract_id() {
    local response="$1"
    local field="$2"        # nombre del campo en data (ej: idVentaQr)
    if [ "$HAS_JQ" -eq 1 ]; then
        local val
        val=$(echo "${response}" | jq -r ".data.${field} // empty" 2>/dev/null || true)
        echo "${val}"
    else
        # Fallback: grep numérico después del campo
        echo "${response}" | grep -oE "\"${field}\"[[:space:]]*:[[:space:]]*[0-9]+" \
            | grep -oE '[0-9]+$' || true
    fi
}

# ================================================================
# PASO 0: Health check
# ================================================================
print_step "Step 0 — Backend health check"
HEALTH=$(curl -s "${API_BASE}/health" || true)
if [ -z "${HEALTH}" ]; then
    print_err "Backend did not respond at ${API_BASE}/health"
    echo "  Make sure the backend is running and API_BASE is correct."
    exit 1
fi
print_ok "Backend is reachable at ${API_BASE}"
echo "  Response: ${HEALTH}"

# ================================================================
# PASO A: Recarga wallet usuario 1
# ================================================================
print_step "Step A — Recharge wallet usuario 1 (+100,000 QA)"

RESP_A=$(api_post "/api/wallets/${ID_WALLET_USUARIO_1}/recarga-manual" \
    "{
      \"valor\": 100000,
      \"creadoPor\": ${ID_USUARIO_QA},
      \"referenciaExterna\": \"QA-RECARGA-SCRIPT-001\",
      \"observacion\": \"Recarga QA generada por script local sin dinero real\"
    }")
echo "  Response: ${RESP_A}"
check_ok "${RESP_A}" "A (recarga-manual)"

# ================================================================
# PASO B: Transferencia usuario 1 → usuario 2
# ================================================================
print_step "Step B — Transfer wallet 1 → wallet 2 (25,000 QA)"

RESP_B=$(api_post "/api/wallets/transferencia" \
    "{
      \"idWalletOrigen\": ${ID_WALLET_USUARIO_1},
      \"idWalletDestino\": ${ID_WALLET_USUARIO_2},
      \"valor\": 25000,
      \"descripcion\": \"Transferencia QA generada por script local\",
      \"creadoPor\": ${ID_USUARIO_QA}
    }")
echo "  Response: ${RESP_B}"
check_ok "${RESP_B}" "B (transferencia)"

# ================================================================
# PASO C: Pago QR QA → capturar ID_VENTA_QR
# ================================================================
print_step "Step C — QR payment QA (30,000 QA) → venta en CONTINGENCIA"

RESP_C=$(api_post "/api/qr/pagar" \
    "{
      \"codigoQr\": \"QR-DEMO-XPAY-QA-001\",
      \"idWalletUsuario\": ${ID_WALLET_USUARIO_1},
      \"valor\": 30000,
      \"descripcion\": \"Pago QR QA generado por script local\",
      \"creadoPor\": ${ID_USUARIO_QA}
    }")
echo "  Response: ${RESP_C}"
check_ok "${RESP_C}" "C (qr/pagar)"

# Capturar ID_VENTA_QR si no viene del entorno
if [ -z "${ID_VENTA_QR:-}" ]; then
    ID_VENTA_QR=$(extract_id "${RESP_C}" "idVentaQr")
fi

if [ -z "${ID_VENTA_QR:-}" ]; then
    print_err "Could not determine ID_VENTA_QR."
    echo ""
    echo "  jq is not installed and automatic extraction failed."
    echo "  Run: GET ${API_BASE}/api/admin/ventas-qr"
    echo "  Find the venta in CONTINGENCIA, then re-run with:"
    echo "    export ID_VENTA_QR=<id>"
    echo "    bash scripts/generate-qa-financial-ops.sh"
    exit 1
fi
print_ok "ID_VENTA_QR = ${ID_VENTA_QR}"

# ================================================================
# PASO D: Liquidar venta QR
# ================================================================
print_step "Step D — Liquidate QR sale ${ID_VENTA_QR} → LIQUIDADA"

RESP_D=$(api_post "/api/comercios/liquidar-venta-qr" \
    "{
      \"idVentaQr\": ${ID_VENTA_QR},
      \"creadoPor\": ${ID_USUARIO_QA},
      \"observacion\": \"Liquidacion QA generada por script local\"
    }")
echo "  Response: ${RESP_D}"
check_ok "${RESP_D}" "D (liquidar-venta-qr)"

# ================================================================
# PASO E: Solicitar retiro 1 → capturar ID_RETIRO_1
# ================================================================
print_step "Step E — Request withdrawal 1 (20,000 QA) → PENDIENTE"

RESP_E=$(api_post "/api/comercios/solicitar-retiro" \
    "{
      \"idComercio\": ${ID_COMERCIO_QA},
      \"valor\": 20000,
      \"medioRetiro\": \"TRANSFERENCIA_QA\",
      \"banco\": \"BANCO QA\",
      \"tipoCuenta\": \"AHORROS\",
      \"numeroCuenta\": \"0000000001\",
      \"titularCuenta\": \"Comercio Demo XPAY QA\",
      \"documentoTitular\": \"900999001-1\",
      \"observacion\": \"Retiro QA 1 generado por script local sin dinero real\",
      \"creadoPor\": ${ID_USUARIO_QA}
    }")
echo "  Response: ${RESP_E}"
check_ok "${RESP_E}" "E (solicitar-retiro 1)"

if [ -z "${ID_RETIRO_1:-}" ]; then
    ID_RETIRO_1=$(extract_id "${RESP_E}" "idRetiro")
fi

if [ -z "${ID_RETIRO_1:-}" ]; then
    print_err "Could not determine ID_RETIRO_1."
    echo ""
    echo "  Run: GET ${API_BASE}/api/comercios/retiros"
    echo "  Find the retiro in PENDIENTE, then re-run with:"
    echo "    export ID_RETIRO_1=<id>"
    echo "    bash scripts/generate-qa-financial-ops.sh"
    exit 1
fi
print_ok "ID_RETIRO_1 = ${ID_RETIRO_1}"

# ================================================================
# PASO F: Confirmar pago retiro 1
# ================================================================
print_step "Step F — Confirm payment for withdrawal ${ID_RETIRO_1} → PAGADO"

RESP_F=$(api_post "/api/comercios/retiros/confirmar-pago" \
    "{
      \"idRetiro\": ${ID_RETIRO_1},
      \"referenciaPago\": \"QA-PAGO-RETIRO-SCRIPT-001\",
      \"observacion\": \"Confirmacion QA generada por script local sin dinero real\",
      \"creadoPor\": ${ID_USUARIO_QA}
    }")
echo "  Response: ${RESP_F}"
check_ok "${RESP_F}" "F (retiros/confirmar-pago)"

# ================================================================
# PASO G: Solicitar retiro 2 → capturar ID_RETIRO_2
# ================================================================
print_step "Step G — Request withdrawal 2 (5,000 QA) → PENDIENTE (for rejection)"

RESP_G=$(api_post "/api/comercios/solicitar-retiro" \
    "{
      \"idComercio\": ${ID_COMERCIO_QA},
      \"valor\": 5000,
      \"medioRetiro\": \"TRANSFERENCIA_QA\",
      \"banco\": \"BANCO QA\",
      \"tipoCuenta\": \"AHORROS\",
      \"numeroCuenta\": \"0000000002\",
      \"titularCuenta\": \"Comercio Demo XPAY QA\",
      \"documentoTitular\": \"900999001-1\",
      \"observacion\": \"Retiro QA 2 para prueba de rechazo — sin dinero real\",
      \"creadoPor\": ${ID_USUARIO_QA}
    }")
echo "  Response: ${RESP_G}"
check_ok "${RESP_G}" "G (solicitar-retiro 2)"

if [ -z "${ID_RETIRO_2:-}" ]; then
    ID_RETIRO_2=$(extract_id "${RESP_G}" "idRetiro")
fi

if [ -z "${ID_RETIRO_2:-}" ]; then
    print_err "Could not determine ID_RETIRO_2."
    echo ""
    echo "  Run: GET ${API_BASE}/api/comercios/retiros"
    echo "  Find the second retiro in PENDIENTE, then re-run with:"
    echo "    export ID_RETIRO_2=<id>"
    echo "    bash scripts/generate-qa-financial-ops.sh"
    exit 1
fi
print_ok "ID_RETIRO_2 = ${ID_RETIRO_2}"

# ================================================================
# PASO H: Rechazar retiro 2
# ================================================================
print_step "Step H — Reject withdrawal ${ID_RETIRO_2} → RECHAZADO"

RESP_H=$(api_post "/api/comercios/retiros/rechazar" \
    "{
      \"idRetiro\": ${ID_RETIRO_2},
      \"motivoRechazo\": \"Rechazo QA controlado generado por script local\",
      \"observacion\": \"Rechazo QA sin movimiento real de dinero\",
      \"creadoPor\": ${ID_USUARIO_QA}
    }")
echo "  Response: ${RESP_H}"
check_ok "${RESP_H}" "H (retiros/rechazar)"

# ================================================================
# CONSULTAS FINALES
# ================================================================
print_header "Final state queries"

print_step "Wallet saldo — usuario 1"
api_get "/api/wallets/${ID_WALLET_USUARIO_1}/saldo"
echo ""

print_step "Wallet saldo — usuario 2"
api_get "/api/wallets/${ID_WALLET_USUARIO_2}/saldo"
echo ""

print_step "Resumen comercio QA"
api_get "/api/reportes/comercios/${ID_COMERCIO_QA}/resumen"
echo ""

print_step "Resumen general de operaciones"
api_get "/api/reportes/operaciones/resumen-general"
echo ""

print_step "Ventas QR (últimas 5)"
api_get "/api/admin/ventas-qr?page=1&pageSize=5"
echo ""

print_step "Retiros (últimos 5)"
api_get "/api/comercios/retiros?page=1&pageSize=5"
echo ""

print_step "Ledger transacciones (últimas 5)"
api_get "/api/admin/ledger-transacciones?page=1&pageSize=5"
echo ""

# ================================================================
# RESUMEN FINAL
# ================================================================
print_header "QA Financial Operations — Completed"
echo ""
echo -e "${GRN}  Operations executed successfully:${NC}"
echo "    A. Recarga wallet usuario 1       +100,000"
echo "    B. Transferencia usuario 1→2       -25,000 / +25,000"
echo "    C. Pago QR QA (venta ${ID_VENTA_QR})     -30,000 (CONTINGENCIA)"
echo "    D. Liquidación venta QR ${ID_VENTA_QR}   → LIQUIDADA"
echo "    E. Retiro 1 (${ID_RETIRO_1})               -20,000 (PENDIENTE)"
echo "    F. Confirmación retiro 1 (${ID_RETIRO_1}) → PAGADO"
echo "    G. Retiro 2 (${ID_RETIRO_2})                -5,000 (PENDIENTE)"
echo "    H. Rechazo retiro 2 (${ID_RETIRO_2})      → RECHAZADO (+5,000 devuelto)"
echo ""
echo -e "${YLW}  Ledger flow:${NC}"
echo "    Recarga:     110101 D / 210101 C"
echo "    Pago QR:     210101 D / 210201 C"
echo "    Liquidacion: 210201 D / 210202 C"
echo "    Retiro:      210202 D / 210203 C"
echo "    Confirmado:  210203 D / 110101 C"
echo "    Rechazado:   210203 D / 210202 C"
echo ""
echo -e "${YLW}  Next steps:${NC}"
echo "    - Validate accounting balance in GET /api/admin/ledger-transacciones"
echo "    - Run manual QA cases in docs/QA_MANUAL_TESTING.md"
echo "    - See docs/QA_FINANCIAL_OPERATIONS_API.md for endpoint details"
echo ""
echo -e "${RED}  !! XPAY QA only. Do not use in production. !!${NC}"
echo ""
