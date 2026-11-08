# XPAY — Passport / Bre-B — M5-T6 / M5-T7 — Implementación del webhook entrante

**Ticket:** XPAY-594 · **Fecha:** 2026-09-29 (UTC)
**Base contractual:** `M5-T6-T7-WEBHOOK-CONTRATO-FINAL.md` (XPAY-593, cerrado)
**Estado:** implementado y probado OFFLINE · **no desplegado** · **callback no registrado**

Cero llamadas a la API de Passport, cero operaciones financieras, cero pagos, cero
migraciones, cero cambios en Azure, cero `stage`/`commit`/`push`.

---

## 1. Endpoint

```
POST /api/passport/webhook
```

`PassportWebhookController` (`[Route("api/passport")]`, `[HttpPost("webhook")]`,
`[AllowAnonymous]`). Lee el cuerpo **crudo** con `Request.EnableBuffering()` antes de
cualquier *model binding*, porque la verificación de firma necesita re-serializarlo en
forma canónica; después repone `Request.Body.Position = 0` para no romper el parseo
posterior.

El patrón estructural procede de `KycController.VeriffWebhook`, pero **no se acopla KYC con
Passport**: componentes, configuración y secreto son propios. Se verifica por prueba
ejecutable que el controller no recibe `KycService`, `IPassportHttpClient`,
`IPassportPaymentClient` ni `BrebPaymentService`.

---

## 2. Eventos

Clasificador aislado `PassportInboundEventClassifier`, que reconoce exactamente:

| Evento | Clase |
|---|---|
| `payment.inbound.received` | `Received` |
| `payment.inbound.confirmed` | `Confirmed` |
| `payment.inbound.settled` | `Settled` (terminal) |
| `payment.inbound.rejected` | `Rejected` (terminal) |

`payment.outbound.*` se clasifica como `NotInbound`; cualquier otro nombre, o su ausencia,
como `Unknown`. Ambos se registran para auditoría y **no se procesan**: reintentar no
cambiaría el resultado.

**No se reutiliza `BrebPaymentStateMachine`,** por las dos razones cerradas en XPAY-593:
no conoce `CONFIRMED` —lo clasificaría como desconocido— y sus métodos de decisión exigen
un `idBrebRetiro` real y son la única ruta del proyecto que mueve Wallet/Ledger. Hay una
prueba que falla si alguien introduce esa dependencia.

---

## 3. Autenticación

`IPassportWebhookSignatureValidator` / `PassportWebhookSignatureValidator`.

Material firmado, exactamente como lo documenta Passport:

```
HMAC-SHA256( secret , "<X-Passport-Timestamp>.<JSON_CANONICO>" )
```

Decisiones de implementación:

- El **timestamp se usa verbatim** tal como llegó. No se re-formatea: cualquier
  normalización cambiaría el material firmado. Sólo se interpreta —aparte— para la ventana
  anti-reenvío, admitiendo epoch en segundos, epoch en milisegundos e ISO-8601, porque
  Passport no documenta su formato.
- La **codificación del valor de la cabecera de firma no está documentada**. Se aceptan
  hexadecimal y base64, siempre con `CryptographicOperations.FixedTimeEquals`. No hay
  debilitamiento: un atacante debe acertar el HMAC completo en cualquiera de las dos.
- El secreto se lee del entorno con `.Trim()`, misma defensa que `KycService`, para que un
  espacio final en Azure App Settings no rompa la firma.

### Configuración

| Variable | Uso |
|---|---|
| `PASSPORT_WEBHOOK_SECRET` | Secret Token definido por XPAY al crear el webhook |
| `PASSPORT_WEBHOOK_SIGNATURE_MODE` | `ENFORCE` (defecto) o `OBSERVE` |
| `PASSPORT_WEBHOOK_TIMESTAMP_TOLERANCE_SECONDS` | Ventana anti-reenvío, defecto 300 |

Ninguna está configurada en ningún ambiente. Ningún secreto real aparece en el código, en
las pruebas, en `appsettings` ni en esta documentación; los valores de prueba son
sintéticos.

### Sandbox frente a Producción

**Producción: `ENFORCE`.** Firma ausente, inválida, sin secreto configurado, timestamp
ausente, no interpretable o fuera de ventana ⇒ **401**, sin persistir nada.

**Sandbox: `OBSERVE` disponible.** La documentación de Passport **no confirma** que
`X-Passport-Signature-V2` se envíe en Sandbox, y Gustavo confirmó que allí el Secret Token
puede ser cualquier cadena. `OBSERVE` permite recibir la entrega registrando
`signature_valid = 0` cuando no pudo verificarse, sin rechazarla.

La diferencia se mantiene **explícita** y no degrada producción:

- el valor por defecto es `ENFORCE`;
- **cualquier** valor no reconocido cae en `ENFORCE` — un modo permisivo nunca se obtiene
  por omisión ni por error tipográfico, y hay una prueba por cada variante probada;
- en `OBSERVE`, una firma que sí valida se sigue reportando como válida;
- el uso de `OBSERVE` debe declararse en cualquier evidencia de certificación.

`TIMESTAMP_TOLERANCE_SOURCE = XPAY_CONFIGURABLE_DEFAULT`. Passport no fija ningún valor:
sólo indica que V2 no incluye nonce y que debe revisarse el timestamp. Los 300 s son una
elección conservadora de XPAY, configurable, congelada por una prueba para que un cambio
silencioso sea visible. **No debe atribuirse al proveedor.** No se inventó ningún nonce.

---

## 4. Canonicalización

`PassportWebhookCanonicalJson` reproduce literalmente la implementación de referencia que
el contrato cita —`json.dumps(payload, sort_keys=True, separators=(',', ':')).encode()`—
en los cuatro puntos donde una versión .NET aproximada divergiría:

1. **Orden de claves por punto de código Unicode**, no por unidad UTF-16. Hay una prueba
   con un par suplente que demuestra que `string.CompareOrdinal` daría el orden contrario.
2. **Separadores `,` y `:` sin espacios.**
3. **`ensure_ascii=True`**: todo carácter fuera de `0x20..0x7E` se escapa como `\uXXXX` en
   hexadecimal minúscula; los suplementarios, como par suplente. La barra inclinada no se
   escapa; `0x7F` sí.
4. **Números**: un token sin `.`, `e` ni `E` es entero y se conserva literal; cualquier
   otro se re-emite con el `repr()` de Python, incluido su umbral exacto de notación
   exponencial (`exp < -4` o `exp >= 16`). Es la divergencia concreta frente al formato
   `"R"` de .NET, que escribiría `1E+15` donde Python escribe `1000000000000000.0`.

Ningún payload documentado por Passport contiene números JSON desnudos —todos los valores
de sus ejemplos son cadenas u objetos—, por lo que la ruta numérica es **defensiva**; se
implementa igualmente para no dejar un agujero silencioso.

---

## 5. Idempotencia

**Clave lógica: `(event_type, passport_payment_id)`.**

`payment_id` por sí solo no sirve: `received`, `confirmed` y `settled` lo comparten y son
entregas legítimamente distintas. `payload_hash` (SHA-256 hex del cuerpo crudo) es
**evidencia secundaria**, nunca identidad primaria, porque un reintento puede traer un
`updated_at` distinto y por tanto otro hash para la misma entrega.

Comportamiento:

| Situación | Resultado |
|---|---|
| Primera entrega | Se inserta con `processed = 1` |
| Misma clave ya procesada | `Duplicate`: no se reprocesa, no se inserta otra fila, se conserva el hash original |
| Misma clave registrada pero **no** procesada | Se completa sobre la misma fila en lugar de duplicarla |

### Limitación residual de concurrencia — declarada, no oculta

La tabla `passport_webhook_events` **no tiene índice único** sobre
`(event_type, passport_payment_id)`; sus tres índices (`IX_pwh_payment`, `IX_pwh_retiro`,
`IX_pwh_processed`) son no únicos, y **XPAY-594 no añade ninguna migración**. La protección
es por comprobación previa a la inserción en la capa de aplicación, que **no es atómica**:
dos entregas simultáneas del mismo evento pueden insertar dos filas.

El impacto está acotado y es de auditoría, no financiero: este flujo no mueve dinero, así
que una fila duplicada no produce doble acreditación ni ningún efecto contable. La prueba
`Concurrencia_RiesgoResidualAcotado_SinIndiceUnico` ejecuta dos procesamientos en paralelo
y afirma lo que realmente se garantiza —que no lanza y que al menos una queda procesada—
aceptando 1 o 2 filas. **No afirma una protección que no existe.**

Cierre recomendado para un ticket futuro: índice único filtrado sobre
`(event_type, passport_payment_id)`. Requiere migración y por tanto autorización expresa.

---

## 6. Respuestas HTTP

| Situación | Código |
|---|---|
| Entrega entrante válida y procesada | **200** |
| Duplicado ya procesado | **200** |
| Evento saliente o desconocido, registrado | **200** |
| Cuerpo no interpretable como JSON | **400** |
| Firma ausente o inválida en `ENFORCE` | **401** |
| Fallo interno real | **500** |

Passport reintenta ante cualquier respuesta distinta de 200, hasta 12 veces con retroceso
exponencial. Los 401 y 500 provocan ese reintento **deliberadamente**: no se acepta lo no
autenticado, y un fallo transitorio merece reintentarse. El 400 es una decisión explícita:
un cuerpo ilegible no permite identificar el evento ni construir la forma canónica, y
responder 200 a algo que no pudimos leer sería mentir sobre el procesamiento.

Ninguna respuesta devuelve información sensible: sólo `received`, `processed` y el nombre
del resultado.

---

## 7. Persistencia y privacidad

Única tabla escrita: `passport_webhook_events`. **Sin migración** — `DB_MIGRATION_REQUIRED`
ya era `NO` en XPAY-593.

| Columna | Contenido |
|---|---|
| `event_type` | Campo `Event` del payload (E mayúscula) |
| `passport_payment_id` | `payment_id` |
| `id_breb_retiro` | **NULL siempre** — un pago entrante no tiene retiro |
| `payload_hash` | SHA-256 hex del cuerpo crudo |
| `payload_sanitized_json` | Proyección sanitizada |
| `signature_valid` | Resultado de la verificación |
| `processed`, `fecha_procesado` | Control de idempotencia |
| `error_message` | Motivo cuando no se procesa |

**No se persiste ni se registra:** el cuerpo crudo, el secreto, la firma, el material HMAC,
la cabecera de autorización, ni los bloques `sender`/`receiver` —nombres, tipo y número de
identificación, tipo y número de cuenta—. Del `destination_key` se conserva `key_type` y un
booleano de presencia, **no su valor**, porque una llave Bre-B puede ser un teléfono, un
correo o un documento. Los registros sólo incluyen longitudes, formato y resultado.

Una entrega que falla la autenticación en `ENFORCE` **no se persiste**, para que un tercero
no pueda llenar la tabla de eventos.

---

## 8. Aislamiento financiero

`WALLET_MODIFIED=NO` · `LEDGER_MODIFIED=NO` · `BREB_RETIROS_MODIFIED=NO` ·
`FINANCIAL_BEHAVIOR_ADDED=NO`

`PassportWebhookService` recibe únicamente `XpayDbContext`, `TimeProvider` e `ILogger`, y
escribe una sola tabla. Verificado por pruebas ejecutables que fallarían ante una
regresión:

- el constructor no admite `BrebPaymentService`, `IPassportPaymentClient`,
  `IPassportHttpClient` ni `IPassportTokenProvider`;
- el código fuente real —con los comentarios retirados, para que nombrar algo al explicar
  su exclusión no cuente— no menciona `ApplyPassportPaymentStatusAsync`,
  `BrebPaymentStateMachine`, `BrebPaymentService`, `WalletMovimientos`,
  `LedgerMovimientos`, `PassportBrebRetiros`, `IPassportHttpClient` ni
  `IPassportPaymentClient`;
- ninguna otra colección del `DbContext` se toca;
- `IdBrebRetiro` se asigna `null` explícitamente.

El comportamiento financiero del producto —acreditación real de un pago entrante— sigue
siendo alcance de Fase 65 y permanece fuera de aquí.

---

## 9. Pruebas

Todas OFFLINE, sin red y sin llamadas a Passport.

```
FOCUSED_TESTS      = PASS 95/95
BACKEND_FULL_SUITE = PASS 1013/1013
```

| Archivo | Cobertura |
|---|---|
| `PassportWebhookCanonicalJsonTests` | 11 vectores fijos: objeto simple, claves fuera de orden, anidamiento, arrays, espacios en valores, no-ASCII (tildes y ñ), enteros, flotantes con el umbral exponencial exacto, vacíos, escapes, y el payload entrante real. Más idempotencia de la canonicalización y la prueba de orden por punto de código |
| `PassportWebhookSignatureValidatorTests` | Firma válida en hex y base64; indiferencia al formato del cuerpo; firma alterada, cuerpo alterado, secreto distinto, cuerpo ilegible; ausencias y fail-closed sin secreto; ventana anti-reenvío dentro, en el borde, fuera y configurable; tres formatos de timestamp; `OBSERVE` frente a `ENFORCE`; defecto `ENFORCE` y cuatro variantes no reconocidas |
| `PassportInboundEventClassifierTests` | Los cuatro eventos entrantes; los tres salientes como `NotInbound`; seis formas de desconocido; insensibilidad a mayúsculas y espacios; terminales |
| `PassportWebhookPayloadParserTests` | Campos de T7; entrante sin `payment_id`; saliente no procesable; rechazo con código de error; cuatro cuerpos inválidos; **ausencia de PII en el JSON sanitizado**; estabilidad del hash |
| `PassportWebhookContractTests` | Ruta y verbo; ausencia de acoplamiento con KYC y con clientes HTTP; **aislamiento financiero**; el cuerpo crudo no se persiste; `IdBrebRetiro` nulo |
| `PassportWebhookServiceIdempotencyTests` | Primera entrega; reintento idéntico; mismo `payment_id` con evento distinto; hash distinto para la misma clave; saliente y desconocido registrados sin procesar; concurrencia con riesgo residual acotado |

### Vectores de firma

`OFFICIAL_HMAC_TEST_VECTOR_AVAILABLE = NO`. La documentación de Passport describe el
algoritmo pero **no publica ningún vector con una firma calculada**. Los vectores de este
ticket están marcados como `XPAY_INTERNAL_TEST_VECTOR` en el propio código y se generaron
ejecutando la implementación de **referencia** que el contrato cita (el `json.dumps` de
Python más HMAC-SHA256) y fijando el resultado como literal. **No deben presentarse como
evidencia del proveedor.**

### Alcance real de las pruebas SQL

Las seis pruebas de idempotencia y la de concurrencia siguen el patrón SQL establecido del
repositorio: requieren `ConnectionStrings__XpayConnection`, **se saltan en local** si no
está definida y son **obligatorias en CI**. En esta ejecución local no se conectaron a
ninguna base de datos, así que se reportan como parte de los 1013 pero **su cuerpo SQL no
se ejecutó aquí**. No se introdujo ningún proveedor en memoria: el repositorio no tiene esa
infraestructura por decisión documentada y este ticket no la cambia.

---

## 10. Pendientes para Sandbox

En orden, y **ninguno ejecutado en XPAY-594**:

1. Desplegar a un entorno HTTPS accesible públicamente. Requiere autorización.
2. Configurar `PASSPORT_WEBHOOK_SECRET` y, si se decide, `PASSPORT_WEBHOOK_SIGNATURE_MODE`
   en App Settings. **No se tocó Azure.**
3. Registrar el callback en el Dashboard de Passport: Platform > Webhooks > Create Webhook,
   con la URL, el Secret Token, Delivery Mode y los eventos `payment.inbound.*`.
   `CALLBACK_REGISTRATION = MANUAL_PROVIDER_ACTION` — no automatizable desde código.
   Recordar que un mismo evento no puede apuntar a dos endpoints.
4. Provocar un pago entrante real hacia la llave o el QR de XPAY. **Es una operación
   financiera y exige autorización expresa.**
5. Capturar la evidencia T6 (recepción) y T7 (validación del payload y del estado con firma
   verificada), sanitizando antes de adjuntar.

Si en el paso 4 se opera en `OBSERVE`, debe declararse explícitamente en la evidencia que
la firma no fue verificada.

T8 sigue fuera de alcance, con la recomendación `NEW_RETRIEVE_RECOMMENDED` de XPAY-593.

---

## Seguridad de este documento

No contiene secretos, tokens, firmas, material HMAC, cuerpos crudos, identificadores reales
de pago, cuentas bancarias ni PII. Los únicos valores citados son nombres de campo, de
evento, de cabecera y de variable de entorno.
