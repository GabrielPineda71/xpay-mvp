# XPAY MVP — Readiness de Monitoreo y Alertas Operativas

**Fase:** 47  
**Estado:** Readiness documentado — alertas reales pendientes de configuración en Azure  
**Aplica a:** QA · Piloto controlado · Preproducción  
**No aplica a:** Operación con dinero real sin completar los ítems pendientes de T5/T6

---

## 1. Objetivo

Definir los endpoints de monitoreo disponibles, los probes recomendados, la matriz de alertas mínimas y el runbook de respuesta a incidentes para el MVP XPAY. Esta fase no crea recursos cloud ni configura alertas reales: provee la base documental para que el responsable técnico y Ops las configure antes del piloto o de dinero real.

---

## 2. Endpoints de monitoreo

Todos los endpoints listados son públicos (sin autenticación JWT). No exponen secretos, connection strings, contraseñas, cédulas ni datos personales.

| Endpoint | Método | Auth | Propósito | Respuesta esperada |
|----------|--------|------|-----------|-------------------|
| `/health` | GET | Ninguna | Uptime probe de plataforma — confirma que el proceso ASP.NET Core levantó | HTTP 200 `{"status":"Healthy","service":"XPAY API","timestamp":"..."}` |
| `/api/diagnostics/ping` | GET | Ninguna | Liveness probe — proceso vivo, middleware de correlación activo | HTTP 200 `{"status":"OK","service":"...","environment":"...","timestamp":"...","correlationId":"..."}` |
| `/api/diagnostics/ready` | GET | Ninguna | Readiness probe — API lista a nivel de config/pipeline; no consulta DB | HTTP 200 `{"status":"READY","service":"...","environment":"...","timestampUtc":"...","correlationId":"..."}` |
| `/api/version` | GET | Ninguna | Versión desplegada — confirma el commit activo en el ambiente | HTTP 200 `{"success":true,"data":{"name":"...","version":"0.1.0-mvp","environment":"..."}}` |

> **Diferencia ping vs ready:** `ping` confirma que el proceso está vivo (liveness). `ready` confirma que el pipeline de middleware está activo y la configuración mínima es accesible (readiness). En esta fase ninguno consulta la base de datos — esa verificación queda para una fase futura de health checks con DB.

---

## 3. Tabla de probes recomendados

| Probe | Endpoint / fuente | Frecuencia sugerida QA/Piloto | Umbral de alerta | Severidad |
|-------|------------------|------------------------------|------------------|-----------|
| **Uptime / liveness** | `GET /health` | Cada 1 min | Falla ≥ 2 checks consecutivos | Critical |
| **Readiness** | `GET /api/diagnostics/ready` | Cada 1 min | Falla ≥ 2 checks consecutivos | Critical |
| **API version check** | `GET /api/version` | Cada 5 min o post-deploy | Respuesta ≠ 200 o versión inesperada | High |
| **Tasa de errores 5xx** | Logs / App Insights | Ventana de 5 min | > 5 errores 5xx en 5 min | High |
| **Latencia P95** | Logs / App Insights | Ventana de 1 min | P95 > 3 000 ms | Medium |
| **HTTP 401/403 spikes** | Logs / App Insights | Ventana de 5 min | > 20 respuestas 401/403 en 5 min | Medium |
| **Login failures / rate limiting 429** | Logs de auditoría (`event=LOGIN_FAILURE`) | Ventana de 10 min | > 10 LOGIN_FAILURE en 10 min o cualquier 429 en prod | High |
| **Ausencia de logs de auditoría** | Logs / App Insights | Post-operación financiera QA | 0 eventos `audit=True` después de operación conocida | High |
| **Dependency Security Scan fallido** | GitHub Actions | Cada push a main | Workflow falla (exit 1 o 2) | High |
| **Backend Validation fallido** | GitHub Actions | Cada push a main | Workflow falla | Critical |
| **Frontend Build fallido** | GitHub Actions | Cada push a main | Workflow falla | High |

---

## 4. Matriz de alertas mínimas

### Critical — Respuesta inmediata (< 15 min en horario de piloto)

| Alerta | Condición de disparo | Acción inmediata |
|--------|---------------------|-----------------|
| **API caída** | `/health` o `/api/diagnostics/ping` ≥ 2 fallos consecutivos | Verificar App Service, revisar logs de startup, ejecutar runbook sección 6 |
| **Readiness falla** | `/api/diagnostics/ready` ≥ 2 fallos consecutivos | Revisar middleware pipeline, variables de entorno, última versión desplegada |
| **Backend Validation CI falla** | Workflow `Backend Validation` en estado `failure` | No hacer deploy. Revisar fallo en GitHub Actions, corregir y re-ejecutar |

### High — Respuesta en < 1 hora en horario de piloto

| Alerta | Condición de disparo | Acción inmediata |
|--------|---------------------|-----------------|
| **Tasa 5xx elevada** | > 5 errores HTTP 500/502/503 en 5 min | Buscar en logs por correlationId, revisar último deploy, evaluar rollback |
| **Login failures anómalos** | > 10 `LOGIN_FAILURE` en 10 min desde la misma IP o usuario | Revisar logs de auditoría, verificar rate limiting, escalar si hay patrón de ataque |
| **429 en login** | Respuestas 429 detectadas en producción o piloto | Confirmar que es rate limiting legítimo; si viene de uso normal, revisar límites |
| **Dependency Security Scan falla** | Workflow `Dependency Security Scan` en `failure` | No hacer deploy. Ejecutar `bash scripts/scan-dependencies-security.sh` local, revisar hallazgos, no continuar a dinero real sin resolución o acta de riesgo |
| **Frontend Build falla** | Workflow `Frontend Build` en `failure` | Revisar errores de TypeScript/Vite, corregir en rama separada |
| **Ausencia de logs de auditoría** | 0 eventos con `audit=True` después de operación financiera confirmada | Revisar `Audit__EnableAuditLogs`, revisar `AuditLogService`, buscar por correlationId |

### Medium — Respuesta en < 4 horas

| Alerta | Condición de disparo | Acción |
|--------|---------------------|--------|
| **Latencia P95 alta** | P95 > 3 000 ms en ventana de 5 min | Revisar logs de request timing, verificar DB, evaluar escalado |
| **401/403 spikes** | > 20 respuestas 401/403 en 5 min | Revisar si es expiración masiva de tokens o acceso no autorizado desde IP específica |
| **Versión inesperada en /api/version** | La versión no coincide con el último tag desplegado | Verificar que el deploy fue exitoso y completó correctamente |

### Low — Monitoreo y registro (sin escalado inmediato)

| Alerta | Condición | Acción |
|--------|-----------|--------|
| **Latencia P95 moderada** | P95 entre 1 500 y 3 000 ms | Registrar tendencia, evaluar en próxima revisión |
| **Aumento gradual de 4xx** | Tendencia creciente de 400/404 sin spike agudo | Revisar si es cambio en clientes o endpoints deprecados |

---

## 5. Runbook de respuesta a incidentes

### Paso 1 — Confirmar el incidente

```bash
# Uptime probe
curl -s https://<API_HOST>/health
# Esperado: {"status":"Healthy","service":"XPAY API","timestamp":"..."}

# Liveness probe
curl -s https://<API_HOST>/api/diagnostics/ping
# Esperado: {"status":"OK","correlationId":"..."}

# Readiness probe
curl -s https://<API_HOST>/api/diagnostics/ready
# Esperado: {"status":"READY","correlationId":"..."}

# Versión activa
curl -s https://<API_HOST>/api/version
# Esperado: {"success":true,"data":{"version":"0.1.0-mvp","environment":"..."}}
```

Si alguno falla (no HTTP 200 o respuesta errónea): **incidente confirmado** → continuar.

### Paso 2 — Revisar GitHub Actions

1. Ir a `https://github.com/<repo>/actions`
2. Verificar los últimos runs de:
   - `Backend Validation`
   - `Frontend Build`
   - `Dependency Security Scan`
3. Si alguno está en `failure`: **no hacer deploy adicional**. Revisar el log del paso fallido.
4. Si todos están en `success` pero la API sigue caída: el problema es de infraestructura (App Service) o de configuración de ambiente, no de código.

### Paso 3 — Revisar logs por correlationId

En Azure App Service → Log Stream o Application Insights:

```
# Buscar por correlationId de la respuesta fallida:
correlationId = "<valor del header X-Correlation-ID>"

# Buscar errores recientes:
level = Error
# o en App Insights:
requests | where resultCode == "500" | order by timestamp desc | take 20
```

El header `X-Correlation-ID` está presente en **todas** las respuestas del backend, incluidos los HTTP 500. Usarlo para trazar el request completo en los logs.

### Paso 4 — Revisar CORS / JWT / rate limiting si aplica

- **CORS:** si frontend no conecta pero backend sí responde en curl → revisar `Access-Control-Allow-Origin` en respuesta. Verificar `Cors__AllowedOrigins__0` en Azure App Settings.
- **JWT:** si endpoints protegidos devuelven 401 → verificar `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience` en App Settings. Verificar que el token no expiró (`Jwt__ExpirationHours`).
- **Rate limiting:** si aparecen 429 anómalos → verificar `RateLimiting__LoginPermitLimit` y `RateLimiting__LoginWindowSeconds`. No deshabilitar en producción sin acta de riesgo.

### Paso 5 — Revisar última versión/commit

```bash
# Confirmar versión activa
curl -s https://<API_HOST>/api/version

# Confirmar último commit desplegado vs. GitHub
git log --oneline -5
```

Si la versión no coincide con el commit esperado: el deploy puede haber fallado silenciosamente. Re-ejecutar deploy o rollback.

### Paso 6 — Evaluar rollback

Si el incidente correlaciona con un deploy reciente:

1. Identificar el último commit estable (ver `git log --oneline`).
2. Hacer rollback al commit anterior vía Azure App Service → Deployment Center → historial de deployments.
3. Verificar que los 3 workflows de GitHub Actions están en `success` para el commit de rollback.
4. Re-ejecutar smoke test (`bash scripts/validate-backend.sh` si hay ambiente local, o los probes manuales en QA).

### Paso 7 — Escalar

Si el incidente no se resuelve en el tiempo objetivo:

| Escenario | Escalar a |
|-----------|-----------|
| API caída > 15 min | Responsable técnico + Ops |
| Sospecha de ataque (login failures, 429 masivo) | Security Lead |
| Error financiero / saldo incorrecto | Responsable financiero + Responsable técnico |
| Fallo de CI bloqueante | Responsable técnico |

### Paso 8 — Registrar el incidente

Usando el formato de `docs/QA_INTERNAL_ISSUES_TRACKING.md`:

- ID, fecha, hora UTC de detección y resolución
- Severidad
- Descripción del síntoma
- correlationId si aplica
- Causa raíz identificada
- Acción tomada
- Estado: Abierto / En progreso / Cerrado
- Pendientes si los hay

---

## 6. Guía de configuración sugerida (sin crear recursos)

> Esta sección describe la configuración recomendada para cuando se apruebe la creación de recursos cloud. No implica la creación de recursos en esta fase.

### Opción A — Azure Monitor + Application Insights

1. Crear recurso Application Insights en el mismo Resource Group del App Service.
2. Agregar `APPLICATIONINSIGHTS_CONNECTION_STRING` como App Setting en el App Service backend.
3. Instalar `Microsoft.ApplicationInsights.AspNetCore` (evaluar en Fase posterior — requiere aprobación de dependencia).
4. Configurar Availability Test en Azure Monitor:
   - URL: `https://<API_HOST>/api/diagnostics/ready`
   - Frecuencia: 1 min
   - Ubicaciones: mínimo 2 regiones
   - Alerta: disparar si falla > 1 check
5. Configurar Alert Rules:
   - `requests/failed` > 5 en 5 min → severidad High
   - Availability < 100% → severidad Critical
6. Configurar Action Group con email/Teams/SMS.

### Opción B — Monitor externo (UptimeRobot / BetterUptime / similar)

1. Crear monitor HTTP(S) hacia `https://<API_HOST>/api/diagnostics/ping`.
2. Frecuencia: 1 min. Timeout: 10 s.
3. Keyword check: `"status":"OK"`.
4. Canales: email + canal de Teams/Slack del equipo.
5. Crear segundo monitor hacia `https://<API_HOST>/api/diagnostics/ready`.
6. Keyword check: `"status":"READY"`.

### Opción C — GitHub Actions como señal de CI/CD

Sin infraestructura adicional, los 3 workflows actúan como señal mínima de calidad:

- `Backend Validation` → tests de endpoints + validación de integridad
- `Frontend Build` → compilación TypeScript/Vite
- `Dependency Security Scan` → scan NuGet + npm

Revisar estado en cada deploy antes de aprobar el paso a producción.

---

## 7. Variables de observabilidad relevantes

| Variable de entorno | Config key | Default | Propósito |
|--------------------|------------|---------|-----------|
| `Observability__EnableCorrelationId` | `Observability:EnableCorrelationId` | `true` | Genera/propaga X-Correlation-ID |
| `Observability__EnableRequestLogging` | `Observability:EnableRequestLogging` | `true` | Log de cada request con método/path/status/elapsed/cid |
| `Audit__EnableAuditLogs` | `Audit:EnableAuditLogs` | `true` | Eventos de auditoría financiera |
| `ErrorHandling__EnableGlobalErrorHandler` | `ErrorHandling:EnableGlobalErrorHandler` | `true` | JSON 500 seguro sin stack trace |
| `Diagnostics__EnableErrorTestEndpoint` | `Diagnostics:EnableErrorTestEndpoint` | `false` | Endpoint de prueba de error handling (deshabilitar en producción) |

---

## 8. Qué no está cubierto aún

Los siguientes ítems son pendientes explícitos antes de operación con dinero real o apertura pública:

| Pendiente | Responsable | Bloqueante para dinero real |
|-----------|-------------|---------------------------|
| Configuración real de Azure Monitor / App Insights | Responsable técnico + Ops | Sí (T5, T6) |
| Alertas de uptime activas y probadas | Ops | Sí (T5) |
| Canales de notificación configurados (email/Teams/PagerDuty) | Ops | Sí (T6) |
| Dashboards operativos con métricas en tiempo real | Responsable técnico | Sí (T6) |
| Umbrales validados con carga real | QA Lead + Responsable técnico | Sí (T6, T10) |
| On-call formal con rotación y SLA definido | Ops + Responsable técnico | Sí |
| Monitoreo de base de datos (disponibilidad, espacio, DTUs) | Ops | Sí |
| Monitoreo de saldos/ledger en tiempo real (anomalías financieras) | Responsable financiero + técnico | Sí — crítico |
| SIEM / alertas de seguridad externas | Security Lead | Sí |
| Alertas SMS/WhatsApp/email para incidentes críticos | Ops | Sí |
| Health check con verificación de conectividad a DB | Responsable técnico | Recomendado |
| Runbook validado con simulacro de incidente | QA Lead + Ops | Sí |

---

*Documento creado en Fase 47. Actualizar al configurar alertas reales o al modificar endpoints de diagnóstico.*
