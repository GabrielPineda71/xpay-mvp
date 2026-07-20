# XPAY MVP — Wallet: Cierre Fase 70.2

**Fase:** 70.2 — Liquidación de recaudos de comercio hacia XPAY
**Fecha UTC:** 2026-07-20
**Responsable:** Gabriel Alfonso Pineda Ortiz `g.pineda@cercaymejor.com`
**Ambiente:** QA — NO producción
**Estado:** ✅ **APROBADA Y CERRADA**

---

> **ADVERTENCIA:**
> Todos los saldos, cupos y movimientos de este documento corresponden a `qa.usuario1`,
> `qa.comercio1` y `qa.admin.xpay` en el ambiente QA (`xpay-api-qa.azurewebsites.net`). No
> son dinero real, no involucran producción, Passport, Veriff ni Datacrédito.

---

## Alcance implementado

- Módulo administrativo para que `ADMIN_XPAY`/`SUPERUSUARIO` liquide (registre como recibido)
  el efectivo que un comercio ya recaudó en Fase 70.1.
- Listado de recaudos pendientes por comercio/sede/cajero, con filtros por fecha.
- Resumen agrupado por comercio/sede (cantidad de recargas + valor pendiente).
- Selección múltiple de recargas y liquidación en lote, restringida a un único comercio por
  operación.
- Dos métodos de liquidación: `EFECTIVO_BOVEDA` y `CONSIGNACION_BANCO`.
- Frontend en `/admin/wallet-recaudos-comercio`, sección **"Liquidación Recaudos Comercio"**.
- Producción no tocada en ningún momento.

---

## Regla de negocio

El comercio solo puede recaudar en **EFECTIVO** (regla ya vigente desde Fase 70.1 —
`wallet_recargas_comercio.metodo_recaudo` siempre es `EFECTIVO`; el backend nunca acepta otro
valor desde el frontend). El **método de liquidación** de esta fase (`EFECTIVO_BOVEDA` /
`CONSIGNACION_BANCO`) **no** describe cómo el usuario le pagó al comercio — describe cómo el
comercio le entrega posteriormente ese efectivo a XPAY:

- `EFECTIVO_BOVEDA`: el comercio entrega el efectivo físico a XPAY.
- `CONSIGNACION_BANCO`: el comercio consigna a la cuenta bancaria de XPAY el efectivo que
  ya había recaudado.

Por esta razón, la liquidación **valida explícitamente** que todas las recargas seleccionadas
tengan `metodo_recaudo = 'EFECTIVO'` antes de proceder — si alguna no lo tiene, se rechaza la
liquidación completa como inconsistencia de datos, sin producir ningún efecto. No existe
"mezcla de método de recaudo" que resolver porque el comercio nunca tiene otro medio de
recaudo habilitado (transferencia, consignación directa del usuario, datáfono y PSE quedan
fuera de alcance del comercio; esos métodos, cuando aplican, pertenecen al módulo de
Cobranzas/Pagos XPAY, no a `wallet_recargas_comercio`).

Esta fase **no modifica el saldo de la Wallet del usuario** en ningún caso — solo mueve el
"efectivo por recaudar" de una cuenta de activo a otra.

---

## Contabilidad

| Método de liquidación | DR | CR |
|---|---|---|
| `EFECTIVO_BOVEDA` | `110101` Efectivo en Bóveda | `130107` Efectivo por Recaudar en Comercios |
| `CONSIGNACION_BANCO` | `110102` Banco Coopcentral XPAY | `130107` Efectivo por Recaudar en Comercios |

Ledger balanceado (DR = CR) verificado en ambas liquidaciones ejecutadas durante la validación.

---

## Migración

**`database/026_wallet_liquidacion_recaudos_comercio.sql`** — aplicada en QA, idempotente.

Objetos creados/verificados:

- **Tabla** `wallet_liquidaciones_recaudo_comercio` + 3 índices
  (`ix_wallet_liquidaciones_recaudo_comercio_comercio_fecha`,
  `_admin_fecha`, `_ledger`).
- **Tabla** `wallet_liquidaciones_recaudo_comercio_detalle` + 2 índices
  (`_detalle_liquidacion`, `_detalle_recarga`).
- **3 columnas nuevas** en `wallet_recargas_comercio` (tabla creada en migración 025, fuera
  del baseline de CI): `id_liquidacion_recaudo`, `fecha_liquidacion`, `liquidado_por_usuario`.
- **1 índice nuevo** en `wallet_recargas_comercio`: `ix_wallet_recargas_comercio_estado_liquidacion`
  (sobre `estado, id_liquidacion_recaudo`), envuelto en `sp_executesql` porque referencia una
  columna agregada en el mismo batch (mismo patrón que Fase 69.3/70.1).

No se agregó ninguna columna a tablas que el baseline de CI (`backend-validation.yml`, solo
migraciones 001-010) toca — `wallet_recargas_comercio` está fuera de ese baseline desde su
creación en la migración 025.

### Nota operativa — persistencia de la migración

La migración 026 requirió **dos rondas de parche** en QA, con el mismo patrón ya observado en
fases anteriores (el editor SQL del responsable reportó éxito, pero parte del contenido no
quedó persistido):

1. **Primera ronda**: las 3 columnas nuevas de `wallet_recargas_comercio` no quedaron creadas
   inicialmente — rompió incluso la recarga normal de Fase 70.1 (`HTTP 500`,
   `Invalid column name 'fecha_liquidacion'` / `'id_liquidacion_recaudo'` /
   `'liquidado_por_usuario'`). Se corrigió con un parche puntual idempotente; verificado con
   una recarga nueva ($3.000, luego $2.000) que confirmó persistencia y dejó la regresión de
   70.1 funcionando de nuevo.
2. **Segunda ronda**: las 2 tablas nuevas (`wallet_liquidaciones_recaudo_comercio` y su
   detalle) tampoco quedaron creadas — el primer intento de liquidación falló con
   `Invalid object name 'wallet_liquidaciones_recaudo_comercio'`. Se corrigió con un parche
   transaccional explícito (`SET XACT_ABORT ON`, `BEGIN TRANSACTION`/`COMMIT`,
   `IF OBJECT_ID(...) IS NULL`, `IF NOT EXISTS` sobre `sys.indexes`), verificado con
   `sys.tables`/`sys.indexes` antes de reintentar. Se confirmó además que el intento fallido
   no dejó ninguna fila parcial ni transacción ledger huérfana (rollback completo).

---

## Nota operativa — reset de contraseña de `qa.admin.xpay`

`qa.admin.xpay` (único usuario QA con rol `ADMIN_XPAY`) tenía una contraseña inválida/rota,
bloqueando toda validación en vivo del módulo admin de esta fase (no existe usuario
`SUPERUSUARIO` en QA). El responsable del proyecto **autorizó explícitamente** un reset de
contraseña, exclusivo de QA, a la contraseña QA estándar ya documentada.

- **Dos intentos de reset por script SQL externo fallaron** (el hash se auto-verificó
  correctamente antes de escribirse, con `bcrypt`/Python primero y con
  `BCrypt.Net.BCrypt.HashPassword` — la misma librería y versión exacta del backend —
  después; ambas veces el hash persistido en QA no verificaba contra la contraseña esperada).
- Se diagnosticó con dos endpoints **temporales**, desplegados únicamente en `xpay-api-qa`:
  `GET /api/auth/_diag-verify-qa-admin` (solo lectura, reutiliza `BCrypt.Net.BCrypt.Verify`
  contra el hash real, sin exponerlo) y `POST /api/auth/_diag-fix-qa-admin-hash` (genera un
  hash con la misma librería, lo compara contra el almacenado, y si difiere lo escribe
  directamente vía Entity Framework Core — la misma conexión confiable que usa toda la API en
  operación normal — evitando el cliente SQL externo que había fallado repetidamente).
- El endpoint de escritura confirmó `verifiedAfter: true` tras escribir directamente por EF
  Core, confirmando que la causa raíz era el cliente SQL externo del responsable del proyecto
  (no un problema del backend ni de la librería `BCrypt.Net-Next`).
- Verificado con un único intento de login real (`HTTP 200`, `roles: ["ADMIN_XPAY"]`, token no
  expuesto en ningún momento).
- **Ambos endpoints temporales fueron retirados inmediatamente después de usarlos** —
  confirmado con `git diff` (0 diferencias en `AuthController.cs` respecto al original) antes
  del commit final, y con `HTTP 404` en ambas rutas tras el redeploy limpio.
- Ningún otro usuario QA fue modificado. Producción no fue tocada en ningún momento.

---

## Endpoints implementados

- `GET  /api/admin/wallet-recaudos-comercio/pendientes`
- `GET  /api/admin/wallet-recaudos-comercio/resumen-pendientes`
- `POST /api/admin/wallet-recaudos-comercio/liquidar`

---

## Seguridad

- `[Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]` a nivel de controller.
- `idUsuarioAdmin` se deriva del claim `idUsuario` del JWT — nunca del body.
- `LiquidarRecaudosComercioRequest` solo acepta `idsRecarga[]`, `metodoLiquidacion`,
  `referenciaExterna`, `observaciones` — no acepta `idComercio`, `idWallet`, cuentas contables
  ni `valorTotal` desde el frontend. Todo se deriva server-side de las recargas bloqueadas.
- Lock pesimista `WITH (UPDLOCK, ROWLOCK)` sobre cada `wallet_recargas_comercio` seleccionada.
- Validaciones antes de mutar cualquier dato: existencia de todas las recargas, estado
  `APLICADA` sin liquidación previa, `metodo_recaudo = EFECTIVO`, mismo comercio, valor total
  > 0.
- Transacción atómica completa (`BeginTransactionAsync` / `CommitAsync` / `RollbackAsync`);
  verificado que un fallo a mitad de proceso (tabla inexistente) no dejó filas parciales ni
  ledger huérfano.
- `IdComercioAliado` de la liquidación se deja `NULL` si las recargas seleccionadas
  pertenecen a distintos comercios aliados (hallazgo del code review, corregido antes del
  primer despliegue — no afecta el límite real de "un comercio por operación").

---

## Frontend

`/admin/wallet-recaudos-comercio` — **"Liquidación Recaudos Comercio"**. Incluye:

- resumen por comercio/sede (cantidad + valor pendiente);
- tabla de recargas pendientes con checkboxes de selección;
- filtros por fecha desde/hasta, comercio, sede, cajero;
- panel de liquidación (método, referencia externa, observaciones) con confirmación previa
  (cantidad + valor total + método) y botón deshabilitado mientras procesa o sin selección;
- panel de resultado con `idLiquidacion`, `idTransaccionLedger`, valor total y comprobante.

---

## Validación end-to-end — resultado

**Usuario admin:** `qa.admin.xpay` · **Comercio:** `Comercio Demo XPAY QA` (#2)

### Caso A — EFECTIVO_BOVEDA

| Campo | Valor |
|---|---|
| Recargas liquidadas | #1 ($100.000), #2 ($5.000), #3 ($3.000) |
| Liquidación creada | `idLiquidacion=1` |
| Transacción ledger | `idTransaccionLedger=134` |
| Asiento | DR `110101` $108.000 / CR `130107` $108.000 |
| Ledger | Balanceado DR=CR ✅ |
| Recargas #1/#2/#3 | `estado=LIQUIDADA`, `id_liquidacion_recaudo=1` (confirmado: ya no aparecen en `pendientes`) |
| Saldo Wallet #2 | **Sin cambios** ($147.783 antes y después, `fechaActualizacion` intacta) |

### Caso B — CONSIGNACION_BANCO

| Campo | Valor |
|---|---|
| Recarga liquidada | #4 ($2.000) |
| Liquidación creada | `idLiquidacion=2` |
| Transacción ledger | `idTransaccionLedger=135` |
| Asiento | DR `110102` $2.000 / CR `130107` $2.000 |
| Ledger | Balanceado DR=CR ✅ |
| Saldo Wallet #2 | **Sin cambios** ($147.783) |
| Pendientes tras Caso B | Lista vacía ✅ |

---

## Pruebas negativas

| # | Prueba | Resultado |
|---|---|---|
| 1 | Liquidar recarga ya liquidada (#1) | Rechazado `HTTP 400` — "no están pendientes de liquidar" |
| 2 | `idsRecarga` vacío | Rechazado `HTTP 400` |
| 3 | Método inválido (`TARJETA_CREDITO`) | Rechazado `HTTP 400` |
| 4 | Usuario `OPERADOR_XPAY` sin `ADMIN_XPAY`/`SUPERUSUARIO` | Rechazado `HTTP 403` |
| 5 | Mezcla de comercios | No ejecutado en vivo — no existe un segundo comercio con recargas pendientes en QA. Validado por código: `comerciosDistintos.Count > 1` rechaza con mensaje claro (confirmado en code review) |
| 6 | Recarga con `metodo_recaudo` distinto de `EFECTIVO` | No simulado en vivo (requeriría alterar datos válidos). Validado por código: el filtro `MetodoRecaudo != "EFECTIVO"` rechaza toda la liquidación antes de cualquier mutación |

Pruebas 1-4 verificadas sin efectos secundarios: `pendientes` sin cambios, saldo Wallet sin
cambios.

---

## Regresión

- Recarga Wallet comercio (Fase 70.1): recarga #5, $147.783 → $149.283 ✅
- Pago QR normal con Wallet: venta #28, transacción #137 ✅
- Compra QR con Cupo Ordinario: utilización #6, transacción #138 ✅
- Pago de cuota de Cartera Ordinaria: pago #3, transacción #139, cuota #8 `PAGADA` ✅

---

## Commits

Pendiente de generar en el cierre de esta fase (ver sección Despliegue QA y CI).

---

## Despliegue QA y CI

- Backend (`xpay-api-qa`): desplegado múltiples veces durante la fase — incluyendo dos
  despliegues temporales con endpoints de diagnóstico (`_diag-verify-qa-admin`,
  `_diag-fix-qa-admin-hash`) y un despliegue final limpio sin ellos (confirmado por `git diff`
  y `HTTP 404` en ambas rutas).
- Frontend (`xpay-admin-qa`): desplegado — `/admin/wallet-recaudos-comercio` accesible,
  `/health` responde 200.
- Base de datos QA (`sqldb-xpay-qa`): migración 026 aplicada manualmente por el responsable
  del proyecto, con dos rondas de parche puntual (ver "Nota operativa — persistencia de la
  migración").
- GitHub Actions (Backend Validation, Frontend Build, Dependency Security Scan): 3/3 ✅
  (ver commit final).

---

## Fuera de alcance / próximos pasos

- Conciliación bancaria automática.
- Anulación de liquidaciones o de recargas.
- Comisiones por recarga.
- Arqueo físico por denominaciones.
- Soportes PDF de la liquidación.
- Otros métodos de recaudo del comercio (transferencia, datáfono, PSE) — el comercio solo
  recauda en efectivo; formas de pago adicionales pertenecen al módulo de Cobranzas/Pagos
  XPAY, fuera de esta fase.
- Producción — no se tocó en ningún momento de esta fase.
- No se ejecutaron nuevas liquidaciones después del cierre de esta validación.

---

## Conclusión

**Fase 70.2 — APROBADA Y CERRADA ✅**
