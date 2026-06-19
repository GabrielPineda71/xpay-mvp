# XPAY MVP — Runbook de Rollback Técnico y Recuperación

**Fase:** 48  
**Estado:** Documentado — rollback real en Azure pendiente de validación en ambiente productivo  
**Aplica a:** QA · Piloto controlado · Preproducción  
**No aplica a:** Rollback de base de datos, reversión de transacciones financieras (ver sección 3)

---

## 1. Objetivo

Definir la estrategia, los criterios de activación y los pasos concretos para revertir el backend API, el frontend admin y la configuración de ambiente a un estado estable conocido cuando un deploy introduce una regresión crítica. Esta fase no crea infraestructura ni automatización cloud: provee el playbook operativo para ejecutar el rollback manualmente en QA y piloto.

---

## 2. Alcance

Este runbook cubre rollback de:

| Componente | Mecanismo | Artefacto |
|------------|-----------|-----------|
| **Backend API** | Redeploy de commit/artefacto anterior en Azure App Service | `.NET 8 publish` → zip deploy |
| **Frontend admin** | Redeploy de build estático anterior en Azure Static Web Apps / App Service | `Vite dist/` → redespliegue |
| **Variables de configuración** | Restaurar snapshot de App Settings en Azure | App Settings backup manual |
| **Workflows GitHub Actions** | Identificar commit verde y revertir rama | `git revert` o `git push` del commit estable |
| **Artefactos QA** | Reutilizar artefacto previo generado con `scripts/build-qa-artifacts.sh` | `artifacts/backend-qa` / `artifacts/frontend-qa` |

---

## 3. Qué NO cubre este runbook

> **Leer antes de ejecutar cualquier rollback.**

| Fuera de alcance | Por qué | Responsable cuando aplique |
|-----------------|---------|---------------------------|
| Rollback de base de datos (schema/datos) | Riesgo de pérdida de datos contables; requiere backup validado y plan específico | Responsable técnico + Ops + revisión financiera |
| Restauración de backup real de SQL Azure | No probado en este ambiente; proceso separado documentado en T4 | Responsable técnico + Ops |
| Reversión de transacciones financieras ya ejecutadas | **El rollback técnico NO revierte operaciones financieras.** Si ya se ejecutó un pago, retiro o recarga, esa operación persiste en el ledger independientemente del rollback de código. | Responsable financiero + proceso de corrección manual con auditoría |
| Reversión de saldos/ledger | Requiere contraasiento contable, no rollback técnico | Responsable financiero + contador |
| Rollback automático por infraestructura cloud | No configurado todavía (Azure deployment slots, blue/green) | Fase futura |
| Rollback de migraciones SQL (001–007) | Las migraciones son acumulativas y pueden afectar integridad referencial | Revisar sección 3 del runbook QA antes de intentar |

**Principio fundamental:** rollback técnico y corrección financiera son procesos independientes. Si hubo operaciones financieras durante el periodo con el código defectuoso, el equipo financiero debe evaluarlas por separado antes de declarar el incidente cerrado.

---

## 4. Criterios para activar rollback

Evaluar cada criterio con la matriz de la sección 5.

| # | Criterio | Severidad sugerida |
|---|----------|-------------------|
| R1 | `Backend Validation` falla en GitHub Actions después de un deploy | Critical |
| R2 | `/health` o `/api/diagnostics/ready` no responde HTTP 200 | Critical |
| R3 | Login (POST `/api/auth/login`) roto — 500 o credenciales válidas rechazadas | Critical |
| R4 | Endpoint protegido sin token devuelve 200 en lugar de 401 (exposición) | Critical |
| R5 | Exposición de secretos, connection strings o datos personales en respuestas | Critical |
| R6 | Error 5xx sostenido (> 5 en 5 min) sin causa identificada ni fix inmediato | High |
| R7 | CORS roto — preflight falla desde frontend QA con origen legítimo | High |
| R8 | JWT roto — token válido rechazado en endpoints protegidos | High |
| R9 | `Dependency Security Scan` falla por vulnerabilidad High/Critical nueva | High |
| R10 | Operaciones financieras principales fallan (recarga, pago QR, retiro, liquidación) | High |
| R11 | `Frontend Build` falla o UI crítica rota (login o dashboard inaccesibles) | High |
| R12 | Ledger/balance inconsistente respecto al esperado tras operaciones de prueba | High — requiere revisión financiera antes del rollback |
| R13 | Latencia sostenida > 10 s en endpoints principales sin causa de infraestructura | Medium |

---

## 5. Matriz rollback vs fix-forward

| Severidad | Decisión por defecto | Condición para excepcionar |
|-----------|---------------------|--------------------------|
| **Critical** | **Rollback inmediato** | Solo aplazar si el fix ya está listo, probado en local/CI y puede desplegarse en < 15 min |
| **High** | **Rollback si afecta flujo principal, seguridad o datos financieros** | Fix-forward si el impacto es aislado, no financiero y el fix está identificado |
| **Medium** | **Fix-forward** (rama `fix/<descripcion>`, PR, CI verde, deploy) | Rollback si el fix tarda > 4 h o escala a High |
| **Low** | **Backlog** | Nunca activar rollback por criterio Low solamente |

**Regla de quórum:** para activar rollback en criterio R5 (exposición de secretos) o R4 (endpoint expuesto), no esperar quórum — rollback inmediato + notificar a Security Lead.

**Dueño de la decisión:** Responsable técnico. En ausencia, el QA Lead puede activar rollback Critical con notificación inmediata al Responsable técnico.

---

## 6. Procedimiento: rollback backend

### Paso B1 — Identificar el último commit verde

```bash
# Ver historial reciente
git log --oneline -10

# Confirmar que el commit objetivo tiene GitHub Actions en success
gh run list --repo <org>/<repo> --limit 10 --json headSha,workflowName,conclusion
```

Anotar el SHA del último commit con `Backend Validation` en `success`.

### Paso B2 — Reconstruir artefacto del commit estable

```bash
# Checkout al commit estable
git checkout <sha-commit-estable>

# Reconstruir artefacto
bash scripts/build-backend-qa.sh
# O manualmente:
dotnet publish backend/Xpay.Api/Xpay.Api.csproj -c Release -o ./publish-rollback
```

### Paso B3 — Redeploy en Azure App Service

```bash
# Comprimir artefacto
cd publish-rollback && zip -r ../xpay-rollback-backend.zip . && cd ..

# Deploy via Azure CLI
az webapp deploy \
  --resource-group rg-xpay-qa \
  --name xpay-api-qa \
  --src-path xpay-rollback-backend.zip \
  --type zip
```

Alternativa desde Azure Portal: App Service → Deployment Center → historial de deployments → seleccionar versión anterior → Redeploy.

### Paso B4 — Volver a rama main

```bash
# Volver a main después del rollback de artefacto
git checkout main
```

### Paso B5 — Validar backend post-rollback (ver sección 9)

---

## 7. Procedimiento: rollback frontend

### Paso F1 — Identificar el último build verde

```bash
gh run list --repo <org>/<repo> --limit 10 --json headSha,workflowName,conclusion \
  | jq '.[] | select(.workflowName=="Frontend Build" and .conclusion=="success")'
```

### Paso F2 — Reconstruir artefacto

```bash
# Checkout al commit estable
git checkout <sha-commit-estable>

# Configurar .env QA (sin secretos en repo)
cp frontend/xpay-admin/.env.qa.example frontend/xpay-admin/.env
# Editar VITE_API_BASE_URL con la URL del backend QA

bash scripts/build-frontend-qa.sh
# Genera artifacts/frontend-qa/
```

### Paso F3 — Redeploy del artefacto estático

Si se usa **Azure Static Web Apps**: subir el contenido de `artifacts/frontend-qa/` o `frontend/xpay-admin/dist/` según el método de deploy configurado.

Si se usa **Azure App Service (static files)**: zip deploy de `artifacts/frontend-qa/`.

### Paso F4 — Validar frontend post-rollback

1. Abrir la URL del frontend QA en navegador.
2. Verificar que el formulario de login carga.
3. Verificar el label de API (debe mostrar URL QA, no `localhost`).
4. Hacer login con usuario QA → verificar redirección al dashboard.
5. Limpiar caché si es necesario (`Ctrl+Shift+R`).

---

## 8. Procedimiento: rollback de configuración

> **Antes de cada deploy**, tomar screenshot o exportar las App Settings actuales de Azure. Azure Portal no tiene rollback automático de configuración.

### Paso C1 — Conservar snapshot antes del deploy

En Azure Portal: App Service → Configuration → Export → guardar el JSON como evidencia.

Alternativa via Azure CLI:
```bash
az webapp config appsettings list \
  --name xpay-api-qa \
  --resource-group rg-xpay-qa \
  --output json > appsettings-snapshot-$(date +%Y%m%d-%H%M%S).json
```

> Este archivo puede contener valores sensibles (JWT Key, Connection String). **No versionar en git.** Guardar en almacenamiento seguro o Key Vault.

### Paso C2 — Revertir variables críticas

Si el rollback de código requiere también revertir configuración, restaurar estas variables en Azure App Settings:

| Sección | Variables clave |
|---------|----------------|
| `ConnectionStrings` | `ConnectionStrings__XpayConnection` |
| `Jwt` | `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__ExpirationHours`, `Jwt__ClockSkewSeconds` |
| `Cors` | `Cors__AllowedOrigins__0` (y `__1`, `__2`... si aplica) |
| `ApiDocs` | `ApiDocs__EnableSwagger` |
| `SecurityHeaders` | `SecurityHeaders__EnableSecurityHeaders`, `SecurityHeaders__EnableNoStoreCache` |
| `RateLimiting` | `RateLimiting__EnableRateLimiting`, `RateLimiting__LoginPermitLimit`, `RateLimiting__LoginWindowSeconds` |
| `Audit` | `Audit__EnableAuditLogs` |
| `ErrorHandling` | `ErrorHandling__EnableGlobalErrorHandler` |
| `Diagnostics` | `Diagnostics__EnableErrorTestEndpoint` |
| `Https` | `Https__EnableHttpsRedirection`, `Https__EnableHsts`, `Https__HstsMaxAgeDays` |

### Paso C3 — Reiniciar servicio

```bash
az webapp restart --name xpay-api-qa --resource-group rg-xpay-qa
```

O desde Azure Portal: App Service → Overview → Restart.

### Paso C4 — Validar post-reinicio (sección 9)

---

## 9. Validaciones post-rollback

Ejecutar en orden. Todas deben pasar antes de declarar el rollback exitoso.

### 9.1 GitHub Actions

```bash
gh run list --repo <org>/<repo> --limit 5 --json workflowName,status,conclusion
```

Esperado: `Backend Validation`, `Frontend Build`, `Dependency Security Scan` → `success`.

Si el commit de rollback no tiene runs: ejecutar `workflow_dispatch` o esperar push.

### 9.2 Probes de disponibilidad

```bash
BASE="https://xpay-api-qa.azurewebsites.net"

curl -sf "$BASE/health"          # {"status":"Healthy"}
curl -sf "$BASE/api/diagnostics/ping"  # {"status":"OK","correlationId":"..."}
curl -sf "$BASE/api/diagnostics/ready" # {"status":"READY","correlationId":"..."}
curl -sf "$BASE/api/version"     # {"success":true,"data":{"version":"..."}}
```

### 9.3 Autenticación y autorización

```bash
# Login con usuario QA válido → debe devolver token
curl -sf -X POST "$BASE/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"username":"<qa-user>","password":"<qa-pass>"}' | jq .

# Endpoint protegido sin token → debe devolver 401 (no 200, no 403, no 500)
HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$BASE/api/admin/wallets")
echo "Sin token → $HTTP_STATUS"  # Esperado: 401
```

### 9.4 CORS

```bash
# Origen legítimo → debe devolver Access-Control-Allow-Origin con la URL QA
curl -sI -X OPTIONS "$BASE/api/auth/login" \
  -H "Origin: https://xpay-admin-qa.azurewebsites.net" \
  -H "Access-Control-Request-Method: POST" \
  | grep -i "access-control-allow-origin:" || true

# Origen malicioso → no debe devolver ese origen
curl -sI -X OPTIONS "$BASE/api/auth/login" \
  -H "Origin: https://evil.example.com" \
  | grep -i "access-control-allow-origin:" || true  # debe estar vacío
```

### 9.5 Escaneo de dependencias

```bash
bash scripts/scan-dependencies-security.sh
# Esperado: exit 0
```

### 9.6 Script de validación CI

```bash
API_URL="https://xpay-api-qa.azurewebsites.net" bash scripts/validate-backend.sh
```

Si no hay DB local disponible: ejecutar al menos los probes de disponibilidad y autenticación manualmente.

### 9.7 Operación financiera QA (solo si ambiente controlado)

Solo si el rollback fue por criterio R10 o R12 (operaciones financieras), ejecutar:

```bash
bash scripts/generate-qa-financial-ops.sh
```

Verificar que el ledger registra las operaciones correctamente. **No ejecutar con datos reales.**

---

## 10. Evidencia requerida

Antes de declarar el rollback cerrado, registrar:

| Campo | Descripción |
|-------|-------------|
| **Commit de rollback** | SHA del commit desplegado |
| **Commit problemático** | SHA del commit que generó el incidente |
| **Timestamp de detección** | UTC — cuándo se detectó el incidente |
| **Timestamp de rollback iniciado** | UTC |
| **Timestamp de rollback completado** | UTC |
| **Responsable de la decisión** | Nombre + rol |
| **Responsable de ejecución** | Nombre + rol |
| **Criterio activado** | Código del criterio (R1–R13) + descripción |
| **Componentes revertidos** | Backend / Frontend / Configuración (indicar cuáles) |
| **Correlation IDs relevantes** | De los requests fallidos que confirmaron el incidente |
| **Evidencia antes del rollback** | Screenshot / log / curl output mostrando el fallo |
| **Evidencia después del rollback** | Resultado de las validaciones (sección 9) |
| **Decisión final** | Rollback exitoso / Escalado / Fix-forward decidido |
| **Pendientes post-rollback** | Acciones para corregir la causa raíz |

---

## 11. Plantilla de acta de rollback

```
═══════════════════════════════════════════════════════════
ACTA DE ROLLBACK TÉCNICO — XPAY MVP
═══════════════════════════════════════════════════════════
Fecha y hora UTC de detección  : ____________________
Fecha y hora UTC de inicio     : ____________________
Fecha y hora UTC de cierre     : ____________________

Ambiente afectado              : QA / Piloto / Preproducción
Commit problemático (SHA)      : ____________________
Commit de rollback (SHA)       : ____________________

Criterio activado              : R__ — ________________
Componentes revertidos         : [ ] Backend  [ ] Frontend  [ ] Configuración

Descripción del incidente:
_____________________________________________________________
_____________________________________________________________

Correlation IDs relevantes:
_____________________________________________________________

Evidencia antes del rollback (adjuntar o describir):
_____________________________________________________________

Validaciones post-rollback (marcar las ejecutadas y su resultado):
[ ] GitHub Actions verde (Backend Validation, Frontend Build, Dependency Scan)
[ ] /health → HTTP 200
[ ] /api/diagnostics/ping → HTTP 200 + status=OK
[ ] /api/diagnostics/ready → HTTP 200 + status=READY
[ ] /api/version → HTTP 200 + versión correcta
[ ] Login con credenciales válidas → token emitido
[ ] Endpoint protegido sin token → HTTP 401
[ ] CORS origen legítimo → Access-Control-Allow-Origin correcto
[ ] Dependency scan → exit 0
[ ] Operación financiera QA (si aplica) → resultado: ________

Causa raíz identificada:
_____________________________________________________________

Decisión final:
[ ] Rollback exitoso — sistema estable en versión anterior
[ ] Rollback exitoso — fix-forward planificado: ____________
[ ] Rollback parcial — pendientes: _________________________
[ ] Escalado — motivo: ____________________________________

¿Involucra operaciones financieras?
[ ] No
[ ] Sí — descripción: _______ — Responsable financiero notificado: ___

Pendientes post-rollback:
_____________________________________________________________

Responsable de la decisión    : ________________________ Firma: _____
Responsable de ejecución      : ________________________ Firma: _____
Notificado a Security Lead    : ________________________ Fecha: _____
═══════════════════════════════════════════════════════════
```

---

## 12. Relación con otros runbooks

| Runbook | Cuándo usarlo conjuntamente |
|---------|---------------------------|
| `docs/OBSERVABILITY_AND_ALERTING_RUNBOOK.md` | Para confirmar el incidente antes de decidir el rollback (pasos 1–5 del runbook de alertas) |
| `docs/QA_DEPLOYMENT_RUNBOOK.md` — Sección 11 | Para referencia rápida de comandos de rollback en QA |
| `docs/QA_INTERNAL_ISSUES_TRACKING.md` | Para registrar el incidente en el tracker interno |
| `docs/PREPRODUCTION_GAPS_AND_REAL_MONEY_CHECKLIST.md` | Para evaluar si el incidente bloquea el avance a dinero real |

---

## 13. Qué falta para rollback productivo completo

| Pendiente | Bloquea dinero real |
|-----------|-------------------|
| Rollback real ejecutado y probado en Azure QA | Sí |
| Snapshot automático de App Settings antes de cada deploy | Sí |
| Backup/restore de base de datos probado (T4) | Sí — crítico |
| Runbook financiero para reversión de operaciones (T4, F-series) | Sí — crítico |
| Azure deployment slots o blue/green deployment | Recomendado |
| Automatización de rollback (script o workflow CI/CD) | Recomendado |
| Rollback probado con simulacro formal | Sí |
| On-call con responsable definido para ejecutar rollback nocturno | Sí |

---

*Documento creado en Fase 48. Actualizar al ejecutar el primer rollback real en Azure o al incorporar automatización.*
