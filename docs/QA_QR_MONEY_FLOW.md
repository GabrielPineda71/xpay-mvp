# XPAY MVP — Flujo QR de Dinero QA/Demo

**Fase:** 59 (wallet auto-refresh)
**Actualizado:** 2026-06-20
**Responsable:** Gabriel Alfonso Pineda Ortiz `g.pineda@cercaymejor.com`
**Ambiente:** QA/Demo — NO producción · NO dinero real · NO datos reales

---

> **ADVERTENCIA:**
> Todos los saldos, transferencias y pagos de este documento son ficticios.
> No representan dinero real. No involucran operaciones financieras reales.
> Uso exclusivo QA/Demo. No ejecutar en producción.

---

## Objetivo

Habilitar flujos de QR interactivos y refresco automático para la demo QA de XPAY:

- **A.** Recibir dinero entre usuarios (usuario genera QR, otro escanea y transfiere).
- **B.** Enviar dinero escaneando el QR del receptor.
- **C.** Confirmar transferencia/pago con clave numérica de 7 dígitos.
- **D.** Comercio genera, muestra y descarga su QR de cobro.
- **E.** Wallet se actualiza automáticamente cada 7 segundos sin cerrar sesión.

---

## Estructura JSON del QR

### QR de transferencia entre usuarios (`type = XPAY_TRANSFER`)

```json
{
  "type": "XPAY_TRANSFER",
  "env": "QA",
  "version": 1,
  "receiverUser": "qa.usuario2",
  "receiverWalletId": 3,
  "amount": 5000,
  "currency": "COP"
}
```

Con valor opcional (el emisor digita el monto):

```json
{
  "type": "XPAY_TRANSFER",
  "env": "QA",
  "version": 1,
  "receiverUser": "qa.usuario2",
  "receiverWalletId": 3,
  "amount": null,
  "currency": "COP"
}
```

Campos:

| Campo | Requerido | Descripción |
|-------|-----------|-------------|
| `type` | ✅ | Siempre `XPAY_TRANSFER` |
| `env` | ✅ | Siempre `QA` (el frontend rechaza QRs de otros ambientes) |
| `version` | ✅ | Siempre `1` |
| `receiverUser` | ✅ | Username del receptor |
| `receiverWalletId` | ✅ | ID del wallet destino |
| `amount` | — | Monto en COP o `null` si el emisor elige |
| `currency` | ✅ | Siempre `COP` |

### QR de comercio (`type = XPAY_MERCHANT_PAYMENT`)

```json
{
  "type": "XPAY_MERCHANT_PAYMENT",
  "env": "QA",
  "version": 1,
  "merchantName": "Comercio Demo XPAY QA",
  "qrCode": "QR-DEMO-XPAY-QA-001",
  "amount": 5000,
  "currency": "COP"
}
```

### QR texto plano (compatible para pago QR)

El campo "Pegar código QR" del wallet acepta también texto plano por compatibilidad:

```
QR-DEMO-XPAY-QA-001
```

En este caso el usuario debe digitar el valor manualmente.

---

## Flujo A — Recibir dinero (qa.usuario2)

1. Login con `qa.usuario2`.
2. Abrir Mi Wallet → pestaña **Recibir**.
3. Ingresar valor opcional (ej. `$5,000`) o dejar vacío.
4. Clic **Generar QR**.
5. QR se muestra en pantalla (imagen PNG en canvas).
6. Clic **Descargar QR PNG** → descarga como `xpay-recibir-qa.usuario2.png`.
7. Mostrar el QR al emisor (en pantalla o impreso).

> El QR contiene: `type=XPAY_TRANSFER`, `receiverWalletId=3`, `receiverUser=qa.usuario2`.
> No ejecuta ninguna operación — solo genera el QR para presentar.

---

## Flujo B — Enviar dinero escaneando QR (qa.usuario1)

1. Login con `qa.usuario1`.
2. Abrir Mi Wallet → pestaña **Enviar**.
3. Opciones:
   - **Escanear QR** (usa `html5-qrcode`, funciona en iPhone/Safari/Firefox) → apuntar al QR del receptor.
   - **Pegar contenido QR** → pegar el JSON del QR en el textarea.
   - **Ingresar destino manualmente** → digitar el ID de wallet.
4. El sistema lee el QR y llena:
   - Wallet destino: `#3 (qa.usuario2)`
   - Valor: del QR si viene, o pide al emisor si es `null`.
5. Si el QR no trae valor → campo "Valor a transferir" habilitado para digitar.
6. Ingresar **clave de 7 dígitos** (ver sección PIN).
7. Clic **Enviar dinero** → `POST /api/wallets/transferencia`.
8. Saldo se actualiza inmediatamente en la vista del emisor.
9. La vista del **receptor** (qa.usuario2) se actualiza automáticamente en ≤7 segundos sin necesidad de cerrar sesión.

> **Importante:** Escanear solo rellena datos. La transferencia NO ocurre hasta confirmar.

---

## Flujo C — Confirmar con clave de 7 dígitos

Antes de ejecutar cualquier operación (transferencia o pago QR), el sistema pide una **clave de 7 dígitos numéricos**.

### Comportamiento en QA/Demo (esta fase)

- Validación solo de **formato**: exactamente 7 dígitos numéricos (`/^\d{7}$/`).
- No hay validación backend en esta fase.
- El PIN se limpia del campo inmediatamente después del submit (exitoso o fallido).
- El PIN **nunca** se almacena en localStorage, sessionStorage ni en el DOM.
- Demo: usar cualquier combinación de 7 dígitos (ej. `1234567`).

> **PIN demo para esta fase:** cualquier 7 dígitos — `<pin-demo-7-digitos-por-canal-seguro>`
> No se expone en documentación pública por convención de seguridad.

---

## Flujo D — QR de comercio (qa.comercio1)

1. Login con `qa.comercio1`.
2. Abrir Mi Comercio → sección **QR del comercio**.
3. Ingresar valor opcional o dejar vacío.
4. Clic **Generar QR comercio**.
5. QR se muestra en pantalla con código `QR-DEMO-XPAY-QA-001`.
6. Clic **Descargar QR PNG** → descarga como `xpay-comercio-QR-DEMO-XPAY-QA-001.png`.
7. Clic **Copiar JSON** → copia el payload JSON al portapapeles para pruebas.

> El QR generado puede ser escaneado por cualquier usuario QA desde Mi Wallet → Pagar QR.

---

## Flujo E — Pagar comercio con QR (qa.usuario1 o qa.usuario2)

1. Login como usuario wallet.
2. Mi Wallet → pestaña **Pagar QR**.
3. Opciones:
   - **Escanear QR del comercio** (con cámara — `html5-qrcode`).
   - **Pegar código QR** → JSON o texto plano `QR-DEMO-XPAY-QA-001`.
4. Sistema llena: código = `QR-DEMO-XPAY-QA-001`, valor si viene en QR.
5. Si no trae valor → digitar valor.
6. Ingresar clave de 7 dígitos.
7. Clic **Pagar QR** → `POST /api/qr/pagar`.
8. Saldo actualiza, venta queda en estado `CONTINGENCIA`.
9. Admin puede verla en Ventas QR.

---

## Flujo F — Refresco automático de wallet (Fase 59)

La vista Mi Wallet consulta el endpoint de estado de cuenta cada **7 segundos** en segundo plano.

### Comportamiento

| Evento | Comportamiento |
|--------|---------------|
| Nuevo movimiento detectado (crédito) | Toast verde: "Recibiste dinero. Saldo actualizado." |
| Nuevo movimiento detectado (débito) | Toast verde: "Movimiento realizado. Saldo actualizado." |
| Error de red en polling | Aviso discreto: "No se pudo actualizar automáticamente. Usa 'Actualizar ahora'." |
| Transacción en curso (enviar/pagar) | Polling pausado hasta que la operación finalice |
| Barra de estado | "↻ Actualización automática activa · Última actualización: HH:mm:ss" |
| Botón manual | "Actualizar ahora" → recarga completa con spinner (igual que carga inicial) |
| Desmontar componente | Intervalo cancelado con `clearInterval` |

### Detección de movimiento nuevo

La función `pollRefresh` compara `movimientos[0].idMovimiento` con la última ID conocida (`lastKnownMovIdRef`). Solo dispara el toast si `idMovimiento` aumentó desde la última verificación.

- La **carga inicial** y el botón **"Actualizar ahora"** actualizan la baseline sin disparar toast.
- Solo el **polling automático** puede disparar el toast.

### Límites de esta fase

- Polling activo: 7 segundos (read-only, sin presión en backend QA).
- **Pendiente producción:** reemplazar con SignalR/WebSocket push. Ver sección Pendientes.

---

## Mapa demo — usuarios y wallets QA

| Usuario | idWallet | Rol | Vista |
|---------|----------|-----|-------|
| qa.usuario1 | 2 | (ninguno) | Mi Wallet |
| qa.usuario2 | 3 | (ninguno) | Mi Wallet |
| qa.comercio1 | (wallet 4 del comercio) | COMERCIO | Mi Comercio |
| qa.admin.xpay | — | ADMIN_XPAY | Panel admin |

---

## Tecnología QR — decisiones de implementación

| Componente | Tecnología | Justificación |
|-----------|------------|--------------|
| Generación QR | `qrcode` npm v1.x | Estable, zero-dep runtime, output PNG/SVG/dataURL |
| Lectura QR | `html5-qrcode@2.3.8` | Funciona en iOS Safari, Firefox, Android — sin restricción de navegador |
| Fallback lectura | Textarea de texto | Siempre disponible — pegar JSON o código |
| Fallback destino manual | Input numérico de wallet ID | Compatible con flujo original |
| Auto-refresh | `setInterval` polling (7s) | QA/Demo — producción usará SignalR/WebSocket |

### `html5-qrcode` — soporte de navegadores

| Navegador | Soporte escaneo QR |
|-----------|---------|
| Chrome (desktop/Android) | ✅ |
| Edge | ✅ |
| Safari (iOS — todos) | ✅ |
| Firefox | ✅ |
| Safari <14 | ⚠️ Puede requerir HTTPS (App Service QA sirve HTTPS) |

La librería `html5-qrcode` usa `BarcodeDetector` internamente como optimización cuando está disponible, y cae al decoder JavaScript en caso contrario. El botón "Escanear QR" siempre se muestra sin detección de features.

---

## Seguridad QA/Demo

| Control | Estado |
|---------|--------|
| Escanear QR no paga automáticamente | ✅ Solo rellena datos; requiere botón confirmar + PIN |
| Transferir no ocurre sin confirmar | ✅ Botón deshabilitado hasta: QR leído + valor > 0 + PIN 7 dígitos |
| Pagar QR no ocurre sin confirmar | ✅ Ídem |
| PIN no se guarda en localStorage | ✅ Solo en estado React, limpiado post-submit |
| No hay datos reales | ✅ Saldos ficticios, sin dinero real |
| No hay secretos en código | ✅ Sin PIN real hardcodeado |
| No se toca producción | ✅ |
| Validar env=QA en QR | ✅ Frontend rechaza QRs con env≠QA |
| No transferir a wallet propia | ✅ Frontend valida receiverWalletId ≠ idWallet propio |
| Polling no ejecuta transacciones | ✅ Solo GET read-only |
| Polling pausado durante transacciones | ✅ `opInProgressRef` previene race conditions |

---

## Prueba en celular

1. Abrir `https://xpay-admin-qa.azurewebsites.net` desde el navegador del celular.
2. Login con el usuario QA.
3. Para escanear: usar cualquier navegador moderno (Chrome, Safari, Firefox).
4. Dar permiso de cámara cuando se solicite.
5. La cámara trasera se selecciona automáticamente (`facingMode: 'environment'`).
6. Si hay error de cámara → usar el textarea para pegar el JSON del QR.
7. El saldo del receptor se actualiza automáticamente en ≤7 segundos (sin necesidad de recargar la página).

---

## Fallback manual

Siempre disponible:

| Operación | Fallback |
|-----------|---------|
| Enviar dinero | Pegar JSON del QR en textarea, o clic "Ingresar destino manualmente" → digitar ID wallet |
| Pagar comercio QR | Pegar JSON del QR o texto plano `QR-DEMO-XPAY-QA-001` en textarea |
| Recibir / Generar QR | No requiere cámara — genera QR siempre disponible |
| QR Comercio | No requiere cámara — genera QR siempre disponible |
| Actualizar saldo | Botón "Actualizar ahora" — disponible en cualquier momento |

---

## Pendientes para producción

| Pendiente | Descripción |
|-----------|------------|
| SignalR / WebSocket push | Reemplazar el polling de 7s con notificaciones push en tiempo real. El polling actual es solo para QA/Demo |
| PIN backend con hash | El servidor debe validar un PIN hasheado (BCrypt/Argon2) sin recibirlo en texto plano |
| Intentos fallidos y bloqueo | Limitar intentos de PIN (ej. 3 intentos → bloqueo 30 min) |
| Biometría como alternativa al PIN | En app móvil: Face ID / huella como alternativa al PIN de 7 dígitos |
| Firma de transacción | Firmar el payload de la transferencia con la clave privada del usuario |
| Expiración de QR | El QR debe tener timestamp de generación y expirar (ej. 15 min) para evitar replay |
| QR dinámico firmado | Firmar el QR con clave del servidor + timestamp |
| Antifraude | Límites de monto por operación, detección de patrones anómalos |
| Backend PIN validation endpoint | `POST /api/auth/validar-pin` para validar PIN antes de transacción |

---

## Advertencias QA/Demo

- ✅ Sin dinero real involucrado.
- ✅ Sin datos personales reales.
- ✅ Saldos ficticios creados para demostración.
- ✅ Transferencias y pagos registrados en ledger QA (no producción).
- ✅ El PIN de 7 dígitos en QA es solo validación de formato.
- ✅ El QR generado contiene `env=QA` — el sistema rechaza QRs de otros ambientes.
- ✅ No se puede transferir a la propia wallet.
- ✅ El polling de 7s solo hace GET — no escribe datos, no ejecuta transacciones.

---

*Documento creado en Fase 57. Actualizado en Fase 58 (html5-qrcode móvil) y Fase 59 (wallet auto-refresh). Actualizar cuando se implemente SignalR/WebSocket push o se modifique la estructura del QR.*
