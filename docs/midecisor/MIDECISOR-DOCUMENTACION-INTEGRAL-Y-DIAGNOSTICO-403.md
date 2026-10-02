# MiDecisor / DataCrédito — Auditoría documental integral y diagnóstico del HTTP 403

**Ticket:** XPAY-590 · **Fecha:** 2026-09-29 (UTC) · **Modo:** 100 % offline, cero red, cero OAuth
**Propósito:** determinar si alguna fuente oficial local explica o resuelve el HTTP 403 de
XPAY-588 **sin escalar** al proveedor.

**Conclusión anticipada:** no la hay. La petición de XPAY-588 cumple íntegramente el contrato
oficial y ninguna fuente documenta una causa, un requisito incumplido ni una remediación para
ese 403. El escalamiento al proveedor es inevitable.

---

## 1. Inventario de documentos

```
DOCUMENTS_DISCOVERED=24 (relevantes)
UNIQUE_OFFICIAL_DOCUMENTS=8 (emitidos por DataCrédito/Experian, deduplicados)
DOCUMENTS_FULLY_REVIEWED=8
SENSITIVE_DOCUMENT_FOUND=YES
```

### 1.1 Fuentes oficiales del proveedor (leídas íntegramente)

| # | Documento | Versión / fecha | Emisor | Tipo | Relevancia | Sensible | Leído |
|---|---|---|---|---|---|---|---|
| 1 | Manual de Conectividad API Experian Clientes | v1.0 · mod. 05-nov-2025 · 27 pp | DataCrédito Experian (Producto FS) | normativo | **MÁXIMA** — autoridad delegada para autenticación | NO | SÍ |
| 2 | Manual de implementación API MiDecisor | v1.0 · mod. 11-sep-2025 · 19 pp | DataCrédito Experian (Producto FS-BI) | manual | ALTA — API de producto | NO | SÍ |
| 3 | `swagger-api-externa.yaml` (Token) | 26-sep-2025 · 2 609 líneas | DataCrédito Experian | OpenAPI contractual | **MÁXIMA** | NO | SÍ |
| 4 | `Swagger MiDecisor.yaml` | 04-nov-2025 · 2 304 líneas | DataCrédito Experian | OpenAPI contractual | ALTA | NO | SÍ |
| 5 | `Ejemplo Salida MiDecisor PN.json` | 05-nov-2025 | DataCrédito Experian | ejemplo | BAJA — solo respuesta de producto | NO | SÍ |
| 6 | `Ejemplo Salida MiDecisor PJ.json` | 05-nov-2025 | DataCrédito Experian | ejemplo | BAJA — solo respuesta de producto | NO | SÍ |
| 7 | Guía para solución de errores de conexión a `uat-api…` | renovación 21-sep-2026 | Connectivity & Support Team | aviso operativo | MEDIA — solo TLS | NO | SÍ |
| 8 | `Mi DECISOR_PJ_PN_EJEMPLO.postman_collection.json` | 28-jul-2026 · Postman v2.1.0 | colección de ejemplo del paquete | ejemplo ejecutable | **ALTA — hallazgo nuevo** | **SÍ** | SÍ (estructura; valores no leídos) |

**Duplicados descartados como fuente independiente:** el Manual de implementación existe en dos
rutas con **md5 idéntico** (`bfe0aa7d…`), y `WS_REST_MI_DECISOR.zip` contiene exactamente los
mismos 6 archivos del árbol extraído. Cuentan como una sola fuente cada uno.

### 1.2 Documentación XPAY y de gobernanza (revisada)

Registro de blockers XGOV, preflight de producción XPAY-583, evidencia TLS XPAY-584, evidencia de
autenticación XPAY-588, runbook de purga B4, actas y política de decisión crediticia, documento de
continuidad M2.4b. Ninguno aporta información sobre habilitación de credenciales, IP registrada
ni el 403: el documento de continuidad M2.4b tiene **0** menciones de `client_id`, `credencial`,
`habilitación`, `whitelist`, `403` o `Okta`.

### 1.3 Documentos sensibles detectados y NO abiertos

`SENSITIVE_DOCUMENT_FOUND=YES`. La colección Postman contiene los cuatro valores de credencial
como literales (no variables de Postman); se inspeccionó únicamente su estructura. En
`~/Documents/datafin/` existen además informes crediticios históricos y una base de datos
DataCrédito cuyos nombres de archivo sugieren números de documento: **no fueron abiertos**. Ningún
documento sensible se copió al repositorio.

---

## 2. Hallazgo nuevo: la colección Postman oficial

Es el artefacto más informativo de esta auditoría porque es el único **ejecutable** del paquete.
Sus tres peticiones apuntan al **mismo host**:

| Petición | Método | Host | Ruta |
|---|---|---|---|
| `Generar_TOKEN` | POST | `uat-api.datacredito.com.co` | `/spla/oauth2/v1/token` |
| `Revocar_TOKEN` | POST | `uat-api.datacredito.com.co` | `/spla/oauth2/v1/revokeToken` |
| `Servicio` | POST | `uat-api.datacredito.com.co` | `/co/cs/midecisor/v1/client` |

Datos no sensibles verificados: `Content-Type: application/json` en token y en producto;
esquema de autorización del producto = `Bearer`; el username del token pertenece al **dominio de
ambiente `demo`** (verificado como booleano, sin leer ni registrar el username); cabeceras del
token = exactamente `client_id`, `client_secret`, `Content-Type`, **sin ninguna adicional**.

Consecuencias:

1. **Cierra el residual de XPAY-587.** El host del API de producto es el mismo `uat-api`, no
   `demo-api`. Ya no es una inferencia desde la estructura del Swagger.
2. **Tercera confirmación independiente de DEMO → UAT**, ahora en un artefacto ejecutable.
3. **Descarta cabeceras faltantes** como causa del 403.

---

## 3. Respuestas documentales (A–T)

| # | Pregunta | Respuesta documental |
|---|---|---|
| A | Procedimiento completo de habilitación en DEMO/UAT | Documentado solo a alto nivel: contrato en firme → DataCrédito configura el **usuario** del cliente (que personaliza la clave) → DataCrédito **entrega** `client_id`/`client_secret`. Sin más pasos |
| B | Pasos posteriores a recibir las credenciales | **NOT_DOCUMENTED** |
| C | Activación / suscripción / asociación de producto del `client_id` | **NOT_DOCUMENTED** como procedimiento. Indicio estructural: `client_id` se define como «**ID del producto**» y `client_secret` como «**Secreto del producto**», y el usuario se configura «con privilegios de consulta de **un producto** vía API» — el alcance es por producto por construcción, pero no hay procedimiento de activación |
| D | Registro de IP / whitelist / restricción de origen | **NOT_DOCUMENTED** — 0 ocurrencias en las 7 fuentes de texto. El único `firewall`/`proxy` es la guía TLS refiriéndose al proxy **del cliente** |
| E | Configuración de scope/permisos | **NOT_DOCUMENTED** como configurable. `scope` aparece 2 veces, solo como sugerencia diagnóstica del manual de implementación para el endpoint de **producto** |
| F | Diferencia creadas / activadas / habilitadas para MiDecisor | **NOT_DOCUMENTED** |
| G | Significado oficial de 403 | **Endpoint de token:** el manual de conectividad lo lista con la **columna de escenario vacía**, y **ningún Swagger declara 403** (solo 200/400/401). **Endpoint MiDecisor:** «El token no tiene permisos para acceder al recurso solicitado» → verificar scope del token y credenciales del cliente. Esa fila presupone un token emitido, por lo que no aplica al endpoint de token |
| H | Código o mensaje más específico para Forbidden | **NO.** El mensaje observado es literalmente el genérico de la tabla, y el manual declara que «la estructura del mensaje de error es la misma para todos los códigos» |
| I | Políticas Apigee que generen 403 | Apigee está documentado como gateway (3 menciones), pero el **único** rechazo por política documentado se mapea a **401** (ataque con expresión regular, amenaza JSON, política Spike Arrest). Ninguna política se documenta produciendo 403 |
| J | Cabeceras adicionales no usadas en XPAY-588 | **NO.** El Swagger declara para el token exactamente **dos** parámetros de cabecera (`Client_id`, `Client_secret`); Tabla 6 coincide; `Content-Type: application/json` se exige vía la fila 415 y los ejemplos |
| K | User-Agent / Accept / Origin / API key / scope / grant_type / audience / Authorization / mTLS | **Todos NOT_DOCUMENTED para generar token** (0 ocurrencias de User-Agent, Accept, Origin, api key, audience, mTLS, certificado cliente). `grant_type` existe **solo** en el servicio de **refrescar** (valor fijo `refresh_token`), junto con la cabecera `refresh_token`; generar token no lo usa. El token endpoint declara `security: []` — no requiere autorización previa |
| L | Algo distinto para DEMO/UAT en el endpoint de token | **NO.** Misma ruta y mismo contrato; solo cambia el host |
| M | ¿`@demo` emparejado explícitamente con `uat-api`? | **SÍ**, por tres vías independientes: los dos ejemplos de integración del manual de conectividad (generar y refrescar) y la colección Postman |
| N | ¿`client_id`/`client_secret` específicos por ambiente? | **SÍ** — «Cada ambiente requiere credenciales específicas. Los tokens generados en un ambiente no son válidos en otro» |
| O | Expiración / rotación / activación de `client_id`/`client_secret` | Parcial: Okta gestiona «políticas de rotación, resguardo de secretos y registro de accesos». Sin regla concreta de vigencia ni procedimiento → **NOT_DOCUMENTED** en términos operativos |
| P | Bloqueo por intentos fallidos | **NOT_DOCUMENTED** (0 ocurrencias) |
| Q | Bloqueo / estado del usuario Okta | Documentado, **pero mapeado a 401**: «Your account is in invalid state. For further assistance, please contact apisupport@experian.com» |
| R | Primer ingreso / cambio de contraseña previo al API | **NOT_DOCUMENTED.** Se menciona que el cliente «personaliza la clave», sin procedimiento, portal ni requisito de completarlo antes del API |
| S | Portal / Okta a activar previamente | **NOT_DOCUMENTED** (0 ocurrencias de `portal`) |
| T | ¿Algún documento diagnostica este 403 sin una segunda llamada? | **NO** |

---

## 4. Correlación con XPAY-588

```
XPAY_588_REQUEST_FULLY_CONFORMS=YES
```

| Requisito documental | Fuente | Cumplimiento XPAY-588 |
|---|---|---|
| Método POST | Manual conectividad §Generación; Swagger | **YES** |
| Host UAT `uat-api.datacredito.com.co` | Tablas 1/5; ejemplos; Postman | **YES** |
| Ruta `/spla/oauth2/v1/token` | Tablas 1/5; Swagger | **YES** |
| `client_id` en cabecera | Tabla 6; Swagger (`Client_id`, `in: header`) | **YES** |
| `client_secret` en cabecera | Tabla 6; Swagger (`Client_secret`, `in: header`) | **YES** |
| `Content-Type: application/json` | Fila 415; ejemplos; Postman | **YES** |
| `username` en body JSON | Tabla 7 | **YES** |
| `password` en body JSON | Tabla 7 | **YES** |
| Body `data-raw` JSON | Tabla 7 | **YES** |
| Sin cabeceras adicionales | Swagger: exactamente 2 parámetros | **YES** |
| Sin `grant_type` al generar | Tabla 6 vs Tabla 12 | **YES** |
| TLS obligatorio | §Seguridad (OIDC requiere TLS) | **YES** (`ssl_verify_result=0`) |
| Credenciales del ambiente correcto | Manual implementación §Ambientes | **YES** — username de dominio `demo` contra host UAT, emparejamiento confirmado |

```
POTENTIAL_ROOT_CAUSE_FOUND=NO
```

No se identificó **ningún** requisito documentado que XPAY-588 haya incumplido.

---

## 5. DOCUMENTATION_CONTRADICTIONS

| # | Tema | SOURCE_A | SOURCE_B | Autoridad de resolución | Resuelta | Resolución |
|---|---|---|---|---|---|---|
| 1 | Host del token | Manual conectividad: `uat-api` / `api` | Manual implementación: `demo-api` / `prod-api`; sus curl: `test-api` | Manual de conectividad (delegación explícita del propio manual de implementación) + Postman | **SÍ** | `uat-api.datacredito.com.co` |
| 2 | Host del API de producto | Manual implementación: `demo-api` | Swagger: mismo server que el token; Postman: `uat-api` | Colección Postman (artefacto ejecutable) + estructura del Swagger | **SÍ** | `uat-api.datacredito.com.co`. Cierra el residual de XPAY-587 |
| 3 | Ubicación de las credenciales | Quickstart implementación: los 4 en el body | Manual conectividad Tablas 6/7 + Swagger + Postman: `client_id`/`client_secret` en cabeceras, `username`/`password` en body | Manual de conectividad + Swagger | **SÍ** | Cabeceras + body. XPAY lo implementa así |
| 4 | Tipo de `expires_in` | Quickstart implementación: número `3600` | Manual conectividad: cadena `"1800"` | Manual de conectividad | **SÍ** | Cadena. El código de XPAY es correcto; no «corregirlo» a entero |
| 5 | Host de producción | Manual implementación: `prod-api` | Manual conectividad: `api.datacredito.com.co` (sin prefijo) | Manual de conectividad | **SÍ** | `api.datacredito.com.co` |
| 6 | Ruta de revocación | Manual conectividad: `/revoke-token` | Swagger y Postman: `/revokeToken` | — | **NO** | Sin impacto: XPAY no revoca |
| 7 | Enum de ambientes | Swagger: `[dev, qa]`, default `dev` | Manuales: `uat`, `demo`, `prod` | — | **NO** | El enum del Swagger está incompleto; no usarlo |
| 8 | Existencia del 403 | Manual conectividad: lo lista (sin escenario) | Ambos Swagger: declaran solo 200/400/401 | — | **NO** | Indica que el 403 se origina en la **capa de gateway**, fuera del contrato de la API |
| 9 | Mayúsculas de cabeceras | Ejemplos: `client_id` | Swagger: `Client_id` | RFC 9110 | **SÍ** | Insensible a caso; no es un problema |
| 10 | Nº de cabeceras al generar | Tabla 6: «al menos dos» | Swagger: exactamente dos | Swagger | **SÍ** | Ninguna tercera cabecera está documentada |

Ninguna contradicción se ha omitido.

---

## 6. Diagnóstico del 403

```
403_ROOT_CAUSE_STATUS=NOT_DOCUMENTED
ROOT_CAUSE_CONFIRMED=NO
ROOT_CAUSE=UNKNOWN
DOCUMENTED_REMEDIATION_AVAILABLE=NO
PROVIDER_ESCALATION_STILL_REQUIRED=YES
```

Tres hechos documentales convergen:

1. El manual autoritativo **lista el 403 sin escenario**, único código de su tabla en esa
   situación.
2. **Ningún Swagger declara un 403** en ninguno de los dos endpoints.
3. El cuerpo recibido **sí respetó** el envelope de error documentado, de modo que la petición
   alcanzó el servicio Apigee correcto y fue rechazada por política.

De ello se sigue que el 403 es una denegación de la **capa de infraestructura/política**, ajena al
contrato de la API. Ninguna corrección de la forma de la petición del lado de XPAY está
documentada como remedio, y la petición ya conforma al contrato en los trece requisitos
verificables.

Adicionalmente, la propia tabla del proveedor **excluye** las causas ordinarias, porque les asigna
códigos distintos: atributos ausentes → 400; método distinto de POST → 405; `Content-Type`
incorrecto → 415; usuario o contraseña inválidos → 400; `client_id`/`client_secret` inválidos →
401; cuenta en estado inválido → 401; políticas Spike Arrest / amenaza JSON → 401.

El dato discriminante no existe del lado de XPAY: reside en los registros del gateway de
DataCrédito Experian.

---

## 7. Controles respetados

Cero llamadas de red, cero OAuth nuevas, cero llamadas MiDecisor, cero consultas de crédito. El
total de solicitudes OAuth del proyecto permanece en **1** (la de XPAY-588). No se imprimieron
valores de `client_id`, `client_secret`, `username`, `password`, tokens, JWT, NIT, cédulas, IP
públicas ni PII. No se abrieron los informes crediticios históricos. No se copió ningún documento
sensible al repositorio. Sin cambios de código, entorno, configuración, Azure, base de datos,
deploy, stage, commit ni push. Passport no fue tocado.

Se hace constar que la guía TLS oficial ofrece una «Solución 2 (Temporal): deshabilitar validación
de certificados». **No se usó y no debe usarse**: la validación TLS de XPAY-584 fue exitosa sin
ella, y las políticas de estos tickets la prohíben.

Este documento no autoriza reintentos, nuevas llamadas, cambios de configuración ni inicio de UAT
funcional.
