# XPAY — Passport / Bre-B — M5-T7 — Despliegue del fix de lotes a QA

**Ticket:** XPAY-603 · **Fecha:** 2026-09-30 (UTC) · **Entorno:** QA
**Resultado:** desplegado y validado de forma no mutante · **sin nuevo pago**

```
DEPLOY_PERFORMED=YES   DEPLOY_TARGET=xpay-api-qa
HEALTH_CHECK=PASS      WEBHOOK_ROUTE_PRESENT=YES
SIGNATURE_MODE=ENFORCE (intacto)
PASSPORT_API_CALLS=0   PAYMENT_CALLS=0   RESOLVE_CALLS=0   RETRIEVE_CALLS=0
MANUAL_WEBHOOK_POSTS=0
```

---

## 1. Revisión del diff — `DIFF_REVIEW=PASS`

Cambios de **código desplegable** respecto a `285f5ac`, exactamente tres archivos:

| Archivo | Líneas |
|---|---|
| `Integrations/Passport/PassportWebhookPayload.cs` | 86 |
| `Controllers/PassportWebhookController.cs` | 44 |
| `Services/PassportWebhookService.cs` | 32 |

Más pruebas (`PassportWebhookBatchTests.cs` nuevo, dos archivos extendidos) y la evidencia de
XPAY-602 — ninguno entra en el artefacto publicado.

Verificado archivo por archivo que **no se tocó** lo que ya estaba demostrado contra tráfico
real:

```
SIGNATURE_VALIDATION_CHANGED=NO
CANONICALIZER_CHANGED=NO
```

`PassportWebhookCanonicalJson.cs`, `PassportWebhookSignatureValidator.cs`,
`IPassportWebhookSignatureValidator.cs`, `PassportWebhookOptions.cs` y
`PassportInboundEventClassifier.cs` están **intactos**.

```
WALLET_CHANGED=NO        LEDGER_CHANGED=NO       BREB_RETIROS_CHANGED=NO
PAYMENT_FLOW_CHANGED=NO  RESOLVE_FLOW_CHANGED=NO DB_MIGRATION_CREATED=NO
```

### Dos cambios ajenos, identificados y no desplegados

El árbol de trabajo arrastra modificaciones **preexistentes** a este frente, presentes desde
antes de estos tickets:

- `docs/WALLET_CAJA_CUADRE_FASE_70.4_DISENO.md` — documentación. Aparece al filtrar por
  «wallet», pero es un `.md`: no es código y no entra en el publish.
- `tools/Xpay.PassportSandboxHarness/*` — el harness es un proyecto de consola separado que
  el API no referencia, así que tampoco forma parte del artefacto.

No se descartaron ni se incluyeron. El publish contiene **59 archivos**, los mismos que en
XPAY-595.

---

## 2. Pruebas previas al despliegue

```
FOCUSED_TESTS      = PASS 127/127     SKIPPED=0
FULL_BACKEND_TESTS = PASS 1045/1045   SKIPPED=0
```

Coinciden exactamente con lo declarado por XPAY-602. Cero regresiones.

---

## 3. Build y despliegue

```
RELEASE_BUILD=PASS   (59 archivos publicados, zip 7,9 MB)
```

| Dato | Valor |
|---|---|
| Deployment id | `c28d599d-0743-4dc4-8b83-871ac170d459` |
| Inicio / fin (UTC) | 2026-09-30T21:40:29Z → 21:40:39Z |
| Resultado | `complete = True`, `OneDeploy` |

Sólo código. Mismo procedimiento de XPAY-595 (`az webapp deploy --type zip`).

---

## 4. Configuración: intacta

Comparación antes / después, siempre por **nombre**, nunca por valor:

| Elemento | Antes | Después |
|---|---|---|
| `PASSPORT_WEBHOOK_SIGNATURE_MODE` | `ENFORCE` | **`ENFORCE`** |
| Total de App Settings | 36 | **36** |
| `PASSPORT_WEBHOOK_SECRET` | presente | **presente** |
| Access Restrictions | `Allow all` | **`Allow all`** |

```
APP_SETTINGS_MODIFIED=NO   ACCESS_RESTRICTIONS_MODIFIED=NO
PASSPORT_DASHBOARD_MODIFIED=NO   PRODUCTION_MODIFIED=NO
```

Ningún secreto fue leído, impreso ni rotado.

---

## 5. Validación posterior — no mutante

| Comprobación | Resultado |
|---|---|
| `GET /health` | `200 Healthy` |
| `GET /api/version` | `200` · `XPAY API QA` · `0.1.0-mvp-qa` |
| `GET /api/passport/webhook` | **`405`** — ruta desplegada, POST-only |
| `HEAD /api/passport/webhook` | **`405`** |

Tras el despliegue el backend devolvió `502` durante unos dos minutos — arranque en frío del
App Service, idéntico a lo observado en XPAY-595. Se resolvió por sí solo. Se registra por
transparencia.

Se usó exclusivamente `GET`/`HEAD` sobre una ruta declarada POST-only: el `405` prueba que la
ruta está mapeada y, al no ser POST, la petición **no alcanza el handler**, de modo que no
puede verificar firma, parsear payload ni persistir nada. **No se envió ningún POST**, ni
Test Event, ni replay, ni webhook sintético.

### Límite honesto sobre la confirmación de versión

`/api/passport/health-config` existe pero exige autenticación (`401`), y no se intentó
autenticar. No hay ningún marcador en tiempo de ejecución que distinga el fix de lotes sin
enviar un POST, que este ticket prohíbe. La trazabilidad del código desplegado es por tanto
el `deployment id` más el hecho de que el zip se construyó del árbol de trabajo verificado en
§1 y §2. **No se afirma haber confirmado el fix en caliente.**

---

## 6. Reintentos automáticos: ninguno

```
POST_DEPLOY_AUTOMATIC_RETRY_OBSERVED=NO
AUTOMATIC_RETRY_HTTP_RESULT=NONE
AUTOMATIC_RETRY_SIGNATURE_VALID=NOT_OBSERVED
AUTOMATIC_RETRY_PROCESSED=NOT_OBSERVED
```

Observación **pasiva** de los logs del App Service: los **36** POST registrados son todos de
la ventana de XPAY-601 (21:25:18Z–21:25:21Z) y **ninguno** es posterior al despliegue de las
21:40:39Z. Totales sin cambio: 36 POST, 36 × HTTP 400, 0 × 200, 0 × 401, 0 eventos procesados.

Era lo esperable: las 36 entregas ocurrieron en una ráfaga de 3 segundos, así que el
presupuesto de reintentos de Passport para ese pago se agotó más de quince minutos antes del
despliegue. No se provocó ningún reintento ni se interfirió con ninguno.

En consecuencia **no existe todavía un candidato de evidencia T7**: no hay ninguna entrega con
HTTP 200 vinculable al ciclo de XPAY-601.

---

## 7. Estado

```
T6_STATUS=PASS            (evidencia de XPAY-601, sin cambios)
T7_STATUS=INCONCLUSIVE    (el fix está desplegado pero aún no ejercitado)
READY_FOR_T7_REAL_VALIDATION=YES
```

El endpoint corregido está vivo en QA y espera una entrega real. Validar T7 exige un **nuevo
pago**, que este ticket **no** ejecutó y que requiere autorización expresa. Con ella, la
secuencia debe volver a ser atómica —limpieza de artefactos, Resolve, configure, Payment— por
la ventana de 30 minutos de la resolución.

Ese pago también resolverá el riesgo residual declarado en XPAY-602: si Passport envuelve el
lote en un objeto contenedor en vez de un array desnudo, reaparecerá `PAYLOAD_UNPARSEABLE` y
lo sabremos en un solo intento.

---

## Seguridad de este documento

Sin secretos, tokens, firmas, cuerpos crudos, identificadores reales de pago o resolución,
llaves, cuentas ni PII. Los únicos valores citados son nombres de recurso, códigos HTTP,
conteos y el identificador de despliegue de Azure.
