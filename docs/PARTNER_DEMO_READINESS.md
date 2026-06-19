# XPAY MVP — Partner Demo Readiness

**Fase:** 52  
**Fecha UTC:** 2026-06-19  
**Evaluador:** Gabriel Alfonso Pineda Ortiz `g.pineda@cercaymejor.com`  
**Ambiente:** QA/Demo — NO producción · NO dinero real · NO datos reales

---

## URLs del ambiente

| Servicio | URL | Estado |
|---------|-----|--------|
| **Frontend Admin** | `https://xpay-admin-qa.azurewebsites.net` | ✅ Activo |
| **Backend API** | `https://xpay-api-qa.azurewebsites.net` | ✅ Activo |
| SQL Server | `xpay-sql-qa.database.windows.net` | ✅ Privado (firewall) |

**Commit desplegado (frontend):** `0c6385f` (docs: record Azure QA frontend deployment)  
**Commit desplegado (backend):** `0c6385f` (mismo — sin cambios funcionales en Fase 52)

---

## Estado visual por módulo

| Módulo | Ruta | HTTP | Datos cargados | Estado |
|--------|------|------|---------------|--------|
| Login | `/login` | 200 | — | ✅ Lista |
| Dashboard | `/dashboard` | 200 | Métricas + 3 tablas | ✅ Lista |
| Wallets — listado | `/wallets/listado` | 200 | 6 wallets (ficticias) | ✅ Lista |
| Wallets — detalle | `/wallets/:id` | 200 | Estado de cuenta + movimientos | ✅ Lista |
| Wallets — búsqueda | `/wallets` | 200 | Formulario de búsqueda | ✅ Lista |
| Comercios — listado | `/comercios/listado` | 200 | 2 comercios demo | ✅ Lista |
| Comercios — detalle | `/comercios/:id` | 200 | Datos del comercio | ✅ Lista |
| Ventas QR | `/ventas-qr/listado` | 200 | 1 venta QR LIQUIDADA | ✅ Lista |
| Ledger — listado | `/ledger/listado` | 200 | Transacciones del ciclo QA | ✅ Lista |
| Ledger — detalle | `/ledger/:id` | 200 | Entradas débito/crédito | ✅ Lista |
| Retiros — listado | `/retiros/listado` | 200 | Retiros PAGADO y RECHAZADO | ✅ Lista |
| Retiros — detalle | `/retiros/:id` | 200 | Detalle del retiro | ✅ Lista |
| Logout | botón nav | — | Redirige a /login | ✅ Lista |
| Refresh en ruta interna | cualquier ruta | 200 | SPA fallback a index.html | ✅ Lista |

---

## Estado login

| Check | Resultado |
|-------|----------|
| `POST /api/auth/login` con usuario demo | ✅ HTTP 200 |
| `success: true` | ✅ |
| Token JWT presente | ✅ (no visible en pantalla) |
| Rol `ADMIN_XPAY` | ✅ |
| Redirección a `/dashboard` | ✅ |
| Manejo de credenciales incorrectas (mensaje "Usuario o contraseña inválidos") | ✅ (por código del backend) |
| Sesión expirada → redirige a /login con mensaje | ✅ (por código del frontend `AuthContext.tsx`) |

---

## Estado CORS

| Check | Resultado |
|-------|----------|
| `OPTIONS /api/auth/login` desde `https://xpay-admin-qa.azurewebsites.net` | ✅ HTTP 204 |
| `Access-Control-Allow-Origin` = `https://xpay-admin-qa.azurewebsites.net` | ✅ |
| `Access-Control-Allow-Methods` = `POST` | ✅ |
| No hay errores CORS en consola del navegador | ✅ (verificado por preflight correcto) |
| Llamadas protegidas incluyen `Authorization: Bearer` | ✅ (por `authHeaders()` en `src/api/client.ts`) |

---

## Estado de datos — ficticios confirmados

| Dato visible | Origen | ¿Es real? |
|-------------|--------|----------|
| Wallet "Carlos Gomez" — saldo 45,000 | Usuario CI test (`carlos_ci_test`) | No — nombre genérico ficticio, sin cédula real |
| Wallet "Maria Lopez" — saldo 25,000 | Usuario CI test (`maria_ci_test`) | No — nombre genérico ficticio, sin cédula real |
| Wallet "QA Usuario Uno/Dos" — saldo 0 | Seed 008 | No — ficticios, CC 900000001-004 (rango de prueba) |
| Wallet "Comercio Demo XPAY QA" — saldo 0 | Seed 008 | No — ficticio |
| Comercio "Comercio Demo XPAY QA" | Seed 008 | No — ficticio |
| Comercio "Comercio Demo XPAY" | CI test | No — ficticio |
| Venta QR — valor 30,000 LIQUIDADA | Corrida validate-backend.sh | No — monto de prueba ficticio |
| Retiros PAGADO (20k) y RECHAZADO (5k) | Corrida validate-backend.sh | No — montos de prueba ficticios |
| Transacciones ledger | Corrida validate-backend.sh | No — trazabilidad de prueba |
| Emails | `@xpay.test` (dominio ficticio) | No |
| Documentos CC | Rango 900000001–900000004 y rangos de test CI | No — NITs/CCs de prueba sin asignación real |
| Saldos | Ficticios, sin respaldo bancario real | No — no representan dinero real |

**Decisión de datos: Opción A — mantener datos del ciclo QA como evidencia de prueba.**
Los datos visibles demuestran el ciclo financiero completo funcionando. Presentar a socios como *"datos del ciclo de pruebas automatizadas QA"*.

---

## Estado técnico del ambiente

| Componente | Estado |
|-----------|--------|
| `GET /health` | ✅ 200 Healthy |
| `GET /api/diagnostics/ready` | ✅ 200 READY |
| `GET /api/version` | ✅ 200 `0.1.0-mvp-qa` |
| Frontend HTTP 200 en todas las rutas SPA | ✅ |
| `dotnet build` (Release) | ✅ 0 errors, 0 warnings |
| `npm run build` (Vite) | ✅ built in 1.05s |
| `scan-dependencies-security.sh` | ✅ 0 vulnerabilidades |
| GitHub Actions (commit `0c6385f`) | ✅ Backend Validation, Frontend Build, Dependency Security Scan — todos `success` |

---

## Limitaciones conocidas

| Limitación | Impacto en demo | Mitigación |
|-----------|----------------|------------|
| Cold Start del App Service | Primera carga del día puede tardar 5–15 seg | Verificar 30 min antes de la demo, precalentar visitando la URL |
| Módulo de búsqueda de wallet/comercio/ledger/retiro muestra formulario vacío hasta buscar | Puede parecer vacío al primer ingreso | Explicar que requiere ID para buscar |
| Los saldos del dashboard son acumulados de pruebas QA | Pueden ser números "raros" (ej. $70,000 usuarios) | Presentar como datos del ciclo de prueba |
| No hay acción de recarga/transferencia desde la UI actual | La UI es de administración/consulta, no de operación para usuarios finales | Aclarar que la UI admin es para el equipo XPAY, no para usuarios finales |
| Sin módulo de registro de usuarios desde UI | Solo consulta/admin — el registro es por API | Aclarar que el registro de usuarios finales es por app móvil futura |
| HSTS deshabilitado en QA | El backend no envía HSTS en QA (correcto para QA, se habilita en producción) | No relevante para demo |
| Swagger habilitado en QA | `/swagger` expone la documentación de la API (intencional en QA) | No exponer públicamente |
| Saldo $0 en wallet "Comercio Demo XPAY QA" | El saldo del comercio QA muestra $0 (se acumulan y reset en cada corrida de validate-backend.sh) | Señalar otras wallets con saldo para la demo |
| No hay paginación visible en la UI (solo primera página) | Los listados muestran la primera página; no hay botón "siguiente" visible | Aclarar que la paginación es por API; la UI actual muestra los primeros registros |

---

## Riesgos operativos para la demo

| Riesgo | Probabilidad | Mitigación |
|--------|-------------|-----------|
| App Service "dormido" (sin tráfico previo) | Media | Precalentar 30 min antes |
| IP del presentador bloqueada en SQL (no afecta frontend) | Baja | El frontend solo llama al backend, no a SQL directamente |
| Token JWT expira durante demo larga (> 2 horas) | Baja | La demo dura 15 min; si expira, el frontend muestra mensaje y redirige a login |
| Error de red en la sala de reunión | Baja | Alternativa: grabar la demo en video antes de la reunión |
| Credenciales demo comprometidas | Baja | Cambiar la contraseña demo en Azure App Settings si se sospecha compromiso |

---

## Decisión final

> **✅ LISTA PARA DEMO CON SOCIOS — CON OBSERVACIONES**

**Condiciones de la decisión:**

- ✅ Frontend accesible y cargando correctamente
- ✅ Backend respondiendo en todos los endpoints críticos
- ✅ Login end-to-end funcional
- ✅ Todos los módulos sirven HTTP 200
- ✅ CORS correcto entre frontend y backend
- ✅ Datos ficticios confirmados (sin datos personales reales ni dinero real)
- ✅ No hay secretos en el repositorio
- ✅ CI/CD verde en el commit desplegado

**Observaciones (no bloqueantes):**

- Los listados de wallets/comercios/etc. requieren que el usuario busque o cargue la primera página manualmente — no hay carga automática en la vista de búsqueda individual.
- El Cold Start del App Service puede generar un retraso en la primera carga del día.
- Los saldos acumulados del ciclo QA (ej. $70,000 en wallets de usuarios CI) son ficticios pero pueden confundir si no se aclara el origen.

**Qué NO está lista:**

- ❌ Producción
- ❌ Dinero real
- ❌ Usuarios reales
- ❌ Integración bancaria
- ❌ Validación legal/regulatoria
- ❌ Preproducción formal

---

## Registro de demos realizadas

| Fecha | Participantes | Resultado | Observaciones |
|-------|--------------|----------|--------------|
| — | — | — | Completar después de cada sesión de demo |

---

*Documento creado en Fase 52. Actualizar después de cada demo con socios y después de cambios en el ambiente QA.*
