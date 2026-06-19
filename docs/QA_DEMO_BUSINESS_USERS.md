# XPAY MVP — Usuarios QA de Negocio (Comercio y Empresa)

**Fase:** 55 (demo usuarios negocio)  
**Fecha UTC:** 2026-06-19  
**Responsable:** Gabriel Alfonso Pineda Ortiz `g.pineda@cercaymejor.com`  
**Ambiente:** QA/Demo — NO producción · NO dinero real · NO datos reales

---

> **ADVERTENCIA:**
> Todos los saldos, transacciones y datos de este documento son ficticios.
> No representan dinero real. No involucran operaciones financieras reales.
> Uso exclusivo QA/Demo. No ejecutar en producción.

---

## Perfiles de usuario negocio

### qa.comercio1 — Comercio Aliado

| Campo | Valor |
|-------|-------|
| **Usuario** | `qa.comercio1` |
| **Contraseña** | `<password-demo-entregada-por-canal-seguro>` |
| **idUsuario** | 11 |
| **idPersona** | 5 (CC ficticia 900000005) |
| **Rol en BD** | `COMERCIO` (id_rol=18) |
| **Rol en spec** | COMERCIO_ALIADO → mapeado a `COMERCIO` existente |
| **Vista asignada** | `/mi-comercio` (Mi Comercio) |
| **Comercio demo** | Comercio Demo XPAY QA |
| **idComercio** | 2 |
| **idWalletComercio** | 4 |
| **QR** | `QR-DEMO-XPAY-QA-001` |

> El rol `COMERCIO_ALIADO` no existe en la BD — se usó `COMERCIO` (el rol más cercano disponible en 007_security_roles_jwt.sql). La detección en frontend usa `roles.includes('COMERCIO')` como condición principal, más un fallback por username `qa.comercio1`.

**Qué puede ver:**
- Nombre del comercio
- Saldo de wallet del comercio (ficticio)
- Resumen de ventas QR del comercio: total, contingencia, liquidadas, valor total
- Resumen de retiros: total, pendientes, valor
- Listado de ventas QR filtrado por su comercio
- Listado de retiros filtrado por su comercio
- Formulario para solicitar retiro
- Flujo de liquidación QA (explicativo)

**Qué NO puede ver:**
- Ledger global
- Wallets de otros usuarios/comercios
- Datos de otros comercios
- Panel administrativo (Dashboard, menú admin, etc.)
- Información de qa.usuario1/qa.usuario2

**Endpoints usados (todos filtrados a idComercio=2):**
- `GET /api/reportes/comercios/2/resumen` — saldo + resumen ventas + retiros
- `GET /api/admin/ventas-qr?idComercio=2&pageSize=10` — ventas QR del comercio
- `GET /api/comercios/retiros?idComercio=2&pageSize=10` — retiros del comercio
- `POST /api/comercios/solicitar-retiro` — solicitar retiro (requiere saldo disponible)

**Limitación de seguridad:** Los endpoints `/api/admin/ventas-qr` y `/api/comercios/retiros` aceptan filtros por `idComercio` pero no validan en backend que el usuario autenticado pertenezca al comercio. Esta validación es responsabilidad del frontend QA (que hardcodea idComercio=2). En producción, se requeriría validación de autorización a nivel de backend.

---

### qa.empresa1 — Empresa Libranza

| Campo | Valor |
|-------|-------|
| **Usuario** | `qa.empresa1` |
| **Contraseña** | `<password-demo-entregada-por-canal-seguro>` |
| **idUsuario** | 12 |
| **idPersona** | 6 (CC ficticia 900000006) |
| **Rol en BD** | (ninguno asignado) |
| **Rol en spec** | EMPRESA_LIBRANZA → NO existe en BD; detección por username |
| **Vista asignada** | `/mi-empresa` (Mi Empresa / Libranza Demo) |
| **Módulo libranza** | NO implementado en este MVP |

> El rol `EMPRESA_LIBRANZA` no existe ni en el schema ni en el seed. La detección es exclusivamente por username `qa.empresa1`. Esto es una regla temporal QA — en producción se implementaría el módulo de libranza con sus propios roles y endpoints.

**Qué puede ver:**
- Vista informativa del módulo de libranza
- Flujo previsto (5 pasos): carga empleados → validación cupo → uso wallet/QR → consulta → recaudo
- Estado del módulo: en preparación
- Capacidades planificadas (tabla)
- Nombre y datos del "Empresa Demo Libranza QA" (ficticios)

**Qué NO puede ver:**
- Ningún endpoint transaccional (módulo no implementado)
- Datos de otros usuarios o comercios
- Panel administrativo
- Ledger global, wallets globales, comercios, retiros

**Operaciones disponibles:** Ninguna transaccional. Vista 100% informativa.

---

## Lógica de detección de vista (frontend)

```typescript
// src/auth/AuthContext.tsx — getViewForUser()
export function getViewForUser(user: AuthUser): UserView {
  if (user.roles.includes('ADMIN_XPAY') || user.roles.includes('OPERADOR_XPAY')) return 'admin';
  if (user.roles.includes('COMERCIO') || user.usuario === 'qa.comercio1')         return 'comercio';
  if (user.usuario === 'qa.empresa1')                                              return 'empresa';
  return 'wallet';
}
```

| Usuario | Roles JWT | Vista |
|---------|-----------|-------|
| qa.admin.xpay | ['ADMIN_XPAY'] | `/dashboard` (panel admin completo) |
| qa.operador.xpay | ['OPERADOR_XPAY'] | `/dashboard` (panel admin completo) |
| qa.comercio1 | ['COMERCIO'] | `/mi-comercio` |
| qa.empresa1 | [] | `/mi-empresa` (detección por username) |
| qa.usuario1 | [] | `/mi-wallet` |
| qa.usuario2 | [] | `/mi-wallet` |

> **Regla temporal QA:** La detección por username (`qa.comercio1`, `qa.empresa1`) es exclusiva del ambiente QA/Demo. No usar en producción. En producción, la autorización debe validarse en backend con roles explícitos por cada endpoint.

---

## Mapa demo hardcodeado (frontend)

```typescript
// src/pages/MiComercioPage.tsx — DEMO_COMERCIO_MAP
const DEMO_COMERCIO_MAP = {
  'qa.comercio1': { idComercio: 2, idWalletComercio: 4 },
};
```

---

## Creación de usuarios en BD QA

Los usuarios fueron creados directamente en `sqldb-xpay-qa` via sqlcmd (previo ajuste de firewall Azure SQL). No se modificaron scripts SQL versionados.

| Paso | qa.comercio1 | qa.empresa1 |
|------|-------------|-------------|
| Persona | CC 900000005, QA Comercio Demo | CC 900000006, QA Empresa Libranza |
| Usuario | Insertado con hash demo | Insertado con hash demo |
| Rol | COMERCIO (id=18) asignado | Sin rol |
| Wallet | No (usa wallet del comercio) | No (módulo no implementado) |

---

## Seguridad

- ✅ Contraseñas NO aparecen en este documento
- ✅ Tokens JWT NO aparecen en este documento
- ✅ Connection string NO aparece en este documento
- ✅ No se modificaron scripts SQL versionados
- ✅ No se modificó lógica financiera del backend
- ✅ No se modificó el backend funcional
- ✅ Todos los datos son ficticios
- ✅ No hay datos personales reales (CCs en rango 9000000xx ficticio)
- ✅ qa.comercio1 solo accede a datos de su comercio (idComercio=2, filtrado en frontend)
- ✅ qa.empresa1 no tiene acceso a endpoints transaccionales
- ✅ No hay producción, dinero real ni usuarios reales

---

*Documento creado en Fase 55. Actualizar si se modifican los IDs de comercio, wallet, o se resetea la BD QA. La regla de detección por username es temporal QA — documentar reemplazo cuando se implemente autorización en backend para estos roles.*
