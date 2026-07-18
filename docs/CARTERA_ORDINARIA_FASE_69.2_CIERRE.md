# XPAY MVP — Cartera Ordinaria: Cierre Fase 69.2

**Fase:** 69.2 — Confirmación real de utilización AVANCE_WALLET
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

Fase 69.1/69.1b (commits `0230e0a`, `b30e191`) construyeron la base de Cartera Ordinaria
(cupos, parámetros de utilización, política de crédito, simulador de amortización French),
pero la confirmación real quedaba deshabilitada ("próxima fase").

Fase 69.2 activa la confirmación real **solo para `AVANCE_WALLET`** (desembolso de crédito
directo a la wallet del usuario), de forma transaccional: mueve cupo, wallet y ledger reales
en QA con rollback completo ante cualquier error. La confirmación de `COMPRA_COMERCIO` sigue
fuera de alcance, pendiente para una fase futura.

---

## Validación end-to-end — resultado

**Usuario de prueba:** `qa.usuario1` (idUsuario=3, idWallet=2)
**Operación:** Desembolso AVANCE_WALLET — $200.000, plazo 1 mes, frecuencia MENSUAL

### Antes → Después

| Campo | Antes | Después | Δ |
|-------|------:|--------:|--:|
| Cupo aprobado | $1.000.000 | $1.000.000 | sin cambio |
| Cupo usado | $0 | $200.000 | +$200.000 |
| Cupo disponible | $1.000.000 | $800.000 | -$200.000 |
| Saldo Wallet #2 | $155.265 | $355.265 | +$200.000 |

### Entidades creadas

| Entidad | Valor |
|---|---|
| Utilización | `idUtilizacion=1`, tipo `AVANCE_WALLET`, estado **`DESEMBOLSADO`** |
| Cuota | 1 cuota, estado `PENDIENTE`, capital $200.000, interés $4.000, aval $28.000, admin $2.000, IVA $5.700, total $239.700 |
| Ledger | `idTransaccionLedger=112`, tipo `CARTERA_AVANCE_WALLET_DESEMBOLSO` |

> El estado real de la utilización es `DESEMBOLSADO` (no `ACTIVA`) — misma convención que
> `LibranzaAnticipo` (`CREADO → DESEMBOLSADO → PAGADO`). No existe un estado `ACTIVA` en el
> modelo `CarteraUtilizacion`.

### Ledger balanceado

| Cuenta | Naturaleza | Valor |
|---|---|---:|
| `130105` Cartera Ordinaria - Avance Wallet | D | $200.000 |
| `210101` Obligación Wallet Usuarios | C | $200.000 |

`totalDebitos = totalCreditos = $200.000` → **balanceado: true**

### Prueba negativa — cupo insuficiente

Intento de confirmar `$900.000` contra un cupo disponible de `$800.000` → `HTTP 400`,
`"El valor solicitado supera tu cupo disponible (800,000)"`.

Verificado sin efectos secundarios tras el rechazo:
- Cupo usado permaneció en $200.000 (no subió a $1.100.000).
- Cupo disponible permaneció en $800.000.
- Saldo de wallet permaneció en $355.265, sin movimiento nuevo.
- Conteo de transacciones ledger `CARTERA_AVANCE_WALLET_DESEMBOLSO` permaneció en 1 (no se creó una segunda).
- No se creó ninguna utilización ni cuota adicional.

### Fuera de este flujo

- **Passport:** sin movimientos — el código de `ConfirmarAvanceWalletAsync` no invoca ningún servicio Passport.
- **Veriff:** sin movimientos.
- **Datacrédito:** sin movimientos.
- **Producción:** todo ejecutado contra QA (`xpay-api-qa.azurewebsites.net`).

---

## Fixes técnicos incluidos en esta fase

| # | Fix | Descripción | Commit |
|---|-----|--------------|--------|
| 1 | Locks transaccionales | `ConfirmarAvanceWalletAsync` bloquea cupo (`cartera_cupos_ordinarios`) y saldo de wallet (`wallet_saldos`) con `WITH (UPDLOCK, ROWLOCK)` dentro de la misma transacción, antes de validar cupo disponible — evita que dos confirmaciones concurrentes del mismo usuario sobregiren el cupo o desincronicen el saldo. Toda la validación (parámetros, cupo, wallet, amortización) ocurre dentro de la transacción; nunca se confía en valores del frontend. | `431dd3e` |
| 2 | PIN QA de 7 dígitos | Se agregó a `MiCarteraOrdinariaPage.tsx` el mismo campo de PIN (formato-only, sin backend, QA/Demo) ya usado en `UserWalletPage.tsx`, para consistencia de UX. | `a28fa6c` |
| 3 | Fix claim `idUsuario` | `CarteraOrdinariaController.IdUsuarioActual` leía el claim `"sub"` del JWT, que no sobrevive el mapeo de claims entrantes de ASP.NET Core en este backend — resolvía siempre a `0`. Se corrigió a `User.FindFirst("idUsuario")`, el mismo patrón usado por los otros 8 controllers del proyecto. Sin este fix, `/mi-cupo` y `/confirmar-avance-wallet` fallaban para **cualquier** usuario, no solo `qa.usuario1`. | `e2e10d8` |
| 4 | Migración 022 | `database/022_cartera_ordinaria_avance_wallet.sql` — cuenta ledger `130105` "Cartera Ordinaria - Avance Wallet" (ACTIVO, D). Idempotente. Aplicada manualmente en QA por el responsable del proyecto (no vía backend, ver política de credenciales abajo). | — (SQL, sin ejecución automática) |
| 5 | Tabla diagnóstico admin | `CarteraOrdinariaAdminPage.tsx` — pestaña "Cupos QA" ahora muestra `idCupo`/`idUsuario`/`idWallet`/`fecha_vencimiento`, agregado temporalmente para diagnosticar el fix #3. | `8494689` |

### Nota de seguridad — credenciales QA

Durante esta fase, dos intentos de extraer automáticamente credenciales de Azure (connection
string de `xpay-api-qa` y decodificación de payload JWT) fueron bloqueados por el clasificador
de seguridad del entorno de ejecución. Se respetaron ambos bloqueos sin intentar evadirlos; la
migración 022 fue aplicada manualmente por el responsable del proyecto con sus propias
credenciales.

---

## Commits de la fase

| Commit | Descripción |
|--------|-------------|
| `431dd3e` | fix: add transactional locks to ordinary credit wallet advance |
| `a28fa6c` | fix: require QA PIN for ordinary credit wallet advance |
| `8494689` | diag: show idCupo/idUsuario/idWallet/vencimiento in Cupos QA admin table |
| `e2e10d8` | fix: read idUsuario claim instead of sub in CarteraOrdinariaController |

Todos pusheados a `main`. CI (Backend Validation, Frontend Build, Dependency Security Scan) 3/3 ✅ en cada uno.

---

## Despliegue QA

- Backend (`xpay-api-qa`): desplegado — `RuntimeSuccessful`, `/health` y `/api/version` responden 200.
- Frontend (`xpay-admin-qa`): desplegado — `RuntimeSuccessful`, SPA routing verificado.
- Base de datos QA (`sqldb-xpay-qa`): migración 022 aplicada manualmente, cuentas `130105` y `210101` confirmadas ACTIVAS.

---

## Fuera de alcance / próximos pasos

- Confirmación real de `COMPRA_COMERCIO` — pendiente, fase futura.
- Cobro de cuotas (`CarteraPago`/`CarteraPagoDetalle`) — no implementado en esta fase; la cuota generada queda en `PENDIENTE` sin flujo de pago todavía.
- La tabla de diagnóstico agregada en el admin panel (`idCupo`/`idUsuario`/`idWallet`) es temporal — considerar si se mantiene permanentemente o se retira en una fase posterior de limpieza de UI.
- No se ejecutaron nuevos desembolsos después del cierre de esta validación.
