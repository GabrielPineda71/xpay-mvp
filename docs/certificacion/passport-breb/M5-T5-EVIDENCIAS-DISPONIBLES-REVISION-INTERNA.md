# XPAY — Passport/Bre-B Certification — M5-T5 (Casos de error)

## ⚠️ BORRADOR PARA REVISIÓN INTERNA — NO ENVIAR A PASSPORT TODAVÍA

Este documento consolida, **100% offline**, la evidencia YA existente de
los cuatro subcasos de M5-T5 actualmente `TECNICAMENTE_DOCUMENTADO`. No
se ejecutó ningún caso nuevo para producirlo (XPAY-578) — cero llamadas a
Passport, cero mutaciones, cero cambios de env/código/git.

**M5-T5 NO está completo. M5-T5 NO está aceptado por Passport.**
`TECNICAMENTE_DOCUMENTADO` significa únicamente: evidencia real de
Sandbox capturada y sanitizada, con `review_status=PENDING_PASSPORT_REVIEW`
en los cuatro casos — la revisión y aceptación final corresponden
exclusivamente a Passport.

## 1. Objetivo y alcance

Consolidar para revisión del director la evidencia de:

- **A** — Missing amount
- **B** — Missing account
- **D** — Suspended key
- **F** — Invalid resolution_id

Y dejar registrado, sin ejecutar nada, el estado de los dos subcasos
restantes:

- **C** — Amount > límite — `BLOCKED`
- **E** — Deleted key — `BLOCKED`

## 2. Requisito oficial M5-T5

Transcripción validada por el director (XPAY-568R), ya incorporada en
[`M5-T5-DESIGN-PREFLIGHT.md`](M5-T5-DESIGN-PREFLIGHT.md):

> M5-T5 — Casos de error (campos faltantes, límites excedidos, claves
> inválidas). Evidencia requerida: Registros de respuestas de error.
>
> Detalle: (1) monto faltante, (2) cuenta faltante, (3) transacción
> superior a $12.000.000 COP, (4) envío a clave Bre-B suspendida,
> (5) envío a clave Bre-B eliminada, (6) uso de un ID de resolución de
> clave incorrecto.

No se ha inventado ningún requisito nuevo para este documento.

## 3. Estado general

| Subcaso | Estado | Alcanzó Passport real |
|---|---|---|
| A — Missing amount | `TECNICAMENTE_DOCUMENTADO` | No (validación local) |
| B — Missing account | `TECNICAMENTE_DOCUMENTADO` | No (validación local) |
| D — Suspended key | `TECNICAMENTE_DOCUMENTADO` | Sí (1 Resolve real) |
| F — Invalid resolution_id | `TECNICAMENTE_DOCUMENTADO` | Sí (1 Payment real) |
| C — Amount > límite | `BLOCKED` | No |
| E — Deleted key | `BLOCKED` | No |

`M5_T5_OVERALL_STATUS=IN_PROGRESS`.

## 4. Subcaso A — Missing amount

- **Evidencia**: [`M5-T5-MISSING-AMOUNT/evidence-2026-09-26T02-11-57Z.json`](M5-T5-MISSING-AMOUNT/evidence-2026-09-26T02-11-57Z.json)
- **Origen del resultado**: `RESULT_SOURCE=LOCAL_CLIENT_VALIDATION` —
  `request_sanitized.validation_layer="LOCAL"`,
  `response_sanitized.passport_http_attempted=false`,
  `response_sanitized.transport_result="NOT_ATTEMPTED"`.
- **Passport respondió**: NO (`PASSPORT_RESPONSE_RECEIVED=NO`).
- **Presupuesto real**: `HTTP_REAL_CALLS=0`,
  `OAUTH_REAL_TOKEN_REQUESTS=0`, `PAYMENT_REAL_CALLS=0`.
- **Qué demuestra**: que el cliente productivo
  (`PassportPaymentClient.CreateBrebPaymentAsync`) exige `amount` y
  rechaza la solicitud ANTES de cualquier transporte —
  `request_sanitized.missing_field="amount"`.
- **`A_EVIDENCE_STATUS=SUFFICIENT_FOR_REVIEW`** — el JSON es
  autosuficiente, correctamente rotulado como validación local, y no se
  presenta como si Passport hubiera respondido.

## 5. Subcaso B — Missing account

- **Evidencia**: [`M5-T5-MISSING-ACCOUNT/evidence-2026-09-26T02-19-25Z.json`](M5-T5-MISSING-ACCOUNT/evidence-2026-09-26T02-19-25Z.json)
- **Origen del resultado**: `RESULT_SOURCE=LOCAL_CLIENT_VALIDATION` —
  mismos campos que A (`validation_layer="LOCAL"`,
  `passport_http_attempted=false`, `transport_result="NOT_ATTEMPTED"`).
- **Passport respondió**: NO.
- **Presupuesto real**: `HTTP_REAL_CALLS=0`,
  `OAUTH_REAL_TOKEN_REQUESTS=0`, `PAYMENT_REAL_CALLS=0`.
- **Qué demuestra**: que el cliente exige `account_id` — el JSON usa el
  marcador explícito `"ABSENT"` (nunca un fingerprint de cadena vacía)
  para dejar constancia de que el campo estaba realmente ausente, no
  redactado.
- **`B_EVIDENCE_STATUS=SUFFICIENT_FOR_REVIEW`**.

## 6. Subcaso D — Suspended key

- **Evidencia FINAL** (no confundir con la trazabilidad preparatoria):
  [`M5-T5-SUSPENDED-KEY/evidence-2026-09-26T03-47-22Z.json`](M5-T5-SUSPENDED-KEY/evidence-2026-09-26T03-47-22Z.json)
- **Cadena que produjo este resultado** (fixture nuevo y desechable,
  nunca el histórico contaminado de M4-T3-A): Create Key real (XPAY-575)
  → Suspend Key real, `SUSPENDED` confirmado (XPAY-576) → Resolve real
  sobre esa misma key ya suspendida (XPAY-577, este JSON).
- **Origen del resultado**: `RESULT_SOURCE=PROVIDER_RESPONSE` —
  `passport_response_received=true`.
- **HTTP status**: `400`.
- **Provider error code**: `generic_error`.
- **Provider message** (sanitizado, ya acotado por el propio Passport):
  menciona explícitamente que la key fue suspendida por el cliente
  (código corto `U805`).
- **`resolution_returned=false`** — no se generó ninguna resolución.
- **`current_suspension_confirmed=true`** — determinado literalmente
  (el mensaje de Passport nombra la suspensión explícitamente), nunca
  inferido.
- **Presupuesto real**: `RESOLVE_REAL_CALLS=1`, `PAYMENT_REAL_CALLS=0`
  (estructuralmente imposible en este executor — nunca conoce el cliente
  de Payment).
- **`D_EVIDENCE_STATUS=SUFFICIENT_FOR_REVIEW`** — es el resultado más
  inequívoco de los cuatro: Passport confirma la causa por su propio
  texto, sin necesidad de interpretación adicional.

## 7. Subcaso F — Invalid resolution_id

- **Evidencia**: [`M5-T5-INVALID-RESOLUTION/evidence-2026-09-26T02-38-49Z.json`](M5-T5-INVALID-RESOLUTION/evidence-2026-09-26T02-38-49Z.json)
- **Origen del resultado**: `RESULT_SOURCE=PROVIDER_RESPONSE` —
  `passport_response_received=true`.
- **HTTP status**: `400`.
- **Provider error code**: `invalid_parameter_value`.
- **Provider message**: *"UUID version 4 expected"*.
- **Resolution/Payment aceptado**: NO —
  `response_sanitized.request_accepted=false`,
  `payment_id_present=false`.
- **Presupuesto real**: `PAYMENT_REAL_CALLS=1` (el único POST de este
  subcaso — el `resolution_id` usado fue un UUID sintético, nunca real),
  `RESOLVE_REAL_CALLS=0`, `RETRIEVE_REAL_CALLS=0`.
- **Nota honesta**: el rechazo fue por **formato** de UUID, no
  necesariamente por "resolution_id inexistente" en un sentido semántico
  más profundo — no se afirma una causa más específica de la que
  Passport realmente devolvió.
- **`F_EVIDENCE_STATUS=SUFFICIENT_FOR_REVIEW`** — satisface literalmente
  el requisito ("uso de un ID de resolución de clave incorrecto"), con
  la salvedad anotada arriba.

## 8. Subcasos pendientes — C y E

| Subcaso | Estado | Razón del bloqueo |
|---|---|---|
| C — Amount > límite | `BLOCKED` | Discrepancia sin resolver entre el monto indicado en el Anexo y otras referencias, y ausencia de un guard local que garantice que una prueba de monto alto no produzca un movimiento real aceptado por error. No ejecutado, no diseñado como llamada real en este ticket. |
| E — Deleted key | `BLOCKED` | El recurso histórico (`PASSPORT_TEST_NEW_KEY_ID`/`_VALUE`) no conserva de forma confiable el `key_value` necesario para Resolve — ver XPAY-573/574. No se usan esas variables contaminadas. No se creó fixture nuevo en este ticket (a diferencia de D, que sí lo tiene ya resuelto). |

Ninguno de los dos se presenta como ejecutado.

## 9. Distinción crítica — validación local vs. respuesta del proveedor

| Grupo | Subcasos | Significa |
|---|---|---|
| `LOCAL_CLIENT_VALIDATION` | A, B | El cliente productivo de XPAY rechazó la solicitud antes de cualquier transporte. Passport nunca vio la solicitud. Cero llamadas reales. |
| `PROVIDER_RESPONSE` | D, F | Passport recibió la solicitud real y respondió con un rechazo (D) o error de validación (F). Una llamada real cada uno. |

Esta distinción queda deliberadamente visible para que el director pueda
decidir si Passport exige, para certificar los campos obligatorios
faltantes, una respuesta HTTP real del proveedor, o si la validación
local (que demuestra que XPAY nunca podría enviar una solicitud
incompleta) es evidencia aceptable. Esa decisión no se toma en este
documento.

## 10. Matriz requisito → ejecución → resultado → evidencia

| Subcaso | Requisito oficial | Estado | Fuente del resultado | HTTP real | Resultado | Evidence JSON | SHA-256 | Apto para revisión |
|---|---|---|---|---|---|---|---|---|
| A | Monto faltante | `TECNICAMENTE_DOCUMENTADO` | `LOCAL_CLIENT_VALIDATION` | 0 | Rechazo local (`amount` ausente) | `M5-T5-MISSING-AMOUNT/evidence-2026-09-26T02-11-57Z.json` | `bd6f21f2b7a1d5b158116531cbcbb89bf03a29479da238bbd361f3a132794d7c` | YES |
| B | Cuenta faltante | `TECNICAMENTE_DOCUMENTADO` | `LOCAL_CLIENT_VALIDATION` | 0 | Rechazo local (`account_id` ausente) | `M5-T5-MISSING-ACCOUNT/evidence-2026-09-26T02-19-25Z.json` | `8883355389ba3cdbefe3859ccde76178f06710baebec4c3eeb4bf4ada8e442b3` | YES |
| D | Clave suspendida | `TECNICAMENTE_DOCUMENTADO` | `PROVIDER_RESPONSE` | 1 (Resolve) | HTTP 400, suspensión confirmada explícitamente (`U805`) | `M5-T5-SUSPENDED-KEY/evidence-2026-09-26T03-47-22Z.json` | `29757365146619b86a5d571efb5d3d4b0c8bb260ece2f55c9f1083e5275ca745` | YES |
| F | resolution_id incorrecto | `TECNICAMENTE_DOCUMENTADO` | `PROVIDER_RESPONSE` | 1 (Payment) | HTTP 400, `invalid_parameter_value` (formato UUID) | `M5-T5-INVALID-RESOLUTION/evidence-2026-09-26T02-38-49Z.json` | `1ccc17a1e212d4501cdd3774b000064899373329c984ac575cca1891875c8517` | YES |

| Subcaso | Requisito oficial | Estado |
|---|---|---|
| C | Monto > $12.000.000 COP | `BLOCKED` |
| E | Clave eliminada | `BLOCKED` |

Ningún estado aquí se confunde con "aceptado por Passport" — todos los
`review_status` de origen permanecen `PENDING_PASSPORT_REVIEW`.

## 11. Archivos que integrarían el paquete para Gustavo (inventario únicamente)

**No se copió ningún archivo. No se creó ningún ZIP. No se envió nada.**
Este es solo el inventario candidato:

1. `M5-T5-EVIDENCIAS-DISPONIBLES-REVISION-INTERNA.md` (este documento)
2. `M5-T5-MISSING-AMOUNT/evidence-2026-09-26T02-11-57Z.json`
3. `M5-T5-MISSING-ACCOUNT/evidence-2026-09-26T02-19-25Z.json`
4. `M5-T5-SUSPENDED-KEY/evidence-2026-09-26T03-47-22Z.json`
5. `M5-T5-INVALID-RESOLUTION/evidence-2026-09-26T02-38-49Z.json`

**Explícitamente EXCLUIDOS del paquete principal de certificación**
(trazabilidad interna de la preparación del fixture de D, no evidencia
final del comportamiento suspendido):

- `M5-T5-D-KEY-PREP-CREATE/evidence-2026-09-26T03-11-54Z.json` (XPAY-575)
- `M5-T5-D-KEY-PREP-SUSPEND/evidence-2026-09-26T03-22-05Z.json` (XPAY-576)

Pueden mencionarse como contexto de cómo se preparó D, si resulta útil,
pero no deben presentarse como evidencia final del subcaso.

## 12. Gaps / decisiones pendientes del director

- **C** requiere que el director (o Passport/Gustavo) confirme el límite
  real de monto antes de diseñar cualquier prueba real — ver
  `M5-T5-DESIGN-PREFLIGHT.md` sección C para el detalle del bloqueo.
- **E** requiere decidir entre recuperar un backup histórico del
  `key_value` original (si existe fuera de este repositorio) o autorizar
  un fixture nuevo de 3 fases, igual al usado para D.
- **F** — la nota honesta de la sección 7: el rechazo fue por formato de
  UUID, no confirmado como "resolution_id inexistente" en sentido más
  profundo. Si Passport requiere esa distinción más precisa, podría
  necesitar aclaración adicional.
- **D** — nota metodológica ya documentada desde el diseño original: sólo
  se ejecutó un Resolve (política anti-polling del proyecto); no se probó
  qué ocurre en un segundo intento posterior (no aplica aquí, dado que
  Passport ya confirmó la causa explícitamente).
- Ninguna de las cuatro evidencias TECNICAMENTE_DOCUMENTADO ha sido
  revisada todavía por Passport (`review_status=PENDING_PASSPORT_REVIEW`
  en las cuatro).

## 13. Integridad SHA-256 de evidencias (verificado en este ticket, XPAY-578)

| Archivo | SHA-256 |
|---|---|
| `M5-T5-MISSING-AMOUNT/evidence-2026-09-26T02-11-57Z.json` | `bd6f21f2b7a1d5b158116531cbcbb89bf03a29479da238bbd361f3a132794d7c` |
| `M5-T5-MISSING-ACCOUNT/evidence-2026-09-26T02-19-25Z.json` | `8883355389ba3cdbefe3859ccde76178f06710baebec4c3eeb4bf4ada8e442b3` |
| `M5-T5-SUSPENDED-KEY/evidence-2026-09-26T03-47-22Z.json` | `29757365146619b86a5d571efb5d3d4b0c8bb260ece2f55c9f1083e5275ca745` |
| `M5-T5-INVALID-RESOLUTION/evidence-2026-09-26T02-38-49Z.json` | `1ccc17a1e212d4501cdd3774b000064899373329c984ac575cca1891875c8517` |

## Seguridad

Escaneados los cuatro JSON públicos y este documento contra:
`Authorization`/`Bearer`/`access_token`/`client_secret`/`api_key`,
identificadores crudos, email, teléfono, `qr_code_data`, y patrones
Base64 sensibles — **sin coincidencias reales** (los únicos matches del
escaneo automático fueron el `backend_commit_sha`, público por diseño, y
fingerprints SHA-256 ya truncados, ambos explícitamente seguros por
política del proyecto). Ningún ID crudo, `key_value`, token, secreto ni
PII aparece en este documento ni en las evidencias referenciadas.

---

**Este documento es un BORRADOR DE REVISIÓN INTERNA.** No ha sido
enviado a Passport ni a Gustavo Muñoz. No declara `M5-T5 COMPLETE`, no
declara `M5-T5 ACCEPTED`, y no declara `CERTIFICATION PASSED`.
