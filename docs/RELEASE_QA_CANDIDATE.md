# XPAY MVP — Release QA Candidate v0.1

> ⚠️ **AVISO IMPORTANTE**  
> Esta versión es un **QA Candidate** para pruebas internas controladas.  
> **No es producción. No debe usarse con dinero real. No está autorizada para clientes externos.**

---

## 1. Resumen ejecutivo

Esta versión consolida el **MVP administrativo QA de XPAY** para su despliegue en ambiente QA y validación funcional, financiera y operativa por el equipo interno.

XPAY MVP QA Candidate v0.1 incluye:
- Un **backend financiero** (.NET 8) con wallets, pagos QR, liquidaciones, retiros y ledger.
- Un **panel administrativo** (React/Vite) para operar, consultar y gestionar transacciones.
- **Documentación completa** de despliegue QA, pruebas manuales y plantilla de ejecución.
- **CI validado** mediante GitHub Actions en cada commit de la rama `main`.

**Esta versión NO está autorizada para:**
- Operación con dinero real o saldos reales de usuarios.
- Despliegue en ambiente de producción.
- Acceso de usuarios externos o clientes finales.
- Integración con sistemas bancarios reales (Bre-B, PSE, ACH).

---

## 2. Identificación de versión

| Campo | Valor |
|-------|-------|
| **Nombre de versión** | XPAY MVP QA Candidate v0.1 |
| **Fecha** | 2026-06-17 |
| **Rama** | main |
| **Commit base** | f112ace |
| **Estado** | QA Candidate |
| **Responsable técnico** | Por definir |
| **Responsable QA** | Por definir |
| **Ambiente objetivo** | QA Azure |
| **Backend** | .NET 8 ASP.NET Core — Azure App Service |
| **Frontend** | React 18 / Vite 5 — Azure App Service o Static Web App |
| **Base de datos** | Azure SQL QA (XPAY_MVP_QA) |
| **CI** | GitHub Actions — Backend Validation + Frontend Build |

---

## 3. Estado de fases

| Fase | Nombre | Estado | Evidencia |
|------|--------|--------|-----------|
| 1 | Registro usuario final, login, wallet, recarga manual | ✅ Cerrada · CI verde | Backend Validation `main` |
| 2 | Transferencias XPAY a XPAY entre wallets | ✅ Cerrada · CI verde | Backend Validation `main` |
| 3 | Pago a comercio por código QR | ✅ Cerrada · CI verde | Backend Validation `main` |
| 4 | Liquidación de ventas QR al wallet del comercio | ✅ Cerrada · CI verde | Backend Validation `main` |
| 5 | Solicitud de retiro de saldo del comercio | ✅ Cerrada · CI verde | Backend Validation `main` |
| 6 | Gestión de retiros: confirmar pago y rechazar | ✅ Cerrada · CI verde | Backend Validation `main` |
| 7 | Reportes transaccionales (4 endpoints) | ✅ Cerrada · CI verde | Backend Validation `main` |
| 8 | Seguridad JWT: `[Authorize]`, 401, roles | ✅ Cerrada · CI verde | Backend Validation `main` |
| 9 | Health check, versión API, Swagger con JWT Bearer | ✅ Cerrada · CI verde | Backend Validation `main` |
| 10 | CORS, configuración por ambiente, preparación QA | ✅ Cerrada · CI verde | Backend Validation `main` |
| 11 | Frontend: login, auth, rutas privadas, layout | ✅ Cerrada · CI verde · Documentada | Frontend Build `main` |
| 12 | Frontend: páginas wallet y comercio | ✅ Cerrada · CI verde · Documentada | Frontend Build `main` |
| 13 | Frontend: gestión de retiros | ✅ Cerrada · CI verde · Documentada | Frontend Build `main` |
| 14 | Admin: listado wallets y listado comercios | ✅ Cerrada · CI verde · Documentada | Frontend Build `main` |
| 15 | Admin: listado ventas QR y listado ledger | ✅ Cerrada · CI verde · Documentada | Frontend Build `main` |
| 16 | Dashboard operativo con métricas y tablas | ✅ Cerrada · CI verde · Documentada | Frontend Build `main` |
| 17 | Documentación despliegue QA | ✅ Cerrada · CI verde · Documentada | `docs/QA_DEPLOYMENT.md` |
| 18 | Manejo de sesión expirada y errores frontend | ✅ Cerrada · CI verde · Documentada | Frontend Build `main` |
| 19 | Manual de pruebas QA (35 casos) | ✅ Cerrada · CI verde · Documentada | `docs/QA_MANUAL_TESTING.md` |
| 20 | Plantilla de ejecución y evidencias QA | ✅ Cerrada · CI verde · Documentada | `docs/QA_EXECUTION_TEMPLATE.md` |

---

## 4. Funcionalidades incluidas

### Backend (.NET 8)

| Funcionalidad | Endpoint(s) principal(es) |
|---------------|--------------------------|
| Registro de usuario final con wallet | `POST /api/usuarios/registro-final` |
| Login JWT | `POST /api/auth/login` |
| Consulta de wallet por persona | `GET /api/wallets/persona/{id}` |
| Saldo de wallet | `GET /api/wallets/{id}/saldo` |
| Movimientos de wallet | `GET /api/wallets/{id}/movimientos` |
| Recarga manual de saldo | `POST /api/wallets/{id}/recarga-manual` |
| Transferencia XPAY a XPAY | `POST /api/wallets/transferencia` |
| Pago QR a comercio | `POST /api/qr/pagar` |
| Liquidación de venta QR | `POST /api/comercios/liquidar-venta-qr` |
| Solicitud de retiro de comercio | `POST /api/comercios/solicitar-retiro` |
| Confirmar pago de retiro | `POST /api/comercios/retiros/confirmar-pago` |
| Rechazar retiro | `POST /api/comercios/retiros/rechazar` |
| Reporte: estado de cuenta wallet | `GET /api/reportes/wallet/{id}/estado-cuenta` |
| Reporte: resumen financiero comercio | `GET /api/reportes/comercios/{id}/resumen` |
| Reporte: detalle transacción ledger | `GET /api/reportes/ledger/transaccion/{id}` |
| Reporte: resumen general del sistema | `GET /api/reportes/operaciones/resumen-general` |
| Health check | `GET /health` |
| Versión API | `GET /api/version` |
| Swagger UI con JWT Bearer | `GET /swagger` |
| Listado admin de wallets (con filtros) | `GET /api/admin/wallets` |
| Listado admin de comercios (con filtros) | `GET /api/admin/comercios` |
| Listado admin de ventas QR (con filtros) | `GET /api/admin/ventas-qr` |
| Listado admin de transacciones ledger | `GET /api/admin/ledger-transacciones` |
| Listado de retiros (con filtros) | `GET /api/comercios/retiros` |

### Frontend (React 18 / Vite 5)

| Funcionalidad | Ruta |
|---------------|------|
| Login con JWT | `/login` |
| Dashboard operativo con métricas y tablas | `/dashboard` |
| Listado de wallets con filtros | `/wallets/listado` |
| Estado de cuenta de una wallet | `/wallets/:idWallet` |
| Listado de comercios con filtros | `/comercios/listado` |
| Resumen financiero de un comercio | `/comercios/:idComercio` |
| Listado de retiros con filtros | `/retiros/listado` |
| Búsqueda de retiro por ID | `/retiros` |
| Gestión de retiro: confirmar / rechazar | `/retiros/:idRetiro` |
| Listado de ventas QR con filtros | `/ventas-qr/listado` |
| Listado de ledger con filtros | `/ledger/listado` |
| Detalle de transacción ledger | `/ledger/:idTransaccion` |
| Manejo de sesión expirada (banner + redirect) | Global — AuthContext |
| Visualización de API actual en login y header | `/login` + Layout |
| Botón ↺ Reintentar en secciones con error | Dashboard |
| Cierre de sesión explícito | Layout |

### QA y Documentación

| Documento | Propósito |
|-----------|-----------|
| `docs/QA_DEPLOYMENT.md` | Guía completa de despliegue en Azure QA |
| `docs/QA_MANUAL_TESTING.md` | 35 casos de prueba manuales con criterios de aprobación |
| `docs/QA_EXECUTION_TEMPLATE.md` | Plantilla de ejecución, evidencias, bugs y acta de cierre |

---

## 5. Funcionalidades NO incluidas todavía

Las siguientes funcionalidades están **fuera del alcance de esta versión**. Su ausencia es intencional y no debe tratarse como un bug.

| Categoría | Funcionalidad no incluida |
|-----------|--------------------------|
| **Integración bancaria** | Bre-B real, PSE real, ACH real, dispersión bancaria real a cuentas externas |
| **Seguridad avanzada** | OTP, MFA (autenticación de dos factores), verificación por SMS/email |
| **KYC** | Verificación de identidad real (biometría, validación contra registros oficiales) |
| **Conciliación** | Conciliación bancaria automática, cuadre contra estado de cuenta bancaria |
| **Operaciones avanzadas** | Reversos y anulaciones completas, carga masiva de datos |
| **Notificaciones** | Push notifications, email transaccional, SMS |
| **Gestión de usuarios** | Gestión avanzada de roles y permisos desde el frontend admin |
| **Observabilidad** | APM, alertas automáticas, dashboards de monitoreo en tiempo real |
| **Producción** | Ningún componente está preparado, auditado ni autorizado para producción |
| **Usuarios finales** | No hay app de usuario final; solo el panel administrativo |
| **Dinero real** | Sin transacciones reales, sin saldos reales, sin cuentas bancarias reales |

---

## 6. Arquitectura QA esperada

```
┌─────────────────────────────────────────────────────────────┐
│                       Azure (QA)                            │
│                                                             │
│  ┌─────────────────────┐    ┌──────────────────────────┐   │
│  │ App Service          │    │ App Service / Static      │   │
│  │ xpay-api-qa         │◄──►│ Web App                   │   │
│  │ .NET 8              │    │ React/Vite (build)        │   │
│  └──────────┬──────────┘    └──────────────────────────┘   │
│             │                                               │
│  ┌──────────▼──────────┐                                    │
│  │ Azure SQL            │                                   │
│  │ XPAY_MVP_QA         │                                    │
│  │ (7 migraciones)     │                                    │
│  └─────────────────────┘                                    │
└─────────────────────────────────────────────────────────────┘
         ▲
         │  GitHub Actions CI
         │  Backend Validation + Frontend Build
```

- **Variables de entorno:** configuradas en Azure App Settings con doble guión bajo (`__`) como separador de jerarquía.
- **CORS:** backend configurado para aceptar únicamente el origen del frontend QA.
- **JWT:** clave secreta ≥ 32 caracteres, configurada solo en Azure App Settings — nunca en el repo.

Ver guía detallada: [`docs/QA_DEPLOYMENT.md`](QA_DEPLOYMENT.md)

---

## 7. Prerrequisitos para desplegar QA

Verificar cada ítem antes de iniciar el despliegue:

- [ ] Azure Resource Group creado para el ambiente QA
- [ ] Azure SQL Server creado en el Resource Group
- [ ] Base de datos `XPAY_MVP_QA` creada en Azure SQL
- [ ] Scripts SQL ejecutados en orden estricto: `001` → `002` → `003` → `004` → `005` → `006` → `007`
- [ ] Backend Azure App Service creado (`xpay-api-qa` o nombre equivalente)
- [ ] Variables del backend configuradas en Azure App Settings (ver `docs/QA_DEPLOYMENT.md` sección 3)
- [ ] Frontend Azure App Service o Static Web App creado
- [ ] `VITE_API_BASE_URL` definido apuntando al backend QA
- [ ] CORS en backend configurado para aceptar el origen del frontend QA
- [ ] `Jwt__Key` QA configurada con mínimo 32 caracteres — **diferente a cualquier clave de producción**
- [ ] Ningún secreto real subido al repositorio (`git log` verificado)
- [ ] GitHub Actions Backend Validation en verde para el commit a desplegar
- [ ] GitHub Actions Frontend Build en verde para el commit a desplegar

---

## 8. Checklist técnico antes de declarar QA listo

Ejecutar en el ambiente QA desplegado antes de abrir a usuarios internos:

- [ ] **Backend Validation** en GitHub Actions: `completed success`
- [ ] **Frontend Build** en GitHub Actions: `completed success`
- [ ] `GET /health` → HTTP 200
- [ ] `GET /api/version` → HTTP 200 con nombre y versión correctos
- [ ] `/swagger` → UI carga con botón Authorize
- [ ] Login con usuario de prueba → redirige a `/dashboard`
- [ ] `GET /api/wallets/persona/1` sin token → HTTP 401 (no 200, no 500)
- [ ] Dashboard carga con métricas y 3 tablas sin errores de consola
- [ ] Listado de wallets carga con datos
- [ ] Listado de comercios carga con datos
- [ ] Retiro PENDIENTE puede gestionarse (confirmar o rechazar)
- [ ] Ledger muestra transacciones; débitos y créditos son consistentes
- [ ] `docs/QA_MANUAL_TESTING.md` disponible para el equipo QA
- [ ] `docs/QA_EXECUTION_TEMPLATE.md` disponible para registrar la ejecución

---

## 9. Riesgos conocidos

| # | Riesgo | Impacto | Mitigación |
|---|--------|---------|------------|
| R-01 | Datos seed insuficientes en la BD QA | Casos QA no ejecutables; pruebas bloqueadas | Ejecutar `scripts/validate-backend.sh` localmente o insertar datos mínimos según prerrequisitos de `QA_MANUAL_TESTING.md` |
| R-02 | CORS mal configurado | Frontend no puede llamar al backend; error de red en todas las páginas | Verificar `Cors__AllowedOrigins__0` en Azure App Settings apunte al origen exacto del frontend QA |
| R-03 | JWT Key incorrecta o menor a 32 caracteres | Login falla con HTTP 500; el backend no arranca correctamente | Usar clave aleatoria ≥ 32 chars en `Jwt__Key`; validar en `/swagger` antes de abrir QA |
| R-04 | Migraciones SQL no ejecutadas o ejecutadas en orden incorrecto | HTTP 500 en todos los endpoints que acceden a la BD | Ejecutar scripts 001→007 en orden estricto; verificar con `SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES` |
| R-05 | Frontend apuntando a backend incorrecto | Datos de otra instancia, posibles errores CORS silenciosos | Verificar `API:` en la pantalla de login del frontend QA antes de cualquier prueba |
| R-06 | Ambiente QA confundido con producción | Pruebas con datos reales, expectativa errónea de funcionalidad completa | URL QA claramente diferenciada; comunicado interno que aclare que es ambiente de pruebas |
| R-07 | Operadores prueban con datos reales de clientes | Riesgo legal y de privacidad | Comunicado formal antes de iniciar QA: "solo datos ficticios, sin dinero real, sin datos de clientes" |
| R-08 | Falta de MFA en el panel admin | Acceso no autorizado si las credenciales de prueba se filtran | Acceso restringido a red interna o VPN durante QA; rotar credenciales después de cada ciclo |
| R-09 | Falta de integración bancaria real | Retiros se confirman en el sistema pero no se pagan realmente; puede generar confusión | Documentado explícitamente en "funcionalidades no incluidas"; capacitar al equipo QA antes de iniciar |
| R-10 | Validaciones financieras en escenarios extremos | Posible inconsistencia de saldo con flujos no previstos (saldo negativo, overflow) | Pruebas con datos controlados; no ejecutar volúmenes extremos en QA; reportar cualquier inconsistencia como bug crítico |

---

## 10. Criterios para pasar a usuarios internos

El sistema está listo para pruebas con usuarios internos cuando se cumplen **todos** los criterios:

| # | Criterio | Responsable de verificar |
|---|----------|--------------------------|
| C-01 | GitHub Actions Backend Validation en `completed success` para el commit a desplegar | Responsable técnico |
| C-02 | GitHub Actions Frontend Build en `completed success` para el commit a desplegar | Responsable técnico |
| C-03 | Despliegue QA exitoso en Azure (backend + frontend + BD) | Responsable técnico |
| C-04 | Ejecución QA documentada en `QA_EXECUTION_TEMPLATE.md` con resultados reales | Responsable QA |
| C-05 | 100% de los 8 casos Críticos con estado ✅ Aprobado | Responsable QA |
| C-06 | ≥ 90% de los 21 casos Altos con estado ✅ Aprobado (mínimo 19 de 21) | Responsable QA |
| C-07 | Cero bugs Críticos abiertos sin commit de solución verificado | Responsable QA |
| C-08 | Cero errores financieros: ledger balanceado, saldos de wallets coherentes con movimientos | Responsable QA |
| C-09 | Cero endpoints protegidos accesibles sin token (QA-06 en estado ✅) | Responsable QA |
| C-10 | Acta de cierre QA firmada por responsable técnico y responsable QA | Ambos |

---

## 11. Plan sugerido de ejecución

Seguir este orden garantiza que los errores se detecten en la etapa más temprana posible:

1. **Preparar ambiente QA** — crear recursos Azure, ejecutar migraciones SQL, configurar variables de entorno.
2. **Desplegar backend** — subir el build `.NET 8`, verificar que el servicio arranca sin errores en los logs.
3. **Probar backend público** — `GET /health`, `GET /api/version`, `/swagger`; confirmar que responden correctamente.
4. **Desplegar frontend** — subir el build Vite, configurar `VITE_API_BASE_URL`, verificar CORS.
5. **Probar login** — iniciar sesión con usuario de prueba; confirmar que el dashboard carga y que `API:` en pantalla apunta al backend QA correcto.
6. **Ejecutar QA_MANUAL_TESTING.md** — seguir los casos QA-01 a QA-35 en orden A → I.
7. **Registrar resultados en QA_EXECUTION_TEMPLATE.md** — completar estado, evidencias y bugs por caso.
8. **Corregir bugs críticos** — el equipo técnico entrega commits con fixes; registrar commit de solución.
9. **Reprobar casos corregidos** — ejecutar nuevamente los casos afectados; actualizar estado a ✅ si pasan.
10. **Completar resumen ejecutivo** — calcular porcentajes y tomar decisión final con los criterios de la sección 10.
11. **Firmar acta QA** — responsable QA y responsable técnico firman el acta de cierre.
12. **Autorizar usuarios internos** — con el acta firmada, comunicar al equipo que el ambiente QA está disponible.

---

## 12. Documentos relacionados

| Documento | Propósito |
|-----------|-----------|
| [`README.md`](../README.md) | Descripción del backend: endpoints, configuración, fases completadas |
| [`frontend/xpay-admin/README.md`](../frontend/xpay-admin/README.md) | Configuración del frontend: instalación, rutas, autenticación, manejo de errores |
| [`docs/QA_DEPLOYMENT.md`](QA_DEPLOYMENT.md) | Guía paso a paso de despliegue en Azure QA |
| [`docs/QA_MANUAL_TESTING.md`](QA_MANUAL_TESTING.md) | 35 casos de prueba manuales, prerrequisitos y criterios de aprobación |
| [`docs/QA_EXECUTION_TEMPLATE.md`](QA_EXECUTION_TEMPLATE.md) | Plantilla de registro de ejecución, evidencias, bugs y acta de cierre |
| [`docs/QA_DEPLOYMENT_RUNBOOK.md`](QA_DEPLOYMENT_RUNBOOK.md) | Runbook operativo: recursos Azure, variables, scripts SQL, smoke test y rollback |

---

## 13. Declaración de release

```
═══════════════════════════════════════════════════════════════════
         DECLARACIÓN DE RELEASE QA CANDIDATE
═══════════════════════════════════════════════════════════════════

XPAY MVP QA Candidate v0.1 queda listo para ser desplegado en
ambiente QA, siempre que se configuren correctamente los recursos
Azure, las variables de entorno y la base de datos QA.

Esta versión:
  ✅ Consolida las Fases 1 a 20 del proyecto XPAY MVP.
  ✅ Ha sido validada por GitHub Actions en cada commit de main.
  ✅ Incluye documentación de despliegue, pruebas y ejecución QA.

  ❌ NO está autorizada para producción.
  ❌ NO debe usarse con dinero real.
  ❌ NO está disponible para clientes externos.
  ❌ NO incluye integración bancaria real.

Commit base: f112ace
Rama: main
Fecha: 2026-06-17
═══════════════════════════════════════════════════════════════════
```
