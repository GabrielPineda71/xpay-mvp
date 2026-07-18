# XPAY MVP — Cartera Ordinaria: Cierre Fase 69.3

**Fase:** 69.3 — Pago manual de cuotas de Cartera Ordinaria desde Wallet
**Fecha UTC:** 2026-07-18
**Responsable:** Gabriel Alfonso Pineda Ortiz `g.pineda@cercaymejor.com`
**Ambiente:** QA — NO producción
**Estado:** ✅ **APROBADA Y CERRADA**

---

> **ADVERTENCIA:**
> Todos los saldos, cupos y movimientos de este documento corresponden a `qa.usuario1`
> en el ambiente QA (`xpay-api-qa.azurewebsites.net`). No son dinero real, no involucran
> producción, Passport, Veriff ni Datacrédito.

---

## Alcance

Fase 69.2 (cerrada, `docs/CARTERA_ORDINARIA_FASE_69.2_CIERRE.md`) dejó un desembolso real
`AVANCE_WALLET` activo para `qa.usuario1` (`idUtilizacion=1`, $200.000, cuota pendiente de
$239.700). Fase 69.3 cierra el ciclo del crédito: permite pagar esa cuota manualmente desde
la Wallet, aplicando el pago por concepto en orden fijo (IVA → IVA gastos cobranza → gastos
cobranza → aval → administración → intereses → capital), liberando el cupo ordinario **solo**
por la parte aplicada a capital, y dejando el ledger balanceado.

Gastos de cobranza, bloqueo por mora, pago automático, compra QR con cupo ordinario, pago
anticipado con condonación y las integraciones reales de Passport/Veriff/Datacrédito quedan
explícitamente fuera de alcance de esta fase.

---

## Validación end-to-end — resultado

**Usuario de prueba:** `qa.usuario1` (idUsuario=3, idWallet=2)
**Crédito:** `idUtilizacion=1` — pago completo de la cuota pendiente, $239.700

### Antes → Después

| Campo | Antes | Después | Δ |
|-------|------:|--------:|--:|
| Cupo usado | $200.000 | $0 | -$200.000 |
| Cupo disponible | $800.000 | $1.000.000 | +$200.000 |
| Saldo Wallet #2 | $355.265 | $115.565 | -$239.700 |
| Cuota — estado | PENDIENTE | PAGADA | — |
| Cuota — saldoCuota | $239.700 | $0 | -$239.700 |
| Utilización #1 — estado | DESEMBOLSADO | PAGADA | — |

### Entidades creadas

| Entidad | Valor |
|---|---|
| `cartera_pagos` | `idPago=1` |
| Movimiento wallet | `CARTERA_PAGO_CUOTA`, débito $239.700 |
| Ledger | `idTransaccionLedger=113`, tipo `CARTERA_PAGO_CUOTA_WALLET` |

### Ledger balanceado

| Cuenta | Naturaleza | Valor |
|---|---|---:|
| `210101` Obligación Wallet Usuarios | D | $239.700 |
| `130105` Cartera Ordinaria - Avance Wallet | C | $200.000 |
| `410301` Ingreso Intereses Cartera Ordinaria | C | $4.000 |
| `410302` Ingreso Aval Cartera Ordinaria | C | $28.000 |
| `410303` Ingreso Administración Cartera Ordinaria | C | $2.000 |
| `240803` IVA Cartera Ordinaria por Pagar | C | $5.700 |

`totalDebitos = totalCreditos = $239.700` → **balanceado: true**

### Pruebas negativas

**1. Pago superior al saldo pendiente** — intento de pagar $300.000 contra un saldo pendiente
de $239.700 → `HTTP 400`, `"El valor a pagar (300,000) supera el saldo pendiente del
crédito (239,700)"`. Verificado sin efectos secundarios: cupo, wallet, cuota y conteo de
transacciones ledger sin cambios.

**2. Pago de crédito ya PAGADO** — intento de pagar $50.000 sobre `idUtilizacion=1` después
de saldado → `HTTP 400`, `"Este crédito ya está pagado en su totalidad"`. Verificado: el
conteo de transacciones ledger tipo `CARTERA_PAGO_CUOTA_WALLET` se mantuvo en 1 (no se creó
una segunda), sin efectos secundarios.

---

## Implementación

### Migración 023 (aplicada en QA)

`database/023_cartera_pago_manual_wallet.sql` — idempotente. Primer intento falló en Azure
SQL QA con `Invalid column name 'saldo_cuota'` (SQL Server compila el batch completo antes de
ejecutar cualquier sentencia; el `UPDATE` que referenciaba la columna recién agregada por el
`ALTER TABLE ADD` en el mismo batch no la veía todavía). Corregido envolviendo ese `UPDATE`
en `sp_executesql` (SQL dinámico, compilado en su propio contexto en tiempo de ejecución).
Segundo intento: aplicado limpio hasta `FIN MIGRACIÓN 023`.

Cuentas ledger nuevas confirmadas ACTIVAS en QA:
- `410301` Ingreso Intereses Cartera Ordinaria
- `410302` Ingreso Aval Cartera Ordinaria
- `410303` Ingreso Administración Cartera Ordinaria
- `240803` IVA Cartera Ordinaria por Pagar

Además: columnas de seguimiento en `cartera_pagos` (antes/después de saldo wallet, cupo
usado, cupo disponible, id de transacción ledger, método de pago, PIN validado QA), en
`cartera_pagos_detalle` (valores aplicados por concepto), y en `cartera_cuotas` (pagado por
concepto + `saldo_cuota`, este último respaldado a `valor_total` para la cuota existente).
Índices `ix_cartera_cuotas_util_estado_venc` e `ix_cartera_pagos_usuario_util_fecha`.

**Fix incluido detectado en revisión de código antes de desplegar:** `ConfirmarAvanceWalletAsync`
no inicializaba `SaldoCuota` al crear cuotas nuevas — cualquier crédito desembolsado
*después* de esta migración habría quedado impagable desde el día uno (saldo_cuota=0). Se
corrigió antes del deploy; la cuota preexistente de `idUtilizacion=1` no se vio afectada
porque la migración la respaldó explícitamente a su `valor_total`.

### Endpoints nuevos

- `GET /api/cartera-ordinaria/mis-creditos`
- `GET /api/cartera-ordinaria/mis-creditos/{idUtilizacion}/cuotas`
- `POST /api/cartera-ordinaria/pagar-cuota-wallet`

Todos derivan el usuario del claim `idUsuario` del JWT — nunca de un id enviado por el
cliente. `PagarCuotaWalletAsync` es transaccional: bloquea cupo, `wallet_saldos` y las
cuotas pendientes/parciales del crédito con `WITH (UPDLOCK, ROWLOCK)` dentro de la misma
transacción, mismo patrón que la confirmación de desembolso de Fase 69.2, con rollback
completo ante cualquier error.

### Frontend

`MiCarteraOrdinariaPage.tsx` — nueva sección "Mis créditos de Cartera Ordinaria":
listado de créditos, botón "Ver cuotas" (tabla expandible), botón "Pagar desde Wallet"
(monto, saldo de wallet actual, PIN QA de 7 dígitos formato-only, advertencia de que el
cupo solo se libera por la parte aplicada a capital), y panel de recibo tras confirmar
(valor pagado, capital/interés/aval/admin/IVA aplicado, nuevo cupo disponible, nuevo saldo
de wallet, idPago, idTransaccionLedger).

---

## Despliegue QA

- Backend (`xpay-api-qa`): desplegado — `RuntimeSuccessful`, `/health` responde 200.
- Frontend (`xpay-admin-qa`): desplegado — `RuntimeSuccessful`.
- Base de datos QA (`sqldb-xpay-qa`): migración 023 aplicada manualmente por el responsable
  del proyecto, cuentas `410301`/`410302`/`410303`/`240803` confirmadas ACTIVAS.

## Commit

| Commit | Descripción |
|--------|-------------|
| `b9fd7c8` | feat: pay ordinary credit installments from wallet |

Pusheado a `main`. GitHub Actions (Backend Validation, Frontend Build, Dependency Security
Scan): 3/3 ✅.

---

## Fuera de alcance / próximos pasos

- Pago automático de cuotas.
- Gastos de cobranza automáticos (columnas y orden de aplicación ya existen en el modelo,
  siempre aportan $0 esta fase).
- Bloqueo por mora.
- Compra QR con cupo ordinario.
- Pago anticipado con condonación.
- Passport, Veriff y Datacrédito reales.
- No se ejecutaron nuevos pagos después del cierre de esta validación.
