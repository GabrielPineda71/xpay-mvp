# XPAY MVP — Backend API

Sistema de pagos digitales con wallets, QR, liquidaciones, retiros y reportes transaccionales.

---

## Cómo correr el backend

### Requisitos

- .NET 8 SDK
- SQL Server 2022 (local, Docker o Azure SQL)

### Paso a paso

```bash
# 1. Crear base de datos y ejecutar migraciones en orden
sqlcmd -S localhost -U sa -P "<contraseña>" -Q "CREATE DATABASE XPAY_MVP"
sqlcmd -S localhost -U sa -P "<contraseña>" -d XPAY_MVP -i database/001_security_identity.sql
sqlcmd -S localhost -U sa -P "<contraseña>" -d XPAY_MVP -i database/002_wallet_ledger.sql
sqlcmd -S localhost -U sa -P "<contraseña>" -d XPAY_MVP -i database/003_comercios_qr.sql
sqlcmd -S localhost -U sa -P "<contraseña>" -d XPAY_MVP -i database/004_liquidacion_qr.sql
sqlcmd -S localhost -U sa -P "<contraseña>" -d XPAY_MVP -i database/005_retiros_comercio.sql
sqlcmd -S localhost -U sa -P "<contraseña>" -d XPAY_MVP -i database/006_gestion_retiros_comercio.sql
sqlcmd -S localhost -U sa -P "<contraseña>" -d XPAY_MVP -i database/007_security_roles_jwt.sql

# 2. Ajustar cadena de conexión en backend/Xpay.Api/appsettings.json

# 3. Correr el backend
cd backend/Xpay.Api
dotnet restore
dotnet run
```

El backend queda disponible en `http://localhost:5000`.
Swagger UI disponible en `http://localhost:5000/swagger`.

---

## Endpoints públicos (sin autenticación)

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| `POST` | `/api/usuarios/registro-final` | Registro de usuario final con wallet |
| `POST` | `/api/auth/login` | Login — devuelve JWT |
| `GET`  | `/health` | Health check del servicio |
| `GET`  | `/api/version` | Versión y ambiente de la API |

---

## Cómo autenticarse

```bash
# 1. Registrar usuario
curl -X POST http://localhost:5000/api/usuarios/registro-final \
  -H "Content-Type: application/json" \
  -d '{"tipoDocumento":"CC","numeroDocumento":"1099001234","primerNombre":"Carlos",
       "primerApellido":"Gomez","celular":"3001112233","email":"carlos@demo.com",
       "usuario":"carlos_demo","password":"Demo@2024!","idUnidadNegocio":1}'

# 2. Login — copiar el campo data.token
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"usuario":"carlos_demo","password":"Demo@2024!"}'

# 3. Usar el token en llamadas protegidas
curl http://localhost:5000/api/wallets/persona/1 \
  -H "Authorization: Bearer <token>"
```

En Swagger UI (`/swagger`), hacer clic en **Authorize** e ingresar el token JWT para probar todos los endpoints protegidos directamente desde el navegador.

---

## Endpoints protegidos principales (requieren `Authorization: Bearer {token}`)

### Wallets

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| `GET`  | `/api/wallets/persona/{idPersona}` | Wallet activa de un usuario |
| `GET`  | `/api/wallets/{idWallet}/saldo` | Saldo disponible |
| `GET`  | `/api/wallets/{idWallet}/movimientos` | Historial de movimientos |
| `POST` | `/api/wallets/{idWallet}/recarga-manual` | Recarga manual de saldo |
| `POST` | `/api/wallets/transferencia` | Transferencia entre wallets |

### Pagos QR

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| `POST` | `/api/qr/pagar` | Pago a comercio por código QR |

### Comercios

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| `POST` | `/api/comercios/liquidar-venta-qr` | Liquidar venta QR al comercio |
| `POST` | `/api/comercios/solicitar-retiro` | Solicitar retiro de saldo del comercio |
| `POST` | `/api/comercios/retiros/confirmar-pago` | Confirmar pago de un retiro |
| `POST` | `/api/comercios/retiros/rechazar` | Rechazar un retiro pendiente |

### Reportes

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| `GET`  | `/api/reportes/wallet/{idWallet}/estado-cuenta` | Estado de cuenta de una wallet |
| `GET`  | `/api/reportes/comercios/{idComercio}/resumen` | Resumen financiero de un comercio |
| `GET`  | `/api/reportes/ledger/transaccion/{idTransaccion}` | Detalle de una transacción en el ledger |
| `GET`  | `/api/reportes/operaciones/resumen-general` | Resumen global del sistema |

---

## Configuración QA / Azure App Service

El backend acepta configuración por variables de entorno usando la notación `__` (doble guión bajo) de .NET como separador de jerarquía.

### Variables de entorno esperadas

| Variable de entorno | Equivalente en appsettings | Descripción |
|---------------------|---------------------------|-------------|
| `ConnectionStrings__XpayConnection` | `ConnectionStrings.XpayConnection` | Cadena de conexión SQL Server |
| `Jwt__Key` | `Jwt.Key` | Llave secreta HS256 (mínimo 32 caracteres) |
| `Jwt__Issuer` | `Jwt.Issuer` | Emisor del token (ej. `Xpay.Api`) |
| `Jwt__Audience` | `Jwt.Audience` | Audiencia del token (ej. `Xpay.App`) |
| `Jwt__ExpirationHours` | `Jwt.ExpirationHours` | Horas de validez del token (ej. `8`) |
| `Api__Name` | `Api.Name` | Nombre de la API en `/api/version` |
| `Api__Version` | `Api.Version` | Versión de la API en `/api/version` |
| `Cors__AllowedOrigins__0` | `Cors.AllowedOrigins[0]` | Primer origen permitido para CORS |
| `Cors__AllowedOrigins__1` | `Cors.AllowedOrigins[1]` | Segundo origen permitido para CORS |

### Ejemplo de configuración QA en Azure App Service

```
ConnectionStrings__XpayConnection = Server=xpay-sql-qa.database.windows.net;Database=XPAY_MVP_QA;User Id=xpay_app;Password=<secret>;TrustServerCertificate=True;
Jwt__Key          = <llave-secreta-qa-minimo-32-chars>
Jwt__Issuer       = Xpay.Api
Jwt__Audience     = Xpay.App
Jwt__ExpirationHours = 8
Api__Name         = XPAY API
Api__Version      = 0.1.0-mvp
Cors__AllowedOrigins__0 = http://localhost:3000
Cors__AllowedOrigins__1 = http://localhost:5173
Cors__AllowedOrigins__2 = https://xpay-frontend-qa.azurewebsites.net
```

### URLs frontend soportadas en CORS

| Ambiente | URL |
|----------|-----|
| Local React / Vite | `http://localhost:5173` |
| Local React CRA | `http://localhost:3000` |
| Local HTTPS | `https://localhost:3000`, `https://localhost:5173` |
| QA Azure (futuro) | `https://xpay-frontend-qa.azurewebsites.net` |

---

## Estado actual del MVP

| Fase | Contenido | Estado |
|------|-----------|--------|
| 1 | Registro de usuario final, login, wallet, recarga manual | Completa |
| 2 | Transferencias XPAY a XPAY entre wallets | Completa |
| 3 | Pago a comercio por código QR | Completa |
| 4 | Liquidación de ventas QR al wallet del comercio | Completa |
| 5 | Solicitud de retiro de saldo del comercio | Completa |
| 6 | Gestión de retiros: confirmar pago y rechazar | Completa |
| 7 | Consultas y reportes transaccionales (4 endpoints) | Completa |
| 8 | Seguridad JWT: `[Authorize]`, 401 sin token, roles | Completa |
| 9 | Health check, versión API, Swagger con JWT Bearer | Completa |
| 10 | CORS para frontend, configuración por ambiente, preparación QA | Completa |

---

## Despliegue QA

Para preparar y verificar un ambiente QA en Azure App Service, ver la guía completa:

**[docs/QA_DEPLOYMENT.md](docs/QA_DEPLOYMENT.md)**

Incluye arquitectura propuesta, variables de entorno, orden de despliegue, checklist de verificación y troubleshooting.

---

## Release QA Candidate

**[docs/RELEASE_QA_CANDIDATE.md](docs/RELEASE_QA_CANDIDATE.md)**

Declara formalmente que XPAY MVP QA Candidate v0.1 está listo para despliegue QA y pruebas internas controladas. Incluye alcance, fases completadas, funcionalidades incluidas y no incluidas, riesgos, prerrequisitos y criterios de aprobación.

> ⚠️ Esta versión **no es producción** y **no debe usarse con dinero real**.

---

## Runbook despliegue QA

**[docs/QA_DEPLOYMENT_RUNBOOK.md](docs/QA_DEPLOYMENT_RUNBOOK.md)**

Guía operativa paso a paso para ejecutar el despliegue real de XPAY QA Candidate v0.1 en Azure: recursos a crear, variables de entorno, orden de scripts SQL, despliegue backend y frontend, smoke test y rollback básico.

---

## Scripts locales QA

Scripts auxiliares para generar artefactos de publicación QA sin desplegar. Los artefactos quedan en `artifacts/` (ignorado por git).

| Script | Acción |
|--------|--------|
| `scripts/build-backend-qa.sh` | Restaura, compila y publica el backend .NET 8 en `artifacts/backend-qa/` |
| `scripts/build-frontend-qa.sh` | Instala dependencias npm y construye el frontend Vite en `artifacts/frontend-qa/`. Requiere `frontend/xpay-admin/.env` con `VITE_API_BASE_URL` configurado. |
| `scripts/build-qa-artifacts.sh` | Ejecuta ambos scripts en orden y muestra resumen final |

```bash
# Generar artefacto backend
bash scripts/build-backend-qa.sh

# Generar artefacto frontend (requiere .env previo)
cp frontend/xpay-admin/.env.qa.example frontend/xpay-admin/.env
bash scripts/build-frontend-qa.sh

# O generar ambos de una vez
bash scripts/build-qa-artifacts.sh
```

> Los scripts **no despliegan** ni contienen secretos. Para el despliegue real, ver `docs/QA_DEPLOYMENT_RUNBOOK.md`.

---

## Dataset QA

**[database/008_seed_qa_dataset.sql](database/008_seed_qa_dataset.sql)**

Script de datos de prueba controlados para el ambiente QA. Ejecutar después de los scripts de migración 001–007.

- Crea personas, usuarios, wallets, comercio demo QA y QR demo QA (`QR-DEMO-XPAY-QA-001`).
- Idempotente: puede ejecutarse más de una vez sin crear duplicados.
- **Uso exclusivo QA / desarrollo. No ejecutar en producción. No contiene datos reales.**
- Los saldos y transacciones financieras se generan vía endpoints del backend (ver `scripts/validate-backend.sh`).

---

## Operaciones financieras QA vía API

**[docs/QA_FINANCIAL_OPERATIONS_API.md](docs/QA_FINANCIAL_OPERATIONS_API.md)**

Guía operativa para generar datos financieros QA usando los endpoints reales del backend después de ejecutar el seed. Documenta el flujo completo: recarga, transferencia, pago QR, liquidación, retiro (confirmación y rechazo), reportes y validación contable.

- Ejecutar después del seed QA (`database/008_seed_qa_dataset.sql`).
- No inserta saldos, ledger ni transacciones directamente en SQL.
- Incluye ejemplos `curl` con DTOs exactos y validaciones por paso.
- **Uso exclusivo QA / desarrollo. No usar dinero real. No ejecutar en producción.**

---

## Script generación operaciones financieras QA

**[scripts/generate-qa-financial-ops.sh](scripts/generate-qa-financial-ops.sh)**

Script local auxiliar que automatiza el flujo financiero QA completo ejecutando los pasos A–H de la guía `docs/QA_FINANCIAL_OPERATIONS_API.md`.

```bash
export API_BASE="http://localhost:5000"
export TOKEN="<jwt-token>"
export ID_WALLET_USUARIO_1="<id>"
export ID_WALLET_USUARIO_2="<id>"
export ID_USUARIO_QA="<id>"
export ID_COMERCIO_QA="<id>"

bash scripts/generate-qa-financial-ops.sh
```

- Usa los endpoints reales del backend vía `curl`.
- No ejecuta SQL. No hace deploy. No contiene secretos.
- Requiere token JWT válido y backend corriendo.
- **Uso exclusivo QA / desarrollo. No usar dinero real. No ejecutar en producción.**

---

## Checklist maestro QA End-to-End

**[docs/QA_MASTER_E2E_CHECKLIST.md](docs/QA_MASTER_E2E_CHECKLIST.md)**

Guía única que une el ciclo QA completo: desde confirmar CI verde hasta firmar el acta de aprobación. Cubre 8 fases: estado del repo, artefactos, ambiente, base de datos, variables, operaciones financieras, smoke test, casos manuales y decisión.

- Punto de entrada único para ejecutar el ciclo QA de punta a punta.
- **Uso exclusivo QA / desarrollo. No producción. No dinero real.**

---

## Ciclo QA Interno 01

**[docs/QA_INTERNAL_CYCLE_01.md](docs/QA_INTERNAL_CYCLE_01.md)**

Paquete documental para ejecutar el primer ciclo QA interno de XPAY MVP QA Candidate v0.1. Incluye identificación del ciclo, roles y responsabilidades, checklist de entrada, alcance de pruebas (10 módulos, 35 casos), plan de ejecución (13 pasos), registro de resultados, tabla de bugs, criterios de cierre y acta lista para diligenciar.

- Diligenciar durante y después de la ejecución QA.
- **Uso exclusivo QA / desarrollo. No producción. No dinero real.**

---

## Onboarding usuarios internos QA

**[docs/QA_INTERNAL_USERS_ONBOARDING.md](docs/QA_INTERNAL_USERS_ONBOARDING.md)**

Guía operativa para habilitar usuarios internos después de que QA Interno 01 sea aprobado. Define perfiles permitidos, reglas de uso, checklist de habilitación, plantilla de comunicación de acceso, formato de reporte de incidencias, severidades y controles de riesgo.

- Usar solo si el Ciclo QA Interno 01 está aprobado o aprobado con observaciones no bloqueantes.
- **Uso exclusivo QA / desarrollo. No producción. No dinero real. No datos reales.**

---

## Seguimiento de incidencias QA internas

**[docs/QA_INTERNAL_ISSUES_TRACKING.md](docs/QA_INTERNAL_ISSUES_TRACKING.md)**

Guía operativa para registrar, clasificar y dar seguimiento a incidencias reportadas por usuarios internos y el equipo QA. Define severidades (Crítica/Alta/Media/Baja), estados (Nueva→Cerrada), flujo de atención, SLA interno sugerido, criterios de cierre por severidad, cuándo abrir Ciclo QA Interno 02, relación con GitHub Issues y plantilla de reporte resumen.

- Usar en paralelo con el acceso de usuarios internos QA.
- **Uso exclusivo QA / desarrollo. No producción. No datos reales.**

---

## Salida QA interno y piloto controlado

**[docs/QA_EXIT_CRITERIA_AND_PILOT_READINESS.md](docs/QA_EXIT_CRITERIA_AND_PILOT_READINESS.md)**

Documento de decisión para determinar cuándo el MVP puede pasar de QA interno a un piloto controlado limitado. Define 12 criterios obligatorios de salida, 12 criterios bloqueantes, alcance permitido/no permitido del piloto, matriz de decisión (avanzar/restricciones/no avanzar), controles de seguridad y financieros, criterios de suspensión y acta de decisión diligenciable.

- Requiere firma del responsable técnico y del responsable de negocio.
- **No autoriza producción, dinero real ni clientes masivos automáticamente.**

---

## Plan operativo piloto controlado

**[docs/PILOT_CONTROLLED_OPERATING_PLAN.md](docs/PILOT_CONTROLLED_OPERATING_PLAN.md)**

Plan operativo para ejecutar el piloto controlado de XPAY MVP después de cumplir los criterios de salida QA. Define identificación del piloto, participantes permitidos (5 perfiles), alcance funcional (10 funcionalidades), datos permitidos/prohibidos, reglas operativas, soporte y monitoreo, 10 criterios de éxito, 10 criterios de suspensión, registro por sesión y acta de cierre.

- Usar solo después de que `docs/QA_EXIT_CRITERIA_AND_PILOT_READINESS.md` esté aprobado con firma doble.
- **No es producción. No autoriza dinero real. No es lanzamiento comercial.**

---

## Preproducción y brechas para dinero real

**[docs/PREPRODUCTION_GAPS_AND_REAL_MONEY_CHECKLIST.md](docs/PREPRODUCTION_GAPS_AND_REAL_MONEY_CHECKLIST.md)**

Checklist estratégico que define las 53 brechas (técnicas, financieras/contables, seguridad, legales/regulatorias, operativas) que deben resolverse antes de operar con dinero real, datos reales o apertura pública. Incluye criterios mínimos para autorizar dinero real (12 ítems), criterios para comercios/clientes reales, matriz de decisión de preproducción (4 opciones), señales de bloqueo absoluto (10) y acta de evaluación con firma quíntuple.

- **El MVP actual sirve para QA y piloto controlado. No está autorizado para operación financiera real.**
- Usar después de cerrar el piloto controlado (`docs/PILOT_CONTROLLED_OPERATING_PLAN.md`).

---

## Observabilidad básica

El backend incluye observabilidad mínima para QA y desarrollo:

**Correlation ID (`X-Correlation-ID`)**

Cada request recibe un correlation ID. Si el cliente envía el header `X-Correlation-ID`, el backend lo propaga en el response. Si no lo envía, se genera un GUID automáticamente.

```bash
# Enviar correlation ID propio
curl -H "X-Correlation-ID: QA-DEBUG-001" http://localhost:5000/api/diagnostics/ping

# El response siempre incluye el header de vuelta
# X-Correlation-ID: QA-DEBUG-001
```

**Request logging**

Cada request registra en los logs: método HTTP, path, status code y tiempo de respuesta en ms, vinculados al correlation ID. Se configura en `appsettings.json`:

```json
"Observability": {
  "EnableRequestLogging": true,
  "EnableCorrelationId": true
}
```

**No se registra por seguridad:** header `Authorization`, body de requests, passwords, tokens, connection strings ni datos personales.

**`GET /api/diagnostics/ping`**

Endpoint público de diagnóstico sin secretos:

```bash
curl http://localhost:5000/api/diagnostics/ping
# { "status": "OK", "service": "XPAY API", "environment": "Development",
#   "timestamp": "...", "correlationId": "..." }
```

No expone: connection string, JWT key, variables de entorno completas ni detalles de infraestructura.

---

## Swagger / API Docs por ambiente

Swagger se habilita o deshabilita con la clave `ApiDocs:EnableSwagger`. Si la clave no existe, el backend usa `true` solo en `Development` y `false` en cualquier otro ambiente.

| Ambiente | Configuración | Resultado |
|----------|--------------|-----------|
| Local / Development | `ApiDocs:EnableSwagger: true` (appsettings.json) | Swagger activo |
| QA | `ApiDocs:EnableSwagger: true` (appsettings.QA.example.json) | Swagger activo |
| Preproducción / Producción | `ApiDocs__EnableSwagger=false` (variable de entorno) | Swagger deshabilitado |

**Deshabilitar en producción:**

```bash
# Azure App Service → Configuration → Application settings
ApiDocs__EnableSwagger = false
```

Con Swagger deshabilitado, `/swagger` retorna 404. Los endpoints `/health`, `/api/version` y `/api/diagnostics/ping` **no dependen de Swagger** y siguen disponibles en todos los ambientes.

---

## Headers básicos de seguridad

El backend aplica headers de seguridad HTTP a todas las respuestas vía `SecurityHeadersMiddleware`. Se controlan con la sección `SecurityHeaders` en la configuración.

**Headers aplicados:**

| Header | Valor | Propósito |
|--------|-------|-----------|
| `X-Content-Type-Options` | `nosniff` | Evita que el browser interprete respuestas con tipo MIME incorrecto |
| `X-Frame-Options` | `DENY` | Bloquea que la API sea embebida en un iframe |
| `Referrer-Policy` | `no-referrer` | No envía información de referer en requests salientes |
| `X-Permitted-Cross-Domain-Policies` | `none` | Bloquea acceso de clientes Flash/Acrobat a datos cross-domain |
| `Permissions-Policy` | `camera=(), microphone=(), geolocation=()` | Deshabilita acceso a hardware del dispositivo |
| `Cache-Control` | `no-store, no-cache` *(si `EnableNoStoreCache: true`)* | Evita que respuestas de la API sean cacheadas |
| `Pragma` | `no-cache` *(si `EnableNoStoreCache: true`)* | Compatibilidad cache para clientes HTTP/1.0 |

**Configuración:**

```json
"SecurityHeaders": {
  "EnableSecurityHeaders": true,
  "EnableNoStoreCache": true
}
```

Para deshabilitar en un ambiente: `SecurityHeaders__EnableSecurityHeaders=false` como variable de entorno.

**Qué no se modificó:**

- JWT: sin cambios en validación ni emisión de tokens.
- CORS: sin cambios en orígenes, métodos ni headers permitidos.
- Lógica financiera: sin cambios en ningún endpoint financiero.
- Swagger configurable: sigue funcionando por `ApiDocs:EnableSwagger`.

**Pendiente (fases posteriores):**

- **Content-Security-Policy (CSP)**: requiere análisis detallado para no romper Swagger UI ni el frontend.
- **HSTS**: requiere HTTPS productivo con certificado válido; no se activa en local ni QA básico.
- **Revisión OWASP completa**: auditoría formal por Security Lead o auditor externo.

---

## Manejo global de errores seguro

`ErrorHandlingMiddleware` captura cualquier excepción no controlada y devuelve una respuesta JSON consistente con HTTP 500. Se ubica en el pipeline después de `CorrelationIdMiddleware` para tener el `correlationId` disponible.

**Formato de respuesta 500:**

```json
{
  "success": false,
  "error": "internal_server_error",
  "message": "An unexpected error occurred.",
  "correlationId": "<guid>"
}
```

**Qué NO se expone:**

- Stack trace
- `exception.Message`
- Inner exceptions
- Connection strings
- Detalles de infraestructura
- Datos de usuario, tokens, headers de request

**Correlation ID:** incluido en el body de la respuesta 500 y también en el header `X-Correlation-ID` (puesto por `CorrelationIdMiddleware` antes del error handler). Permite al equipo técnico buscar el error completo en los logs del backend.

**Errores controlados no se modifican:** errores 400/401/404 retornados explícitamente por los controladores continúan funcionando igual. El middleware solo actúa sobre excepciones que escapan del controlador.

**Endpoint de diagnóstico:**

```
GET /api/diagnostics/error-test
```

Disponible cuando `Diagnostics:EnableErrorTestEndpoint = true`. Lanza una excepción intencional para verificar que el middleware responde correctamente con JSON 500 genérico.

**Configuración:**

```json
"ErrorHandling": {
  "EnableGlobalErrorHandler": true
},
"Diagnostics": {
  "EnableErrorTestEndpoint": true
}
```

Variables de entorno:

```bash
ErrorHandling__EnableGlobalErrorHandler = true
Diagnostics__EnableErrorTestEndpoint    = false   # obligatorio en producción
```

**Posición en el pipeline:**

```
CorrelationIdMiddleware → ErrorHandlingMiddleware → RequestLogging → SecurityHeaders → CORS → Auth → Controllers
```

---

## Escaneo básico de dependencias vulnerables

```bash
bash scripts/scan-dependencies-security.sh
```

Ejecutar desde la raíz del repositorio. Requiere .NET SDK y Node.js instalados.

**Qué revisa:**

- Dependencias NuGet del backend: `dotnet list package --vulnerable --include-transitive`
- Dependencias npm del frontend: `npm audit --audit-level=moderate`

**Qué NO hace:**

- No corrige dependencias automáticamente
- No ejecuta `npm audit fix` ni `npm audit fix --force`
- No ejecuta `dotnet add package`
- No reemplaza SAST, DAST ni auditoría externa formal
- No reemplaza una revisión de seguridad para dinero real

**Exit codes:**

| Código | Significado |
|--------|-------------|
| `0` | Sin vulnerabilidades Moderate/High/Critical detectadas |
| `1` | Vulnerabilidades encontradas — revisar antes de preproducción |
| `2` | Error de ejecución (herramienta faltante o repo mal posicionado) |

**Cuándo ejecutar:**

- Automáticamente en cada push/PR a `main` — workflow `Dependency Security Scan`
- Antes de cualquier despliegue a QA
- Antes de iniciar el piloto con dinero real
- Después de actualizar cualquier dependencia

**CI automático (Fase 45):**

El workflow `.github/workflows/dependency-security-scan.yml` ejecuta el script en cada push y pull request a `main`. Si el script devuelve exit 1 (vulnerabilidades encontradas) o exit 2 (error de herramienta), el workflow falla y bloquea el merge.

Si el workflow de CI falla:
- No usar `npm audit fix --force` ni aplicar upgrades sin revisión manual.
- Revisar la salida del workflow en la pestaña Actions de GitHub.
- Evaluar y aplicar upgrades controlados (patch primero, luego minor con justificación).
- Antes de avanzar a dinero real, este workflow debe estar verde (`success`).

**Remediación controlada de vulnerabilidades**

Las vulnerabilidades encontradas por el script se evalúan manualmente antes de aplicar cualquier cambio:

- **Patch upgrades** (8.0.x → 8.0.y, mismo major): permitidos si no hay breaking changes documentados.
- **Minor/Major upgrades**: requieren aprobación explícita del responsable técnico y prueba de build completo.
- **No usar** `npm audit fix --force` sin revisar el diff — puede instalar versiones mayor con breaking changes.
- **No usar** `dotnet add package` ni cambiar versiones del `.csproj` sin evaluación de compatibilidad.

Para aplicar un upgrade seguro:
```bash
# Backend: editar Xpay.Api.csproj manualmente → dotnet restore → dotnet build → re-escaneo
# Frontend: editar package.json manualmente → npm install → npm run build → re-escaneo
```

---

## Política básica de sesión JWT

Los tokens JWT se generan con duración y clock skew configurables. `ValidateLifetime` y `ValidateIssuerSigningKey` están activos.

**Variables de configuración:**

```json
"Jwt": {
  "ExpirationHours":  2,
  "ClockSkewSeconds": 60
}
```

Variables de entorno equivalentes:

```bash
Jwt__ExpirationHours  = 2    # QA recomendado: 2 h; producción: definir política formal
Jwt__ClockSkewSeconds = 60   # Tolerancia de reloj entre servidores (segundos)
```

**Validaciones activas (`TokenValidationParameters`):**

- `ValidateLifetime = true` — tokens expirados son rechazados con 401.
- `ValidateIssuerSigningKey = true` — la firma HS256 se verifica en cada request.
- `ValidateIssuer` y `ValidateAudience` activos.

**Qué no existe todavía:**

- No hay refresh tokens.
- No hay persistencia de sesiones en base de datos.
- No hay endpoint de logout con invalidación del token.
- No hay revocación de sesiones activas.

**Lo que no cambia con estas variables:**

- Lógica financiera
- CORS
- Rate limiting
- Auditoría

---

## Auditoría básica por logs

El backend registra eventos sensibles mediante `AuditLogService` usando `ILogger`. Todos los eventos incluyen `correlationId` del request y se pueden filtrar por la marca `audit=True` o el prefijo `AUDIT` en los logs.

**Eventos auditados:**

| Evento | Controlador | Nivel |
|--------|-------------|-------|
| `LOGIN_SUCCESS` | `AuthController` | Information |
| `LOGIN_FAILURE` | `AuthController` | Warning |
| `WALLET_MANUAL_RECHARGE_ATTEMPT` / `_SUCCESS` | `WalletsController` | Information |
| `WALLET_TRANSFER_ATTEMPT` / `_SUCCESS` | `WalletsController` | Information |
| `QR_PAYMENT_ATTEMPT` / `_SUCCESS` | `QrController` | Information |
| `QR_SETTLEMENT_ATTEMPT` / `_SUCCESS` | `ComerciosController` | Information |
| `MERCHANT_WITHDRAWAL_REQUEST_ATTEMPT` / `_SUCCESS` | `ComerciosController` | Information |
| `MERCHANT_WITHDRAWAL_PAID_ATTEMPT` / `_SUCCESS` | `ComerciosController` | Information |
| `MERCHANT_WITHDRAWAL_REJECTED_ATTEMPT` / `_SUCCESS` | `ComerciosController` | Information |
| `ADMIN_WALLETS_ACCESS` | `AdminController` | Information |
| `ADMIN_VENTAS_QR_ACCESS` | `AdminController` | Information |
| `ADMIN_LEDGER_ACCESS` | `AdminController` | Information |
| `ADMIN_REPORT_ACCESS` | `ReportesController` | Information |

**Formato de log:**

```
AUDIT audit=True event=LOGIN_SUCCESS user=carlos_ci_test path=/api/auth/login method=POST correlationId=<guid>
AUDIT audit=True event=WALLET_TRANSFER_ATTEMPT user=carlos_ci_test path=/api/wallets/transferencia method=POST correlationId=<guid> metadata={ idWalletOrigen = 1, idWalletDestino = 2, valor = 50.00 }
```

**Correlation ID:** cada evento incluye el `correlationId` propagado por `CorrelationIdMiddleware`. Permite trazar la secuencia completa de un request cruzando logs de request, auditoría y errores.

**Qué NO se registra por seguridad:**

- Passwords (ninguna forma, ni hash)
- Tokens JWT
- Header `Authorization`
- Body completo de requests
- Cédulas / documentos de identidad
- Números de cuenta bancaria completos
- Nombres o datos personales sensibles
- Connection strings

**Configuración:**

```json
"Audit": {
  "EnableAuditLogs": true
}
```

Variable de entorno para deshabilitar:

```bash
Audit__EnableAuditLogs = false
```

**Importante:** esta auditoría por logs NO reemplaza auditoría persistente en base de datos, dashboard de auditoría, retención formal ni integración con SIEM o Application Insights. Esas capacidades se definen como pendientes en `docs/PREPRODUCTION_GAPS_AND_REAL_MONEY_CHECKLIST.md` (brecha S6).

---

## CORS por ambiente

El backend configura CORS mediante `Cors:AllowedOrigins`. La política `FrontendCorsPolicy` aplica a todos los endpoints.

**Reglas por ambiente:**

| Ambiente | Regla |
|----------|-------|
| Development (local) | Si `Cors:AllowedOrigins` está vacío, usa fallback `localhost:5173` y `localhost:3000`. Si está configurado, usa esos valores. |
| QA / Preproducción / Producción | `Cors:AllowedOrigins` **obligatorio**. Si no está configurado, el backend lanza `InvalidOperationException` al arrancar y no inicia. |

**Nunca se usa `AllowAnyOrigin`.** No se agrega `AllowCredentials` (no requerido).

**Configuración local (`appsettings.json`):**

```json
"Cors": {
  "AllowedOrigins": [
    "http://localhost:5173",
    "https://localhost:5173",
    "http://localhost:3000",
    "https://localhost:3000"
  ]
}
```

**Configuración QA (`appsettings.QA.example.json`):**

```json
"Cors": {
  "AllowedOrigins": [
    "https://xpay-admin-qa.azurewebsites.net"
  ]
}
```

**Variable Azure App Service:**

```bash
Cors__AllowedOrigins__0 = https://xpay-admin-qa.azurewebsites.net
# Segundo origen si aplica:
Cors__AllowedOrigins__1 = https://otro-frontend.azurewebsites.net
```

**Guard de startup (Fase 40):** si el ambiente no es Development y no hay orígenes configurados, el backend falla al arrancar con:

```
InvalidOperationException: Cors:AllowedOrigins must be configured outside Development.
Set at least one allowed origin via Cors__AllowedOrigins__0 environment variable.
```

**Log de arranque:** al iniciar, el backend registra los orígenes CORS activos:

```
CORS: FrontendCorsPolicy — allowed origins: https://xpay-admin-qa.azurewebsites.net
```

**Qué no cambia con este hardening:**

- JWT: sin cambios en validación ni emisión.
- Swagger: sin cambios; controlado por `ApiDocs:EnableSwagger`.
- Rate limiting: sin cambios; sigue activo en login.
- Auditoría: sin cambios; sigue emitiendo eventos.
- Lógica financiera: sin cambios.

**Pendiente (fases posteriores):** política definitiva por dominio productivo, revisión con Azure Front Door / App Gateway, pruebas de seguridad externas.

---

## Rate limiting básico

El backend aplica rate limiting por IP al endpoint de login usando `FixedWindowRateLimiter` nativo de .NET 8. Al exceder el límite, se devuelve HTTP 429 con un cuerpo JSON seguro.

**Endpoint protegido:**

| Endpoint | Política | Límite por defecto |
|----------|----------|--------------------|
| `POST /api/auth/login` | `LoginPolicy` (FixedWindow por IP) | 20 requests / 60 segundos |

**Respuesta al exceder el límite:**

```json
{
  "error": "rate_limit_exceeded",
  "message": "Too many requests. Please try again later.",
  "correlationId": "..."
}
```

Header adicional: `Retry-After: 60`

No incluye: IP, stack trace, detalle de usuario, tokens.

**Configuración:**

```json
"RateLimiting": {
  "EnableRateLimiting": true,
  "LoginPermitLimit": 20,
  "LoginWindowSeconds": 60,
  "LoginQueueLimit": 0
}
```

Variables de entorno (Azure App Service):

```bash
RateLimiting__EnableRateLimiting = true
RateLimiting__LoginPermitLimit   = 20   # aumentar si QA/CI supera el límite
RateLimiting__LoginWindowSeconds = 60
RateLimiting__LoginQueueLimit    = 0
```

**Qué no cambia:**

- JWT: sin cambios en validación ni emisión.
- CORS: sin cambios. CORS preflight (OPTIONS) no está sujeto a `LoginPolicy`.
- Lógica financiera: sin cambios en ningún endpoint financiero.
- Endpoints financieros: no tienen rate limiting en esta fase.

**Pendiente (fases posteriores):**

- Límites más finos por endpoint (registro, operaciones financieras, retiros).
- Lockout por usuario (no solo por IP) si aplica.
- Monitoreo y alertas de intentos fallidos.
- WAF / Azure Front Door si se requiere protección a nivel de infraestructura.

---

## Variables operativas QA

**[ops/qa.env.example](ops/qa.env.example)** — plantilla versionada con placeholders.
**[docs/QA_OPERATIONS_VARIABLES.md](docs/QA_OPERATIONS_VARIABLES.md)** — guía completa.

```bash
cp ops/qa.env.example ops/qa.env.local
# completar con valores reales
source ops/qa.env.local
bash scripts/generate-qa-financial-ops.sh
```

- `ops/qa.env.local` está en `.gitignore` — nunca se commitea.
- No contiene secretos, tokens ni contraseñas en el repositorio.
- **Uso exclusivo QA / desarrollo. No usar en producción. No usar dinero real.**

---

## Pruebas QA

| Documento | Propósito |
|-----------|-----------|
| **[docs/QA_MANUAL_TESTING.md](docs/QA_MANUAL_TESTING.md)** | 35 casos de prueba, prerrequisitos, orden de ejecución, criterios de aprobación y checklist de salida |
| **[docs/QA_EXECUTION_TEMPLATE.md](docs/QA_EXECUTION_TEMPLATE.md)** | Plantilla para registrar cada ciclo de pruebas: responsable, estado, evidencias, bugs y acta de cierre |

---

## Validación CI

El flujo completo está validado automáticamente en GitHub Actions mediante `scripts/validate-backend.sh`.
Cada push a `main` ejecuta SQL Server en Docker, aplica las 7 migraciones, corre el backend y valida todos los endpoints de Fases 1 a 10.
