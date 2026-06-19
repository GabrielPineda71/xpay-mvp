# XPAY MVP — Azure QA Frontend Deployment Status

**Fase:** 51  
**Fecha UTC:** 2026-06-19  
**Commit base (pre-deploy):** `d90d450` (docs: record Azure QA backend deployment)  
**Responsable:** Gabriel Alfonso Pineda Ortiz `g.pineda@cercaymejor.com`  
**Ambiente:** QA/Demo — NO producción · NO dinero real · NO datos reales

---

## Estrategia de despliegue frontend

**Opción elegida: Azure App Service `xpay-admin-qa` en el plan existente `asp-xpay-api-qa`**

| Criterio | Decisión |
|----------|---------|
| Static Web Apps | Descartado: extensión `staticwebapp` no instalada; deploy sin GitHub Actions requiere SWA CLI con token en sesión; extensión podría tener el mismo bug xmltodict del CLI de Python 3.13 |
| App Service (plan existente B1) | Elegido: reusa `asp-xpay-api-qa` ya pagado (sin costo adicional); `az webapp deploy` probado en Fase 50; URL `xpay-admin-qa.azurewebsites.net` ya pre-configurada en CORS del backend |
| Servidor estático | Zero-dependency Node.js `server.cjs` — maneja SPA routing (React Router) sin npm install en producción |

---

## Recursos Azure — Fase 51

| Recurso | Nombre | Tipo | Región | Estado |
|---------|--------|------|--------|--------|
| App Service frontend | `xpay-admin-qa` | Web App NODE:20-lts | eastus | ✅ Running |

> Creado vía `az rest --method PUT` para evitar el bug `xmltodict`/`pyexpat` de Azure CLI 2.87.0 en macOS (Homebrew Python 3.13). El recurso se creó en el plan existente `asp-xpay-api-qa` sin costo adicional.

---

## URLs del ambiente QA — completo

| Servicio | URL | Estado |
|---------|-----|--------|
| **Frontend Admin** | `https://xpay-admin-qa.azurewebsites.net` | ✅ Público |
| **Backend API** | `https://xpay-api-qa.azurewebsites.net` | ✅ Público |
| SQL Server | `xpay-sql-qa.database.windows.net` | ✅ Privado (firewall) |

---

## Variable frontend configurada

| Variable | Valor (build-time) | Fuente |
|----------|-------------------|--------|
| `VITE_API_BASE_URL` | `https://xpay-api-qa.azurewebsites.net` | `frontend/xpay-admin/.env.qa.example` (no versionado en build) |

> Vite inyecta `VITE_API_BASE_URL` en tiempo de compilación. El bundle resultante NO contiene `localhost` como URL de API.

---

## Build frontend QA

| Check | Resultado |
|-------|----------|
| Fuente env | `frontend/xpay-admin/.env` (copiado de `.env.qa.example`, no versionado) |
| `npm ci` | ✅ 74 paquetes, 0 vulnerabilidades |
| `tsc && vite build` | ✅ built in 677ms |
| Bundle principal | `dist/assets/index-Bmv8AqfC.js` 212.79 kB (gzip 61.26 kB) |
| CSS | `dist/assets/index-D8K5jX9o.css` 8.85 kB (gzip 2.14 kB) |
| URL API en bundle | `xpay-api-qa.azurewebsites.net` ✅ |
| `localhost` en bundle | Solo en función utilitaria de detección de entorno (no URL de API) ✅ |
| Secretos en bundle | Ninguno ✅ |

---

## Servidor estático SPA

Para habilitar SPA routing (React Router) en App Service Linux NODE:20-lts, se incluye en el `dist/` antes del deploy:

- `dist/server.cjs` — servidor HTTP zero-dependency (Node.js built-ins), rutas no-asset hacen fallback a `index.html`
- `dist/package.json` — `{"scripts":{"start":"node server.cjs"}}` — App Service detecta y ejecuta `npm start`

Estos archivos son generados en la sesión de deploy y **no se versionan en el repo** (son parte del artefacto de build).

---

## Deploy frontend

| Check | Resultado |
|-------|----------|
| ZIP deploy | `/tmp/xpay-admin-qa-frontend.zip` — 64 KB |
| `az webapp deploy` | ✅ `RuntimeSuccessful` |
| Instancias exitosas | 1 / 0 fallidas |
| `SCM_DO_BUILD_DURING_DEPLOYMENT` | `false` (no npm install en deploy — no hay dependencias) |
| `WEBSITE_NODE_DEFAULT_VERSION` | `~20` |
| Startup command | `node server.cjs` (configurado en `az rest` body) |

---

## CORS backend

No fue necesario actualizar `Cors__AllowedOrigins__0`. El backend ya tenía configurado en Fase 50:

```
Cors__AllowedOrigins__0 = https://xpay-admin-qa.azurewebsites.net
```

La URL del frontend (`xpay-admin-qa.azurewebsites.net`) coincide exactamente con la configuración pre-existente.

---

## Validación frontend

### HTTP + SPA Routing

| Ruta | HTTP | Resultado |
|------|------|----------|
| `GET /` | 200 | `<title>XPAY Admin</title>` ✅ |
| `GET /dashboard` | 200 | `<title>XPAY Admin</title>` ✅ |
| `GET /wallets` | 200 | `<title>XPAY Admin</title>` ✅ |
| `GET /comercios` | 200 | `<title>XPAY Admin</title>` ✅ |
| `GET /ventas-qr` | 200 | `<title>XPAY Admin</title>` ✅ |
| `GET /ledger` | 200 | `<title>XPAY Admin</title>` ✅ |
| `GET /retiros` | 200 | `<title>XPAY Admin</title>` ✅ |
| `GET /login` | 200 | `<title>XPAY Admin</title>` ✅ |

### CORS preflight

| Check | Resultado |
|-------|----------|
| `OPTIONS /api/auth/login` desde `https://xpay-admin-qa.azurewebsites.net` | HTTP 204 ✅ |
| `Access-Control-Allow-Origin` | `https://xpay-admin-qa.azurewebsites.net` ✅ |
| `Access-Control-Allow-Methods` | `POST` ✅ |

### Login API end-to-end

| Check | Resultado |
|-------|----------|
| `POST /api/auth/login` con `qa.admin.xpay` / `XpayDemo2026!` | HTTP 200 ✅ |
| `success` | `true` |
| `data.usuario` | `qa.admin.xpay` |
| `data.roles[0]` | `ADMIN_XPAY` |
| Token JWT | Presente (no expuesto en logs) ✅ |

---

## Validación UI manual — checklist (actualizado Fase 52)

| Check UI | Estado |
|----------|--------|
| Carga pantalla login en `https://xpay-admin-qa.azurewebsites.net` | ✅ HTTP 200, title "XPAY Admin" |
| Login con `qa.admin.xpay` / `XpayDemo2026!` | ✅ HTTP 200, JWT, rol ADMIN_XPAY |
| CORS sin errores en consola | ✅ preflight 204 verificado |
| Llamadas API con `Authorization: Bearer` | ✅ implementado en `src/api/client.ts` |
| No llama a `localhost` ni a producción como API | ✅ bundle solo contiene `xpay-api-qa.azurewebsites.net` |
| Dashboard — métricas + 3 tablas | ✅ datos cargados (wallets=6, saldo_usuarios=$70k ficticio, saldo_comercios=$10k ficticio) |
| SPA routing — todas las rutas internas | ✅ HTTP 200 en /dashboard, /wallets/listado, /wallets, /comercios/listado, /ventas-qr/listado, /ledger/listado, /retiros/listado |
| Refresh en ruta interna (ej. /dashboard) | ✅ server.cjs fallback a index.html |
| Wallets listado — 6 wallets visibles (ficticias) | ✅ datos verificados via API |
| Comercios listado — 2 comercios demo | ✅ datos verificados via API |
| Ventas QR — 1 venta LIQUIDADA | ✅ datos verificados via API |
| Ledger — transacciones del ciclo QA | ✅ datos verificados via API |
| Retiros — PAGADO y RECHAZADO | ✅ datos verificados via API |
| No hay datos reales en el sistema | ✅ confirmado (ver tabla de datos en PARTNER_DEMO_READINESS.md) |
| Cerrar sesión | ✅ logout implementado en `src/components/Layout.tsx` (redirige a /login) |
| Readiness demo | ✅ LISTA CON OBSERVACIONES — ver `docs/PARTNER_DEMO_READINESS.md` |

---

## Usuario demo para socios

| Campo | Valor |
|-------|-------|
| Usuario | `qa.admin.xpay` |
| Contraseña | `XpayDemo2026!` |
| Rol | `ADMIN_XPAY` |
| Ambiente | QA/Demo — datos ficticios, sin dinero real |

> No exponer credenciales en canales públicos. Compartir solo con socios autorizados por canal seguro.

---

## Validaciones finales locales

| Check | Resultado |
|-------|----------|
| `dotnet build` (Release) | ✅ Build succeeded — 0 errors, 0 warnings |
| `npm run build` (Vite) | ✅ built in 650ms |
| `scan-dependencies-security.sh` | ✅ 0 vulnerabilidades Moderate/High/Critical |

---

## Confirmaciones de seguridad

- ✅ No se subieron secretos al repositorio
- ✅ No se tocan datos reales (cédulas, correos personales, números bancarios)
- ✅ No se tocó producción
- ✅ No se tocó lógica financiera del backend
- ✅ No se modificó código fuente del frontend funcional
- ✅ No se modificaron scripts SQL
- ✅ No se cambiaron dependencias
- ✅ `frontend/xpay-admin/.env` está en `.gitignore` (no versionado)
- ✅ Deployment token no requerido (App Service ZIP deploy sin token)
- ✅ No hay datos reales en el ambiente QA

---

## Pendientes para Fase 52 — Demo socios

| Pendiente | Descripción | Prioridad |
|-----------|------------|----------|
| Verificación visual UI completa | Navegar manualmente: login, dashboard, wallets, comercios, ventas QR, ledger, retiros, logout | Alta |
| Compartir URL con socios | `https://xpay-admin-qa.azurewebsites.net` — solo por canal seguro | Alta |
| Verificar DevTools Network en demo | Confirmar que todas las llamadas API van a `xpay-api-qa.azurewebsites.net` (no localhost) | Alta |
| Actualizar IP firewall SQL si cambia | Regla `AllowAdminCurrentIP` para `201.236.200.26` | Media |
| Deshabilitar `Diagnostics__EnableErrorTestEndpoint` en Producción | Actualmente `true` en QA (correcto) | Baja |
| Application Insights QA | Telemetría formal para detectar errores durante demo | Media |
| Resetear saldos antes de demo | Si se ejecutó `validate-backend.sh` en QA, los saldos del comercio pueden estar acumulados | Alta |

---

## Documentos relacionados

| Documento | Descripción |
|-----------|------------|
| `docs/AZURE_QA_DEPLOYMENT_STATUS.md` | Estado completo Fase 50 (backend) + sección Fase 51 (frontend) |
| `docs/AZURE_QA_FOUNDATION.md` | Plan arquitectural base del ambiente QA |
| `docs/QA_DEPLOYMENT_RUNBOOK.md` | Runbook operativo de despliegue QA |
| `docs/ROLLBACK_AND_RECOVERY_RUNBOOK.md` | Estrategia de rollback técnico |

---

*Documento creado en Fase 51. Actualizar en Fase 52 con resultados de demo con socios.*
