# XPAY MVP — Runbook Operativo de Despliegue QA

**Versión:** 1.0  
**Fecha:** 2026-06-17  
**Tipo:** Guía operativa manual/controlada  

---

## 1. Propósito

Este runbook es la guía paso a paso para ejecutar el **despliegue real** de XPAY MVP QA Candidate v0.1 en Azure, siguiendo el alcance definido en [`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md).

> **Prerrequisito — ambiente desde cero:** si los recursos Azure (Resource Group, SQL Server, App Service) aún no existen, leer y ejecutar primero **[`docs/AZURE_QA_FOUNDATION.md`](AZURE_QA_FOUNDATION.md)**. Ese documento cubre la creación de todos los recursos, la configuración de variables de entorno y los comandos Azure CLI necesarios antes de ejecutar los pasos de este runbook.

**Qué es este documento:**
- Una guía operativa concreta con comandos, validaciones y checklist.
- Una referencia para el responsable técnico que ejecuta el despliegue.
- Un instrumento de control que deja trazabilidad de cada paso.

**Qué NO es este documento:**
- **No contiene secretos reales.** Todas las credenciales usan placeholders.
- **No ejecuta el despliegue automáticamente.** Cada paso debe ejecutarse manualmente.
- No reemplaza el juicio del responsable técnico ante situaciones imprevistas.
- No autoriza el uso de este ambiente con dinero real o usuarios de producción.

---

## 2. Información de versión

| Campo | Valor |
|-------|-------|
| **Versión** | XPAY MVP QA Candidate v0.1 |
| **Rama** | main |
| **Commit recomendado** | `52ec9cc` (o HEAD actual de `main`) |
| **Backend** | .NET 8 ASP.NET Core |
| **Frontend** | React 18 / Vite 5 |
| **Base de datos** | Azure SQL — `XPAY_MVP_QA` |
| **Tipo de despliegue** | Manual / controlado |
| **Ambiente** | QA |
| **Responsable técnico** | Por designar antes del despliegue |
| **Fecha prevista** | Por definir |

---

## 3. Recursos Azure a crear

Crear los siguientes recursos en el portal Azure o mediante Azure CLI antes de iniciar el despliegue. El orden sugerido es de arriba hacia abajo.

| Recurso | Nombre sugerido | Propósito | Tier mínimo sugerido | Obligatorio |
|---------|----------------|-----------|---------------------|-------------|
| Resource Group | `rg-xpay-qa` | Contenedor lógico de todos los recursos QA | — | Sí |
| SQL Server | `xpay-sql-qa` | Servidor de base de datos para Azure SQL | — (se define en SQL Database) | Sí |
| SQL Database | `XPAY_MVP_QA` | Base de datos relacional del sistema | Basic (5 DTU) — puede escalar | Sí |
| App Service Plan Backend | `asp-xpay-api-qa` | Plan de cómputo para el App Service del backend | B1 (Basic) | Sí |
| App Service Backend | `xpay-api-qa` | Servicio que ejecuta el backend .NET 8 | Usa `asp-xpay-api-qa` | Sí |
| App Service Plan Frontend | `asp-xpay-admin-qa` | Plan de cómputo para el frontend (si no se usa Static Web App) | B1 o Free | Condicional |
| Static Web App | `xpay-admin-qa` | Alternativa al App Service para servir el frontend estático | Free | Condicional |
| Application Insights | `ai-xpay-qa` | Telemetría y logs del backend | Free tier | No (recomendado) |
| Key Vault | `kv-xpay-qa` | Gestión segura de secretos (JWT Key, Connection String) | Standard | No (recomendado) |

> **Nota:** Usar App Service Plan + App Service para el frontend **o** Static Web App — no ambos.  
> Static Web App es más simple y económico para sitios estáticos React/Vite.

---

## 4. Variables de entorno — Backend

Configurar en **Azure App Service → Configuration → Application settings** usando doble guión bajo (`__`) como separador de jerarquía .NET.

| Variable | Descripción | Ejemplo (placeholder) | Obligatoria | Sensible |
|----------|-------------|----------------------|-------------|----------|
| `ConnectionStrings__XpayConnection` | Cadena de conexión completa a Azure SQL QA | `Server=xpay-sql-qa.database.windows.net;Database=XPAY_MVP_QA;User Id=<db-user>;Password=<db-password>;Encrypt=True;TrustServerCertificate=False;` | Sí | **Sí** |
| `Jwt__Key` | Clave secreta HS256 para firmar tokens JWT | `<CLAVE_ALEATORIA_MINIMO_32_CARACTERES>` | Sí | **Sí** |
| `Jwt__Issuer` | Emisor declarado en el token JWT | `Xpay.Api.QA` | Sí | No |
| `Jwt__Audience` | Audiencia declarada en el token JWT | `Xpay.Admin.QA` | Sí | No |
| `Jwt__ExpirationHours` | Validez del token en horas | `2` | Sí | No |
| `Jwt__ClockSkewSeconds` | Tolerancia de reloj para validación de tokens JWT | `60` | No (default 60) | No |
| `Api__Name` | Nombre de la API (visible en `/api/version`) | `XPAY API QA` | Sí | No |
| `Api__Version` | Versión de la API (visible en `/api/version`) | `0.1.0-mvp-qa` | Sí | No |
| `Cors__AllowedOrigins__0` | Origen del frontend QA permitido por CORS | `https://xpay-admin-qa.azurewebsites.net` | Sí | No |
| `Observability__EnableRequestLogging` | Habilitar logging de requests HTTP | `true` | No (default true) | No |
| `Observability__EnableCorrelationId` | Habilitar propagación de X-Correlation-ID | `true` | No (default true) | No |
| `ApiDocs__EnableSwagger` | Habilitar Swagger UI y swagger.json | `true` en QA · `false` en producción | No (default: true en Development, false en otros) | No |
| `SecurityHeaders__EnableSecurityHeaders` | Habilitar headers de seguridad HTTP básicos | `true` | No (default true) | No |
| `SecurityHeaders__EnableNoStoreCache` | Agregar `Cache-Control: no-store` a respuestas | `true` | No (default true) | No |
| `RateLimiting__EnableRateLimiting` | Habilitar rate limiting (FixedWindow por IP en login) | `true` | No (default true) | No |
| `RateLimiting__LoginPermitLimit` | Requests permitidos por ventana en endpoint login | `20` | No (default 20) | No |
| `RateLimiting__LoginWindowSeconds` | Duración de la ventana de rate limiting (segundos) | `60` | No (default 60) | No |
| `RateLimiting__LoginQueueLimit` | Requests en cola cuando se supera el límite | `0` | No (default 0) | No |
| `Audit__EnableAuditLogs` | Habilitar auditoría básica por logs (ILogger) | `true` | No (default true) | No |
| `ErrorHandling__EnableGlobalErrorHandler` | Habilitar middleware de manejo global de errores (JSON 500 seguro) | `true` | No (default true) | No |
| `Diagnostics__EnableErrorTestEndpoint` | Habilitar endpoint diagnóstico `/api/diagnostics/error-test` | `true` en QA · **`false` en producción** | No (default false) | No |
| `Https__EnableHttpsRedirection` | Habilitar redirección automática HTTP → HTTPS | `true` | No (default true) | No |
| `Https__EnableHsts` | Habilitar HSTS (`Strict-Transport-Security`); bloqueado en Development | `false` · **`true` en producción con TLS válido** | No (default false) | No |
| `Https__HstsMaxAgeDays` | Max-Age de HSTS en días | `30` | No (default 30) | No |

> **HSTS** se configura con `Https__EnableHsts=true` + `Https__HstsMaxAgeDays=<días>`. Activar solo en ambientes HTTPS reales con certificado TLS válido — nunca en Development. CSP no está implementado aún (ver `docs/PREPRODUCTION_GAPS_AND_REAL_MONEY_CHECKLIST.md`).

**Reglas críticas:**

- `Jwt__Key` debe tener **mínimo 32 caracteres** (idealmente 64+). Una clave corta hace que el backend falle al arrancar.
- `ConnectionStrings__XpayConnection` **no debe subirse nunca al repositorio**. Configurar solo en Azure App Settings.
- `Cors__AllowedOrigins__0` debe ser la URL **exacta** del frontend QA, incluyendo `https://` y sin barra final.
- Si se agrega un segundo origen, usar `Cors__AllowedOrigins__1` con la misma estructura.
- **Fase 40 — hardening:** si `Cors__AllowedOrigins__0` no está configurado en un ambiente no Development, el backend lanza `InvalidOperationException` al arrancar y no inicia. Configurar antes de hacer deploy.
- Nunca usar `Cors__AllowedOrigins__0=*` ni equivalente. El backend no acepta `AllowAnyOrigin`.

---

## 5. Variables de entorno — Frontend

| Variable | Descripción | Ejemplo (placeholder) | Obligatoria |
|----------|-------------|----------------------|-------------|
| `VITE_API_BASE_URL` | URL base del backend QA | `https://xpay-api-qa.azurewebsites.net` | Sí |

**Importante — Build time vs Runtime:**

Vite inyecta `VITE_API_BASE_URL` **en tiempo de compilación**, no en tiempo de ejecución. Esto significa:
- Si la URL del backend cambia, el frontend **debe reconstruirse** (`npm run build`) con el nuevo valor.
- En Azure Static Web App no se puede cambiar la URL sin rebuild y redeploy.
- Para QA, usar la URL real del App Service backend QA, no `localhost`.

**Flujo recomendado:**
```bash
# 1. Copiar el template de variables QA
cp frontend/xpay-admin/.env.qa.example frontend/xpay-admin/.env

# 2. Verificar/ajustar la URL si difiere del ejemplo
# Editar .env y cambiar VITE_API_BASE_URL si es necesario

# 3. Construir el frontend con las variables QA
cd frontend/xpay-admin
npm install
npm run build
# Los archivos estáticos quedan en dist/
```

---

## 6. Preparación de base de datos

Ejecutar los scripts SQL en el siguiente **orden estricto**. No saltarse ninguno ni ejecutarlos en paralelo.

| Orden | Script | Contenido |
|-------|--------|-----------|
| 1 | `database/001_security_identity.sql` | Tablas de seguridad: usuarios, personas, unidades de negocio |
| 2 | `database/002_wallet_ledger.sql` | Tablas de wallets y ledger de transacciones |
| 3 | `database/003_comercios_qr.sql` | Tablas de comercios, tiendas y QR |
| 4 | `database/004_liquidacion_qr.sql` | Tabla de ventas QR y liquidaciones |
| 5 | `database/005_retiros_comercio.sql` | Tabla de retiros de comercio |
| 6 | `database/006_gestion_retiros_comercio.sql` | Stored procedures y lógica de gestión de retiros |
| 7 | `database/007_security_roles_jwt.sql` | Roles, permisos y configuración JWT en BD |

**Script opcional — Dataset QA:**

| Orden | Script | Tipo | Contenido |
|-------|--------|------|-----------|
| 8 *(opcional)* | `database/008_seed_qa_dataset.sql` | **Seed QA** | Personas, usuarios, wallets, comercio demo QA, QR demo QA. **No es migración estructural. No ejecutar en producción.** |

> `008_seed_qa_dataset.sql` es idempotente y puede ejecutarse más de una vez. Pobla datos de prueba controlados necesarios para el manual QA. Los datos financieros (saldos, ventas QR, retiros) deben generarse vía endpoints del backend.

**Herramientas para ejecutar:**
- Azure Data Studio (recomendado para conexión a Azure SQL)
- SQL Server Management Studio (SSMS)
- Azure Cloud Shell con `sqlcmd`

**Checklist de base de datos:**

- [ ] Script 001 ejecutado sin errores
- [ ] Script 002 ejecutado sin errores
- [ ] Script 003 ejecutado sin errores
- [ ] Script 004 ejecutado sin errores
- [ ] Script 005 ejecutado sin errores
- [ ] Script 006 ejecutado sin errores
- [ ] Script 007 ejecutado sin errores
- [ ] Tablas principales confirmadas: `usuarios`, `wallets`, `ledger_transacciones`, `comercios`, `ventas_qr`, `retiros_comercio`
- [ ] Seed de datos confirmado: existe registro `Comercio Demo XPAY` en tabla `comercios`
- [ ] Seed de datos confirmado: existe QR `QR-DEMO-XPAY-001` vinculado al comercio demo
- [ ] Cuentas ledger confirmadas: existen entradas de tipo `SISTEMA` en la tabla de ledger
- [ ] *(Opcional QA)* Script 008 ejecutado: personas/usuarios/comercio QA creados
- [ ] *(Opcional QA)* Password hashes de usuarios QA actualizados antes de login

> ⚠️ **No modificar los scripts SQL 001–007.** Si alguno falla, detener, revisar el error y corregir el problema de entorno (conexión, permisos, orden) antes de continuar.

---

## 7. Despliegue backend

Ejecutar estos pasos después de que la base de datos esté lista y los recursos Azure creados.

**Paso 1 — Confirmar CI verde**  
Verificar en GitHub Actions que el commit a desplegar (`52ec9cc` o HEAD de `main`) tiene:
- `Backend Validation` → `completed success`
- `Frontend Build` → `completed success`

**Paso 2 — Crear App Service backend en Azure**  
En Azure Portal: Crear recurso → App Service → Nombre: `xpay-api-qa`, Runtime: `.NET 8`, OS: Linux, Plan: `asp-xpay-api-qa`.

**Paso 3 — Configurar runtime .NET 8**  
En `xpay-api-qa` → Configuration → General settings → Stack: `.NET`, Version: `.NET 8`.

**Paso 4 — Configurar App Settings**  
En `xpay-api-qa` → Configuration → Application settings → agregar las 7 variables de la sección 4 (excepto Connection String).

**Paso 5 — Configurar Connection String**  
En `xpay-api-qa` → Configuration → Connection strings → agregar `XpayConnection` de tipo `SQLAzure` con el valor completo de la connection string.

**Paso 6 — Desplegar backend**

*Opción A — Desde GitHub (manual zip deploy):*
```bash
# Compilar el backend localmente
dotnet publish backend/Xpay.Api/Xpay.Api.csproj -c Release -o ./publish

# Comprimir para deploy
cd publish && zip -r ../xpay-api-qa.zip . && cd ..

# Deploy via Azure CLI
az webapp deploy --resource-group rg-xpay-qa \
  --name xpay-api-qa \
  --src-path xpay-api-qa.zip \
  --type zip
```

*Opción B — Desde Azure Portal:*  
App Service → Deployment Center → seleccionar repositorio GitHub, rama `main`.

**Paso 7 — Probar `/health`**
```bash
curl https://xpay-api-qa.azurewebsites.net/health
# Esperado: HTTP 200 {"status":"Healthy"}
```

**Paso 8 — Probar `/api/version`**
```bash
curl https://xpay-api-qa.azurewebsites.net/api/version
# Esperado: HTTP 200 con nombre y versión
```

**Paso 9 — Probar `/swagger`**  
Abrir `https://xpay-api-qa.azurewebsites.net/swagger` en el navegador. La UI debe cargar con el botón **Authorize**.

**Paso 10 — Probar login**
```bash
curl -X POST https://xpay-api-qa.azurewebsites.net/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"usuario":"<usuario_prueba>","password":"<password_prueba>"}'
# Esperado: HTTP 200, campo data.token presente
```

---

## 8. Despliegue frontend

Ejecutar después de que el backend QA esté desplegado y validado.

**Paso 1 — Confirmar `VITE_API_BASE_URL`**  
Verificar que la URL del backend QA es accesible:
```bash
curl https://xpay-api-qa.azurewebsites.net/health
```

**Paso 2 — Crear `.env` desde el template QA**
```bash
cp frontend/xpay-admin/.env.qa.example frontend/xpay-admin/.env
# Verificar que VITE_API_BASE_URL apunta al backend QA correcto
cat frontend/xpay-admin/.env
```

**Paso 3 — Construir el frontend**
```bash
cd frontend/xpay-admin
npm install
npm run build
# Resultado: dist/ con index.html y assets/
```

**Paso 4 — Desplegar `dist/`**

*Opción A — Azure Static Web App (recomendado):*
```bash
# Instalar CLI de Azure Static Web Apps
npm install -g @azure/static-web-apps-cli

# Deploy
swa deploy frontend/xpay-admin/dist \
  --deployment-token <TOKEN_SWA_QA> \
  --env production
```

*Opción B — Azure App Service (zip deploy):*
```bash
cd frontend/xpay-admin/dist
zip -r ../../xpay-admin-qa.zip .
az webapp deploy --resource-group rg-xpay-qa \
  --name xpay-admin-qa \
  --src-path xpay-admin-qa.zip \
  --type zip
```

**Paso 5 — Verificar que abre `/login`**  
Abrir `https://xpay-admin-qa.azurewebsites.net/login` en el navegador. Debe mostrar el formulario de login.

**Paso 6 — Verificar API visible en login**  
Debajo del formulario debe aparecer: `API: https://xpay-api-qa.azurewebsites.net`. Si muestra `localhost`, el build se generó con `.env` incorrecto → repetir paso 2 y 3.

**Paso 7 — Probar login**  
Ingresar con el usuario de prueba. Debe redirigir a `/dashboard`.

**Paso 8 — Probar dashboard**  
Verificar que las métricas y las 3 tablas cargan sin errores de consola (F12 → Console).

---

## 9. Smoke test post-despliegue

Ejecutar inmediatamente después del despliegue, antes de abrir el ambiente a testers:

- [ ] `GET /health` → HTTP 200
- [ ] `GET /api/version` → HTTP 200, nombre y versión correctos
- [ ] `/swagger` → UI carga con botón Authorize *(solo si `ApiDocs__EnableSwagger=true`; en producción debe estar deshabilitado)*
- [ ] Login con usuario de prueba → redirige a `/dashboard`
- [ ] `GET /api/wallets/persona/1` sin token → **HTTP 401** (no 200, no 500)
- [ ] Dashboard carga con métricas y 3 tablas sin errores
- [ ] `/wallets/listado` carga con datos
- [ ] `/comercios/listado` carga con datos
- [ ] `/retiros/listado` carga con datos
- [ ] `/ventas-qr/listado` carga con datos
- [ ] `/ledger/listado` carga con datos
- [ ] CORS correcto: no hay error `Access-Control-Allow-Origin` en consola del navegador
- [ ] `API:` en header del frontend muestra el hostname QA (no `localhost`)
- [ ] `API:` en pantalla de login muestra la URL QA correcta
- [ ] No hay datos reales de clientes ni saldos reales en el sistema
- [ ] `GET /api/diagnostics/ping` → HTTP 200 + campo `status: "OK"` (Fase 35)
- [ ] Response de `/api/diagnostics/ping` incluye header `X-Correlation-ID` con valor no vacío
- [ ] Los logs del backend muestran entradas con `CorrelationId` para cada request (revisar consola o log stream en Azure)
- [ ] Response de `/api/diagnostics/ping` incluye `X-Content-Type-Options: nosniff` (Fase 37)
- [ ] Response incluye `X-Frame-Options: DENY` (Fase 37)
- [ ] Response incluye `Referrer-Policy: no-referrer` (Fase 37)
- [ ] Login normal responde 200 (no 429) — rate limiting activo con límite amplio de 20 req/min (Fase 38)
- [ ] Auditoría básica activa: en los logs del backend, un login exitoso genera una entrada con `event=LOGIN_SUCCESS` y `audit=True` (Fase 39)
- [ ] Un intento de login fallido genera entrada con `event=LOGIN_FAILURE` y `reason=credentials_invalid` (Fase 39)
- [ ] Una operación financiera QA genera entradas con `event=QR_PAYMENT_ATTEMPT` / `QR_PAYMENT_SUCCESS` u operación equivalente (Fase 39)
- [ ] Los eventos de auditoría incluyen `correlationId` coincidente con header `X-Correlation-ID` (Fase 39)
- [ ] Preflight CORS desde frontend QA (`OPTIONS /api/auth/login -H "Origin: https://xpay-admin-qa.azurewebsites.net"`) devuelve `Access-Control-Allow-Origin: https://xpay-admin-qa.azurewebsites.net` (Fase 40)
- [ ] Preflight CORS desde origen desconocido (`Origin: https://evil.example.com`) NO devuelve ese origen en `Access-Control-Allow-Origin` (Fase 40)
- [ ] Los logs de arranque del backend contienen entrada `CORS: FrontendCorsPolicy — allowed origins:` con la URL QA correcta (Fase 40)
- [ ] `GET /api/diagnostics/error-test` responde HTTP 500 (Fase 41 — requiere `Diagnostics__EnableErrorTestEndpoint=true`)
- [ ] El body del 500 contiene `"error":"internal_server_error"` y un `correlationId` no vacío (Fase 41)
- [ ] El body del 500 NO contiene stack trace, mensaje de excepción interno ni detalles de infraestructura (Fase 41)
- [ ] El header `X-Correlation-ID` está presente en la respuesta 500 (Fase 41)
- [ ] Escaneo de dependencias ejecutado: `bash scripts/scan-dependencies-security.sh` desde raíz del repo (Fase 43)
- [ ] Workflow **Dependency Security Scan** en verde (`success`) en GitHub Actions para el commit a desplegar (Fase 45)
- [ ] Si exit code 1 o workflow falla: hallazgos registrados y decisión de riesgo documentada antes de continuar

**Smoke test UI — Frontend QA (Fase 51):**

- [ ] `GET https://xpay-admin-qa.azurewebsites.net/` → HTTP 200, título "XPAY Admin"
- [ ] SPA routing: `/dashboard`, `/wallets`, `/comercios` → HTTP 200 (sin 404)
- [ ] Login en UI con `qa.admin.xpay` / `XpayDemo2026!` → redirige a `/dashboard`
- [ ] DevTools Network: `POST /api/auth/login` → 200 con token JWT
- [ ] DevTools Network: llamadas protegidas incluyen header `Authorization: Bearer ...`
- [ ] DevTools Network: todas las llamadas API van a `xpay-api-qa.azurewebsites.net` (no `localhost`, no producción)
- [ ] No hay errores CORS en consola del navegador (F12 → Console)
- [ ] No hay 401 inesperado después del login
- [ ] No hay datos reales de personas, cédulas ni saldos reales visibles
- [ ] Cerrar sesión: redirige a `/login` y el token se elimina de `localStorage`
- [ ] `UseHttpsRedirection` activa: request HTTP al backend redirige a HTTPS (verificar en Azure App Service con `curl -I http://...` y observar 301/302 a `https://`) (Fase 46)
- [ ] Si `Https__EnableHsts=true` en ambiente HTTPS real: response incluye `Strict-Transport-Security: max-age=<días>` sin `preload` ni `includeSubDomains` (Fase 46)
- [ ] En Development (`ASPNETCORE_ENVIRONMENT=Development`): HSTS no se activa aunque `Https__EnableHsts=true` en config (garantizado por código; no requiere smoke test manual)
- [ ] `GET /api/diagnostics/ready` responde HTTP 200 con `"status":"READY"` y campo `correlationId` no vacío (Fase 47)
- [ ] `GET /api/diagnostics/ping` responde HTTP 200 con `"status":"OK"` — uptime probe activo (Fase 35)
- [ ] `GET /health` responde HTTP 200 con `"status":"Healthy"` — probe básico de plataforma
- [ ] `GET /api/version` responde HTTP 200 con `success: true` y `data.version` no vacío
- [ ] Revisar `docs/OBSERVABILITY_AND_ALERTING_RUNBOOK.md` y confirmar responsable de alertas, canal de incidentes y horario de monitoreo antes del piloto (Fase 47)
- [ ] Confirmar los 3 workflows de GitHub Actions en verde (`success`) para el commit desplegado: **Backend Validation**, **Frontend Build**, **Dependency Security Scan** (Fase 47)
- [ ] Si High/Critical: no avanzar a dinero real sin aprobación explícita del Security Lead y corrección o aceptación formal firmada
- [ ] Si solo Moderate/Low: registrar decisión de riesgo aceptado con justificación y firma del Security Lead
- [ ] Salida del script o enlace al run de GitHub Actions adjuntada como evidencia en el package de QA/preproducción

> **Error handling en QA (Fase 41):** en producción, configurar `Diagnostics__EnableErrorTestEndpoint=false` para deshabilitar el endpoint de prueba. El middleware `ErrorHandlingMiddleware` sigue activo para capturar excepciones reales; solo el endpoint de test se deshabilita. Si se requiere debugging local, `ErrorHandling__EnableGlobalErrorHandler=false` solo en sesión autorizada.
> **CORS en QA (Fase 40):** si el frontend no puede conectarse, revisar CORS antes de tocar JWT o endpoints. Verificar `Cors__AllowedOrigins__0` en Azure App Settings. Si el backend no arranca (503), puede ser que la variable esté ausente — el backend lanza excepción al inicio si no hay orígenes configurados en no-Development.
> **Auditoría en logs QA:** filtrar por `AUDIT` en la consola del backend o log stream de Azure App Service. Búsqueda alternativa: `grep '"audit":true'` o `grep 'event=LOGIN'`. La ausencia de estos eventos con `Audit__EnableAuditLogs=true` es un defecto bloqueante.
> **Rate limiting en QA:** si aparece HTTP 429 en pruebas manuales o CI, verificar que el volumen de llamadas no supera `RateLimiting__LoginPermitLimit` en la ventana de `RateLimiting__LoginWindowSeconds`. Ajustar los valores vía variable de entorno sin deshabilitar completamente.
> **CSP y HSTS:** no se configuran en esta fase. CSP requiere análisis para no romper Swagger UI; HSTS requiere HTTPS productivo. Ambos se definen en una fase posterior o a nivel de App Service / Front Door.

> **Investigar errores por correlation ID:** al reportar un error, incluir el valor del header `X-Correlation-ID` de la respuesta. El responsable técnico puede buscar ese ID en los logs del backend para trazar el request completo.

---

## 10. Validaciones financieras mínimas QA

Ejecutar después del smoke test para confirmar integridad del sistema financiero con datos de prueba:

- [ ] Saldo de wallet visible en `/wallets/:idWallet` (número positivo, no nulo)
- [ ] Estado de cuenta de wallet carga con movimientos en el historial
- [ ] Detalle ledger en `/ledger/:idTransaccion` muestra descripción, valor y tipo
- [ ] Débitos y créditos en el ledger son **numéricamente consistentes**: para una venta QR, el débito en wallet usuario y el crédito en wallet comercio tienen el mismo valor
- [ ] Retiro con estado `PAGADO` o `RECHAZADO` **no muestra botones de acción** (confirmar/rechazar)
- [ ] Confirmar/rechazar retiro se ejecuta **únicamente en ambiente QA con datos de prueba** — registrar el ID del retiro usado en evidencias

> ⚠️ **Nunca ejecutar confirmación o rechazo de retiros sobre datos que puedan confundirse con transacciones reales.**

---

## 11. Rollback básico

> **Referencia completa:** para la estrategia formal de rollback con criterios de activación (R1–R13), matriz rollback/fix-forward, procedimientos por componente y plantilla de acta, consultar **`docs/ROLLBACK_AND_RECOVERY_RUNBOOK.md`**.

Las siguientes acciones son guía rápida para el ambiente QA. En caso de incidente Critical o High, usar el runbook completo.

### Pre-deploy: checklist de rollback readiness

Completar antes de cada deploy en QA o piloto:

- [ ] Identificar el último commit estable con los 3 GitHub Actions en `success`
- [ ] Anotar el SHA del commit estable como punto de rollback
- [ ] Exportar/capturar snapshot de App Settings de Azure antes del deploy (`az webapp config appsettings list ...` o export desde Azure Portal) — **no versionar en git**
- [ ] Confirmar que los artefactos backend (`artifacts/backend-qa`) y frontend (`artifacts/frontend-qa`) del commit estable están disponibles o pueden reconstruirse rápidamente
- [ ] Confirmar rollback owner: responsable técnico disponible durante el deploy
- [ ] Leer `docs/ROLLBACK_AND_RECOVERY_RUNBOOK.md` sección 4 (criterios) y sección 5 (matriz) antes del deploy

### Si el backend falla al arrancar o tras el despliegue

1. Revisar logs en Azure Portal: `xpay-api-qa` → Log stream.
2. Verificar variables de entorno (sección 4): JWT Key ≥ 32 chars, Connection String correcta.
3. Si el problema es de código y no tiene fix inmediato: redeployar el commit estable identificado en el pre-deploy checklist.
   ```bash
   git checkout <sha-commit-estable>
   dotnet publish backend/Xpay.Api/Xpay.Api.csproj -c Release -o ./publish-rollback
   cd publish-rollback && zip -r ../xpay-rollback.zip . && cd ..
   az webapp deploy --resource-group rg-xpay-qa --name xpay-api-qa \
     --src-path xpay-rollback.zip --type zip
   git checkout main
   ```
4. Validar con los probes de la sección 9 y con `GET /api/diagnostics/ready`.
5. Registrar el rollback en el acta (plantilla en `docs/ROLLBACK_AND_RECOVERY_RUNBOOK.md` sección 11).

### Si el frontend falla o apunta al backend incorrecto

1. Verificar `VITE_API_BASE_URL` en el `.env` antes del build.
2. Reconstruir con la URL correcta y redesplegar `dist/`.
3. Limpiar caché del navegador (`Ctrl+Shift+R`) antes de verificar.
4. Si el fallo persiste: redeployar el artefacto `artifacts/frontend-qa` del commit estable.

### Si una migración SQL falla

1. **Detener inmediatamente.** No ejecutar los scripts siguientes.
2. Revisar el mensaje de error exacto (tabla ya existente, constraint violado, etc.).
3. Si el ambiente QA tiene datos relevantes, tomar backup antes de continuar.
4. Corregir el problema de entorno (no modificar el script) y volver a ejecutar desde el script que falló.

### Si CORS falla (error `Access-Control-Allow-Origin`)

1. **Verificar `Cors__AllowedOrigins__0`** en Azure App Settings: debe ser exactamente la URL del frontend QA (sin barra final, con `https://`). Es la primera causa de fallo CORS — revisar esto antes de tocar JWT o endpoints.
2. Si el App Service no inició (`503` o error de arranque), revisar los logs de inicio — puede ser que `Cors__AllowedOrigins__0` esté ausente (Fase 40: el backend no arranca sin CORS configurado en no-Development).
3. Guardar los cambios en Configuration y **reiniciar el App Service** (`Overview → Restart`).
4. Esperar 30 segundos y probar nuevamente.

### Si JWT falla (login devuelve 500 o token inválido)

1. Verificar que `Jwt__Key` tiene mínimo 32 caracteres.
2. Verificar que `Jwt__Issuer` y `Jwt__Audience` coinciden exactamente entre backend y cualquier validación externa.
3. Guardar y reiniciar el App Service.

> **Para QA:** siempre tomar un backup del estado de la BD antes de ejecutar migraciones si hay datos de prueba valiosos. Azure SQL permite crear backups desde el portal con un clic.
> **Regla crítica:** el rollback técnico **no revierte operaciones financieras ya ejecutadas**. Si hubo operaciones durante el periodo con código defectuoso, notificar al responsable financiero antes de declarar el incidente cerrado.

---

## 12. Checklist de "QA listo para testers"

Completar todos los ítems antes de comunicar al equipo QA que el ambiente está disponible:

- [ ] Recursos Azure creados: Resource Group, SQL Server, SQL Database, App Service(s)
- [ ] Scripts SQL 001 a 007 ejecutados en orden, sin errores
- [ ] Backend desplegado y respondiendo en `https://xpay-api-qa.azurewebsites.net`
- [ ] Frontend desplegado y accesible en su URL QA
- [ ] Variables de entorno configuradas en Azure App Settings (sin secretos en repo)
- [ ] GitHub Actions `Backend Validation` en `completed success` para el commit desplegado
- [ ] GitHub Actions `Frontend Build` en `completed success` para el commit desplegado
- [ ] Smoke test completo (sección 9) ejecutado y todos los ítems ✅
- [ ] `docs/QA_MANUAL_TESTING.md` compartido con el equipo QA
- [ ] Copia de `docs/QA_EXECUTION_TEMPLATE.md` preparada para registrar la ejecución real
- [ ] `docs/RELEASE_QA_CANDIDATE.md` revisado: responsable técnico confirma que el alcance es correcto
- [ ] Responsable técnico firma/aprueba formalmente el inicio de pruebas
- [ ] SHA del commit estable anotado como punto de rollback: `____________`
- [ ] Snapshot de App Settings exportado y guardado en ubicación segura (no en git)
- [ ] Rollback owner identificado y disponible durante las pruebas QA
- [ ] `docs/ROLLBACK_AND_RECOVERY_RUNBOOK.md` revisado por el responsable técnico (Fase 48)

---

## 13. Problemas comunes

| Síntoma | Causa probable | Solución |
|---------|---------------|----------|
| Login devuelve HTTP 401 | Usuario no existe en la BD o contraseña incorrecta | Verificar que los scripts SQL se ejecutaron; crear usuario de prueba manualmente si es necesario |
| Frontend muestra *"No fue posible conectar con el backend XPAY"* | `VITE_API_BASE_URL` incorrecto, backend caído, o CORS bloqueando | Verificar URL en pantalla de login; probar `/health` directamente; revisar CORS |
| Error `Access-Control-Allow-Origin` en consola | `Cors__AllowedOrigins__0` no coincide con la URL del frontend | Corregir la variable en Azure App Settings y reiniciar el App Service |
| Swagger abre pero endpoints devuelven 500 | Variables de entorno incompletas o DB no migrada | Revisar Log Stream del App Service; verificar Connection String y migraciones |
| SQL Server login failed | Credenciales de BD incorrectas o firewall bloqueando | Verificar `User Id` y `Password` en la connection string; agregar IP del App Service al firewall de Azure SQL |
| JWT Key corta — backend no arranca | `Jwt__Key` menor a 32 caracteres | Reemplazar con una clave aleatoria de mínimo 32 caracteres en Azure App Settings |
| Dashboard no carga métricas | Endpoint `/api/reportes/operaciones/resumen-general` falla | Verificar token en request; revisar logs del App Service; confirmar que las tablas tienen datos seed |
| Frontend apunta a `localhost` en QA | Build generado con `.env` local en lugar de QA | Reconstruir con `cp .env.qa.example .env` y `npm run build`; redesplegar `dist/` |
| Retiros no aparecen en listado | No hay retiros creados en la BD QA | Crear retiro de prueba mediante `POST /api/comercios/solicitar-retiro` con datos de prueba |
| Ledger vacío en listado | Scripts SQL ejecutados pero no hay transacciones | Ejecutar al menos una operación de prueba (login + recarga + pago QR) para generar entradas en el ledger |

---

## Scripts auxiliares de build QA

Antes de ejecutar el despliegue manual descrito en las secciones anteriores, es posible generar los artefactos de publicación localmente usando los scripts de la carpeta `scripts/`:

| Script | Qué genera | Dónde deja el artefacto |
|--------|-----------|------------------------|
| `scripts/build-backend-qa.sh` | Publica el backend .NET 8 en modo Release | `artifacts/backend-qa/` |
| `scripts/build-frontend-qa.sh` | Construye el frontend Vite (requiere `.env`) | `artifacts/frontend-qa/` |
| `scripts/build-qa-artifacts.sh` | Ejecuta ambos en orden | `artifacts/backend-qa/` y `artifacts/frontend-qa/` |

**Prerequisito frontend:** copiar el template de variables antes de ejecutar el script:

```bash
cp frontend/xpay-admin/.env.qa.example frontend/xpay-admin/.env
# Verificar/ajustar VITE_API_BASE_URL en .env
```

**Uso:**

```bash
# Artefacto backend únicamente
bash scripts/build-backend-qa.sh

# Artefacto frontend únicamente (requiere .env)
bash scripts/build-frontend-qa.sh

# Ambos artefactos de una vez
bash scripts/build-qa-artifacts.sh
```

**Propiedades importantes de los scripts:**
- No despliegan a ningún ambiente.
- No contienen secretos reales.
- Si `frontend/xpay-admin/.env` no existe, `build-frontend-qa.sh` falla con mensaje claro y `exit 1`.
- Los artefactos quedan en `artifacts/`, carpeta ignorada por git (no se suben al repositorio).
- El `artifacts/backend-qa/` generado es el directorio equivalente a lo que se sube al App Service backend en Azure.
- El `artifacts/frontend-qa/` generado es el directorio equivalente al `dist/` que se sube al App Service / Static Web App frontend en Azure.

---

## 14. Documentos relacionados

| Documento | Propósito |
|-----------|-----------|
| [`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md) | Declaración formal de la versión: alcance, fases, riesgos y criterios |
| [`docs/QA_DEPLOYMENT.md`](QA_DEPLOYMENT.md) | Guía de configuración de variables y arquitectura QA |
| [`docs/QA_MANUAL_TESTING.md`](QA_MANUAL_TESTING.md) | 35 casos de prueba manuales con pasos y resultados esperados |
| [`docs/QA_EXECUTION_TEMPLATE.md`](QA_EXECUTION_TEMPLATE.md) | Plantilla de registro de ejecución, evidencias y acta de cierre |
| [`README.md`](../README.md) | Descripción del backend: endpoints, configuración, fases completadas |
| [`frontend/xpay-admin/README.md`](../frontend/xpay-admin/README.md) | Configuración del frontend: instalación, build, rutas, errores |
| [`docs/QA_FINANCIAL_OPERATIONS_API.md`](QA_FINANCIAL_OPERATIONS_API.md) | Guía de operaciones financieras QA vía API: recarga, pago QR, liquidación, retiros y validación contable |
| [`scripts/generate-qa-financial-ops.sh`](../scripts/generate-qa-financial-ops.sh) | Script auxiliar opcional post-seed: ejecuta el flujo financiero QA completo (A–H) vía endpoints reales sin SQL ni deploy |
| [`docs/QA_OPERATIONS_VARIABLES.md`](QA_OPERATIONS_VARIABLES.md) | Guía de variables operativas QA: cómo obtener TOKEN e IDs, cargar `ops/qa.env.local` y ejecutar scripts locales |
| [`docs/QA_MASTER_E2E_CHECKLIST.md`](QA_MASTER_E2E_CHECKLIST.md) | Checklist maestro QA end-to-end: ciclo completo desde CI verde hasta acta de aprobación |
| [`docs/AZURE_QA_FOUNDATION.md`](AZURE_QA_FOUNDATION.md) | Plan Azure QA desde cero: arquitectura, recursos, variables, comandos CLI, scripts DB, smoke test, checklist demo socios |
| [`docs/AZURE_QA_DEPLOYMENT_STATUS.md`](AZURE_QA_DEPLOYMENT_STATUS.md) | Estado real del deploy Fases 50–51: recursos creados, URLs backend + frontend, validación endpoints |
| [`docs/AZURE_QA_FRONTEND_STATUS.md`](AZURE_QA_FRONTEND_STATUS.md) | Detalle del despliegue frontend QA Fase 51: estrategia, build, deploy, SPA routing, CORS, login UI |
| [`docs/OBSERVABILITY_AND_ALERTING_RUNBOOK.md`](OBSERVABILITY_AND_ALERTING_RUNBOOK.md) | Endpoints de monitoreo, matriz de alertas, runbook de respuesta a incidentes |
| [`docs/ROLLBACK_AND_RECOVERY_RUNBOOK.md`](ROLLBACK_AND_RECOVERY_RUNBOOK.md) | Criterios de rollback, matriz rollback/fix-forward, procedimientos por componente, plantilla de acta |

---

*Este runbook cubre el MVP XPAY QA Candidate v0.1 — Fases 1 a 28. Actualizar si cambia la arquitectura de despliegue o se agregan servicios Azure.*
