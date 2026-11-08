# XPAY — Passport/Bre-B Certification — M5-T5 (Casos de error) — DISEÑO / PREFLIGHT

## ⚠️ BORRADOR DE DISEÑO — 4 DE 6 SUBCASOS EJECUTADOS

Este documento nació como un **plan offline** (XPAY-569). Los subcasos
**A — Missing amount** (XPAY-570), **B — Missing account** (XPAY-571),
**F — Invalid resolution_id** (XPAY-572) y **D — Suspended key**
(XPAY-575/576/577, fixture nuevo en 3 fases) ya fueron implementados y
ejecutados — ver actualización de estado en sus secciones. **E sigue
BLOQUEADO** (XPAY-573: `key_id` íntegro pero `key_value` histórico
irrecuperable — mismo tipo de causa raíz que D tenía originalmente en
XPAY-574, pero para E no se autorizó todavía un fixture nuevo). **C
tampoco se ha ejecutado** (bloqueado por falta de confirmación externa
del límite de monto). Ningún envío a Passport se ha realizado con este
documento ni con ninguna evidencia aquí referenciada.

## Requisito oficial (Anexo, Módulo 5, transcripción validada XPAY-568R)

> M5-T5 — Casos de error (campos faltantes, límites excedidos, claves
> inválidas). Evidencia requerida: Registros de respuestas de error.
>
> Detalle: (1) monto faltante, (2) cuenta faltante, (3) transacción
> superior a $12.000.000 COP, (4) envío a clave Bre-B suspendida,
> (5) envío a clave Bre-B eliminada, (6) resolution_id incorrecto.

`M5_T5_HARNESS_EXISTS=PARTIAL` — no existe ningún executor/case_id
dedicado a M5-T5 todavía, pero **los guards de seguridad subyacentes que
varios subcasos necesitan ya existen en el código productivo** (ver
matriz), reutilizables sin modificarlos.

## Matriz de subcasos

| Subcaso | Requisito oficial | Endpoint | Recurso Sandbox | Resolve previo | Llamadas reales estimadas | Riesgo | Harness hoy | Evidencia propuesta | Bloqueo |
|---|---|---|---|---|---|---|---|---|---|
| A — Missing amount | Monto faltante | `POST /v1/payments/breb` | Ninguno nuevo | No | **0** | Ninguno | ✅ `CreatePaymentMissingAmountExecutor` (XPAY-570) | [`M5-T5-MISSING-AMOUNT/evidence-2026-09-26T02-11-57Z.json`](M5-T5-MISSING-AMOUNT/evidence-2026-09-26T02-11-57Z.json) | Ninguno — `TECNICAMENTE_DOCUMENTADO` |
| B — Missing account | Cuenta faltante | `POST /v1/payments/breb` | Ninguno nuevo | No | **0** | Ninguno | ✅ `CreatePaymentMissingAccountExecutor` (XPAY-571) | [`M5-T5-MISSING-ACCOUNT/evidence-2026-09-26T02-19-25Z.json`](M5-T5-MISSING-ACCOUNT/evidence-2026-09-26T02-19-25Z.json) | Ninguno — `TECNICAMENTE_DOCUMENTADO` |
| C — Monto > $12M COP | Límite de monto | `POST /v1/payments/breb` | Resolución fresca de key `0268` | Sí | 1 (Resolve) + 1 (Payment) | **Medio/Alto — sin confirmar** | No existe guard de techo de monto en ningún lado del stack | `M5-T5-AMOUNT-LIMIT/` | **BLOQUEADO** — ver sección dedicada |
| D — Key suspendida | Envío a clave suspendida | `POST /v1/resolve-key` (nunca Payment) | ✅ fixture NUEVO (XPAY-575/576) — `key_id_fingerprint=0326AFB905CB` | Sí | 1 (Resolve, XPAY-577) | Ninguno | ✅ `ResolveM5T5SuspendedKeyExecutor` (XPAY-577) | [`M5-T5-SUSPENDED-KEY/evidence-2026-09-26T03-47-22Z.json`](M5-T5-SUSPENDED-KEY/evidence-2026-09-26T03-47-22Z.json) | Ninguno — `TECNICAMENTE_DOCUMENTADO` |
| E — Key eliminada | Envío a clave eliminada | `POST /v1/resolve-key` (nunca Payment sin Resolve exitoso) | ⚠️ **BLOQUEADO** — ver corrección XPAY-573 | Sí | 0 (bloqueado antes de HTTP) | — | N/A | `M5-T5-DELETED-KEY/` | Recurso asumido aquí (`PASSPORT_TEST_NEW_KEY_ID`) NO verificado contra su propia evidencia histórica — mismatch confirmado |
| F — resolution_id inválido | resolution_id incorrecto | `POST /v1/payments/breb` | account_id operativo (ya existe) + resolution_id sintético (nunca real) | No (deliberadamente se omite) | 1 (Payment) | Bajo (no puede dirigir fondos a ningún destino real — nunca asignado por Passport) | ✅ `CreatePaymentInvalidResolutionExecutor` (XPAY-572) | [`M5-T5-INVALID-RESOLUTION/evidence-2026-09-26T02-38-49Z.json`](M5-T5-INVALID-RESOLUTION/evidence-2026-09-26T02-38-49Z.json) | Ninguno — `TECNICAMENTE_DOCUMENTADO`, repeat guard permanente consumido |

## Diseño por subcaso

### A — Missing amount

- **Endpoint**: `POST /v1/payments/breb` — **nunca alcanzado**.
- **Mecanismo**: idéntico a `M3-T6-MISSING` (Create Key, key_value
  ausente). `PassportPaymentClient.CreateBrebPaymentAsync` YA contiene,
  en código productivo, sin ningún cambio necesario:
  ```
  if (request.Amount is null) throw new ArgumentException(...)
  if (string.IsNullOrWhiteSpace(request.Amount.Value)) throw new ArgumentException(...)
  ```
  Esta excepción es **síncrona, previa a construir cualquier
  `HttpRequestMessage`** — estructuralmente imposible que alcance
  OAuth/HTTP real.
- **Cómo impedir que el harness "arregle" el campo**: un nuevo
  `CreatePaymentMissingAmountExecutor` construye el `PassportCreatePaymentRequest`
  con `Amount: null` (o `Value: string.Empty`) de forma **deliberada y
  hardcodeada en el executor**, nunca leyendo
  `PASSPORT_TEST_M5_PAYMENT_AMOUNT_VALUE` — mismo criterio que
  `CreateKeyMissingExecutor` nunca lee `PASSPORT_TEST_NEW_KEY_VALUE`.
- **Captura**: catch de `ArgumentException` → evidencia con
  `validation_layer=LOCAL`, `passport_http_attempted=false`,
  `missing_field=amount`, `result=PASS` (el caso de certificación es
  demostrar el rechazo, no un pago).
- **Riesgo de convertirse en pago válido**: estructuralmente cero — el
  request nunca sale del proceso.
- **`T5_MISSING_AMOUNT_PLAN=READY`**
- **`T5_MISSING_AMOUNT_REAL_CALL_BUDGET=0`**
- **Estado (XPAY-570): `M5-T5-A_STATUS=TECNICAMENTE_DOCUMENTADO`.**
  Implementado (`CreatePaymentMissingAmountExecutor`/
  `CreatePaymentMissingAmountEvidenceBuilder`, comando
  `create-payment-missing-amount`), probado offline (4/4 tests
  dedicados) y ejecutado exactamente una vez:
  [`M5-T5-MISSING-AMOUNT/evidence-2026-09-26T02-11-57Z.json`](M5-T5-MISSING-AMOUNT/evidence-2026-09-26T02-11-57Z.json)
  — `result=PASS`, `passport_http_attempted=false`, `http_real_calls=0`,
  `oauth_real_token_requests=0`. `review_status=PENDING_PASSPORT_REVIEW`
  (pendiente revisión de Passport, no `PASSPORT_ACCEPTED`).

### B — Missing account

- Mismo diseño exacto que A, usando el guard ya existente:
  `if (string.IsNullOrWhiteSpace(request.AccountId)) throw new ArgumentException(...)`.
- Nuevo `CreatePaymentMissingAccountExecutor`, `AccountId: string.Empty`
  hardcodeado, nunca lee `PASSPORT_ACCOUNT_ID`.
- **`T5_MISSING_ACCOUNT_PLAN=READY`**
- **`T5_MISSING_ACCOUNT_REAL_CALL_BUDGET=0`**
- **Estado (XPAY-571): `M5-T5-B_STATUS=TECNICAMENTE_DOCUMENTADO`.**
  Implementado (`CreatePaymentMissingAccountExecutor`/
  `CreatePaymentMissingAccountEvidenceBuilder`, comando
  `create-payment-missing-account`), probado offline (4/4 tests
  dedicados, sin regresión en los 4 de XPAY-570) y ejecutado exactamente
  una vez:
  [`M5-T5-MISSING-ACCOUNT/evidence-2026-09-26T02-19-25Z.json`](M5-T5-MISSING-ACCOUNT/evidence-2026-09-26T02-19-25Z.json)
  — `result=PASS`, `account_id_fingerprint=ABSENT` (nunca un fingerprint
  de cadena vacía), `passport_http_attempted=false`, `http_real_calls=0`,
  `oauth_real_token_requests=0`. `review_status=PENDING_PASSPORT_REVIEW`.

### C — Transacción > $12.000.000 COP

- **Discrepancia documental**: no se encontró en este repositorio
  ninguna fuente local (código, `docs/`, tests) que documente un límite
  de monto Bre-B — ni 11.5M ni 12M COP. La única fuente del monto
  "$12.000.000 COP" es la transcripción del Anexo aportada por el
  director en XPAY-568R; la cifra "11.5M" mencionada en este ticket no
  tiene fuente local verificable. `T5_LIMIT_DOC_DISCREPANCY=NO_LOCAL_SOURCE`
  (no puedo confirmar ni refutar la discrepancia — simplemente no existe
  documentación local de NINGÚN límite).
- **Ausencia de guard técnico**: `PassportPaymentClient.CreateBrebPaymentAsync`
  valida únicamente que `amount.value` no esté vacío — **no existe
  ningún techo/máximo validado en ningún punto del stack productivo ni
  del harness.** Un monto alto llega a Passport tal cual, real.
- **Riesgo financiero**: si Passport NO rechaza el monto como se espera
  (por ejemplo, porque el límite real de Sandbox es distinto al
  transcrito, o no está enforced en Sandbox), la llamada sería un
  **Payment real aceptado por un monto de decenas de millones de COP**
  en la cuenta Sandbox operativa de XPAY — un resultado indeseado
  incluso en Sandbox (no hay confirmación local de que Sandbox tenga
  fondos limitados o que un "settlement" ficticio de esa magnitud sea
  inocuo).
- **`T5_LIMIT_SAFE_TO_EXECUTE_LATER=UNKNOWN`**
- **`T5_LIMIT_REQUIRED_GUARDS`**:
  1. Confirmar con Passport/Gustavo, por canal oficial, el monto exacto
     de rechazo esperado en Sandbox (no inferirlo).
  2. Nuevo guard LOCAL explícito en el futuro executor que exija un env
     var dedicado (p. ej. `PASSPORT_TEST_M5_T5_LIMIT_AMOUNT_VALUE`,
     nunca un default/hardcode) y rechace localmente ejecutar si el
     valor configurado no es inequívocamente mayor al límite confirmado.
  3. Autorización explícita y separada de cualquier otro subcaso, dado
     su riesgo distinto a los demás (los otros 5 subcasos son de riesgo
     bajo/nulo).
- **`T5_LIMIT_PLAN=BLOCKED`**

### D — Envío a clave Bre-B suspendida

- **Recurso**: `PASSPORT_TEST_QR_SUSPENDED_KEY_ID` — la misma llave
  suspendida usada en M4-T3-A (creada y suspendida ahí, XPAY-476/477,
  fingerprint `E2B083E0FD76`). Ya existe en el env, **no requiere
  ninguna mutación nueva** (no se re-suspende, no se re-crea).
- **Estado no reverificado offline**: el histórico local no registra
  ninguna reactivación de esta key específica después de M4-T3-A — pero
  esto **no está confirmado por una llamada real reciente**. El primer
  paso seguro de la ejecución futura debería ser de solo-lectura (p. ej.
  un Resolve real de esta key, que además es necesario de todos modos
  para obtener un `resolution_id` con el que intentar el Payment) antes
  de asumir su estado.
- **Contexto oficial relevante**: Passport (Gustavo Muñoz) ya clarificó
  formalmente, para M4-T3-A, que *"la validación de suspensión ocurre
  durante Payment (Módulo 5), no durante la creación del QR"* — ver
  `M4-T3-A/passport-official-clarification-2026-09-22.md`. Esto sugiere
  fuertemente que el rechazo esperado para este subcaso ocurre en el
  paso de Payment (Create o Retrieve), consistente con lo que M5-T5
  busca demostrar.
- **`T5_SUSPENDED_KEY_RESOURCE_AVAILABLE=YES`** ~~(afirmación original,
  INCOMPLETA — ver corrección)~~

#### ⚠️ CORRECCIÓN (XPAY-574) — identidad del key_id confirmada, pero el key_value NUNCA fue persistido

XPAY-574 §1 reverificó `PASSPORT_TEST_QR_SUSPENDED_KEY_ID` contra su
propia evidencia histórica (`M4-T3-A`) — **identidad del key_id
confirmada** (`E2B083E0FD76` coincide exactamente).

Pero §2 (correlación key_id ↔ key_value, control obligatorio introducido
tras el hallazgo de E en XPAY-573) reveló un bloqueo estructural
distinto: `CreateM4T3SuspendedFixtureKeyExecutor` generó el `key_value`
de esta llave con `DisposableBcodeGenerator` — **criptográficamente
aleatorio, generado en memoria, nunca persistido en ningún artifact,
env, ni evidencia** (política uniforme del proyecto: `key_value` siempre
`"REDACTED"`, ni siquiera como fingerprint). La propia
`CreateM4T3SuspendedFixtureKeyEvidenceBuilder` lo documenta
explícitamente en sus `Notes`: *"El key_id real NO fue persistido —
recupérelo manualmente desde Passport Sandbox Dashboard"* — eso resolvió
el key_id (ya en el env), pero el **key_value nunca tuvo un mecanismo de
recuperación equivalente**, y no existe ninguna variable de entorno
`PASSPORT_TEST_QR_SUSPENDED_KEY_VALUE` (ni ninguna similar) en todo el
proyecto.

Sin `key_value`, `IPassportKeyClient.ResolveKeyAsync` (que resuelve por
`customer_id + key_type + key_value`, NUNCA por `key_id` — a diferencia
de Create QR, que sí referencia la key directamente por su ID) no puede
construirse de forma segura para esta llave específica.

**`T5_SUSPENDED_KEY_RESOURCE_AVAILABLE=NO`** (corregido — el key_id es
válido pero es insuficiente para Resolve)
**`T5_SUSPENDED_KEY_PLAN=BLOCKED`** (corregido — bloqueado a menos que
el director haya conservado el BCODE generado fuera de este repositorio
en el momento de la creación de XPAY-476, p. ej. desde el propio
Passport Dashboard; de lo contrario requiere un fixture desechable NUEVO
con autorización explícita separada, igual que E)

#### ✅ ACTUALIZACIÓN (XPAY-575) — fixture NUEVO creado, corrigiendo la causa raíz — D sigue SIN ejecutarse

El director autorizó crear un fixture Sandbox nuevo y desechable
EXCLUSIVAMENTE para M5-T5-D, en dos fases separadas. **Esta es
únicamente la FASE 1 (Create Key)** — la llave todavía NO está
suspendida y el caso M5-T5-D **NO se ha ejecutado**.

Mejora estructural respecto al fixture histórico (XPAY-474/476/477): el
`key_value` generado se persiste de forma recuperable en un private
capture ANTES de la llamada HTTP (`PREPARED_NOT_CREATED`) y de nuevo,
correlacionado con el `key_id` real, tras el éxito (`CREATED`) — ambos
registros verificados con hash SHA-256 propio, correlación confirmada
offline. Este problema **no volverá a repetirse** para este fixture.

- **Nuevo fixture**: `key_type=BCODE`, `key_value_fingerprint=816264C840B0`,
  `key_id_fingerprint=0326AFB905CB`, `status=ACTIVE` — **confirmado
  distinto** de la key activa 0268 (comparación directa de valores
  crudos, nunca sólo por fingerprint).
- **Evidencia de trazabilidad** (NO es evidencia de certificación del
  caso D): [`M5-T5-D-KEY-PREP-CREATE/evidence-2026-09-26T03-11-54Z.json`](M5-T5-D-KEY-PREP-CREATE/evidence-2026-09-26T03-11-54Z.json)
  — `certification_test_result=NOT_YET_EXECUTED`,
  `fixture_phase=CREATED_NOT_SUSPENDED`.
- **Private capture** (fuera de Git, mode 700/600, nunca adjunto a
  Gustavo): dos registros correlacionados por el mismo sufijo de
  timestamp, con el `key_value` y el `key_id` reales preservados juntos
  en el registro `CREATED` — recuperables para la Fase 2.
- **Repeat guard PERMANENTE**: consumido — esta Fase 1 nunca puede
  repetirse para este fixture (evita crear accidentalmente una segunda
  llave desechable).
- **`M5_T5_D_STATUS=FIXTURE_CREATED_NOT_SUSPENDED`** (NUNCA
  `TECNICAMENTE_DOCUMENTADO` — eso requiere Suspend + el intento real de
  Resolve, en un ticket futuro separado y explícitamente autorizado).

**Siguiente paso (NO autorizado todavía)**: Fase 2 — Suspend Key sobre
esta misma llave (usando el `key_id` ya preservado), y sólo después,
Fase 3 — el Resolve real que constituye la evidencia de M5-T5-D.

#### ✅ ACTUALIZACIÓN (XPAY-576) — Fase 2 (Suspend) completada — D sigue SIN ejecutarse

El `key_id` se leyó EXCLUSIVAMENTE del private capture `CREATED` de
XPAY-575 (nunca de env/CLI/hardcode) — estructuralmente imposible de
confundir con el fixture histórico de M4-T3-A (ese fixture nunca tuvo
private capture). Fingerprints re-verificados antes de HTTP:
`key_id_fingerprint=0326AFB905CB` / `key_value_fingerprint=816264C840B0`
— coinciden exactamente con XPAY-575.

- **Passport confirmó la suspensión real**: `returned_status=SUSPENDED`.
- **Evidencia de trazabilidad** (NO es evidencia de certificación del
  caso D): [`M5-T5-D-KEY-PREP-SUSPEND/evidence-2026-09-26T03-22-05Z.json`](M5-T5-D-KEY-PREP-SUSPEND/evidence-2026-09-26T03-22-05Z.json)
  — `certification_test_result=NOT_YET_EXECUTED`,
  `fixture_phase=SUSPENDED_NOT_TESTED`.
- **Repeat guard PERMANENTE de esta Fase 2**: consumido
  (`M5-T5-D-SUSPEND-CONSUMED-MARKER.private.json`, mode 600) — esta
  Suspend nunca puede repetirse para este fixture.
- **Artifact `CREATED` de XPAY-575 verificado byte-idéntico** después de
  la operación — el `key_value` sigue recuperable para la Fase 3.
- **`M5_T5_D_STATUS=FIXTURE_SUSPENDED_READY_FOR_TEST`** (NUNCA
  `TECNICAMENTE_DOCUMENTADO` — eso requiere el Resolve real de la Fase 3,
  en un ticket futuro separado y explícitamente autorizado).

**Siguiente paso (NO autorizado todavía)**: Fase 3 — Resolve real sobre
esta misma key ya suspendida (`key_id_fingerprint=0326AFB905CB`,
`key_value_fingerprint=816264C840B0`), leyendo el `key_value` desde el
mismo private capture — esa es la operación que finalmente produce la
evidencia de certificación de M5-T5-D.

#### ✅ FASE 3 COMPLETADA (XPAY-577) — M5-T5-D TECNICAMENTE_DOCUMENTADO

Correlación Create→Suspend verificada offline antes de HTTP (mismos
fingerprints en el artifact `CREATED` de XPAY-575 y el marcador de
XPAY-576, con `returned_status=SUSPENDED` confirmado) — sólo entonces se
autorizó la única llamada Resolve real, usando el `key_value` leído
exclusivamente del private capture.

**Passport rechazó el Resolve con una confirmación EXPLÍCITA e
inequívoca de la causa**:

> `safe_error_message`: *"Key operation rejected: Key has been suspended
> by the Client. (U805)"*

A diferencia del hallazgo de M5-T5-F (rechazo por formato de UUID, causa
ambigua), aquí Passport nombra la suspensión literalmente —
`current_suspension_confirmed=true` sin necesidad de inferencia.

- **Evidencia FINAL de certificación** (no preparatoria):
  [`M5-T5-SUSPENDED-KEY/evidence-2026-09-26T03-47-22Z.json`](M5-T5-SUSPENDED-KEY/evidence-2026-09-26T03-47-22Z.json)
  — `result=FAIL` (fallo de transporte, mismo criterio ya establecido
  para casos negativos: M3-T6-INVALID/M3-T7/M5-T5-INVALID-RESOLUTION),
  `http_status=400`, `safe_error_code=generic_error`,
  `certification_case_result=EXPECTED_PROVIDER_REJECTION`.
- **Repeat guard PERMANENTE de la Fase 3**: consumido
  (`M5-T5-D-RESOLVE-CONSUMED-MARKER.private.json`, mode 600).
- **Cero Payment** — estructuralmente imposible: este executor nunca
  conoció `IPassportPaymentClient`.
- **`M5_T5_D_STATUS=TECNICAMENTE_DOCUMENTADO`.**

**Cadena completa de M5-T5-D**: XPAY-575 (Create) → XPAY-576 (Suspend,
`SUSPENDED` confirmado) → XPAY-577 (Resolve rechazado, suspensión
confirmada explícitamente por Passport). Los JSON de XPAY-575/576 son
trazabilidad preparatoria — el JSON de XPAY-577 es el candidato principal
para el paquete de certificación de este subcaso.

### E — Envío a clave Bre-B eliminada

- **Recurso asumido originalmente**: `PASSPORT_TEST_NEW_KEY_ID` — la
  llave del ciclo M3-T1→T3→T4→T5→T7, eliminada en M3-T5 y con segundo
  Delete confirmado `404` en M3-T7.
- **Sin clarificación oficial específica** (a diferencia de D) sobre en
  qué paso ocurre el rechazo para una key eliminada — a determinar
  empíricamente en la ejecución real (¿falla el Resolve por key no
  encontrada, o falla el Payment?).
- **`T5_DELETED_KEY_RESOURCE_AVAILABLE=YES`** ~~(afirmación original,
  INCORRECTA — ver corrección)~~

#### ⚠️ CORRECCIÓN (XPAY-573) — el recurso NO estaba verificado y el forense reveló una discrepancia real

El forense obligatorio de XPAY-573 §1 comparó el fingerprint
ACTUALMENTE configurado de `PASSPORT_TEST_NEW_KEY_ID` contra el
`key_id_fingerprint` real registrado en la propia evidencia histórica
(M3-T1/M3-T3/M3-T4/M3-T5, las cuatro consistentes entre sí:
`9654CF47D144`) — y **NO coinciden**:

| Variable | Fingerprint esperado (evidencia histórica) | Fingerprint actual en `~/.passport-sandbox.env` |
|---|---|---|
| `PASSPORT_TEST_NEW_KEY_ID` | `9654CF47D144` (M3-T1/T3/T4/T5) | `9315E012C94C` — **NO coincide** |
| `PASSPORT_TEST_NEW_KEY_VALUE` | (nunca fingerprinteado — `key_value` siempre `REDACTED` por política) | comparado directamente (sin imprimir): **idéntico byte a byte a `PASSPORT_TEST_BREB_KEY`** (la llave ACTIVA 0268, fingerprint `41962CECC119`) |

Es decir: en algún momento posterior al cierre del ciclo M3-T7,
`PASSPORT_TEST_NEW_KEY_ID`/`PASSPORT_TEST_NEW_KEY_VALUE` dejaron de
apuntar a la llave históricamente eliminada y ahora contienen valores
que **no corresponden a ninguna llave eliminada conocida** —
`PASSPORT_TEST_NEW_KEY_VALUE` en particular contiene actualmente el
mismo valor que la llave ACTIVA de certificación. Usar estas variables
para "M5-T5-E (key eliminada)" habría ejecutado, en la práctica, un
Resolve sobre la llave ACTIVA 0268 mal etiquetado como prueba de "key
eliminada" — un error de identidad grave para evidencia de
certificación.

**`T5_DELETED_KEY_RESOURCE_AVAILABLE=NO`** (corregido)
**`T5_DELETED_KEY_PLAN=BLOCKED`** (corregido — bloqueado hasta que se
recupere o confirme de forma segura el `key_id`/`key_value` real de la
llave efectivamente eliminada en M3-T5/T7, sin reconstruirlo ni
inventarlo — posiblemente requiere backup histórico del env, o aceptar
que ese recurso ya no es recuperable y se necesita un fixture desechable
NUEVO análogo al de M4-T3-B/XPAY-492, con autorización explícita
separada ya que eso sí implicaría Create+Delete Key reales)

### F — resolution_id incorrecto

- **Diseño seguro**: nuevo `InvalidResolutionIdGenerator` — mismo patrón
  exacto que `InvalidCustomerIdGenerator` (XPAY-471): valor sintético,
  determinista, con forma de UUID (mismo shape observado del
  `resolution_id` real, 36 caracteres), **nunca derivado de ningún
  resolution_id real ni de su fingerprint**, y reconocible como
  sintético (p. ej. terminación `...000000000002`, distinto del usado
  para customer_id en M4-T3-C para no confundirse).
- **Por qué es fail-closed**: un UUID nunca asignado por Passport no
  puede corresponder a ninguna cuenta/destinatario real — no existe
  ningún resultado posible donde esto dirija fondos a un tercero. El
  único resultado posible es un rechazo de Passport (`resolution_id` no
  encontrado/inválido) o, en el peor caso, un error de protocolo — nunca
  un pago real completado.
- **`PassportPaymentClient.CreateBrebPaymentAsync` no valida el formato**
  de `resolution_id` (sólo que no esté vacío) — por tanto esta llamada
  **sí llega a Passport real** (a diferencia de A/B).
- **`T5_INVALID_RESOLUTION_PLAN=READY`**
- **`T5_INVALID_RESOLUTION_RISK=LOW`**
- **`T5_INVALID_RESOLUTION_REQUIRED_GUARDS`**: generador determinista
  dedicado (nunca un valor "random" no reproducible); presupuesto
  estricto de 1 llamada real; nunca reutilizar el UUID sintético de
  `InvalidCustomerIdGenerator` para evitar ambigüedad entre subcasos.
- **Estado (XPAY-572): `M5-T5-F_STATUS=TECNICAMENTE_DOCUMENTADO`.**
  Implementado (`InvalidResolutionIdGenerator` — valor sintético
  determinista `...000000000002`, `CreatePaymentInvalidResolutionExecutor`/
  `CreatePaymentInvalidResolutionEvidenceBuilder`, comando
  `create-payment-invalid-resolution`), probado offline (12/12 tests
  dedicados, sin regresión en A/B) y ejecutado exactamente una vez —
  única llamada real de todo M5-T5 hasta ahora:
  [`M5-T5-INVALID-RESOLUTION/evidence-2026-09-26T02-38-49Z.json`](M5-T5-INVALID-RESOLUTION/evidence-2026-09-26T02-38-49Z.json)
  — `result=FAIL` (fallo de transporte, mismo criterio ya establecido por
  M3-T6-INVALID/M3-T7 — un rechazo de Passport ES el resultado deseado de
  este caso negativo, nunca un fallo del harness),
  `passport_response_received=true`, `http_status=400`,
  `safe_error_code=invalid_parameter_value`,
  `safe_error_message="UUID version 4 expected"`. Hallazgo notable: el
  rechazo ocurrió por validación de **formato** de UUID (mismo tipo de
  error ya observado históricamente para `customer_id` sintético en
  M4-T3-C/XPAY-471), no necesariamente por "resolution_id inexistente" —
  de todas formas satisface literalmente el requisito oficial ("uso de un
  ID de resolución de clave incorrecto"), pero esta distinción debe
  aclararse si Passport lo requiere. Repeat guard PERMANENTE consumido
  (marcador `M5-T5-INVALID-RESOLUTION-CONSUMED-MARKER.private.json`) —
  este subcaso NUNCA puede repetirse en este directorio privado.
  `review_status=PENDING_PASSPORT_REVIEW`.

## Estrategia de ejecución posterior propuesta

Un subcaso por ticket real, orden de menor a mayor riesgo/incertidumbre:

1. ✅ **A — Missing amount** (XPAY-570, completo).
2. ✅ **B — Missing account** (XPAY-571, completo).
3. ✅ **F — resolution_id inválido** (XPAY-572, completo, repeat guard permanente consumido).
4. **D — Key suspendida** — ⚠️ **BLOQUEADO** (XPAY-574): el `key_id`
   (`E2B083E0FD76`) es íntegro (coincide con su evidencia histórica
   M4-T3-A), pero el `key_value` generado por `DisposableBcodeGenerator`
   al crear este fixture nunca fue persistido en ningún lugar recuperable
   del repositorio — Resolve necesita `key_value`, no `key_id`. Bloqueado
   por el mismo tipo de causa raíz que E, aunque en D nunca hubo
   contaminación (simplemente nunca existió un mecanismo de
   preservación).
5. **E — Key eliminada** — ⚠️ **BLOQUEADO** (XPAY-573): el recurso
   asumido (`PASSPORT_TEST_NEW_KEY_ID`/`PASSPORT_TEST_NEW_KEY_VALUE`) no
   corresponde a la llave históricamente eliminada — ver corrección
   arriba. Requiere recuperar el recurso correcto o crear uno nuevo
   (autorización separada) antes de reintentar este subcaso.
6. **C — Monto > $12M COP** — **al final, y sólo tras resolver el
   bloqueo** (confirmación externa del límite real + guard dedicado).
   No ejecutar sin autorización separada y explícita.

Cada ticket real: presupuesto explícito de llamadas, cero reintentos,
evidencia JSON propia, y **detenerse tras la respuesta** para revisión
humana antes de continuar al siguiente subcaso — mismo criterio
disciplinado ya usado en toda la cadena M5-T2/T3/T4 (XPAY-563 a 567).

## Regla incorporada al proyecto (XPAY-569 §1)

**El documento narrativo no es suficiente como paquete final.** Cada
subcaso ejecutado deberá quedar respaldado por su propio
`evidence-<timestamp>.json` sanitizado bajo
`docs/certificacion/passport-breb/M5-T5-<SUBCASO>/`, generado
exclusivamente por una ejecución real autorizada — nunca fabricado.

## Seguridad

Ningún ID crudo, key value, token o PII aparece en este documento —
únicamente fingerprints SHA-256 truncados y nombres de variables de
entorno (nunca sus valores).
