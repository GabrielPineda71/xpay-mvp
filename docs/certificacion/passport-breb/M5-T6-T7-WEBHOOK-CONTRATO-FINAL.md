# XPAY — Passport / Bre-B — M5-T6 / M5-T7 — Contrato final de webhook

**Ticket:** XPAY-593 · **Fecha:** 2026-09-29 (UTC)
**Estado:** contrato CERRADO · implementación NO ejecutada en este ticket
**Alcance:** únicamente lo necesario para la evidencia de certificación T6/T7.
Sin operación financiera, sin Wallet, sin Ledger, sin llamadas a la API de Passport.

Sustituye como fuente de verdad contractual al preflight
`M5-T6-T7-WEBHOOK-PREFLIGHT.md` (XPAY-580), cuyas **cinco preguntas abiertas quedan
todas respondidas** aquí. El preflight sigue vigente en su hallazgo arquitectónico
sobre Wallet/Ledger (su §7), que este documento ratifica.

Convención de trazabilidad usada en todo el documento:
`DOCUMENTADO_PASSPORT` · `CONFIRMADO_GUSTAVO` · `INFERENCIA`.

---

## 1. Requisitos oficiales (DOCUMENTADO_PASSPORT)

Fuente: `https://docs.passportfintech.com/EN/webhooks`

### 1.1 Tipos de evento

| Evento | Significado | Flujo |
|---|---|---|
| `payment.inbound.received` | Pago enviado correctamente a XPAY en la red Bre-B | 1.º |
| `payment.inbound.confirmed` | Confirmado por el banco receptor | 2.º |
| `payment.inbound.settled` | Completado en el banco receptor | 3.º |
| `payment.inbound.rejected` | Pago entrante rechazado | terminal |
| `payment.outbound.confirmed` · `payment.outbound.settled` · `payment.outbound.rejected` | Ciclo saliente (ya certificado en M5-T2/T3/T4) | — |

Flujo entrante documentado: `received → confirmed → settled`.
Flujo saliente documentado: `confirmed → settled`.

**T6/T7 utilizan exclusivamente los eventos `payment.inbound.*`.**

### 1.2 Estados

`PROCESSING` (en `received`), `CONFIRMED`, `SETTLED`. El rechazo no se expresa como
estado sino mediante el evento `payment.*.rejected` con un objeto `error`.

### 1.3 Estructura del payload

Campos comunes a todos los eventos:

| Campo | Tipo | Nota |
|---|---|---|
| `Event` | string | **Nombre del evento. Obsérvese la E mayúscula** |
| `payment_id` | string (UUID) | Identificador del pago |
| `created_at` | string ISO-8601 | |
| `updated_at` | string ISO-8601 | |

Adicionales en `payment.inbound.received`:

`type` ("BREB") · `amount{value, currency}` · `sender{owner{name, identification_type,
identification_number}, account{account_type, account_number}, participant{name,
identification_number}}` · `receiver{…igual que sender, más key{key_type, key_value}}` ·
`account_id` (UUID) · `status` · `destination_key{key_type, key_value}` ·
`qr_code_reference` · `end_to_end_identification`.

En `confirmed` se omiten `amount` y `type`. En `settled` se añade `settled_at`.
En `rejected` aparece `error{error_code, error_description}` (p. ej. `B101`) y se omiten
`amount` y `type`.

`destination_key` y `qr_code_reference` son precisamente los campos que materializan lo
que Gustavo definió como pago entrante.

**Campos obligatorios: NO DOCUMENTADO.** La página no marca ningún campo como requerido.

### 1.4 Cabeceras

| Cabecera | Contenido |
|---|---|
| `X-Passport-Signature-V2` | Firma HMAC-SHA256 |
| `X-Passport-Timestamp` | Marca de tiempo incluida en el mensaje firmado |

Ninguna otra cabecera está documentada.

### 1.5 Esquema de firma V2

Mensaje firmado: `<timestamp>.<canonical_body_bytes>`

donde el cuerpo canónico es una **re-serialización** del JSON con claves ordenadas y sin
espacios — la documentación lo expresa como
`json.dumps(payload, sort_keys=True, separators=(',', ':')).encode()`.

La página es explícita en que V2 **no firma los bytes crudos transmitidos** sino esa
re-serialización canónica, y en que **V2 no incluye nonce**, por lo que la protección
contra reenvío recae en validar `X-Passport-Timestamp`.

Verificación documentada: extraer el timestamp, re-serializar el payload en forma
canónica, construir `<timestamp>.<cuerpo canónico>`, calcular HMAC-SHA256 con el Secret
Token, comparar con la cabecera y rechazar timestamps antiguos.

### 1.6 Reintentos

`Delivery Mode` admite `Once` o `Retry`. En modo `Retry`: hasta **12** intentos con
retroceso exponencial — 1 s, 2 s, 4 s, 8 s… El código de respuesta que Passport considera
exitoso **NO está documentado en la página**.

### 1.7 Registro del callback

Autoservicio en el Dashboard: **Platform > Webhooks > Create Webhook +**. Se indica URL de
callback (HTTPS y accesible públicamente), Secret Token, Delivery Mode, activación y
selección de eventos. Se admiten múltiples webhooks, pero **un mismo evento no puede
dirigirse a varios endpoints**. No hay API documentada para registrarlo.

---

## 2. Aclaraciones de Gustavo Muñoz (CONFIRMADO_GUSTAVO)

1. Documentación aplicable: la citada arriba.
2. **Pago entrante = «pago hacia la llave de XPAY o QR»**. No corresponde al ciclo del pago
   saliente ya certificado.
3. **Sandbox**: «puede ser cualquier string en el campo del token de secreto».
4. **Producción**: «se espera un esquema HMAC».
5. **Respuesta y reintentos**: «no se espera respuesta por parte de su endpoint, sin
   embargo, para los reintentos si detectamos algo que no sea 200, se realiza el
   reintento.»

De (5) se deriva el único requisito operativo de respuesta: **devolver HTTP 200** cuando el
webhook se recibe y procesa correctamente. No se infiere nada más.

---

## 3. Contrato final consolidado

| Aspecto | Valor | Origen |
|---|---|---|
| Significado de pago entrante | Pago hacia llave XPAY o QR | CONFIRMADO_GUSTAVO |
| Eventos a tratar | `payment.inbound.received` / `.confirmed` / `.settled` / `.rejected` | DOCUMENTADO_PASSPORT |
| Método HTTP | **NO DOCUMENTADO** — se implementará `POST` | INFERENCIA |
| Identificador de pago | `payment_id` | DOCUMENTADO_PASSPORT |
| Identificador de evento/entrega | **NO EXISTE** campo documentado | DOCUMENTADO_PASSPORT |
| Nombre del evento | Campo `Event` (E mayúscula) | DOCUMENTADO_PASSPORT |
| Cabecera de firma | `X-Passport-Signature-V2` | DOCUMENTADO_PASSPORT |
| Cabecera de timestamp | `X-Passport-Timestamp` | DOCUMENTADO_PASSPORT |
| Algoritmo | HMAC-SHA256 sobre `<timestamp>.<cuerpo canónico>` | DOCUMENTADO_PASSPORT |
| Secreto en Sandbox | Cualquier string, definido por XPAY al crear el webhook | CONFIRMADO_GUSTAVO |
| Esquema en Producción | HMAC obligatorio | CONFIRMADO_GUSTAVO |
| Respuesta esperada | `200` | CONFIRMADO_GUSTAVO |
| Reintentos | Hasta 12, retroceso exponencial, si la respuesta no es 200 | DOCUMENTADO_PASSPORT + CONFIRMADO_GUSTAVO |
| Registro de callback | Dashboard, acción manual | DOCUMENTADO_PASSPORT |

---

## 4. Payload relevante para T6/T7

Para la evidencia mínima bastan, del evento entrante:

`Event` · `payment_id` · `status` · `created_at` · `updated_at` · `destination_key` ·
`qr_code_reference` · `end_to_end_identification` · `error.error_code` (sólo en
`rejected`).

Los bloques `sender` y `receiver` contienen **PII y datos bancarios** —nombres, tipo y
número de identificación, tipo y número de cuenta—. No deben persistirse en claro: la
tabla de destino fue diseñada desde el origen con ese criterio.

---

## 5. Seguridad — Sandbox

El Secret Token es un valor elegido por XPAY al crear el webhook en el Dashboard. Se lee de
variable de entorno, nunca del repositorio ni de `appsettings.json`, siguiendo el patrón ya
establecido para Veriff y Passport.

Variable propuesta: `PASSPORT_WEBHOOK_SECRET`.

**Ambigüedad documental:** la página no indica si `X-Passport-Signature-V2` se envía
también en Sandbox. Se resuelve por diseño, sin degradar producción: el endpoint calcula y
**registra** siempre el resultado de la verificación en `signature_valid`, y el rechazo por
firma inválida se gobierna con una variable de modo explícita.

Variable propuesta: `PASSPORT_WEBHOOK_SIGNATURE_MODE` ∈ {`ENFORCE`, `OBSERVE`}, con
**`ENFORCE` por defecto**. `OBSERVE` sólo es admisible en Sandbox y debe quedar registrado
en la evidencia cuando se use.

---

## 6. Seguridad — Producción

`ENFORCE` obligatorio. Firma ausente o inválida ⇒ **401**, sin cambio de estado y con
auditoría — exactamente el criterio ya aplicado en `KycController.VeriffWebhook`.

Requisitos técnicos de la verificación:

1. Leer el cuerpo **crudo** antes de cualquier *model binding* (`Request.EnableBuffering()`).
2. Deserializar y **re-serializar canónicamente**: claves ordenadas, separadores `,` y `:`
   sin espacios.
3. Construir `<timestamp>.<cuerpo canónico>` y calcular HMAC-SHA256 con el secreto.
4. Comparar en **tiempo constante** (`CryptographicOperations.FixedTimeEquals`).
5. Rechazar `X-Passport-Timestamp` fuera de una ventana de tolerancia (V2 no tiene nonce).

> **Riesgo técnico principal de todo el ticket.** La firma no se calcula sobre los bytes
> recibidos sino sobre una re-serialización, de modo que la implementación en .NET debe
> reproducir la serialización de Python con fidelidad exacta: orden de claves, ausencia de
> espacios, escapado de caracteres no ASCII (`json.dumps` escapa a `\uXXXX` por defecto) y
> formato de números. Un solo carácter de diferencia invalida la firma. Esto **no es una
> ambigüedad del contrato** —está documentado— sino un requisito de precisión que debe
> cubrirse con pruebas unitarias sobre vectores fijos antes de exponer el endpoint.

---

## 7. Respuesta HTTP

| Situación | Respuesta | Motivo |
|---|---|---|
| Webhook válido, procesado | **200** | CONFIRMADO_GUSTAVO: lo distinto de 200 provoca reintento |
| Webhook válido ya procesado (duplicado) | **200** | Detiene el reintento; no se reprocesa |
| Firma inválida o ausente en `ENFORCE` | **401** | Provoca reintento deliberadamente; no se acepta lo no autenticado |
| Error interno al persistir | **500** | Provoca reintento, que es el comportamiento deseado |

Passport no espera cuerpo de respuesta. Se devolverá un cuerpo mínimo no sensible.

---

## 8. Reintentos

Hasta 12 entregas del **mismo** evento ante cualquier respuesta distinta de 200. La
consecuencia de diseño es directa: **el endpoint debe ser idempotente**, porque un 500
transitorio o un despliegue en curso generarán entregas repetidas legítimas.

---

## 9. Idempotencia

Passport **no publica ningún identificador de entrega ni de evento**, y no documenta
tratamiento de duplicados. La clave de deduplicación debe construirse.

`payment_id` **no basta por sí solo**: los tres eventos del flujo entrante
(`received`, `confirmed`, `settled`) comparten el mismo `payment_id` y son entregas
legítimamente distintas.

**Clave de deduplicación adoptada: `(event_type, passport_payment_id)`.** Es estable entre
reintentos de una misma transición y distingue correctamente las transiciones entre sí.
`payload_hash` (SHA-256 hex del cuerpo crudo) se conserva como evidencia y verificación
secundaria, pero no como clave primaria de deduplicación, porque un reintento podría traer
un `updated_at` distinto.

Regla: si ya existe una fila con esa clave y `processed = 1`, responder **200** sin
reprocesar, dejando constancia de la entrega duplicada.

---

## 10. Mapeo al código XPAY

### 10.1 Reutilizable tal cual

| Componente | Uso |
|---|---|
| `KycController.VeriffWebhook()` | **Patrón** de endpoint: `EnableBuffering`, lectura de cuerpo crudo, HMAC-SHA256, comparación en tiempo constante, 401 sin cambio de estado, auditoría que nunca registra secretos ni PII |
| `KycService.ValidateVeriffSignature()` | **Patrón** de verificación: `HMACSHA256`, `secret.Trim()` para tolerar espacios de Azure App Settings, diagnóstico que sólo registra longitudes y formato |
| Tabla `passport_webhook_events` | Persistencia — **apta sin migración** |
| Modelo `PassportWebhookEvent` | Mapea 1:1 con la tabla |

### 10.2 Idoneidad de `passport_webhook_events`

Definida en `database/010_passport_breb_base.sql`, diseñada explícitamente para no guardar
datos bancarios ni PII en claro.

| Columna | Uso en T6/T7 |
|---|---|
| `event_type` | Campo `Event` del payload |
| `passport_payment_id` | `payment_id` |
| `id_breb_retiro` | **NULL** — es anulable; un pago entrante no tiene retiro asociado |
| `payload_hash` | SHA-256 hex del cuerpo crudo (la columna es `NVARCHAR(64)`, exacta para hex) |
| `payload_sanitized_json` | Payload sin `sender`/`receiver` en claro |
| `signature_valid` | Resultado de la verificación HMAC |
| `processed`, `fecha_procesado` | Control de idempotencia |
| `error_message` | `error.error_description` en rechazos, u error interno |

**`DB_MIGRATION_REQUIRED=NO`.**

Endurecimiento **opcional y no aplicado** (sólo se reporta, conforme al ticket): los tres
índices existentes (`IX_pwh_payment`, `IX_pwh_retiro`, `IX_pwh_processed`) **no son
únicos**. Un índice único filtrado sobre `(event_type, passport_payment_id)` haría la
deduplicación segura ante entregas concurrentes. No es necesario para la evidencia T6/T7,
donde la comprobación previa a la inserción es suficiente, y **no se propone ejecutarlo
ahora**.

### 10.3 Lo que NO debe reutilizarse — hallazgo de este ticket

**`BrebPaymentStateMachine` no sirve para el flujo entrante y no debe tocarse.** Dos
razones independientes:

1. Sus constantes son `PENDING`, `PROCESSING`, `SETTLED`, `REJECTED`. **No contempla
   `CONFIRMED`**, que sí existe en el flujo entrante, de modo que `Classify("CONFIRMED")`
   devolvería `Desconocido`. Añadirlo modificaría una clase compartida con el flujo de
   producto de retiros.
2. `Decide(...)` y `ApplyPassportPaymentStatusAsync` exigen un `idBrebRetiro` real y
   persistido —lanzan excepción si no existe— y son la única ruta del proyecto que mueve
   Wallet/Ledger. Ratifica el hallazgo del preflight XPAY-580 §7.

Por tanto T6/T7 usarán una **clasificación propia y aislada**, basada en el nombre del
evento `payment.inbound.*`, sin tocar la máquina de estados del producto. Es el mismo
criterio de **aislamiento arquitectónico** vigente en todo M5 desde XPAY-529.

`WALLET_LEDGER_CHANGE_REQUIRED_FOR_T6_T7=NO`.

### 10.4 Lo que no existe todavía

No hay ningún endpoint de webhook de Passport en el proyecto: la búsqueda en
`Controllers/` no arrojó ninguna coincidencia. Hay que crearlo.

---

## 11. Implementación mínima (especificada, NO ejecutada)

1. **`PassportWebhookController`**, ruta `POST /api/passport/webhook`, siguiendo la
   convención existente (`[Route("api/kyc")]` en `KycController`).
2. Lectura del cuerpo crudo con `Request.EnableBuffering()` antes de cualquier binding.
3. **`IPassportWebhookSignatureValidator`** — abstracción con una sola implementación
   gobernada por `PASSPORT_WEBHOOK_SIGNATURE_MODE`, que soporta Sandbox y Producción sin
   activar producción ni relajar su exigencia. Calcula HMAC-SHA256 sobre
   `<X-Passport-Timestamp>.<cuerpo canónico>`, compara en tiempo constante y valida la
   ventana temporal.
4. **Serializador canónico** dedicado y cubierto por pruebas con vectores fijos: claves
   ordenadas, separadores sin espacios, escapado `\uXXXX` de no-ASCII.
5. **Parser del evento entrante**: extrae `Event`, `payment_id`, `status`, `created_at`,
   `updated_at`, `destination_key`, `qr_code_reference`, `end_to_end_identification` y
   `error.error_code`. Clasificador aislado por nombre de evento.
6. **Sanitizador de payload**: elimina o enmascara `sender` y `receiver` antes de persistir.
7. **Persistencia idempotente** en `passport_webhook_events` con la clave de §9.
8. **Respuesta** según la tabla de §7.
9. **Auditoría** con `LogSensitiveAction`, registrando nombres de cabecera, longitudes y
   resultado — nunca secreto, firma, cuerpo crudo ni PII.
10. **Pruebas unitarias offline**: vectores de firma canónica, duplicado que devuelve 200 sin
    reprocesar, firma inválida que devuelve 401, y los cuatro eventos `payment.inbound.*`.

**Prohibido en esta implementación:** invocar `ApplyPassportPaymentStatusAsync`, tocar
Wallet, Ledger, `breb_retiros` o cualquier movimiento de saldo.

---

## 12. Pasos posteriores para obtener la evidencia T6/T7

1. Implementar lo anterior en un ticket propio, con pruebas offline en verde.
2. Desplegar a un entorno con HTTPS y accesible públicamente. La documentación lo exige
   para el callback.
3. **`CALLBACK_REGISTRATION=MANUAL_PROVIDER_ACTION`** — registrar el webhook en el Dashboard
   de Passport: Platform > Webhooks > Create Webhook +, indicando la URL, el Secret Token,
   Delivery Mode y los eventos `payment.inbound.*`. **No es automatizable desde código y no
   se ejecuta en este ticket.** Recordar que un mismo evento no puede apuntar a dos
   endpoints.
4. Provocar un pago entrante real hacia la llave o el QR de XPAY en Sandbox. **Requiere
   autorización expresa: es una operación financiera y queda fuera de XPAY-593.**
5. Capturar como evidencia T6 la recepción de la entrega y como evidencia T7 la validación
   del payload y del estado, con la firma verificada. Sanitizar `sender`/`receiver`,
   secretos y firmas antes de adjuntar.

---

## 13. Impacto sobre T8

Se ratifica la recomendación del preflight XPAY-580 §8: `T8_AFTER_T6_T7=NEW_RETRIEVE_RECOMMENDED`.

Una vez exista una entrega real de webhook entrante, T8 gana fuerza demostrativa si es un
`GET` **fresco** ejecutado **después** de esa entrega, evidenciando que la API y el webhook
coinciden sobre el mismo `payment_id`. Sigue siendo una recomendación de diseño: **no
autoriza ninguna llamada ahora**.

---

## Seguridad de este documento

No contiene secretos, tokens, firmas, identificadores crudos, cuentas bancarias ni PII. Los
únicos valores reproducidos son nombres de campo, nombres de evento, nombres de cabecera y
el código de error de ejemplo de la documentación pública. No se realizó ninguna llamada a
la API de Passport, ninguna operación financiera y ninguna modificación de código.
