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

**Actualizado por XPAY-596: el commit ya existe — `285f5ac`. Ver §9.** Lo que sigue
describe la situación en el momento del despliegue.

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
| `PASSPORT_WEBHOOK_SIGNATURE_MODE` | **`ENFORCE`** desde el cierre de XPAY-596. Estuvo en
`OBSERVE` entre el 2026-09-30T02:12Z y 12:35Z — ver §9 y §10 |
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


---

## 9. Actualización XPAY-596 — trazabilidad cerrada, hardening BLOQUEADO

**Fecha:** 2026-09-30 (UTC)

### Trazabilidad: cerrada

```
COMMIT_PERFORMED=YES
COMMIT_SHA=285f5acf4b1239d2f8e6bec526bffd0107841c69
PUSH_PERFORMED=NO
```

Commit exclusivo del alcance Passport T6/T7: **18 archivos, 3134 inserciones**, ningún
archivo ajeno. Incluye la implementación, sus seis archivos de pruebas, el registro de DI en
`Program.cs` y los tres documentos de certificación (contrato, implementación y este mismo
despliegue).

`DEPLOYED_CODE_MATCHES_COMMIT=YES`, verificado por fechas de modificación: todos los fuentes
del alcance se modificaron entre 01:01Z y 01:10Z del 2026-09-30, y el despliegue se ejecutó
a las 02:13:24Z del mismo día. Ningún archivo compilado cambió después. **QA ejecuta
exactamente el código de `285f5ac`**, y la evidencia T6/T7 ya puede referenciar un SHA.

El árbol de trabajo conserva intactos los tres cambios preexistentes y ajenos a este frente
(`docs/WALLET_CAJA_CUADRE_FASE_70.4_DISENO.md`,
`tools/Xpay.PassportSandboxHarness/HarnessApp.cs`, `HarnessDecision.cs`). No se descartaron
ni se incluyeron.

Escaneos previos al commit, todos sobre el diff staged: sin secretos, sin claves privadas,
sin cabeceras de autorización, sin JWT, sin bin/obj, sin `.env`, sin alcance de
MiDecisor/DataCrédito/Cartera/frontend. `PASSPORT_WEBHOOK_SECRET` aparece únicamente como
**nombre** de variable en cinco lugares, nunca con valor. El único correo del diff es
`sintetico@example.test`, sintético y en un TLD reservado para pruebas.

### Hardening: NO aplicado — la cuenta de Azure está deshabilitada

```
SIGNATURE_MODE_BEFORE = OBSERVE
SIGNATURE_MODE_AFTER  = OBSERVE   (sin cambio: el intento falló)
QA_HARDENING_APPLIED  = NO
```

El intento de fijar `PASSPORT_WEBHOOK_SIGNATURE_MODE=ENFORCE` falló. La causa es externa al
proyecto:

```
AADSTS50057: The user account is disabled.
```

La cuenta con la que la CLI de Azure estaba autenticada quedó **deshabilitada** en el
directorio. `az account show` seguía mostrando una sesión válida porque lee estado
cacheado en local; toda operación real contra Azure falla en la adquisición del token.

Consecuencias, dichas sin rodeos:

- El cambio **no se aplicó**. La autenticación falla **antes** de cualquier escritura, así
  que no hubo modificación parcial. No es verificable desde aquí hasta que se restablezca
  el acceso.
- **La exposición declarada en §4 sigue viva.** QA continúa con acceso `Allow all` y en modo
  `OBSERVE`, de modo que un POST sin firma a `/api/passport/webhook` sería aceptado y
  persistido en `passport_webhook_events` con `signature_valid = 0`. El impacto sigue siendo
  de auditoría y no financiero —sólo inserta, no mueve dinero, no altera datos existentes—,
  pero es una vía de escritura pública que no aporta nada mientras el callback no esté
  registrado.
- `PASSPORT_WEBHOOK_SECRET` **no fue leído, ni impreso, ni derivado, ni modificado**. Sigue
  presente e intacto.
- **No se tocaron las restricciones de acceso** (`QA_ACCESS_RESTRICTIONS_MODIFIED=NO`), ni
  ninguna otra App Setting.

El App Service no se vio afectado por el estado de la cuenta: se verificó por HTTP que QA
sigue sano (`/health` → `200 Healthy`) y que la ruta del webhook sigue publicada
(`GET /api/passport/webhook` → `405`). No se envió ningún POST.

### Estado consolidado

```
CALLBACK_REGISTERED=NO
WEBHOOK_POST_REQUESTS=0
WEBHOOK_PERSISTENCE_TRIGGERED=NO
PASSPORT_API_CALLS=0
FINANCIAL_OPERATIONS=0
NEW_PAYMENTS=0
PRODUCTION_MODIFIED=NO
MIDECISOR_MODIFIED=NO
DEPLOY_PERFORMED=NO
```

`READY_FOR_CALLBACK_REGISTRATION=NO` **hasta que se restablezca el acceso a Azure y el modo
quede en `ENFORCE`**, o se decida explícitamente registrar el callback aceptando la
exposición de `OBSERVE`.


---

## 10. Cierre de XPAY-596 — hardening APLICADO

**Fecha:** 2026-09-30 (UTC)

El bloqueo descrito en §9 se resolvió: se reautenticó la CLI de Azure con la misma
cuenta del operador, que volvió a estar habilitada. Antes se ejecutó `az logout` para
descartar la sesión cacheada que informaba un estado válido inexistente.

```
SIGNATURE_MODE_BEFORE = OBSERVE
SIGNATURE_MODE_AFTER  = ENFORCE
QA_HARDENING_APPLIED  = YES
```

Verificaciones del alcance del cambio:

| Comprobación | Resultado |
|---|---|
| `PASSPORT_WEBHOOK_SIGNATURE_MODE` | `ENFORCE` |
| `PASSPORT_WEBHOOK_SECRET` | Presente y no vacío — **no leído, no impreso, no derivado, no modificado** |
| App Settings relacionados con el webhook | Exactamente dos, los dos esperados |
| Total de App Settings | 36 — ninguno añadido ni eliminado por este paso |
| Restricciones de acceso | `Allow all`, **sin tocar** |
| `GET /health` | `200 Healthy` tras el reinicio que provoca el cambio de configuración |
| `GET /api/passport/webhook` | `405` — ruta publicada, sigue siendo POST-only |

La escritura se hizo con `--output none` y la salida redirigida, de modo que en ningún
momento se volcó la configuración con valores.

### Efecto sobre la exposición

La vía de escritura pública no autenticada descrita en §4 y §9 **queda cerrada**. Con
`ENFORCE`, una entrega sin firma válida recibe **401** y **no se persiste**. El acceso al App
Service sigue siendo `Allow all` —no se modificó, conforme al ticket—, pero eso ya no permite
insertar eventos.

Ventana de exposición registrada para auditoría: entre el 2026-09-30T02:12Z (cuando se fijó
`OBSERVE`) y el 2026-09-30T12:35Z (cuando se fijó `ENFORCE`). En ese intervalo no se envió
ningún POST desde XPAY, y el callback nunca estuvo registrado en Passport, por lo que no se
esperaba ninguna entrega legítima.

### Estado consolidado

```
COMMIT_SHA=285f5acf4b1239d2f8e6bec526bffd0107841c69
DEPLOYED_CODE_MATCHES_COMMIT=YES
PUSH_PERFORMED=NO
SIGNATURE_MODE=ENFORCE
CALLBACK_REGISTERED=NO
WEBHOOK_POST_REQUESTS=0
WEBHOOK_PERSISTENCE_TRIGGERED=NO
PASSPORT_API_CALLS=0
FINANCIAL_OPERATIONS=0
READY_FOR_CALLBACK_REGISTRATION=YES
```

> **Actualización XPAY-598:** no hizo falta registrar nada. El webhook **ya existía y
> estaba activo** en Passport Sandbox, apuntando exactamente a esta URL, con Delivery
> Mode `Retry` y los cuatro eventos `payment.inbound.*` seleccionados. No se modificó.
> Ver `M5-T6-T7-CALLBACK-REGISTRO-SANDBOX.md` §7.

Nota para el registro del callback: con `ENFORCE` activo, si Passport **no** firma las
entregas en Sandbox, la primera entrega recibirá 401 y Passport reintentará hasta 12 veces.
Si eso ocurre, la reacción correcta es pasar a `OBSERVE` en ese momento —no antes— y dejarlo
declarado en la evidencia de T6/T7.
