# MiDecisor / DataCrédito — Preflight de Producción (Continuidad XPAY-541)

## ⚠️ PREFLIGHT OFFLINE — CERO RED — NINGUNA ACTIVACIÓN

Auditoría 100% offline (XPAY-583). Cero llamadas a MiDecisor, cero OAuth
real, cero cambios de código/config/DB/despliegue. Continúa desde
XPAY-541 sin repetirlo — corrige y actualiza sus conclusiones con
evidencia de código y con hallazgos de gobernanza más recientes que
XPAY-541 no incorporaba.

## 1. Baseline

- `HEAD` confirmado, `git status` preservado, cero cambios realizados.
- El trabajo de Passport recién enviado (`docs/certificacion/passport-breb/`,
  `XPAY_M5_ENVIO_PASSPORT/`) no fue tocado.
- No se modificó env, secretos, ni configuración desplegada.

## 2. Corrección crítica a la premisa de este ticket

Este ticket asumía que la conclusión de XPAY-541 sobre la URL UAT
(`https://uat-api.datacredito.com.co`) era una base confirmada. **La
inspección de gobernanza más reciente contradice esa premisa.**

El registro `docs/GOVERNANCE_BLOCKERS_AND_DECISIONS_REGISTER.md`
documenta **tres blockers abiertos**, todos con fecha de apertura
`2026-09-12` — **posteriores** a XPAY-541 — que XPAY-541 no pudo haber
incorporado:

| ID | Asunto | Estado | Implicación |
|---|---|---|---|
| `XGOV-B-0001` | Titularidad de credenciales (XPAY vs. DAFIN/Xelecredit) | `DECISION_PENDIENTE` | `PRIMARY_NEXT_BLOCKER` — ninguna credencial debe asumirse/reutilizarse hasta resolver esto |
| `XGOV-B-0002` | Ambiente/host `MIDECISOR_BASE_URL` asignado a XPAY | `BLOQUEADO_EXTERNO` | El valor **no debe inferirse** de defaults, ejemplos, ni de otro proyecto — **no existe URL confirmada hoy** |
| `XGOV-B-0003` | Ruta de consulta `/client` vs `/pn` | `BLOQUEADO_EXTERNO` | El default actual (`/co/cs/midecisor/v1/client`) está marcado en el propio código como `STRUCTURAL_DEFAULT` — **no es autorización contractual** |

Las tres entradas dicen explícitamente: *"no debe iniciarse UAT real
contra MiDecisor"* mientras permanezcan abiertas. Este documento por
tanto **no confirma** ningún valor de `MIDECISOR_BASE_URL` como listo
para usar — ni el mencionado en este ticket ni ningún otro.

## 3. Validación de XPAY-541 contra el código actual

| Conclusión XPAY-541 | Verificación de código (XPAY-583) |
|---|---|
| Persona Natural implementada | **Confirmado** — `PersonaMiDecisorRequestMapper`, `TipoDocumentoMiDecisorMapper`, `MiDecisorConsultaRequest` completos |
| Persona Jurídica no implementada por diseño | **Confirmado** — `TipoDocumentoMiDecisorMapper` excluye explícitamente NIT/PJE ("fuera del flujo PN"), sin ningún flujo de producto que lo requiera hoy |
| Config requerida (5 variables) | **Confirmado** — `MiDecisorOptions.cs` define exactamente esas 5 + 4 no-secretas (`AUTH_PATH`, `QUERY_PATH`, `TIMEOUT_SECONDS`, `TOKEN_SAFETY_MARGIN_SECONDS`) |
| Configuración local ausente | **Confirmado** — 0 variables `MIDECISOR_*` en `~/.passport-sandbox.env`, y el registro de gobernanza confirma 0 en Azure `xpay-api-qa` (XPAY-250) |
| Cliente OAuth existente/listo | **Confirmado — y más completo de lo que la frase sugiere**: `MiDecisorTokenProvider` es una implementación REAL, no un stub — cache en memoria, `SemaphoreSlim` anti-ráfaga, timeout, sin reintentos, logging saneado |
| `IConsultaRiesgoAutorizacion` conectado a un stub no disponible | **Confirmado, y matizado**: NO es el cliente MiDecisor el que está stubbeado — es la **barrera de consentimiento/autorización legal** (`AutorizacionConsultaRiesgoNoDisponible`, devuelve `false` siempre, deliberado). El cliente HTTP real (`MiDecisorClient`) ya existe y está completo. |
| Tests 60/60 PASS | Los tests específicos de MiDecisor pasan; la suite ampliada Cartera+MiDecisor ejecutada en este ticket da **408/408 PASS**, 0 llamadas de red, ~4s |
| Network calls: 0 | Confirmado en este ticket también: 0 |

`XPAY_541_CONTINUITY_CONFIRMED=YES` (con la corrección de la sección 2 y
el matiz de esta tabla).

## 4. Arquitectura actual — hallazgo central

Existen **dos barreras independientes**, ambas necesarias para que una
llamada real llegue a MiDecisor (documentado explícitamente en el propio
código, `CarteraRiesgoRuntimeWiring.cs`):

1. **Barrera de autorización/consentimiento** — `IConsultaRiesgoAutorizacion`
   → hoy `AutorizacionConsultaRiesgoNoDisponible` (fail-closed, siempre
   `false`). Existe una implementación real ya construida,
   `AutorizacionConsultaRiesgoDurable` (respaldada por un store de
   consentimiento, migración 041), **pero no está registrada en DI** —
   el comentario del propio wiring dice literalmente *"NO reemplazar
   aquí"*.
2. **Barrera estructural** — ningún endpoint/scheduler invoca hoy el
   orquestador `CarteraDecisionCrediticiaOrchestrator` en un flujo de
   usuario real más allá de lo ya expuesto por M2.4d
   (`iniciar-evaluacion`, `GET solicitud`, reconciliación admin) — el
   flujo SÍ está expuesto vía endpoint, pero **queda inerte** porque la
   barrera 1 nunca deja pasar nada.

`CONSULTA_INTERFACE_FOUND=YES`
`CURRENT_IMPLEMENTATION=AutorizacionConsultaRiesgoNoDisponible (barrera de consentimiento, fail-closed por diseño — NO es el cliente HTTP)`
`CURRENT_IMPLEMENTATION_IS_STUB=YES` (la barrera de consentimiento sí es un stub deliberado; el cliente MiDecisor NO lo es)
`MIDECISOR_CLIENT_FOUND=YES` (`MiDecisorClient` — implementación HTTP completa)
`OAUTH_CLIENT_FOUND=YES` (`MiDecisorTokenProvider` — completo, con cache/concurrencia)
`PRODUCT_ENDPOINT_FOUND=YES` (`iniciar-evaluacion`/`GET solicitud`/reconciliación admin, vía M2.4d — pero inerte por la barrera 1)

## 5. Contrato del manual — acceso NO disponible

**No se encontró** el archivo *"Manual de implementación API MiDecisor
v1.0.pdf"* en el repositorio ni en el sistema de archivos local
(búsqueda exhaustiva, incluyendo Spotlight). `MANUAL_ACCESSIBLE=NO`.

Lo que se documenta abajo proviene **exclusivamente** de hechos ya
extraídos y citados textualmente en comentarios de código de sesiones
previas (nunca inventados ahora) — cada uno etiquetado con su fuente:

| Aspecto | Valor | Fuente | Estado |
|---|---|---|---|
| Endpoint de token (`AUTH_PATH`) | `/spla/oauth2/v1/token` | Comentario `MiDecisorOptions.cs`: *"ruta del contrato oficial confirmado"* | `MANUAL_OFICIAL` (confirmado, no bloqueado) |
| Endpoint de consulta (`QUERY_PATH`) | `/co/cs/midecisor/v1/client` (default) vs. `/pn` (alternativa soportada técnicamente) | Comentario: *"las dos fuentes Swagger"* | `IMPLEMENTACION_ACTUAL_XPAY` únicamente — **`GAP`**: `XGOV-B-0003` abierto, ninguna de las dos confirmada contractualmente |
| Ambiente UAT/host | Sin valor confirmado | — | `GAP` — `XGOV-B-0002` abierto |
| Ambiente producción | Sin valor confirmado | — | `UNKNOWN_FROM_MANUAL` |
| Mecanismo de auth | OAuth2, headers `Client_id`/`Client_secret` + body `{username, password}` | Implementado en `MiDecisorTokenProvider`/`MiDecisorAuthRequest` | `IMPLEMENTACION_ACTUAL_XPAY` (asumido correcto por construcción previa, sin re-verificación posible sin el manual) |
| Response token | JSON con `access_token` (string) y `expires_in` (string numérico) | `MiDecisorTokenResponse` | `IMPLEMENTACION_ACTUAL_XPAY` |
| Request de consulta PN | `{tipoIdentificacion, numeroIdentificacion, apellidoRazonSocial}` | `MiDecisorConsultaRequest` | `IMPLEMENTACION_ACTUAL_XPAY` |
| Response de consulta | Envelope `{status, content:{status, respuesta:{informacionRiesgo:{...}, validacion:{...}}, infoTransaccion}}` | `MiDecisorRespuestaEnvelope` + lógica de `MapearEnvelope` en `MiDecisorClient.cs` | `IMPLEMENTACION_ACTUAL_XPAY` |
| Status del envelope | `"ACCEPTED"` (éxito) / `"PRECONDITION_FAILED"` (rechazo) | `MiDecisorClient.MapearEnvelope` | `IMPLEMENTACION_ACTUAL_XPAY` |
| Códigos de tipo de documento PN | CC→1, CE→4, PAS→5, CD→6, TI→7, DNI→8, PEP→9 | `TipoDocumentoMiDecisorMapper`, citado como "Swagger de MiDecisor" | `IMPLEMENTACION_ACTUAL_XPAY` |
| Manejo de errores HTTP | 401/403 → `MiDecisorAuthenticationException`; cualquier otro no-2xx → `MiDecisorTransportException`; sin reintentos | `MiDecisorClient.cs`/`MiDecisorTokenProvider.cs` | `IMPLEMENTACION_ACTUAL_XPAY` (decisión propia de XPAY, no necesariamente exigida por el proveedor) |

**Ninguno de estos valores puede reverificarse contra el manual oficial
en este ticket** — el documento no está disponible. Se recomienda
localizarlo (fuera de este repositorio) antes de UAT real.

## 6. Mapping Persona Natural

| CAMPO_XPAY | CAMPO_MIDECISOR | REQUIRED | MAPPING_STATUS | NOTES |
|---|---|---|---|---|
| `Persona.TipoDocumento` (declarado) | `tipoIdentificacion` | YES | `TRANSFORMATION_REQUIRED` | Vía `TipoDocumentoMiDecisorMapper` (CC/CE/PAS/CD/TI/DNI/PEP → "1".."9") |
| `Persona.NumeroDocumento` (declarado) | `numeroIdentificacion` | YES | `MATCH` | Validado: 3–13 dígitos ASCII |
| `Persona.PrimerApellido` (declarado) | `apellidoRazonSocial` | YES | `MATCH` | No usa el apellido verificado por KYC — decisión estructural documentada |

**Nota abierta, no resuelta en este ticket**: el propio código de
`PersonaMiDecisorRequestMapper` señala explícitamente
`MAPPING_AUTHORITY sigue UNRESOLVED` — usa los campos **declarados** por
el usuario, no los verificados por KYC (Veriff), y el código aclara que
esto "no declara que estos campos sean la autoridad definitiva para
UAT". Es una decisión de producto pendiente, no un defecto técnico.

`PERSONA_NATURAL_MAPPING=COMPLETE` (los 3 campos requeridos por el
request PN están mapeados; la brecha real no es de mapeo sino de
contrato/gobernanza — secciones 2 y 5).

## 7. Persona Jurídica

Confirmado por código (`TipoDocumentoMiDecisorMapper.cs`): NIT ("2") y
PJE ("3") están **explícitamente excluidos** del flujo, por diseño —
"fuera del flujo PN". No existe ningún flujo de producto (Cartera
Ordinaria u otro) que hoy intente una consulta de riesgo para Persona
Jurídica.

`PERSONA_JURIDICA_STATUS=No implementada, exclusión deliberada de diseño`
`PERSONA_JURIDICA_BLOCKS_CURRENT_PRODUCTION=NO` (no bloquea porque
ningún flujo de producto actual la requiere — no es una brecha respecto
al alcance vigente).

## 8. De stub a implementación real — qué falta (diseño, no implementado aquí)

**El "stub" a reemplazar es la barrera de consentimiento, no el cliente
MiDecisor** (que ya existe completo). Lo que falta:

- **Clase concreta**: ya existe (`AutorizacionConsultaRiesgoDurable`) —
  no hay que escribir código nuevo para esta pieza específica.
- **DI registration**: cambiar una línea en `CarteraRiesgoRuntimeWiring.cs`
  (swap del stub) — deliberadamente marcada "NO reemplazar aquí" hasta
  autorización explícita separada.
- **Configuración**: las 5 variables `MIDECISOR_*` — ausentes hoy, y
  bloqueadas por `XGOV-B-0001`/`XGOV-B-0002` antes de poder provisionarse
  con valores reales.
- **Manejo de token**: ya implementado (`MiDecisorTokenProvider`).
- **Llamada API / mapping request/response**: ya implementado
  (`MiDecisorClient`, `PersonaMiDecisorRequestMapper`).
- **Timeout/errores**: ya implementado (fail-closed, sin reintentos).
- **Logging saneado / protección PII**: ya implementado (nunca
  documento/apellido/token/headers/bodies).
- **Tests**: ya existen para cada pieza individual (408 tests
  Cartera+MiDecisor, offline). Faltarían tests de integración contra un
  contrato REAL una vez `XGOV-B-0002`/`XGOV-B-0003` se resuelvan (hoy
  cualquier test así sería contra una suposición, no contra el contrato
  verdadero).

**No existe ninguna clase parcial huérfana que deba descartarse** — todo
lo construido hasta ahora es reutilizable tal cual.

`STUB_REPLACEMENT_READY_TO_IMPLEMENT=NO` — técnicamente el swap de DI es
trivial, pero autorizarlo sin resolver `XGOV-B-0001` primero significaría
habilitar consultas reales sin saber de quién son las credenciales que
se usarían — no es una decisión de ingeniería, es una decisión de
gobernanza pendiente.

## 9. Credenciales — sólo presencia, nunca valores

| Variable | Estado |
|---|---|
| `MIDECISOR_BASE_URL` | MISSING |
| `MIDECISOR_CLIENT_ID` | MISSING |
| `MIDECISOR_CLIENT_SECRET` | MISSING |
| `MIDECISOR_USERNAME` | MISSING |
| `MIDECISOR_PASSWORD` | MISSING |
| `MIDECISOR_AUTH_PATH` | MISSING (tiene default estructural, no requiere valor) |
| `MIDECISOR_QUERY_PATH` | MISSING (tiene default estructural, no requiere valor) |

Verificado localmente (`~/.passport-sandbox.env`, 0 coincidencias) y
confirmado independientemente por el registro de gobernanza para Azure
`xpay-api-qa` (XPAY-250: 0 variables `MIDECISOR_*`). Mecanismo ya
establecido para QA/Producción: Azure App Settings exclusivamente
(mismo patrón ya usado para Passport/Veriff) — nunca `appsettings.json`,
nunca el repositorio.

## 10. Ruta UAT → Producción

| Fase | Contenido | Condición para pasar a la siguiente |
|---|---|---|
| A — Implementación offline | Swap DI del stub de consentimiento + smoke tests offline | Requiere: `XGOV-B-0001` resuelto (para saber si hay algo que provisionar) |
| B — Tests unitarios/integración mock | Ya prácticamente completos (408 tests) | Cobertura suficiente ya existe; ampliar sólo si el contrato real difiere de lo asumido en sección 5 |
| C — Instalación segura de credenciales UAT | Provisionar `MIDECISOR_*` reales en Azure App Settings QA | Requiere: `XGOV-B-0001` + `XGOV-B-0002` + `XGOV-B-0003` resueltos — **ninguno de los tres puede saltarse** |
| D — Smoke test controlado UAT | Una consulta real controlada, con cédula de prueba autorizada por el proveedor | Requiere: Fase C completa + autorización humana explícita y separada (nunca automática) |
| E — Evidencia UAT | Documentar resultado sanitizado de D | Requiere: D completada |
| F — Configuración producción | Provisionar credenciales de producción (si distintas de UAT) | Requiere: E revisada + aprobación de producto/legal para activar `AutorizacionConsultaRiesgoDurable` en producción |
| G — Smoke test/control de producción | Verificación final antes de tráfico real | Requiere: F completa + autorización explícita separada |

Ninguna fase de red se ejecutó en este ticket. La Fase C es hoy
estructuralmente inalcanzable por los tres blockers de gobernanza.

## 11. Criterios de producción

| Control | Clasificación | Estado |
|---|---|---|
| OAuth2 (Client_id/secret + username/password) | `PROVIDER_REQUIREMENT` | Implementado |
| Timeout de transporte | `XPAY_PRODUCTION_CONTROL` | Implementado (30s default, configurable) |
| Reintentos | `XPAY_PRODUCTION_CONTROL` | Deliberadamente NINGUNO (decisión propia, no exigida por el proveedor según lo documentado) |
| Manejo de errores (401/403 vs. transporte vs. protocolo) | `XPAY_PRODUCTION_CONTROL` | Implementado, clasificado por excepciones tipadas |
| Logging sin PII/secretos | `XPAY_PRODUCTION_CONTROL` | Implementado (nunca documento, apellido, token, headers, bodies) |
| Gestión de secretos (Azure App Settings, nunca repo) | `XPAY_PRODUCTION_CONTROL` | Mecanismo ya establecido, sin valores provisionados |
| Separación de configuración UAT/PROD | `XPAY_PRODUCTION_CONTROL` | Mecanismo (env vars por ambiente Azure) ya existe; sin valores todavía |
| Comportamiento fail-closed sin config | `XPAY_PRODUCTION_CONTROL` | Implementado (`MiDecisorConfigurationException` antes de cualquier HTTP) |
| Tests | `XPAY_PRODUCTION_CONTROL` | 408/408 offline; faltan pruebas contra contrato real (bloqueado por sección 2) |
| Observabilidad | `XPAY_PRODUCTION_CONTROL` | Logging estructurado ya presente; sin dashboards/alertas dedicados (no verificado en este ticket) |

`MIDECISOR_PRODUCTION_READY=NO` — no por deficiencia técnica de XPAY,
sino porque tres precondiciones de gobernanza externas
(`XGOV-B-0001/0002/0003`) permanecen abiertas y son prerrequisito para
la Fase C, que a su vez es prerrequisito de todo lo demás.

## 12. Blockers (orden de prioridad)

1. `XGOV-B-0001` — titularidad de credenciales (`DECISION_PENDIENTE`,
   `PRIMARY_NEXT_BLOCKER` ya identificado previamente).
2. `XGOV-B-0002` — ambiente/host `MIDECISOR_BASE_URL` (`BLOQUEADO_EXTERNO`).
3. `XGOV-B-0003` — ruta de consulta `/client` vs `/pn` (`BLOQUEADO_EXTERNO`).
4. Acceso al Manual de implementación API MiDecisor v1.0 — no disponible
   en este entorno; necesario para verificar (no sólo asumir) el
   contrato de la sección 5.
5. Decisión de producto (no técnica) sobre `MAPPING_AUTHORITY` (declarado
   vs. verificado por KYC) antes de UAT real.
6. Decisión separada y explícita para el swap DI del stub de
   consentimiento (`AutorizacionConsultaRiesgoDurable`) — trivial
   técnicamente, pero condicionada al blocker #1.

## 13. Siguiente acción exacta

Ninguna de las siguientes requiere código ni red: (a) resolver
`XGOV-B-0001` (decisión de titularidad de credenciales, dueño
`SIN_ASIGNAR` hoy) es el desbloqueo más pequeño identificado; o
(b) localizar y poner a disposición el Manual de implementación API
MiDecisor v1.0 para verificar la sección 5 contra la fuente oficial.
Ninguna otra acción de este frente debe iniciarse antes de esas dos.

---

**Este documento es preflight offline.** No autoriza UAT, no autoriza
producción, no autoriza el swap de DI del stub de consentimiento, no
autoriza provisionar credenciales.
