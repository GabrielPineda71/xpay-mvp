# M5-T5-C — Exceso de límites de Bre-B (> 12M COP): plan de ejecución (XPAY-618)

**Fecha (UTC):** 2026-10-01
**Modo de este ticket:** 100 % offline. `PASSPORT_API_CALLS=0`, `FINANCIAL_OPERATIONS_EXECUTED=0`.
**Commit congelado del stack negativo M5-T5:** `128de82`
**Estado del caso:** `T5_C_STATUS=READY_FOR_IMPLEMENTATION` — **la ejecución NO está autorizada.**

Este documento es la receta de ejecución. No ejecuta nada.

---

## 1. Fuentes, y qué se pudo verificar en este ticket

Se distinguen deliberadamente las fuentes **verificadas aquí contra el documento primario** de
las que solo constan en documentación interna previa.

| Afirmación | Fuente | ¿Verificada en XPAY-618? |
|---|---|---|
| «Exceso de límites de Bre-B (transacción superior a $12 millones COP).» | Anexo de certificación, §3.5 Módulo 5 (`.docx`, leído directamente) | ✅ **SÍ** — aparece literalmente dos veces |
| «Entorno Sandbox: Entorno no productivo proporcionado por Passport para actividades de desarrollo, pruebas y certificación. **En este entorno no se procesan fondos reales.**» | Anexo de certificación, sección de definiciones | ✅ **SÍ** — literal |
| «Bre-B transaction limit is 11.5 million COP» | Documentación oficial de API de Passport, *Initiate a Payment* | ⚠️ **NO re-verificada** — ver abajo |
| `U112` «Maximum amount check failed.», `B107`/`B108`/`B109` (exceden máximo permitido) | Catálogo oficial de códigos de Passport | ⚠️ **NO re-verificada** — ver abajo |

### Por qué el 11,5M y los códigos quedan como no re-verificados

Se buscaron sus cadenas exactas en: todo el repositorio, `~/Documents`, `~/Downloads`, el PDF
*Vinculación Bre-b as a Service Passport_Coop v1.6* y el *Contrato PaaS español V3.3*. Resultado:
**cero coincidencias**. Las únicas apariciones locales están en documentos internos escritos por
nosotros (XPAY-609/611/612), que citan la documentación **online** de Passport consultada en su
momento.

Consecuencia práctica: **ninguna**. El monto propuesto excede los dos umbrales, así que el plan no
depende de cuál sea el correcto. Pero no se debe presentar el 11,5M ni los códigos como cita
re-verificada en este ticket. Si se quiere elevar a verificado, hace falta un ticket que autorice
releer la documentación online de Passport.

---

## 2. Principio de la prueba y monto propuesto

El objetivo **no** es dirimir si el límite contractual es 11,5M o 12M. El Anexo pide demostrar
rechazo para `amount > 12.000.000 COP`. Un monto por encima de 12M excede también el 11,5M
documentado, así que satisface el requisito bajo cualquiera de las dos lecturas.

**`PROPOSED_AMOUNT_COP = 12.000.001`** → valor a enviar: **`"12000001.00"`**

Mínimo exceso posible sobre el umbral del Anexo; evita una cifra arbitrariamente grande.

### Verificación de la representación (Fase 4)

| Punto | Hallazgo |
|---|---|
| Tipo en el DTO | `PassportPaymentAmountRequest.Value` es **`string`** (`PassportCreatePaymentRequest.cs`), `Currency` es `string` fijado a `"COP"` |
| Ruta del harness | `CreateBrebPaymentCertificationExecutor` lee `PASSPORT_TEST_M5_PAYMENT_AMOUNT_VALUE` y lo pasa **verbatim** al DTO — **cero conversiones numéricas** |
| Riesgo de overflow / truncamiento / coma decimal regional / interpretación como centavos | **NINGUNO en nuestro código**: no hay `decimal`, `long`, `double` ni `ToString` cultural en esa ruta |
| Formato canónico | El builder productivo usa `Valor.ToString("0.00", InvariantCulture)` y el valor actual del entorno es `1.00` ⇒ **unidades con 2 decimales, separador punto** |
| Unidad | **Unidades de peso, no centavos** — el pago positivo de 1 COP se envió como `1.00`, no `100` |

`AMOUNT_REPRESENTATION_VERIFIED=YES`

⚠️ **Riesgo residual del operador:** al pasarse verbatim, la corrección depende de escribir la
variable exactamente como `12000001.00`. Un `12.000.001,00` o un `12000001` sin decimales no serían
corregidos por nuestro código. El ejecutor dedicado (§7) debe validar el formato antes de HTTP.

---

## 3. Destino propuesto (Fase 2)

**No se crea ninguna llave nueva.** Se reutiliza la llave positiva de certificación ya empleada en
los flujos exitosos de M5.

| Propiedad | Valor |
|---|---|
| Variable | `PASSPORT_TEST_BREB_KEY` |
| Tipo | `BCODE` (`PASSPORT_TEST_BREB_KEY_TYPE`) |
| Fingerprint | `41962CECC119` |

Evidencia de que es segura y apropiada:

- **Es Sandbox:** `PASSPORT_BASE_URL` resuelve al host Sandbox conocido y `SandboxHostGuard` aborta
  cualquier ejecución contra otro host.
- **Es la llave positiva:** es la que lee `M5ResolveExecutor` (Guard #4, «targets de KEY_A/CUSTOMER_A,
  MISMOS ya usados desde M3-T2») para todos los Resolve positivos de M5.
- **Fue usada en flujos positivos:** M5-T2 Resolve, M5 Payment Create (×4), T6, T7 y el Retrieve de T8.
- **No es un fixture negativo:** su fingerprint difiere de los dos fixtures desechables —
  T5-D suspendido (`816264C840B0`) y T5-E eliminado (`A0989C54075F`).
- **No hay evidencia de suspensión ni eliminación:** ninguna operación `suspend`/`delete` del
  repositorio apunta a esta llave; al contrario, es la que los *protected-key guards* de T5-D y T5-E
  defienden activamente (comparación directa en memoria antes de cada HTTP, verificada en XPAY-615
  y XPAY-616).

`DESTINATION_SAFETY_VERIFIED=YES`. Su valor no se imprime en ningún artefacto público.

---

## 4. `resolution_id` (Fase 3)

**No hay ningún `resolution_id` reutilizable.**

| Dato | Valor |
|---|---|
| `resolution_id` en entorno (fingerprint) | `E5AEF4D709F6` |
| `resolved_at_raw` | `2026-09-30T21:49:27.999Z` |
| `expires_at_raw` | `2026-09-30T22:19:27.999Z` |
| TTL observado | **exactamente 00:30:00** — confirma empíricamente la ventana de 30 minutos |
| Estado a 2026-10-01T02:39Z | **vencido hace ~4 h 20 min** |
| ¿Consumido? | **Sí** — tiene marcador `M5-T2-RESOLVE-CONSUMED-*` y su fingerprint coincide con uno de los `M5-PAYMENT-CREATE` ya ejecutados |

`HISTORICAL_RESOLUTION_REUSABLE=NO` · `NEW_RESOLVE_REQUIRED=YES`

### El guard del Resolve ya permite exactamente un Resolve nuevo

`M5ResolveArtifactReader.IsBlockingForRepeat` bloquea si el artefacto **no** está consumido, o si
está consumido pero `expires_at` sigue vigente, o si está consumido y `expires_at` falta o no
parsea (fail-closed). Solo permite continuar cuando está **consumido Y `expires_at` parsea Y ya
venció** — que es exactamente el estado actual.

**Por tanto: no hace falta archivar nada, ni mover artefactos, ni cambiar código para obtener el
`resolution_id`.** El comando `resolve-key-m5-t2` está habilitado para una ejecución real.

---

## 5. Flujo exacto de la futura ejecución (Fase 1)

```
  (1) resolve-key-m5-t2  --execute --confirm-resolve-key-m5-t2
         POST /v1/resolve-key
         body: { customer_id, key: { key_type: "BCODE", key_value: <KEY_A> } }
         → 200 { id (resolution_id), resolved_at, expires_at }
         → persiste captura privada M5-T2-RESOLVE-<ts>.private.json
                                   │
         ── GATE HUMANO: verificar expires_at y que queden ≥ 10 min ──
                                   │
  (2) <comando T5-C dedicado>  --execute --confirm-<...>     [POR IMPLEMENTAR]
         POST /v1/payments/breb
         body: { account_id, resolution_id, amount: { value: "12000001.00", currency: "COP" } }
         → se espera RECHAZO por exceder el máximo
```

Campos requeridos del Payment, confirmados en el DTO (`PassportCreatePaymentRequest`):
`account_id` (**siempre la cuenta operativa de XPAY**, nunca una cuenta de usuario),
`resolution_id`, `amount.value`, `amount.currency`. `display_name` es opcional y se omite.

`FUTURE_MAX_RESOLVE_CALLS=1` · `FUTURE_MAX_PAYMENT_CALLS=1` · `AUTOMATIC_RETRY_ALLOWED=NO`

Si el Resolve falla → **Payment = 0** y detenerse. Si el Payment devuelve cualquier respuesta
(éxito o error) → **no hay segundo Payment**, bajo ninguna circunstancia.

---

## 6. Clasificador del rechazo (Fase 5)

Conservador por diseño, mismo criterio que `ClassifyDeletionSignal` de T5-E: **nunca** devuelve
`NO`, porque la ausencia de señal no prueba lo contrario.

`LIMIT_REJECTION = YES` solo si la respuesta sanitizada de Passport contiene una señal
**inequívoca de exceso de monto máximo**, es decir cualquiera de:

- código estructurado `U112`, `B107`, `B108` o `B109`; **o**
- un `safe_error_code` / `safe_error_message` que mencione inequívocamente *maximum amount*
  (p. ej. «maximum amount», «exceeds the maximum», «amount limit», «límite … monto») **junto a**
  una semántica de exceso.

No se hardcodea un único código como la única forma de PASS: la documentación admite varias señales.

`LIMIT_REJECTION = UNKNOWN` en todo lo demás. En particular, **nunca** cuentan como PASS de T5-C
los errores de: autenticación, `resolution_id` (inválido/vencido/inexistente), cuenta, llave,
conectividad/timeout, o validación no relacionada con el monto. Un `400` genérico sin código ni
mensaje de monto es `UNKNOWN`, no PASS.

⚠️ **Precedente que obliga a esta cautela:** en T5-D el rechazo real llegó como
`safe_error_code = "generic_error"` con el detalle real en el mensaje (`U805`), y en T5-E llegó como
`generic_error` con **mensaje nulo**. Es perfectamente posible que el rechazo por límite llegue sin
código estructurado. Por eso el clasificador debe mirar código **y** mensaje, y el resultado
`UNKNOWN` debe considerarse un desenlace probable, no una anomalía.

---

## 7. Implementación faltante (Fase 9)

`T5_C_EXECUTOR_EXISTS=NO` · `IMPLEMENTATION_REQUIRED_BEFORE_EXECUTION=YES`

No existe ningún ejecutor de exceso de monto en el harness (los únicos ejecutores de Payment son
el positivo `M5-PAYMENT-CREATE` y los negativos de *missing amount*, *missing account* e
*invalid resolution*).

**`CreateBrebPaymentCertificationExecutor` NO debe reutilizarse**, aunque técnicamente aceptaría el
monto por entorno: su `case_id` es `M5-PAYMENT-CREATE`, el del **caso positivo cuya evidencia ya fue
enviada a Passport**. Ejecutarlo con 12.000.001 contaminaría ese expediente con un intento negativo
y escribiría una captura privada bajo ese mismo prefijo.

### Qué hay que implementar antes de autorizar red

1. **`CreatePaymentOverLimitExecutor`** — un Payment, exactamente uno.
   - `case_id` propio: **`M5-T5-OVER-LIMIT`**.
   - Lee el `resolution_id` **exclusivamente** de la captura privada `M5-T2-RESOLVE-*` (nunca de
     entorno, nunca de CLI), replicando el patrón de T5-E.
   - **Guard de vigencia obligatorio antes de HTTP:** `expires_at` parseable y aún vigente con
     margen; si no, `LOCAL_BLOCKED` sin llamada.
   - **Guard de formato del monto:** exige `^\d+\.\d{2}$` y valor `> 12000000.00`; si no,
     `LOCAL_BLOCKED`. Cierra el riesgo del operador descrito en §2.
   - **Repeat guard permanente propio**, marcador fijo `M5-T5-OVER-LIMIT-CONSUMED-MARKER`.
     No reutiliza ninguno de *missing amount*, *missing account*, *invalid resolution*,
     *suspended key* ni *deleted key*.
   - No conoce `IPassportKeyClient`; solo `IPassportPaymentClient.CreateBrebPaymentAsync`.
     **Nunca** `RetrievePaymentAsync`.
2. **`CreatePaymentOverLimitEvidenceBuilder`** — `BuildExpectedRejection` (con
   `limit_rejection_confirmed` en `true`/`null`, nunca `false`),
   `BuildUnexpectedAcceptance`, `BuildLocalBlocked`.
3. **Clasificador `ClassifyLimitSignal(httpStatus, safeErrorCode, safeErrorMessage)`** según §6.
4. **Wiring:** comando `create-payment-over-limit` + flag exclusivo
   `--confirm-create-payment-over-limit`.
5. **Tests offline** con `FakeBrebPaymentClient`: guards, formato de monto, vigencia de resolución,
   repeat guard, clasificador (incluido el caso `generic_error` con mensaje nulo → `UNKNOWN`),
   saneamiento de evidencia y ausencia de `RetrievePaymentAsync`.

`LIMIT_REJECTION_CLASSIFIER_DESIGNED=YES` (diseñado aquí, no implementado).

---

## 8. Si Passport ACEPTA el Payment (Fase 6)

Desenlace inesperado, explícitamente contemplado:

1. **No repetir. No crear un segundo Payment. No intentar compensación manual.** No existe
   ninguna operación de reverso autorizada, y el ejecutor no puede invocar `RetrievePaymentAsync`.
2. Registrar de forma sanitizada `PAYMENT_ACCEPTED_UNEXPECTEDLY=YES`, con
   `certification_case_result = "UNEXPECTED_PAYMENT_ACCEPTANCE"`, el `payment_id` **solo como
   fingerprint**, el `status` devuelto y `limit_rejection_confirmed = null`.
3. **Detenerse** y escalar al director.
4. `T5_C_RESULT = INCONCLUSIVE`. Una aceptación **no** cierra el caso ni lo marca FAIL: documenta
   que Sandbox no aplicó el límite, que es información de certificación en sí misma.

**Status inicial que podría devolver:** el estado documentado/observado del ciclo de vida de pago en
este repositorio incluye valores intermedios tipo `PROCESSING`/`PENDING` antes de un terminal. **No
se asume** ninguno concreto: se registra literalmente el que llegue. Y no se asume que una
aceptación implique movimiento de fondos — el Anexo dice que en Sandbox no se procesan fondos reales
(§1, verificado).

---

## 9. Webhooks (Fase 7)

No se modifica, desactiva ni cambia nada: el callback de QA sigue registrado y
`PASSPORT_WEBHOOK_SIGNATURE_MODE` sigue en ENFORCE. No se emite ningún webhook manual.

**Qué pasaría realmente si Passport acepta el pago.** El pago de T5-C es **saliente**
(*outbound*), y el handler de QA solo procesa eventos **inbound**:
`PassportWebhookService` clasifica un evento outbound como `NotInbound`, el controller responde
**200** (`received: true, processed: false`) y **no persiste nada** en `passport_webhook_events`.
Es decir: el 200 evita reintentos del proveedor, pero **no quedará rastro en base de datos** de los
eventos de este pago.

**Cómo reconocer entonces si llegaron eventos asociados a esta prueba**, sin IDs completos:

- Fuente primaria: el `payment_id_fingerprint` registrado por el propio ejecutor T5-C en su
  artefacto privado y en la evidencia sanitizada.
- Correlación: comparar ese fingerprint con `Fingerprint.Compute(<payment_id>)` de cualquier
  registro o log que se inspeccione después. Nunca copiar el `payment_id` completo.
- Si se necesitara confirmación del ciclo de vida, requeriría un `Retrieve` — **operación aparte,
  no autorizada por este plan ni por el ticket de ejecución propuesto**.

`WEBHOOK_CONTINGENCY_PLAN=READY`

---

## 10. Aislamiento del caso (Fase 8)

| Componente | Valor propio de T5-C |
|---|---|
| `case_id` de evidencia | `M5-T5-OVER-LIMIT` |
| Repeat guard | marcador fijo `M5-T5-OVER-LIMIT-CONSUMED-MARKER.private.json` |
| Evidence builder | `CreatePaymentOverLimitEvidenceBuilder` |
| Artefacto privado | **sí**, pero **fingerprint-only** (no hay valor crudo nuevo que preservar: el `resolution_id` ya vive en la captura de Resolve y el `payment_id` no se necesita en claro) |
| Evidencia sanitizada | `docs/certificacion/passport-breb/M5-T5-OVER-LIMIT/evidence-<ts>.json` |
| Clasificador | `ClassifyLimitSignal` |

Ningún guard, `case_id` ni marcador se comparte con los otros cinco subcasos.

---

## 11. Riesgo (Fase 11)

| Dimensión | Valor | Fundamento |
|---|---|---|
| `FINANCIAL_OPERATION_TYPE` | `PAYMENT_API_CALL` | Es una llamada real a la API de pagos, no una simulación |
| `REAL_FUNDS_RISK` | **`NONE_PER_SANDBOX_DOCUMENTATION`** | Anexo, verificado literalmente en §1: «En este entorno no se procesan fondos reales» |
| `CERTIFICATION_TRACE_RISK` | **`YES`** | No se minimiza: ver abajo |

El riesgo real no es financiero, es de expediente. Si Passport acepta inesperadamente el pago, en
Sandbox pueden quedar: un registro de pago, un ciclo de vida asociado, eventos de webhook enviados a
QA (que no persistiremos, §9) y evidencia adicional que no estaba prevista en el paquete M5. Eso es
ruido en el rastro de certificación que después hay que explicar a Passport. Es exactamente por eso
que el caso lleva repeat guard permanente, una sola llamada y un gate humano entre Resolve y Payment.

---

## 12. Criterio de cierre (Fase 12)

**`T5_C_PASS`** únicamente si se cumplen las cuatro condiciones:

1. el monto enviado fue `> 12.000.000` COP;
2. el request llegó realmente a Passport (hubo respuesta del proveedor, no un bloqueo local);
3. Passport **rechazó** el Payment;
4. el rechazo está **inequívocamente** relacionado con el límite máximo de monto (§6).

**`T5_C_INCONCLUSIVE`** si el Payment fue aceptado, o si falló por cualquier otra razón
(autenticación, resolución, cuenta, llave, conectividad, validación ajena al monto).

En ningún caso se repite automáticamente. `INCONCLUSIVE` no es un fracaso del harness: es un
resultado que se documenta y se eleva al director.

---

## 13. Qué tiene que pasar antes de ejecutar

1. Implementar lo de §7 (código + tests offline, cero llamadas) y verificarlo.
2. Autorizar, en ticket aparte, **un** `resolve-key-m5-t2` real.
3. Verificar `expires_at` con margen suficiente.
4. Autorizar, en ticket aparte, **un** `create-payment-over-limit` real.

`T5_C_EXECUTION_AUTHORIZED=NO` · `OVER_12M_IMPLEMENTED=NO`

## Seguridad de este documento

Sin secretos, credenciales, PII ni identificadores completos de Passport. Los únicos valores
sensibles referidos lo están por fingerprint SHA-256 truncado (`41962CECC119`, `816264C840B0`,
`A0989C54075F`, `E5AEF4D709F6`). El monto propuesto y el TTL observado no son información sensible.

---

## Addendum (XPAY-619) — divergencias del diseño implementado

El plan de arriba se conserva **sin modificar**. La implementación (ver
`M5-T5-C-IMPLEMENTACION-XPAY-619.md`) divergió en dos puntos, ambos hacia un diseño más estricto:

1. **El monto no se lee del entorno.** §7 esbozaba un guard de formato sobre
   `PASSPORT_TEST_M5_PAYMENT_AMOUNT_VALUE`. El ejecutor usa en su lugar una constante propia
   (`CertificationAmountValue = "12000001.00"`) y no lee esa variable. Elimina por completo el
   riesgo de operador identificado en §2, en vez de solo validarlo.
2. **El repeat guard se consume antes del HTTP**, no después de la respuesta. §10 lo describía con
   el patrón de T5-D/T5-E. Al tratarse de un pago, una caída entre el request y la escritura del
   marcador habría dejado habilitado un segundo intento.

El resto del plan —destino, resolución, flujo, clasificador, aceptación inesperada, webhooks,
riesgos y criterio de cierre— se implementó tal como está descrito.

**T5-C sigue sin ejecutar.**
