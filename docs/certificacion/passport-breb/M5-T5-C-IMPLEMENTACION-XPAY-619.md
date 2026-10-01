# M5-T5-C — Exceso de límites de Bre-B (> 12M COP): implementación (XPAY-619)

**Fecha (UTC):** 2026-10-01
**Modo de este ticket:** 100 % offline. `PASSPORT_API_CALLS=0`, `RESOLVE_CALLS=0`, `PAYMENT_CALLS=0`.
**Base:** `128de82` (`feat(passport): consolidate M5 negative certification cases`)
**Plan previo:** `M5-T5-C-PLAN-EJECUCION-XPAY-618.md`

> ## ⚠️ T5-C NO HA SIDO EJECUTADO
> Este ticket implementa y prueba el ejecutor **sin hacer ninguna llamada a Passport**.
> No hubo Resolve, no hubo Payment, no se generó evidencia de certificación.
> `T5_C_EXECUTION_AUTHORIZED=NO`.

---

## 1. Qué se implementó

| Archivo | Líneas | Rol |
|---|---|---|
| `CreatePaymentOverLimitExecutor.cs` | 516 | Ejecutor: guards locales, un único Payment, clasificador |
| `CreatePaymentOverLimitEvidenceBuilder.cs` | 186 | Evidencia saneada (rechazo / aceptación inesperada / bloqueo local) |
| `CreatePaymentOverLimitExecutorTests.cs` | 807 | 36 métodos → 92 casos de prueba offline |
| `HarnessDecision.cs` | +13 | Comando `create-payment-over-limit` + flag exclusiva |
| `HarnessApp.cs` | +57 | Ayuda, bloque dry-run y bloque de ejecución |

`case_id` de certificación: **`M5-T5-OVER-LIMIT`** · Repeat guard: **`M5-T5-OVER-LIMIT-CONSUMED`**

Comando: `create-payment-over-limit --execute --confirm-create-payment-over-limit`

---

## 2. El monto: constante, no variable de entorno

**`CertificationAmountValue = "12000001.00"`**, constante de la clase. El ejecutor **no lee**
`PASSPORT_TEST_M5_PAYMENT_AMOUNT_VALUE` en absoluto.

Esto es una **divergencia deliberada** respecto a lo que esbozaba el plan de XPAY-618 §7, y es más
seguro: elimina por completo el riesgo de operador que ese mismo documento identificó en §2. Un
`12.000.001,00`, un `12000001` o un `1.00` mal puestos en el entorno no pueden llegar a Passport,
porque el entorno no participa en la decisión. Además, la variable del caso **positivo** sigue
valiendo `1.00` y no hay que tocarla.

La validación de formato se aplica igualmente sobre la constante, como defensa en profundidad ante
una edición futura del código.

### `ValidateAmount(value, currency)` — función pura

Rechaza, y hay test para cada forma:

| Entrada | Resultado |
|---|---|
| `12000001.00` + `COP` | `Valid` |
| `12000000.00` (el umbral exacto) | `NotAboveThreshold` — «superior a» es estricto |
| `11999999.99`, `11500001.00`, `1.00`, `0.00` | `NotAboveThreshold` |
| `12000001` (sin decimales) | `MalformedFormat` |
| `12000001,00`, `12.000.001,00`, `12.000.001.00` | `MalformedFormat` |
| `12000001.0`, `12000001.000`, `12000001.` | `MalformedFormat` |
| ` 12000001.00`, `12000001.00 `, `+…`, `-…`, `1.2e7` | `MalformedFormat` |
| cualquier `currency` ≠ `COP` (incluido `cop`) | `UnsupportedCurrency` |
| vacío / nulo | `Missing` |

El parseo decimal se usa **solo** para comparar contra el umbral. El string que viaja a Passport es
siempre el original, nunca reformateado.

### Umbral: la cifra del Anexo, no la de la API

`MinCertificationThresholdCop = 12000000.00`, la única verificada en fuente primaria.
**El 11,5M de la documentación de API no está hardcodeado** — XPAY-618 no pudo re-verificarlo en
ninguna fuente local, y no es el criterio del caso. Hay un test que lo fija explícitamente:
`11500001.00` **no** satisface este caso.

---

## 3. La resolución: fuente privada, no entorno

El ejecutor lee el `resolution_id` **exclusivamente** de la captura privada del Resolve positivo
(`M5ResolveArtifactReader`, reutilizado sin modificar). Nunca de entorno, nunca de CLI, nunca
hardcodeado. **Nunca ejecuta Resolve por su cuenta.**

`ValidateResolution` exige, antes de cualquier HTTP:

| Condición | Fallo |
|---|---|
| Directorio privado existe | `DirectoryMissing` |
| Exactamente un artefacto `M5-T2-RESOLVE-*` | `ArtifactMissing` / `ArtifactAmbiguous` |
| Legible y con `case_id` + `resolution_id` | `ArtifactUnreadable` / `ArtifactInvalid` |
| **No** tiene marcador de consumo | `AlreadyConsumed` |
| `expires_at` parseable | `ExpiresAtUnparseable` |
| `expires_at` vigente **con ≥ 60 s de margen** | `Expired` |
| `key_value_fingerprint` == fingerprint de `PASSPORT_TEST_BREB_KEY` | `KeyCorrelationMismatch` |

Cualquier fallo ⇒ `LOCAL_BLOCKED_INVALID_OR_EXPIRED_RESOLUTION`, `PAYMENT_CALLS=0`.

Dos decisiones que endurecen el caso positivo:

- **`ExpiresAtUnparseable` bloquea.** En el Payment positivo (XPAY-529 §16) la ausencia de metadata
  temporal se documentaba como *gap* en vez de bloquear. Aquí no: gastar el único intento autorizado
  en una resolución cuya vigencia no se puede verificar no se justifica.
- **Margen de 60 s.** Más estricto que `expires_at > now`: evita que la resolución venza en vuelo.
  Sobre un TTL de 30 min (medido empíricamente en XPAY-618) el coste es despreciable.

La correlación con la llave positiva se hace **por fingerprint**. El `key_value` nunca entra en el
request del Payment, que solo necesita el `resolution_id`.

---

## 4. Repeat guard: consumido ANTES del HTTP

Marcador fijo `M5-T5-OVER-LIMIT-CONSUMED-MARKER.private.json`, con `state: "ATTEMPTED"`, escrito
**antes** de la llamada. Si no se puede escribir, **no se llama a Passport**.

**Divergencia deliberada** respecto a T5-D y T5-E, que escriben su marcador *después* de la
respuesta. Aquí la operación es un **pago**: una caída del proceso entre el request y la escritura
del marcador dejaría el guard disponible y habilitaría un segundo pago. Fail-closed: preferimos un
guard consumido sin respuesta registrada (reconciliable offline con el artefacto de outcome) a la
posibilidad de un segundo Payment.

Consecuencia verificada por test: **ningún** desenlace habilita un segundo intento — ni un rechazo,
ni una aceptación, ni una excepción inesperada no contemplada.

Tras la respuesta se escribe, aparte y nunca sobrescribiendo, un
`M5-T5-OVER-LIMIT-OUTCOME-<ts>.private.json` fingerprint-only.

El guard es exclusivo: no se comparte con *missing amount*, *missing account*,
*invalid resolution*, *suspended key* ni *deleted key*, y hay test de que los marcadores de T5-D/T5-E
presentes en el mismo directorio no lo bloquean ni lo habilitan.

---

## 5. Clasificador `ClassifyLimitSignal`

Devuelve **`true` (YES)** o **`null` (UNKNOWN)**. **Nunca `false`**: la ausencia de señal no prueba
que el rechazo no fuera por límite.

`YES` si se cumple **cualquiera** de:

- el `safe_error_code` es exactamente `U112`, `B107`, `B108` o `B109` (comparación de token
  completo: un `AB107XYZ` no dispara); o
- uno de esos códigos aparece como token en el mensaje — **la forma en que T5-D expuso su `U805`**; o
- código o mensaje contienen una frase inequívoca de máximo/límite de monto. El separador es
  flexible (`[\s_-]+`), así que `limit exceeded` y `limit_exceeded` se reconocen igual. Cubre
  inglés y español (`monto máximo`, `excede … monto`, `límite … monto`).

`UNKNOWN` en todo lo demás.

### `generic_error` por sí solo nunca es PASS

Calibrado contra la realidad ya observada en este proyecto:

| Caso | Respuesta real |
|---|---|
| T5-D | `generic_error` + mensaje útil (`"…suspended by the Client. (U805)"`) |
| T5-E | `generic_error` + **mensaje nulo** |

Por eso `generic_error` sin señal de límite ⇒ `LIMIT_REJECTION=UNKNOWN` ⇒ `T5_C_RESULT=INCONCLUSIVE`,
**no PASS**. Lo mismo para errores de autenticación, resolución, cuenta, llave, conectividad o
validación ajena al monto — todos con test.

Un `200` con `error` poblado se trata como **rechazo**, nunca como aceptación.

---

## 6. Si Passport acepta el pago

`payment_accepted_unexpectedly = true`, `limit_rejection = null`,
`certification_case_result = "UNEXPECTED_PAYMENT_ACCEPTANCE"`, `t5_c_result = "INCONCLUSIVE"`.

El ejecutor **se detiene**: no consulta el ciclo de vida (no conoce `RetrievePaymentAsync`), no
intenta un segundo Payment, no intenta compensación. El `status` devuelto se registra **literalmente**
—no se asume `PROCESSING` ni ningún otro— y el `payment_id` solo como fingerprint.

El Anexo define Sandbox como entorno donde no se procesan fondos reales (verificado en XPAY-618),
así que el riesgo es de rastro de certificación, no financiero.

---

## 7. Separación estructural de capacidades

| Capacidad | Estado | Cómo se garantiza |
|---|---|---|
| Create Payment | **SÍ** (necesaria) | `IPassportPaymentClient.CreateBrebPaymentAsync`, exactamente una vez |
| Retrieve Payment | **NO** | El fake lanza si se invoca; test en los tres desenlaces |
| Resolve | **NO** | No conoce `IPassportKeyClient` |
| Create / Suspend / Activate / Delete / List Keys | **NO** | Igual: sin `IPassportKeyClient` por ninguna vía |

Verificado por reflexión: ni parámetro, ni campo, ni retorno de tipo `IPassportKeyClient` en todo el
tipo; y la única dependencia remota declarada por `ExecuteAsync` es `IPassportPaymentClient`.

`PAYMENT_CAPABILITY_PRESENT=YES` · `PAYMENT_CAPABILITY_INVOKED_DURING_XPAY_619=NO`

---

## 8. Dry-run

```
dry_run=YES http_call=NO oauth_token_requested=NO
amount_value=12000001.00 currency=COP
annex_threshold_cop=12000000.00 amount_above_threshold=True
```

En dry-run se evalúan **solo** las validaciones puras (el monto), seguras sin credenciales ni
artefactos. **No** se lee la captura privada de Resolve, **no** se verifica el repeat guard real y
**no** se escribe su marcador — verificado: el directorio privado quedó con los mismos 13 archivos
y cero artefactos `M5-T5-OVER-LIMIT`.

---

## 9. Evidencia saneada

Registra: `case_id`, `environment`, timestamp, `amount_value`, `currency`,
`amount_above_annex_threshold`, `annex_threshold_cop`, `request_attempted`, `observed_http_status`,
`safe_error_code`, `safe_error_message`, `limit_rejection`, `payment_accepted`,
`payment_accepted_unexpectedly`, `returned_status`, `resolution_id_fingerprint`,
`account_id_fingerprint`, `payment_id_fingerprint`, contadores de llamadas por tipo,
`repeat_guard_consumed`, `certification_case_result`, `t5_c_result`, `review_status`.

**Nunca**: `resolution_id`, `payment_id`, `account_id`, `customer_id`, `key_value`, `key_id`
completos, ni `Authorization`/`Bearer`/tokens/secretos/PII/raw request/raw response. Hay test que
serializa la evidencia y verifica la ausencia de cada valor crudo.

---

## 10. Pruebas

| Suite | Resultado |
|---|---|
| Focalizada T5-C | **92/92 PASS** |
| M5-T5 negativos (A, B, D, E, F + C) | **245/245 PASS** |
| Harness completa | **1060/1060 PASS** |
| Backend completa | **1045/1045 PASS** |

`NETWORK_CALLS_FROM_TESTS=0`: el único colaborador remoto es un `FakePaymentClient` en memoria.

Un fallo real encontrado y corregido durante la implementación: el clasificador no reconocía
`limit_exceeded` (código estructurado con guion bajo) porque el patrón solo admitía espacios. El test
tenía razón y se corrigió la implementación, no el test.

---

## 11. Qué falta para ejecutar

1. Autorizar **un** `resolve-key-m5-t2` real. El guard de ese comando ya lo permite (XPAY-618 §4).
2. Verificar `expires_at` con margen suficiente.
3. Autorizar **un** `create-payment-over-limit` real.

`FUTURE_MAX_RESOLVE_CALLS=1` · `FUTURE_MAX_PAYMENT_CALLS=1` · `AUTOMATIC_RETRY_ALLOWED=NO`

`T5_C_STATUS=READY_FOR_CONTROLLED_EXECUTION` · `T5_C_EXECUTION_AUTHORIZED=NO`

## Seguridad de este documento

Sin secretos, credenciales, PII ni identificadores completos de Passport. El monto, el umbral y los
códigos de error citados no son información sensible.
