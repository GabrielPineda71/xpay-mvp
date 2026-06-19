# XPAY MVP — Azure QA Foundation

**Fase:** 49  
**Estado:** Plan documentado — recursos Azure pendientes de creación manual por el responsable técnico  
**Propósito:** Demo QA controlada para socios  
**No aplica a:** Producción · Dinero real · Datos reales · Clientes reales

---

## 1. Objetivo

Crear un ambiente QA visible en Azure para demostrar el MVP XPAY a socios. Este ambiente usa datos ficticios, montos ficticios y usuarios demo. No implica dinero real, cédulas reales, correos reales de usuarios finales ni operación financiera real de ningún tipo.

**Regla fundamental:** antes de crear cualquier recurso Azure, el responsable técnico debe autenticarse (`az login`) y confirmar suscripción y región objetivo (`az account show`). Este documento NO crea recursos automáticamente.

---

## 2. Arquitectura QA propuesta

### 2.1 Diagrama de componentes

```
┌─────────────────────────────────────────────────────────┐
│                  Azure — Resource Group                 │
│                     rg-xpay-qa                          │
│                                                         │
│  ┌──────────────────┐      ┌──────────────────────┐    │
│  │  App Service     │      │  Static Web Apps      │    │
│  │  xpay-api-qa     │◄─────│  swa-xpay-admin-qa    │    │
│  │  (.NET 8)        │      │  (Vite React SPA)     │    │
│  └────────┬─────────┘      └──────────────────────┘    │
│           │                                             │
│  ┌────────▼─────────┐      ┌──────────────────────┐    │
│  │  Azure SQL Server│      │  App Insights (opc.) │    │
│  │  sql-xpay-qa     │      │  ai-xpay-qa          │    │
│  │  sqldb-xpay-qa   │      └──────────────────────┘    │
│  └──────────────────┘                                   │
└─────────────────────────────────────────────────────────┘
```

### 2.2 Recursos QA

| Recurso | Nombre sugerido | Tipo | Tier recomendado QA |
|---------|----------------|------|---------------------|
| Resource Group | `rg-xpay-qa` | Resource Group | N/A |
| SQL Server | `sql-xpay-qa` | Azure SQL Server | N/A |
| SQL Database | `sqldb-xpay-qa` | Azure SQL Database | Basic o Standard S0 (demo) |
| App Service Plan | `asp-xpay-api-qa` | App Service Plan | B1 (Basic, Linux) |
| App Service backend | `xpay-api-qa` | Web App (.NET 8) | — (usa asp-xpay-api-qa) |
| Frontend | `swa-xpay-admin-qa` | **Static Web Apps** *(recomendado)* | Free tier |
| Application Insights | `ai-xpay-qa` | Application Insights | — (opcional, tier Consumption) |
| Storage | `stxpayqa` | Storage Account | LRS · opcional para artefactos |

> **Nomenclatura:** se usa el patrón existente en `.env.qa.example` y `appsettings.QA.example.json` (`xpay-api-qa`, `xpay-admin-qa`) para consistencia con los scripts y runbooks ya escritos. Ajustar si la suscripción ya tiene recursos con estos nombres.

### 2.3 Frontend — opción recomendada: Static Web Apps

**Recomendación principal: Azure Static Web Apps (`swa-xpay-admin-qa`)**

| Criterio | Static Web Apps | App Service (estático) |
|----------|----------------|----------------------|
| Costo QA | **Free tier disponible** | B1 ~$13/mes |
| CI/CD integrado | Sí (GitHub Actions automático) | Manual (zip deploy) |
| CDN global | Sí (incluido) | Solo con Front Door adicional |
| SSL automático | Sí | Requiere configuración |
| Aplica a Vite SPA | Sí — deploy de `dist/` | Sí |
| Routing SPA (404 → index.html) | Configurar `staticwebapp.config.json` | Configurar en App Service |
| Complejidad QA | Baja | Media |

**Alternativa: App Service `xpay-admin-qa`** — usar si ya existe el plan `asp-xpay-api-qa` y se quiere simplicidad de gestión (un solo App Service Plan para backend y frontend). La URL quedaría `https://xpay-admin-qa.azurewebsites.net`, que es la referenciada en `appsettings.QA.example.json` (CORS).

> Para la demo QA con socios, **Static Web Apps** es preferible por costo cero y despliegue más rápido. Para producción, evaluar App Service con Front Door.

---

## 3. Variables de entorno — Backend QA

Configurar como **App Settings** en Azure App Service (nunca en el repositorio). Todos los valores marcados como `<placeholder>` deben ser generados de forma segura antes del deploy.

| Variable | Valor recomendado QA | Sensible |
|----------|---------------------|---------|
| `ConnectionStrings__XpayConnection` | `Server=sql-xpay-qa.database.windows.net;Database=sqldb-xpay-qa;User Id=<db-user>;Password=<db-password>;Encrypt=True;TrustServerCertificate=False;` | **Sí — no versionar** |
| `Jwt__Key` | `<clave-aleatoria-min-64-chars>` | **Sí — no versionar** |
| `Jwt__Issuer` | `Xpay.Api.QA` | No |
| `Jwt__Audience` | `Xpay.Admin.QA` | No |
| `Jwt__ExpirationHours` | `2` | No |
| `Jwt__ClockSkewSeconds` | `60` | No |
| `Api__Name` | `XPAY API QA` | No |
| `Api__Version` | `0.1.0-mvp-qa` | No |
| `Cors__AllowedOrigins__0` | URL exacta del frontend QA (ej. `https://swa-xpay-admin-qa.azurestaticapps.net` o `https://xpay-admin-qa.azurewebsites.net`) | No |
| `ApiDocs__EnableSwagger` | `true` (QA) · `false` (producción) | No |
| `SecurityHeaders__EnableSecurityHeaders` | `true` | No |
| `SecurityHeaders__EnableNoStoreCache` | `true` | No |
| `RateLimiting__EnableRateLimiting` | `true` | No |
| `RateLimiting__LoginPermitLimit` | `20` | No |
| `RateLimiting__LoginWindowSeconds` | `60` | No |
| `RateLimiting__LoginQueueLimit` | `0` | No |
| `Audit__EnableAuditLogs` | `true` | No |
| `ErrorHandling__EnableGlobalErrorHandler` | `true` | No |
| `Diagnostics__EnableErrorTestEndpoint` | `true` (QA) · `false` (producción) | No |
| `Https__EnableHttpsRedirection` | `true` | No |
| `Https__EnableHsts` | `false` (inicialmente) · evaluar `true` después de validar TLS | No |
| `Https__HstsMaxAgeDays` | `30` | No |
| `ASPNETCORE_ENVIRONMENT` | `Production` | No |

> **`ASPNETCORE_ENVIRONMENT=Production`** en Azure App Service hace que el backend no tome el `appsettings.Development.json` local. Todas las variables arriba sobreescriben los defaults. El fallback CORS para `Development` (localhost) no aplica en Azure.

> **Generar `Jwt__Key`:** usar `openssl rand -base64 48` o equivalente. Nunca usar la clave del repositorio (`XpayMvpDevKeyParaCIYDesarrolloLocal2024Secure!`) en QA ni producción.

---

## 4. Variables de entorno — Frontend QA

El frontend usa Vite. La variable se inyecta en tiempo de build (no en runtime).

| Variable | Valor QA | Archivo |
|----------|---------|--------|
| `VITE_API_BASE_URL` | `https://xpay-api-qa.azurewebsites.net` | `frontend/xpay-admin/.env` (local, no versionar) |

**Cómo configurar antes del build:**

```bash
cp frontend/xpay-admin/.env.qa.example frontend/xpay-admin/.env
# Verificar que VITE_API_BASE_URL apunta a la URL real del backend QA
cat frontend/xpay-admin/.env
```

El archivo `frontend/xpay-admin/.env.qa.example` ya contiene el valor de referencia. Copiarlo y ajustar la URL si el nombre del recurso App Service difiere.

> **Importante:** si el frontend se despliega en Static Web Apps (`swa-xpay-admin-qa`), la URL del frontend cambiará (ej. `https://swa-xpay-admin-qa.azurestaticapps.net`). Actualizar `Cors__AllowedOrigins__0` en el backend con esa URL exacta antes de hacer deploy.

---

## 5. Pasos para crear el ambiente Azure desde cero

> **Prerrequisito:** tener instalado Azure CLI (`az --version ≥ 2.50`), `dotnet 8`, `node 20`, `npm`.

### Paso 0 — Autenticación y selección de suscripción

```bash
az login
az account show                        # confirmar tenant y subscription
az account set --subscription "<ID>"   # si hay múltiples suscripciones
```

### Paso 1 — Resource Group

```bash
az group create \
  --name rg-xpay-qa \
  --location eastus          # ajustar según región del equipo
```

### Paso 2 — Azure SQL Server

```bash
az sql server create \
  --name sql-xpay-qa \
  --resource-group rg-xpay-qa \
  --location eastus \
  --admin-user xpay_admin \
  --admin-password "<SA_PASSWORD_SEGURO>"   # mínimo 16 chars, mayúscula, número, especial
```

> La contraseña SA no se almacena en el repo. Guardar en gestor de contraseñas o Key Vault.

### Paso 3 — Azure SQL Database

```bash
az sql db create \
  --name sqldb-xpay-qa \
  --server sql-xpay-qa \
  --resource-group rg-xpay-qa \
  --edition Basic \
  --capacity 5               # DTUs — ajustar si se necesita más rendimiento
```

### Paso 4 — Firewall temporal para administrador

```bash
# Obtener IP pública actual del administrador
MY_IP=$(curl -s https://api.ipify.org)

az sql server firewall-rule create \
  --name AllowAdminIP \
  --server sql-xpay-qa \
  --resource-group rg-xpay-qa \
  --start-ip-address "$MY_IP" \
  --end-ip-address "$MY_IP"

# Habilitar acceso desde Azure Services (para App Service)
az sql server firewall-rule create \
  --name AllowAzureServices \
  --server sql-xpay-qa \
  --resource-group rg-xpay-qa \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0
```

### Paso 5 — App Service Plan (backend)

```bash
az appservice plan create \
  --name asp-xpay-api-qa \
  --resource-group rg-xpay-qa \
  --sku B1 \
  --is-linux
```

### Paso 6 — Web App backend (.NET 8)

```bash
az webapp create \
  --name xpay-api-qa \
  --resource-group rg-xpay-qa \
  --plan asp-xpay-api-qa \
  --runtime "DOTNETCORE:8.0"
```

### Paso 7 — Configurar App Settings backend

```bash
az webapp config appsettings set \
  --name xpay-api-qa \
  --resource-group rg-xpay-qa \
  --settings \
    "ASPNETCORE_ENVIRONMENT=Production" \
    "Jwt__Issuer=Xpay.Api.QA" \
    "Jwt__Audience=Xpay.Admin.QA" \
    "Jwt__ExpirationHours=2" \
    "Jwt__ClockSkewSeconds=60" \
    "Api__Name=XPAY API QA" \
    "Api__Version=0.1.0-mvp-qa" \
    "ApiDocs__EnableSwagger=true" \
    "SecurityHeaders__EnableSecurityHeaders=true" \
    "SecurityHeaders__EnableNoStoreCache=true" \
    "RateLimiting__EnableRateLimiting=true" \
    "RateLimiting__LoginPermitLimit=20" \
    "RateLimiting__LoginWindowSeconds=60" \
    "RateLimiting__LoginQueueLimit=0" \
    "Audit__EnableAuditLogs=true" \
    "ErrorHandling__EnableGlobalErrorHandler=true" \
    "Diagnostics__EnableErrorTestEndpoint=true" \
    "Https__EnableHttpsRedirection=true" \
    "Https__EnableHsts=false" \
    "Https__HstsMaxAgeDays=30"
```

**Variables sensibles — configurar POR SEPARADO (no en scripts compartidos):**

```bash
# Jwt__Key — generar localmente: openssl rand -base64 48
az webapp config appsettings set \
  --name xpay-api-qa \
  --resource-group rg-xpay-qa \
  --settings "Jwt__Key=<CLAVE_GENERADA>"

# CORS — usar URL exacta del frontend QA (después de crearlo)
az webapp config appsettings set \
  --name xpay-api-qa \
  --resource-group rg-xpay-qa \
  --settings "Cors__AllowedOrigins__0=<URL_FRONTEND_QA>"
```

**Connection String — configurar como Connection String (no App Setting):**

```bash
az webapp config connection-string set \
  --name xpay-api-qa \
  --resource-group rg-xpay-qa \
  --connection-string-type SQLAzure \
  --settings "XpayConnection=Server=sql-xpay-qa.database.windows.net;Database=sqldb-xpay-qa;User Id=xpay_admin;Password=<SA_PASSWORD>;Encrypt=True;TrustServerCertificate=False;"
```

### Paso 8 — Build y deploy backend

```bash
# Desde la raíz del repo
bash scripts/build-backend-qa.sh
# Genera artifacts/backend-qa/

cd artifacts/backend-qa
zip -r ../../xpay-api-qa.zip .
cd ../..

az webapp deploy \
  --name xpay-api-qa \
  --resource-group rg-xpay-qa \
  --src-path xpay-api-qa.zip \
  --type zip
```

### Paso 9 — Frontend: Static Web Apps (opción recomendada)

```bash
# Crear Static Web App
az staticwebapp create \
  --name swa-xpay-admin-qa \
  --resource-group rg-xpay-qa \
  --location "eastus2"     # SWA disponible en regiones específicas

# Build del frontend con URL del backend QA
cp frontend/xpay-admin/.env.qa.example frontend/xpay-admin/.env
# Editar .env → VITE_API_BASE_URL=https://xpay-api-qa.azurewebsites.net

cd frontend/xpay-admin
npm ci
npm run build
cd ../..

# Obtener deployment token (no versionar)
SWA_TOKEN=$(az staticwebapp secrets list \
  --name swa-xpay-admin-qa \
  --resource-group rg-xpay-qa \
  --query "properties.apiKey" -o tsv)

# Deploy con SWA CLI (instalar: npm install -g @azure/static-web-apps-cli)
swa deploy frontend/xpay-admin/dist \
  --deployment-token "$SWA_TOKEN" \
  --env production
```

**Alternativa — App Service estático:**

```bash
az webapp create \
  --name xpay-admin-qa \
  --resource-group rg-xpay-qa \
  --plan asp-xpay-api-qa \
  --runtime "NODE:20-lts"

# Build + zip deploy de dist/
cd frontend/xpay-admin/dist
zip -r ../../../xpay-admin-qa.zip .
cd ../../..

az webapp deploy \
  --name xpay-admin-qa \
  --resource-group rg-xpay-qa \
  --src-path xpay-admin-qa.zip \
  --type zip
```

> Si se usa App Service para el frontend, la URL será `https://xpay-admin-qa.azurewebsites.net`. Actualizar `Cors__AllowedOrigins__0` en el backend con esa URL exacta.

---

## 6. Base de datos QA — ejecución de scripts

> **Crítico:** ejecutar SOLO contra `sqldb-xpay-qa`. Nunca contra la BD de desarrollo local de otro miembro del equipo ni contra cualquier BD de producción.

### Verificar conexión antes de ejecutar

```bash
# Usando sqlcmd (instalar: https://learn.microsoft.com/en-us/sql/tools/sqlcmd-utility)
sqlcmd -S sql-xpay-qa.database.windows.net \
  -d sqldb-xpay-qa \
  -U xpay_admin \
  -P "<SA_PASSWORD>" \
  -Q "SELECT @@VERSION"
```

### Orden obligatorio de scripts

| Orden | Script | Contenido |
|-------|--------|-----------|
| 1 | `database/001_security_identity.sql` | Tablas de identidad, personas, usuarios, autenticación |
| 2 | `database/002_wallet_ledger.sql` | Wallets, ledger contable, cuentas |
| 3 | `database/003_comercios_qr.sql` | Comercios, códigos QR, ventas QR |
| 4 | `database/004_liquidacion_qr.sql` | Liquidación de ventas QR a comercios |
| 5 | `database/005_retiros_comercio.sql` | Retiros de comercios |
| 6 | `database/006_gestion_retiros_comercio.sql` | Gestión y estados de retiros |
| 7 | `database/007_security_roles_jwt.sql` | Roles y permisos JWT |
| 8 | `database/008_seed_qa_dataset.sql` | Dataset QA: personas, usuarios demo, wallets, comercio demo |

```bash
# Ejecutar cada script en orden (ajustar credenciales)
for script in 001 002 003 004 005 006 007 008; do
  echo "==> Ejecutando ${script}..."
  sqlcmd -S sql-xpay-qa.database.windows.net \
    -d sqldb-xpay-qa \
    -U xpay_admin \
    -P "<SA_PASSWORD>" \
    -i "database/${script}_*.sql" \
    -C   # Trust server certificate (Azure SQL)
  echo "    OK: ${script}"
done
```

> Si algún script falla: **detenerse inmediatamente**. No ejecutar el siguiente. Revisar el error, corregir el entorno (no el script SQL) y reintentar desde el script que falló.

### Verificación post-migración

```sql
-- Verificar tablas clave
SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_NAME;

-- Verificar usuarios QA del seed (script 008)
SELECT usuario, email FROM usuarios WHERE usuario LIKE 'qa.%';
-- Esperado: qa.admin.xpay, qa.operador.xpay, qa.usuario1, qa.usuario2

-- Verificar comercio demo
SELECT nombre_comercio FROM comercios;
-- Esperado: "Comercio Demo XPAY QA"

-- Verificar wallets QA
SELECT w.id_wallet, p.nombre_completo, w.saldo
FROM wallets w JOIN personas p ON w.id_persona = p.id_persona;
```

---

## 7. Datos demo para socios

**Reglas de datos ficticios obligatorias:**

| Campo | Regla | Ejemplo válido | Ejemplo PROHIBIDO |
|-------|-------|---------------|-----------------|
| Nombres | Ficticios o seudónimos | "Ana Demo", "Carlos Test" | Nombres reales de personas |
| Cédulas/CC | Números fuera de rangos válidos reales | CC 900000001–900000004 *(seed 008)* | Cédulas reales de personas |
| Correos | Solo dominio `@xpay.test` o `@demo.xpay` | `qa.admin@xpay.test` | Correos personales reales |
| Números bancarios | No usar | N/A | Cuentas bancarias reales |
| Montos | Montos ficticios en pesos ficticios | $50.000, $100.000 | Montos de transacciones reales |
| Teléfonos | Ficticios o de prueba | `3000000001` | Celulares reales |
| Direcciones | Ficticias | "Calle 1 # 2-3 Demo" | Direcciones reales |

**Usuarios disponibles después de script 008:**

| Usuario | Rol | Propósito demo |
|---------|-----|----------------|
| `qa.admin.xpay` | Admin | Gestión de wallets, comercios, reportes |
| `qa.operador.xpay` | Operador | Operaciones financieras QA |
| `qa.usuario1` | Usuario final | Wallet de persona demo 1 |
| `qa.usuario2` | Usuario final | Wallet de persona demo 2 |

> Las contraseñas de estos usuarios están en el script 008 como hashes BCrypt de placeholders. Antes de la demo, actualizar las contraseñas a valores conocidos y seguros mediante el endpoint de cambio de contraseña o directamente en BD QA.

---

## 8. Smoke test post-deploy

Ejecutar en orden. Todos deben pasar antes de la demo.

### 8.1 Probes de disponibilidad

```bash
BASE="https://xpay-api-qa.azurewebsites.net"

curl -sf "$BASE/health"                     && echo "✓ /health OK"
curl -sf "$BASE/api/diagnostics/ping"       | jq '.status' && echo "✓ ping OK"
curl -sf "$BASE/api/diagnostics/ready"      | jq '.status' && echo "✓ ready OK"
curl -sf "$BASE/api/version"                | jq '.data.version' && echo "✓ version OK"
```

### 8.2 Swagger QA

Abrir en navegador: `https://xpay-api-qa.azurewebsites.net/swagger`  
Verificar: UI carga · Botón **Authorize** visible · Listado de endpoints.

### 8.3 Autenticación

```bash
# Login con usuario demo
TOKEN=$(curl -sf -X POST "$BASE/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"username":"qa.admin.xpay","password":"<password>"}' \
  | jq -r '.data.token // empty')
echo "Token: ${TOKEN:0:30}..."  # mostrar solo prefijo

# Endpoint protegido sin token → 401
HTTP=$(curl -s -o /dev/null -w "%{http_code}" "$BASE/api/admin/wallets")
echo "Sin token → $HTTP"  # debe ser 401
```

### 8.4 CORS

```bash
FRONTEND_URL="https://swa-xpay-admin-qa.azurestaticapps.net"  # ajustar según deploy

curl -sI -X OPTIONS "$BASE/api/auth/login" \
  -H "Origin: $FRONTEND_URL" \
  -H "Access-Control-Request-Method: POST" \
  | grep -i "access-control-allow-origin:" || echo "⚠ CORS header ausente"
```

### 8.5 Checklist manual

- [ ] `GET /health` → HTTP 200 `{"status":"Healthy"}`
- [ ] `GET /api/diagnostics/ping` → HTTP 200, `correlationId` presente
- [ ] `GET /api/diagnostics/ready` → HTTP 200, `status=READY`
- [ ] `GET /api/version` → HTTP 200, versión `0.1.0-mvp-qa`
- [ ] Swagger UI carga en `/swagger`
- [ ] Login `qa.admin.xpay` devuelve token JWT
- [ ] Endpoint protegido sin token → HTTP 401
- [ ] CORS desde URL del frontend QA → `Access-Control-Allow-Origin` presente
- [ ] CORS desde URL desconocida → sin `Access-Control-Allow-Origin`
- [ ] `GET /api/admin/wallets` con token → HTTP 200 con lista de wallets
- [ ] `GET /api/admin/comercios` → HTTP 200 con comercio demo
- [ ] Dashboard frontend carga en URL del Static Web App / App Service
- [ ] Login en frontend → redirección al dashboard
- [ ] Wallets en frontend → muestra wallets del seed 008
- [ ] QR payment → flujo completo con datos ficticios
- [ ] Merchant settlement → flujo completo
- [ ] Withdrawal request → flujo completo
- [ ] Ledger/reportes → muestra entradas contables
- [ ] `bash scripts/scan-dependencies-security.sh` → exit 0
- [ ] GitHub Actions `Backend Validation` → success para el commit desplegado
- [ ] GitHub Actions `Frontend Build` → success
- [ ] GitHub Actions `Dependency Security Scan` → success

---

## 9. Riesgos y bloqueantes para la demo

| Bloqueante | Causa probable | Mitigación |
|-----------|----------------|-----------|
| Backend no despliega | Error de build o config faltante | Revisar `az webapp log tail --name xpay-api-qa --resource-group rg-xpay-qa` |
| Frontend no conecta por CORS | `Cors__AllowedOrigins__0` incorrecto o falta el reinicio | Verificar URL exacta, reiniciar App Service, esperar 30 s |
| DB no migrada | Script fallido o firewall bloqueando | Verificar conectividad, ejecutar script fallido nuevamente |
| Login no funciona | `Jwt__Key` faltante o < 32 chars, o usuarios sin contraseña actualizada | Verificar App Settings, regenerar Jwt__Key |
| Ledger inconsistente | Seed 008 no ejecutado o ejecutado parcialmente | Verificar tablas con queries de verificación (sección 6) |
| GitHub Actions fallan | Código roto o dependencias vulnerables | No desplegar hasta CI verde |
| Dependency scan falla | Vulnerabilidad nueva detectada | Ejecutar `bash scripts/scan-dependencies-security.sh`, revisar hallazgos |
| Datos reales detectados | Seed con datos reales por error | Reemplazar con datos ficticios, no continuar demo |
| URL frontend incorrecta | `VITE_API_BASE_URL` apuntando a localhost | Reconstruir frontend con `.env` correcto y redesplegar |

---

## 10. Checklist de salida a demo

Completar todos antes de mostrar el sistema a socios.

| Ítem | Estado | Responsable |
|------|--------|------------|
| Backend QA URL activa: `https://xpay-api-qa.azurewebsites.net/health` → 200 | `[ ]` | Responsable técnico |
| Frontend QA URL activa (UI carga sin errores de consola) | `[ ]` | Responsable técnico |
| DB QA con seed 008 ejecutado y verificado | `[ ]` | Responsable técnico |
| Usuario demo `qa.admin.xpay` con contraseña conocida y funcional | `[ ]` | Responsable técnico |
| Smoke test completo de sección 8 aprobado | `[ ]` | QA Lead |
| GitHub Actions (3 workflows) en success para el commit desplegado | `[ ]` | Responsable técnico |
| Dependency Security Scan → exit 0 | `[ ]` | Security Lead |
| Datos confirman ser ficticios (sin cédulas/correos/montos reales) | `[ ]` | QA Lead + Responsable técnico |
| NO hay dinero real involucrado | `[ ]` | Responsable técnico + Responsable financiero |
| NO hay clientes reales en el sistema | `[ ]` | Responsable técnico |
| Responsable técnico disponible durante la demo | `[ ]` | Responsable técnico |
| Guion de demo preparado y ensayado | `[ ]` | QA Lead / Responsable de socios |
| Rollback owner identificado y disponible | `[ ]` | Responsable técnico |
| Snapshot de App Settings exportado como evidencia | `[ ]` | Responsable técnico |
| `docs/ROLLBACK_AND_RECOVERY_RUNBOOK.md` revisado por el responsable técnico | `[ ]` | Responsable técnico |

---

## 11. Application Insights (opcional)

Para habilitar telemetría básica en QA:

```bash
# Crear recurso
az monitor app-insights component create \
  --app ai-xpay-qa \
  --location eastus \
  --resource-group rg-xpay-qa \
  --kind web

# Obtener connection string
AI_CONN=$(az monitor app-insights component show \
  --app ai-xpay-qa \
  --resource-group rg-xpay-qa \
  --query connectionString -o tsv)

# Configurar en App Service
az webapp config appsettings set \
  --name xpay-api-qa \
  --resource-group rg-xpay-qa \
  --settings "APPLICATIONINSIGHTS_CONNECTION_STRING=$AI_CONN"
```

> Integración SDK de Application Insights en el backend (.NET) requiere agregar el paquete `Microsoft.ApplicationInsights.AspNetCore` — evaluar en una fase posterior para no introducir dependencias no aprobadas.

---

## 12. Qué falta para producción real

Este ambiente QA cubre la demo pero NO habilita producción con dinero real. Ver `docs/PREPRODUCTION_GAPS_AND_REAL_MONEY_CHECKLIST.md` para la lista completa. Ítems críticos pendientes:

| Pendiente | Brecha |
|-----------|--------|
| Azure Key Vault para secretos (Jwt__Key, Connection String) | T9 |
| Ambiente de preproducción separado del QA | T8 |
| Backup automático de Azure SQL configurado y probado | T4 |
| Pruebas de carga básicas | T10 |
| Monitoreo real con Azure Monitor / App Insights configurado | T5, T6 |
| HSTS activado con certificado TLS validado | S9 |
| WAF / Azure Front Door | S8 |
| Rollback probado en Azure QA real | T7 |

---

*Documento creado en Fase 49. Actualizar al crear los recursos Azure reales, al cambiar la URL del frontend o al modificar la estructura de configuración.*
