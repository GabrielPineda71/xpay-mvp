#!/usr/bin/env bash
# Valida los endpoints del backend XPAY MVP — Fases 1, 2, 3, 4, 5 y 6.
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

check_sql_value() {
  local label="$1" query="$2" esperado="$3"
  local resultado
  resultado=$("$SQLCMD" -S "$DB_HOST" -U sa -P "$SA_PASS" \
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

info "POST /api/auth/login (carlos)"
LOGIN_A=$(post_json "$API_URL/api/auth/login" '{
  "usuario": "carlos_ci_test",
  "password": "Xpay@Test1!"
}') || fail "POST login carlos no respondió"
echo "$LOGIN_A" | jq .
assert_ok "$LOGIN_A" "login carlos"
ID_PERSONA_A=$(echo "$LOGIN_A" | jq -r '.data.idPersona')
ok "Login carlos → idPersona=$ID_PERSONA_A"

info "GET /api/wallets/persona/$ID_PERSONA_A"
WALLET_A=$(get_json "$API_URL/api/wallets/persona/$ID_PERSONA_A") \
  || fail "GET wallet carlos no respondió"
echo "$WALLET_A" | jq .
assert_ok "$WALLET_A" "wallet carlos"
ID_WALLET_A=$(echo "$WALLET_A" | jq -r '.data.idWallet')
ok "Wallet carlos → idWallet=$ID_WALLET_A"

info "GET /api/wallets/$ID_WALLET_A/saldo (inicial)"
SALDO_A_INICIAL=$(get_json "$API_URL/api/wallets/$ID_WALLET_A/saldo") \
  || fail "GET saldo carlos inicial no respondió"
assert_ok "$SALDO_A_INICIAL" "saldo carlos inicial"
assert_saldo "$SALDO_A_INICIAL" 0 "Saldo inicial carlos"

info "POST /api/wallets/$ID_WALLET_A/recarga-manual (100.000)"
RECARGA=$(post_json "$API_URL/api/wallets/$ID_WALLET_A/recarga-manual" \
  "{\"valor\": 100000, \"creadoPor\": $ID_USUARIO_A, \"observacion\": \"Recarga CI fase 1\"}") \
  || fail "POST recarga carlos no respondió"
echo "$RECARGA" | jq .
assert_ok "$RECARGA" "recarga carlos"
ok "Recarga carlos 100.000 → OK"

info "GET /api/wallets/$ID_WALLET_A/saldo (tras recarga)"
SALDO_A_RECARGADO=$(get_json "$API_URL/api/wallets/$ID_WALLET_A/saldo") \
  || fail "GET saldo carlos post-recarga no respondió"
assert_saldo "$SALDO_A_RECARGADO" 100000 "Saldo carlos tras recarga"

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
ok "Login maria → idPersona=$ID_PERSONA_B"

info "GET /api/wallets/persona/$ID_PERSONA_B"
WALLET_B=$(get_json "$API_URL/api/wallets/persona/$ID_PERSONA_B") \
  || fail "GET wallet maria no respondió"
echo "$WALLET_B" | jq .
assert_ok "$WALLET_B" "wallet maria"
ID_WALLET_B=$(echo "$WALLET_B" | jq -r '.data.idWallet')
ok "Wallet maria → idWallet=$ID_WALLET_B"

info "POST /api/wallets/transferencia (25.000)"
TRANSFERENCIA=$(post_json "$API_URL/api/wallets/transferencia" \
  "{\"idWalletOrigen\": $ID_WALLET_A, \"idWalletDestino\": $ID_WALLET_B, \"valor\": 25000, \"creadoPor\": $ID_USUARIO_A, \"descripcion\": \"Transferencia CI fase 2\"}") \
  || fail "POST transferencia no respondió"
echo "$TRANSFERENCIA" | jq .
assert_ok "$TRANSFERENCIA" "transferencia"
ID_TRANSACCION_T=$(echo "$TRANSFERENCIA" | jq -r '.data.idTransaccion')
ok "Transferencia → idTransaccion=$ID_TRANSACCION_T"

info "GET /api/wallets/$ID_WALLET_A/saldo (tras transferencia)"
SALDO_A_POST_T=$(get_json "$API_URL/api/wallets/$ID_WALLET_A/saldo") \
  || fail "GET saldo carlos tras transferencia no respondió"
assert_saldo "$SALDO_A_POST_T" 75000 "Saldo carlos tras transferencia"

info "GET /api/wallets/$ID_WALLET_B/saldo (tras transferencia)"
SALDO_B_POST_T=$(get_json "$API_URL/api/wallets/$ID_WALLET_B/saldo") \
  || fail "GET saldo maria tras transferencia no respondió"
assert_saldo "$SALDO_B_POST_T" 25000 "Saldo maria tras transferencia"

# ════════════════════════════════════════════════════
# FASE 3 — Pago a comercio por QR
# ════════════════════════════════════════════════════
phase "FASE 3: Pago a comercio por QR"

info "POST /api/qr/pagar (30.000)"
PAGO_QR=$(post_json "$API_URL/api/qr/pagar" \
  "{\"codigoQr\": \"QR-DEMO-XPAY-001\", \"idWalletUsuario\": $ID_WALLET_A, \"valor\": 30000, \"creadoPor\": $ID_USUARIO_A, \"descripcion\": \"Pago QR CI fase 3\"}") \
  || fail "POST /api/qr/pagar no respondió"
echo "$PAGO_QR" | jq .
assert_ok "$PAGO_QR" "pago QR"

ID_VENTA_QR=$(echo "$PAGO_QR"      | jq -r '.data.idVentaQr')
ID_TRANSACCION_Q=$(echo "$PAGO_QR" | jq -r '.data.idTransaccion')
ESTADO_QR=$(echo "$PAGO_QR"        | jq -r '.data.estado')
[[ "$ESTADO_QR" == "CONTINGENCIA" ]] || fail "estado esperado CONTINGENCIA, obtenido $ESTADO_QR"
ok "Pago QR → idVentaQr=$ID_VENTA_QR  estado=$ESTADO_QR"

info "GET /api/wallets/$ID_WALLET_A/saldo (tras pago QR)"
SALDO_A_POST_QR=$(get_json "$API_URL/api/wallets/$ID_WALLET_A/saldo") \
  || fail "GET saldo carlos tras pago QR no respondió"
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
LIQUIDACION=$(post_json "$API_URL/api/comercios/liquidar-venta-qr" \
  "{\"idVentaQr\": $ID_VENTA_QR, \"creadoPor\": $ID_USUARIO_A, \"observacion\": \"Liquidacion CI fase 4\"}") \
  || fail "POST liquidar-venta-qr no respondió"
echo "$LIQUIDACION" | jq .
assert_ok "$LIQUIDACION" "liquidacion QR"

ID_COMERCIO=$(echo "$LIQUIDACION"        | jq -r '.data.idComercio')
ID_WALLET_COMERCIO=$(echo "$LIQUIDACION" | jq -r '.data.idWalletComercio')
ESTADO_VENTA_LIQ=$(echo "$LIQUIDACION"  | jq -r '.data.estadoVenta')
[[ "$ESTADO_VENTA_LIQ" == "LIQUIDADA" ]] || fail "estadoVenta esperado LIQUIDADA, obtenido $ESTADO_VENTA_LIQ"
ok "Liquidación → idComercio=$ID_COMERCIO  idWalletComercio=$ID_WALLET_COMERCIO  estadoVenta=$ESTADO_VENTA_LIQ"

# Verificar doble liquidación rechazada
info "Doble liquidación debe retornar error"
DOBLE_LIQ=$(post_json "$API_URL/api/comercios/liquidar-venta-qr" \
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
  "SELECT CAST(CAST(ws.saldo_disponible AS BIGINT) AS NVARCHAR(50)) FROM wallet_saldos ws INNER JOIN wallets w ON ws.id_wallet = w.id_wallet WHERE w.tipo_wallet = 'COMERCIO'" \
  "30000"

info "POST /api/comercios/solicitar-retiro (idComercio=$ID_COMERCIO, valor=20000)"
RETIRO=$(post_json "$API_URL/api/comercios/solicitar-retiro" \
  "{\"idComercio\": $ID_COMERCIO, \"valor\": 20000, \"medioRetiro\": \"TRANSFERENCIA_BANCARIA\", \"banco\": \"Banco Demo\", \"tipoCuenta\": \"AHORROS\", \"numeroCuenta\": \"1234567890\", \"titularCuenta\": \"Comercio Demo XPAY\", \"documentoTitular\": \"900123456\", \"observacion\": \"Retiro CI fase 5\", \"creadoPor\": $ID_USUARIO_A}") \
  || fail "POST solicitar-retiro no respondió"
echo "$RETIRO" | jq .
assert_ok "$RETIRO" "solicitar retiro"

ID_RETIRO=$(echo "$RETIRO"     | jq -r '.data.idRetiro')
ESTADO_RETIRO=$(echo "$RETIRO" | jq -r '.data.estado')
[[ "$ESTADO_RETIRO" == "PENDIENTE" ]] || fail "estado esperado PENDIENTE, obtenido $ESTADO_RETIRO"
ok "Retiro → idRetiro=$ID_RETIRO  valor=20000  estado=$ESTADO_RETIRO"

# Verificar saldo insuficiente rechazado
info "Retiro con saldo insuficiente debe retornar error"
RETIRO_INVALIDO=$(post_json "$API_URL/api/comercios/solicitar-retiro" \
  "{\"idComercio\": $ID_COMERCIO, \"valor\": 99999, \"creadoPor\": $ID_USUARIO_A}") || true
[[ "$(echo "$RETIRO_INVALIDO" | jq -r '.success' 2>/dev/null || echo false)" != "true" ]] \
  || fail "El retiro con saldo insuficiente debió fallar"
ok "Retiro con saldo insuficiente rechazado ✓"

# ════════════════════════════════════════════════════
# FASE 6 — Gestión de retiros: confirmar pago y rechazar
# ════════════════════════════════════════════════════
phase "FASE 6: Gestión de retiros del comercio"

# 6a: Confirmar el retiro de 20.000 como PAGADO
info "POST /api/comercios/retiros/confirmar-pago (idRetiro=$ID_RETIRO)"
CONFIRMAR=$(post_json "$API_URL/api/comercios/retiros/confirmar-pago" \
  "{\"idRetiro\": $ID_RETIRO, \"referenciaPago\": \"PAGO-MANUAL-CI-001\", \"observacion\": \"Pago manual CI fase 6\", \"creadoPor\": $ID_USUARIO_A}") \
  || fail "POST confirmar-pago no respondió"
echo "$CONFIRMAR" | jq .
assert_ok "$CONFIRMAR" "confirmar pago"

ESTADO_PAGADO=$(echo "$CONFIRMAR" | jq -r '.data.estado')
[[ "$ESTADO_PAGADO" == "PAGADO" ]] || fail "estado esperado PAGADO, obtenido $ESTADO_PAGADO"
ok "Retiro confirmado como PAGADO → idRetiro=$ID_RETIRO  estado=$ESTADO_PAGADO"

# Verificar que no se puede confirmar dos veces
info "Doble confirmación debe retornar error"
DOBLE_CONF=$(post_json "$API_URL/api/comercios/retiros/confirmar-pago" \
  "{\"idRetiro\": $ID_RETIRO, \"creadoPor\": $ID_USUARIO_A}") || true
[[ "$(echo "$DOBLE_CONF" | jq -r '.success' 2>/dev/null || echo false)" != "true" ]] \
  || fail "La doble confirmación debió fallar"
ok "Doble confirmación rechazada ✓"

# Wallet comercio sigue con saldo 10.000 (la confirmación no modifica wallet)
check_sql_value \
  "Saldo wallet comercio tras confirmar pago = 10000" \
  "SELECT CAST(CAST(ws.saldo_disponible AS BIGINT) AS NVARCHAR(50)) FROM wallet_saldos ws INNER JOIN wallets w ON ws.id_wallet = w.id_wallet WHERE w.tipo_wallet = 'COMERCIO'" \
  "10000"

# Verificar fecha_pago y referencia_pago en DB
check_sql_value \
  "retiro PAGADO tiene fecha_pago no nula" \
  "SELECT CASE WHEN fecha_pago IS NOT NULL THEN 'OK' ELSE 'NULL' END FROM retiros_comercio WHERE id_retiro = $ID_RETIRO" \
  "OK"

check_sql_value \
  "retiro PAGADO tiene referencia_pago correcta" \
  "SELECT referencia_pago FROM retiros_comercio WHERE id_retiro = $ID_RETIRO" \
  "PAGO-MANUAL-CI-001"

# 6b: Segundo retiro de 5.000 (saldo comercio = 10k - 5k = 5k)
info "POST /api/comercios/solicitar-retiro (segundo retiro: valor=5000)"
RETIRO_2=$(post_json "$API_URL/api/comercios/solicitar-retiro" \
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
  "SELECT CAST(CAST(ws.saldo_disponible AS BIGINT) AS NVARCHAR(50)) FROM wallet_saldos ws INNER JOIN wallets w ON ws.id_wallet = w.id_wallet WHERE w.tipo_wallet = 'COMERCIO'" \
  "5000"

# 6c: Rechazar el segundo retiro (saldo comercio = 5k + 5k = 10k)
info "POST /api/comercios/retiros/rechazar (idRetiro=$ID_RETIRO_2)"
RECHAZO=$(post_json "$API_URL/api/comercios/retiros/rechazar" \
  "{\"idRetiro\": $ID_RETIRO_2, \"motivoRechazo\": \"Cuenta bancaria inválida\", \"observacion\": \"Rechazo CI fase 6\", \"creadoPor\": $ID_USUARIO_A}") \
  || fail "POST rechazar no respondió"
echo "$RECHAZO" | jq .
assert_ok "$RECHAZO" "rechazar retiro"

ESTADO_RECHAZADO=$(echo "$RECHAZO" | jq -r '.data.estado')
[[ "$ESTADO_RECHAZADO" == "RECHAZADO" ]] || fail "estado esperado RECHAZADO, obtenido $ESTADO_RECHAZADO"
ok "Retiro rechazado → idRetiro=$ID_RETIRO_2  estado=$ESTADO_RECHAZADO"

# Verificar que no se puede rechazar dos veces
info "Doble rechazo debe retornar error"
DOBLE_RECH=$(post_json "$API_URL/api/comercios/retiros/rechazar" \
  "{\"idRetiro\": $ID_RETIRO_2, \"creadoPor\": $ID_USUARIO_A}") || true
[[ "$(echo "$DOBLE_RECH" | jq -r '.success' 2>/dev/null || echo false)" != "true" ]] \
  || fail "El doble rechazo debió fallar"
ok "Doble rechazo rechazado ✓"

# Saldo comercio restaurado a 10.000
check_sql_value \
  "Saldo wallet comercio tras rechazo = 10000 (restaurado)" \
  "SELECT CAST(CAST(ws.saldo_disponible AS BIGINT) AS NVARCHAR(50)) FROM wallet_saldos ws INNER JOIN wallets w ON ws.id_wallet = w.id_wallet WHERE w.tipo_wallet = 'COMERCIO'" \
  "10000"

# ════════════════════════════════════════════════════
# Validaciones SQL — acumulado Fases 1 + 2 + 3 + 4 + 5 + 6
# ════════════════════════════════════════════════════
phase "Validaciones SQL — acumulado Fases 1 a 6"

# wallet_saldos: carlos + maria + wallet_comercio (seed 004)
check_count "wallet_saldos"                3
# wallet_movimientos:
#   1 recarga + 2 transfer + 1 pago QR + 1 liquidación comercio
#   + 1 retiro 20k + 1 retiro 5k + 1 rechazo 5k (devuelto)
check_count "wallet_movimientos"           8
# ledger_transacciones:
#   recarga + transfer + QR + liquidación + retiro20k + pagado20k + retiro5k + rechazado5k
check_count "ledger_transacciones"         8
# ledger_movimientos: 2 × 8 transacciones
check_count "ledger_movimientos"          16
# auditoria:
#   2 registro + 1 recarga + 1 transfer + 1 QR + 1 liquidación
#   + 1 retiro20k + 1 pagado + 1 retiro5k + 1 rechazado
check_count "auditoria"                   10
check_count "ventas_qr"                    1
check_count "liquidaciones_comercio"       1
check_count "liquidacion_comercio_detalle" 1
check_count "retiros_comercio"             2

# Estados finales en retiros_comercio
check_sql_value \
  "Primer retiro estado = PAGADO" \
  "SELECT estado FROM retiros_comercio WHERE id_retiro = $ID_RETIRO" \
  "PAGADO"

check_sql_value \
  "Segundo retiro estado = RECHAZADO" \
  "SELECT estado FROM retiros_comercio WHERE id_retiro = $ID_RETIRO_2" \
  "RECHAZADO"

# Saldo final wallet comercio = 10.000
check_sql_value \
  "Saldo final wallet comercio = 10000" \
  "SELECT CAST(CAST(ws.saldo_disponible AS BIGINT) AS NVARCHAR(50)) FROM wallet_saldos ws INNER JOIN wallets w ON ws.id_wallet = w.id_wallet WHERE w.tipo_wallet = 'COMERCIO'" \
  "10000"

# Ledger PAGO_QR balanceado
check_sql_value \
  "Ledger PAGO_QR balanceado" \
  "SELECT CASE WHEN SUM(CASE WHEN naturaleza='D' THEN valor ELSE 0 END) = SUM(CASE WHEN naturaleza='C' THEN valor ELSE 0 END) THEN 'OK' ELSE 'DESBALANCEADO' END FROM ledger_movimientos WHERE id_transaccion_ledger = $ID_TRANSACCION_Q" \
  "OK"

# Ledger RETIRO_COMERCIO_PAGADO balanceado
check_sql_value \
  "Ledger RETIRO_COMERCIO_PAGADO balanceado" \
  "SELECT CASE WHEN SUM(CASE WHEN lm.naturaleza='D' THEN lm.valor ELSE 0 END) = SUM(CASE WHEN lm.naturaleza='C' THEN lm.valor ELSE 0 END) THEN 'OK' ELSE 'DESBALANCEADO' END FROM ledger_movimientos lm INNER JOIN ledger_transacciones lt ON lm.id_transaccion_ledger = lt.id_transaccion_ledger WHERE lt.tipo_transaccion = 'RETIRO_COMERCIO_PAGADO'" \
  "OK"

# Ledger RETIRO_COMERCIO_RECHAZADO balanceado
check_sql_value \
  "Ledger RETIRO_COMERCIO_RECHAZADO balanceado" \
  "SELECT CASE WHEN SUM(CASE WHEN lm.naturaleza='D' THEN lm.valor ELSE 0 END) = SUM(CASE WHEN lm.naturaleza='C' THEN lm.valor ELSE 0 END) THEN 'OK' ELSE 'DESBALANCEADO' END FROM ledger_movimientos lm INNER JOIN ledger_transacciones lt ON lm.id_transaccion_ledger = lt.id_transaccion_ledger WHERE lt.tipo_transaccion = 'RETIRO_COMERCIO_RECHAZADO'" \
  "OK"

echo ""
ok "═══ VALIDACIÓN COMPLETA FASES 1 a 6: todos los endpoints y tablas OK ═══"
