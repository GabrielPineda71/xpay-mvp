# XPAY MVP — Guía de Despliegue QA en Azure

Esta guía describe cómo desplegar el ambiente QA del sistema XPAY en Azure App Service, sin incluir secretos reales.

---

## 1. Arquitectura QA propuesta

```
┌─────────────────────────────────────────────────────────────────────┐
│                          Azure (QA)                                 │
│                                                                     │
│  ┌─────────────────────┐        ┌──────────────────────────────┐   │
│  │  Azure App Service  │        │  Azure App Service /         │   │
│  │  xpay-api-qa        │◄──────►│  Static Web App              │   │
│  │  (.NET 8 backend)   │  CORS  │  xpay-admin-qa               │   │
│  └────────┬────────────┘        │  (React/Vite frontend)       │   │
│           │                     └──────────────────────────────┘   │
│           │ EF Core / ADO.NET                                       │
│  ┌────────▼────────────┐                                            │
│  │  Azure SQL Database │                                            │
│  │  XPAY_MVP_QA        │                                            │
│  │  (xpay-sql-qa)      │                                            │
│  └─────────────────────┘                                            │
│                                                                     │
│  GitHub Actions ──► Backend Validation (en cada push a main)       │
│                 └──► Frontend Build (en cada push a main)           │
└─────────────────────────────────────────────────────────────────────┘
```

**Flujo de autenticación:**  
Usuario (navegador) → Frontend QA → `POST /api/auth/login` → Backend QA → JWT → Frontend almacena en localStorage → Requests subsecuentes con `Authorization: Bearer {token}`

---

## 2. Recursos Azure sugeridos

| Recurso | Nombre sugerido | Tier mínimo QA |
|---|---|---|
| Resource Group | `rg-xpay-qa` | — |
| SQL Server | `xpay-sql-qa` | — |
| SQL Database | `XPAY_MVP_QA` | Basic (5 DTU) |
| App Service Plan | `plan-xpay-qa` | B1 (1 core, 1.75 GB) |
| Backend App Service | `xpay-api-qa` | en plan B1 |
| Frontend App Service / SWA | `xpay-admin-qa` | Free tier SWA |

URL backend QA: `https://xpay-api-qa.azurewebsites.net`  
URL frontend QA: `https://xpay-admin-qa.azurewebsites.net`

---

## 3. Variables de entorno — Backend (Azure App Settings)

Configurar en **Azure Portal → App Service `xpay-api-qa` → Configuration → Application settings**.

.NET lee variables de entorno con `__` como separador de jerarquía de configuración.

| Variable de entorno | Descripción | Ejemplo |
|---|---|---|
| `ConnectionStrings__XpayConnection` | Cadena de conexión Azure SQL | `Server=xpay-sql-qa.database.windows.net;Database=XPAY_MVP_QA;User Id=xpay_app;Password=<secret>;Encrypt=True;TrustServerCertificate=False;` |
| `Jwt__Key` | Llave secreta HS256 — mínimo 32 caracteres | `<secret-min-32-chars>` |
| `Jwt__Issuer` | Emisor del JWT | `Xpay.Api.QA` |
| `Jwt__Audience` | Audiencia del JWT | `Xpay.Admin.QA` |
| `Jwt__ExpirationHours` | Horas de validez del token | `8` |
| `Api__Name` | Nombre de la API (aparece en `/api/version`) | `XPAY API QA` |
| `Api__Version` | Versión (aparece en `/api/version`) | `0.1.0-mvp-qa` |
| `Cors__AllowedOrigins__0` | Origen permitido para CORS | `https://xpay-admin-qa.azurewebsites.net` |

> **Nunca** guardar estos valores en el repositorio. Usar siempre Azure App Settings o Key Vault.

Archivo de referencia (sin secretos reales): [`backend/Xpay.Api/appsettings.QA.example.json`](../backend/Xpay.Api/appsettings.QA.example.json)

---

## 4. Variables de entorno — Frontend

El frontend es una SPA estática compilada con Vite. `VITE_API_BASE_URL` se inyecta en tiempo de build.

| Variable | Descripción | Valor QA |
|---|---|---|
| `VITE_API_BASE_URL` | URL base del backend | `https://xpay-api-qa.azurewebsites.net` |

**Para build QA:**
```bash
cd frontend/xpay-admin
cp .env.qa.example .env
# Ajustar VITE_API_BASE_URL si el nombre del App Service difiere
npm run build
# El output queda en dist/ — desplegarlo en Azure SWA o App Service
```

Archivo de referencia: [`frontend/xpay-admin/.env.qa.example`](../frontend/xpay-admin/.env.qa.example)

Si se usa **Azure Static Web Apps**, definir `VITE_API_BASE_URL` en la sección **Configuration → Application settings** del SWA y configurar el workflow de build para que lo use.

---

## 5. Orden recomendado de despliegue

```
1.  Crear Resource Group rg-xpay-qa en Azure
2.  Crear Azure SQL Server xpay-sql-qa y base de datos XPAY_MVP_QA
3.  Ejecutar migraciones SQL en orden:
      database/001_security_identity.sql
      database/002_wallet_ledger.sql
      database/003_comercios_qr.sql
      database/004_liquidacion_qr.sql
      database/005_retiros_comercio.sql
      database/006_gestion_retiros_comercio.sql
      database/007_security_roles_jwt.sql
4.  Crear App Service Plan plan-xpay-qa (B1)
5.  Crear App Service xpay-api-qa (.NET 8)
6.  Configurar variables de entorno backend (ver sección 3)
7.  Desplegar backend (zip deploy, GitHub Actions deploy workflow o VS Publish)
8.  Verificar: GET https://xpay-api-qa.azurewebsites.net/health
9.  Verificar: GET https://xpay-api-qa.azurewebsites.net/api/version
10. Verificar: GET https://xpay-api-qa.azurewebsites.net/swagger
11. Crear Static Web App xpay-admin-qa (o App Service)
12. Construir frontend con VITE_API_BASE_URL apuntando al backend QA
13. Desplegar archivos dist/ en xpay-admin-qa
14. Verificar: login en https://xpay-admin-qa.azurewebsites.net
15. Verificar dashboard, listados y navegación
```

---

## 6. Checklist de verificación QA

| # | Verificación | Cómo validar |
|---|---|---|
| 1 | `/health` responde `Healthy` | `curl https://xpay-api-qa.azurewebsites.net/health` |
| 2 | `/api/version` muestra ambiente QA | `curl https://xpay-api-qa.azurewebsites.net/api/version` — verificar `name: "XPAY API QA"` |
| 3 | Swagger abre sin errores | Navegar a `https://xpay-api-qa.azurewebsites.net/swagger` |
| 4 | Login devuelve JWT | `POST /api/auth/login` con usuario demo — debe retornar `success: true` y `data.token` |
| 5 | Endpoint protegido sin token devuelve 401 | `curl https://xpay-api-qa.azurewebsites.net/api/admin/wallets` sin header Authorization |
| 6 | Endpoint protegido con token funciona | Mismo endpoint con `Authorization: Bearer {token}` — debe retornar 200 |
| 7 | CORS permite frontend QA | Preflight `OPTIONS` desde `https://xpay-admin-qa.azurewebsites.net` — debe retornar `Access-Control-Allow-Origin` correcto |
| 8 | Dashboard carga métricas | Iniciar sesión en frontend QA — el dashboard debe mostrar tarjetas con datos |
| 9 | Listados cargan | `/wallets/listado`, `/comercios/listado`, `/retiros/listado`, `/ventas-qr/listado`, `/ledger/listado` |
| 10 | Retiros pueden consultarse | `/retiros` → buscar por ID existente |

---

## 7. Troubleshooting

### Error CORS
**Síntoma:** El frontend recibe `Access to XMLHttpRequest blocked by CORS policy`.  
**Causa probable:** `Cors__AllowedOrigins__0` no coincide exactamente con la URL del frontend (sin barra final, con `https://`).  
**Solución:** Verificar el valor en Azure App Settings. El origen debe ser `https://xpay-admin-qa.azurewebsites.net` (sin `/` al final).

---

### Error 401 en todos los endpoints protegidos
**Síntoma:** Todos los requests devuelven 401 incluso con token.  
**Causa probable:** `Jwt__Key`, `Jwt__Issuer` o `Jwt__Audience` en el backend QA no coinciden con los usados para generar el token.  
**Solución:** Verificar que las App Settings del backend QA coincidan con los valores usados en login. Reiniciar el App Service tras cambiar variables.

---

### Error de conexión SQL (`Cannot open server`)
**Síntoma:** El backend arranca pero responde 500 en cualquier endpoint que toque la base de datos.  
**Causa probable:** `ConnectionStrings__XpayConnection` incorrecta, firewall de Azure SQL no permite la IP del App Service, o las migraciones no se ejecutaron.  
**Solución:**  
1. Agregar la IP del App Service (o rango del servicio) en las reglas de firewall de Azure SQL.  
2. Verificar que la connection string tiene `Encrypt=True;TrustServerCertificate=False;` para Azure SQL.  
3. Confirmar que las 7 migraciones fueron ejecutadas en `XPAY_MVP_QA`.

---

### Error JWT key demasiado corta
**Síntoma:** El backend no inicia o lanza `IDX10653: The encryption algorithm 'HS256' requires the SecurityKey...`.  
**Causa:** `Jwt__Key` tiene menos de 32 caracteres.  
**Solución:** Usar una clave de al menos 32 caracteres en `Jwt__Key`. Generar con:
```bash
openssl rand -base64 48 | tr -dc 'A-Za-z0-9!@#$%' | head -c 48
```

---

### Error frontend apunta al backend incorrecto
**Síntoma:** El frontend QA hace requests a `http://localhost:5000` en lugar de al backend QA.  
**Causa:** El build se generó sin definir `VITE_API_BASE_URL` o con el valor de desarrollo.  
**Solución:** Verificar que el archivo `.env` usado en build tenía `VITE_API_BASE_URL=https://xpay-api-qa.azurewebsites.net`. Reconstruir el frontend con el valor correcto y redesplegar `dist/`.

---

### Error migraciones no ejecutadas
**Síntoma:** Login falla con error de tabla no encontrada (`Invalid object name 'usuarios'`).  
**Causa:** Las migraciones SQL no fueron aplicadas en `XPAY_MVP_QA`.  
**Solución:** Conectarse a Azure SQL con SQL Server Management Studio o Azure Data Studio y ejecutar los 7 scripts en orden estricto:
```
001_security_identity.sql → 002_wallet_ledger.sql → 003_comercios_qr.sql
→ 004_liquidacion_qr.sql → 005_retiros_comercio.sql
→ 006_gestion_retiros_comercio.sql → 007_security_roles_jwt.sql
```

---

## 8. Nota de seguridad

- **No subir secretos reales** al repositorio. Ni en `appsettings.json`, ni en `.env`, ni en comentarios de código.
- Usar **Azure App Settings** para todas las variables sensibles en QA y producción.
- La connection string incluye `Password=` — configurarla solo en Azure App Settings, nunca en el repo.
- **Rotar `Jwt__Key`** antes de pasar a producción. La clave QA no debe reutilizarse en producción.

---

## Alcance de la versión candidata QA

Para conocer qué funcionalidades incluye esta versión, qué está excluido deliberadamente, los riesgos conocidos y los criterios de aprobación para pasar a usuarios internos, ver:

**[`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md)**
- Si se sospecha compromiso de la clave JWT, rotarla inmediatamente en Azure App Settings y reiniciar el App Service (los tokens existentes quedarán inválidos).
- Considerar Azure Key Vault para manejo de secretos en ambientes productivos.
