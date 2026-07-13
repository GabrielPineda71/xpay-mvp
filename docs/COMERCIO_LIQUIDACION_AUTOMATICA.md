# Liquidación Automática de Ventas QR — Comercios Aliados

## Propósito

Liberar automáticamente ventas QR que han llegado a su fecha programada de disponibilidad (`fecha_disponible_programada ≤ fecha_corte`) sin requerir acción manual del administrador.

- Sin descuento anticipado — solo aplica el descuento de convenio (negociación comercial).
- Tipo de liberación: `AUTOMATICA`
- Estado resultante en `comercio_ventas_qr_disponibilidad`: `DISPONIBLE`
- Estado resultante en `ventas_qr`: `LIQUIDADA`

## Endpoint manual (admin)

```
POST /api/comercios-aliados/admin/liquidacion-automatica/ejecutar
Authorization: Bearer <ADMIN_XPAY token>
Content-Type: application/json

{
  "fechaCorte": "2026-07-12T23:59:59",   // opcional — default: DateTime.UtcNow
  "soloComercioAliadoId": 2              // opcional — default: todos los comercios aliados
}
```

### Respuesta

```json
{
  "success": true,
  "data": {
    "cantidadProcesadas": 5,
    "totalBruto": 500000.00,
    "totalNetoLiberado": 475000.00,
    "totalDescuento": 25000.00,
    "idsVentasLiquidadas": [14, 15, 16, 17, 18],
    "errores": [],
    "fechaCorteUsada": "2026-07-12 23:59:59"
  }
}
```

## Contabilidad

Cada venta genera su propia transacción ledger independiente:

| Cuenta | Naturaleza | Concepto              | Valor         |
|--------|------------|----------------------|---------------|
| 210201 | DR         | LIBERACION_CONTINGENCIA_QR | ValorBruto |
| 210202 | CR         | OBLIGACION_WALLET_COMERCIO | ValorNeto  |
| 410201 | CR         | INGRESO_DESCUENTO_COMERCIO | ValorDescConvenio |

**No se aplica 410202 (descuento anticipado)** — solo aplica en `LiquidarAhora` (anticipado por comercio).

## Programación diaria — Azure Logic App (recomendado)

No se implementó un `BackgroundService` para evitar complejidad operacional. El enfoque recomendado es una Azure Logic App que llame al endpoint cada día.

### Configuración en Azure Logic App

1. Abrir [Azure Logic Apps](https://portal.azure.com) → crear Logic App (Consumption).
2. Trigger: **Recurrence** → Interval: 1, Frequency: Day, Time zone: SA Pacific Standard Time (UTC-5), Start time: `2000-01-01T06:00:00`.
3. Action: **HTTP**
   - Method: `POST`
   - URI: `https://xpay-api-qa.azurewebsites.net/api/comercios-aliados/admin/liquidacion-automatica/ejecutar`
   - Headers: `Authorization: Bearer <ADMIN_XPAY_TOKEN>`, `Content-Type: application/json`
   - Body: `{}` (o `{"fechaCorte": null}` para usar `DateTime.UtcNow`)
4. Action adicional: **Send an email** o **Teams notification** con el resultado (opcional).

### Horario

| Zona horaria | Hora    |
|-------------|---------|
| Colombia (UTC-5) | 6:00 AM |
| UTC              | 11:00 AM |

> El token de administrador debe ser de larga duración o gestionarse con Azure Key Vault + identidad administrada.

## Idempotencia

- Solo procesa ventas con `estado = NO_DISPONIBLE`.
- Si una venta ya fue procesada (`estado ≠ NO_DISPONIBLE`), lanza excepción capturada en `errores[]`.
- Se puede ejecutar N veces con la misma fecha de corte sin duplicar transacciones.

## Parámetros de descuento (tabla xpay_parametros_liquidacion_anticipada)

Administrados en `/admin/parametros-liquidacion-comercio` de la UI admin.
- Días faltantes 0–60.
- Porcentaje aplica solo en liquidación anticipada (`ANTICIPADA_COMERCIO`), NO en liquidación automática.
