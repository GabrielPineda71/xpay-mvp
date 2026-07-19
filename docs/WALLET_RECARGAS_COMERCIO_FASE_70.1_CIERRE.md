# XPAY MVP — Wallet: Cierre Fase 70.1

**Fase:** 70.1 — Recarga de Wallet en efectivo por Cajero de Comercio
**Fecha UTC:** 2026-07-19
**Responsable:** Gabriel Alfonso Pineda Ortiz `g.pineda@cercaymejor.com`
**Ambiente:** QA — NO producción
**Estado:** ✅ **APROBADA Y CERRADA**

---

> **ADVERTENCIA:**
> Todos los saldos, cupos y movimientos de este documento corresponden a `qa.usuario1` y
> `qa.comercio1` en el ambiente QA (`xpay-api-qa.azurewebsites.net`). No son dinero real,
> no involucran producción, Passport, Veriff ni Datacrédito.

---

## Alcance implementado

- Recarga de Wallet de usuario en efectivo desde el portal Mi Comercio.
- Acceso para usuarios de comercio con scope operativo:
  - `ADMIN_COMERCIO`
  - `ADMIN_SEDE_COMERCIO`
  - `CAJERO`
- Búsqueda de usuario XPAY con Wallet activa (por usuario/documento/celular/correo).
- Confirmación de recarga con PIN QA de 7 dígitos.
- Registro de movimiento Wallet (`RECARGA_EFECTIVO_COMERCIO`).
- Registro de recaudo en tabla `wallet_recargas_comercio`.
- Ledger balanceado en cada recarga.
- Frontend en `/mi-comercio`, sección **"Recargar Wallet"**.
- Tabla de **"Mis recargas recientes"**.
- Comprobante/resumen de recarga tras confirmar.
- Producción no tocada en ningún momento.

---

## Regla de negocio

Un cajero de comercio recibe efectivo físico de un usuario XPAY y recarga su Wallet desde
el portal Mi Comercio. El efectivo **no entra todavía** a caja/banco de XPAY — queda
registrado contablemente como un activo por cobrar/recaudar al comercio, no como efectivo
recibido en cuentas propias de XPAY. La liquidación real del efectivo (cierre de caja del
comercio, consignación a XPAY, conciliación bancaria) queda fuera de alcance de esta fase.

---

## Contabilidad

Recarga de Wallet en efectivo:

| Cuenta | Naturaleza |
|---|---|
| `130107` Efectivo por Recaudar en Comercios | D |
| `210101` Obligación Wallet Usuarios | C |

**Ejemplo validado:**
- DR `130107` $100.000
- CR `210101` $100.000

Ledger balanceado (DR = CR) verificado en cada recarga ejecutada durante la validación.

---

## Migración

**`database/025_wallet_recargas_comercio.sql`** — aplicada en QA, idempotente.

Objetos creados/verificados:

- **Cuenta ledger:**
  - `130107` Efectivo por Recaudar en Comercios
  - tipo `ACTIVO`
  - naturaleza `D`
  - estado `ACTIVA`

- **Tabla:**
  - `wallet_recargas_comercio`

- **Índices:**
  - `ix_wallet_recargas_comercio_comercio_fecha`
  - `ix_wallet_recargas_comercio_cajero_fecha`
  - `ix_wallet_recargas_comercio_wallet_fecha`
  - `ix_wallet_recargas_comercio_ledger`

### Nota operativa

La migración 025 requirió parches puntuales en Azure SQL QA porque algunos objetos
reportados por el editor SQL del responsable como ejecutados exitosamente (incluido el
`PRINT` final de fin de migración) no quedaron persistidos en la base de datos en el
primer intento — tanto la fila de la cuenta ledger `130107` como, en una segunda ronda,
la tabla `wallet_recargas_comercio` y sus índices. Se corrigió manualmente aplicando el
mismo bloque `IF NOT EXISTS` idempotente de la migración original, y se verificó en ambos
casos por `SELECT` directo contra `ledger_cuentas`, `sys.tables` y `sys.indexes`:

- cuenta `130107` `ACTIVA` ✅
- tabla `wallet_recargas_comercio` existente ✅
- los 4 índices existentes ✅

---

## Endpoints implementados

- `GET  /api/comercio/wallet-recargas/buscar-usuario?query=`
- `POST /api/comercio/wallet-recargas`
- `GET  /api/comercio/wallet-recargas/mis-recargas`

---

## Seguridad

- El backend deriva `idUsuarioCajero` desde el claim `idUsuario` del JWT — nunca del body.
- No acepta `idComercio` desde el frontend.
- No acepta `idTienda` desde el frontend.
- No acepta `idWallet` desde el frontend.
- El scope comercio se resuelve con `ComercioScopeService.RequireScopeAsync`.
- `CAJERO` y `ADMIN_SEDE_COMERCIO` requieren sede (`IdEstablecimiento`) asignada — validado
  con un chequeo defensivo explícito.
- Usuario sin rol comercio recibe `HTTP 403`.
- Valor mínimo QA: $1.000.
- Valor máximo QA: $2.000.000.
- PIN QA: exactamente 7 dígitos numéricos.
- Transacción atómica (`BeginTransactionAsync` / `CommitAsync` / `RollbackAsync`).
- Lock pesimista `WITH (UPDLOCK, ROWLOCK)` sobre `wallet_saldos` durante la recarga.
- Rollback completo ante cualquier error dentro de la transacción.

---

## Frontend

En `/mi-comercio` se agregó la sección **"Recargar Wallet"**, visible para los 3 roles
operativos de comercio. Incluye:

- búsqueda de usuario por documento/celular/usuario;
- selección de usuario desde los resultados;
- saldo actual del usuario seleccionado;
- campo de valor de recarga;
- campo de PIN QA (7 dígitos);
- campo de observaciones opcional;
- confirmación de la recarga;
- comprobante de la operación tras confirmar;
- tabla "Mis recargas recientes".

Sin impresión PDF (fuera de alcance de esta fase).

---

## Validación end-to-end — resultado

**Usuario de comercio:** `qa.comercio1`
**Usuario destino:** `qa.usuario1`

### Recarga principal

| Campo | Valor |
|---|---|
| Valor | $100.000 |
| Wallet | #2 |
| Saldo antes | $109.565 |
| Saldo después | $209.565 |
| Movimiento wallet | `RECARGA_EFECTIVO_COMERCIO` |
| Recarga creada | `idRecarga=1` |
| Transacción ledger | `idTransaccionLedger=122` |
| Asiento | DR `130107` $100.000 / CR `210101` $100.000 |
| Ledger | Balanceado DR=CR ✅ |

---

## Fix adicional de trazabilidad

Se detectó durante la validación que `WalletMovimiento.ReferenciaId` quedaba `null` tras
crear la recarga, a diferencia de `LedgerTransaccion.ReferenciaId` y
`LedgerMovimiento.ReferenciaId`, que sí se backfilleaban correctamente con el `idRecarga`
una vez creada la fila de `wallet_recargas_comercio`. No era un error contable ni de saldo
(el movimiento seguía siendo 100% rastreable vía `IdTransaccionLedger`), pero rompía la
consistencia del patrón de backfill usado para las otras dos entidades del mismo método.

**Corrección:** se corrigió en un commit aparte para que el `WalletMovimiento` de
`RECARGA_EFECTIVO_COMERCIO` quede referenciado al `idRecarga` correspondiente, dentro del
mismo `SaveChangesAsync` final ya existente. Sin cambios de lógica contable, ledger, saldo,
endpoints ni migración.

### Validación del fix

| Campo | Valor |
|---|---|
| Recarga de prueba adicional | $5.000 |
| Recarga creada | `idRecarga=2` |
| Movimiento wallet | `#112`, `referenciaId=2` |
| Transacción ledger | `#126`, balanceada |
| Asiento | DR `130107` $5.000 / CR `210101` $5.000 |
| Saldo wallet | $137.783 → $142.783 |

La recarga #1 histórica conserva `referenciaId=null` — esperado, ya que el fix no
reescribe datos históricos y solo aplica hacia adelante, en línea con la instrucción de no
tocar migración ni datos existentes.

---

## Pruebas negativas

| # | Prueba | Resultado |
|---|---|---|
| 1 | Valor 0 | Rechazado `HTTP 400` |
| 2 | Valor negativo | Rechazado `HTTP 400` |
| 3 | Valor mayor a $2.000.000 | Rechazado `HTTP 400` |
| 4 | PIN inválido | Rechazado `HTTP 400` |
| 5 | Usuario destino inexistente | Rechazado `HTTP 404` |
| 6 | Usuario sin rol comercio | Rechazado `HTTP 403` |

Todas verificadas sin efectos secundarios:
- saldo wallet sin cambios;
- conteo de recargas (`wallet_recargas_comercio`) sin cambios;
- conteo de transacciones ledger (`WALLET_RECARGA_EFECTIVO_COMERCIO`) sin cambios.

---

## Regresión

- Pago QR normal con Wallet (`POST /api/qr/pagar`) sigue funcionando ✅
- Compra QR con Cupo Ordinario (`POST /api/cartera-ordinaria/pagar-qr-con-cupo`) sigue
  funcionando ✅
- Pago de cuota de Cartera Ordinaria (`POST /api/cartera-ordinaria/pagar-cuota-wallet`)
  sigue funcionando ✅

---

## Commits

| Commit | Descripción |
|--------|-------------|
| `68ba420` | feat: cash wallet recharge by commerce cashier |
| `a983105` | fix: backfill WalletMovimiento.ReferenciaId for cash wallet recharges |

Ambos pusheados a `main`.

---

## Despliegue QA y CI

- Backend (`xpay-api-qa`): desplegado dos veces (feature + fix) — `RuntimeSuccessful` en
  ambos casos, `/health` responde 200.
- Frontend (`xpay-admin-qa`): desplegado — homepage responde 200.
- Base de datos QA (`sqldb-xpay-qa`): migración 025 aplicada manualmente por el responsable
  del proyecto, con parches puntuales intermedios para los objetos que no quedaron creados
  en el primer intento (ver "Nota operativa").
- Smoke test post-deploy: `GET /api/comercio/wallet-recargas/mis-recargas` verificado
  funcional tras cada redeploy.
- GitHub Actions (Backend Validation, Frontend Build, Dependency Security Scan): **3/3 ✅**
  tanto para el commit de feature como para el commit de fix.

---

## Fuera de alcance / próximos pasos

- Cierre de caja diario del comercio.
- Consignación del comercio a XPAY.
- Conciliación bancaria.
- Reversos/anulaciones complejas de recargas.
- Comisiones por recarga.
- Arqueo por denominaciones.
- Límites regulatorios avanzados.
- Integración con banco, Passport, Veriff, Datacrédito.
- Producción — no se tocó en ningún momento de esta fase.
- No se ejecutaron nuevas recargas después del cierre de esta validación.

---

## Conclusión

**Fase 70.1 — APROBADA Y CERRADA ✅**
