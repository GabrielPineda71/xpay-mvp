# XPAY — Passport / Bre-B — M5-T8 — Retrieve detenido por guard local

**Ticket:** XPAY-605 · **Fecha:** 2026-09-30 (UTC) · **Entorno:** Sandbox
**Resultado:** **Retrieve NO ejecutado.** Bloqueo local previo a cualquier HTTP.

```
PASSPORT_API_CALLS=0   RETRIEVE_CALLS=0   RESOLVE_CALLS=0   PAYMENT_CREATE_CALLS=0
ADDITIONAL_FINANCIAL_OPERATION_EXECUTED=NO
```

La única llamada autorizada **no se consumió**. Queda disponible.

---

## 1. El pago de XPAY-604 SÍ está identificado sin ambigüedad

`XPAY_604_PAYMENT_IDENTIFIED=YES`

Artefacto privado `M5-PAYMENT-CREATE-2026-09-30T21-49-51Z.private.json`:

| Campo | Valor |
|---|---|
| `case_id` | `M5-PAYMENT-CREATE` |
| `captured_at_utc` | **2026-09-30T21:49:51Z** |
| `backend_commit_sha` | **`d2faff9b…`** — el commit creado en XPAY-604 |
| `status_at_creation` | `PROCESSING` |
| `payment_id` | presente, **no impreso** |

La coincidencia del `backend_commit_sha` con el commit de XPAY-604 lo ata de forma
inequívoca: ningún otro artefacto de pago lo referencia. El de las 21:25:10Z lleva
`285f5ac` (XPAY-601) y el del 26-sep lleva `d209d5b`.

## 2. Por qué no se pudo ejecutar el Retrieve

`M5PaymentArtifactReader` exige **exactamente un** artefacto
`M5-PAYMENT-CREATE-*.private.json` y devuelve `MultipleArtifactsFound` ante varios. En el
directorio privado conviven **tres**:

| Artefacto | Origen |
|---|---|
| `M5-PAYMENT-CREATE-2026-09-26T01-26-30Z` | pago histórico de septiembre |
| `M5-PAYMENT-CREATE-2026-09-30T21-25-10Z` | pago de XPAY-601 |
| `M5-PAYMENT-CREATE-2026-09-30T21-49-51Z` | **pago de XPAY-604, el que T8 necesita** |

`RetrieveBrebPaymentCertificationExecutor` resuelve el artefacto **antes** de construir
petición alguna y aborta con `LocalBlocked` — «No se realizó ninguna llamada a Passport».

Es el mismo patrón de acumulación que ya bloqueó XPAY-600 y que XPAY-601/604 resolvieron
archivando de forma reversible, pero **XPAY-605 no autoriza normalizar artefactos**: autoriza
un Retrieve y prohíbe expresamente modificar código. No se movió, renombró ni eliminó nada.

```
ARTIFACTS_DELETED=0   ARTIFACTS_ARCHIVED=0   ARTIFACT_NORMALIZATION_PERFORMED=NO
```

## 3. Estado de T8

```
HTTP_RESULT=NONE            PAYMENT_FOUND=UNKNOWN
API_PAYMENT_STATUS=NONE     API_FINAL_STATE_OBSERVED=NO
SETTLED_AT_PRESENT=NOT_APPLICABLE
API_WEBHOOK_SAME_PAYMENT_CONFIRMED=NO
API_WEBHOOK_STATE_CONSISTENT=INCONCLUSIVE
T8_STATUS=INCONCLUSIVE
```

No hay lectura de API que comparar. `INCONCLUSIVE`, no `FAIL`: nada falló — no llegó a
ejercitarse.

La evidencia webhook de XPAY-604 permanece **intacta y disponible**:
`WEBHOOK_REFERENCE_AVAILABLE=YES`, con los estados entrantes
`received → confirmed → settled` observados y procesados.

## 4. Nota sobre direcciones, para el ticket que cierre T8

El ciclo de XPAY-604 produjo, en los mismos lotes, eventos entrantes y salientes del **mismo**
pago: `payment.outbound.settled` junto a `payment.inbound.settled`, y
`payment.outbound.confirmed` junto a `payment.inbound.confirmed`. Es lo esperado, porque el
pagador (Cuenta B) y el receptor (A) son ambos entidades de XPAY en Sandbox.

Por tanto, al comparar, el Retrieve consultará **un** pago —el creado desde la cuenta
operativa— y su estado debe compararse con la progresión del ciclo, **no** declararse
inconsistente por el hecho de que existan ambas direcciones. El artefacto de pago se
identifica por `account_id_fingerprint`, que el propio ejecutor valida contra
`PASSPORT_ACCOUNT_ID` antes de llamar.

## 5. Acción mínima que desbloquea T8

Archivar de forma reversible —`mv`, cero borrados— los **dos** artefactos de pago anteriores
(`…21-25-10Z` y el del 26-sep) al subdirectorio `archive/`, que el lector no escanea por usar
`Directory.GetFiles` sin `SearchOption` (TopDirectoryOnly). Quedaría exactamente uno, el de
XPAY-604, y el Retrieve podría ejecutarse.

Requiere autorización explícita, como la tuvieron XPAY-601 y XPAY-604.

## 6. Lo que no se tocó

```
SECOND_RETRIEVE_EXECUTED=NO   MANUAL_WEBHOOK_POSTS=0
WALLET_MODIFIED=NO   LEDGER_MODIFIED=NO   BREB_RETIROS_MODIFIED=NO
AZURE_MODIFIED=NO    PASSPORT_DASHBOARD_MODIFIED=NO
PRODUCTION_MODIFIED=NO   MIDECISOR_MODIFIED=NO
STAGE_PERFORMED=NO   COMMIT_PERFORMED=NO   PUSH_PERFORMED=NO
```

Sin Resolve, sin pago, sin POST al webhook, sin replay, sin Test Event, sin cambios de código,
despliegue, `ENFORCE` ni configuración.

---

## Seguridad de este documento

Sin `payment_id`, `resolution_id`, identificadores completos de cuenta o cliente, llaves,
tokens, secretos ni PII. Sólo nombres de artefacto, marcas de tiempo, prefijos de commit y
estados.
