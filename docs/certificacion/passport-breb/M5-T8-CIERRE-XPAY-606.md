# XPAY — Passport / Bre-B — M5-T8 — CIERRE

**Ticket:** XPAY-606 · **Fecha:** 2026-09-30 (UTC) · **Entorno:** Sandbox

```
T6_STATUS=PASS   T7_STATUS=PASS   T8_STATUS=PASS
API_WEBHOOK_SAME_PAYMENT_CONFIRMED=YES
API_WEBHOOK_STATE_CONSISTENT=YES
```

Un solo Retrieve. Cero pagos nuevos, cero Resolve, cero reintentos.

---

## 1. Disciplina de redacción corregida

`REDACTION_FILTER_CORRECTED=YES` · `SENSITIVE_VALUES_REPRINTED=NO`

El filtro usado para inspeccionar artefactos privados en este ticket enmascara, por nombre de
campo: `key_value`, `key_id`, `payment_id`, `resolution_id`, `account*`, `customer*`,
`identif*`, `owner`, `name`, `email`, `phone`, `address`, `authoriz*`, `token`, `secret` y
`key`. Sólo se dejan pasar campos explícitamente inocuos (`case_id`, `captured_at_utc`,
`backend_commit_sha`, `status*`, `operation`, `outcome`, `observed_http_status`).

El `key_value`/`key_id` del fixture negativo M5-T5-D que se imprimió por error en XPAY-605
**no se reprodujo aquí**, no se copió a ninguna evidencia y no motivó cambio de código. Queda
como incidente local de redacción, contenido: el valor pertenece a una llave de prueba
deliberadamente suspendida desde el 26-sep, almacenada en un directorio privado fuera del
repositorio.

## 2. Archivado reversible

`ARTIFACT_NORMALIZATION_PERFORMED=YES` · `ARTIFACTS_ARCHIVED=2` · `ARTIFACTS_DELETED=0`

Confirmados por metadata no sensible los tres artefactos de pago y archivados **sólo los dos
anteriores**, por `mv` a `archive/` —que el lector no escana, por usar `Directory.GetFiles`
sin `SearchOption`—:

| Artefacto | `backend_commit_sha` | Acción |
|---|---|---|
| `…2026-09-26T01-26-30Z` | `d209d5b…` | archivado |
| `…2026-09-30T21-25-10Z` | `285f5ac…` (XPAY-601) | archivado |
| `…2026-09-30T21-49-51Z` | **`d2faff9b…` (XPAY-604)** | **conservado activo** |

Sin borrar, sin editar contenido, sin sobrescribir, sin tocar guards ni el reader.

```
ACTIVE_PAYMENT_ARTIFACT_COUNT=1
XPAY_604_ONLY_ACTIVE_ARTIFACT=YES
XPAY_604_PAYMENT_IDENTIFIED=YES
```

El `backend_commit_sha` es el vínculo inequívoco: sólo el artefacto de XPAY-604 referencia el
commit `d2faff9b`, creado en ese mismo ticket.

## 3. Retrieve único

```
PASSPORT_API_CALLS=1   RETRIEVE_CALLS=1   RESOLVE_CALLS=0   PAYMENT_CREATE_CALLS=0
operation   = GET /v1/payments/{payment_id}
executed_at = 2026-09-30T22:05:50Z
result      = PASS
evidencia   = M5-PAYMENT-RETRIEVE/evidence-2026-09-30T22-05-50Z.json
```

`request_executed = True` · `request_accepted = True` · `error_present = False` ·
`payment_retrieve_calls = 1` · `retry_count = 0` · `payment_create_calls = 0`

`PAYMENT_FOUND=YES` · `SECOND_RETRIEVE_EXECUTED=NO`

## 4. Estado devuelto por la API

```
API_PAYMENT_STATUS=SETTLED
API_FINAL_STATE_OBSERVED=YES
SETTLED_AT_PRESENT=NO
```

`SETTLED` es el estado **final** del ciclo. `amount_present = True`, `currency = COP`.

Sobre `settled_at`: el constructor de evidencia de este caso **no lo proyecta**, así que no es
observable desde el artefacto. No se afirma su presencia ni su ausencia en la respuesta de
Passport — sólo que la evidencia sanitizada no lo recoge. No se repitió la consulta para
obtenerlo.

## 5. Consistencia API ↔ webhook

### Es el mismo pago

El `payment_id_fingerprint` de la evidencia de **creación** (XPAY-604) y el de la **consulta**
coinciden — verificado por comparación booleana, sin imprimir ningún valor. Ambas evidencias
llevan además el mismo `backend_commit_sha` (`d2faff9`).

`API_WEBHOOK_SAME_PAYMENT_CONFIRMED=YES`

### Los estados encajan

| Fuente | Progresión observada |
|---|---|
| Creación (XPAY-604) | `PROCESSING` |
| Webhook (XPAY-604) | `payment.inbound.received` → `payment.inbound.confirmed` → `payment.inbound.settled` |
| **API (este Retrieve)** | **`SETTLED`** |

La API devuelve exactamente el estado terminal que el webhook había anunciado. La progresión
es coherente de principio a fin: el pago nace `PROCESSING`, el webhook narra las tres
transiciones entrantes y la consulta confirma `SETTLED`.

`API_WEBHOOK_STATE_CONSISTENT=YES`

### Sobre las dos direcciones — no es inconsistencia

Los lotes de XPAY-604 trajeron también eventos `payment.outbound.*` del mismo ciclo,
clasificados `NotInbound`, registrados y financieramente inertes. No constituye
inconsistencia: el pagador (Cliente/Cuenta B) y el receptor (A) son **ambos** entidades de
XPAY en Sandbox, así que un único pago B→A emite eventos por las dos direcciones.

El Retrieve consulta **un** pago —el creado desde la cuenta operativa—, y el propio ejecutor
valida el `account_id_fingerprint` del artefacto contra `PASSPORT_ACCOUNT_ID` antes de llamar.
La comparación se hace, por tanto, contra la progresión del ciclo, no contra una dirección
concreta.

## 6. Lo que no se tocó

```
ADDITIONAL_FINANCIAL_OPERATION_EXECUTED=NO   MANUAL_WEBHOOK_POSTS=0
WALLET_MODIFIED=NO   LEDGER_MODIFIED=NO   BREB_RETIROS_MODIFIED=NO
AZURE_MODIFIED=NO    PASSPORT_DASHBOARD_MODIFIED=NO
PRODUCTION_MODIFIED=NO   MIDECISOR_MODIFIED=NO
STAGE_PERFORMED=NO   COMMIT_PERFORMED=NO   PUSH_PERFORMED=NO
```

Sin Resolve, sin pago, sin segundo Retrieve, sin POST al webhook, sin replay, sin Test Event,
sin cambios de código, despliegue, `ENFORCE`, migración ni escritura manual en base de datos.

## 7. Estado de M5 — cierre parcial, no total

| Caso | Estado |
|---|---|
| T1 | READY |
| T2 · T3 · T4 | evidencia disponible (ya enviada) |
| T5-A · T5-B · T5-D · T5-F | evidencia disponible (ya enviada) |
| **T5-C · T5-E** | **PENDIENTES** |
| **T6** | **PASS** |
| **T7** | **PASS** |
| **T8** | **PASS** |

```
M5_T1_T8_TECHNICAL_CLOSURE=NO
READY_FOR_M5_FINAL_EVIDENCE_PACKAGE=NO
```

El frente T6/T7/T8 **queda cerrado técnicamente**, y con ello el bloque que llevaba abierto
desde XPAY-580. Pero **T5-C y T5-E siguen pendientes**, así que no corresponde declarar el
cierre técnico de T1–T8 ni armar el paquete final de evidencia. Sería inexacto presentarlo
como M5 completo.

---

## Seguridad de este documento

Sin `payment_id`, `resolution_id`, llaves, BCODE, identificadores completos de cuenta o
cliente, tokens, secretos, respuestas crudas ni PII. Todas las comparaciones de
identificadores se hicieron de forma **booleana**.
