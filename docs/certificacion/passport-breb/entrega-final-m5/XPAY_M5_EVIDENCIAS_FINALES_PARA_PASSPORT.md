# XPAY — Certificación Passport Bre-B
## Módulo 5 — Evidencias finales

Este documento completa el avance parcial que les enviamos el 26 de septiembre de 2026.
Contiene **únicamente las evidencias nuevas** generadas desde entonces; las que ya recibieron
no se repiten, y al final se listan para referencia.

Todas las ejecuciones se realizaron en **Sandbox**. Las evidencias adjuntas están saneadas:
los identificadores (resolución, pago, cuenta, cliente, clave) aparecen únicamente como huella
SHA-256 truncada, nunca en claro.

---

## 1. Resumen del módulo

| Requisito | Estado | Evidencia |
|---|---|---|
| T1 — Cliente y cuenta B | Cubierto con recursos existentes | — (enviado previamente) |
| T2 — Iniciar pago saliente (B → A) | Documentado | enviado previamente + `M5-T6-T7-01/02` |
| T3 — Consultar estado del pago | Documentado | enviado previamente |
| T4 — Ciclo de vida del pago | Documentado | enviado previamente + `M5-T8` |
| T5-A — `amount` faltante | Documentado, con salvedad | enviado previamente |
| T5-B — `account_id` faltante | Documentado, con salvedad | enviado previamente |
| **T5-C — Transacción > $12 millones** | **Ejecutado, no concluyente** | **`M5-T5-C-Over-Limit-Attempt.json`** |
| T5-D — Clave Bre-B suspendida | Documentado | enviado previamente |
| **T5-E — Clave Bre-B eliminada** | **Documentado** | **`M5-T5-E-01/02/03`** |
| T5-F — `resolution_id` incorrecto | Documentado | enviado previamente |
| **T6 — Recepción de webhook** | **Documentado** | **§4 de este documento + `M5-T6-T7-01/02`** |
| **T7 — Validación de webhook** | **Documentado** | **§4 de este documento** |
| **T8 — Consulta final del pago** | **Documentado** | **`M5-T8-Payment-Retrieve-Settled.json`** |

---

## 2. T5-E — Clave Bre-B eliminada

Preparamos una clave desechable nueva y exclusiva para este caso, la eliminamos, y luego
intentamos resolverla. Tres llamadas reales, una por fase:

| Fase | Operación | Resultado | Evidencia |
|---|---|---|---|
| 1 | `POST /v1/keys` (BCODE desechable) | clave creada, `status = ACTIVE` | `M5-T5-E-01-Key-Create.json` |
| 2 | `DELETE /v1/keys/{key_id}` | eliminación reconocida (204 No Content) | `M5-T5-E-02-Key-Delete.json` |
| 3 | `POST /v1/resolve-key` sobre esa misma clave | **HTTP 404**, sin resolución devuelta | `M5-T5-E-03-Deleted-Key-Resolve.json` |

Las tres evidencias comparten la misma huella de clave, de modo que puede verificarse que las
tres fases actuaron sobre el mismo recurso sin necesidad de exponer su valor.

No se utilizó ninguna clave preexistente: la clave de este caso fue creada para él y queda
eliminada.

*Nota sobre el formato de la respuesta del paso 3:* el 404 llegó sin un código de error
estructurado en el cuerpo (nuestro cliente lo registra como `generic_error` con mensaje vacío).
El estado HTTP 404 es la señal que recogemos como demostración.

---

## 3. T5-C — Transacción superior a $12 millones COP

**Ejecutamos el escenario.** No fue posible observar de forma concluyente la validación
específica del límite, por la razón que se detalla abajo.

| Dato | Valor |
|---|---|
| Operación | `POST /v1/payments/breb` |
| Monto enviado | **12.000.001,00 COP** (un peso por encima del umbral del Anexo) |
| Moneda | COP |
| Solicitud efectivamente enviada | **Sí** |
| Respuesta de Passport | **HTTP 400** |
| Código de error | `service_blocked` |
| Mensaje | «The account does not have enough available funds for this payment» |
| Resolución utilizada | vigente, obtenida momentos antes, sobre la clave positiva de certificación |

Evidencia: `M5-T5-C-Over-Limit-Attempt.json`

### Lectura de este resultado

Passport **rechazó** la solicitud, pero el motivo observado fue **saldo disponible
insuficiente** en la cuenta operativa de Sandbox, no el límite de monto de Bre-B. La validación
de fondos se aplica antes que la del límite, de modo que la solicitud no llegó a evaluarse contra
la regla de monto máximo.

Por eso clasificamos este caso como **no concluyente**, y no como superado:

- `REQUEST_EXECUTED = YES` — la solicitud se envió realmente y Passport respondió.
- `LIMIT_REJECTION_DEMONSTRATED = NO` — la respuesta no permite afirmar que se evaluara el límite.

No afirmamos que el rechazo por fondos demuestre la regla de límite, ni hemos reintentado la
operación. Adjuntamos la evidencia tal como se obtuvo.

Quedamos atentos por si requieren una validación adicional específica de este escenario.

---

## 4. T6 y T7 — Recepción y validación de webhooks

Implementamos y desplegamos el endpoint de webhook en nuestro entorno de QA, con el callback
registrado en el Dashboard de Sandbox y verificación de firma activa en modo estricto.

Sobre el pago documentado en `M5-T6-T7-01-Resolve.json` y `M5-T6-T7-02-Payment-Create.json`
(resolución y creación del pago, `status` inicial `PROCESSING`), Passport entregó los siguientes
eventos a nuestro endpoint:

| # | Hora (UTC) | Eventos en la entrega | Respuesta de nuestro endpoint |
|---|---|---|---|
| 1 | 21:50:01 | 1 evento (entrante, *received*) | **200** |
| 2 | 21:50:01 | 2 eventos (saliente + *settled*) | **200** |
| 3 | 21:50:04 | 2 eventos (saliente + *confirmed*) | **200** |

- **T6 — Recepción:** 3 de 3 entregas respondidas con HTTP 200 a la primera. No hubo reintentos.
- **T7 — Validación:** la firma de todas las entregas se validó correctamente y todos los
  cuerpos se interpretaron sin error. Los eventos se registraron de forma idempotente (cinco
  registros, sin duplicados).

Los eventos correspondientes a pagos **salientes** se reconocen, se responden con 200 y se
registran para auditoría, pero no alteran el estado del pago en nuestro sistema: ese estado
proviene de la API, no del webhook.

---

## 5. T8 — Consulta final del pago

Consulta posterior sobre el mismo pago de T6/T7:

| Dato | Valor |
|---|---|
| Operación | `GET /v1/payments/{payment_id}` |
| Estado devuelto | **`SETTLED`** |
| Moneda | COP |

Evidencia: `M5-T8-Payment-Retrieve-Settled.json`

La huella del pago coincide con la de su creación (`M5-T6-T7-02`), de modo que puede verificarse
que se trata del mismo pago y que el ciclo `PROCESSING → SETTLED` quedó cerrado por la vía de la
API.

---

## 6. Evidencias ya enviadas el 26 de septiembre (no se repiten)

Para referencia, el paquete anterior contenía:

| Archivo | Caso |
|---|---|
| `M5-T2-Resolve.json` | T2 — resolución de la clave destino |
| `M5-T2-Payment-Create.json` | T2 — creación del pago |
| `M5-T3-T4-Payment-Retrieve.json` | T3 / T4 — consulta y ciclo de vida |
| `M5-T5-A-Missing-Amount.json` | T5-A |
| `M5-T5-B-Missing-Account.json` | T5-B |
| `M5-T5-D-Suspended-Key.json` | T5-D |
| `M5-T5-F-Invalid-Resolution.json` | T5-F |
| `XPAY_M5_AVANCE_EVIDENCIAS_PARA_PASSPORT.md` | documento narrativo |

Si prefieren un paquete consolidado con todo el Módulo 5 en un solo envío, lo preparamos sin
problema.

### Salvedad ya señalada en ese envío, que mantenemos

En **T5-A** y **T5-B** la solicitud fue rechazada por la validación de campos obligatorios de
nuestro propio sistema, **antes** de llegar a Passport. No son respuestas de error de Passport.
Las incluimos porque demuestran que nuestro sistema no envía solicitudes incompletas, pero no las
presentamos como validación del proveedor.

---

## 7. Estado del módulo

Los casos técnicos del Módulo 5 están ejecutados y documentados. El único punto que no quedó
demostrado de forma concluyente es la regla de límite de T5-C, por la precondición de saldo del
entorno de Sandbox descrita en §3.

Quedamos atentos a su revisión.
