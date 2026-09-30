# XPAY — Passport / Bre-B — M5-T6 / M5-T7 — CIERRE

**Ticket:** XPAY-604 · **Fecha:** 2026-09-30 (UTC) · **Entorno:** Sandbox / QA

```
T6_STATUS=PASS
T7_STATUS=PASS
READY_FOR_T8=YES
```

Una sola resolución, un solo pago, tres entregas reales de webhook autenticadas, parseadas y
procesadas. Cero segundo pago, cero POST manual, cero replay.

---

## 1. Trazabilidad Git — `COMMIT_SHA=d2faff9b0cc2b82f7cfde0c28b26349fa16fe3f5`

Commit local exclusivo de XPAY-602/603: **8 archivos, 983 inserciones**. Tres archivos de
código (`PassportWebhookPayload.cs`, `PassportWebhookController.cs`,
`PassportWebhookService.cs`), tres de pruebas y las dos evidencias técnicas.

`UNRELATED_CHANGES_COMMITTED=NO`. Se excluyeron deliberadamente los cambios preexistentes
ajenos (`docs/WALLET_CAJA_CUADRE_FASE_70.4_DISENO.md` y `tools/Xpay.PassportSandboxHarness/*`)
y, por estar fuera del alcance declarado, también las evidencias de XPAY-599/600/601 y la
actualización de `M5-T6-T7-WEBHOOK-DEPLOY-QA.md` — **siguen sin versionar** y convendría un
commit sólo de documentación más adelante.

Pruebas antes del commit: focalizadas **127/127**, backend completo **1045/1045**, 0 omitidas.
`STAGED_SECRET_PII_SCAN=PASS`: ningún secreto, ningún correo, y el único UUID del diff es el
`deployment id` de Azure — traza de auditoría deliberada, no un identificador de pago.

**El código desplegado en QA está representado por este commit**: los tres archivos
funcionales son idénticos entre `HEAD` y el árbol de trabajo. `PUSH_PERFORMED=NO`.

---

## 2. Preparación de artefactos privados

`ARTIFACT_NORMALIZATION_PERFORMED=YES` · `ARTIFACTS_ARCHIVED=2` · `ARTIFACTS_DELETED=0`

El guard ya permitía un Resolve nuevo (el artefacto de XPAY-601 estaba consumido **y**
vencido). Pero se archivó igualmente su par —artefacto base más marcador de consumo— porque
de lo contrario, **tras** el nuevo Resolve habrían quedado dos artefactos activos y
`configure-m5-payment-resolution` habría fallado con `MultipleArtifactsFound`: exactamente la
trampa que bloqueó XPAY-600. Con `mv` a `archive/`, reversible, sin borrar ni editar nada.

También se respaldó `~/.passport-sandbox.env` y se eliminó **exclusivamente** la entrada
obsoleta `PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID` de XPAY-601, para que el configurador
pudiera transferir la nueva por sí mismo. Verificado por diferencia de nombres: **una sola
línea** cambió. No se escribió ningún valor a mano.

---

## 3. Resolve único

```
NEW_RESOLVE_CALLS=1        RESOLVE_RESULT=PASS
resolved_at = 2026-09-30T21:49:26Z
evidencia   = M5-T2-RESOLVE/evidence-2026-09-30T21-49-26Z.json
```

## 4. Configuración de la resolución

```
RESOLUTION_CONFIGURATION_RESULT=TRANSFERRED
RESOLUTION_CONFIGURATION_LOCAL_ONLY=YES   (cero HTTP)
```

El `resolution_id` se transfirió automáticamente desde el artefacto privado, sin imprimirse.

Verificación previa al pago, booleana y contra el **artefacto autoritativo** —porque el
harness no persiste `EXPIRES_AT` en el entorno, gap conocido de XPAY-529 §16 que este ticket
no corrigió—:

| Comprobación | Resultado |
|---|---|
| Artefactos activos | 1 (esperado 1) |
| Es el Resolve de XPAY-604 | SÍ |
| Corresponde a KEY_A | SÍ |
| `RESOLUTION_ID` del entorno == artefacto | SÍ |
| Vigente | **SÍ**, restante 1778 s |

`RESOLUTION_VALID_AT_PAYMENT_TIME=YES`

## 5. Payment único

```
PAYMENT_CREATE_CALLS=1     PAYMENT_CREATED=YES     PAYMENT_RESULT=PASS
executed_at = 2026-09-30T21:49:51Z
evidencia   = M5-PAYMENT-CREATE/evidence-2026-09-30T21-49-51Z.json
```

`SECOND_RESOLVE_EXECUTED=NO` · `SECOND_PAYMENT_CREATED=NO`

---

## 6. T6 — Recepción real del webhook · **PASS**

Passport entregó **10 segundos** después del pago. Observación pasiva de los logs del App
Service. Tres entregas distintas, sin ningún POST manual:

| # | Hora (UTC) | Forma | Eventos | Clases | HTTP |
|---|---|---|---|---|---|
| 1 | 21:50:01 | `Batch` | 1 | `Received` | **200** |
| 2 | 21:50:01 | `Batch` | 2 | `NotInbound`, `Settled` | **200** |
| 3 | 21:50:04 | `Batch` | 2 | `NotInbound`, `Confirmed` | **200** |

```
WEBHOOK_DELIVERY_OBSERVED=YES      WEBHOOK_HTTP_RESULT=200 (3/3)
```

## 7. T7 — Validación real del webhook · **PASS**

```
SIGNATURE_MODE=ENFORCE             SIGNATURE_VALID=YES
PAYLOAD_PARSE_RESULT=PASS          PAYLOAD_UNPARSEABLE_OBSERVED=NO
WEBHOOK_PROCESSED=YES
INBOUND_EVENTS_OBSERVED=payment.inbound.received, payment.inbound.confirmed, payment.inbound.settled
```

Las tres entregas: `signature check: outcome=Valid valid=True enforcing=True`, `sigLen=64`
(hex), `tsLen=10` (epoch en segundos). Cero 401, cero 400, cero 500, **cero
`PAYLOAD_UNPARSEABLE`**.

Persistencia por evento, cinco filas en `passport_webhook_events`:

| `idEvent` | Clase | Tratamiento |
|---|---|---|
| 1 | `Received` | **procesado** |
| 2 | `NotInbound` | registrado, **no procesado** |
| 3 | `Settled` | **procesado** |
| 4 | `NotInbound` | registrado, **no procesado** |
| 5 | `Confirmed` | **procesado** |

`duplicados idempotentes = 0` (no hubo reintentos: las tres entregas fueron 200 a la primera).

Se observó el **flujo entrante completo**: `received → confirmed → settled`, los tres
procesados como eventos distintos gracias a la clave `(event_type, passport_payment_id)`.
Con una clave por `payment_id` solo, dos de los tres se habrían descartado como falsos
duplicados.

### El riesgo residual de XPAY-602 queda resuelto

`shape = Batch` en **3 de 3** entregas y `shape = SingleEvent` en **0**. La raíz es un array,
como se había deducido — y con un dato añadido que la documentación no anticipaba: **incluso
un evento único llega envuelto en un array** (entrega #1: `Batch` con `events = 1`). El
soporte de objeto en la raíz se conserva por compatibilidad, pero Passport Sandbox no lo usa.

### Los eventos salientes funcionaron como se diseñó

Cada lote mixto trajo el saliente y el entrante del mismo ciclo. El saliente se clasificó
`NotInbound`, se registró para auditoría y **no se procesó**, devolviendo 200 para que
Passport no reintente. Confirma la decisión de XPAY-598 de no “corregir” su suscripción.

---

## 8. Lo que no se tocó

```
MANUAL_WEBHOOK_POSTS=0     WALLET_MODIFIED=NO     LEDGER_MODIFIED=NO
BREB_RETIROS_MODIFIED=NO   AZURE_MODIFIED=NO      PASSPORT_DASHBOARD_MODIFIED=NO
PRODUCTION_MODIFIED=NO     MIDECISOR_MODIFIED=NO  PUSH_PERFORMED=NO
```

Sin Retrieve HTTP, sin segunda resolución, sin segundo pago, sin llaves ni QR, sin migración,
sin cambios en `ENFORCE`, sin despliegue adicional. Ningún elemento de los lotes se
correlacionó con un retiro: `id_breb_retiro` nulo en las cinco filas, de modo que no existe
vía por la que se hubiera podido tocar Wallet o Ledger.

---

## 9. Estado de M5

| Caso | Estado |
|---|---|
| T1 | READY |
| T2 · T3 · T4 | evidencia disponible (enviada) |
| T5-A/B/D/F | evidencia disponible (enviada) |
| T5-C · T5-E | pendientes |
| **T6** | **PASS** — entrega real recibida |
| **T7** | **PASS** — entrega real autenticada, parseada y procesada |
| T8 | pendiente · `READY_FOR_T8=YES` |

Para T8 sigue vigente la recomendación de XPAY-580 §8: un `GET` **fresco** del pago, ejecutado
**después** de estas entregas, que evidencie que la API y el webhook coinciden sobre el mismo
pago. Requiere autorización propia: es una llamada HTTP a Passport.

---

## Seguridad de este documento

Sin secretos, tokens, firmas, cuerpos crudos, `payment_id`, `resolution_id`, llaves, cuentas
ni PII. Los identificadores citados son `idEvent` locales, nombres de evento, códigos HTTP y
marcas de tiempo.
