# XPAY — Passport / Bre-B — M5-T6 / M5-T7 — Despliegue controlado a QA

**Ticket:** XPAY-595 · **Fecha:** 2026-09-30 (UTC)
**Base:** XPAY-593 (contrato) y XPAY-594 (implementación), ambos cerrados
**Estado:** endpoint **desplegado y publicado en QA** · callback **NO registrado** · cero
operaciones financieras

---

## 1. Entorno

| Elemento | Valor |
|---|---|
| Ambiente | **QA** (nunca producción) |
| Suscripción | `Suscripción de Azure 1` |
| Resource Group | `rg-xpay-qa` |
| App Service | `xpay-api-qa` |
| URL backend | `https://xpay-api-qa.azurewebsites.net` |
| Dominio propio | `https://api-qa.xpaycol.com` |
| Runtime | `DOTNETCORE 8.0`, Always On activo |

**Confirmación de identidad previa a cualquier cambio.** Se enumeraron todos los App
Services de la suscripción: sólo `xpay-api-qa` y `xpay-admin-qa` pertenecen a
`rg-xpay-qa`, y **no existe ningún recurso XPAY de producción**. Los demás recursos de la
suscripción corresponden a proyectos ajenos y no fueron tocados. No se creó infraestructura
nueva.

---

## 2. Auditoría del diff previa al despliegue

`DIFF_REVIEWED=YES` · `UNEXPECTED_CHANGES_FOUND=NO` (para el alcance de XPAY-594)

Rama `main`, `HEAD = d209d5b`. El delta de XPAY-594 desplegado es exactamente:

  - `backend/Xpay.Api/Program.cs` (modificado)
  - `backend/Xpay.Api.Tests/Controllers/PassportWebhookContractTests.cs` (nuevo)
  - `backend/Xpay.Api.Tests/Integrations/Passport/PassportInboundEventClassifierTests.cs` (nuevo)
  - `backend/Xpay.Api.Tests/Integrations/Passport/PassportWebhookCanonicalJsonTests.cs` (nuevo)
  - `backend/Xpay.Api.Tests/Integrations/Passport/PassportWebhookPayloadParserTests.cs` (nuevo)
  - `backend/Xpay.Api.Tests/Integrations/Passport/PassportWebhookSignatureValidatorTests.cs` (nuevo)
  - `backend/Xpay.Api.Tests/Services/PassportWebhookServiceIdempotencyTests.cs` (nuevo)
  - `backend/Xpay.Api/Controllers/PassportWebhookController.cs` (nuevo)
  - `backend/Xpay.Api/Integrations/Passport/IPassportWebhookSignatureValidator.cs` (nuevo)
  - `backend/Xpay.Api/Integrations/Passport/PassportInboundEventClassifier.cs` (nuevo)
  - `backend/Xpay.Api/Integrations/Passport/PassportWebhookCanonicalJson.cs` (nuevo)
  - `backend/Xpay.Api/Integrations/Passport/PassportWebhookOptions.cs` (nuevo)
  - `backend/Xpay.Api/Integrations/Passport/PassportWebhookPayload.cs` (nuevo)
  - `backend/Xpay.Api/Integrations/Passport/PassportWebhookSignatureValidator.cs` (nuevo)
  - `backend/Xpay.Api/Services/PassportWebhookService.cs` (nuevo)

Verificado: **una sola** modificación en archivo versionado —el registro de DI en
`Program.cs`—; **ninguna migración** (`database/` sin cambios); **Wallet, Ledger y
breb_retiros intactos** en código; sin secretos ni PII en los archivos nuevos —los únicos
literales numéricos son valores sintéticos de prueba y un vector HMAC interno etiquetado
como tal—; y **ningún artefacto de MiDecisor/DataCrédito** incluido.

El árbol de trabajo contiene además cambios **preexistentes y ajenos** a este frente
(`docs/WALLET_CAJA_CUADRE_FASE_70.4_DISENO.md`,
`tools/Xpay.PassportSandboxHarness/HarnessApp.cs`, `HarnessDecision.cs` y documentación
de certificación sin versionar). **No se descartaron, limpiaron ni desplegaron como cambio
propio**, conforme a la instrucción del ticket: no impidieron aislar XPAY-594.

### Trazabilidad — limitación declarada

`COMMIT_PERFORMED=NO` · `PUSH_PERFORMED=NO`. El despliegue es por paquete zip desde una
compilación local, mecanismo establecido del repositorio, que **no requiere commit**, y el
ticket sólo lo autorizaba si fuera necesario. Consecuencia: **QA ejecuta código que aún no
está en git.** El delta queda enumerado arriba para que el estado desplegado sea
reconstruible, pero la evidencia de certificación T6/T7 no podrá apuntar a un SHA hasta que
se cree el commit. Se recomienda crearlo antes de registrar el callback.

---

## 3. Build y pruebas previas al despliegue

```
PREDEPLOY_BUILD    = PASS   (Release, 0 advertencias, 0 errores)
FOCUSED_TESTS      = PASS   95/95
BACKEND_FULL_SUITE = PASS   1013/1013
```

Todo offline, sin red y sin llamadas a Passport. Las pruebas SQL de idempotencia siguen el
patrón del repositorio y se saltan en local por ausencia de cadena de conexión: son
obligatorias en CI.

---

## 4. Configuración requerida

Verificada por **nombre**, nunca por valor.

| App Setting | Estado |
|---|---|
| `PASSPORT_WEBHOOK_SECRET` | **Presente y con valor** — preexistía |
| `PASSPORT_WEBHOOK_SIGNATURE_MODE` | **Configurado en `OBSERVE`** por este ticket |
| `PASSPORT_WEBHOOK_TIMESTAMP_TOLERANCE_SECONDS` | Ausente → default de XPAY, 300 s |

### El secreto no se regeneró — y por qué

El ticket preveía generar un valor aleatorio nuevo. Al auditar la configuración,
`PASSPORT_WEBHOOK_SECRET` **ya existía y no estaba vacío**. Sobrescribirlo habría sido un
acto destructivo sin beneficio: el requisito ya estaba satisfecho, y si ese valor ya fuera
el que se piensa introducir en el campo Secret Token del Dashboard de Passport,
reemplazarlo rompería la correspondencia antes de registrar el callback. Por tanto **se
conservó**. `PASSPORT_WEBHOOK_SECRET_CONFIGURED=YES`.

En ningún momento se imprimió, registró ni derivó su valor: sólo se comprobó por booleano
que no estuviera vacío. Si se desea rotarlo, es una decisión explícita del responsable y
debe hacerse **antes** de registrar el callback, no después.

### Modo de firma

`PASSPORT_WEBHOOK_SIGNATURE_MODE = OBSERVE`, el modo Sandbox previsto por XPAY-594. Es
necesario porque la documentación de Passport **no confirma** que
`X-Passport-Signature-V2` se envíe en Sandbox.

El default seguro de producción **no se alteró**: en el código sigue siendo `ENFORCE`, y
cualquier valor no reconocido cae en `ENFORCE`. `OBSERVE` es un opt-in explícito, aquí
limitado al App Service de QA.

> **Exposición que esto introduce, declarada.** El App Service de QA tiene una única regla
> de acceso, `Allow all`, así que es públicamente alcanzable. Con `OBSERVE`, un POST sin
> firma a `/api/passport/webhook` sería **aceptado y persistido** como fila en
> `passport_webhook_events` con `signature_valid = 0`. El impacto está acotado y es de
> auditoría, no financiero: el flujo no mueve dinero, sólo inserta, y no altera datos
> existentes. Aun así, mientras el callback no esté registrado nada legítimo va a llegar, de
> modo que el modo permisivo no aporta valor todavía. Dos mitigaciones a considerar antes
> del primer intento de entrega: restringir por IP el acceso al App Service, o mantener
> `ENFORCE` y pasar a `OBSERVE` sólo en el momento de la primera entrega, si resulta que
> Passport no firma en Sandbox. La reversión es un solo comando.

---

## 5. Despliegue

`BACKEND_DEPLOYED=YES` · `DEPLOY_RESULT=SUCCESS`

Paquete zip de 7,8 MB con 59 archivos publicados en Release, desplegado con
`az webapp deploy --type zip` sobre `xpay-api-qa`.

| Dato | Valor |
|---|---|
| Deployment id | `fa207b72-fe0b-4f3f-a304-fadeecaa3e5a` |
| Inicio / fin (UTC) | 2026-09-30T02:13:05Z → 02:13:24Z |
| Resultado | `complete = True`, `OneDeploy` |

Sólo backend. **No** se desplegó el frontend. **No** se ejecutó ninguna migración
(`DATABASE_MIGRATION_EXECUTED=NO`; XPAY-594 ya declaró `DB_MIGRATION_CREATED=NO`).
**No** se tocó producción.

---

## 6. Verificación posterior

`BACKEND_HEALTH=PASS` · `WEBHOOK_ROUTE_DEPLOYED=YES`

| Comprobación | Antes del deploy | Después |
|---|---|---|
| `GET /health` | `200 Healthy` | `200 Healthy` |
| `GET /api/version` | `XPAY API QA`, `0.1.0-mvp-qa` | idéntico |
| `GET /api/passport/webhook` | **404** | **405** |
| `HEAD /api/passport/webhook` | — | **405** |
| `GET /api/passport/noexiste` (control) | — | **404** |

Inmediatamente tras el despliegue el backend devolvió `502` durante unos tres minutos:
arranque en frío del App Service tras el zip deploy. Se resolvió por sí solo y quedó en
`200 Healthy`. Se registra por transparencia, no fue un fallo del despliegue.

### La comprobación de ruteo fue deliberadamente incapaz de persistir

Se usó `GET`/`HEAD` sobre una ruta declarada **POST-only**. El **405** demuestra que la
ruta está mapeada; al no ser POST, la petición **no alcanza el handler**, así que no pudo
verificarse firma, no pudo parsearse payload y **no pudo insertarse ningún evento**. El
control sobre una ruta inexistente devuelve 404, lo que confirma que el 405 es informativo y
no un comodín.

Esto era obligatorio precisamente porque el modo quedó en `OBSERVE`: un POST, incluso
sintético, habría sido aceptado y persistido. **No se envió ningún POST.**

El contrato OpenAPI publicado confirma el endpoint, con un único método:

```
/api/passport/webhook   ->  post
```

---

## 7. Lo que NO se hizo

```
CALLBACK_REGISTERED=NO
WEBHOOK_PERSISTENCE_TRIGGERED=NO
PASSPORT_API_CALLS=0
FINANCIAL_OPERATIONS=0
NEW_PAYMENTS=0
RESOLVE_CALLS=0
PAYMENT_RETRIEVE_CALLS=0
WALLET_MODIFIED=NO
LEDGER_MODIFIED=NO
BREB_RETIROS_MODIFIED=NO
DATABASE_MIGRATION_EXECUTED=NO
PRODUCTION_MODIFIED=NO
MIDECISOR_MODIFIED=NO
SECRET_VALUE_PRINTED=NO
```

Ni webhook real ni sintético. Ni pago, ni Resolve, ni Payment Create, ni Payment Retrieve,
ni consulta de estado. T6 y T7 **siguen sin provocarse**.

---

## 8. Siguiente paso

`READY_FOR_CALLBACK_REGISTRATION=YES` — con dos decisiones pendientes del responsable
antes de registrarlo:

1. Si se rota `PASSPORT_WEBHOOK_SECRET` o se conserva el valor existente. El campo Secret
   Token del Dashboard debe coincidir con el que quede configurado.
2. Si se acepta la exposición de `OBSERVE` en un QA públicamente alcanzable, se restringe
   por IP, o se vuelve a `ENFORCE` hasta el momento de la primera entrega.

Recomendado además crear el commit de XPAY-594/595 para que la evidencia T6/T7 pueda
referenciar un SHA.

---

## Seguridad de este documento

No contiene secretos, tokens, firmas, cuerpos de webhook, identificadores reales de pago,
cuentas bancarias ni PII. Los únicos valores citados son nombres de recurso, nombres de
variable, códigos HTTP y el identificador de despliegue de Azure.
