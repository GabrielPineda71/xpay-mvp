# XPAY MVP — Guía de Operaciones Financieras QA vía API

**Versión:** 1.0
**Fecha:** 2026-06-17
**Tipo:** Guía operativa QA — uso exclusivo QA / desarrollo

---

## 1. Propósito

Esta guía se usa **después de ejecutar los scripts SQL 001–008** para generar datos financieros QA usando los endpoints reales del backend, respetando la lógica transaccional y contable del sistema.

**Principios:**

- No insertar saldos, movimientos ledger, ventas QR ni retiros directamente en SQL. El sistema exige consistencia de doble entrada entre `ledger_transacciones`, `ledger_movimientos`, `wallet_movimientos` y `wallet_saldos`; insertar estas tablas manualmente crea estados contablemente inconsistentes que invalidan las pruebas.
- Toda operación financiera pasa por los endpoints del backend, que garantizan la integridad del ledger.
- No usar dinero real.
- No usar datos reales.
- No ejecutar en producción.

**Relación con otros instrumentos:**

| Instrumento | Rol |
|-------------|-----|
| `database/008_seed_qa_dataset.sql` | Crea entidades base: personas, usuarios, wallets, comercio QA, QR QA |
| `scripts/validate-backend.sh` | Flujo automatizado de referencia usado por CI |
| Esta guía | Procedimiento manual documentado para ejecución QA |

---

## 2. Flujo recomendado

```
  [0] ─── Ejecutar scripts SQL 001–008
  [1] ─── Verificar backend /health
  [2] ─── Login → obtener JWT
  [3] ─── Consultar wallets QA (GET /api/admin/wallets)
  [4] ─── Recargar wallet usuario QA           → wallet usuario +valor / ledger: 110101 D, 210101 C
  [5] ─── Transferir entre wallets QA          → wallet origen -valor / wallet destino +valor
  [6] ─── Pagar QR QA                          → wallet usuario -valor / venta en CONTINGENCIA
  [7] ─── Liquidar venta QR                    → wallet comercio +valor / venta LIQUIDADA
  [8] ─── Solicitar retiro comercio            → wallet comercio -valor / retiro PENDIENTE
  [9] ─── Confirmar pago de retiro             → retiro PAGADO
 [10] ─── Crear segundo retiro                 → retiro PENDIENTE (para probar rechazo)
 [11] ─── Rechazar segundo retiro              → wallet comercio +valor / retiro RECHAZADO
 [12] ─── Consultar reportes y ledger          → verificar balances
 [13] ─── Validar dashboard / listados         → verificar UI
```

> Cada paso puede ejecutarse de forma independiente si los datos previos ya existen. El orden es el recomendado para un ciclo completo desde cero.

---

## 3. Prerrequisitos

Antes de ejecutar cualquier operación de esta guía, confirmar:

- [ ] Scripts SQL `001` a `007` ejecutados sin errores (migraciones estructurales).
- [ ] Script SQL `008` ejecutado (`database/008_seed_qa_dataset.sql` — seed QA).
- [ ] Backend corriendo y accesible en `API_BASE`.
- [ ] Variables de entorno del backend configuradas: `ConnectionStrings__XpayConnection`, `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`.
- [ ] CORS configurado para el origen del cliente si se usa el frontend (`Cors__AllowedOrigins__0`).
- [ ] QR `QR-DEMO-XPAY-QA-001` existente en tabla `qr_comercios` (creado por script 008).
- [ ] Wallets QA existentes con `id_wallet` conocido (creadas por script 008 con saldo 0).
- [ ] Comercio Demo XPAY QA existente (creado por script 008).

**Sobre los usuarios QA del script 008:**

El script 008 crea usuarios (`qa.admin.xpay`, `qa.operador.xpay`, `qa.usuario1`, `qa.usuario2`) con un hash BCrypt placeholder que no corresponde a ninguna contraseña real. Estos usuarios **no pueden hacer login** hasta que se actualice el hash. Opciones para habilitar el login en QA:

| Opción | Procedimiento |
|--------|---------------|
| **Opción 1 (recomendada)** | Crear el usuario con contraseña real vía `POST /api/usuarios/registro-final` **antes** de ejecutar el script 008, o inmediatamente después si el usuario ya existe actualizar el hash con BCrypt cost-11 generado desde el proyecto .NET. |
| **Opción 2** | Usar los usuarios generados por `scripts/validate-backend.sh` en CI (`carlos_ci_test`, `maria_ci_test`). |
| **Opción 3** | Generar hash: `BCrypt.Net.BCrypt.HashPassword("XpayQA@Test1!")` desde el proyecto .NET y ejecutar `UPDATE usuarios SET password_hash = '<hash>' WHERE usuario = 'qa.admin.xpay'` solo en ambiente QA. |

No usar contraseñas reales ni compartir hashes entre ambientes.

---

## 4. Variables de entorno para los ejemplos

Los bloques `curl` de esta guía usan las siguientes variables. Son placeholders: reemplazar con los valores reales del ambiente QA antes de ejecutar.

> **Atajo:** copiar `ops/qa.env.example` a `ops/qa.env.local`, completar los valores y ejecutar `source ops/qa.env.local`. El archivo local está en `.gitignore` y nunca se commitea. Ver [`docs/QA_OPERATIONS_VARIABLES.md`](QA_OPERATIONS_VARIABLES.md) para instrucciones detalladas.

```bash
export API_BASE="http://localhost:5000"
export TOKEN="<jwt-token-obtenido-en-login>"
export ID_WALLET_USUARIO_1="<id-wallet-qa-usuario-uno>"
export ID_WALLET_USUARIO_2="<id-wallet-qa-usuario-dos>"
export ID_USUARIO_QA="<id-usuario-qa-creadoPor>"
export ID_COMERCIO_QA="<id-comercio-demo-xpay-qa>"
export ID_VENTA_QR="<id-venta-qr-del-paso-6>"
export ID_RETIRO_1="<id-retiro-del-paso-8>"
export ID_RETIRO_2="<id-retiro-del-paso-10>"
```

Para obtener los IDs después de ejecutar el script 008:

```sql
-- Wallets QA
SELECT w.id_wallet, w.nombre_wallet, p.numero_documento
FROM   wallets w JOIN personas p ON p.id_persona = w.id_persona
WHERE  p.numero_documento IN ('900000003','900000004');

-- Usuarios QA
SELECT id_usuario, usuario FROM usuarios
WHERE  usuario IN ('qa.admin.xpay','qa.operador.xpay','qa.usuario1','qa.usuario2');

-- Comercio QA
SELECT id_comercio, nombre_comercial FROM comercios
WHERE  nombre_comercial = N'Comercio Demo XPAY QA';
```

---

## 5. Login y token JWT

### Endpoint

```
POST /api/auth/login
Content-Type: application/json
```

### Ejemplo

```bash
curl -s -X POST "$API_BASE/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{
    "usuario": "qa.admin.xpay",
    "password": "<contraseña-qa-no-real>"
  }' | jq .
```

### Respuesta esperada

```json
{
  "success": true,
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "usuario": "qa.admin.xpay",
  "roles": ["ADMIN_XPAY"]
}
```

Copiar el valor de `token` a la variable `$TOKEN`.

### Si el usuario del seed no puede hacer login

Ver sección 3 — prerrequisitos de usuarios QA. El hash placeholder del script 008 hace que `BCrypt.Verify` retorne `false` (no lanza excepción). El backend responderá `401` con mensaje de credenciales inválidas.

---

## 6. Consultar wallets QA

### Listar todas las wallets (admin)

```bash
curl -s "$API_BASE/api/admin/wallets" \
  -H "Authorization: Bearer $TOKEN" | jq .
```

### Consultar saldo de una wallet específica

```bash
curl -s "$API_BASE/api/wallets/$ID_WALLET_USUARIO_1/saldo" \
  -H "Authorization: Bearer $TOKEN" | jq .
```

### Consultar movimientos de una wallet

```bash
curl -s "$API_BASE/api/wallets/$ID_WALLET_USUARIO_1/movimientos" \
  -H "Authorization: Bearer $TOKEN" | jq .
```

### Consultar wallet por persona

```bash
# GET /api/wallets/persona/{idPersona}
curl -s "$API_BASE/api/wallets/persona/<id-persona-qa>" \
  -H "Authorization: Bearer $TOKEN" | jq .
```

---

## 7. Recarga manual wallet QA

Carga saldo inicial en la wallet de usuario QA. Punto de entrada para que los demás flujos tengan fondos disponibles.

### Movimiento contable

```
DÉBITO  110101 (Efectivo en Bóveda)          +valor
CRÉDITO 210101 (Obligación Wallet Usuarios)  +valor
→ wallet usuario: saldo_disponible +valor
```

### Endpoint

```
POST /api/wallets/{idWallet}/recarga-manual
Authorization: Bearer <token>
Content-Type: application/json
```

### Ejemplo

```bash
curl -s -X POST "$API_BASE/api/wallets/$ID_WALLET_USUARIO_1/recarga-manual" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "valor": 100000,
    "creadoPor": '"$ID_USUARIO_QA"',
    "referenciaExterna": "QA-RECARGA-001",
    "observacion": "Recarga QA controlada sin dinero real"
  }' | jq .
```

### Respuesta esperada

```json
{
  "success": true,
  "message": "Recarga manual aplicada correctamente.",
  "idMovimientoWallet": 1
}
```

### Validaciones post-recarga

- [ ] `GET /api/wallets/$ID_WALLET_USUARIO_1/saldo` → `saldo_disponible` aumentó en 100000.
- [ ] `GET /api/wallets/$ID_WALLET_USUARIO_1/movimientos` → aparece movimiento `RECARGA_MANUAL`.
- [ ] `GET /api/admin/ledger-transacciones` → aparece transacción con entrada débito en cuenta `110101` y crédito en cuenta `210101`.
- [ ] Débitos = Créditos en la transacción ledger.

---

## 8. Transferencia entre wallets QA

Transfiere saldo entre dos wallets de usuario QA. Útil para probar el módulo de transferencias antes del pago QR.

### Movimiento contable

```
DÉBITO  210101 (wallet origen, reasignación)
CRÉDITO 210101 (wallet destino, reasignación)
→ wallet origen: saldo_disponible -valor
→ wallet destino: saldo_disponible +valor
```

### Endpoint

```
POST /api/wallets/transferencia
Authorization: Bearer <token>
Content-Type: application/json
```

### Ejemplo

```bash
curl -s -X POST "$API_BASE/api/wallets/transferencia" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "idWalletOrigen": '"$ID_WALLET_USUARIO_1"',
    "idWalletDestino": '"$ID_WALLET_USUARIO_2"',
    "valor": 25000,
    "descripcion": "Transferencia QA entre wallets ficticias",
    "creadoPor": '"$ID_USUARIO_QA"'
  }' | jq .
```

### Respuesta esperada

```json
{
  "success": true,
  "message": "Transferencia realizada exitosamente.",
  "data": {
    "idTransaccion": 2,
    "idWalletOrigen": 1,
    "idWalletDestino": 2,
    "valor": 25000
  }
}
```

### Validaciones post-transferencia

- [ ] `GET /api/wallets/$ID_WALLET_USUARIO_1/saldo` → saldo disminuyó en 25000.
- [ ] `GET /api/wallets/$ID_WALLET_USUARIO_2/saldo` → saldo aumentó en 25000.
- [ ] Ambos movimientos aparecen en `/movimientos` de cada wallet.
- [ ] Ledger balanceado: débito = crédito.
- [ ] `GET /api/admin/ledger-transacciones` → aparece transacción de tipo `TRANSFERENCIA`.

---

## 9. Pago QR QA

El usuario paga al comercio QA usando el código QR de demo. La venta queda en estado `CONTINGENCIA` hasta ser liquidada.

### Movimiento contable

```
DÉBITO  210101 (Obligación Wallet Usuarios)       -valor
CRÉDITO 210201 (Ventas QR en Contingencia)        +valor
→ wallet usuario: saldo_disponible -valor
→ venta_qr: estado = CONTINGENCIA
```

### Prerequisito

- Wallet usuario tiene saldo suficiente (ejecutar paso 7 primero si saldo = 0).
- QR `QR-DEMO-XPAY-QA-001` existe y está activo.

### Endpoint

```
POST /api/qr/pagar
Authorization: Bearer <token>
Content-Type: application/json
```

### Ejemplo

```bash
curl -s -X POST "$API_BASE/api/qr/pagar" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "codigoQr": "QR-DEMO-XPAY-QA-001",
    "idWalletUsuario": '"$ID_WALLET_USUARIO_1"',
    "valor": 30000,
    "descripcion": "Pago QR QA sin dinero real",
    "creadoPor": '"$ID_USUARIO_QA"'
  }' | jq .
```

### Respuesta esperada

```json
{
  "success": true,
  "message": "Pago QR realizado exitosamente.",
  "data": {
    "idVentaQr": 1,
    "idTransaccion": 3,
    "idComercio": 1,
    "idWalletUsuario": 1,
    "valor": 30000,
    "estado": "CONTINGENCIA"
  }
}
```

Guardar `idVentaQr` en `$ID_VENTA_QR`.

### Validaciones post-pago QR

- [ ] `GET /api/wallets/$ID_WALLET_USUARIO_1/saldo` → saldo disminuyó en 30000.
- [ ] `GET /api/admin/ventas-qr` → aparece venta con estado `CONTINGENCIA`.
- [ ] Ledger mueve de cuenta `210101` (débito) a `210201` (crédito).
- [ ] Wallet del comercio NO cambia todavía (el saldo llega al liquidar).

---

## 10. Liquidación de venta QR QA

Mueve la venta del estado `CONTINGENCIA` a `LIQUIDADA` y acredita el saldo en la wallet del comercio QA.

### Movimiento contable

```
DÉBITO  210201 (Ventas QR en Contingencia)        -valor
CRÉDITO 210202 (Obligación Wallet Comercios)      +valor
→ wallet comercio: saldo_disponible +valor (neto de comisiones si aplica)
→ venta_qr: estado = LIQUIDADA
```

### Endpoint

```
POST /api/comercios/liquidar-venta-qr
Authorization: Bearer <token>
Content-Type: application/json
```

### Ejemplo

```bash
curl -s -X POST "$API_BASE/api/comercios/liquidar-venta-qr" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "idVentaQr": '"$ID_VENTA_QR"',
    "creadoPor": '"$ID_USUARIO_QA"',
    "observacion": "Liquidacion QR QA sin dinero real"
  }' | jq .
```

### Respuesta esperada

```json
{
  "success": true,
  "message": "Venta QR liquidada exitosamente.",
  "data": {
    "idLiquidacion": 1,
    "idVentaQr": 1,
    "idComercio": 1,
    "idWalletComercio": 3,
    "valorNeto": 30000,
    "estadoVenta": "LIQUIDADA"
  }
}
```

### Validaciones post-liquidación

- [ ] `GET /api/admin/ventas-qr` → venta aparece con estado `LIQUIDADA`.
- [ ] `GET /api/wallets/<id-wallet-comercio>/saldo` → saldo del comercio aumentó.
- [ ] Ledger mueve de cuenta `210201` (débito) a `210202` (crédito).
- [ ] `GET /api/reportes/comercios/$ID_COMERCIO_QA/resumen` → refleja venta liquidada.

---

## 11. Solicitud de retiro comercio QA

El comercio retira fondos de su wallet. La wallet disminuye y el retiro queda en estado `PENDIENTE` hasta que un operador confirme o rechace el pago.

### Movimiento contable

```
DÉBITO  210202 (Obligación Wallet Comercios)      -valor
CRÉDITO 210203 (Retiros Pendientes de Pago)       +valor
→ wallet comercio: saldo_disponible -valor
→ retiro: estado = PENDIENTE
```

### Endpoint

```
POST /api/comercios/solicitar-retiro
Authorization: Bearer <token>
Content-Type: application/json
```

### Ejemplo

```bash
curl -s -X POST "$API_BASE/api/comercios/solicitar-retiro" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "idComercio": '"$ID_COMERCIO_QA"',
    "valor": 20000,
    "medioRetiro": "TRANSFERENCIA_BANCARIA",
    "banco": "Banco QA Demo",
    "tipoCuenta": "CORRIENTE",
    "numeroCuenta": "0000000001",
    "titularCuenta": "Comercio Demo XPAY QA SAS",
    "documentoTitular": "900999001",
    "observacion": "Retiro QA pendiente sin dinero real",
    "creadoPor": '"$ID_USUARIO_QA"'
  }' | jq .
```

### Respuesta esperada

```json
{
  "success": true,
  "message": "Solicitud de retiro creada exitosamente.",
  "data": {
    "idRetiro": 1,
    "idComercio": 1,
    "idWalletComercio": 3,
    "valor": 20000,
    "estado": "PENDIENTE"
  }
}
```

Guardar `idRetiro` en `$ID_RETIRO_1`.

### Validaciones post-solicitud

- [ ] `GET /api/comercios/retiros` → aparece retiro con estado `PENDIENTE`.
- [ ] `GET /api/wallets/<id-wallet-comercio>/saldo` → saldo disminuyó en 20000.
- [ ] Ledger mueve de cuenta `210202` (débito) a `210203` (crédito).

---

## 12. Confirmar pago de retiro QA

Un operador XPAY marca el retiro como pagado. Esto representa que el dinero fue enviado al comercio fuera del sistema (transferencia bancaria real en producción). En QA es una confirmación controlada sin dinero real.

### Movimiento contable

```
DÉBITO  210203 (Retiros Pendientes de Pago)   -valor
CRÉDITO 110101 (Efectivo en Bóveda)           +valor
→ wallet comercio: no cambia (ya se dedujo en solicitar-retiro)
→ retiro: estado = PAGADO
```

### Endpoint

```
POST /api/comercios/retiros/confirmar-pago
Authorization: Bearer <token>
Content-Type: application/json
```

### Ejemplo

```bash
curl -s -X POST "$API_BASE/api/comercios/retiros/confirmar-pago" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "idRetiro": '"$ID_RETIRO_1"',
    "referenciaPago": "QA-PAGO-RETIRO-001",
    "observacion": "Confirmacion de pago QA sin dinero real",
    "creadoPor": '"$ID_USUARIO_QA"'
  }' | jq .
```

### Respuesta esperada

```json
{
  "success": true,
  "message": "Retiro marcado como pagado exitosamente.",
  "data": {
    "idRetiro": 1,
    "estado": "PAGADO",
    "valor": 20000
  }
}
```

### Validaciones post-confirmación

- [ ] `GET /api/comercios/retiros/$ID_RETIRO_1` → estado = `PAGADO`.
- [ ] Wallet comercio NO cambia de saldo (el cambio ocurrió al solicitar).
- [ ] Ledger mueve de cuenta `210203` (débito) a `110101` (crédito).
- [ ] En frontend: el retiro pagado no muestra botones de acción.

---

## 13. Rechazar retiro QA

Para probar el flujo de rechazo, primero crear un segundo retiro y luego rechazarlo. El rechazo devuelve el saldo a la wallet del comercio.

### Movimiento contable

```
DÉBITO  210203 (Retiros Pendientes de Pago)   -valor
CRÉDITO 210202 (Obligación Wallet Comercios)  +valor
→ wallet comercio: saldo_disponible +valor (devuelto)
→ retiro: estado = RECHAZADO
```

### Paso 1 — Crear segundo retiro

```bash
curl -s -X POST "$API_BASE/api/comercios/solicitar-retiro" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "idComercio": '"$ID_COMERCIO_QA"',
    "valor": 5000,
    "medioRetiro": "TRANSFERENCIA_BANCARIA",
    "banco": "Banco QA Demo",
    "tipoCuenta": "CORRIENTE",
    "numeroCuenta": "0000000001",
    "titularCuenta": "Comercio Demo XPAY QA SAS",
    "documentoTitular": "900999001",
    "observacion": "Segundo retiro QA para probar rechazo",
    "creadoPor": '"$ID_USUARIO_QA"'
  }' | jq .
```

Guardar el `idRetiro` del segundo retiro en `$ID_RETIRO_2`.

### Paso 2 — Rechazar el segundo retiro

```
POST /api/comercios/retiros/rechazar
Authorization: Bearer <token>
Content-Type: application/json
```

```bash
curl -s -X POST "$API_BASE/api/comercios/retiros/rechazar" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "idRetiro": '"$ID_RETIRO_2"',
    "motivoRechazo": "Rechazo QA controlado sin dinero real",
    "observacion": "Prueba de rechazo en ambiente QA",
    "creadoPor": '"$ID_USUARIO_QA"'
  }' | jq .
```

### Respuesta esperada

```json
{
  "success": true,
  "message": "Retiro rechazado exitosamente.",
  "data": {
    "idRetiro": 2,
    "estado": "RECHAZADO",
    "valor": 5000
  }
}
```

### Validaciones post-rechazo

- [ ] `GET /api/comercios/retiros/$ID_RETIRO_2` → estado = `RECHAZADO`.
- [ ] `GET /api/wallets/<id-wallet-comercio>/saldo` → saldo aumentó en 5000 (devuelto).
- [ ] Ledger mueve de cuenta `210203` (débito) a `210202` (crédito).
- [ ] En frontend: el retiro rechazado no muestra botones de acción.

---

## 14. Reportes y consultas finales

Una vez ejecutado el flujo completo, validar los reportes del sistema.

### Estado de cuenta de una wallet

```bash
curl -s "$API_BASE/api/reportes/wallet/$ID_WALLET_USUARIO_1/estado-cuenta" \
  -H "Authorization: Bearer $TOKEN" | jq .
```

### Resumen de comercio QA

```bash
curl -s "$API_BASE/api/reportes/comercios/$ID_COMERCIO_QA/resumen" \
  -H "Authorization: Bearer $TOKEN" | jq .
```

### Detalle de una transacción ledger

```bash
curl -s "$API_BASE/api/reportes/ledger/transaccion/<id-transaccion>" \
  -H "Authorization: Bearer $TOKEN" | jq .
```

### Resumen general de operaciones

```bash
curl -s "$API_BASE/api/reportes/operaciones/resumen-general" \
  -H "Authorization: Bearer $TOKEN" | jq .
```

### Listados administrativos

```bash
# Ventas QR
curl -s "$API_BASE/api/admin/ventas-qr" \
  -H "Authorization: Bearer $TOKEN" | jq .

# Transacciones ledger
curl -s "$API_BASE/api/admin/ledger-transacciones" \
  -H "Authorization: Bearer $TOKEN" | jq .

# Retiros
curl -s "$API_BASE/api/comercios/retiros" \
  -H "Authorization: Bearer $TOKEN" | jq .

# Wallets
curl -s "$API_BASE/api/admin/wallets" \
  -H "Authorization: Bearer $TOKEN" | jq .

# Comercios
curl -s "$API_BASE/api/admin/comercios" \
  -H "Authorization: Bearer $TOKEN" | jq .
```

---

## 15. Validación contable mínima

Checklist para confirmar que el ledger está balanceado y los saldos son consistentes al finalizar el flujo completo:

**Ledger:**

- [ ] Cada transacción ledger tiene `suma(débitos) = suma(créditos)`.
- [ ] Cada operación genera exactamente una entrada en `ledger_transacciones` con su par de movimientos en `ledger_movimientos`.
- [ ] La transacción de recarga tiene movimiento `DÉBITO 110101` y `CRÉDITO 210101`.
- [ ] La transacción de pago QR tiene movimiento `DÉBITO 210101` y `CRÉDITO 210201`.
- [ ] La transacción de liquidación tiene movimiento `DÉBITO 210201` y `CRÉDITO 210202`.
- [ ] La transacción de retiro tiene movimiento `DÉBITO 210202` y `CRÉDITO 210203`.
- [ ] La transacción de confirmación de pago tiene movimiento `DÉBITO 210203` y `CRÉDITO 110101`.
- [ ] La transacción de rechazo de retiro tiene movimiento `DÉBITO 210203` y `CRÉDITO 210202`.

**Wallets:**

- [ ] Saldo final de wallet usuario = recarga − transferencias salientes + transferencias entrantes − pagos QR.
- [ ] Saldo final de wallet comercio = valor liquidado − retiros solicitados + valor de retiros rechazados.
- [ ] Saldo retenido y en tránsito = 0 si no hay operaciones pendientes.

**Estados de entidades:**

- [ ] QR pagado y liquidado: venta QR en estado `LIQUIDADA`.
- [ ] Retiro confirmado: estado `PAGADO`; la wallet del comercio no recupera saldo.
- [ ] Retiro rechazado: estado `RECHAZADO`; la wallet del comercio recupera el saldo.

**Reportes:**

- [ ] `GET /api/reportes/operaciones/resumen-general` retorna totales coherentes con las operaciones ejecutadas.
- [ ] `GET /api/reportes/comercios/$ID_COMERCIO_QA/resumen` refleja ventas liquidadas y retiros del ciclo QA.
- [ ] Dashboard frontend muestra datos QA sin errores.

---

## 16. Script auxiliar de generación automática

**`scripts/generate-qa-financial-ops.sh`** automatiza el flujo documentado en esta guía (pasos A–H) usando los mismos endpoints `curl`. Útil para poblar un ambiente QA/dev desde cero sin ejecutar cada paso manualmente.

```bash
export API_BASE="http://localhost:5000"
export TOKEN="<jwt-token>"
export ID_WALLET_USUARIO_1="<id>"
export ID_WALLET_USUARIO_2="<id>"
export ID_USUARIO_QA="<id>"
export ID_COMERCIO_QA="<id>"

bash scripts/generate-qa-financial-ops.sh
```

**Requisitos:** token JWT válido, backend activo, scripts SQL 001–008 ejecutados, usuario QA con hash real habilitado.

**Comportamiento con/sin `jq`:**
- Con `jq` instalado: extrae `ID_VENTA_QR`, `ID_RETIRO_1`, `ID_RETIRO_2` automáticamente de las respuestas JSON.
- Sin `jq`: intenta extracción por grep. Si no puede, imprime la respuesta y solicita exportar los IDs manualmente antes de continuar.

> No contiene secretos. No ejecuta SQL. No hace deploy. Uso exclusivo QA / desarrollo.

---

## 17. Relación con `scripts/validate-backend.sh`

`scripts/validate-backend.sh` es el flujo automatizado usado por GitHub Actions en cada push. Sirve como referencia técnica de la secuencia real de operaciones financieras del sistema.

| Aspecto | `validate-backend.sh` | Esta guía |
|---------|----------------------|-----------|
| Uso | CI automatizado (GitHub Actions) | Ejecución manual QA |
| Usuarios | `carlos_ci_test`, `maria_ci_test` (creados por el script) | Usuarios QA del seed 008 |
| QR de referencia | `QR-DEMO-XPAY-001` | `QR-DEMO-XPAY-QA-001` |
| Propósito | Validación funcional de compilación | Pruebas manuales QA completas |
| Modificable en esta fase | No | N/A |

> No modificar `scripts/validate-backend.sh`. Si se necesita un flujo automatizado adicional, crear un script separado.

---

## 18. Problemas comunes

| Síntoma | Causa probable | Solución |
|---------|---------------|----------|
| `401 Unauthorized` al llamar cualquier endpoint | Token JWT expirado o no incluido en el header | Hacer login nuevamente (`POST /api/auth/login`) y actualizar `$TOKEN` |
| `401` en login con usuario del seed 008 | Hash BCrypt placeholder — el usuario no puede autenticarse | Ver sección 3: actualizar hash o usar usuario alternativo |
| `400` con "Saldo insuficiente" en pago QR o retiro | Wallet con saldo 0 o menor al valor solicitado | Ejecutar recarga manual (sección 7) antes de operar |
| `400` con "QR no encontrado" en pago QR | `QR-DEMO-XPAY-QA-001` no existe o no está activo | Verificar que script 008 se ejecutó; consultar `qr_comercios` |
| `400` al liquidar venta QR | Venta ya liquidada, o `idVentaQr` incorrecto | Consultar `GET /api/admin/ventas-qr` para obtener el ID correcto y el estado actual |
| `400` al confirmar pago de retiro | Retiro no está en estado `PENDIENTE` | Consultar `GET /api/comercios/retiros` para verificar estado |
| `400` al rechazar retiro | Retiro ya confirmado o rechazado | Crear un nuevo retiro en estado `PENDIENTE` (sección 13) |
| `GET /api/admin/ledger-transacciones` retorna lista vacía | No se ha ejecutado ninguna operación financiera aún | Ejecutar el flujo desde la sección 7 |
| Dashboard frontend sin datos | Operaciones no generadas o token inválido | Verificar token y que el flujo financiero QA esté ejecutado |
| Error CORS al llamar desde el frontend | `Cors__AllowedOrigins__0` no incluye el origen del frontend | Actualizar variable de entorno y reiniciar el backend |
| `500 Internal Server Error` en cualquier operación | Error de base de datos o configuración | Revisar logs del backend; verificar que scripts 001–008 se ejecutaron correctamente |

---

## 19. Documentos relacionados

| Documento | Propósito |
|-----------|-----------|
| [`database/008_seed_qa_dataset.sql`](../database/008_seed_qa_dataset.sql) | Seed QA: crea las entidades base necesarias antes de este flujo |
| [`scripts/validate-backend.sh`](../scripts/validate-backend.sh) | Flujo automatizado de referencia usado por CI |
| [`docs/QA_DEPLOYMENT_RUNBOOK.md`](QA_DEPLOYMENT_RUNBOOK.md) | Runbook operativo: recursos Azure, variables, scripts SQL, smoke test y rollback |
| [`docs/QA_MANUAL_TESTING.md`](QA_MANUAL_TESTING.md) | 35 casos de prueba manuales con pasos y resultados esperados |
| [`docs/QA_EXECUTION_TEMPLATE.md`](QA_EXECUTION_TEMPLATE.md) | Plantilla de registro de ejecución, evidencias, bugs y acta de cierre |
| [`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md) | Declaración formal de la versión QA Candidate v0.1 |
| [`README.md`](../README.md) | Descripción del backend: endpoints, configuración, fases completadas |

---

*Esta guía cubre el MVP XPAY — Fases 1 a 25. No modificar la lógica financiera del backend. Actualizar si se agregan nuevos flujos operacionales en fases posteriores.*
