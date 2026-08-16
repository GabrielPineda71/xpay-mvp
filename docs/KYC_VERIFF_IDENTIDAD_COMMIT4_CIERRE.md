# XPAY MVP — Commit 4: Consolidación de identidad verificada Veriff — Cierre

**Commit:** 4 — Veriff → Persona (consolidación de identidad verificada)
**Fecha UTC:** 2026-08-16
**Responsable:** Gabriel Alfonso Pineda Ortiz `g.pineda@cercaymejor.com`
**Ambiente probado:** QA — NO producción
**Commit Git publicado:** `257e594a6e8cbd767bafaffac83a088b8bdb4b40` — `feat: consolidate verified Veriff identity`
**Estado:** ✅ **CERRADO** (implementado, migrado, desplegado, probado en QA y publicado en `origin/main`)

---

> **ADVERTENCIA:**
> Todos los usuarios, documentos y datos de identidad mencionados en este documento son
> ficticios y corresponden exclusivamente a `qa.usuario1` y `qa.usuario2` en el ambiente QA
> (`xpay-api-qa.azurewebsites.net`). No son personas reales, no involucran producción ni
> Datacrédito.

---

## A. Objetivo

Consolidar en `Persona` la identidad verificada que Veriff entrega vía el webhook de
decisión (`POST /api/kyc/veriff/webhook`), de forma idempotente, transaccionalmente segura
bajo concurrencia, y con trazabilidad de auditoría sin PII — sin tocar los campos de
identidad legado de `Persona` (`TipoDocumento`, `NumeroDocumento`, `PrimerNombre`,
`SegundoNombre`, `PrimerApellido`, `SegundoApellido`), que quedan reservados a un mapeo
formal futuro fuera de este commit.

## B. Alcance implementado

- Parser puro y reutilizable del objeto `verification` de Veriff.
- Persistencia durable de `attemptId`/`decisionTime` en `kyc_verificaciones`, con una
  máquina de orden/idempotencia que decide si un evento entrante debe procesarse.
- Consolidación de identidad en `Persona` bajo un gate de 4 campos obligatorios
  (`firstName`, `lastName`, `document.type`, `document.number`), protegida por lock
  exclusivo (`AppLockHelper`) y por un índice único filtrado en SQL Server como respaldo
  defensivo.
- Seis comportamientos de consolidación de identidad (Casos 1-6, ver sección H).
- Auditoría estructurada (tabla `auditoria`) de los eventos de identidad relevantes, sin
  PII.

## C. Archivos incluidos en el commit `257e594`

1. `backend/Xpay.Api/Services/KycService.cs`
2. `backend/Xpay.Api/Models/KycVerificacion.cs`
3. `backend/Xpay.Api/Data/XpayDbContext.cs`
4. `backend/Xpay.Api/DTOs/VeriffDecisionParsed.cs`
5. `backend/Xpay.Api/Exceptions/IdentidadDocumentoConcurrenteException.cs`
6. `database/034_kyc_identidad_orden_unicidad.sql`

Ningún otro archivo del repositorio fue modificado por este commit.

## D. Migración 034

- **Archivo:** `database/034_kyc_identidad_orden_unicidad.sql`
- **SHA-256 ejecutado en QA:** `d686ebfaddfceca54a47982afd9aef6b02d0dece209f2c712f53245fa9870d3e`
- Ejecutada exitosamente contra `sqldb-xpay-qa` (servidor `xpay-sql-qa`) en un turno previo
  de este mismo engagement, con acceso temporal (Entra admin + regla de firewall) creado
  inmediatamente antes y destruido inmediatamente después. No se volvió a ejecutar en
  ningún paso posterior de este cierre.

## E. Cambios de esquema

- `kyc_verificaciones.attempt_id_veriff VARCHAR(200) NULL` — rastrea el último `attemptId`
  de Veriff procesado para esa fila.
- `kyc_verificaciones.decision_time_veriff DATETIME2 NULL` — rastrea el `decisionTime` de
  Veriff del último evento procesado.
- `UX_personas_documento_verificado` — índice único filtrado sobre
  `personas(id_unidad_negocio, numero_documento_verificado)`, activo únicamente cuando
  `numero_documento_verificado IS NOT NULL AND identidad_verificada = 1`.

## F. Diseño del parser reutilizable Veriff

`ParseVeriffDecision` (privado, puro, sin acceso a BD ni reglas de negocio) extrae de
`verification`: `SessionId`, `AttemptId`, `Decision`, `Reason`, `DecisionTimeUtc`,
`VendorData`, `FirstName`, `LastName`, `DocumentType`, `DocumentNumber`, `DateOfBirth` —
devueltos en el record `VeriffDecisionParsed`. `root.status` (nivel raíz del payload) se
mantiene deliberadamente fuera del parser: en el webhook actúa como *fallback* de la
decisión, pero en el futuro `GET /decision` (fuera de alcance de este commit) significa
algo distinto ("la llamada HTTP tuvo éxito"). Ese fallback vive en el adaptador del
webhook, no en el parser, para que el parser pueda reutilizarse sin cambios el día que se
implemente `GET /decision`.

## G. Máquina de orden/idempotencia `attemptId`/`decisionTime`

Basada exclusivamente en lo persistido en `kyc_verificaciones` (nunca en memoria del
proceso):

- **Caso A** — mismo `attemptId` y misma decisión ya registrada → idempotente, sin
  escritura.
- **Caso B** — mismo `attemptId`, decisión distinta → anomalía, no se sobrescribe.
- **Caso C** — `attemptId` distinto y `decisionTime` posterior al persistido → intento
  legítimo, se procesa.
- **Caso D** — `attemptId` distinto pero `decisionTime` no posterior → no se sobrescribe.
- **Caso E** — orden no determinable (falta `decisionTime` en alguno de los dos lados, o
  falta `attemptId` entrante habiendo uno ya rastreado) → no se sobrescribe.
- **Caso E-transición** — fila histórica sin `attemptId` rastreado todavía (anterior a la
  migración 034) → se trata como primer intento rastreado, sin bloquear.

## H. Casos 1-6 de consolidación de identidad y comportamiento final

Todos requieren decisión `APROBADO`. El gate de 4 campos obligatorios (`firstName`,
`lastName`, `document.type`, `document.number`) determina si se entra a la rama con lock
exclusivo (Casos 1, 3, 4, 5, 6) o a la rama sin lock (Caso 2).

| Caso | Condición | Efecto sobre Persona | Auditoria |
|---|---|---|---|
| 1 — Consolidada | Gate completo, no verificada, sin conflicto de documento | Consolida `IdentidadVerificada=true` + los 4 campos + `FechaNacimiento` si viene | `KYC_IDENTIDAD_CONSOLIDADA` |
| 2a — Incompleta, no verificada | Gate incompleto, Persona aún no verificada | Persiste solo los campos crudos presentes, sin marcar `IdentidadVerificada` | `KYC_IDENTIDAD_INCOMPLETA` |
| 2b — Incompleta, ya verificada | Gate incompleto, Persona ya verificada | Sin cambios | Ninguna |
| 3 — Documento duplicado | Gate completo, no verificada, documento ya verificado en otra Persona de la misma unidad de negocio | Sin cambios | `KYC_IDENTIDAD_DOCUMENTO_DUPLICADO` |
| 3 tardío | Violación del índice único detectada en `SaveChangesAsync` (backstop defensivo) | Rollback completo, sin segunda escritura | Ninguna (queda para una reentrega futura) |
| 4 — Revalidación equivalente | Ya verificada, mismo documento y mismos datos | Sin cambios | Ninguna |
| 5 — Cambio de datos | Ya verificada, mismo documento, datos distintos | Sin cambios | `KYC_IDENTIDAD_CAMBIO_DATOS` |
| 6 — Cambio de documento | Ya verificada, documento distinto | Sin cambios | `KYC_IDENTIDAD_CAMBIO_DOCUMENTO` |

En todos los casos, `KycVerificacion` (estado, decisión, `attemptId`/`decisionTime`) y
`Usuario` (`EstadoKycActual`) se actualizan según corresponda a la decisión recibida,
independientemente de si la identidad se consolidó o no.

## I. AppLock y protección por índice único

- Clave de lock: `XPAY:IDENTIDAD_DOCUMENTO:{idUnidadNegocio}:{documentoNormalizado}`,
  adquirida vía `AppLockHelper.AdquirirAsync` dentro de una transacción explícita
  (`BeginTransactionAsync`), interpretada por el helper propio `ValidarResultadoLockIdentidad`
  (no reutiliza `AppLockHelper.ValidarResultado`, que pertenece semánticamente al dominio
  Caja/Cierre) y traducida a `IdentidadDocumentoConcurrenteException` en caso de contención.
- Normalización de documento para lock y comparación en memoria:
  `Trim().ToUpperInvariant()`. Para consultas traducidas a SQL (`AnyAsync`) se usa
  `Trim().ToUpper()` (traducible por el proveedor SqlServer de EF Core, verificado
  empíricamente vía `ToQueryString()` en un turno previo), equivalente a
  `ToUpperInvariant()` para el rango de caracteres de un número de documento.
- `UX_personas_documento_verificado` actúa como respaldo defensivo de última línea: si la
  violación del índice ocurre pese al lock, se hace rollback completo sin ninguna segunda
  escritura (Caso 3 tardío).

## J. Auditoría y política No-PII

Acciones registradas en la tabla `auditoria` (`Modulo=KYC`, `Entidad=kyc_verificaciones`,
`Resultado=EXITOSO` en todos los casos):

- `KYC_IDENTIDAD_CONSOLIDADA`
- `KYC_IDENTIDAD_INCOMPLETA`
- `KYC_IDENTIDAD_DOCUMENTO_DUPLICADO`
- `KYC_IDENTIDAD_CAMBIO_DATOS`
- `KYC_IDENTIDAD_CAMBIO_DOCUMENTO`

Los Casos 2b, 3 tardío y 4 **no generan ninguna fila de Auditoria nueva**, por diseño
(no hay una decisión de negocio nueva que registrar). `ValorAnterior`/`ValorNuevo` solo
toman los valores categóricos `"NO_VERIFICADA"`/`"VERIFICADA"` (Caso 1) o `NULL` (el
resto). `Observacion` contiene únicamente texto fijo y, cuando aplica, etiquetas técnicas
de campo (`FIRST_NAME`, `LAST_NAME`, `DOCUMENT_TYPE`, `DOCUMENT_NUMBER`, `NOMBRE`,
`APELLIDO`, `TIPO_DOCUMENTO`) — nunca nombres, apellidos, números de documento, fechas de
nacimiento, `sessionId`, `attemptId`, `vendorData` ni JSON crudo. Verificado por grep
directo sobre el código y sobre las filas reales creadas en QA en cada prueba.

## K. Pruebas funcionales ejecutadas en QA

Todas ejecutadas mediante webhooks reales firmados con HMAC-SHA256 contra
`https://xpay-api-qa.azurewebsites.net/api/kyc/veriff/webhook`, con verificación SQL de
solo lectura antes y después de cada envío (acceso temporal Entra admin + firewall creado
y destruido en cada turno).

| # | Comportamiento | Sujeto QA | Resultado |
|---|---|---|---|
| 1 | Caso 1 — consolidación completa | `qa.usuario1` (idUsuario=3/idPersona=3), `kyc_verificaciones#12` | HTTP 200, `processed=true`. Persona consolidada (`IdentidadVerificada=true`, `Proveedor=VERIFF`, 4 campos + `FechaNacimiento`). Auditoria `KYC_IDENTIDAD_CONSOLIDADA` (id=95) creada. |
| 2 | Caso 2a — incompleto, no verificada | `qa.usuario2` (idUsuario=4/idPersona=4), `kyc_verificaciones#5` | HTTP 200. Persona con campos crudos parciales, `IdentidadVerificada` sin cambio. Auditoria `KYC_IDENTIDAD_INCOMPLETA` (id=96) creada. |
| 3 | Caso 2b — incompleto, ya verificada | `qa.usuario1`, `kyc_verificaciones#12` | HTTP 200. Persona sin ningún cambio (`fecha_actualizacion` idéntica PRE/POST). 0 Auditorias nuevas. |
| 4 | Caso 3 — documento duplicado | `qa.usuario2`, `kyc_verificaciones#5`, documento en conflicto con Persona id=3 | HTTP 200. Persona sujeto sin cambios. Auditoria `KYC_IDENTIDAD_DOCUMENTO_DUPLICADO` (id=97) creada. Persona id=3 (dueña original del documento) intacta. |
| 5 | Caso 4 — revalidación equivalente | `qa.usuario1`, `kyc_verificaciones#12` | HTTP 200. Persona idéntica byte a byte PRE/POST. 0 Auditorias nuevas (siguen exactamente las mismas). |
| 6 | Caso 5 — cambio de datos | `qa.usuario1`, `kyc_verificaciones#12` | HTTP 200. Persona sin sobrescribir (`nombre_verificado_completo` se mantuvo en el valor ya consolidado). Auditoria `KYC_IDENTIDAD_CAMBIO_DATOS` (id=98) creada, campo distinto reportado: `NOMBRE`. |
| 7 | Caso 6 — cambio de documento | `qa.usuario1`, `kyc_verificaciones#12` | HTTP 200. Persona sin sobrescribir (`numero_documento_verificado` se mantuvo en el valor ya consolidado). Auditoria `KYC_IDENTIDAD_CAMBIO_DOCUMENTO` (id=99) creada. Documento nuevo (ficticio) nunca persistido en ninguna Persona. |
| 8 | Reentrega idéntica / idempotencia | `qa.usuario1`, `kyc_verificaciones#12` (mismo `sessionId`+`attemptId`+`decisionTime`+datos que el Caso 2b) | HTTP 200, `processed=true`. Todos los timestamps de Persona/Usuario/KYC idénticos PRE/POST. 0 Auditorias nuevas — la máquina de idempotencia (Caso A) cortó el procesamiento antes de cualquier escritura. |

## L. Resultado de build

`dotnet build backend/Xpay.Api/Xpay.Api.csproj` → **0 Warning(s), 0 Error(s)**, confirmado
de forma repetida a lo largo de todo el commit, incluida la verificación final previa al
commit Git.

## M. Deploy a `xpay-api-qa`

- Publicado mediante `dotnet publish` + empaquetado ZIP + `az webapp deploy` contra el
  App Service `xpay-api-qa` en el resource group `rg-xpay-qa` (único recurso tocado).
- Resultado de `az webapp deploy`: `status=RuntimeSuccessful`,
  `numberOfInstancesSuccessful=1`, `numberOfInstancesFailed=0`.
- Verificación post-deploy: App Service `Running`; `GET /health` → `200`;
  `GET /api/version` → `200`; `POST /api/kyc/veriff/webhook` (probe sin firma) → `401`
  (ruta enrutada y disponible, rechazada correctamente antes de cualquier lógica de
  negocio).

## N. Commit Git final y confirmación de push

- **Commit:** `257e594a6e8cbd767bafaffac83a088b8bdb4b40`
- **Mensaje:** `feat: consolidate verified Veriff identity`
- **Push:** `git push origin main` → `f353a5a..257e594 main -> main` (fast-forward, sin
  `--force`).
- **Verificado post-push:** `HEAD == origin/main == 257e594a6e8cbd767bafaffac83a088b8bdb4b40`.

## O. Riesgos / hallazgos que permanecen abiertos

- **`qa.usuario2` quedó con dos filas `kyc_verificaciones` con `es_actual=1` simultáneas
  (`#5` y `#11`)** — efecto colateral conocido y explícitamente autorizado durante las
  pruebas de Caso 2a/3 (`ProcessVeriffWebhookAsync` marca `EsActual=true` sobre la fila
  encontrada por `sessionId`, sin desactivar otras filas del mismo usuario). No fue
  corregido, por instrucción explícita, para no interferir con la evidencia de las
  pruebas ya ejecutadas.
- **`qa.usuario1` no tenía ninguna fila en `usuario_roles` durante la prueba del Caso 1**
  — anomalía preexistente en el dato QA, no introducida por este commit, que no afectó el
  resultado de la prueba (`ProcessVeriffWebhookAsync` no valida roles).
- La verificación de traducción EF Core → SQL del `AnyAsync` del Caso 3 se validó
  estáticamente vía `ToQueryString()` (sin ejecución real contra QA en ese turno
  específico), no mediante una traza de ejecución en producción/QA de esa consulta en
  aislamiento.
- Los Casos 2a, 3, 5 y 6 dejan intencionalmente una identidad sin consolidar o una
  divergencia sin resolver — su resolución operativa (seguimiento, revisión humana) queda
  fuera de este commit (ver sección P).

## P. Explícitamente fuera de alcance de Commit 4

- Fallback `GET /v1/sessions/{sessionId}/decision`.
- Reconciliación / scheduler automático.
- Frontend (ningún archivo de `frontend/` fue tocado).
- Integración con Datacrédito.
- Cualquier sobreescritura automática de una identidad ya verificada (Casos 4, 5 y 6
  preservan siempre el valor ya consolidado).
- Resolución operativa (bandeja de revisión, seguimiento) de los Casos 2a, 3, 5 y 6 —
  quedan auditados pero sin mecanismo de gestión.
- Limpieza de los datos de prueba creados en QA — `qa.usuario1`/`Persona id=3` y
  `qa.usuario2`/`Persona id=4` permanecen exactamente en el estado resultante de las
  pruebas, sin reversión.

## Q. Criterio de cierre / estado final

**CERRADO.** Implementado, compilado sin warnings/errores, migrado en QA, desplegado en
`xpay-api-qa`, probado funcionalmente en los 8 comportamientos descritos en la sección K,
y publicado en `origin/main` en el commit `257e594a6e8cbd767bafaffac83a088b8bdb4b40`.
