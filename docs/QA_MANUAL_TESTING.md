# XPAY MVP — Manual de Pruebas QA Operativas

**Versión:** 1.0  
**Fecha:** 2026-06-17  
**Estado:** Activo  

---

## 1. Objetivo del documento

Este manual sirve para validar el MVP XPAY en ambiente local o QA antes de pruebas con usuarios internos. Describe, en orden lógico, todos los módulos a probar, los pasos exactos a seguir, el resultado esperado en cada caso y la evidencia que debe capturarse.

Una persona sin acceso al código puede ejecutar este manual con las credenciales y datos correctos, y determinar si el sistema está listo para usarse.

---

## 2. Alcance de pruebas

| Módulo | Descripción |
|--------|-------------|
| **Backend público** | Health check, versión API y Swagger |
| **Login y sesión** | Autenticación JWT, tokens, cierre de sesión, manejo de expiración |
| **Dashboard** | Métricas generales, accesos rápidos, últimas tablas, retry |
| **Wallets** | Listado, filtros, estado de cuenta, movimientos |
| **Comercios** | Listado, filtros, resumen financiero |
| **Retiros** | Listado, filtros, consulta, confirmar pago, rechazar |
| **Ventas QR** | Listado, filtros, navegación a comercio |
| **Ledger** | Listado, filtros, detalle de transacción, consistencia débito/crédito |
| **Seguridad JWT** | Acceso sin token → 401, token inválido → 401 |
| **Manejo de errores frontend** | Sesión expirada, backend no disponible, indicador de API |

---

## 3. Prerrequisitos

Antes de iniciar las pruebas, verificar que todo lo siguiente está en orden:

### 3.1 Infraestructura

- [ ] Backend `.NET 8` corriendo en `http://localhost:5000` o en la URL de QA Azure.
- [ ] Frontend React/Vite corriendo en `http://localhost:5173` o desplegado en Azure.
- [ ] SQL Server accesible desde el backend.
- [ ] Base de datos `XPAY_MVP` migrada con los 7 scripts en orden:

```
001_security_identity.sql
002_wallet_ledger.sql
003_comercios_qr.sql
004_liquidacion_qr.sql
005_retiros_comercio.sql
006_gestion_retiros_comercio.sql
007_security_roles_jwt.sql
```

### 3.2 Configuración frontend

- [ ] Archivo `.env` del frontend con `VITE_API_BASE_URL` apuntando al backend correcto.
- [ ] CORS configurado en backend para aceptar el origen del frontend.

### 3.3 Datos seed

Los siguientes datos deben existir en la base de datos antes de probar. Se generan ejecutando el script de validación del CI (`scripts/validate-backend.sh`) o con el dataset QA:

**Script de seed QA recomendado:** `database/008_seed_qa_dataset.sql`

Ejecutar después de los scripts 001–007. Crea personas, usuarios, wallets, comercio demo QA (`Comercio Demo XPAY QA`) y QR demo QA (`QR-DEMO-XPAY-QA-001`) de forma idempotente. No contiene datos reales ni dinero real. No ejecutar en producción.

Después del seed, generar datos financieros (saldos, ventas QR, retiros, ledger) vía endpoints del backend siguiendo la guía: **[`docs/QA_FINANCIAL_OPERATIONS_API.md`](QA_FINANCIAL_OPERATIONS_API.md)**.

Para automatizar este paso, usar el script auxiliar **[`scripts/generate-qa-financial-ops.sh`](../scripts/generate-qa-financial-ops.sh)** con las variables de entorno requeridas (ver sección de uso en el propio script). Ejecutar después del seed QA, con backend activo y token JWT válido.

| Dato | Descripción | Dónde verificar |
|------|-------------|-----------------|
| **Usuario admin de prueba** | `usuario: admin_test`, contraseña definida en appsettings | `POST /api/auth/login` |
| **Usuarios QA** | `qa.admin.xpay`, `qa.operador.xpay`, `qa.usuario1`, `qa.usuario2` (creados por 008, requieren hash actualizado para login) | `POST /api/auth/login` |
| **Persona y wallet de usuario** | Al menos una wallet activa con saldo > 0 | `GET /api/admin/wallets` |
| **Comercio Demo XPAY** | Comercio activo base (creado por migración 003) | `GET /api/admin/comercios` |
| **Comercio Demo XPAY QA** | Comercio QA separado (creado por 008) con tienda y QR `QR-DEMO-XPAY-QA-001` | `GET /api/admin/comercios` |
| **QR de comercio** | Al menos un QR activo asociado al comercio demo | `POST /api/qr/pagar` |
| **Ventas QR** | Al menos 3 ventas QR (estados mixtos) | `GET /api/admin/ventas-qr` |
| **Retiros** | Al menos 1 retiro PENDIENTE y 1 PAGADO | `GET /api/comercios/retiros` |
| **Ledger** | Al menos 5 transacciones de diferentes tipos | `GET /api/admin/ledger-transacciones` |

### 3.4 Herramientas recomendadas

- **Navegador:** Google Chrome o Firefox (último estable)
- **DevTools:** F12 → pestaña Network para verificar status codes HTTP
- **Herramienta REST:** Postman o Bruno para pruebas de backend directo
- **Capturas:** herramienta de screenshot del sistema operativo

---

## 4. Orden sugerido de pruebas

Seguir este orden garantiza que las dependencias entre módulos estén cubiertas antes de probar funcionalidades compuestas.

```
A. Backend público     → sin autenticación
B. Autenticación       → obtener token
C. Dashboard           → vista general del sistema
D. Wallets             → estado de cuenta
E. Comercios           → resumen financiero
F. Retiros             → gestión operativa (crítico)
G. Ventas QR           → trazabilidad de pagos
H. Ledger              → integridad financiera (crítico)
I. Sesión y errores    → robustez del frontend
```

---

### A. Backend público

Probar directamente en el navegador o con herramienta REST. No requiere token.

| Paso | Acción | Resultado esperado |
|------|--------|--------------------|
| A1 | `GET http://localhost:5000/health` | HTTP 200, body `{"status":"Healthy"}` o similar |
| A2 | `GET http://localhost:5000/api/version` | HTTP 200, body con nombre de API y versión |
| A3 | Abrir `http://localhost:5000/swagger` | Interfaz Swagger UI carga; hay botón **Authorize** para JWT |

---

### B. Autenticación

| Paso | Acción | Resultado esperado |
|------|--------|--------------------|
| B1 | Ir a `/login` en el frontend | Formulario visible; texto `API: http://localhost:5000` debajo del botón |
| B2 | Ingresar usuario y contraseña correctos → clic **Ingresar** | Redirige a `/dashboard` |
| B3 | Ir a `/login`, ingresar contraseña incorrecta → clic **Ingresar** | Error visible: credenciales inválidas |
| B4 | Con Postman: `GET /api/wallets/persona/1` sin header Authorization | HTTP 401 |

---

### C. Dashboard

| Paso | Acción | Resultado esperado |
|------|--------|--------------------|
| C1 | Cargar `/dashboard` con sesión activa | Página carga; se ven accesos rápidos, métricas y 3 tablas |
| C2 | Verificar 11 tarjetas de métricas | Total Wallets, Saldo Usuarios, Saldo Comercios, Ventas QR, QR Liquidadas, QR Contingencia, Retiros Pagados, Pendientes, Rechazados, Txs Ledger, Auditoría |
| C3 | Clic en **Wallets** (acceso rápido) | Navega a `/wallets/listado` |
| C4 | Clic en **Retiros** (acceso rápido) | Navega a `/retiros/listado` |
| C5 | Para simular error: desconectar backend → recargar dashboard | Secciones con error muestran botón **↺ Reintentar** |

---

### D. Wallets

| Paso | Acción | Resultado esperado |
|------|--------|--------------------|
| D1 | Ir a `/wallets/listado` | Tabla de wallets con columnas: ID, Tipo, Estado, Persona, Saldo; paginación visible |
| D2 | Aplicar filtro **Estado = ACTIVA** → clic Buscar | Solo aparecen wallets ACTIVAS |
| D3 | Aplicar filtro **Tipo = USUARIO** → clic Buscar | Solo aparecen wallets de tipo USUARIO |
| D4 | Clic **Ver** en cualquier wallet | Navega a `/wallets/:idWallet`; muestra saldo actual, movimientos y datos de la wallet |

---

### E. Comercios

| Paso | Acción | Resultado esperado |
|------|--------|--------------------|
| E1 | Ir a `/comercios/listado` | Tabla de comercios: ID, Nombre, NIT, Estado, Saldo wallet |
| E2 | Ingresar texto parcial del nombre → clic Buscar | Solo aparecen comercios que coinciden con el texto |
| E3 | Clic **Ver** en cualquier comercio | Navega a `/comercios/:idComercio` |
| E4 | En el resumen del comercio | Muestra ventas QR realizadas, retiros solicitados y saldo disponible |

---

### F. Retiros

| Paso | Acción | Resultado esperado |
|------|--------|--------------------|
| F1 | Ir a `/retiros/listado` | Tabla de retiros: ID, Comercio, Valor, Estado, Fecha solicitud |
| F2 | Filtrar por **Estado = PENDIENTE** → clic Buscar | Solo retiros PENDIENTES |
| F3 | Ir a `/retiros`, ingresar el ID de un retiro PENDIENTE → clic Consultar | Muestra detalle: valor, comercio, estado, fecha solicitud |
| F4 | En el retiro PENDIENTE: completar referencia pago → clic **Confirmar pago** | Estado cambia a PAGADO; mensaje de confirmación visible |
| F5 | Ir a `/retiros`, ingresar ID de otro retiro PENDIENTE → ingresar motivo → clic **Rechazar** | Estado cambia a RECHAZADO; mensaje de confirmación visible |
| F6 | Consultar el retiro ya PAGADO o RECHAZADO | Los botones de acción **no aparecen**; solo se muestra el estado final |

---

### G. Ventas QR

| Paso | Acción | Resultado esperado |
|------|--------|--------------------|
| G1 | Ir a `/ventas-qr/listado` | Tabla con columnas: ID, Comercio, Tienda, Valor bruto, Estado, Fecha venta |
| G2 | Filtrar por **Estado = LIQUIDADA** → clic Buscar | Solo ventas LIQUIDADAS |
| G3 | Clic **Ver comercio** en cualquier venta | Navega a `/comercios/:idComercio` del comercio correspondiente |

---

### H. Ledger

| Paso | Acción | Resultado esperado |
|------|--------|--------------------|
| H1 | Ir a `/ledger/listado` | Tabla con columnas: ID, Tipo transacción, Referencia tipo, Referencia ID, Valor total, Fecha |
| H2 | Filtrar por **Tipo = PAGO_QR** → clic Buscar | Solo transacciones de tipo PAGO_QR |
| H3 | Clic **Ver detalle** en cualquier transacción | Navega a `/ledger/:idTransaccion`; muestra detalle completo |
| H4 | Verificar consistencia de valores | Para cada PAGO_QR debe haber un débito en wallet usuario y un crédito en wallet comercio; los valores deben ser iguales |

---

### I. Sesión y errores

| Paso | Acción | Resultado esperado |
|------|--------|--------------------|
| I1 | Con sesión activa: clic **Cerrar sesión** en el header | Token eliminado de localStorage; redirige a `/login`; NO aparece mensaje de sesión expirada |
| I2 | En DevTools (Application → Local Storage): eliminar `xpay_token` manualmente → intentar navegar a `/dashboard` | Redirige a `/login`; `PrivateRoute` activa |
| I3 | En Postman: llamar endpoint con token inválido `Authorization: Bearer FAKE` | HTTP 401 |
| I4 | Verificar en header (con sesión activa) | Se ve `API: local` (en local) o el hostname de QA |
| I5 | Verificar en `/login` (sin sesión) | Se ve `API: http://localhost:5000` debajo del formulario |

---

## 5. Tabla de casos de prueba

> **Estado:** usar `⬜ Pendiente` / `✅ Pasó` / `❌ Falló` / `⚠️ Bloqueado`

| ID | Módulo | Caso de prueba | Pasos resumidos | Resultado esperado | Evidencia sugerida | Estado |
|----|--------|---------------|-----------------|-------------------|-------------------|--------|
| QA-01 | Backend público | Health check | `GET /health` | HTTP 200, status Healthy | Screenshot respuesta | ⬜ |
| QA-02 | Backend público | Versión API | `GET /api/version` | HTTP 200, nombre y versión en body | Screenshot respuesta | ⬜ |
| QA-03 | Backend público | Swagger UI | Abrir `/swagger` | UI carga con botón Authorize | Screenshot Swagger | ⬜ |
| QA-04 | Autenticación | Login exitoso | Credenciales correctas en `/login` | Redirige a `/dashboard` | Screenshot dashboard | ⬜ |
| QA-05 | Autenticación | Login fallido | Contraseña incorrecta en `/login` | Error visible en formulario | Screenshot error | ⬜ |
| QA-06 | Autenticación | Endpoint protegido sin token | `GET /api/wallets/persona/1` sin Auth header | HTTP 401 | Screenshot Postman | ⬜ |
| QA-07 | Autenticación | Cierre de sesión | Clic **Cerrar sesión** en header | Redirige a `/login`, sin mensaje de expiración | Screenshot `/login` | ⬜ |
| QA-08 | Dashboard | Carga inicial | Navegar a `/dashboard` con sesión | Página completa sin errores en consola | Screenshot dashboard | ⬜ |
| QA-09 | Dashboard | 11 métricas visibles | Ver sección de tarjetas | 11 cards con valores numéricos o monetarios | Screenshot cards | ⬜ |
| QA-10 | Dashboard | Acceso rápido Wallets | Clic QuickCard **Wallets** | Navega a `/wallets/listado` | Screenshot URL | ⬜ |
| QA-11 | Dashboard | Acceso rápido Retiros | Clic QuickCard **Retiros** | Navega a `/retiros/listado` | Screenshot URL | ⬜ |
| QA-12 | Dashboard | Botón Reintentar | Detener backend → recargar dashboard | Secciones con error muestran **↺ Reintentar** | Screenshot botón | ⬜ |
| QA-13 | Wallets | Listado sin filtros | Ir a `/wallets/listado` | Tabla con registros y paginación | Screenshot tabla | ⬜ |
| QA-14 | Wallets | Filtro por Estado | Seleccionar ACTIVA → Buscar | Solo wallets ACTIVAS en tabla | Screenshot tabla filtrada | ⬜ |
| QA-15 | Wallets | Filtro por Tipo | Seleccionar USUARIO → Buscar | Solo wallets tipo USUARIO | Screenshot tabla filtrada | ⬜ |
| QA-16 | Wallets | Estado de cuenta | Clic Ver → `/wallets/:id` | Saldo y movimientos de la wallet | Screenshot detalle | ⬜ |
| QA-17 | Comercios | Listado sin filtros | Ir a `/comercios/listado` | Tabla con nombre, NIT, estado, saldo | Screenshot tabla | ⬜ |
| QA-18 | Comercios | Filtro por texto | Ingresar nombre parcial → Buscar | Solo comercios con ese texto | Screenshot tabla filtrada | ⬜ |
| QA-19 | Comercios | Resumen financiero | Clic Ver → `/comercios/:id` | Ventas QR, retiros y saldo del comercio | Screenshot detalle | ⬜ |
| QA-20 | Comercios | Datos resumen | Verificar totales en resumen | Valores numéricos/monetarios coherentes | Screenshot resumen | ⬜ |
| QA-21 | Retiros | Listado sin filtros | Ir a `/retiros/listado` | Tabla con ID, valor, estado, fecha | Screenshot tabla | ⬜ |
| QA-22 | Retiros | Filtro PENDIENTE | Estado = PENDIENTE → Buscar | Solo retiros PENDIENTES | Screenshot tabla filtrada | ⬜ |
| QA-23 | Retiros | Buscar por ID | Ingresar ID en `/retiros` → Consultar | Detalle del retiro carga | Screenshot detalle | ⬜ |
| QA-24 | Retiros | Confirmar pago | Retiro PENDIENTE → ref. pago → Confirmar | Estado cambia a PAGADO | Screenshot antes y después | ⬜ |
| QA-25 | Retiros | Rechazar retiro | Retiro PENDIENTE → motivo → Rechazar | Estado cambia a RECHAZADO | Screenshot antes y después | ⬜ |
| QA-26 | Retiros | Sin acciones en gestionado | Consultar retiro PAGADO o RECHAZADO | No aparecen botones de acción | Screenshot sin botones | ⬜ |
| QA-27 | Ventas QR | Listado sin filtros | Ir a `/ventas-qr/listado` | Tabla con ID, comercio, valor, estado, fecha | Screenshot tabla | ⬜ |
| QA-28 | Ventas QR | Filtro por Estado | Estado = LIQUIDADA → Buscar | Solo ventas LIQUIDADAS | Screenshot tabla filtrada | ⬜ |
| QA-29 | Ventas QR | Navegar a comercio | Clic **Ver comercio** en una venta | Navega a `/comercios/:id` correcto | Screenshot resumen comercio | ⬜ |
| QA-30 | Ledger | Listado sin filtros | Ir a `/ledger/listado` | Tabla con tipo, referencia, valor, fecha | Screenshot tabla | ⬜ |
| QA-31 | Ledger | Filtro por tipo | Tipo = PAGO_QR → Buscar | Solo transacciones PAGO_QR | Screenshot tabla filtrada | ⬜ |
| QA-32 | Ledger | Detalle transacción | Clic **Ver detalle** → `/ledger/:id` | Detalle completo de la transacción | Screenshot detalle | ⬜ |
| QA-33 | Ledger | Consistencia financiera | Sumar débitos y créditos del listado | Total débitos = Total créditos (ledger balanceado) | Screenshot cálculo manual | ⬜ |
| QA-34 | Manejo de errores | Sesión expirada | Eliminar `xpay_token` de localStorage → navegar a ruta protegida | Redirige a `/login` con banner amarillo *"Tu sesión ha expirado..."* | Screenshot banner | ⬜ |
| QA-35 | Manejo de errores | Backend no disponible | Detener backend → intentar login | Mensaje claro: *"No fue posible conectar con el backend XPAY..."* | Screenshot mensaje | ⬜ |

**Total: 35 casos de prueba**

---

## 6. Evidencias sugeridas

Para cada caso de prueba que se ejecute, capturar la siguiente información:

| Campo | Descripción |
|-------|-------------|
| **Screenshot** | Captura de pantalla del estado del sistema en el momento de la prueba |
| **URL** | URL exacta que aparece en el navegador |
| **Usuario** | Nombre de usuario con el que se inició sesión |
| **Fecha y hora** | Timestamp de la prueba |
| **Resultado esperado** | Qué debería ocurrir según este manual |
| **Resultado obtenido** | Qué ocurrió realmente |
| **IDs relevantes** | `idRetiro`, `idWallet`, `idComercio`, `idTransaccionLedger` según aplique |
| **Status HTTP** | Código de respuesta del backend (visible en DevTools → Network) |
| **Observaciones** | Cualquier comportamiento inesperado aunque la prueba pase |

> **Sugerencia:** nombrar los screenshots con el ID del caso de prueba: `QA-24_confirmar_pago_antes.png`, `QA-24_confirmar_pago_despues.png`.

---

## 7. Criterios de aprobación QA

### 7.1 Severidades

| Nivel | Criterio |
|-------|----------|
| **Crítico** | Login y sesión, dashboard (métricas), endpoints protegidos (401 sin token), consulta de wallet, gestión de retiros (confirmar/rechazar), integridad del ledger |
| **Alto** | Listados con paginación, filtros funcionales, navegación entre módulos, mensajes de error |
| **Medio** | Textos, etiquetas, estilos visuales, badges de estado, formato de fechas y montos |

### 7.2 Reglas de aprobación

El sistema se considera **aprobado para pruebas con usuarios internos** cuando:

- ✅ **100%** de los casos **Críticos** pasan.
- ✅ **≥ 90%** de los casos **Altos** pasan.
- ✅ **Cero errores financieros**: débitos y créditos del ledger son consistentes; saldos de wallets cuadran con movimientos.
- ✅ **Cero endpoints protegidos accesibles sin token**: toda ruta que requiere `[Authorize]` devuelve 401 sin header.
- ⚠️ Los casos **Medios** pueden tener observaciones sin bloquear la aprobación.

### 7.3 Criterios de bloqueo (no puede pasar a usuarios internos si alguno falla)

| Código | Criterio de bloqueo |
|--------|---------------------|
| BLQ-01 | Login no funciona |
| BLQ-02 | Token JWT no es validado correctamente |
| BLQ-03 | Endpoint protegido accesible sin token |
| BLQ-04 | Confirmar o rechazar retiro no cambia estado |
| BLQ-05 | Ledger no refleja transacciones realizadas |
| BLQ-06 | Saldo de wallet incorrecto después de operación |
| BLQ-07 | Dashboard no carga ninguna métrica |

---

## 8. Plantilla de reporte de bug

Cuando un caso de prueba falla, documentarlo con este formato:

```
## Bug Report

**Título:**         [Resumen corto del problema, ej: "Login no redirige al dashboard"]
**Fecha:**          [YYYY-MM-DD HH:MM]
**Ambiente:**       [Local / QA Azure]
**URL afectada:**   [Ej: http://localhost:5173/login]
**Usuario:**        [Usuario utilizado durante la prueba]
**Módulo:**         [Login / Dashboard / Wallets / Comercios / Retiros / Ventas QR / Ledger / Seguridad]
**Caso de prueba:** [ID del caso, ej: QA-04]
**Severidad:**      [Crítico / Alto / Medio]

---

### Pasos para reproducir

1. [Paso 1]
2. [Paso 2]
3. [Paso 3]

---

### Resultado esperado

[Qué debería haber ocurrido según el manual]

---

### Resultado obtenido

[Qué ocurrió realmente]

---

### Evidencia

- Screenshot: [nombre del archivo adjunto]
- Status HTTP observado: [200 / 400 / 401 / 500 / etc.]
- Mensaje de error exacto: [copiar el texto exacto del mensaje]
- Consola del navegador: [si hay errores relevantes en DevTools → Console]

---

### Observaciones adicionales

[Contexto útil: ¿ocurre siempre o solo a veces? ¿con qué datos específicos?]
```

---

## 9. Checklist final de salida a QA usuarios internos

Marcar todos los puntos antes de abrir el ambiente a pruebas con usuarios internos:

- [ ] **Backend Validation** verde en GitHub Actions (`main`)
- [ ] **Frontend Build** verde en GitHub Actions (`main`)
- [ ] `GET /health` responde HTTP 200
- [ ] `GET /api/version` responde HTTP 200 con nombre y versión
- [ ] Frontend apunta al backend correcto (verificar `API:` en pantalla de login)
- [ ] Login con usuario de prueba → accede a `/dashboard`
- [ ] Dashboard muestra métricas y tablas sin errores de consola
- [ ] Listado de wallets carga con datos
- [ ] Listado de comercios carga con datos
- [ ] Listado de retiros carga; retiro PENDIENTE puede gestionarse
- [ ] Listado de ventas QR carga con datos
- [ ] Listado ledger carga; débitos y créditos consistentes
- [ ] CORS configurado: frontend puede llamar al backend sin errores de red
- [ ] Endpoint protegido sin token devuelve 401 (no 200, no 500)
- [ ] Sin secretos reales en el repositorio (`git log` no muestra keys ni passwords)
- [ ] Ambiente QA documentado en `docs/QA_DEPLOYMENT.md`

---

## 10. Relación con otros documentos

| Documento | Propósito |
|-----------|-----------|
| [`docs/QA_DEPLOYMENT.md`](QA_DEPLOYMENT.md) | Guía de despliegue en Azure App Service: variables, orden, troubleshooting |
| [`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md) | Declaración formal de la versión QA Candidate: alcance, riesgos y criterios de aprobación |
| [`README.md`](../README.md) | Descripción del backend: endpoints, configuración, estado del MVP |
| [`frontend/xpay-admin/README.md`](../frontend/xpay-admin/README.md) | Configuración del frontend: instalación, rutas, autenticación, manejo de errores |
| [`docs/QA_DEPLOYMENT_RUNBOOK.md`](QA_DEPLOYMENT_RUNBOOK.md) | Runbook operativo: recursos Azure, variables, scripts SQL, smoke test y rollback |

---

*Este documento cubre el MVP XPAY — Fases 1 a 19. Actualizar en cada fase que agregue módulos o modifique flujos existentes.*

---

## 11. Plantilla de ejecución QA

Este manual define **qué probar** y cuál es el resultado esperado en cada caso.

Para registrar **la ejecución real** — quién lo ejecutó, qué ocurrió, qué evidencias se capturaron, qué bugs se encontraron y cuál fue la decisión final — usar la plantilla de ejecución:

**[`docs/QA_EXECUTION_TEMPLATE.md`](QA_EXECUTION_TEMPLATE.md)**

La plantilla incluye la matriz de los 35 casos con columnas de responsable, fecha, estado, evidencia y bug relacionado; el registro de evidencias y bugs; los criterios de decisión final; y el acta de cierre QA.
