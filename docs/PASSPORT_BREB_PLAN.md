# XPAY MVP — Plan Integración Passport / Bre-B

**Versión:** 0.1.0-fase64-base  
**Fase actual:** 64 — Modelo base Passport/Bre-B Sandbox (sin pagos reales)  
**Siguiente fase:** 65 — Conexión real Passport sandbox, resolver llave y pago Bre-B  
**Ambiente:** QA/Demo únicamente. No producción. No dinero real.

---

## 1. Objetivo

Preparar XPAY para que usuarios/clientes y comercios puedan retirar saldo de su wallet hacia su cuenta bancaria usando su llave Bre-B, a través del proveedor **Passport Fintech**.

En Fase 64: modelo de datos, estados, endpoints de preparación y UI base.  
En Fase 65: llamadas reales a Passport sandbox, resolver llave y pagar.

---

## 2. Alcance Fase 64

| Componente | Estado |
|---|---|
| Migración SQL 010 (3 tablas + 2 cuentas ledger) | ✅ |
| Modelos C# (PassportBrebLlave, PassportBrebRetiro, PassportWebhookEvent) | ✅ |
| BrebService (validaciones, registro de llave, retiro simulado) | ✅ |
| BrebController (7 endpoints) | ✅ |
| Frontend usuario: tab "Retirar a mi banco" | ✅ |
| Frontend comercio: sección "Retirar saldo del comercio" | ✅ |
| Variables Azure documentadas | ✅ |
| Modelo contable documentado | ✅ |
| Pagos reales a Passport | ❌ Fase 65 |
| Resolver llave real en Passport | ❌ Fase 65 |
| Webhooks Passport procesados | ❌ Fase 65 |
| Movimiento ledger real al retirar | ❌ Fase 65 |

---

## 3. Reglas de negocio

1. Solo el propietario puede ver y usar su llave Bre-B. No se puede retirar a llaves de terceros.
2. La llave debe estar en estado **VALIDADA** antes de solicitar retiro.
3. No se puede ingresar una llave distinta al momento del retiro — se usa la llave activa registrada.
4. El retiro solo puede hacerse si el saldo disponible es suficiente.
5. Un solo retiro activo por wallet a la vez (Fase 65).
6. El valor de la llave nunca se guarda en claro — solo hash SHA-256 y máscara.
7. Cuentas bancarias nunca se guardan en claro.

---

## 4. Flujo Usuario (Persona/Cliente)

```
Usuario autenticado → Mi Wallet → Tab "Retirar a mi banco"
1. Ver estado de llave Bre-B (NO_REGISTRADA / PENDIENTE_VALIDACION / VALIDADA / RECHAZADA)
2. Registrar llave: seleccionar tipo (ID/PHONE/EMAIL/ALPHA/BCODE) + valor
   → Backend: hash + máscara guardados, estado = PENDIENTE_VALIDACION
3. Validar llave (Fase 64: admin simula; Fase 65: Passport resuelve)
   → Admin: POST /api/breb/admin/simular-validacion-llave { idBrebLlave, estado: "VALIDADA" }
4. Solicitar retiro: ingresar valor
   → Backend: verifica llave VALIDADA + saldo disponible
   → Crea retiro en estado CREADO
   → Fase 65: llama Passport, mueve ledger
5. Ver historial de retiros con referencia interna y estado
```

---

## 5. Flujo Comercio

```
qa.comercio1 → Mi Comercio → Sección "Retirar saldo del comercio"
1. Ver estado de llave Bre-B del comercio
2. Registrar llave (idComercio en body, requiere rol COMERCIO o ADMIN_XPAY)
3. Validar llave (admin)
4. Solicitar retiro simulado (idComercio en body)
5. Ver historial de retiros
```

---

## 6. Estados — Llave Bre-B

| Estado | Descripción |
|---|---|
| `NO_REGISTRADA` | Sin llave registrada (estado UI solamente) |
| `PENDIENTE_VALIDACION` | Llave registrada, esperando validación en Passport |
| `VALIDADA` | Passport confirmó que la llave pertenece al usuario |
| `RECHAZADA` | Passport rechazó la llave (no existe, no corresponde al titular) |
| `SUSPENDIDA` | Llave suspendida administrativamente |

---

## 7. Estados — Retiro Bre-B

| Estado | Descripción | ¿Final? |
|---|---|---|
| `CREADO` | Retiro registrado, validaciones pendientes | No |
| `PENDIENTE_VALIDACION_LLAVE` | Verificando llave en Passport | No |
| `LLAVE_VALIDADA` | Llave verificada, listo para enviar | No |
| `PENDIENTE_ENVIO_PASSPORT` | En cola para envío | No |
| `ENVIADO_PASSPORT` | Instrucción enviada a Passport/Bre-B | No |
| `CONFIRMADO` | Passport confirmó recepción (no es pago final) | No |
| `LIQUIDADO` | Banco destino confirmó acreditación | **Sí ✓** |
| `RECHAZADO` | Passport/banco rechazó — saldo debe liberarse | **Sí ✓** |
| `CANCELADO` | Cancelado por el usuario antes de envío | **Sí ✓** |
| `ERROR` | Error técnico — requiere revisión manual | **Sí ✓** |

---

## 8. Variables Azure App Settings

```
PASSPORT_BASE_URL         = https://api.passportfintech.com (sandbox URL a confirmar)
PASSPORT_API_KEY          = [Integration Key desde Passport dashboard]
PASSPORT_API_SECRET       = [API Secret desde Passport dashboard]
PASSPORT_WEBHOOK_SECRET   = [Webhook signing secret desde Passport dashboard]
```

**Reglas:**
- No poner valores en `appsettings.json`
- No subir al repositorio
- Configurar exclusivamente en Azure App Settings → xpay-api-qa
- Verificar presencia: `GET /api/passport/health-config` (admin only)

---

## 9. Endpoints Fase 64

| Método | Endpoint | Auth | Descripción |
|---|---|---|---|
| GET | `/api/passport/health-config` | ADMIN_XPAY | Confirma presencia de variables, nunca valores |
| GET | `/api/breb/mi-llave` | [Authorize] | Llave activa del usuario (USUARIO context) |
| POST | `/api/breb/mi-llave` | [Authorize] | Registra/reemplaza llave propia |
| GET | `/api/breb/mi-llave/comercio?idComercio=N` | ADMIN/COMERCIO | Llave del comercio |
| POST | `/api/breb/mi-llave/comercio` | ADMIN/COMERCIO | Registra llave del comercio |
| POST | `/api/breb/admin/simular-validacion-llave` | ADMIN_XPAY | QA: marca VALIDADA o RECHAZADA |
| GET | `/api/breb/mis-retiros` | [Authorize] | Retiros propios (USUARIO) |
| GET | `/api/breb/mis-retiros/comercio?idComercio=N` | ADMIN/COMERCIO | Retiros del comercio |
| POST | `/api/breb/retiros/simular` | [Authorize] | Crea retiro simulado (CREADO, sin ledger) |

---

## 10. Tipos de llave Bre-B soportados

| Tipo | Descripción | Validación básica |
|---|---|---|
| `ID` | Cédula / Número de identificación | 5-20 dígitos |
| `PHONE` | Número de celular | 7-15 chars |
| `EMAIL` | Correo electrónico | Contiene @ y . |
| `ALPHA` | Alias alfanumérico | 3-30 chars alfanum/guión |
| `BCODE` | Código Bre-B | 8-50 chars alfanum/guión |

---

## 11. Modelo contable

### Solicitud de retiro (al crear)
```
Retiro USUARIO:
  DR 210101  Obligación Wallet Usuarios          [valor]
  CR 210204  Retiros Bre-B Pendientes de Pago   [valor]

Retiro COMERCIO:
  DR 210202  Obligación Wallet Comercios         [valor]
  CR 210204  Retiros Bre-B Pendientes de Pago   [valor]
```

### Cuando Passport/Bre-B liquida (LIQUIDADO)
```
  DR 210204  Retiros Bre-B Pendientes de Pago   [valor]
  CR 110102  Banco Coopcentral XPAY             [valor]
```

### Cuando Passport/Bre-B rechaza (RECHAZADO)
```
  DR 210204  Retiros Bre-B Pendientes de Pago   [valor]
  CR 210101  o 210202 (según tipo_sujeto)       [valor]
```

**Nota Fase 64:** Los asientos del ledger NO se implementan aún. El retiro simulado queda en estado `CREADO` sin mover saldo ni contabilidad. Esto se implementa en Fase 65.

---

## 12. Dependencia cuenta Coopcentral

| Concepto | Estado |
|---|---|
| Cuenta 110102 Banco Coopcentral XPAY | Creada en ledger_cuentas (modelo) |
| Cuenta bancaria real en Coopcentral | **PENDIENTE** — en proceso de apertura |
| Operaciones reales en Coopcentral | Bloqueadas hasta apertura y habilitación |
| Integración API Coopcentral/Passport | Fase 65+ |

La cuenta 110102 existe en el modelo contable de XPAY para documentar los asientos futuros, pero no representa una cuenta bancaria real hasta que Coopcentral la aperture.

---

## 13. Tablas nuevas (migración 010)

### `passport_breb_llaves`
- Una llave activa por wallet (índice filtrado `es_activa = 1`)
- `key_value_hash`: SHA-256 hex lowercase del valor
- `key_value_masked`: versión enmascarada para mostrar
- `key_value_encrypted`: NULL en Fase 64 (pendiente cifrado at-rest en Fase 65)
- FK a `wallets`

### `passport_breb_retiros`
- `referencia_interna`: GUID único por retiro
- `idempotency_key`: generado por XPAY, único
- `id_transaccion_ledger`: NULL en Fase 64, se llena en Fase 65
- FK a `wallets` y `passport_breb_llaves`

### `passport_webhook_events`
- Log de todos los eventos Passport recibidos
- `payload_hash`: SHA-256 del raw body para integridad
- `payload_sanitized_json`: sin cuentas bancarias en claro
- NO se guarda payload completo con datos sensibles

---

## 14. Seguridad

- Un usuario solo puede ver y operar su propia llave y retiros.
- Un comercio solo puede ver y operar la llave y retiros de su propio comercio.
- Admin puede consultar usando idComercio en parámetros.
- No se guarda key_value en claro nunca.
- No se guarda número de cuenta bancaria en claro.
- PASSPORT_API_KEY, PASSPORT_API_SECRET y PASSPORT_WEBHOOK_SECRET nunca se loguean.
- El endpoint `/api/passport/health-config` solo confirma presencia (`true/false`), nunca el valor.

---

## 15. Riesgos

| Riesgo | Impacto | Mitigación |
|---|---|---|
| Cuenta Coopcentral no abierta aún | Bloquea dispersiones reales | No operar con dinero real hasta apertura |
| Passport sandbox credentials no configuradas | Endpoints 503 | health-config indica estado de configuración |
| Llave Bre-B no pertenece al titular | Retiro a cuenta incorrecta | Fase 65: resolver+verificar via Passport antes de pagar |
| Doble gasto (retiro duplicado) | Pérdida financiera | idempotency_key único + validación de saldo en tx |

---

## 16. Pendientes para Fase 65

1. **Resolver llave en Passport**: `POST /v1/keys/resolve` con key_type + key_value → obtener `owner_name`, `account_number` (masked), `participant_name`.
2. **Verificar que la llave pertenece al usuario**: comparar identificación del propietario en Passport con la del usuario registrado en XPAY.
3. **Iniciar pago Bre-B en Passport**: `POST /v1/payments` con payment details → obtener `passport_payment_id`.
4. **Mover saldo al crear retiro**: deducir `saldo_disponible` + incrementar `saldo_retenido` transaccionalmente.
5. **Asientos ledger**: DR 210101/210202 / CR 210204 al solicitar.
6. **Webhook Passport**: recibir `confirmed`, `settled`, `rejected` → actualizar estado retiro + ledger + wallet.
7. **Cifrado at-rest** de `key_value_encrypted`.
8. **Cancelación** de retiros en estado CREADO antes de envío.
9. **Límites** de monto por retiro y por día.
10. **KYC gate**: verificar que el usuario tenga KYC APROBADO antes de permitir retiro.
