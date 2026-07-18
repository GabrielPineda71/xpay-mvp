# XPAY MVP — Cartera Ordinaria: Cierre Fase 69.4A

**Fase:** 69.4A — Backend de compra QR usando Cupo Ordinario
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

Fases 69.2 y 69.3 (cerradas) dejaron completo el ciclo `AVANCE_WALLET`: desembolso a Wallet
y pago de cuotas. Fase 69.4A extiende Cartera Ordinaria al segundo tipo de utilización ya
modelado desde 69.1 (`COMPRA_COMERCIO`): pagar una compra QR usando el cupo ordinario en
vez de debitar la Wallet.

- **Solo backend/API** — validado por curl contra QA.
- **No incluye selector visual Wallet/Cupo** en el flujo de pago QR del frontend.
- El frontend (selector, simulación previa, confirmación con PIN, recibo) queda para
  **Fase 69.4B**.
- El pago QR normal con Wallet (`POST /api/qr/pagar`) **no se rompió** — verificado con
  prueba de regresión explícita.
- No se tocó producción en ningún momento.

---

## Migración

**`database/024_cartera_compra_qr_cupo.sql`** — aplicada en QA.

Cuenta ledger creada:
- `130106` Cartera Ordinaria - Compra Comercio (ACTIVO, D)

Columna creada:
- `cartera_utilizaciones.id_venta_qr` (referencia utilización → venta QR)

> **Nota:** `ventas_qr.id_utilizacion_cartera` se planeó como referencia inversa
> (venta → utilización), pero se retiró del modelo C# antes del cierre — no era
> necesaria (la relación queda completamente cubierta por `cartera_utilizaciones.id_venta_qr`)
> y su mapeo en Entity Framework rompía GitHub Actions (ver "Bugs encontrados y corregidos",
> ítem 2). Si la columna quedó creada en QA por una versión anterior del script, es un
> campo inerte: no se usa en ningún código y no afecta la operación.

---

## Endpoint

`POST /api/cartera-ordinaria/pagar-qr-con-cupo`

**Request validado:**
```json
{
  "qrCode": "QR-DEMO-XPAY-QA-001",
  "valorCompra": 60000,
  "plazoMeses": 1,
  "frecuencia": "MENSUAL",
  "pin": "1234567"
}
```

El usuario se deriva siempre del claim `idUsuario` del JWT — nunca del body.

---

## Contabilidad

Compra QR con Cupo Ordinario:

| Cuenta | Naturaleza |
|---|---|
| `130106` Cartera Ordinaria - Compra Comercio | D |
| `210201` Ventas QR en Contingencia Comercios | C |

Misma cuenta de contingencia (`210201`) que ya usa el pago QR con Wallet — la venta entra
al mismo pipeline de disponibilidad/liquidación existente sin modificarlo. Ledger balanceado
DR=CR en cada compra validada.

---

## Validación end-to-end — resultado

**Usuario de prueba:** `qa.usuario1` (idUsuario=3, idWallet=2)

### Resultados concretos

| Evento | Resultado |
|---|---|
| Compra inicial de prueba ($80.000) | Detectó bug: disponibilidad no persistida |
| Fix aplicado | `SaveChangesAsync()` agregado tras `TryRegistrarDisponibilidadAsync` |
| Compra posterior ($50.000) | Disponibilidad registrada correctamente, validado |
| Cupo usado durante las pruebas | $0 → $80.000 → $130.000 → $190.000 (tras pruebas adicionales de regresión) |
| Cupo disponible durante las pruebas | $1.000.000 → $920.000 → $870.000 → $810.000 |
| Wallet de `qa.usuario1` | **Sin cambios en ninguna compra con cupo** |
| Ledger | Balanceado (DR 130106 = CR 210201) en cada compra con cupo |

### Checklist de validación

- Wallet no se movió en compras con cupo ✅
- Cupo se descuenta correctamente ✅
- Utilización `COMPRA_COMERCIO` creada, estado `DESEMBOLSADO` ✅
- Cuotas creadas con `saldoCuota` inicializado a `valorTotal` (no se repitió el bug de Fase 69.3) ✅
- Venta QR creada en estado `CONTINGENCIA` ✅
- Disponibilidad del comercio registrada (`comercio_ventas_qr_disponibilidad`) ✅
- Ledger balanceado DR `130106` = CR `210201` ✅
- Sin Passport ✅ · Sin Veriff ✅ · Sin Datacrédito ✅ · Sin producción ✅

---

## Bugs encontrados y corregidos

**1. `TryRegistrarDisponibilidadAsync` no persistía — disponibilidad se perdía silenciosamente.**
El método solo hace `db.Add(...)` internamente (no llama `SaveChangesAsync()` él mismo,
igual que en su llamador original `PagoQrService.PagarQrAsync`, que sí tiene un
`SaveChangesAsync()` posterior). La primera versión de `PagarQrConCupoAsync` no tenía ese
`SaveChangesAsync()` de seguimiento.
- **Resultado antes del fix:** la venta QR y la utilización se creaban correctamente, pero
  la fila de disponibilidad nunca llegaba a la base de datos — sin error, sin log, sin
  romper la compra (el `try/catch` alrededor solo atrapa excepciones, y aquí no había
  ninguna: el `Add()` simplemente nunca se guardó).
- **Corrección:** se agregó `await db.SaveChangesAsync();` justo después del bloque
  `try/catch` que llama a `TryRegistrarDisponibilidadAsync`.
- **Validado:** nueva compra QR con cupo mostró la fila de disponibilidad correctamente
  registrada y visible vía `GET /api/comercio/ventas-no-disponibles`.

**2. `ventas_qr.id_utilizacion_cartera` mapeada en EF rompía GitHub Actions.**
- **Causa:** el pipeline de CI (`backend-validation.yml`) solo aplica las migraciones
  001-010 sobre una base de datos nueva — nunca 011-024 (mismo patrón que todas las fases
  de Cartera Ordinaria y Libranza anteriores). `ventas_qr` es una tabla que sí toca el
  flujo QR normal, ya cubierto por el baseline de CI (`FASE 3: Pago a comercio por QR`).
  Al mapear una columna nueva sobre ese modelo, **cualquier** consulta de Entity Framework
  contra `ventas_qr` —incluida la del endpoint no relacionado `/api/qr/pagar`— fallaba con
  `Invalid column name 'id_utilizacion_cartera'` en el ambiente de CI, donde esa columna
  nunca se creó.
- **Corrección:** se retiró la propiedad `IdUtilizacionCartera` del modelo `VentaQr` y su
  mapeo en `XpayDbContext`. La relación utilización↔venta queda cubierta enteramente por
  `cartera_utilizaciones.id_venta_qr` (columna en una tabla que el baseline de CI nunca
  toca).

---

## Pruebas negativas

| Prueba | Resultado |
|---|---|
| Monto superior al cupo disponible | Rechazado `HTTP 400`, sin mover cupo/wallet/ledger |
| PIN inválido (formato) | Rechazado `HTTP 400`, sin efectos secundarios |
| QR inexistente | Rechazado `HTTP 400`, sin efectos secundarios |

---

## Regresión — pago QR normal con Wallet

`POST /api/qr/pagar` verificado después de ambos fixes:
- Sigue funcionando exactamente igual que antes de esta fase.
- Wallet se debitó correctamente en el flujo con Wallet (no en el flujo con cupo).
- Ledger sigue generando `DR 210101` (Obligación Wallet Usuarios) / `CR 210201` (Ventas QR
  en Contingencia Comercios), balanceado.
- **El flujo existente no se rompió.**

---

## Commits

| Commit | Descripción |
|--------|-------------|
| `48a3ec9` | feat: pay QR with ordinary credit cupo |
| `1ca3d47` | fix: remove unused ventas_qr.id_utilizacion_cartera EF mapping (CI regression) |

Ambos pusheados a `main`.

## Despliegue QA y CI

- Backend (`xpay-api-qa`): desplegado — `RuntimeSuccessful`, `/health` responde 200.
- Base de datos QA (`sqldb-xpay-qa`): migración 024 aplicada manualmente por el responsable
  del proyecto (con un fix puntual intermedio para las columnas que no habían quedado
  creadas en el primer intento).
- GitHub Actions (Backend Validation, Frontend Build, Dependency Security Scan): 3/3 ✅
  tras el fix del ítem 2 de bugs.

---

## Fuera de alcance / próximos pasos

- Selector Wallet/Cupo en el frontend del flujo de pago QR (Fase 69.4B).
- Simulación previa, confirmación con PIN y recibo en frontend para este flujo (69.4B).
- Gastos de cobranza, mora, pago anticipado con condonación (mismos límites que 69.2/69.3).
- No se ejecutaron nuevas compras después del cierre de esta validación.
