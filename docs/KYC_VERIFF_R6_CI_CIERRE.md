# XPAY — Cierre Veriff R6 + estabilización CI

**Fecha UTC:** 2026-08-30
**Ambiente probado:** QA — NO producción
**Estado:** ✅ Bloque de trabajo cerrado (creación de sesión Veriff en QA + Backend Validation), **KYC E2E todavía no completo** (ver sección 17-18)

> **ADVERTENCIA:**
> `qa.uat.usuario1` es una cuenta técnica utilizada exclusivamente para pruebas en el
> ambiente QA de XPAY. Este documento no contiene contraseñas, API keys, Shared Secrets,
> JWT, documentos de identidad ni otros secretos o PII.

---

## 1. Objetivo y alcance

Este cierre cubre la estabilización de la creación de sesiones Veriff en QA (`POST
/api/kyc/veriff/session`) y la reparación del pipeline de Backend Validation de GitHub
Actions, cuyo fallo fue descubierto como consecuencia directa de haber ejercitado ese
mismo flujo. No cubre la validación end-to-end de decisiones Veriff, webhook, ni
reconciliación — esos permanecen pendientes (sección 18).

## 2. Línea base Git

Cadena lineal de commits en `main`, cada uno hijo directo del anterior (confirmado vía
`git log --format="%h %P"`):

| Commit | Mensaje | Padre |
|---|---|---|
| `ecafbe5ba06de82a038959c1ae586ca5d1832971` | `feat: add Veriff KYC reconciliation and concurrency safeguards` | `27b2f005898cce8844db3c42734ebbb2049ec8a3` |
| `2fefc25dd9591933c5bac52d355ad50d6bbb9beb` | `fix: align Veriff session payload with API schema` | `ecafbe5ba06de82a038959c1ae586ca5d1832971` |
| `a0b1db3f42cd5c6a72f1e790cddff5ef418e3012` | `ci: apply all backend migrations in validation` | `2fefc25dd9591933c5bac52d355ad50d6bbb9beb` |

**Commit 5** (`ecafbe5`) introdujo la reconciliación administrativa y las salvaguardas de
concurrencia KYC/Veriff — 3 archivos (`KycController.cs`, `KycUsuarioConcurrenteException.cs`
nuevo, `KycService.cs`), +901/−329 líneas.

**Body fix** (`2fefc25`) corrigió exclusivamente el payload de creación de sesión — 1
archivo (`KycService.cs`), +2/−5 líneas.

**CI fix** (`a0b1db3`) corrigió exclusivamente la secuencia de migraciones del workflow de
CI — 1 archivo (`.github/workflows/backend-validation.yml`), +15/−57 líneas.

Los tres commits están publicados en `origin/main` (verificado vía `git ls-remote` en cada
sub-paso de la línea de trabajo).

## 3. Problema Veriff observado

Cronología comprobada:

1. En una etapa anterior de esta línea de trabajo, un intento de creación de sesión Veriff
   (`POST /api/kyc/veriff/session`) recibió **HTTP 401** del proveedor.
2. Tras alinear el `VERIFF_API_KEY`/`VERIFF_SHARED_SECRET` de Azure QA con la integración
   "Sandbox xpay" (verificado por comparación de huella criptográfica, sin exponer valores),
   una nueva ejecución del mismo flujo pasó de 401 a **HTTP 400**.
3. Evidencia pasiva recuperada de logs de plataforma (captura de stdout del contenedor, sin
   habilitar logging nuevo) mostró el mensaje real devuelto por Veriff:
   `"Request includes invalid parameters"`, sin código de error adicional.
4. Verificación SQL PRE/POST de ese intento fallido confirmó: **sin mutación** — el estado
   de `qa.uat.usuario1` permaneció exactamente igual antes y después del 400.

No se incluyen en este documento valores de credenciales, HMAC, JWT ni ningún dato personal.

## 4. Diagnóstico del payload

La revisión contra el contrato vigente documentado de Veriff (`devdocs.veriff.com`) para
`POST /v1/sessions` identificó dos campos presentes en el payload construido por XPAY que
**no forman parte del schema documentado** en esa ubicación:

- `verification.timestamp` (no existe como campo directo de `verification`; `timestamp`
  solo aparece dentro de objetos de `consents[]`)
- `verification.redirectUrl` (nombre de campo inexistente en el schema; la funcionalidad
  equivalente ya está cubierta por `callback`)

Se conservaron, por estar confirmados como soportados:

- `verification.callback`
- `verification.vendorData`

**Importante — alcance exacto de lo demostrado:**

- El código de firma HMAC (`X-HMAC-SIGNATURE`) y sus headers **no fueron modificados** —
  permanecen exactamente iguales a como estaban antes de este bloque de trabajo. No se
  afirma que el HMAC haya sido eliminado ni corregido.
- No se realizó ninguna prueba aislada que determinara individualmente si `timestamp` o
  `redirectUrl` era, por separado, el causante del 400. Ambos campos fueron eliminados
  juntos en una única corrección mínima.
- Lo único demostrado empíricamente (sección 8) es que **el payload resultante, con ambos
  campos eliminados a la vez, fue aceptado por Veriff en la siguiente ejecución real.**

## 5. Corrección implementada

**Archivo:** `backend/Xpay.Api/Services/KycService.cs` (método `CreateVeriffSessionAsync`)

**REMOVED:**
- `Timestamp`
- `RedirectUrl`
- Comentario asociado exclusivamente a `RedirectUrl`

**PRESERVED:**
- `Callback`
- `VendorData`
- Lógica y headers HMAC existentes (`X-AUTH-CLIENT`, `X-HMAC-SIGNATURE`), sin cambios

**Build local posterior:** `dotnet build backend/Xpay.Api/Xpay.Api.csproj --no-restore` →
**PASS**, 0 warnings, 0 errors.

## 6. Deploy QA

| Campo | Valor |
|---|---|
| Target | `xpay-api-qa` / `rg-xpay-qa` |
| Scope desplegado | `COMMIT5-PLUS-BODY-FIX` |
| PRE deployment ID | `cd301fef-a13a-4527-9c92-fde40a183d98` |
| POST deployment ID | `23a34242-443b-47bc-a3eb-27a4b96f33ac` |
| Mecanismo | OneDeploy / ZIP, 1 intento |
| Resultado | `RuntimeSuccessful` |
| Health posterior | HTTP 200, `Healthy` |

**Aclaración explícita:** el commit `a0b1db3` (CI fix) **modifica exclusivamente**
`.github/workflows/backend-validation.yml` y **no fue desplegado a QA** — no aplica a un
App Service, no tiene relación con el artefacto de backend desplegado en esta sección.

## 7. R6 PRE

**Target técnico:** `qa.uat.usuario1` (sin datos personales).

**PRE comprobado (vía consulta SQL de solo lectura):**

```
EstadoKycActual        = NO_INICIADO
IdentidadVerificada    = FALSE
KYC rows               = 0
EsActual=true rows     = 0
SessionId               = ABSENT
```

El acceso SQL temporal utilizado para esta verificación fue eliminado inmediatamente
después de la consulta y antes de ejecutar el POST.

## 8. R6 ejecución real

**Endpoint:** `POST /api/kyc/veriff/session`
**Número de ejecuciones:** 1 (sin reintento)

**Resultado XPAY:**
```
HTTP 200
success = true
SessionId = PRESENT
```

**Aclaración sobre la URL de sesión:** el parser utilizado en la Terminal del usuario
reportó inicialmente `VERIFF URL = ABSENT`, porque solo buscaba las claves
`data.url`/`data.verificationUrl`/`data.veriffUrl` en la respuesta JSON. Esto **no
significó** que Veriff no hubiera devuelto ni que XPAY no hubiera persistido la URL — fue
una limitación del parser cliente, no un defecto del backend. La comprobación SQL
posterior (sección 9) confirmó `session_url = PRESENT`.

## 9. R6 SQL POST

| Campo | PRE | POST |
|---|---|---|
| EstadoKycActual | `NO_INICIADO` | `PENDIENTE` |
| IdentidadVerificada | `FALSE` | `FALSE` |
| KYC rows | `0` | `1` |
| EsActual=true rows | `0` | `1` |
| SessionId | `ABSENT` | `PRESENT` |
| SessionUrl | — | `PRESENT` |
| Proveedor | — | `VERIFF` |

**Resultado:**
```
SESSION PERSISTED = YES
DATABASE TRANSITION CONSISTENT = YES
```

La regla de firewall SQL temporal usada para la consulta POST fue eliminada
inmediatamente después de obtener este snapshot.

## 10. Resultado funcional de R6

```
R6 SESSION CREATION: PASS
```

El fallo HTTP 400 de creación de sesión quedó resuelto mediante la alineación del payload
con el schema aceptado por Veriff.

**No se declara** `KYC E2E COMPLETE` — quedan pendientes la validación de recepción y
procesamiento de decisión, el webhook, y la reconciliación administrativa (sección 18).

## 11. Commit del body fix

- Commit: `2fefc25dd9591933c5bac52d355ad50d6bbb9beb`
- Único archivo: `backend/Xpay.Api/Services/KycService.cs`
- Push: normal, fast-forward, a `origin/main`, sin `--force`

## 12. Fallo posterior de Backend Validation

GitHub Actions **Backend Validation #138**, disparado por el commit `2fefc25`, falló. La
compilación del backend (`dotnet build`) había pasado exitosamente; el fallo real ocurrió
dentro de `scripts/validate-backend.sh`, durante la primera prueba:

```
POST /api/usuarios/registro-final → HTTP 500
```

La base de datos de CI devolvió columnas inexistentes:

```
apellido_verificado_completo
identidad_verificada
identidad_verificada_fecha
identidad_verificada_proveedor
nombre_verificado_completo
numero_documento_verificado
tipo_documento_veriff_raw
```

**Root cause confirmado:** `database/032_persona_identidad_verificada.sql` — la migración
que crea exactamente esas 7 columnas sobre `dbo.personas` — no estaba siendo aplicada por
`backend-validation.yml`.

## 13. Auditoría de migraciones CI

```
TOTAL MIGRATIONS IN REPO = 34
```

La estrategia anterior del workflow aplicaba una lista manual parcial de 18 migraciones
numeradas. La auditoría identificó 16 migraciones omitidas, incluyendo las relevantes al
fallo:

```
031_catalogo_geografico.sql
032_persona_identidad_verificada.sql
033_persona_nombre_nullable.sql
034_kyc_identidad_orden_unicidad.sql
```

**Aclaración:** `database/008_seed_qa_dataset.sql` está **deliberadamente excluida** del CI
— es sustituida por `scripts/ci/ci_admin_xpay_fixture.sql`, un fixture propio de CI con
usuario/contraseña exclusivos, distintos de cualquier usuario de QA real. Esta exclusión es
una decisión de diseño ya documentada en el propio fixture, **no un error**.

## 14. Corrección CI

- Commit: `a0b1db3f42cd5c6a72f1e790cddff5ef418e3012`
- Archivo único: `.github/workflows/backend-validation.yml`

**Nueva estrategia:**
- Loop determinista sobre `database/*.sql`.
- Orden lexicográfico, compatible con los prefijos `001`...`034` (verificado por
  simulación previa sin `sqlcmd`).
- `008_seed_qa_dataset.sql` no se ejecuta contra SQL.
- `scripts/ci/ci_admin_xpay_fixture.sql` se ejecuta exactamente una vez, en la posición
  conceptual de 008.
- Migraciones `031`, `032`, `033` y `034` quedan incluidas.
- `set -euo pipefail` agregado explícitamente al bloque.

## 15. Validación GitHub Actions

| Campo | Valor |
|---|---|
| Workflow | Backend Validation #139 |
| Commit | `a0b1db3` |
| Resultado | **SUCCESS** |
| Job | Compile, Run & Test API — **PASS** |
| Duración aproximada | ~1m32s (job) / ~1m36s (workflow) |

Esto confirma que la corrección del workflow resolvió el fallo observado en Backend
Validation #138.

## 16. Warning no bloqueante

Se registró, de forma separada, un warning visible en la ejecución de GitHub Actions:

```
Node.js 20 is deprecated
```

Relacionado con las acciones `actions/checkout@v4` y `actions/setup-dotnet@v4`.

**Estado:** `NON-BLOCKING MAINTENANCE DEBT` — no corregido en este bloque de trabajo.

## 17. Estado al cierre

| Ítem | Estado |
|---|---|
| Veriff credentials QA alignment | COMPLETED |
| Veriff session payload fix | COMPLETED |
| QA deploy | PASS |
| R6 PRE | PASS |
| R6 POST HTTP | PASS |
| R6 SQL transition | PASS |
| SessionId persistence | PASS |
| SessionUrl persistence | PASS |
| Body fix committed/pushed | PASS |
| CI migration root cause | CONFIRMED |
| CI workflow fix committed/pushed | PASS |
| Backend Validation #139 | PASS |
| KYC full E2E | PENDING |
| Webhook/decision validation | PENDING |

## 18. Pendientes explícitos

1. Continuar con la siguiente prueba Veriff de la matriz, **sin repetir R6**.
2. Validar recepción/procesamiento de decisión Veriff (GET /decision y/o webhook).
3. Revisar cuidadosamente el master/shared secret usado para firmas de webhooks antes del
   E2E de webhook.
4. No eliminar ni revocar todavía el secret anterior únicamente por este cierre.
5. Resolver posteriormente la deuda técnica de Node.js/GitHub Actions (sección 16).
6. Continuar después con DataCrédito/MiDecisor y Passport/Bre-B según el roadmap, una vez
   cerrado el flujo E2E de Veriff.

No se numeran pruebas futuras (R7, R8, etc.) en este documento por no contar con evidencia
verificable de su ejecución dentro de este bloque de trabajo.
