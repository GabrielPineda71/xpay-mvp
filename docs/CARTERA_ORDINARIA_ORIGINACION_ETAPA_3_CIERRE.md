# XPAY — Cartera Ordinaria — Cierre Etapa 3

**Línea de trabajo:** Originación automática de cupo de Cartera Ordinaria (vía MiDecisor/DataCrédito — aún no integrado)
**Fecha UTC:** 2026-08-31
**Responsable:** Gabriel Alfonso Pineda Ortiz `gabrielalfonsopineda@gmail.com`
**Ambiente:** desarrollo local — sin QA, sin producción, sin Azure

---

## Estado

**ETAPA 3: CLOSED** · Originación estructural **ETAPAS 1–4 + CI: CLOSED** (actualización 2026-09-01)

- ETAPA 1: COMPLETA — validación dinámica de la migración 035 **completada en CI efímero** (2026-09-01; antes pendiente por infraestructura local)
- ETAPA 2: COMPLETA
- ETAPA 3: CLOSED
- HARDENING 017: COMPLETO
- ETAPA 4: CLOSED (endpoint + KYC) — ver documento de ETAPA 4
- Merge a `main`: PR #1 → squash `2cf0ef398be70426151ecc6b87f5bb4b6b94229d`

Todos los `dotnet build` de `backend/Xpay.Api/Xpay.Api.csproj` posteriores a cada etapa: **PASS, 0 warnings, 0 errores.**
Ningún SQL ejecutado. Migración 035 no aplicada. *(Estado al momento del cierre original de ETAPA 3 — 2026-08-31.)*

**Actualización 2026-09-01:** la validación dinámica que en ese momento estaba pendiente por falta de infraestructura local quedó resuelta vía CI efímero (GitHub Actions), y el trabajo llegó a `main` mediante PR #1. Ver [Cierre final — CI efímero + PR #1 + merge a main](#cierre-final--ci-efímero--pr-1--merge-a-main) al final de este documento.

---

## Alcance completado

### Etapa 1 — schema + entidades (código completo)

- `database/035_cartera_solicitud_cupo.sql` — dos tablas nuevas: `dbo.cartera_solicitudes_cupo` y `dbo.cartera_solicitud_cupo_intentos`. Idempotente y fail-fast (mismo patrón que 029/031/034): crea con PK/FK/UNIQUE/CHECK si no existen; si ya existen, valida estructura crítica y aborta con `THROW` sin reparar. Índice `UNIQUE` filtrado `ux_cartera_solicitudes_cupo_usuario_activa` (una solicitud activa por usuario). `UNIQUE (idempotency_key)` y `UNIQUE (id_solicitud, numero_intento)` en la tabla de intentos.
- `backend/Xpay.Api/Models/CarteraSolicitudCupo.cs`
- `backend/Xpay.Api/Models/CarteraSolicitudCupoIntento.cs`
- `backend/Xpay.Api/Data/XpayDbContext.cs` — DbSets `CarteraSolicitudesCupo` / `CarteraSolicitudCupoIntentos` + mappings `MapCarteraSolicitudCupo` / `MapCarteraSolicitudCupoIntento` (sólo `HasColumnName` / tipos; sin `IsRequired` manual).

Correcciones PRE-CALL incorporadas al schema y al modelo (la solicitud/intento se persisten **antes** de llamar al proveedor):

| Columna | Antes | Ahora | Significado de NULL |
|---|---|---|---|
| `estado_score` | NOT NULL | **NULL** | todavía no se ha evaluado respuesta del proveedor |
| `resultado_tecnico` (intento) | NOT NULL | **NULL** | intento insertado PRE-CALL, TX1 (etapa posterior) lo completa |
| `edad_calculada_al_momento` | NOT NULL | **NULL** (decisión 016) | edad no calculada / no disponible |

El `CHECK (edad_calculada_al_momento >= 0)` se mantiene sin cambios: `NULL >= 0` evalúa a `UNKNOWN` (no `FALSE`), por lo que sólo restringe valores negativos reales.

**Decisión 016 (aplicada y verificada):** `edad_calculada_al_momento` es nullable. NO se calcula edad en PRE-CALL. NO se usa edad para aprobar/rechazar crédito. No existe precedente determinista de cálculo de edad en el backend y `Persona.FechaNacimiento` es nullable; fabricar el dato (p. ej. `0`) habría sido un valor falso.

### Etapa 2 — contratos (completa)

- `backend/Xpay.Api/DTOs/CarteraSolicitudCupoDtos.cs`
  - `SolicitarCupoRequest(decimal MontoSolicitado)` — único campo de body; usuario/persona se derivan del contexto autenticado (etapa posterior); la Idempotency-Key llega por header HTTP, nunca en el body.
  - `SolicitudCupoResponse(long IdSolicitud, decimal MontoSolicitado, string EstadoSolicitud, string DecisionCrediticia, decimal? MontoAprobado, string? CodigoMotivoDecision, DateTime FechaSolicitud, DateTime? FechaDecision, long? IdCupoOrdinario)` — proyección pública; NO expone score, edad, snapshot de política, viabilidad, rating, monto sugerido, correlationId ni detalle técnico del intento.
- `backend/Xpay.Api/Common/CarteraSolicitudCupoEstados.cs`
  - `CarteraSolicitudCupoEstados`: `RECIBIDA`, `VALIDANDO`, `CONSULTANDO_RIESGO`, `EN_EVALUACION`, `APROBADA_PENDIENTE_CUPO`, `APROBADA`, `RECHAZADA`, `ERROR_PROVEEDOR`.
  - `CarteraDecisionCrediticia`: `PENDIENTE`, `APROBADA`, `RECHAZADA`.
  - Estructural: enumera valores autorizados. NO implica transición automática, elegibilidad, scoring ni decisión de rechazo.

### Etapa 3 — creación segura de solicitud + idempotencia PRE-CALL (cerrada)

Implementado sólo en `backend/Xpay.Api/Services/CarteraOrdinariaService.cs`.

```csharp
public async Task<SolicitudCupoResponse> CrearSolicitudCupoAsync(
    long idUsuario,
    Guid idempotencyKey,
    decimal montoSolicitado,
    string correlationId)
```

Comportamiento:

1. Validaciones estructurales: `montoSolicitado > 0`, `idempotencyKey != Guid.Empty`, `correlationId` no vacío (`ArgumentException` → 400 en la convención actual del service).
2. `BeginTransactionAsync`.
3. `AppLockHelper.AdquirirAsync(db, "XPAY:CARTERA_SOLICITUD_CUPO:{idUsuario}")` dentro de la transacción, antes de cualquier otra consulta de la sección crítica (mismo patrón que `KycService`). `ValidarResultadoLockSolicitudCupo` interpreta el retorno de `sp_getapplock`: 0/1 = adquirido; -1/-2/-3 → `InvalidOperationException` ("otra solicitud en proceso"); otro código → `Exception` sin tipar.
4. Replay por Idempotency-Key: busca `CarteraSolicitudCupoIntento` por `IdempotencyKey` (releído dentro del lock). Si existe → `ReplayValidadoOConflicto` (ver hardening 017) y devuelve sin crear nada.
5. Resuelve `Usuario` por `idUsuario` (`KeyNotFoundException` si no existe).
6. Resuelve `Persona` por `usuario.IdPersona` (`KeyNotFoundException` si no existe).
7. Resuelve política activa: `Estado == "ACTIVO"` + `OrderByDescending(VigenteDesde)` (idéntico a `GetPoliticaVigenteAsync`). `InvalidOperationException` si no hay.
8. Detecta solicitud activa del usuario: `AnyAsync(s => s.IdUsuario == idUsuario && EstadosSolicitudActivos.Contains(s.EstadoSolicitud))` (los 5 estados activos del índice filtrado 035). Si hay → `InvalidOperationException` ("Ya tienes una solicitud de cupo en curso").
9. Crea `CarteraSolicitudCupo`:
   - `EstadoSolicitud = RECIBIDA`, `DecisionCrediticia = PENDIENTE`
   - `MontoAprobado = null`, `CodigoMotivoDecision = null`
   - Snapshot de política (sólo auditoría, NO se compara contra el usuario): `IdPoliticaAplicada`, `ScoreDatacreditoMinimoAplicado`, `CupoMinimoAplicado`, `CupoMaximoAplicado`, `EdadMinimaAplicada`, `EdadMaximaAplicada`
   - `EdadCalculadaAlMomento = null`
   - Campos de proveedor iniciales `null`: `ScoreObservado`, `EstadoScore`, `ViabilidadObservada`, `RatingRecaudosObservado`, `MontoSugeridoObservado`
   - `NumeroIntento = 1`, `IdCupoOrdinario = null`, `FechaDecision = null`, `FechaMaterializacionCupo = null`
   - `CorrelationId = correlationId`, `FechaSolicitud = FechaActualizacion = DateTime.UtcNow`
   - `SaveChangesAsync()` → genera `IdSolicitud` (EF).
10. Crea `CarteraSolicitudCupoIntento`: `IdSolicitud` recién generado, `NumeroIntento = 1`, `IdempotencyKey`, `FechaInicio = UtcNow`, `FechaFin = null`, `ResultadoTecnico = null`, `HttpStatusObservado = null`, `ContentStatusObservado = null`, `CorrelationId`, `EsIntentoConResultadoUtil = false`. `SaveChangesAsync()`.
11. `CommitAsync()` → devuelve `ToSolicitudResponse(solicitud)`.

Si falla la creación del intento, el `catch` hace `RollbackAsync()` y la solicitud no queda persistida (una sola transacción; no se abre una segunda para el intento).

**NO** proveedor. **NO** HTTP externo. **NO** decisión automática. **NO** cálculo de edad. **NO** uso de score. **NO** endpoint. **NO** cambios posteriores a `RECIBIDA`.

### Hardening 017 — idempotencia vinculada a usuario + payload (completo)

Una Idempotency-Key existente sólo es replay válido cuando (`ReplayValidadoOConflicto`):

1. `solicitudPrevia.IdUsuario == idUsuario` (ownership), **y**
2. `solicitudPrevia.MontoSolicitado == montoSolicitado` (mismo request — único campo de body; sin request hash).

| Caso | Entrada | Resultado |
|---|---|---|
| A | misma key + mismo usuario + mismo monto | replay válido — devuelve `SolicitudCupoResponse` existente |
| B | misma key + mismo usuario + monto distinto | `InvalidOperationException("Idempotency-Key ya utilizada con parámetros diferentes.")` — nada creado, sin datos previos |
| C | misma key + usuario distinto | `InvalidOperationException("Idempotency-Key ya utilizada para otra solicitud.")` — sin exposición de datos del otro usuario |
| D | key nueva + usuario sin solicitud activa | creación normal |

Las mismas dos comprobaciones se aplican en el camino posterior a una violación `UNIQUE` (`catch (Exception ex) when (SqlExceptionHelper.IsUniqueViolation(ex))`): rollback → `ChangeTracker.Clear()` → busca intento ganador por key → si aparece, `ReplayValidadoOConflicto`; si no aparece, conflicto de solicitud activa. Los mensajes de conflicto nunca incluyen `idUsuario`, `IdSolicitud`, monto ni dato personal.

---

## Invariantes actuales

- Una solicitud activa por usuario (aplicación + índice `UNIQUE` filtrado de BD).
- Idempotency-Key globalmente única (`UNIQUE (idempotency_key)` en `cartera_solicitud_cupo_intentos`).
- Replay sólo si mismo usuario **y** mismo monto.
- Solicitud + primer intento creados atómicamente (una sola transacción local).
- Estado inicial de solicitud: `RECIBIDA`.
- Decisión crediticia inicial: `PENDIENTE`.
- Campos de proveedor `NULL` en PRE-CALL (`ScoreObservado`, `EstadoScore`, `ViabilidadObservada`, `RatingRecaudosObservado`, `MontoSugeridoObservado`).
- `EdadCalculadaAlMomento = NULL` en PRE-CALL.
- `resultado_tecnico` del intento `NULL` en PRE-CALL.
- No hay integración de proveedor.
- No hay decisión crediticia automática.

---

## Archivos involucrados

**Etapa 1:**
- `database/035_cartera_solicitud_cupo.sql`
- `backend/Xpay.Api/Models/CarteraSolicitudCupo.cs`
- `backend/Xpay.Api/Models/CarteraSolicitudCupoIntento.cs`
- `backend/Xpay.Api/Data/XpayDbContext.cs`

**Etapa 2:**
- `backend/Xpay.Api/DTOs/CarteraSolicitudCupoDtos.cs`
- `backend/Xpay.Api/Common/CarteraSolicitudCupoEstados.cs`

**Etapa 3 + hardening 017:**
- `backend/Xpay.Api/Services/CarteraOrdinariaService.cs`

**Reutilizados sin modificar:**
- `backend/Xpay.Api/Common/AppLockHelper.cs`
- `backend/Xpay.Api/Common/SqlExceptionHelper.cs`
- `backend/Xpay.Api/Models/CarteraPoliticaCredito.cs`, `Usuario.cs`, `Persona.cs`

---

## Validación realizada

*(Estado al cierre original de ETAPA 3 — 2026-08-31. La validación dinámica se completó después en CI: ver [Cierre final](#cierre-final--ci-efímero--pr-1--merge-a-main).)*

- `dotnet build backend/Xpay.Api/Xpay.Api.csproj` → **PASS** (0 warnings, 0 errores) tras cada etapa y tras hardening 017.
- Trazado por lectura de código de los 4 casos de idempotencia (A/B/C/D) y del camino posterior a `UNIQUE`.
- En ese momento: **no** se había ejecutado SQL, **no** se había aplicado la migración 035, **no** se había ejecutado el método contra una base de datos. *(Resuelto el 2026-09-01 — la migración 035 se aplicó y re-ejecutó, y el método se ejercitó vía el endpoint contra SQL Server efímero en CI.)*

---

## Pendientes no bloqueantes

1. ~~**Migración 035 — validación dinámica pendiente.**~~ **RESUELTO (2026-09-01).** Aplicada + re-ejecutada contra SQL Server 2022 efímero en CI (GitHub Actions) — ambas ejecuciones PASS. Ver [Cierre final](#cierre-final--ci-efímero--pr-1--merge-a-main).
2. ~~**Endpoint inexistente.**~~ **RESUELTO en ETAPA 4.** `POST /api/cartera-ordinaria/solicitar-cupo` implementado — ver `docs/CARTERA_ORDINARIA_ORIGINACION_ETAPA_4_CIERRE.md`.
3. ~~**KYC no conectado.**~~ **RESUELTO en ETAPA 4.** `[Authorize(Policy = "KycAprobado")]` aplicado directamente al action de `solicitar-cupo`.
4. **Race residual de idempotencia.** Ventana muy pequeña tras una violación `UNIQUE` donde la fila ganadora podría no ser visible de inmediato en la re-lectura; hoy se devuelve un conflicto conservador. No resolver ahora.
5. ~~**HTTP status de conflictos.**~~ **RESUELTO en ETAPA 4.** El controller mapea los conflictos de originación/idempotencia/concurrencia a **409** (`EsConflictoSolicitudCupo` sobre la lista exacta de mensajes); el resto de `InvalidOperationException` (config/inconsistencia) se propaga a `ErrorHandlingMiddleware` → 500 genérico. Nota histórica: una excepción de concurrencia dedicada (precedente `KycUsuarioConcurrenteException`) queda como posible refinamiento futuro, no bloqueante.
6. **Proveedor ausente.** No existe integración real DataCrédito/MiDecisor en el repo. No re-ejecutar pruebas históricas A/B.
7. **Decisión crediticia ausente.** No hay política automatizada, uso autorizado de score, uso de edad, viabilidad, rating, monto sugerido, TX1 ni TX2. Esperado.

---

## Restricción de alto impacto

- `EdadMinima` / `EdadMaxima` existentes en la política **NO** equivalen a autorización para rechazo automático.
- `EdadCalculadaAlMomento = NULL` en PRE-CALL; **no** existe `RECHAZADA_EDAD`.
- `ScoreDatacreditoMinimo` existente **NO** se está utilizando todavía para decidir; **no** existe `RECHAZADA_SCORE`.
- PEP/OFAC/alertas futuras serían señales de compliance, **no** criterio de scoring.
- No activar lógica crediticia hasta que exista una decisión legal/de producto válida.

---

## Próxima etapa *(histórico — ETAPA 4 ya implementada y cerrada)*

Lo que sigue era el plan de ETAPA 4 al momento del cierre de ETAPA 3. **Ya está hecho** — ver `docs/CARTERA_ORDINARIA_ORIGINACION_ETAPA_4_CIERRE.md`.

**ETAPA 4** — diseñar/implementar únicamente:

`POST /api/cartera-ordinaria/solicitar-cupo`

con:

- `[Authorize(Policy = "KycAprobado")]`
- `idUsuario` desde el claim JWT (`User.FindFirst("idUsuario")`, patrón `IdUsuarioActual` del controller actual)
- `Idempotency-Key` desde el header HTTP (GUID)
- body `SolicitarCupoRequest`
- `correlationId` según la convención existente del proyecto
- llamada a `CrearSolicitudCupoAsync(idUsuario, idempotencyKey, montoSolicitado, correlationId)`
- mapping explícito de errores HTTP (definir 400 vs 404 vs 409 para los conflictos de idempotencia/concurrencia)

**Todavía sin:** DataCrédito, MiDecisor, score, edad, llamada a proveedor, decisión crediticia.

*(ETAPA 4 fue implementada y cerrada — ver `docs/CARTERA_ORDINARIA_ORIGINACION_ETAPA_4_CIERRE.md`.)*

---

## Cierre final — CI efímero + PR #1 + merge a main

**Fecha:** 2026-09-01

La originación estructural de cupo **ETAPAS 1–4 + validación CI** queda **CLOSED**. La validación dinámica de la migración 035, antes bloqueada por ausencia de SQL Server local, se completó en CI efímero.

### Trazabilidad

| Elemento | Valor |
|---|---|
| Commit técnico de rama `feat/cartera-originacion-cupo` | `b4a70e5db07612620f42ec944e7c1081a56f075c` |
| Pull Request | [#1](https://github.com/GabrielPineda71/xpay-mvp/pull/1) |
| Squash merge a `main` | `2cf0ef398be70426151ecc6b87f5bb4b6b94229d` |
| Archivos del commit | 10 (los de ETAPAS 1–4 + `.github/workflows/backend-validation.yml` + `scripts/validate-backend.sh`), 1168 inserciones, 0 borrados |

### CI del PR #1 — los 3 workflows PASS

- Backend Validation (`Compile, Run & Test API`) — run `33515657089` — **SUCCESS** (~1m37s)
- Frontend Build (`Build xpay-admin`) — run `33515657086` — **SUCCESS**
- Dependency Security Scan — run `33515657182` — **SUCCESS**

### CI post-merge en `main` — los 3 workflows PASS

- Backend Validation — run `33521510010` — **SUCCESS**
- Frontend Build — run `33521510069` — **SUCCESS**
- Dependency Security Scan — run `33521510035` — **SUCCESS**

### Backend Validation — qué se ejecutó realmente (SQL Server 2022 efímero, `mcr.microsoft.com/mssql/server:2022-latest`)

| Verificación | Resultado |
|---|---|
| Migración 035 — aplicación normal (loop `database/*.sql`) | PASS |
| Migración 035 — **segunda ejecución** en el mismo job (rama idempotente/fail-fast) | PASS |
| B1 — tablas `dbo.cartera_solicitudes_cupo`, `dbo.cartera_solicitud_cupo_intentos` | PASS |
| B2 — PK **exactamente** `(id_solicitud)` / `(id_intento)` (hardened 023: `key_ordinal=1`, `is_included_column=0`, 1 sola key column) | PASS |
| B3 — las 5 FKs (child.col → ref.col) | PASS |
| B4 — `UNIQUE (idempotency_key)` (unicolumna, no filtrado) | PASS |
| B5 — `UNIQUE (id_solicitud, numero_intento)` en ese orden | PASS |
| B6 — índice `UNIQUE` filtrado por `id_usuario` con los 5 estados activos, **todo en el mismo índice** (hardened 023: una sola query correlacionada) | PASS |
| B7 — nullability real: `estado_score`, `edad_calculada_al_momento`, `resultado_tecnico` | PASS |
| Precondiciones — `carlos_ci_test` KYC APROBADO, persona `identidad_verificada=1`, política de crédito ACTIVA (seed migración 021) | PASS |
| CASE A/B/C/D → HTTP 400, y `COUNT` de solicitudes del usuario = 0 (sin persistencia) | PASS |
| CASE L → HTTP 200 + `SolicitudCupoResponse` (`RECIBIDA`/`PENDIENTE`); solicitud + 1 intento PRE-CALL con columnas de resultado `NULL`, campos de proveedor/edad `NULL` | PASS |
| REPLAY misma key + mismo monto → HTTP 200, misma `idSolicitud`, sin duplicar solicitud ni intento | PASS |
| CASE G misma key + monto distinto → HTTP 409, sin mutación | PASS |
| CASE F key nueva con solicitud activa → HTTP 409, sin nueva solicitud ni intento | PASS |

### Endpoint integrado

`POST /api/cartera-ordinaria/solicitar-cupo` — `[Authorize(Policy = "KycAprobado")]`, `idUsuario` desde claim JWT, `Idempotency-Key` obligatoria por header, `correlationId` controlado por servidor, una sola llamada a `CrearSolicitudCupoAsync`.

### Lo que esta etapa persiste — y lo que NO integra

- **Persiste:** la solicitud (`estado_solicitud = RECIBIDA`, `decision_crediticia = PENDIENTE`) y su primer intento **PRE-CALL** (`resultado_tecnico IS NULL`), de forma atómica.
- **NO integra:** MiDecisor, DataCrédito, consulta real de score, decisión crediticia real, consentimiento/legal de consulta de riesgo, desembolso, asignación final de cupo por proveedor, Passport/Bre-B real. **No hubo ninguna llamada real a proveedor de riesgo en esta validación.**

### Deuda no bloqueante

- **Infraestructura CI (no bloqueante):** GitHub Actions emite un aviso de futura deprecación de Node.js 20 asociado a `actions/checkout@v4` / `actions/setup-dotnet@v4`. Es un aviso de la plataforma; no afecta el resultado funcional de Cartera Ordinaria. Ver el detalle equivalente en `docs/CARTERA_ORDINARIA_ORIGINACION_ETAPA_4_CIERRE.md`.
- Los pendientes técnicos #4 (race residual de idempotencia) y #5 (mapping HTTP — ya resuelto a 409 en ETAPA 4 para los conflictos de originación) se mantienen como notas históricas; ninguno bloquea.
