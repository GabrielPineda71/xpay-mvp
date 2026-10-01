# XPAY — Passport/Bre-B Certification — M5-T6 / M5-T7 (Webhook)

> ## 📌 ESTADO HISTÓRICO / SUPERADO
>
> Este documento es el **preflight de diseño de XPAY-580**, anterior a cualquier
> implementación o ejecución. Todo lo que afirma era correcto **en su momento** y se conserva
> sin modificar por trazabilidad. **No describe el estado actual.**
>
> Lo que ocurrió después:
>
> | Entonces (XPAY-580) | Ahora |
> |---|---|
> | `T6_CODE_IMPLEMENTED=NO` · `T7_CODE_IMPLEMENTED=NO` | Implementado en XPAY-594 y corregido para lotes en XPAY-602 (commits `285f5ac` y `d2faff9`) |
> | «pago entrante» ambiguo entre dos lecturas (§1) | **Resuelto por Gustavo Muñoz** en XPAY-593: «pago hacia la llave de XPAY o QR» — la segunda lectura |
> | `PASSPORT_QUESTIONS_REQUIRED=YES`, 5 preguntas sin enviar (§13) | **Las cinco respondidas**: contrato cerrado en XPAY-593 (ver `M5-T6-T7-WEBHOOK-CONTRATO-FINAL.md`) |
> | T6 y T7 sin evidencia | **T6 = PASS** y **T7 = PASS** con entrega real (XPAY-604); **T8 = PASS** (XPAY-606) |
>
> Su §7 sobre la relación con Wallet/Ledger y su §8 sobre T8 **siguen vigentes** y fueron
> ratificados por el contrato final y por el cierre de T8.
>
> Cierre documental: `M5-T6-T7-CIERRE-XPAY-604.md` y `M5-T8-CIERRE-XPAY-606.md`.

## ⚠️ PREFLIGHT OFFLINE — NO IMPLEMENTADO — NO ENVIADO

Inspección **100% offline** (XPAY-580). Cero llamadas a Passport, cero
código nuevo, cero cambios de configuración/base de datos, cero
despliegue. Este documento es exclusivamente de diseño/decisión.

## 1. Requisito oficial T6 / T7 / T8

Transcripción ya validada (XPAY-568R):

| Caso | Descripción oficial | Evidencia requerida |
|---|---|---|
| M5-T6 | Recibir webhook de pago entrante | Registros de entrega del webhook |
| M5-T7 | Validar payload y estado del webhook | Registros del webhook |
| M5-T8 | Consultar el estado final del pago vía API | Respuesta de GET payment |

**`CERTIFICATION_REQUIREMENT`**: recibir una entrega HTTP de Passport,
validarla, y registrar evidencia de esa recepción/validación.

**`IMPLEMENTATION_ASSUMPTION` (no confirmada por el Anexo, señalada
explícitamente para no confundirla con requisito)**: la frase "pago
entrante" es ambigua entre dos lecturas igualmente plausibles:

1. El **webhook en sí** es entrante (llega a nuestro servidor), sobre el
   MISMO pago saliente B→A ya certificado en T2 — narrativa coherente
   con "T2 inicia → T6 notifica async del mismo pago → T8 confirma vía
   API".
2. Un **pago genuinamente entrante** (un tercero paga hacia la llave
   Bre-B de XPAY) — un flujo Bre-B completamente distinto, nunca
   ejercitado en esta certificación.

No se puede resolver esta ambigüedad únicamente con fuentes locales —
ver sección 13 (pregunta a Passport).

## 2. Estado actual del backend — confirmado por inspección de código, no sólo por XPAY-579

`T6_CODE_IMPLEMENTED=NO`
`T7_CODE_IMPLEMENTED=NO`

Confirmado por:

- **Cero controllers** de webhook Passport (`grep` exhaustivo sobre
  `backend/Xpay.Api/Controllers/` sin resultados para un endpoint que
  reciba Passport).
- `docs/PASSPORT_BREB_PLAN.md` marca explícitamente
  `Webhooks Passport procesados | ❌ Fase 65` y lo lista como pendiente
  #6 de "Pendientes para Fase 65".
- `BrebPaymentReconciliationService.cs` (XPAY-381) documenta en su
  propio header: *"hoy esa era la ÚNICA vía (XPAY-380 lo confirmó con
  evidencia de código: **sin webhook**, sin polling previo, ni
  server-side ni frontend)"*.
- El modelo `PassportWebhookEvent` existe (tabla `passport_webhook_events`,
  migración `database/010_passport_breb_base.sql`) pero **ningún código
  lo instancia ni lo consulta** fuera de su registro en `XpayDbContext`
  — es un esquema preparado, nunca usado.

## 3. Componentes existentes reutilizables

| Componente | Propósito actual | Reusable para T6/T7 | Razón |
|---|---|---|---|
| `KycController.VeriffWebhook()` (`POST /api/kyc/veriff/webhook`) | Receptor de webhook de Veriff (KYC) | `PARTIAL` | Patrón arquitectónico DIRECTAMENTE transplantable: `Request.EnableBuffering()` + lectura de raw body antes de model binding, validación HMAC-SHA256 de firma con comparación constant-time, 401 sin cambio de estado si la firma falla, logging saneado (nombres de headers, nunca valores/secretos/body), delegación a un método de servicio para el procesamiento real. NO reutilizable literalmente porque el algoritmo/header de Passport es desconocido (sección 6). |
| `KycService.ValidateVeriffSignature(rawBody, signature)` | HMAC-SHA256(body, `VERIFF_SHARED_SECRET`) constant-time | `PARTIAL` | Mismo algoritmo probablemente aplicable a Passport (HMAC-SHA256 es el estándar de facto), pero el header/nombre exacto de Passport es desconocido. |
| `BrebPaymentStateMachine.Classify` / `.Decide` | Clasifica `PENDING/PROCESSING/SETTLED/REJECTED` y decide la transición de un retiro LOCAL | `PARTIAL` | Diseñado EXPLÍCITAMENTE para reutilizarse desde un futuro webhook (comentario propio: *"FASE 11 — el mismo BrebPaymentStateMachine... está diseñado para reutilizarse desde un futuro handler de webhook"*). Su función `Classify` (clasificación pura del status) es 100% reutilizable sin cambios. Su función `Decide`/`ApplyPassportPaymentStatusAsync` (aplicación financiera) NO es utilizable para un webhook de certificación — ver sección 9. |
| `passport_webhook_events` (tabla) | Log genérico de eventos Passport | `YES` | `id_breb_retiro` es `NULL`-able — puede registrar un evento de certificación SIN ningún retiro local asociado. Columnas ya cubren: tipo de evento, payment_id, hash+versión saneada del payload, si la firma fue válida, si se procesó, timestamps, mensaje de error. Sin cambio de esquema necesario para el mínimo de certificación. |
| Confirmación de `PASSPORT_WEBHOOK_SECRET` (`BrebService.cs`/health-config) | Sólo verifica presencia (booleano) de la variable | `PARTIAL` | Confirma que el proyecto YA previó necesitar este secreto — pero la variable **no está configurada actualmente** en `~/.passport-sandbox.env` (verificado, ausente). |

`EXISTING_REUSABLE_COMPONENTS=KycController.VeriffWebhook (patrón), KycService.ValidateVeriffSignature (algoritmo probable), BrebPaymentStateMachine.Classify (clasificación pura), passport_webhook_events (persistencia)`

## 4. Contrato conocido vs. desconocido

| Aspecto | Estado | Fuente |
|---|---|---|
| HTTP method | `UNKNOWN` (asumible `POST` por convención universal de webhooks, nunca confirmado para Passport específicamente) | — |
| Callback path | `UNKNOWN` | — |
| Auth mechanism (transporte) | `UNKNOWN` (probablemente ninguno — los webhooks típicamente no usan `Authorization`, se aseguran por firma) | — |
| Signature mechanism | `PARTIAL` — existe la variable `PASSPORT_WEBHOOK_SECRET` (implica algún esquema de firma), algoritmo probable HMAC-SHA256 por precedente de Veriff, pero **nunca confirmado para Passport** | `docs/PASSPORT_BREB_PLAN.md` §8 |
| Signature header name | `UNKNOWN` | — |
| Secret/config required | `PARTIAL` — nombre de variable ya decidido (`PASSPORT_WEBHOOK_SECRET`), valor real no configurado todavía | `docs/PASSPORT_BREB_PLAN.md` §8 |
| Event type | `PARTIAL` — `payment.outbound.settled` y `payment.outbound.rejected` confirmados textualmente (consulta de solo lectura previa a `docs.passportfintech.com/EN/webhooks`, XPAY-373); ningún `payment.inbound.*` documentado localmente | `BrebPaymentStateMachine.cs` (comentario XPAY-373) |
| Payload schema (JSON body completo) | `UNKNOWN` — nunca capturado/documentado localmente | — |
| Payment identifier (campo en el webhook) | `UNKNOWN` (razonable esperar `id`/`payment_id`, nunca confirmado en el body del webhook específicamente) | — |
| Amount / currency en el webhook | `UNKNOWN` | — |
| Origin/sender information | `UNKNOWN` | — |
| Destination information | `UNKNOWN` | — |
| Payment status (vocabulario) | `PARTIAL` — mismo vocabulario `PENDING/PROCESSING/SETTLED/REJECTED` ya confirmado para Create/Retrieve Payment, razonablemente el mismo para el webhook, nunca confirmado explícitamente en ese contexto | `BrebPaymentStateMachine.cs` |
| Event identifier (id del propio evento, para idempotencia) | `UNKNOWN` | — |
| Event timestamp | `UNKNOWN` | — |
| Retry behavior | `UNKNOWN` | — |
| Duplicate delivery behavior | `UNKNOWN` | — |
| Expected HTTP response | `UNKNOWN` (convención universal: `2xx`, nunca confirmado para Passport) | — |
| Timeout expectations | `UNKNOWN` | — |
| IP allowlist | `UNKNOWN` (sin mención en ningún documento local) | — |

`WEBHOOK_CONTRACT_DOCUMENTED_LOCALLY=PARTIAL` — lo único confirmado
localmente, con fuente textual real: los dos nombres de evento
`payment.outbound.settled`/`payment.outbound.rejected` y el vocabulario
de `status`. Todo lo demás (forma exacta del payload, firma, semántica
de reintentos/duplicados, respuesta esperada) es desconocido y **no
debe inventarse**.

## 5. Seguridad

| Control | Clasificación |
|---|---|
| Validación de firma (HMAC) | `REQUIRED_BY_PASSPORT` (implícito por la existencia de `PASSPORT_WEBHOOK_SECRET`) |
| Comparación constant-time de firma | `RECOMMENDED_XPAY_CONTROL` (ya aplicado en el precedente Veriff — buena práctica propia, no exigida explícitamente por Passport en ningún texto local) |
| Idempotencia / protección ante entrega duplicada | `RECOMMENDED_XPAY_CONTROL` — infraestructura PARCIAL: `passport_webhook_events.processed` permite marcar procesado, pero no existe ninguna restricción única sobre un `event_id` de Passport (porque ese campo es `UNKNOWN`) |
| Protección contra replay | `RECOMMENDED_XPAY_CONTROL` — sin infraestructura específica hoy (ni timestamp-window ni nonce); dependería de si Passport firma con un timestamp incluido (desconocido) |
| Logging saneado (nunca payload crudo con datos bancarios/PII) | `RECOMMENDED_XPAY_CONTROL`, ya resuelto arquitectónicamente: `payload_hash` + `payload_sanitized_json` en el esquema existente, mismo criterio ya aplicado en TODO el proyecto |
| Nunca loguear el secreto | `REQUIRED_BY_PASSPORT` (implícito, estándar) + ya como regla explícita del proyecto (`docs/PASSPORT_BREB_PLAN.md` §14) |

No se implementó nada de lo anterior en este ticket — es únicamente
clasificación de qué correspondería a cada categoría cuando se
implemente.

## 6. Persistencia / modelo de datos

`DATABASE_CHANGE_REQUIRED=NO` **para el mínimo de certificación**.

La tabla `passport_webhook_events` ya existente cubre: tipo de evento,
`payment_id` (nullable), referencia opcional a un retiro local (nullable
— nunca obligatoria), hash + versión saneada del payload, si la firma
fue válida, si se procesó, timestamps de recepción/procesamiento,
mensaje de error. **No requiere ninguna migración nueva** para registrar
un evento de certificación de Passport, incluso uno que no corresponda a
ningún retiro local real.

**Mejora recomendada, no bloqueante**: agregar una restricción de
unicidad sobre un futuro `event_id` de Passport (hoy `UNKNOWN`) para
protección de duplicados a nivel de base de datos — sólo evaluable una
vez se conozca si ese campo existe en el payload real.

## 7. Relación con Wallet / Ledger — hallazgo arquitectónico central

**Hallazgo clave de este preflight**: `BrebPaymentStateMachine.Decide` /
`BrebPaymentService.ApplyPassportPaymentStatusAsync` — la única función
del proyecto que mueve Wallet/Ledger en respuesta a un status de
Passport — **exige un `idBrebRetiro` real y ya persistido** (lanza
`InvalidOperationException("Retiro {id} no encontrado.")` si no existe).
Esta función está diseñada para el flujo de PRODUCTO (retiros reales de
usuarios), nunca para un payment de certificación creado directamente
por el harness (que, por diseño deliberado desde XPAY-529, está
**aislado arquitectónicamente** de `BrebPaymentService`/Wallet/ledger —
mismo criterio "AISLAMIENTO ARQUITECTÓNICO" ya aplicado en
`CreateBrebPaymentCertificationExecutor` y todo M5).

Por tanto:

- **`CERTIFICATION_MINIMUM`**: recibir la entrega HTTP, validar firma,
  registrar el evento en `passport_webhook_events` (con o sin
  correlación a un retiro — normalmente SIN, para el pago de
  certificación de M5-T2), responder `2xx` a Passport. **NUNCA** llama a
  `ApplyPassportPaymentStatusAsync`, **NUNCA** toca Wallet/Ledger.
- **`PRODUCT_FINANCIAL_BEHAVIOR`**: el webhook de un retiro REAL de
  usuario sí debe, eventualmente, invocar
  `ApplyPassportPaymentStatusAsync` (ítem #6 de "Pendientes para Fase
  65" en `docs/PASSPORT_BREB_PLAN.md`) — pero esto es alcance de
  producto/Fase 65, **no** un requisito de esta certificación.

No se modificó Wallet ni Ledger en este ticket. No se asume
acreditación automática alguna.

## 8. M5-T8 — recomendación

`T8_AFTER_T6_T7=NEW_RETRIEVE_RECOMMENDED`

Razonamiento: la evidencia ya reutilizada en XPAY-579 (mismo GET de
T3/T4) demuestra técnicamente la operación, pero una vez que T6/T7
existan y produzcan una entrega real de webhook, la narrativa de
certificación se fortalece demostrablemente si T8 es un GET **fresco**,
ejecutado específicamente **después** de recibir esa entrega — mostrando
que la API y el webhook coinciden. Esto es una recomendación de diseño
para un ticket futuro, **no autoriza ninguna llamada ahora**.

## 9. Estrategia mínima de implementación (futura, no autorizada aquí)

1. Nuevo endpoint dedicado (p. ej. `POST /api/passport/webhook`),
   mismo patrón que `KycController.VeriffWebhook()`: raw body con
   buffering, sin `[Authorize]`, validación de firma antes de cualquier
   procesamiento.
2. Validación de firma mediante una función nueva análoga a
   `ValidateVeriffSignature`, usando `PASSPORT_WEBHOOK_SECRET` — SÓLO
   una vez confirmado el algoritmo/header real de Passport.
3. Registro en `passport_webhook_events` (sin migración) —
   `signature_valid`, `payload_hash`, `payload_sanitized_json`,
   `event_type`, `passport_payment_id` si el body lo trae.
4. Respuesta `2xx` genérica — nunca acoplada a `BrebPaymentService`.
5. **Explícitamente EXCLUIDO** de este endpoint mínimo: cualquier
   llamada a `ApplyPassportPaymentStatusAsync`/Wallet/Ledger.

## 10. Estrategia de pruebas (futura)

- Tests offline (xUnit) del nuevo validador de firma, con secretos
  sintéticos — mismo patrón que las pruebas HMAC ya existentes para
  Veriff.
- Test end-to-end offline del endpoint con un payload sintético y firma
  sintética válida/inválida — nunca contra Passport real.
- Ninguna prueba puede ejecutarse hoy porque el algoritmo/formato real
  de Passport es desconocido — se implementaría contra el contrato
  documentado el día que Passport lo confirme.

## 11. Estrategia futura de evidencia de certificación

Cuando exista el endpoint: registrar UNA entrega real (Sandbox) del
webhook de Passport para el mismo pago ya certificado en T2 (o uno
nuevo), generar `evidence.json` sanitizado para T6 (recepción) y T7
(validación de payload/status), siguiendo el mismo patrón ya establecido
en todo M5 (`case_id`, fingerprints, nunca IDs/payload crudos). Esto NO
se ejecuta en este ticket.

## 12. Dependencias / bloqueos

- Implementación de código nueva (fuera de alcance de certificación
  pura — es desarrollo de producto, Fase 65 según el plan existente).
- Confirmación del contrato real de Passport (sección 13) antes de
  escribir cualquier validador de firma o parser de payload — inventar
  cualquiera de los dos sería inseguro.
- Configuración de `PASSPORT_WEBHOOK_SECRET` en Sandbox (actualmente
  ausente).
- Decisión del director sobre si vale la pena invertir en esto ahora
  (development real) versus enviar el paquete M5-T1..T5 ya disponible
  (XPAY-579) y tratar T6/T7/T8 como pendiente declarado ante Passport.

## 13. Preguntas mínimas a Passport (NO enviadas)

Sólo las estrictamente necesarias, dado lo que ya sabemos:

1. ¿Cuál es el esquema JSON completo del payload de webhook para
   `payment.outbound.settled` / `payment.outbound.rejected`? (campos,
   anidación, tipos).
2. ¿Existen también eventos de **pago entrante** (dinero recibido en una
   llave Bre-B de XPAY), distintos de los `payment.outbound.*` ya
   confirmados? Si sí, ¿cuáles son sus nombres de evento?
3. ¿Cuál es el mecanismo exacto de firma? (nombre del header, algoritmo,
   qué se firma exactamente — body crudo, o body + timestamp).
4. ¿Qué respuesta HTTP espera Passport para considerar la entrega
   exitosa, y cuál es la política de reintentos ante una respuesta no
   exitosa o timeout?
5. ¿Cómo se registra/confirma la URL de callback para Sandbox —
   autoservicio en el Dashboard, o requiere configuración por parte de
   Passport?

`PASSPORT_QUESTIONS_REQUIRED=YES` — no se enviaron, per instrucción
explícita de este ticket.

## Seguridad de este documento

Ningún secreto, token, ID crudo ni PII fue incluido. Los únicos valores
citados (`PASSPORT_WEBHOOK_SECRET` como NOMBRE de variable,
`payment.outbound.settled`/`payment.outbound.rejected` como nombres de
evento documentados) son metadatos de configuración/contrato, nunca
valores secretos.

---

**Este documento es PREFLIGHT OFFLINE.** No se implementó código, no se
modificó configuración/base de datos, no se desplegó nada, no se llamó a
Passport, no se contactó a Gustavo Muñoz.
