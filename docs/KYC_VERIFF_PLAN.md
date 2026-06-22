# KYC con Veriff — Plan de Integración XPAY

> **Estado:** Fase 62 completada (sesión Veriff sandbox real + botón Mi Wallet).
> **Próximo:** Fase 63 — Webhook HMAC-SHA256 + decisión automática.
> **Ambiente:** QA/Demo únicamente. Sin dinero real. Sin producción.

---

## 1. Contexto

XPAY requiere verificación de identidad (KYC) de usuarios wallet antes de habilitar operaciones en producción. El proveedor seleccionado es **Veriff** (verificación de documentos + biométrica).

Esta integración se prepara en fases:

| Fase | Descripción                                  | Estado       |
|------|----------------------------------------------|--------------|
| 61   | Modelo KYC, endpoints QA, UI básica          | ✅ Completada |
| 62   | Sesión Veriff sandbox real + botón Mi Wallet | ✅ Completada |
| 63   | Webhook HMAC-SHA256 + decisión automática    | Pendiente    |
| 64   | Restricciones por estado KYC en operaciones  | Pendiente    |
| 65   | Producción + datos reales                    | Pendiente    |

---

## 2. Estados KYC

| Estado       | Significado                                               |
|--------------|-----------------------------------------------------------|
| `NO_INICIADO`| El usuario no ha iniciado ninguna verificación            |
| `PENDIENTE`  | Sesión Veriff creada, usuario aún no la completó          |
| `EN_REVISION`| Veriff recibió la verificación, está siendo revisada      |
| `APROBADO`   | Identidad verificada exitosamente                         |
| `RECHAZADO`  | Verificación rechazada (documento inválido, fraude, etc.) |
| `EXPIRADO`   | La sesión o verificación caducó                           |
| `ERROR`      | Error técnico durante el proceso                          |

---

## 3. Flujo futuro completo (Fase 62–63)

```
Usuario inicia KYC
      │
      ▼
POST /api/kyc/veriff/session
      │  Backend crea sesión en Veriff API
      │  Responde { sessionId, sessionUrl }
      │
      ▼
Frontend abre sessionUrl (redirect o iframe)
      │
      ▼
Usuario completa verificación en Veriff
      │
      ▼
Veriff envía webhook a POST /api/kyc/veriff/webhook
      │  Backend valida firma HMAC-SHA256 con VERIFF_WEBHOOK_SECRET
      │  Backend actualiza kyc_verificaciones + usuarios.estado_kyc_actual
      │
      ▼
Estado visible en Mi Wallet (con polling o push)
```

---

## 4. Modelo de datos (implementado en Fase 61)

### Tabla `kyc_verificaciones`

| Columna              | Tipo           | Descripción                                   |
|----------------------|----------------|-----------------------------------------------|
| `id_kyc_verificacion`| BIGINT PK      | Identificador único                           |
| `id_usuario`         | BIGINT FK      | Usuario dueño de la verificación              |
| `id_persona`         | BIGINT FK NULL | Persona asociada (para futuro enriquecimiento)|
| `proveedor`          | VARCHAR(50)    | `'VERIFF'` o `'SIMULACION_QA'`               |
| `estado_kyc`         | VARCHAR(30)    | Estado actual de esta verificación            |
| `session_id`         | VARCHAR(200)   | ID de sesión Veriff (Fase 62)                 |
| `session_url`        | VARCHAR(1000)  | URL de la sesión Veriff (Fase 62)             |
| `decision`           | VARCHAR(50)    | Decisión Veriff: approved/declined/resubmission|
| `reason`             | VARCHAR(500)   | Razón de la decisión                          |
| `vendor_data`        | VARCHAR(500)   | Metadata adicional (sin PII)                  |
| `es_actual`          | BIT            | `1` = registro vigente para este usuario      |
| `fecha_creacion`     | DATETIME2      | Timestamp de creación                         |
| `fecha_actualizacion`| DATETIME2      | Timestamp de última actualización             |
| `fecha_decision`     | DATETIME2      | Timestamp de decisión Veriff                  |

### Columnas en `usuarios`

| Columna                   | Tipo      | Descripción                             |
|---------------------------|-----------|-----------------------------------------|
| `estado_kyc_actual`       | VARCHAR(30)| Resumen rápido — DEFAULT `'NO_INICIADO'`|
| `fecha_kyc_actualizacion` | DATETIME2 | Timestamp del último cambio de estado   |

---

## 5. Endpoints implementados (Fase 61)

### `GET /api/kyc/mi-estado`
- Requiere: `[Authorize]` (cualquier usuario autenticado)
- Respuesta: `{ estadoKyc, fechaActualizacion, nota }`
- No expone session_id, session_url, ni datos Veriff
- Lee de `usuarios.estado_kyc_actual` (lookup sin JOIN)

### `POST /api/kyc/qa/simular-estado`
- Requiere: `[Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]`
- Solo QA/Demo
- Body: `{ "usuario": "qa.usuario1", "estadoKyc": "APROBADO" }`
- Usuarios permitidos: `qa.usuario1`, `qa.usuario2`
- Estados válidos: todos los del enum KYC
- Registra en `kyc_verificaciones` con `proveedor = 'SIMULACION_QA'`
- Actualiza `usuarios.estado_kyc_actual`

### `POST /api/kyc/veriff/session` ✅ Fase 62
- Requiere: `[Authorize]` (cualquier usuario autenticado)
- Lee `VERIFF_API_KEY`, `VERIFF_SHARED_SECRET`, `VERIFF_BASE_URL` desde Azure App Settings
- Si falta config → `503 "Veriff sandbox no configurado. Contacta al administrador."`
- Payload a Veriff: `{ verification: { callback, vendorData: "XPAY-QA-USUARIO-{id}", timestamp } }`
- Sin datos personales reales — vendorData es el único identificador
- Guarda en `kyc_verificaciones`: `proveedor=VERIFF`, `estado_kyc=PENDIENTE`, `session_id`, `session_url`
- Actualiza `usuarios.estado_kyc_actual = 'PENDIENTE'`
- Responde: `{ success: true, data: { estadoKyc, sessionId, sessionUrl } }`
- API key y shared secret **nunca** en respuesta ni en logs

### `POST /api/kyc/veriff/webhook` (stub seguro — pendiente Fase 63)
- No requiere auth (webhook externo de Veriff)
- Devuelve `200 { received: true }` sin procesar nada
- Fase 63: añadir validación HMAC-SHA256 con `VERIFF_SHARED_SECRET` + actualización de estado

---

## 6. Cómo simular estado KYC en QA

**Prerrequisito:** Sesión activa como `qa.admin.xpay` (rol `ADMIN_XPAY`).

```bash
# 1. Login como admin QA
TOKEN=$(curl -s -X POST https://xpay-api-qa.azurewebsites.net/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"usuario":"qa.admin.xpay","password":"XpayDemo2026!"}' \
  | jq -r '.data.token')

# 2. Simular APROBADO para qa.usuario1
curl -s -X POST https://xpay-api-qa.azurewebsites.net/api/kyc/qa/simular-estado \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"usuario":"qa.usuario1","estadoKyc":"APROBADO"}'

# 3. Verificar estado como qa.usuario1
TOKEN2=$(curl -s -X POST https://xpay-api-qa.azurewebsites.net/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"usuario":"qa.usuario1","password":"XpayDemo2026!"}' \
  | jq -r '.data.token')

curl -s https://xpay-api-qa.azurewebsites.net/api/kyc/mi-estado \
  -H "Authorization: Bearer $TOKEN2"
```

---

## 7. Reglas de negocio futuras (Fase 64)

| Estado KYC    | Regla                                                      |
|---------------|------------------------------------------------------------|
| `NO_INICIADO` | Advertencia visible; en producción bloquea envíos > límite |
| `PENDIENTE`   | Advertencia: "Verificación en curso"                       |
| `EN_REVISION` | Advertencia: "En revisión — espera la decisión"            |
| `APROBADO`    | Operaciones habilitadas sin restricción adicional          |
| `RECHAZADO`   | Bloquear operaciones sensibles; solicitar nueva verificación|
| `EXPIRADO`    | Solicitar nueva verificación; operaciones restringidas     |
| `ERROR`       | Solicitar reintento; soporte disponible                    |

---

## 8. Seguridad

| Control                          | Implementación                                          |
|----------------------------------|---------------------------------------------------------|
| API Key Veriff                   | Azure App Settings — NUNCA en repositorio              |
| Webhook secret (HMAC)            | Azure App Settings — `VERIFF_WEBHOOK_SECRET`           |
| Validación de firma webhook      | HMAC-SHA256 (Fase 63)                                  |
| Datos biométricos                | No almacenados en XPAY — Veriff los gestiona           |
| Minimización de datos            | Solo session_id, decision, reason en kyc_verificaciones|
| Auditoría                        | Tabla `auditoria` para cada cambio de estado           |
| Rate limiting simulación QA      | `[Authorize(Roles="ADMIN_XPAY,SUPERUSUARIO")]`         |

---

## 9. Cumplimiento Colombia (Habeas Data)

Requerimientos para producción:

- [ ] Política de Privacidad actualizada con tratamiento de datos biométricos
- [ ] Autorización explícita del usuario antes de iniciar KYC
- [ ] Definición del período de retención de datos KYC
- [ ] Procedimiento de eliminación de datos a solicitud del usuario
- [ ] Registro de consentimiento (timestamp, versión de política aceptada)
- [ ] Contacto designado para solicitudes de datos personales
- [ ] Cumplimiento Ley 1581 de 2012 y Decreto 1377 de 2013

---

## 10. Pendientes para Fase 63

- [ ] Validación HMAC-SHA256 en webhook con `VERIFF_SHARED_SECRET`
- [ ] Actualizar `kyc_verificaciones.estado_kyc` y `kyc_verificaciones.decision` según decisión Veriff
- [ ] Actualizar `usuarios.estado_kyc_actual` cuando llega decisión (APROBADO / RECHAZADO / EN_REVISION)
- [ ] Probar webhook con evento real desde dashboard Veriff sandbox
- [ ] Considerar polling de `GET /api/kyc/mi-estado` en frontend para detectar cambio de estado post-Veriff

### Variables Azure requeridas (ya configuradas en xpay-api-qa)

| Variable              | Uso                                                       |
|-----------------------|-----------------------------------------------------------|
| `VERIFF_API_KEY`      | Header `X-AUTH-CLIENT` en llamada a Veriff `/v1/sessions` |
| `VERIFF_SHARED_SECRET`| Validación HMAC-SHA256 webhook (Fase 63)                  |
| `VERIFF_BASE_URL`     | Base URL Veriff sandbox (ej. `https://stationapi.veriff.com`) |

### Cómo iniciar verificación desde Mi Wallet

1. Ir a `https://xpay-admin-qa.azurewebsites.net` → login como `qa.usuario1`
2. Asegurarse de que el estado KYC sea `NO_INICIADO` (o `RECHAZADO`/`EXPIRADO`/`ERROR`)
3. Hacer clic en **"Iniciar verificación"** en la sección de identidad
4. Backend crea sesión Veriff sandbox → estado pasa a `PENDIENTE`
5. Frontend muestra "Verificación iniciada. Continúa en Veriff." y redirige a `sessionUrl`
6. Completar verificación con documento de prueba Veriff (sandbox)
7. Veriff envía webhook → Fase 63 procesará la decisión

---

## 11. Restricciones permanentes

- **Sin dinero real** — QA/Demo únicamente hasta Fase 65
- **Sin producción** — nunca configurar keys reales en código
- **Sin datos personales reales** — solo CC ficticias (9000000xx)
- **Sin almacenar documentos** — Veriff los gestiona, XPAY solo recibe decisión
