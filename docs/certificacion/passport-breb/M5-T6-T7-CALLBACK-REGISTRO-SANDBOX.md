# XPAY — Passport / Bre-B — M5-T6 / M5-T7 — Registro del callback en Sandbox

**Ticket:** XPAY-597 · **Fecha:** 2026-09-30 (UTC)
**Estado:** **el webhook YA EXISTÍA y está activo** — no hubo que crear nada
**Corrección:** este documento nació asumiendo que faltaba registrar el callback. Esa
premisa era falsa y queda corregida en §7.
**Acción:** manual en el Dashboard de Passport — no automatizable desde código

Cero pagos, cero webhooks provocados, cero llamadas a la API de Passport, cero cambios en
Azure, cero operaciones financieras.

---

## 1. Precondiciones verificadas antes del registro

| Comprobación | Resultado |
|---|---|
| Commit desplegado | `285f5acf4b1239d2f8e6bec526bffd0107841c69` |
| `DEPLOYED_CODE_MATCHES_COMMIT` | YES |
| `GET` sobre la URL de callback | `405` — ruta publicada y POST-only |
| Validación TLS de la URL | Correcta (`ssl_verify_result = 0`) |
| Redirecciones | 0 |
| Alcanzable públicamente por HTTPS | Sí, requisito del Dashboard |
| `PASSPORT_WEBHOOK_SIGNATURE_MODE` | **`ENFORCE`** |
| `PASSPORT_WEBHOOK_SECRET` | Presente y no vacío en `xpay-api-qa` — no rotado, no leído por la herramienta |

---

## 2. Valores esperados (comparados después contra el webhook real — ver §7)

Ruta del Dashboard: **Platform > Webhooks > Create Webhook +**

| Campo | Valor autorizado |
|---|---|
| **URL** | `https://xpay-api-qa.azurewebsites.net/api/passport/webhook` |
| **Secret Token** | `CONFIGURED_MANUALLY_NOT_DISPLAYED` — debe coincidir **exactamente** con `PASSPORT_WEBHOOK_SECRET` del App Service QA |
| **Delivery Mode** | `Retry` |
| **Eventos** | `payment.inbound.received`, `payment.inbound.confirmed`, `payment.inbound.settled`, `payment.inbound.rejected` |
| **Activación** | Habilitado |

Sin query strings. Sin `localhost`. Ninguna otra URL.

**No seleccionar `payment.outbound.*`.** Ese ciclo ya quedó certificado en M5-T2/T3/T4 y está
fuera del alcance de T6/T7. Si el Dashboard obligara técnicamente a elegir una categoría más
amplia que incluya los eventos salientes, **detenerse antes de guardar** y reportarlo.

### Sobre el Secret Token

El valor **no aparece en este documento, ni en ningún registro, ni pasó por la herramienta**.
Se introduce manualmente en el Dashboard leyéndolo de
Azure Portal → `xpay-api-qa` → Configuration → Application settings →
`PASSPORT_WEBHOOK_SECRET` → *Show value*.

`SECRET_ROTATED=NO`. Si el Secret Token del Dashboard no coincide exactamente con el App
Setting, **toda entrega firmada fallará la verificación** y, con `ENFORCE` activo, recibirá
401. La coincidencia es responsabilidad de quien ejecuta el registro.

---

## 3. Comprobación previa obligatoria en el Dashboard

La documentación de Passport establece que **un mismo evento no puede dirigirse a varios
endpoints**. Antes de crear el webhook hay que revisar la lista existente en
**Platform > Webhooks**: si ya hay un webhook que cubra alguno de los cuatro eventos
`payment.inbound.*`, crear otro provocará conflicto o desviará las entregas. En ese caso,
detenerse y decidir si se edita el existente en lugar de crear uno nuevo.

---

## 4. Consecuencia conocida de `Retry` con `ENFORCE`

Decisión tomada: `Delivery Mode = Retry`.

La documentación de Passport **no confirma** que `X-Passport-Signature-V2` se envíe en
Sandbox. Con `ENFORCE` activo y `Retry` seleccionado, si Passport no firma la entrega:

- XPAY responderá **401** y **no persistirá** nada;
- Passport reintentará **hasta 12 veces** con retroceso exponencial (1 s, 2 s, 4 s, 8 s…);
- el resultado será una ráfaga de doce 401 para un único pago.

Eso es ruidoso pero **inocuo**: no mueve dinero, no inserta filas y no altera datos. Y de
hecho constituye evidencia observable de que Passport reintenta ante respuestas distintas de
200, tal como confirmó Gustavo Muñoz.

**Reacción correcta si ocurre:** cambiar `PASSPORT_WEBHOOK_SIGNATURE_MODE` a `OBSERVE` **en
ese momento**, nunca de forma preventiva, y declararlo explícitamente en la evidencia de
T6/T7. No se cambia ahora.

---

## 5. Después de crear

Verificar **únicamente** que el webhook aparece registrado y activo en la lista de
Platform > Webhooks.

**No pulsar** `Test`, `Send test`, `Retry`, `Replay` ni `Trigger` si el Dashboard ofrece esas
opciones. No enviar ningún POST manual al endpoint. No provocar ninguna entrega. T6 y T7 se
obtienen con un pago entrante real, que requiere autorización expresa en un ticket posterior.

---

## 6. Estado

```
CALLBACK_URL=https://xpay-api-qa.azurewebsites.net/api/passport/webhook
INBOUND_EVENTS=payment.inbound.received|.confirmed|.settled|.rejected
DELIVERY_MODE=Retry
SECRET_TOKEN=CONFIGURED_MANUALLY_NOT_DISPLAYED
SIGNATURE_MODE=ENFORCE

CALLBACK_PREEXISTING=YES
CALLBACK_ACTIVE=YES
CALLBACK_URL_MATCH=YES
TEST_WEBHOOK_TRIGGERED=NO
MANUAL_WEBHOOK_POSTS=0
PASSPORT_PAYMENT_CALLS=0
RESOLVE_CALLS=0
RETRIEVE_CALLS=0
FINANCIAL_OPERATIONS=0
AZURE_MODIFIED=NO
DEPLOY_PERFORMED=NO
SECRET_ROTATED=NO
SECRET_VALUE_EXPOSED=NO
```

Estado real confirmado en §7: el webhook ya existía, está activo y no fue modificado.

---

## Seguridad de este documento

No contiene el Secret Token ni ningún otro secreto, token, firma, identificador real de pago,
cuenta bancaria ni PII.


---

## 7. Hallazgo XPAY-598 — el webhook YA EXISTÍA

**Fecha:** 2026-09-30 (UTC) · Confirmado manualmente por el usuario en el Dashboard de
Passport Sandbox.

**No hubo que crear ningún webhook.** Al abrir Platform > Webhooks apareció uno ya registrado
y activo apuntando exactamente al endpoint de QA. **No se pulsó `Update` y no se modificó
nada.**

```
CALLBACK_PREEXISTING=YES
CALLBACK_ACTIVE=YES
CALLBACK_URL_MATCH=YES
DELIVERY_MODE=Retry
INBOUND_EVENTS_CONFIGURED=YES
OUTBOUND_EVENTS_ALSO_CONFIGURED=YES
SECRET_MATCH_CONFIRMED_MANUALLY=YES
SECRET_VALUE_EXPOSED=NO
PASSPORT_DASHBOARD_MODIFIED=NO
```

### Configuración observada

| Campo | Valor observado | ¿Coincide con lo preparado en §2? |
|---|---|---|
| Callback URL | `https://xpay-api-qa.azurewebsites.net/api/passport/webhook` | **Sí, exacto** |
| Delivery Mode | `Retry` | **Sí** |
| Active webhook | `Yes` | Sí |
| Inbound payments | Received, Confirmed, Settled, Rejected | **Sí, los cuatro** |
| Outbound payments | Confirmed, Rejected, Settled | Adicional — ver nota |
| Test event | No seleccionado | Correcto: no se desea ninguna entrega de prueba |

### Sobre los eventos salientes

Los `payment.outbound.*` también están seleccionados. **No es un defecto y no se corrigió.**
Al contrario, es coherente con la arquitectura de M5: el pago certificado en T2 es un
**saliente B→A**, donde tanto el pagador (Cliente/Cuenta B) como el receptor (A) son
entidades de XPAY en Sandbox. Un único pago B→A genera por tanto **eventos salientes por el
lado de B y eventos entrantes por el lado de A**. Tener ambas familias suscritas es
exactamente lo que hace observable el ciclo completo.

El endpoint trata los `payment.outbound.*` de forma explícita y segura: los clasifica como
`NotInbound`, los registra para auditoría y **no los procesa** como T6/T7, devolviendo 200
para que Passport no reintente. Está cubierto por pruebas.

### Sobre el Secret Token

El usuario comparó **manualmente y de forma segura** el Secret Token del Dashboard contra
`PASSPORT_WEBHOOK_SECRET` del App Service QA. **Coincidieron exactamente.**

El valor **no fue enviado al chat, no fue leído por la herramienta, no fue impreso, no fue
hasheado y no fue modificado**. `SECRET_ROTATED=NO` · `SECRET_VALUE_EXPOSED=NO`.

Esto elimina el riesgo que quedaba abierto: con `ENFORCE` activo, una discrepancia de
secreto habría hecho fallar la verificación de toda entrega firmada.

---

## 8. Qué falta para la evidencia REAL de T6/T7

### Requisito oficial (Anexo, transcripción validada en XPAY-568R)

| Caso | Descripción oficial | Evidencia requerida |
|---|---|---|
| M5-T6 | Recibir webhook de pago entrante | Registros de entrega del webhook |
| M5-T7 | Validar payload y estado del webhook | Registros del webhook |

Es un requisito **mínimo**: basta una entrega HTTP real de Passport, recibida y validada, con
su registro. La ambigüedad que XPAY-580 había señalado —si «pago entrante» se refería al
webhook o al pago— **quedó resuelta por Gustavo**: «pago hacia la llave de XPAY o QR».

### La acción externa que produce el pago entrante

Un tercero debe pagar hacia una llave Bre-B o un QR de XPAY. En Sandbox ese tercero ya
existe dentro de la propia arquitectura de M5: **Cliente B / Cuenta B**, creados en M5-T1
precisamente como pagador del flujo B→A.

Por tanto la acción es: **crear un nuevo pago B→A dirigido a la llave o al QR de A**, esta
vez **con el webhook ya activo**. El pago de T2 se ejecutó antes de que el endpoint
existiera, así que no generó ninguna entrega capturable: **hace falta un pago nuevo**.

> **Eso es una operación financiera real.** `REAL_FINANCIAL_OPERATION_REQUIRED=YES`. Un
> `POST /v1/payments/breb` real contra Passport Sandbox. **Queda identificada y NO se
> ejecuta aquí**, conforme a la instrucción del ticket. Requiere autorización expresa.

### Precondición que probablemente falte: un destino activo

Todas las llaves documentadas en las evidencias locales terminaron deliberadamente
**suspendidas o eliminadas**, porque se usaron como fixtures de pruebas negativas
(M4-T3-A → suspendida; M4-T3-B → eliminada; M5-T5-D → suspendida). No consta localmente
ninguna llave de XPAY que siga **activa**.

Confirmarlo exigiría un `Resolve` o un `Retrieve` contra Passport, que este ticket prohíbe.
Por eso: `EXISTING_XPAY_KEY_OR_QR_AVAILABLE=UNKNOWN`, con la expectativa fundada de que
**haya que crear o activar un destino** (llave o QR) antes del pago. El harness ya tiene los
ejecutores necesarios para ambas cosas; **no se crea ninguno en este ticket**.

### Eventos esperados en una ejecución real

El contrato documenta el flujo entrante `received → confirmed → settled`, y `rejected` sólo
en caso de fallo. Para el Anexo **basta una entrega**, así que no se afirma que vayan a
llegar los cuatro. Lo esperable en un pago exitoso es una o más de:

- `payment.inbound.received` (estado `PROCESSING`)
- `payment.inbound.confirmed` (estado `CONFIRMED`)
- `payment.inbound.settled` (añade `settled_at`)

y, por el lado del pagador, los `payment.outbound.*` correspondientes, que el endpoint
registrará sin procesarlos.

### Cómo verificar sin exponer PII ni secretos

La verificación se hace sobre `passport_webhook_events`, **no sobre el cuerpo crudo**, que
nunca se persiste. Basta consultar, para el `payment_id` de la prueba: `event_type`,
`signature_valid`, `processed`, `fecha_recibido`, `fecha_procesado` y
`payload_sanitized_json` —que por construcción ya excluye `sender`, `receiver` y el valor de
la llave destino—. Los bloques de PII y datos bancarios nunca llegan a disco.

### Riesgo operativo vigente

`PASSPORT_WEBHOOK_SIGNATURE_MODE=ENFORCE` y `Delivery Mode=Retry`. Si Passport **no** firma
las entregas en Sandbox, la primera entrega recibirá 401 y Passport reintentará hasta 12
veces. Si eso ocurre: **registrar el hecho, no repetir el pago, no crear otro, no manipular
datos y detenerse.** La decisión sobre pasar a `OBSERVE` se tomará entonces, con evidencia
real en la mano.

```
READY_FOR_T6_T7_CONTROLLED_EXECUTION=NO
```
Bloqueado por dos cosas, en este orden: confirmar o habilitar un destino activo (llave o QR)
de XPAY, y la autorización expresa para ejecutar un pago real.
