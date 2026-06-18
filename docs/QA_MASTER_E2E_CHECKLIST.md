# XPAY MVP — Checklist Maestro QA End-to-End

**Versión:** 1.0
**Fecha:** 2026-06-18
**Tipo:** Checklist operativo QA — uso exclusivo QA / desarrollo

---

## 1. Propósito

Este documento une el ciclo QA completo: desde confirmar que el repositorio está limpio hasta firmar el acta de aprobación para usuarios internos. Es la referencia única que una persona puede seguir de punta a punta sin necesidad de saltar entre múltiples documentos.

**Qué cubre:**
- Preparación de artefactos y ambiente.
- Ejecución de base de datos y seed.
- Generación de operaciones financieras QA.
- Smoke test de backend y frontend.
- Ejecución de los 35 casos manuales QA.
- Toma de decisión de aprobación.

**Qué NO es:**
- **No producción.** Ejecutar solo en ambientes QA o locales.
- **No dinero real.** Todas las operaciones son de prueba controlada.
- **No datos reales.** Personas, documentos, emails y cuentas son ficticios.
- **No deploy automático.** Cada paso requiere acción manual deliberada.

---

## 2. Mapa de documentos QA

| Documento / Recurso | Rol en el ciclo QA |
|--------------------|--------------------|
| [`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md) | Define el alcance, versión, features incluidas/excluidas, riesgos y criterios de aprobación |
| [`docs/QA_DEPLOYMENT_RUNBOOK.md`](QA_DEPLOYMENT_RUNBOOK.md) | Guía operativa de despliegue en Azure: recursos, variables de entorno, orden de scripts SQL, smoke test y rollback |
| [`docs/QA_OPERATIONS_VARIABLES.md`](QA_OPERATIONS_VARIABLES.md) | Cómo preparar variables locales (TOKEN, IDs) sin secretos en el repo |
| [`database/008_seed_qa_dataset.sql`](../database/008_seed_qa_dataset.sql) | Seed QA: personas, usuarios, wallets, comercio demo QA, QR y cuentas ledger |
| [`docs/QA_FINANCIAL_OPERATIONS_API.md`](QA_FINANCIAL_OPERATIONS_API.md) | Guía de operaciones financieras vía API: recarga, pago QR, liquidación, retiros, validación contable |
| [`docs/QA_MANUAL_TESTING.md`](QA_MANUAL_TESTING.md) | 35 casos de prueba manuales: pasos, evidencias esperadas, criterios de aprobación |
| [`docs/QA_EXECUTION_TEMPLATE.md`](QA_EXECUTION_TEMPLATE.md) | Plantilla de registro: matriz de ejecución, bugs, evidencias y acta de cierre |
| [`ops/qa.env.example`](../ops/qa.env.example) | Plantilla de variables operativas (versionada, sin secretos) |
| [`scripts/build-backend-qa.sh`](../scripts/build-backend-qa.sh) | Genera artefacto .NET 8 de publicación QA |
| [`scripts/build-frontend-qa.sh`](../scripts/build-frontend-qa.sh) | Genera artefacto Vite del frontend QA |
| [`scripts/build-qa-artifacts.sh`](../scripts/build-qa-artifacts.sh) | Orquestador: ejecuta backend + frontend en secuencia |
| [`scripts/generate-qa-financial-ops.sh`](../scripts/generate-qa-financial-ops.sh) | Automatiza el flujo financiero QA (pasos A–H) vía endpoints reales |

---

## 3. Fase 0 — Confirmar estado del repositorio

Antes de cualquier acción, verificar que el punto de partida es correcto.

```bash
# Estado del árbol de trabajo
git status

# Sincronizar con remoto
git pull origin main

# Confirmar último commit
git log --oneline -3

# Estado de GitHub Actions
gh run list --limit 5
```

Checklist:

- [ ] `git status` → `nothing to commit, working tree clean`
- [ ] `git pull` no trajo conflictos
- [ ] Último commit es el esperado para el ciclo QA (anotar hash)
- [ ] `Backend Validation` en GitHub Actions → `completed success`
- [ ] `Frontend Build` en GitHub Actions → `completed success`
- [ ] No hay archivos locales con secretos pendientes de borrar (`ops/qa.env.local`, `.env`, tokens en texto)
- [ ] `git status` no muestra `ops/qa.env.local` ni ningún archivo `.local`

> Si CI está rojo, detener. Ver sección 13 — Señales de bloqueo.

---

## 4. Fase 1 — Preparar artefactos QA

Generar los artefactos de publicación del backend y frontend antes del despliegue.

```bash
# Opción A: solo backend
bash scripts/build-backend-qa.sh

# Opción B: solo frontend (requiere frontend/xpay-admin/.env con VITE_API_BASE_URL)
bash scripts/build-frontend-qa.sh

# Opción C: ambos en secuencia (recomendado)
bash scripts/build-qa-artifacts.sh
```

Checklist:

- [ ] `frontend/xpay-admin/.env` existe con `VITE_API_BASE_URL` configurado (si se va a construir frontend)
- [ ] `scripts/build-backend-qa.sh` completó sin errores
- [ ] `scripts/build-frontend-qa.sh` completó sin errores
- [ ] Directorio `artifacts/backend-qa/` existe y contiene la publicación .NET
- [ ] Directorio `artifacts/frontend-qa/` existe y contiene `index.html`
- [ ] `git status` → `artifacts/` **no aparece** como archivo a trackear (protegido por `.gitignore`)

> Los artefactos generados localmente por estos scripts son `artifacts/`; están en `.gitignore` y nunca se commitean.

---

## 5. Fase 2 — Preparar ambiente QA

### Si el ambiente es Azure QA

Seguir `docs/QA_DEPLOYMENT_RUNBOOK.md` sección a sección:
- Crear recursos Azure (App Service backend, App Service frontend, Azure SQL).
- Configurar variables de entorno en App Service (sección 4 y 5 del runbook).
- Desplegar artefactos (sección 7 y 8 del runbook).

### Si el ambiente es local

```bash
# Iniciar backend (.NET 8)
cd backend/Xpay.Api
dotnet run
# Verificar: http://localhost:5000/health

# Iniciar frontend (Vite dev server)
cd frontend/xpay-admin
npm run dev
# Verificar: http://localhost:5173
```

Checklist:

- [ ] Backend responde en `$API_BASE/health` con `{"status":"Healthy"}` (o equivalente)
- [ ] `$API_BASE/api/version` retorna versión
- [ ] Base de datos accesible desde el backend (connection string configurado)
- [ ] JWT configurado: `Jwt__Key` (≥32 chars), `Jwt__Issuer`, `Jwt__Audience`, `Jwt__ExpirationHours`
- [ ] CORS configurado: `Cors__AllowedOrigins__0` apunta al origen del frontend si se usa UI
- [ ] Frontend apunta al backend correcto (`VITE_API_BASE_URL` o fallback `localhost:5000`)

---

## 6. Fase 3 — Ejecutar base de datos

Ejecutar los scripts SQL en orden estricto usando Azure Data Studio, SSMS o `sqlcmd`.

```sql
-- Ejecutar en orden, verificando cada uno antes de continuar:
-- 001_security_identity.sql
-- 002_wallet_ledger.sql
-- 003_comercios_qr.sql
-- 004_liquidacion_qr.sql
-- 005_retiros_comercio.sql
-- 006_gestion_retiros_comercio.sql
-- 007_security_roles_jwt.sql
-- 008_seed_qa_dataset.sql   ← opcional QA/dev, nunca en producción
```

Checklist:

- [ ] Script `001` ejecutado sin errores — tablas `personas`, `usuarios`, `roles`, `usuario_roles` existen
- [ ] Script `002` ejecutado sin errores — tablas `wallets`, `wallet_saldos`, `ledger_cuentas` existen
- [ ] Script `003` ejecutado sin errores — `comercios`, `comercio_tiendas`, `qr_comercios` existen; `Comercio Demo XPAY` existe
- [ ] Script `004` ejecutado sin errores — columna `id_wallet_comercio` en `comercios`
- [ ] Script `005` ejecutado sin errores — tabla `retiros_comercio` existe
- [ ] Script `006` ejecutado sin errores — columnas de gestión en `retiros_comercio` existen
- [ ] Script `007` ejecutado sin errores — roles `ADMIN_XPAY`, `OPERADOR_XPAY`, `COMERCIO` existen
- [ ] Script `008` ejecutado — personas QA (`CC 900000001–4`), usuarios QA, wallets QA, `Comercio Demo XPAY QA`, `QR-DEMO-XPAY-QA-001`
- [ ] Cuentas ledger confirmadas: `110101`, `210101`, `210201`, `210202`, `210203`

> El script `008` **no inserta transacciones financieras**. Los saldos y el ledger se generan en la Fase 5 vía endpoints del backend.

---

## 7. Fase 4 — Preparar variables operativas

```bash
# Copiar plantilla
cp ops/qa.env.example ops/qa.env.local

# Completar con valores reales del ambiente QA
nano ops/qa.env.local

# Cargar variables en el shell actual
source ops/qa.env.local

# Verificar que están cargadas
echo "API_BASE=$API_BASE"
echo "TOKEN=${TOKEN:0:20}..."
echo "ID_WALLET_USUARIO_1=$ID_WALLET_USUARIO_1"
echo "ID_WALLET_USUARIO_2=$ID_WALLET_USUARIO_2"
echo "ID_USUARIO_QA=$ID_USUARIO_QA"
echo "ID_COMERCIO_QA=$ID_COMERCIO_QA"
```

Pasos para obtener cada variable:

```bash
# TOKEN — hacer login con usuario QA habilitado
curl -s -X POST "$API_BASE/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"usuario": "<usuario-qa>", "password": "<contraseña-qa>"}'
# → copiar el campo "token" de la respuesta

# IDs de wallets y usuario QA
curl -s "$API_BASE/api/admin/wallets" -H "Authorization: Bearer $TOKEN"

# ID de comercio QA
curl -s "$API_BASE/api/admin/comercios" -H "Authorization: Bearer $TOKEN"
```

Checklist:

- [ ] `ops/qa.env.local` creado localmente (no en repo)
- [ ] `API_BASE` apunta al backend correcto (sin barra final)
- [ ] `TOKEN` obtenido vía `POST /api/auth/login` con usuario QA habilitado
- [ ] `ID_WALLET_USUARIO_1` obtenido: wallet de `QA Usuario Uno`
- [ ] `ID_WALLET_USUARIO_2` obtenido: wallet de `QA Usuario Dos`
- [ ] `ID_USUARIO_QA` obtenido: `id_usuario` del usuario QA ejecutor
- [ ] `ID_COMERCIO_QA` obtenido: ID de `Comercio Demo XPAY QA`
- [ ] `source ops/qa.env.local` ejecutado
- [ ] `git status` → `ops/qa.env.local` **no aparece** (protegido por `.gitignore`)

> Ver `docs/QA_OPERATIONS_VARIABLES.md` para instrucciones detalladas y troubleshooting de variables.

---

## 8. Fase 5 — Generar operaciones financieras QA

```bash
bash scripts/generate-qa-financial-ops.sh
```

El script ejecuta automáticamente los pasos A–H:

| Paso | Operación | Estado resultante |
|------|-----------|------------------|
| A | Recarga wallet usuario 1 (+100,000) | Saldo usuario +100,000 |
| B | Transferencia usuario 1 → usuario 2 (25,000) | Saldo reasignado |
| C | Pago QR `QR-DEMO-XPAY-QA-001` (30,000) | Venta `CONTINGENCIA` |
| D | Liquidar venta QR | Venta `LIQUIDADA`, saldo comercio +valor |
| E | Retiro comercio 1 (20,000) | Retiro `PENDIENTE` |
| F | Confirmar pago retiro 1 | Retiro `PAGADO` |
| G | Retiro comercio 2 (5,000) | Retiro `PENDIENTE` |
| H | Rechazar retiro 2 | Retiro `RECHAZADO`, saldo comercio +5,000 |

Checklist:

- [ ] Script completó sin errores (`exit 0`)
- [ ] Paso A — recarga: `"success": true`
- [ ] Paso B — transferencia: `"success": true`
- [ ] Paso C — pago QR: `"success": true`, `ID_VENTA_QR` capturado
- [ ] Paso D — liquidación: `"success": true`, venta en `LIQUIDADA`
- [ ] Paso E — retiro 1: `"success": true`, `ID_RETIRO_1` capturado
- [ ] Paso F — confirmación: `"success": true`, retiro en `PAGADO`
- [ ] Paso G — retiro 2: `"success": true`, `ID_RETIRO_2` capturado
- [ ] Paso H — rechazo: `"success": true`, retiro en `RECHAZADO`
- [ ] Consultas finales del script: saldos, resumen comercio, ventas QR, retiros, ledger — responden con datos
- [ ] `GET /api/admin/ledger-transacciones` → ≥6 transacciones generadas
- [ ] Cada transacción ledger tiene débitos = créditos (validación manual spot-check)

> Si el script falla en un paso intermedio con `"success": false`, consultar `docs/QA_FINANCIAL_OPERATIONS_API.md` sección 18 (Problemas comunes).

---

## 9. Fase 6 — Smoke test backend y frontend

Verificar que el ambiente QA está operativo antes de ejecutar los 35 casos manuales.

### Backend smoke test

```bash
# Health
curl -s "$API_BASE/health"

# Versión
curl -s "$API_BASE/api/version"

# Swagger (abrir en navegador)
# $API_BASE/swagger

# Login
curl -s -X POST "$API_BASE/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"usuario": "<usuario-qa>", "password": "<contraseña-qa>"}'

# Endpoint protegido sin token → debe retornar 401
curl -s -o /dev/null -w "%{http_code}" "$API_BASE/api/admin/wallets"
```

### Frontend smoke test (manual en navegador)

1. Abrir URL del frontend QA.
2. Verificar que aparece pantalla de login.
3. Verificar etiqueta `API:` visible con la URL del backend.
4. Hacer login con usuario QA.
5. Verificar que navega al dashboard.

Checklist:

- [ ] `GET /health` → respuesta OK (200 o `{"status":"Healthy"}`)
- [ ] `GET /api/version` → retorna versión del API
- [ ] Swagger accesible en `$API_BASE/swagger` (si está habilitado en ambiente QA)
- [ ] Login con usuario QA → token JWT en respuesta
- [ ] `GET /api/admin/wallets` **sin token** → `401 Unauthorized`
- [ ] Dashboard del frontend carga sin errores de consola críticos
- [ ] Página de wallets muestra datos
- [ ] Página de comercios muestra `Comercio Demo XPAY QA`
- [ ] Página de retiros muestra retiro `PAGADO` y retiro `RECHAZADO`
- [ ] Página de ventas QR muestra venta `LIQUIDADA`
- [ ] Ledger muestra transacciones
- [ ] Etiqueta `API:` visible en layout con URL correcta
- [ ] Logout funciona y redirige a login

---

## 10. Fase 7 — Ejecutar casos manuales QA

### Preparación

```bash
# Copiar la plantilla de ejecución para este ciclo
cp docs/QA_EXECUTION_TEMPLATE.md docs/QA_EXECUTION_RUN-$(date +%Y%m%d).md
```

Abrir `docs/QA_MANUAL_TESTING.md` y `docs/QA_EXECUTION_RUN-YYYYMMDD.md` en paralelo.

### Ejecución

Checklist de módulos:

- [ ] **Módulo 1 — Autenticación y sesión** (QA-01 a QA-05): login válido, inválido, token expirado, logout, sesión expirada
- [ ] **Módulo 2 — Wallets** (QA-06 a QA-10): listado, saldo, movimientos, recarga, estado
- [ ] **Módulo 3 — Transferencias** (QA-11 a QA-13): entre wallets QA, saldo insuficiente, validaciones
- [ ] **Módulo 4 — Pago QR** (QA-14 a QA-17): pago correcto, QR inválido, saldo insuficiente, estado venta
- [ ] **Módulo 5 — Liquidación QR** (QA-18 a QA-20): liquidar, doble liquidación, estado resultante
- [ ] **Módulo 6 — Retiros comercio** (QA-21 a QA-25): solicitar, listar, confirmar pago, rechazar, validaciones
- [ ] **Módulo 7 — Reportes** (QA-26 a QA-29): estado cuenta, resumen comercio, ledger transacción, resumen general
- [ ] **Módulo 8 — Dashboard admin** (QA-30 a QA-32): carga de secciones, datos reales, retry
- [ ] **Módulo 9 — Seguridad** (QA-33 a QA-34): endpoints protegidos, 401 sin token
- [ ] **Módulo 10 — Errores y edge cases** (QA-35): manejo de errores de red

### Por cada caso QA

- [ ] Registrar resultado en la plantilla: `APROBADO` / `FALLIDO` / `BLOQUEADO`
- [ ] Capturar screenshot de evidencia
- [ ] Si falla: registrar bug con severidad (`CRÍTICO` / `ALTO` / `MEDIO` / `BAJO`)
- [ ] Si el bug es crítico: detener módulo relacionado, escalar

### Cierre de ejecución

- [ ] Los 35 casos tienen resultado registrado
- [ ] Evidencias capturadas y referenciadas en la plantilla
- [ ] Bugs registrados con ID único (ej: `BUG-001`)
- [ ] Bugs críticos y altos tienen asignado responsable
- [ ] Acta de cierre completada en `QA_EXECUTION_RUN-YYYYMMDD.md`

---

## 11. Fase 8 — Decisión de aprobación

### Criterios mínimos de aprobación

| Criterio | Umbral mínimo |
|----------|---------------|
| Casos críticos aprobados | **100%** |
| Casos de alta severidad aprobados | **≥ 90%** |
| Bugs críticos abiertos | **0** |
| Errores en operaciones financieras | **0** |
| Endpoints protegidos expuestos sin token | **0** |

### Checklist de aprobación

- [ ] 100% de casos críticos marcados `APROBADO`
- [ ] ≥ 90% de casos de prioridad alta marcados `APROBADO`
- [ ] Ningún bug con severidad `CRÍTICO` abierto sin corrección
- [ ] Ninguna operación financiera produce ledger desbalanceado
- [ ] Ningún endpoint protegido retorna 200 sin `Authorization: Bearer`
- [ ] Acta de cierre QA firmada por responsable QA
- [ ] Responsable técnico revisó el acta

### Decisión

| Resultado | Condición | Siguiente paso |
|-----------|-----------|----------------|
| **Aprobado** | Todos los criterios cumplidos | Comunicar a stakeholders; proceder con distribución a usuarios internos |
| **Aprobado con observaciones** | Criterios cumplidos con bugs de baja severidad abiertos documentados | Compartir con usuarios internos con lista de observaciones conocidas |
| **No aprobado** | Al menos un criterio incumplido | Corregir, re-ejecutar ciclo QA desde Fase 5 (si solo cambia data) o Fase 0 (si hay cambios de código) |

---

## 12. Comandos rápidos de referencia

```bash
# --- Fase 0: Estado del repo ---
git status
git pull origin main
git log --oneline -3
gh run list --limit 5

# --- Fase 1: Artefactos ---
bash scripts/build-backend-qa.sh
bash scripts/build-frontend-qa.sh
# o ambos:
bash scripts/build-qa-artifacts.sh

# --- Fase 2: Health check local ---
curl -s http://localhost:5000/health

# --- Fase 4: Variables operativas ---
cp ops/qa.env.example ops/qa.env.local
# (editar ops/qa.env.local con valores reales)
source ops/qa.env.local

# --- Fase 5: Operaciones financieras ---
bash scripts/generate-qa-financial-ops.sh

# --- Smoke test rápido ---
curl -s "$API_BASE/health"
curl -s "$API_BASE/api/version"
curl -s -o /dev/null -w "%{http_code}" "$API_BASE/api/admin/wallets"
# debe retornar 401

# --- Ver wallets e IDs ---
curl -s "$API_BASE/api/admin/wallets" -H "Authorization: Bearer $TOKEN" | jq .
curl -s "$API_BASE/api/admin/comercios" -H "Authorization: Bearer $TOKEN" | jq .
```

---

## 13. Señales de bloqueo

| Señal de bloqueo | Qué significa | Acción inmediata |
|-----------------|---------------|------------------|
| CI rojo (Backend Validation o Frontend Build failed) | Cambio reciente rompió compilación o tests | **Detener.** No continuar con QA. Investigar el run fallido con `gh run view --log-failed`. Corregir y esperar CI verde. |
| Backend no responde en `/health` | Servidor no iniciado, URL incorrecta, o error de configuración | Revisar que el backend está corriendo y que `API_BASE` es correcto. Revisar logs del servidor. |
| Login falla con credenciales correctas | Hash BCrypt placeholder (seed 008) o JWT mal configurado | Verificar que el usuario QA tiene hash real. Revisar variables `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`. |
| Script `008` falla con error SQL | Migración previa incompleta o conexión interrumpida | Verificar que scripts 001–007 se ejecutaron sin errores. Revisar mensaje de error del RAISERROR. |
| `generate-qa-financial-ops.sh` falla con `"success": false` | Saldo insuficiente, entidad no encontrada, o token expirado | Leer respuesta del paso fallido. Verificar variables. Ver sección 18 de `docs/QA_FINANCIAL_OPERATIONS_API.md`. |
| Ledger desbalanceado (débitos ≠ créditos) | Error en lógica financiera o seed manual incorrecto | **Detener Fase 7.** Escalar a responsable técnico. No aprobar ciclo QA con este estado. |
| Dashboard no carga / error 500 en UI | Backend caído, CORS mal configurado, o token inválido | Revisar consola del navegador. Verificar `CORS__AllowedOrigins__0` y que el token no expiró. |
| Endpoint protegido sin token retorna 200 | Regresión en middleware de autenticación | **Bloqueo crítico de seguridad.** Detener todo. Escalar inmediatamente. No distribuir a usuarios internos. |
| Bug crítico abierto sin corrección | Funcionalidad esencial rota | No aprobar el ciclo. Crear ticket, asignar responsable, re-ejecutar ciclo después de la corrección. |

---

## 14. Cierre de ciclo QA

Completar al finalizar la ejecución de cada ciclo QA:

```
XPAY MVP — Acta de Cierre de Ciclo QA
======================================

Fecha inicio:         ___________
Fecha cierre:         ___________
Commit evaluado:      ___________
Ambiente:             [ ] Local   [ ] Azure QA   [ ] Otro: ___________
Responsable QA:       ___________
Responsable técnico:  ___________

Resultados:
  Casos totales:        35
  Aprobados:            ___
  Fallidos:             ___
  Bloqueados:           ___

Bugs encontrados:
  Críticos abiertos:    ___
  Altos abiertos:       ___
  Medios abiertos:      ___
  Bajos abiertos:       ___

Decisión:
  [ ] Aprobado
  [ ] Aprobado con observaciones: ___________
  [ ] No aprobado: ___________

Próximo paso:
  ___________

Firma responsable QA:       _________________
Firma responsable técnico:  _________________
```

---

## 15. Documentos relacionados

| Documento | Propósito en el ciclo QA |
|-----------|--------------------------|
| [`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md) | Alcance, versión, features incluidas/excluidas, riesgos y criterios formales |
| [`docs/QA_DEPLOYMENT_RUNBOOK.md`](QA_DEPLOYMENT_RUNBOOK.md) | Despliegue en Azure: recursos, variables de entorno, scripts SQL, rollback |
| [`docs/QA_OPERATIONS_VARIABLES.md`](QA_OPERATIONS_VARIABLES.md) | Preparación de variables locales: TOKEN, IDs, seguridad |
| [`database/008_seed_qa_dataset.sql`](../database/008_seed_qa_dataset.sql) | Seed QA: entidades base sin transacciones financieras |
| [`docs/QA_FINANCIAL_OPERATIONS_API.md`](QA_FINANCIAL_OPERATIONS_API.md) | Flujo financiero QA vía API: DTOs, curls, validación contable |
| [`docs/QA_MANUAL_TESTING.md`](QA_MANUAL_TESTING.md) | 35 casos de prueba: módulos, pasos, evidencias esperadas |
| [`docs/QA_EXECUTION_TEMPLATE.md`](QA_EXECUTION_TEMPLATE.md) | Plantilla de registro: matriz de casos, bugs, acta de cierre |
| [`README.md`](../README.md) | Descripción del proyecto: endpoints, configuración, fases |
| [`docs/QA_INTERNAL_CYCLE_01.md`](QA_INTERNAL_CYCLE_01.md) | Paquete de ejecución del primer ciclo QA interno: identificación, roles, plan, resultados y acta |

---

*Este checklist cubre el MVP XPAY — Fases 1 a 29. Actualizar cuando se agreguen nuevos módulos, flujos o criterios de aprobación.*
