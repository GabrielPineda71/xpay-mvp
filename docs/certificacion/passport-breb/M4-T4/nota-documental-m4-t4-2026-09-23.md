# M4-T4 — Create QR Code DYNAMIC — nota documental de cierre técnico

**Caso:** M4-T4 (Create QR Code — DINÁMICO)
**Endpoint:** `POST /v1/qrcodes`
**Ambiente:** Passport Sandbox
**Estado final:** SUCCESS (técnico) — `review_status=PENDING_PASSPORT_REVIEW`

Esta nota consolida, de forma sanitizada, los dos intentos reales
históricos de M4-T4. No reinterpreta los hechos más allá de lo que las
evidencias versionadas ya registran.

## Intento 1 — FAIL

- **Fecha UTC:** `2026-09-23T01:52:52Z`
- **Commit backend/harness:** `899b3da40eccf4b50aa16a920212d8515d9e61fe`
- **Evidencia:** `evidence-2026-09-23T01-52-52Z.json`
  (SHA256 `b9bf14a852951b9f85c46a94975197b259d1bfabd091a4bafba135d102d66d19`)
- **Payload enviado:** `type=DYNAMIC`, `channel=MPOS`, vat/inc/tip
  presentes, **`amount` ausente** (ningún valor de certificación
  confirmado disponible en ese momento).
- **Respuesta de Passport:**
  - `HTTP 400`
  - `error_code=invalid_parameter_value`
  - `error_message="amount is invalid."`
- Sin retry. Se preservó la evidencia sanitizada del fallo.

## Hallazgo intermedio — ejemplo oficial Passport

Se inspeccionó directamente el workspace oficial **"Passport PaaS for
Developers"** (Postman), request **"QR Codes / Create Dynamic QR Code"**.
Su Body de ejemplo incluye:

```json
"amount": { "value": "10000.00", "currency": "COP" }
```

Este valor se adoptó **únicamente para el campo `amount`** — ningún otro
campo de ese mismo ejemplo (p. ej. `additional_info`) se incorporó al
payload de M4-T4 sin evidencia propia de que fuera necesario.

## Intento 2 — SUCCESS

- **Fecha UTC:** `2026-09-23T02:43:10Z`
- **Commit backend/harness:** `e7c266bb68d932b0f17abab042e17ca2fb306bbf`
- **Evidencia:** `evidence-2026-09-23T02-43-10Z.json`
  (SHA256 `94921a4917f63751cb21b3a08f11df77020fceef64f110bea584ec91ef64ecbc`)
- **Payload enviado:**
  - `type=DYNAMIC`
  - `channel=MPOS`
  - `vat` presente
  - `inc` presente
  - `tip` presente
  - `amount` presente — `value="10000.00"`, `currency="COP"`
  - `additional_info` ausente
  - `qr_code_reference` presente (fresco, generado por ejecución)
- **Resultado:**
  - `result=PASS`
  - `status=ACTIVE`
  - `type=DYNAMIC`
  - `qr_code_data` presente
  - `qr_code_image` presente
  - `qr_code_reference` presente (en request y en response)

## Evidencia privada (XPAY-508)

El material crudo (`qr_code_data`, `qr_code_image`,
`qr_code_reference` recibido) del intento 2 quedó preservado en el
destino privado no versionado, fuera del repositorio (mecanismo
`M4T4PrivateCapture`, XPAY-508):

- `PRIVATE_ARTIFACT_CREATED=YES`
- `PRIVATE_ARTIFACT_FILE_COUNT=1`
- `PRIVATE_ARTIFACT_READABLE=YES`
- `PRIVATE_ARTIFACT_HAS_QR_CODE_DATA=YES`
- `PRIVATE_ARTIFACT_HAS_QR_CODE_IMAGE=YES`
- `PRIVATE_ARTIFACT_HAS_QR_CODE_REFERENCE=YES`

Este artefacto privado no se copia, no se stagea ni se commitea al
repositorio — es la fuente controlada para una eventual materialización
de evidencia visual (imagen del QR) y para preparar M4-T5, en tickets
documentales separados.

## Resumen de estado

| Campo | Valor |
|---|---|
| `M4_T4_REAL_EXECUTIONS_TOTAL` | 2 |
| `M4_T4_SUCCESSFUL_EXECUTIONS` | 1 |
| `M4_T4_TECHNICAL_RESULT` | PASS |
| `FINAL_PASSPORT_STATUS` | ACTIVE |
| `FINAL_PASSPORT_TYPE` | DYNAMIC |
| `review_status` | PENDING_PASSPORT_REVIEW |
| `M4_T4_REPEAT_REQUIRED` | NO |

## Alcance de esta nota

No se ejecutó ninguna llamada a Passport para producir esta nota. No se
modificó ninguna de las dos evidencias versionadas históricas. No se
materializó todavía ninguna imagen/PNG documental ni se decodificó
ningún payload — esa preparación queda para un ticket documental
separado y controlado.
