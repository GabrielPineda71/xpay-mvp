# XPAY — Cartera Ordinaria — Cierre Etapa 4

**Línea de trabajo:** Originación automática de cupo de Cartera Ordinaria (vía MiDecisor/DataCrédito — aún no integrado)
**Fecha UTC:** 2026-08-31
**Responsable:** Gabriel Alfonso Pineda Ortiz `gabrielalfonsopineda@gmail.com`
**Ambiente:** desarrollo local — sin QA, sin producción, sin Azure

---

## Estado

**ETAPA 4: CLOSED** *(actualización 2026-09-01; antes `CLOSED_CODE`)*

- La implementación del endpoint HTTP está **completa** en código.
- `dotnet build backend/Xpay.Api/Xpay.Api.csproj` → **PASS**, 0 warnings, 0 errores.
- La **validación dinámica** (migración 035 aplicada + re-ejecutada contra SQL Server 2022 efímero, constraints reales, smoke HTTP del endpoint) **se completó en CI** — ver [Cierre final — CI efímero + PR #1 + merge a main](#cierre-final--ci-efímero--pr-1--merge-a-main).
- La adaptación mínima del CI efímero (segunda pasada de la migración 035 + fase de assertions estructurales + smoke HTTP) fue **implementada** (`.github/workflows/backend-validation.yml`, `scripts/validate-backend.sh`), con hardening posterior de las assertions B2/B6.

**Originación estructural ETAPAS 1–4 + CI: CLOSED.** Etapas previas: ETAPA 1 `COMPLETE` (validación dinámica ya completada), ETAPA 2 `COMPLETE`, ETAPA 3 `CLOSED` + hardening 017 `COMPLETE` (ver `docs/CARTERA_ORDINARIA_ORIGINACION_ETAPA_3_CIERRE.md`).

**Sigue fuera de alcance (no cerrado, no iniciado):** integración MiDecisor / DataCrédito, decisión crediticia real, consulta real de score, consentimiento/legal de consulta de riesgo, desembolso, asignación final de cupo por proveedor, Passport/Bre-B real. No hubo ninguna llamada real a proveedor en esta validación.

---

## Endpoint implementado

`POST /api/cartera-ordinaria/solicitar-cupo`

Archivo modificado (único de ETAPA 4): `backend/Xpay.Api/Controllers/CarteraOrdinariaController.cs`

| Aspecto | Implementación |
|---|---|
| **Autenticación** | `[Authorize]` a nivel de clase (heredado) |
| **KYC** | `[Authorize(Policy = "KycAprobado")]` aplicado **directamente al action** (opt-in; no se toca `DefaultPolicy` ni otros endpoints). La policy está registrada en `Program.cs` (`options.AddPolicy("KycAprobado", …)`). No se re-consulta KYC en el controller. |
| **Ownership (JWT)** | `idUsuario` sólo desde `IdUsuarioActual` → `User.FindFirst("idUsuario")` (claim JWT). Nunca body/query/route/header. |
| **Body** | `SolicitarCupoRequest` (`[FromBody]`) — único campo funcional `MontoSolicitado`. Sin DTO nuevo, sin campos añadidos. |
| **Idempotency-Key** | Header HTTP obligatorio vía `TryGetIdempotencyKey(out Guid, out string)` (mismo criterio que `WalletsController`/`QrController` + rechazo explícito de `Guid.Empty`). Rechaza: ausente, múltiples valores, vacío, GUID inválido, `Guid.Empty` → **400**. Nunca se genera en el backend, nunca se toma del body/query. |
| **correlationId** | `HttpContext.Items["CorrelationId"]?.ToString() ?? HttpContext.TraceIdentifier` — poblado por `CorrelationIdMiddleware` (registrado en `Program.cs`), del header `X-Correlation-ID` entrante o un GUID nuevo. Controlado por el servidor, nunca del body. |
| **Service call** | Exactamente **una** llamada: `await svc.CrearSolicitudCupoAsync(IdUsuarioActual, idempotencyKey, req.MontoSolicitado, correlationId)`. Ninguna consulta de usuario/persona/política/solicitud activa, AppLock, transacción, replay ni inserción se duplica en el controller. |
| **Success** | `return Ok(result)` → **HTTP 200 + `SolicitudCupoResponse`** (convención del controller; no existe GET canónico de solicitud por ID que justifique 201). |
| **400** | Header Idempotency-Key inválido/ausente (controller). `ArgumentException` del service (monto ≤ 0, key `Guid.Empty`, correlationId vacío) → `catch (ArgumentException)` → `BadRequest(new { error = ex.Message })`. Sin stack trace. |
| **404** | `catch (KeyNotFoundException)` → `NotFound(new { error = ex.Message })` ("Usuario no encontrado" / "Persona asociada no encontrada"). Sin stack trace. |
| **409** | `catch (InvalidOperationException ex) when (EsConflictoSolicitudCupo(ex.Message))` → `Conflict(new { error = ex.Message })`. Lista **exacta** de mensajes: "Ya tienes una solicitud de cupo en curso"; "Idempotency-Key ya utilizada para otra solicitud."; "Idempotency-Key ya utilizada con parámetros diferentes."; "Hay otra solicitud de cupo en proceso para este usuario. Intenta de nuevo en unos segundos." Ninguna otra `InvalidOperationException` se mapea a 409. |
| **500 seguro** | Otras `InvalidOperationException` ("No hay una política de crédito activa", "Intento de solicitud sin solicitud asociada — inconsistencia de datos.") y cualquier excepción no prevista **se propagan** a `ErrorHandlingMiddleware`, que responde `{"success":false,"error":"internal_server_error","message":"An unexpected error occurred.","correlationId":"…"}` sin exponer el mensaje ni el stack. No hay `catch (Exception)` redundante en el controller. |

Formato de error: `new { error = "…" }` — consistente con los 8 endpoints existentes de `CarteraOrdinariaController`.

Scan del diff — sin lógica nueva de: `DataCredito`/`Datacredito`/`MiDecisor`/`HttpClient`/`ScoreObservado`/`FechaNacimiento`/`EdadCalculadaAlMomento`/`RECHAZADA`/`APROBADA`.

---

## Invariantes heredados de Etapa 3 (sin cambios)

- Una solicitud activa por usuario (aplicación + índice `UNIQUE` filtrado de BD).
- Idempotency-Key globalmente única (`UNIQUE (idempotency_key)`).
- Replay válido sólo si **mismo usuario + mismo monto** (hardening 017); usuario distinto o monto distinto → conflicto sin exponer datos.
- Solicitud + primer intento creados atómicamente (una transacción local).
- Estado inicial `RECIBIDA`; decisión inicial `PENDIENTE`.
- Campos de proveedor y `EdadCalculadaAlMomento` → `NULL` en PRE-CALL; `resultado_tecnico` del intento → `NULL`.
- Sin proveedor, sin decisión crediticia, sin cálculo de edad, sin uso de score.

---

## Validación realizada

*(Estado al cierre de código de ETAPA 4 — 2026-08-31. La validación dinámica se completó después: ver [Cierre final](#cierre-final--ci-efímero--pr-1--merge-a-main).)*

- `dotnet build` → **PASS** (0 warnings, 0 errores) al cerrar ETAPA 4.
- **Inspección estática HTTP → PASS** para los 12 casos A–L (mapeo de cada excepción del service a su código HTTP verificado por lectura del código).
- Sin SQL, sin QA, sin Azure, sin proveedor en ese momento.

---

## Validación pendiente → **COMPLETADA (2026-09-01)**

Los 4 puntos que estaban pendientes se ejecutaron en CI efímero (ver [Cierre final](#cierre-final--ci-efímero--pr-1--merge-a-main)):

1. ~~Migración 035 aplicada dinámicamente contra SQL Server (primera ejecución).~~ **PASS** (loop `database/*.sql`).
2. ~~Re-ejecución de la migración 035 en el mismo job.~~ **PASS** (segunda pasada añadida a `backend-validation.yml`; rama idempotente/fail-fast verificada).
3. ~~Verificación de los constraints reales en SQL Server.~~ **PASS** — B1–B7: tablas, PKs exactas, 5 FKs, `UNIQUE (idempotency_key)`, `UNIQUE (id_solicitud, numero_intento)`, índice `UNIQUE` filtrado con los 5 estados, y nullability de `estado_score` / `resultado_tecnico` / `edad_calculada_al_momento`.
4. ~~Smoke / integración HTTP sintético del endpoint en CI.~~ **PASS** — CASE A/B/C/D → 400 sin persistencia; CASE L → 200 + estado PRE-CALL; replay → 200 sin duplicar; CASE G/F → 409 sin mutación.

---

## Estrategia CI encontrada — *inspección original (histórico)*

> **Nota (2026-09-01):** toda esta sección — incluidas sus tablas de "¿… hoy?" y de "viabilidad (diseño, no ejecución)" — es el registro de la inspección READ-ONLY **previa** a implementar el cambio de CI. Todo lo que aquí figura como "no se hace hoy" / "el CI no ejecuta ninguna migración dos veces" / "endpoints de `cartera-ordinaria` probados hoy: NO" / "VIABLE con cambio mínimo" **ya fue implementado y está en verde** — el estado vigente está en [Cierre final](#cierre-final--ci-efímero--pr-1--merge-a-main). Se conserva como contexto de por qué antes estaba pendiente.

### Workflow

`.github/workflows/backend-validation.yml` — job `validate-backend` en `ubuntu-latest`. Dispara en `push`/`pull_request` a `main`.

### SQL Server efímero

| Pregunta | Respuesta (evidencia) |
|---|---|
| ¿SQL Server efímero? | **SÍ** |
| Mecanismo | GitHub Actions **service container** (`services: sqlserver:`) |
| Imagen / versión | `mcr.microsoft.com/mssql/server:2022-latest`, `MSSQL_PID: Developer`, puerto `1433:1433` |
| Readiness | `--health-cmd` del service (sqlcmd `SELECT 1`, interval 10s, retries 12) **+** step explícito "Wait for SQL Server to accept connections" (30 intentos × 5s) |
| Aplicación de migraciones | Step "Crear base de datos y ejecutar migraciones": `for migration in database/*.sql; do sqlcmd … -i "$migration"; done` con `sqlcmd -b` (falla el step ante cualquier `THROW`) |
| Descubrimiento | **Automático** — glob `database/*.sql` ordenado lexicográficamente. No hay lista manual. Excepción única: `008_seed_qa_dataset.sql` se sustituye por `scripts/ci/ci_admin_xpay_fixture.sql`. |
| ¿035 corre automáticamente? | **SÍ** — `035_cartera_solicitud_cupo.sql` entra por el glob y ordena último (`001…035`). No requiere cambio para la **primera** ejecución. |
| ¿Migraciones en orden? | **SÍ** — orden lexicográfico del glob del shell. |
| ¿035 dos veces seguro en el mismo job? | **SÍ por diseño** — la migración es idempotente/fail-fast (`IF OBJECT_ID(...) IS NULL … ELSE verify+THROW`), y la BD es efímera. Pero **el CI no ejecuta ninguna migración dos veces hoy**. |
| ¿Patrón existente de "correr una migración específica dos veces"? | **NO** — cada migración se ejecuta exactamente una vez. |
| Backend contra la BD efímera | **SÍ** — `ConnectionStrings__XpayConnection: Server=localhost,1433;Database=XPAY_MVP;…` |
| Destrucción de DB/container | **SÍ, automática** — service container + runner efímeros, destruidos al terminar el job. |

### Fixtures sintéticos (todos sólo en la BD efímera del job)

| Elemento | ¿Existe en CI? | Cómo |
|---|---|---|
| Usuario sintético | **SÍ** | `POST /api/usuarios/registro-final` (`carlos_ci_test`) crea persona + usuario |
| Persona sintética | **SÍ** | misma llamada `registro-final` |
| JWT sintético | **SÍ** | `POST /api/auth/login` → `.data.token` |
| Fixture admin | **SÍ** | `scripts/ci/ci_admin_xpay_fixture.sql` (`ci_admin_xpay`, ADMIN_XPAY + SUPERUSUARIO) |
| Prueba negativa KYC | **SÍ** | `POST /api/wallets/transferencia` sin KYC → espera `403` + `error == "KYC_REQUIRED"` (FASE 1.5) |
| Fixture KYC aprobado | **SÍ** | `UPDATE usuarios SET estado_kyc_actual='APROBADO'` + `UPDATE personas SET identidad_verificada=1` sobre la BD efímera (FASE 1.5). Sin Veriff, sin filas en `kyc_verificaciones`. |
| Política de Cartera Ordinaria activa | **SÍ, ya sembrada** | `database/021_cartera_ordinaria.sql` §11: `IF NOT EXISTS (… estado='ACTIVO') INSERT INTO cartera_politicas_credito (… , 'ACTIVO', GETUTCDATE())`. Corre en CI automáticamente. **No hay que tocar QA.** |
| Helper de assertion SQL | **SÍ** | `check_sql_value` / `check_sql_count` en `scripts/validate-backend.sh` (usados ~30 veces) |
| Endpoints autenticados en CI | **SÍ** | `post_auth_json` / `get_auth_json` con `Authorization: Bearer`; helper de header `Idempotency-Key` para endpoints idempotentes (transferencia, qr/pagar) |
| Endpoints de `cartera-ordinaria` probados hoy | **NO** — ninguno |

### Viabilidad de validar la migración 035 *(análisis previo — ya ejecutado)*

| Paso | ¿Viable con la infra actual? — *(todos ejecutados en verde; ver Cierre final)* |
|---|---|
| A. Crear SQL Server efímero | **SÍ** (ya ocurre) |
| B. Aplicar migraciones 001..035 | **SÍ** (ya ocurre, automático) |
| C. Verificar que 035 terminó OK (1ª vez) | **SÍ** — `sqlcmd -b` ya falla el step ante `THROW`; falta una assertion explícita del `SELECT … AS resultado` final de 035 |
| D. Ejecutar 035 una **2ª** vez | **VIABLE con cambio mínimo** — re-invocar `sqlcmd … -i database/035_cartera_solicitud_cupo.sql` |
| E. Verificar que la 2ª ejecución termina OK | **VIABLE** con el mismo step añadido + `-b` |
| F/G. Assertions estructurales (tablas, PK/FK/UNIQUE/índice filtrado, nullability) | **VIABLE** — reutilizando `check_sql_value` contra `sys.tables` / `sys.indexes` / `sys.key_constraints` / `sys.columns` |

---

## Casos HTTP — clasificación y estado

Actualizado 2026-09-01 con el resultado real en CI. Los casos `CI_EPHEMERAL_SAFE` se ejecutaron dinámicamente en Backend Validation (PR #1 + post-merge en `main`, ambos SUCCESS); los `STATIC_ONLY` siguen verificados sólo por lectura de código (no se fabrican estados artificiales para forzarlos).

| Caso | Descripción | Clasificación | Estado real |
|---|---|---|---|
| A | sin Idempotency-Key → 400 | CI_EPHEMERAL_SAFE | **PASS en CI** (+ 0 solicitudes creadas) |
| B | Idempotency-Key inválida → 400 | CI_EPHEMERAL_SAFE | **PASS en CI** |
| C | Guid.Empty → 400 | CI_EPHEMERAL_SAFE | **PASS en CI** |
| D | monto ≤ 0 → 400 | CI_EPHEMERAL_SAFE | **PASS en CI** |
| E | usuario/persona inexistente → 404 | STATIC_ONLY | verificado por lectura (`KeyNotFoundException → 404`); no reproducible sin token huérfano |
| F | solicitud activa + key nueva → 409 | CI_EPHEMERAL_SAFE | **PASS en CI** (sin nueva solicitud ni intento) |
| G | misma key + monto distinto → 409 | CI_EPHEMERAL_SAFE | **PASS en CI** (sin mutación) |
| H | misma key + usuario distinto → 409 sin exposición | NO IMPLEMENTADO POR DISEÑO | cubierto por hardening 017 + lectura de código; no requiere 2º fixture KYC |
| I | contención AppLock → 409 | STATIC_ONLY | verificado por lectura (`ValidarResultadoLockSolicitudCupo`); concurrencia real no determinista |
| J | política activa ausente → 500 genérico | STATIC_ONLY | la política está sembrada por 021; no se borra para forzar un 500 |
| K | inconsistencia interna → 500 genérico | STATIC_ONLY | no se corrompen datos para forzar un 500 |
| L | request válido → 200 + `SolicitudCupoResponse` | CI_EPHEMERAL_SAFE | **PASS en CI** (RECIBIDA/PENDIENTE + estado PRE-CALL en BD verificado) |
| REPLAY | misma key + mismo monto → 200 misma solicitud | CI_EPHEMERAL_SAFE | **PASS en CI** (sin duplicar solicitud ni intento) |

---

## Cambio mínimo de CI → **IMPLEMENTADO (2026-09-01)**

Lo descrito abajo se implementó en los dos archivos previstos y se validó en verde (con hardening posterior de las assertions B2/B6 para evitar falsos positivos por PK compuesta o por índice filtrado partido). Ver [Cierre final](#cierre-final--ci-efímero--pr-1--merge-a-main).

**Archivos tocados:**

1. `.github/workflows/backend-validation.yml` — en el step "Crear base de datos y ejecutar migraciones", **después** del loop, añadir una re-ejecución explícita de `database/035_cartera_solicitud_cupo.sql` con `sqlcmd -b` (segunda pasada → prueba la rama idempotente/fail-fast). Objetivo: pasos **D** y **E**.

2. `scripts/validate-backend.sh` — añadir **una** fase nueva (p. ej. "FASE CARTERA-ORIGINACION"):
   - **Assertions estructurales de 035** con `check_sql_value` (pasos C/F/G): existencia de `dbo.cartera_solicitudes_cupo` y `dbo.cartera_solicitud_cupo_intentos`; PK de cada tabla; las 4 FKs de `cartera_solicitudes_cupo` + la FK de intentos; `uq_cartera_solicitud_cupo_intentos_idempotency_key`; `uq_cartera_solicitud_cupo_intentos_solicitud_numero`; `ux_cartera_solicitudes_cupo_usuario_activa` (`is_unique = 1`, filtro con los 5 estados); `is_nullable = 1` para `estado_score`, `resultado_tecnico`, `edad_calculada_al_momento`.
   - **Smoke HTTP** con el usuario `carlos_ci_test` (ya KYC-aprobado por la FASE 1.5) y la política ya sembrada por 021:
     - CASE A: `POST /api/cartera-ordinaria/solicitar-cupo` sin header → 400.
     - CASE B/C/D: header no-GUID / `Guid.Empty` / `montoSolicitado` ≤ 0 → 400.
     - CASE L: request válido con `Idempotency-Key` GUID nuevo → 200 + body con `estadoSolicitud == "RECIBIDA"`, `decisionCrediticia == "PENDIENTE"`, sin campos internos.
     - CASE F: 2ª llamada con otra key → 409.
     - CASE G: misma key que L + `montoSolicitado` distinto → 409.
   - **Fixtures efímeros mínimos:** ninguno nuevo obligatorio (usuario + KYC + política ya existen). CASE H (opcional) requeriría promover un 2º usuario a KYC aprobado con el mismo patrón `UPDATE`.
   - **Assertions mínimas:** códigos HTTP + campos públicos del `SolicitudCupoResponse`; contadores `check_sql_value` sobre `cartera_solicitudes_cupo` / `cartera_solicitud_cupo_intentos`.
   - **Cleanup esperado:** ninguno — BD efímera destruida con el job.

Si en el futuro el workflow ya validara 035 automáticamente y sólo faltaran assertions, bastaría con el punto 2.

---

## Restricciones (respetadas)

- No QA fabricado, no datos reales, no reset de KYC en QA — el smoke corre sólo contra la BD efímera del job.
- No proveedor (DataCrédito / MiDecisor), no decisión crediticia, no cálculo/uso de edad, no uso de score.

---

## Cierre final — CI efímero + PR #1 + merge a main

**Fecha:** 2026-09-01

### Trazabilidad

| Elemento | Valor |
|---|---|
| Commit técnico de rama `feat/cartera-originacion-cupo` | `b4a70e5db07612620f42ec944e7c1081a56f075c` |
| Pull Request | [#1](https://github.com/GabrielPineda71/xpay-mvp/pull/1) |
| Squash merge a `main` | `2cf0ef398be70426151ecc6b87f5bb4b6b94229d` |
| Archivos del commit | 10 (ETAPAS 1–4 + `.github/workflows/backend-validation.yml` + `scripts/validate-backend.sh`), 1168 inserciones, 0 borrados |

### CI del PR #1 — 3/3 PASS

| Workflow | Job | Run | Resultado |
|---|---|---|---|
| Backend Validation | Compile, Run & Test API | `33515657089` | **SUCCESS** (~1m37s) |
| Frontend Build | Build xpay-admin | `33515657086` | **SUCCESS** |
| Dependency Security Scan | Scan NuGet and npm | `33515657182` | **SUCCESS** |

### CI post-merge en `main` — 3/3 PASS

| Workflow | Run | Resultado |
|---|---|---|
| Backend Validation | `33521510010` | **SUCCESS** |
| Frontend Build | `33521510069` | **SUCCESS** |
| Dependency Security Scan | `33521510035` | **SUCCESS** |

### Backend Validation — qué se ejecutó realmente

SQL Server 2022 efímero (`mcr.microsoft.com/mssql/server:2022-latest`, service container de GitHub Actions):

- Migración 035 — aplicación normal (loop `database/*.sql`): **PASS**
- Migración 035 — **segunda ejecución** en el mismo job (rama idempotente / fail-fast): **PASS**
- B1 tablas · B2 PK exactas (hardened) · B3 5 FKs · B4 `UNIQUE (idempotency_key)` · B5 `UNIQUE (id_solicitud, numero_intento)` · B6 índice `UNIQUE` filtrado con 5 estados en el mismo índice (hardened) · B7 nullability (`estado_score`, `edad_calculada_al_momento`, `resultado_tecnico`): **todos PASS**
- Precondiciones: `carlos_ci_test` KYC APROBADO, persona `identidad_verificada=1`, política de crédito ACTIVA (seed migración 021): **PASS**
- Smoke HTTP `POST /api/cartera-ordinaria/solicitar-cupo`:
  - CASE A/B/C/D → **400** + `COUNT` de solicitudes = 0 (sin persistencia): **PASS**
  - CASE L → **200** + `SolicitudCupoResponse` (`RECIBIDA`/`PENDIENTE`); solicitud + 1 intento PRE-CALL con `resultado_tecnico`/`http_status_observado`/`content_status_observado` `NULL` y campos de proveedor/edad `NULL`: **PASS**
  - REPLAY misma key + mismo monto → **200**, misma `idSolicitud`, sin duplicar solicitud ni intento: **PASS**
  - CASE G misma key + monto distinto → **409**, sin mutación: **PASS**
  - CASE F key nueva con solicitud activa → **409**, sin nueva solicitud ni intento: **PASS**

### Endpoint integrado y policy

`POST /api/cartera-ordinaria/solicitar-cupo` — `[Authorize(Policy = "KycAprobado")]` aplicada directamente al action; `idUsuario` desde claim JWT; `Idempotency-Key` obligatoria por header; `correlationId` controlado por servidor; una sola llamada a `CrearSolicitudCupoAsync`.

### Alcance persistido vs. no integrado

- **Persiste:** solicitud (`estado_solicitud = RECIBIDA`, `decision_crediticia = PENDIENTE`) + primer intento **PRE-CALL** (`resultado_tecnico IS NULL`), de forma atómica.
- **NO integra (fuera de alcance, no iniciado):** MiDecisor, DataCrédito, consulta real de score, decisión crediticia real, consentimiento/legal de consulta de riesgo, desembolso, asignación final de cupo por proveedor, Passport/Bre-B real. **No hubo ninguna llamada real a proveedor de riesgo en esta validación.**

### Deuda de infraestructura no bloqueante

GitHub Actions emite un aviso de futura deprecación de **Node.js 20** asociado a `actions/checkout@v4` / `actions/setup-dotnet@v4` (los runners los fuerzan a Node.js 24). Es un aviso de la plataforma, **no un fallo**, y no afecta el resultado funcional de Cartera Ordinaria. Se registra como **deuda de infraestructura no bloqueante** (actualizar las versiones de las actions en una tarea de mantenimiento de CI independiente).

---

## Próximo paso

La originación estructural E1–E4 + CI está cerrada. El siguiente bloque de trabajo — **fuera del alcance de estas etapas y no iniciado** — es el diseño de la integración real del proveedor de riesgo (MiDecisor / DataCrédito): contrato, consentimiento/legal de consulta, TX1 (decisión) y TX2 (materialización de cupo), transiciones de estado posteriores a `RECIBIDA`. Nada de eso debe activarse hasta que exista una decisión legal/de producto válida.
