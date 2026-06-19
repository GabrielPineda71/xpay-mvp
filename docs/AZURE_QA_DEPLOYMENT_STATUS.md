# XPAY MVP — Azure QA Deployment Status

**Fase:** 50  
**Fecha UTC:** 2026-06-19  
**Commit desplegado:** `322c992` (docs: add Azure QA foundation plan)  
**Responsable:** Gabriel Alfonso Pineda Ortiz `g.pineda@cercaymejor.com`  
**Ambiente:** QA/Demo — NO producción · NO dinero real · NO datos reales

---

## Recursos Azure creados

| Recurso | Nombre real | Tipo | Región | Estado |
|---------|-------------|------|--------|--------|
| Resource Group | `rg-xpay-qa` | Resource Group | eastus | ✅ Activo |
| SQL Server | `xpay-sql-qa` | Azure SQL Server | **eastus2** ¹ | ✅ Activo |
| SQL Database | `sqldb-xpay-qa` | Azure SQL — Basic | eastus2 | ✅ Online |
| App Service Plan | `asp-xpay-api-qa` | App Service Plan B1 | eastus | ✅ Ready |
| App Service backend | `xpay-api-qa` | Web App .NET 8 | eastus | ✅ Running |
| App Service frontend | `xpay-admin-qa` | Web App NODE:20-lts | eastus | ✅ Running (Fase 51) |

> ¹ SQL Server en `eastus2` (no `eastus`) porque `eastus` presentó restricción de provisioning temporal durante la creación. El Resource Group `rg-xpay-qa` contiene recursos en ambas regiones — esto es válido en Azure y no afecta el funcionamiento.

### Notas de nomenclatura
El nombre del SQL Server es `xpay-sql-qa` (no `sql-xpay-qa` como en el plan original) porque el nombre `sql-xpay-qa` quedó en estado inconsistente por el intento fallido en `eastus`. Se usó `xpay-sql-qa` en `eastus2` que creó exitosamente.

---

## URLs del ambiente QA

| Servicio | URL | Estado |
|---------|-----|--------|
| **Frontend Admin** | `https://xpay-admin-qa.azurewebsites.net` | ✅ Público (Fase 51) |
| **Backend API** | `https://xpay-api-qa.azurewebsites.net` | ✅ Público |
| SQL Server | `xpay-sql-qa.database.windows.net` | ✅ Privado (firewall) |

---

## App Settings configurados (sin secretos)

| Variable | Valor configurado QA | Sensible |
|----------|---------------------|---------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | No |
| `ConnectionStrings__XpayConnection` | `Server=tcp:xpay-sql-qa.database.windows.net,...` | **Sí — en Azure App Settings** |
| `Jwt__Key` | `<64 chars base64>` | **Sí — en Azure App Settings** |
| `Jwt__Issuer` | `Xpay.Api.QA` | No |
| `Jwt__Audience` | `Xpay.Admin.QA` | No |
| `Jwt__ExpirationHours` | `2` | No |
| `Jwt__ClockSkewSeconds` | `60` | No |
| `Api__Name` | `XPAY API QA` | No |
| `Api__Version` | `0.1.0-mvp-qa` | No |
| `Cors__AllowedOrigins__0` | `https://xpay-admin-qa.azurewebsites.net` | No |
| `ApiDocs__EnableSwagger` | `true` | No |
| `SecurityHeaders__EnableSecurityHeaders` | `true` | No |
| `SecurityHeaders__EnableNoStoreCache` | `true` | No |
| `RateLimiting__EnableRateLimiting` | `true` | No |
| `RateLimiting__LoginPermitLimit` | `20` | No |
| `RateLimiting__LoginWindowSeconds` | `60` | No |
| `RateLimiting__LoginQueueLimit` | `0` | No |
| `Audit__EnableAuditLogs` | `true` | No |
| `ErrorHandling__EnableGlobalErrorHandler` | `true` | No |
| `Diagnostics__EnableErrorTestEndpoint` | `true` (QA) | No |
| `Https__EnableHttpsRedirection` | `true` | No |
| `Https__EnableHsts` | `false` (QA inicial) | No |
| `Https__HstsMaxAgeDays` | `30` | No |

> Ningún secreto fue versionado en el repositorio. Las credenciales se generaron localmente en `/tmp/xpay-qa-phase50.env` (modo 600, fuera del repo, sesión local).

---

## Reglas de firewall SQL

| Regla | IP de inicio | IP de fin | Propósito |
|-------|-------------|----------|----------|
| `AllowAdminCurrentIP` | `201.236.200.26` | `201.236.200.26` | Admin temporal |
| `AllowAzureServices` | `0.0.0.0` | `0.0.0.0` | App Service → SQL |

> La regla `AllowAdminCurrentIP` debe actualizarse si la IP del administrador cambia. Se puede eliminar después de las migraciones iniciales.

---

## Scripts de base de datos ejecutados

| Script | Estado | Tablas/datos creados |
|--------|--------|---------------------|
| `001_security_identity.sql` | ✅ OK | personas, usuarios, roles, permisos, etc. |
| `002_wallet_ledger.sql` | ✅ OK | wallets, wallet_saldos, ledger_* |
| `003_comercios_qr.sql` | ✅ OK | comercios, qr_comercios, ventas_qr |
| `004_liquidacion_qr.sql` | ✅ OK | liquidaciones_comercio, liquidacion_comercio_detalle |
| `005_retiros_comercio.sql` | ✅ OK | retiros_comercio |
| `006_gestion_retiros_comercio.sql` | ✅ OK | Gestión de retiros |
| `007_security_roles_jwt.sql` | ✅ OK | Roles, permisos JWT |
| `008_seed_qa_dataset.sql` | ✅ OK | Personas, usuarios, wallets, comercio QA |

**Tablas creadas:** 23  
**Cuentas ledger creadas:** 25  
**Usuarios QA seed:** `qa.admin.xpay`, `qa.operador.xpay`, `qa.usuario1`, `qa.usuario2`  
**Contraseña demo actualizada a hash BCrypt válido** (work factor 11, misma lib BCrypt.Net-Next 4.0.3)

---

## Datos demo (ficticios)

| Campo | Valor seed | ¿Es real? |
|-------|-----------|----------|
| Nombres | "QA Admin XPAY", "QA Operador XPAY", "QA Usuario Uno", "QA Usuario Dos" | No — ficticios |
| Números de documento | 900000001–900000004 (rango NIT ficticio) | No — ficticios |
| Emails | `@xpay.test` (dominio ficticio) | No |
| Comercio | "Comercio Demo XPAY QA" | No — ficticio |
| Saldos | $0 iniciales | No dinero real |

---

## Validación de endpoints (post-deploy)

| Endpoint | HTTP | Resultado |
|----------|------|----------|
| `GET /health` | 200 | `status=Healthy` ✅ |
| `GET /api/diagnostics/ping` | 200 | `status=OK`, `correlationId` presente ✅ |
| `GET /api/diagnostics/ready` | 200 | `status=READY`, `environment=Production` ✅ |
| `GET /api/version` | 200 | `version=0.1.0-mvp-qa`, `environment=Production` ✅ |
| `GET /swagger/index.html` | 200 | Swagger UI disponible ✅ |
| CORS preflight `POST /api/auth/login` | 204 | `Access-Control-Allow-Origin` presente ✅ |
| `POST /api/auth/login` (qa.admin.xpay) | 200 | Token JWT, rol `ADMIN_XPAY` ✅ |
| Endpoint protegido sin token | 401 | 401 Unauthorized ✅ |

---

## validate-backend.sh contra Azure QA

**Resultado:** ✅ FASES 1 a 47 — TODAS PASAN

```
FASE 1:  Registro, login, wallet, recarga          ✅
FASE 2:  Transferencias XPAY a XPAY                ✅
FASE 3:  Pago a comercio por QR                    ✅
FASE 4:  Liquidación de ventas QR al comercio       ✅
FASE 5:  Solicitud de retiro del comercio           ✅
FASE 6:  Gestión de retiros — PAGADO               ✅
FASE 7:  Gestión de retiros — RECHAZADO            ✅
FASE 8:  Listados y reportes                        ✅
...
FASE 41: Error handling global                      ✅
FASE 42: Política JWT                               ✅
FASE 46: HTTPS/HSTS readiness                      ✅
FASE 47: Readiness probe /ready                    ✅
```

**Cambios aplicados al script para compatibilidad Azure QA:**
1. `SQLCMD` auto-detectado (ya no hardcodeado a `/opt/mssql-tools18/bin/sqlcmd`)
2. `DB_USER` configurable via env var (default `sa` para CI, `xpayadmin` para Azure QA)
3. Query de saldo comercio usa `id_wallet = $ID_WALLET_COMERCIO` (no `tipo_wallet = 'COMERCIO'`) para evitar concatenación de múltiples wallets

---

## Herramientas instaladas en sesión (macOS)

| Herramienta | Estado |
|-------------|--------|
| `unixodbc 2.3.14` | Instalado via Homebrew — requerido por `mssql-tools18` / `sqlcmd` |

---

## Build y scan locales

| Check | Resultado |
|-------|----------|
| `dotnet build` (Release) | ✅ 0 errors, 0 warnings |
| `npm run build` (Vite) | ✅ built in 977ms |
| `scan-dependencies-security.sh` | ✅ 0 vulnerabilidades |

---

## GitHub Actions (commit `322c992`)

| Workflow | Estado |
|---------|--------|
| Backend Validation | ✅ success |
| Frontend Build | ✅ success |
| Dependency Security Scan | ✅ success |

> Los workflows de GitHub Actions se ejecutan contra el entorno CI local (SQL Server Docker). El `validate-backend.sh` contra Azure QA se ejecutó manualmente en esta fase.

---

## Confirmaciones de seguridad

- ✅ No se subieron secretos al repositorio
- ✅ No se tocan datos reales (cédulas, correos personales, números bancarios)
- ✅ No se tocó producción
- ✅ No se tocó lógica financiera del backend
- ✅ No se modificó el frontend funcional
- ✅ No se modificaron scripts SQL
- ✅ No se cambiaron dependencias
- ✅ Contraseñas y JWT key generadas con `secrets` Python y `BCrypt.Net-Next`
- ✅ Credenciales almacenadas solo en `/tmp/xpay-qa-phase50.env` (modo 600, fuera del repo)

---

## Fase 51 — Frontend QA desplegado

**Fecha UTC:** 2026-06-19  
**Frontend URL:** `https://xpay-admin-qa.azurewebsites.net`

| Check | Estado |
|-------|--------|
| Recurso `xpay-admin-qa` creado (NODE:20-lts) | ✅ |
| Build frontend con QA env | ✅ 0 errores, 677ms |
| URL API en bundle | ✅ `xpay-api-qa.azurewebsites.net` |
| ZIP deploy (`RuntimeSuccessful`) | ✅ |
| SPA routing (todas las rutas → index.html) | ✅ 200 en /dashboard, /wallets, /comercios, /ventas-qr, /ledger, /retiros |
| CORS preflight `/api/auth/login` desde frontend | ✅ 204, `Access-Control-Allow-Origin` correcto |
| Login `qa.admin.xpay` end-to-end | ✅ HTTP 200, JWT, rol `ADMIN_XPAY` |
| `Cors__AllowedOrigins__0` | ✅ Pre-configurado correctamente desde Fase 50 — sin cambios requeridos |

Ver detalle completo: **[docs/AZURE_QA_FRONTEND_STATUS.md](AZURE_QA_FRONTEND_STATUS.md)**

---

## Pendientes para Fase 52 — Demo socios

| Pendiente | Descripción |
|-----------|------------|
| Verificación visual UI completa en navegador | Login, dashboard, wallets, comercios, ventas QR, ledger, retiros, logout |
| Compartir URL con socios | `https://xpay-admin-qa.azurewebsites.net` — solo por canal seguro |
| Resetear saldos antes de demo | Si se corrió `validate-backend.sh` los saldos del comercio pueden estar acumulados |
| Application Insights QA (opcional) | Telemetría para detectar errores durante demo |

---

## Riesgos / notas operativas

| Riesgo | Mitigación |
|--------|-----------|
| IP del admin cambió | Actualizar regla `AllowAdminCurrentIP` en firewall SQL |
| Saldo comercio acumulado entre corridas de validate-backend | Resetear con UPDATE wallet_saldos antes de cada corrida QA |
| Usuarios CI acumulados en BD | Ejecutar limpieza FK-segura antes de cada corrida (ver procedure documental en este fase) |
| App Service plan B1 — 1 instancia | Suficiente para demo; escalar a B2 si hay múltiples socios simultáneos |
| SQL Database Basic (5 DTU) | Suficiente para demo; escalar a Standard S1 si hay latencia |
| CORS apunta a URL de frontend no desplegado | Actualizar en Fase 51 con URL real del frontend |
| Credenciales en /tmp expiran con la sesión | Guardar en gestor de contraseñas antes de cerrar la sesión |

---

*Documento creado en Fase 50. Actualizar en Fase 51 al desplegar frontend QA.*
