# XPAY MVP — Flujo QR de Dinero QA/Demo

**Fase:** 57 (QR money flow demo)  
**Fecha UTC:** 2026-06-19  
**Responsable:** Gabriel Alfonso Pineda Ortiz `g.pineda@cercaymejor.com`  
**Ambiente:** QA/Demo — NO producción · NO dinero real · NO datos reales

---

> **ADVERTENCIA:**
> Todos los saldos, transferencias y pagos de este documento son ficticios.
> No representan dinero real. No involucran operaciones financieras reales.
> Uso exclusivo QA/Demo. No ejecutar en producción.

---

## Objetivo

Habilitar flujos de QR interactivos para la demo QA de XPAY:

- **A.** Recibir dinero entre usuarios (usuario genera QR, otro escanea y transfiere).
- **B.** Enviar dinero escaneando el QR del receptor.
- **C.** Confirmar transferencia/pago con clave numérica de 7 dígitos.
- **D.** Comercio genera, muestra y descarga su QR de cobro.

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

Con valor opcional:

```json
{
  "type": "XPAY_MERCHANT_PAYMENT",
  "env": "QA",
  "version": 1,
  "merchantName": "Comercio Demo XPAY QA",
  "qrCode": "QR-DEMO-XPAY-QA-001",
  "amount": null,
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
   - **Escanear QR** (requiere `BarcodeDetector` + cámara) → apuntar al QR del receptor.
   - **Pegar contenido QR** → pegar el JSON del QR en el textarea.
   - **Ingresar destino manualmente** → digitar el ID de wallet.
4. El sistema lee el QR y llena:
   - Wallet destino: `#3 (qa.usuario2)`
   - Valor: del QR si viene, o pide al emisor si es `null`.
5. Si el QR no trae valor → campo "Valor a transferir" habilitado para digitar.
6. Ingresar **clave de 7 dígitos** (ver sección PIN).
7. Clic **Enviar dinero** → `POST /api/wallets/transferencia`.
8. Saldo se actualiza automáticamente → ver en pestaña Saldo o Movimientos.

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

### Pendientes para producción (ver sección Pendientes)

- Validación backend con hash.
- Intentos fallidos con bloqueo temporal.
- Biometría como alternativa.

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
   - **Escanear QR del comercio** (con cámara).
   - **Pegar código QR** → JSON o texto plano `QR-DEMO-XPAY-QA-001`.
4. Sistema llena: código = `QR-DEMO-XPAY-QA-001`, valor si viene en QR.
5. Si no trae valor → digitar valor.
6. Ingresar clave de 7 dígitos.
7. Clic **Pagar QR** → `POST /api/qr/pagar`.
8. Saldo actualiza, venta queda en estado `CONTINGENCIA`.
9. Admin puede verla en Ventas QR.

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
| Generación QR | `qrcode` npm v1.x | Estable, mantenida activamente, zero-dep runtime, funciona en browser via canvas, output PNG/SVG/dataURL |
| Lectura QR | `BarcodeDetector` (Web API nativa) | Zero-dep, disponible Chrome/Edge/Android/Safari 17+; sin overhead de librería adicional |
| Fallback lectura | Textarea de texto | Funciona en todos los navegadores; el usuario pega el JSON del QR |
| Fallback destino manual | Input numérico de wallet ID | Mantiene compatibilidad con el flujo original |

### `BarcodeDetector` — soporte de navegadores

| Navegador | Soporte |
|-----------|---------|
| Chrome (desktop/Android) | ✅ |
| Edge | ✅ |
| Safari 17+ | ✅ |
| Firefox | ❌ (pendiente) |
| Safari <17 | ❌ (fallback textarea) |

En navegadores sin `BarcodeDetector`, el botón "Escanear QR" no aparece y se muestra el textarea de texto. El flujo manual siempre está disponible.

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

---

## Prueba en celular

1. Abrir `https://xpay-admin-qa.azurewebsites.net` desde el navegador del celular.
2. Login con el usuario QA.
3. Para escanear: usar Chrome (Android) o Safari 17+ (iOS).
4. Dar permiso de cámara cuando se solicite.
5. La cámara trasera se selecciona automáticamente (`facingMode: 'environment'`).
6. Si hay error de cámara → usar el textarea para pegar el JSON del QR.

---

## Fallback manual

Siempre disponible cuando:

- El navegador no soporta `BarcodeDetector`.
- La cámara no está disponible o el usuario deniega el permiso.
- El usuario prefiere ingresar datos directamente.

**Opciones de fallback:**

| Operación | Fallback |
|-----------|---------|
| Enviar dinero | Pegar JSON del QR en textarea, o clic "Ingresar destino manualmente" → digitar ID wallet |
| Pagar comercio QR | Pegar JSON del QR o texto plano `QR-DEMO-XPAY-QA-001` en textarea |
| Recibir / Generar QR | No requiere cámara — genera QR siempre disponible |
| QR Comercio | No requiere cámara — genera QR siempre disponible |

---

## Pendientes para producción

| Pendiente | Descripción |
|-----------|------------|
| PIN backend con hash | El servidor debe validar un PIN hasheado (BCrypt/Argon2) sin recibirlo en texto plano. El frontend enviaría el hash o el backend usaría challenge-response |
| Intentos fallidos y bloqueo | Limitar intentos de PIN (ej. 3 intentos → bloqueo 30 min) con registro en BD |
| Biometría como alternativa al PIN | En app móvil: Face ID / huella como alternativa al PIN de 7 dígitos |
| Firma de transacción | Firmar el payload de la transferencia con la clave privada del usuario |
| Expiración de QR | El QR debe tener timestamp de generación y expirar (ej. 15 min) para evitar replay |
| QR dinámico firmado | Firmar el QR con clave del servidor + timestamp; el frontend/backend validan la firma antes de procesar |
| Antifraude | Límites de monto por operación, detección de patrones anómalos, alerta en tiempo real |
| Módulo libranza | `qa.empresa1` tiene vista informativa — pendiente implementación real de libranza |
| Notificaciones push | Notificar receptor cuando recibe dinero |
| QR imprimible | Generación de PDF/recibo con QR para comercios |
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

---

*Documento creado en Fase 57. Actualizar cuando se implementen los pendientes de producción o se modifique la estructura del QR.*
