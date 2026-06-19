# XPAY MVP — Guion de Demostración para Socios

**Versión:** 1.0  
**Fase:** 52  
**Fecha:** 2026-06-19  
**Duración estimada:** 10–15 minutos  
**Audiencia:** Socios potenciales, inversionistas, comercios interesados  
**Responsable de la demo:** Por designar antes de la reunión

---

> **ADVERTENCIA — LEER ANTES DE PRESENTAR:**
> Este ambiente es una **demo QA funcional** con datos ficticios.
> **No procesa dinero real.** No hay integración bancaria real.
> No tiene clientes reales. No está en producción.
> Usar únicamente para demostración controlada con socios autorizados.

---

## 1. Objetivo de la demo

Mostrar a socios el estado funcional del MVP XPAY:

- Plataforma de wallets digitales con flujo financiero completo (recarga, transferencia, pago QR, liquidación, retiro).
- Panel administrativo web con autenticación JWT, roles y módulos de operación.
- Infraestructura en la nube (Azure) con backend .NET 8, frontend React y base de datos SQL.
- Calidad del ciclo E2E demostrada por el flujo CI/CD (GitHub Actions).

**Frase central para socios:**

> *"Este ambiente es una demo QA funcional con datos ficticios. No procesa dinero real todavía. Lo que ven es el MVP completo funcionando en Azure, con datos de prueba generados por nuestro suite de validación automática."*

---

## 2. Acceso demo

| Campo | Valor |
|-------|-------|
| **URL frontend** | `https://xpay-admin-qa.azurewebsites.net` |
| **Usuario demo** | `qa.admin.xpay` |
| **Contraseña** | `<password-demo-entregada-por-canal-seguro>` |
| **Rol** | ADMIN_XPAY (acceso completo al panel) |
| **Ambiente** | QA/Demo — Azure eastus — datos ficticios |

> La contraseña NO aparece en este documento. Entregar por canal seguro (WhatsApp cifrado, correo corporativo) a la persona que presentará la demo.

---

## 3. Preparación antes de la reunión

**Checklist pre-demo (30 minutos antes):**

- [ ] Verificar que el frontend carga: `https://xpay-admin-qa.azurewebsites.net`
- [ ] Hacer login con usuario demo y confirmar que el dashboard muestra datos
- [ ] Verificar que las URLs en DevTools Network apuntan a `xpay-api-qa.azurewebsites.net` (no localhost)
- [ ] Confirmar que la barra del navegador no muestra errores de consola (F12 → Console → sin errores rojos)
- [ ] Cerrar otras pestañas y ventanas que puedan distraer
- [ ] Pantalla compartida lista (resolución ≥ 1280×768)
- [ ] Silenciar notificaciones del sistema operativo
- [ ] Tener este documento abierto en otra pestaña como referencia
- [ ] Confirmar que el backend responde: `curl https://xpay-api-qa.azurewebsites.net/health`
- [ ] Si hay saldos acumulados de pruebas anteriores, avisarlos como "datos del ciclo QA automático"

---

## 4. Flujo de demostración (10–15 minutos)

### Módulo 1 — Login y autenticación (2 min)

1. Abrir `https://xpay-admin-qa.azurewebsites.net` en el navegador.
2. Señalar la pantalla de login: **"Esta es la pantalla de acceso al panel administrativo XPAY"**.
3. Mostrar el campo `API: xpay-api-qa.azurewebsites.net` visible en la página — *"El frontend está conectado al backend en Azure"*.
4. Ingresar usuario `qa.admin.xpay` y la contraseña demo.
5. Clic en **Ingresar**.
6. El sistema redirige automáticamente al dashboard.

**Qué destacar:** Autenticación JWT, sin contraseña en URL, redirección segura.

---

### Módulo 2 — Dashboard operativo (2 min)

1. Señalar las métricas superiores:
   - **Total Wallets** — número de wallets activas en el sistema
   - **Saldo Usuarios** — saldo total acumulado en wallets de personas (datos QA)
   - **Saldo Comercios** — saldo total en wallets de comercios (datos QA)
   - **Ventas QR** — transacciones QR procesadas
   - **Retiros** — retiros pagados/pendientes/rechazados
   - **Txs Ledger** — transacciones en el libro contable
   - **Auditoría Eventos** — eventos de seguridad registrados

2. Señalar las tres tablas de resumen: últimos retiros, últimas ventas QR, últimas transacciones ledger.
3. Señalar los botones de acceso rápido (Wallets, Comercios, Retiros, Ventas QR, Ledger).

**Qué destacar:** Vista de operación en tiempo real, métricas del sistema, indicadores de negocio.

**Frase sugerida:** *"Este dashboard le permite al equipo operativo ver el estado del sistema de un vistazo: saldos, transacciones, retiros pendientes y eventos de auditoría."*

---

### Módulo 3 — Wallets (2 min)

1. Clic en **Wallets** en el menú lateral o en el botón de acceso rápido.
2. Mostrar el listado con filtros: tipo (PERSONA / COMERCIO / XPAY), estado, ID Persona.
3. Señalar las columnas: tipo de wallet, nombre, saldo disponible, estado, fecha de creación.
4. Clic en **Ver estado de cuenta** en cualquier wallet para mostrar el detalle y movimientos.

**Qué destacar:** Wallets por persona y por comercio, saldos en tiempo real, historial de movimientos, filtros de búsqueda.

**Datos visibles:** Wallets "Carlos Gomez", "Maria Lopez", "Comercio Demo XPAY QA" — todos ficticios, generados por el ciclo de pruebas automatizadas QA.

---

### Módulo 4 — Comercios (1 min)

1. Clic en **Comercios** en el menú lateral.
2. Mostrar el listado con nombre comercial, estado (ACTIVO), opciones de filtro.
3. Clic en un comercio para ver su detalle: datos del comercio, wallet asociada, saldo.

**Qué destacar:** Módulo para gestionar comercios afiliados al sistema de pagos QR.

**Frase sugerida:** *"Aquí el equipo XPAY gestiona los comercios afiliados — los que aceptan pagos por QR y solicitan retiros."*

---

### Módulo 5 — Ventas QR (1 min)

1. Clic en **Ventas QR** en el menú lateral.
2. Mostrar el listado con valor bruto, estado (LIQUIDADA / CONTINGENCIA), fecha.
3. Señalar los estados de una venta QR: el flujo va de venta → liquidación al comercio.

**Qué destacar:** El núcleo del negocio: pagos con código QR entre usuarios finales y comercios.

**Frase sugerida:** *"Cada transacción QR aparece aquí. El sistema registra el pago, lo pasa a CONTINGENCIA hasta la liquidación, y luego LIQUIDADA cuando se abona al comercio."*

---

### Módulo 6 — Ledger (1 min)

1. Clic en **Ledger** en el menú lateral.
2. Mostrar el listado de transacciones del libro contable: tipo (RECARGA, TRANSFERENCIA, PAGO_QR, RETIRO_COMERCIO_*, etc.), valor total, fecha.
3. Clic en **Ver detalle** de una transacción para mostrar el detalle completo con entradas de débito y crédito.

**Qué destacar:** Trazabilidad contable completa de cada operación financiera. Cada movimiento deja huella en el ledger.

**Frase sugerida:** *"El ledger es el corazón contable del sistema. Cada operación — recarga, transferencia, pago QR, retiro — genera entradas en el libro contable con débito y crédito balanceados."*

---

### Módulo 7 — Retiros (1 min)

1. Clic en **Retiros** en el menú lateral.
2. Mostrar el listado con estado (PAGADO, RECHAZADO, PENDIENTE), valor, fechas.
3. Señalar el flujo: el comercio solicita retiro → el equipo XPAY lo gestiona → PAGADO o RECHAZADO.

**Qué destacar:** Flujo de gestión de retiros para comercios, con trazabilidad de estado.

---

### Módulo 8 — Seguridad (1 min)

1. Abrir DevTools (F12) → pestaña **Network**.
2. Hacer clic en cualquier módulo para mostrar las llamadas API.
3. Señalar:
   - Todas las llamadas van a `xpay-api-qa.azurewebsites.net` (no a localhost)
   - El header `Authorization: Bearer <token>` en cada request protegido
   - HTTP 200 en todas las respuestas exitosas
4. Cerrar DevTools.

**Qué destacar:** Autenticación JWT en cada request, HTTPS, arquitectura desacoplada frontend-backend.

---

### Módulo 9 — Infraestructura y CI/CD (1 min)

Mostrar brevemente (sin salir del frontend):

- *"El backend está en Azure App Service, .NET 8, con Azure SQL Basic."*
- *"El frontend es React + Vite, también en Azure App Service."*
- *"Cada push al repositorio ejecuta automáticamente validación del backend (47 casos), build del frontend y escaneo de seguridad de dependencias — los tres workflows en verde."*
- *"El ciclo E2E de validación corre contra una BD real en CI y contra este Azure QA manualmente."*

---

### Módulo 10 — Logout (30 seg)

1. Clic en **Cerrar sesión** (esquina superior derecha del navegador).
2. El sistema redirige a `/login`.
3. Mostrar que sin sesión, el acceso a `/dashboard` redirige a `/login`.

**Qué destacar:** Manejo de sesión seguro, token eliminado de localStorage.

---

## 5. Preguntas probables de socios — Respuestas sugeridas

### ¿Ya funciona el sistema?
**Respuesta:** *"Sí, el MVP está funcionando en Azure con todas las funcionalidades core: wallets, pagos QR, liquidaciones y retiros. Lo que acabas de ver es el sistema real corriendo. Lo que falta es la integración con bancos reales, la validación legal y regulatoria, y el proceso de onboarding de usuarios reales."*

### ¿Ya mueve dinero real?
**Respuesta:** *"No. Este ambiente es QA/demo. Los saldos que ves son ficticios, generados por nuestro suite de pruebas automatizadas. El sistema está técnicamente capaz de procesar transacciones, pero para mover dinero real necesitamos completar la validación regulatoria, la integración bancaria y la aprobación formal del equipo financiero y legal."*

### ¿Qué falta para producción?
**Respuesta:** *"Tenemos un checklist formal de preproducción con ~53 brechas identificadas. Las más importantes son: integración bancaria real (PSE/Bre-B), validación regulatoria y legal, KYC formal, Key Vault para secretos, backups probados, pruebas de carga y un ambiente de preproducción separado del QA. Estamos en el punto correcto del roadmap — MVP validado, demo lista, ahora definimos la ruta a producción."*

### ¿Cuánto cuesta operar este sistema en la nube?
**Respuesta:** *"El ambiente QA actual cuesta aproximadamente USD 18–20/mes en Azure (SQL Basic + App Service B1). Para producción, el costo depende del volumen de transacciones y los requerimientos de disponibilidad — podría escalar de USD 100 a varios cientos al mes dependiendo del nivel de servicio."*

### ¿Qué tan seguro es el sistema?
**Respuesta:** *"El MVP implementa autenticación JWT con expiración, CORS estricto (solo el frontend autorizado puede llamar al backend), rate limiting en el login, headers de seguridad HTTP, manejo global de errores sin exponer detalles internos, y auditoría de eventos sensibles. Para producción, agregaríamos Key Vault, WAF, pruebas de penetración formales y cumplimiento OWASP completo."*

### ¿Cuándo se puede hacer un piloto con usuarios reales?
**Respuesta:** *"El plan contempla un piloto controlado después de cerrar los criterios de salida QA formales (documentados en nuestro checklist). Estimamos que podríamos tener un piloto controlado con 5–10 usuarios internos en el próximo ciclo — pero requiere la aprobación del responsable técnico y del responsable de negocio con firma en el acta de salida QA."*

### ¿Qué se necesita para que comercios reales puedan usar el sistema?
**Respuesta:** *"Para comercios reales necesitamos: (1) definir el proceso de onboarding legal del comercio, (2) integración con el mecanismo de pago real (PSE, Bre-B u otro), (3) definir el modelo financiero de retiros reales a cuentas bancarias, (4) cumplir con la normativa del sector de pagos electrónicos en Colombia. El sistema técnico está listo para construir ese flujo encima."*

### ¿Puedo registrarme como usuario?
**Respuesta:** *"En este ambiente QA no, para proteger la integridad del ambiente de demo. Cuando avancemos al piloto controlado, habilitaremos el registro de usuarios internos siguiendo el proceso de onboarding definido."*

---

## 6. Qué NO decir en la demo

| NO decir | Por qué |
|----------|---------|
| "Ya está en producción" | No es verdad — es QA/demo |
| "Ya mueve dinero real" | No hay integración bancaria ni validación legal |
| "Estamos procesando transacciones reales" | Los datos son ficticios |
| "Podemos lanzar en X semanas" | Sin fecha comprometida sin análisis completo |
| "Ya cumplimos con la regulación" | La validación regulatoria está pendiente |
| "Ya tenemos integración con PSE/Bre-B" | No existe en el MVP actual |
| "Los saldos representan dinero real" | Son saldos de prueba ficticios |

---

## 7. Checklist post-reunión

- [ ] Registrar preguntas y observaciones de los socios
- [ ] Anotar si algún módulo tuvo problemas visuales durante la demo
- [ ] Si hubo errores en consola o de API, documentarlos en `docs/QA_INTERNAL_ISSUES_TRACKING.md`
- [ ] Actualizar `docs/PARTNER_DEMO_READINESS.md` con resultado de la reunión
- [ ] No compartir URL ni credenciales demo por canales no autorizados después de la reunión
- [ ] Si los socios solicitaron acceso independiente, evaluar antes de compartir credenciales

---

## 8. Módulo opcional — Flujo transaccional en vivo (5 min)

> Este módulo muestra el ciclo transaccional completo desde la **vista Mi Wallet** de usuarios QA.
> A partir de Fase 54, los usuarios QA tienen su propia interfaz — ya no se necesita Swagger para las demo transaccionales.

**Usuarios transaccionales disponibles:**

| Usuario | idWallet | Saldo ficticio | Contraseña |
|---------|----------|---------------|-----------|
| `qa.usuario1` | 2 | ~$285,000 ficticio | `<por canal seguro>` |
| `qa.usuario2` | 3 | ~$195,000 ficticio | `<por canal seguro>` |
| QR demo | — | `QR-DEMO-XPAY-QA-001` | — |

**Opción A — Mostrar evidencia ya cargada desde panel admin (rápido):**

1. Logueado como `qa.admin.xpay` → abrir **Wallets** → mostrar "Wallet QA Usuario Uno" y "Wallet QA Usuario Dos".
2. Abrir **Ventas QR** → mostrar ventas CONTINGENCIA ya ejecutadas.
3. Abrir **Ledger** → mostrar TRANSFERENCIA_WALLET y PAGO_QR.

**Frase:** *"Este es el resultado de una transferencia entre usuarios y un pago QR ejecutados. Cada operación genera entradas balanceadas en el ledger."*

**Opción B — Demostrar la vista usuario final (recomendado para mostrar diferenciación) — Fase 54:**

1. **Logout** del admin → ingresar como `qa.usuario1` (contraseña por canal seguro).
2. El sistema redirige automáticamente a `/mi-wallet` — el menú admin **no aparece**.
3. Mostrar: saldo ficticio, movimientos recientes.
4. Ejecutar una transferencia a wallet 3 (`qa.usuario2`) por $5,000 desde el formulario.
5. Saldo se actualiza automáticamente al completar.
6. **Logout** → ingresar como `qa.usuario2`.
7. Verificar que el saldo aumentó (recibió la transferencia).
8. Ejecutar un pago QR a `QR-DEMO-XPAY-QA-001` por $5,000.
9. Saldo se actualiza.
10. **Logout** → ingresar como `qa.admin.xpay` → mostrar la nueva venta QR en CONTINGENCIA en el panel admin.

**Frase:** *"Los usuarios finales acceden a su propia vista de wallet. No ven el panel administrativo. El admin ve todo el panorama global."*

> **Si la red falla:** tener preparada la Opción A como respaldo. La Opción B requiere red estable para los POSTs de transferencia y pago QR.

Ver IDs, saldos y operaciones detalladas: **[docs/QA_DEMO_TRANSACTIONAL_USERS.md](QA_DEMO_TRANSACTIONAL_USERS.md)**

---

## 9. Notas técnicas para el presentador

- La pantalla de login muestra `API: xpay-api-qa.azurewebsites.net` — es visible para socios, es la URL del backend QA real, es correcto.
- El menú lateral muestra el usuario `qa.admin.xpay` y `API: xpay-api-qa.azurewebsites.net` — información de contexto, no sensible.
- Los saldos en el dashboard (Saldo Usuarios ~$555,000) son ficticios, acumulados por las recargas QA y el ciclo de validación automática — presentarlos como "datos del ciclo de pruebas".
- Si una sección muestra "Error" en el dashboard, usar el botón "↺ Reintentar" — puede ser un timeout del Cold Start del App Service.
- El Cold Start del App Service puede tardar 5–15 segundos en la primera carga del día.
- Si el backend está dormido (primera carga del día), `/health` puede tardar hasta 30 segundos en responder — verificar 30 minutos antes de la demo.

---

*Documento creado en Fase 52. Actualizado en Fase 53 con flujo transaccional. No versionado con contraseñas reales. Actualizar después de cada demo con socios.*
