# XPAY MVP — Usuarios QA Transaccionales para Demo

**Fase:** 53 (preparación demo transaccional)  
**Fecha UTC:** 2026-06-19  
**Responsable:** Gabriel Alfonso Pineda Ortiz `g.pineda@cercaymejor.com`  
**Ambiente:** QA/Demo — NO producción · NO dinero real · NO datos reales

---

> **ADVERTENCIA:**
> Todos los saldos, transacciones y datos de este documento son ficticios.
> No representan dinero real. No involucran operaciones financieras reales.
> Uso exclusivo QA/Demo. No ejecutar en producción.

---

## Usuarios QA transaccionales

| Campo | qa.usuario1 | qa.usuario2 |
|-------|-------------|-------------|
| **Usuario** | `qa.usuario1` | `qa.usuario2` |
| **Contraseña** | `<password-demo-entregada-por-canal-seguro>` | `<password-demo-entregada-por-canal-seguro>` |
| **idUsuario** | 3 | 4 |
| **idPersona** | 3 | 4 |
| **idWallet** | 2 | 3 |
| **Tipo wallet** | PERSONA | PERSONA |
| **Nombre wallet** | Wallet QA Usuario Uno | Wallet QA Usuario Dos |
| **Estado** | ACTIVO | ACTIVO |
| **Roles** | (ninguno asignado) | (ninguno asignado) |
| **Acceso endpoints `[Authorize]`** | ✅ Funcionan con JWT válido | ✅ Funcionan con JWT válido |

> Los usuarios del seed QA no tienen rol explícito asignado (a diferencia de `qa.admin.xpay` con `ADMIN_XPAY`). Los endpoints `[Authorize]` sin restricción de rol específico son accesibles con su JWT. Esto es suficiente para demostrar el flujo transaccional de usuarios finales.

---

## Comercio / QR demo

| Campo | Valor |
|-------|-------|
| **Código QR** | `QR-DEMO-XPAY-QA-001` |
| **Comercio** | Comercio Demo XPAY QA |
| **idComercio** | 2 |
| **idTienda** | 2 |
| **Estado QR** | ACTIVO |

---

## Estado de saldos (ficticios)

### Saldo inicial (antes de preparación)
| Usuario | Wallet | Saldo disponible |
|---------|--------|-----------------|
| qa.usuario1 | 2 | $0 |
| qa.usuario2 | 3 | $0 |

### Después de recargas ficticias (admin → usuarios QA)
| Operación | Usuario | Wallet | Monto | Observación |
|-----------|---------|--------|-------|-------------|
| Recarga 1 | qa.usuario1 | 2 | +$100,000 | Recarga ficticia demo socios - usuario QA 1 |
| Recarga 2 | qa.usuario1 | 2 | +$100,000 | Recarga ficticia demo socios - usuario QA 1 |
| Recarga 3 | qa.usuario1 | 2 | +$100,000 | Recarga ficticia demo socios - usuario QA 1 |
| Recarga 1 | qa.usuario2 | 3 | +$100,000 | Recarga ficticia demo socios - usuario QA 2 |
| Recarga 2 | qa.usuario2 | 3 | +$100,000 | Recarga ficticia demo socios - usuario QA 2 |

> Las 3 recargas de usuario1 se generaron en la sesión de preparación (un par de llamadas redundantes en la validación). El saldo de $300,000 es ficticio y suficiente para la demo.

| Usuario | Saldo post-recargas |
|---------|-------------------|
| qa.usuario1 | $300,000 (ficticio) |
| qa.usuario2 | $200,000 (ficticio) |

---

## Operaciones transaccionales ejecutadas

### Transferencia — usuario1 → usuario2

| Campo | Valor |
|-------|-------|
| **idTransaccion (ledger)** | 21 |
| **Tipo** | TRANSFERENCIA_WALLET |
| **Wallet origen** | 2 (qa.usuario1) |
| **Wallet destino** | 3 (qa.usuario2) |
| **Valor** | $10,000 (ficticio) |
| **Descripción** | "Transferencia demo QA usuario1 a usuario2" |
| **Resultado** | ✅ HTTP 200 — "Transferencia realizada exitosamente." |
| **Ledger balance** | ✅ Débito wallet 2 / Crédito wallet 3 — balanceado |

**Saldos tras transferencia:**
- qa.usuario1: $300,000 - $10,000 = **$290,000** ✅
- qa.usuario2: $200,000 + $10,000 = **$210,000** ✅

---

### Compra QR — usuario2 → Comercio Demo

| Campo | Valor |
|-------|-------|
| **idVentaQr** | 4 |
| **idTransaccion (ledger)** | 22 |
| **Tipo** | PAGO_QR |
| **QR usado** | `QR-DEMO-XPAY-QA-001` |
| **Wallet pagadora** | 3 (qa.usuario2) |
| **idComercio** | 2 (Comercio Demo XPAY QA) |
| **Valor** | $15,000 (ficticio) |
| **Descripción** | "Compra QR demo QA usuario2 comercio" |
| **Estado venta QR** | CONTINGENCIA (pendiente de liquidación) |
| **Resultado** | ✅ HTTP 200 — "Pago QR realizado exitosamente." |
| **Ledger balance** | ✅ 2 movimientos de $15,000 c/u — Débito wallet usuario2 / Crédito wallet comercio |

**Saldo tras pago QR:**
- qa.usuario2: $210,000 - $15,000 = **$195,000** ✅

---

## Saldos finales verificados

| Usuario | Wallet | Saldo final (ficticio) |
|---------|--------|----------------------|
| qa.usuario1 | 2 | **$290,000** |
| qa.usuario2 | 3 | **$195,000** |

---

## Validación ledger

| tx# | Tipo | Valor | Movimientos | Balance |
|-----|------|-------|-------------|---------|
| 18 | RECARGA_WALLET | $100,000 | 2 | ✅ |
| 19 | RECARGA_WALLET | $100,000 | 2 | ✅ |
| 20 | RECARGA_WALLET | $100,000 | 2 | ✅ |
| 21 | TRANSFERENCIA_WALLET | $10,000 | 2 | ✅ |
| 22 | PAGO_QR | $15,000 | 2 | ✅ |

**Total transacciones ledger en el sistema:** 15 (post-preparación)  
**Ventas QR totales:** 2 (ventaQR#3 LIQUIDADA del CI + ventaQR#4 CONTINGENCIA de esta demo)

---

## Resumen operativo

| Check | Estado |
|-------|--------|
| Login qa.usuario1 con contraseña demo | ✅ HTTP 200 |
| Login qa.usuario2 con contraseña demo | ✅ HTTP 200 |
| Hash BCrypt válido (ambos usuarios) | ✅ |
| Wallet activa qa.usuario1 (id=2) | ✅ |
| Wallet activa qa.usuario2 (id=3) | ✅ |
| Saldo usuario1 ≥ $100,000 | ✅ $290,000 |
| Saldo usuario2 ≥ $100,000 | ✅ $195,000 |
| Transferencia usuario1 → usuario2 | ✅ tx#21 $10,000 |
| Compra QR usuario2 → comercio | ✅ ventaQR#4 $15,000 CONTINGENCIA |
| Ledger balanceado | ✅ débito = crédito en cada transacción |
| Venta QR visible en admin panel | ✅ `/ventas-qr/listado` muestra ventaQR#4 |
| No hay datos reales | ✅ nombres, CCs, emails y saldos ficticios |
| No hay dinero real | ✅ sin integración bancaria real |

---

## Cómo usar en la demo

### Login usuario
```
URL:      https://xpay-admin-qa.azurewebsites.net
Usuario:  qa.usuario1 o qa.usuario2
Password: <entregar por canal seguro>
```

### Flujo transaccional en vivo (si se desea mostrar en demo)

Para ejecutar transacciones en tiempo real durante la demo, usar los endpoints:

**1. Login y obtener token:**
```bash
# No ejecutar en pantalla pública — solo referencia
POST https://xpay-api-qa.azurewebsites.net/api/auth/login
{ "usuario": "qa.usuario1", "password": "<por canal seguro>" }
```

**2. Transferencia:**
```bash
POST /api/wallets/transferencia
{
  "idWalletOrigen": 2,
  "idWalletDestino": 3,
  "valor": 5000,
  "creadoPor": 3,
  "descripcion": "Demo transferencia QA"
}
```

**3. Pago QR:**
```bash
POST /api/qr/pagar
{
  "codigoQr": "QR-DEMO-XPAY-QA-001",
  "idWalletUsuario": 3,
  "valor": 5000,
  "creadoPor": 4,
  "descripcion": "Demo compra QR"
}
```

> Para la demo visual, se recomienda **mostrar la evidencia ya cargada** en el panel admin (ventas QR, ledger, saldos) en lugar de ejecutar transacciones en vivo, para evitar dependencias de red en la sala de reunión.

---

## Seguridad

- ✅ Contraseña NO aparece en este documento
- ✅ Tokens JWT NO aparecen en este documento
- ✅ Connection string NO aparece en este documento
- ✅ SQL admin password NO aparece en este documento
- ✅ No se modificó ningún script SQL versionado
- ✅ No se modificó lógica financiera del backend
- ✅ No se modificó el frontend funcional
- ✅ Todos los saldos son ficticios (no representan dinero real)
- ✅ No hay datos personales reales

---

## Fase 54 — Vista "Mi Wallet" en el frontend

En Fase 54 se creó la página `UserWalletPage.tsx` para que qa.usuario1 y qa.usuario2 tengan una experiencia diferenciada del admin.

| Elemento | Detalle |
|----------|---------|
| Ruta | `/mi-wallet` |
| Acceso | Solo usuarios sin rol ADMIN_XPAY/OPERADOR_XPAY |
| Muestra | Saldo ficticio, movimientos recientes, formulario transferencia, formulario pago QR |
| Oculta | Menú admin (dashboard, ledger global, todos los wallets/comercios/retiros) |
| Detección | `isAdminUser(user)` → roles includes ADMIN_XPAY o OPERADOR_XPAY |
| Mapa demo | `qa.usuario1` → idWallet=2, idUsuario=3 / `qa.usuario2` → idWallet=3, idUsuario=4 |

**Operaciones de validación ejecutadas en Fase 54 (ficticias):**

| Operación | Detalles | Resultado |
|-----------|---------|-----------|
| Transferencia qa.usuario1→qa.usuario2 desde UI | $5,000 desde formulario Mi Wallet | ✅ HTTP 200 |
| Pago QR qa.usuario2→QR-DEMO-XPAY-QA-001 desde UI | $5,000 desde formulario Mi Wallet | ✅ HTTP 200 |

**Saldos tras Fase 54 (ficticios):**
- qa.usuario1 (wallet 2): $285,000
- qa.usuario2 (wallet 3): $195,000

*Documento creado en Fase 53. Actualizado en Fase 54 con vista Mi Wallet y operaciones de validación adicionales. Actualizar si se modifican los IDs de wallet o se resetea la BD QA.*
