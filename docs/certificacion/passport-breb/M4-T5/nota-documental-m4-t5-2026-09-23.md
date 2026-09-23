# M4-T5 — Decode QR Code DYNAMIC — nota documental de cierre técnico

**Caso:** M4-T5 (Decode QR Code — DINÁMICO, reutiliza el QR de M4-T4)
**Endpoint:** `POST /v1/qrcodes/decode`
**Ambiente:** Passport Sandbox
**Estado final:** SUCCESS (técnico) — `review_status=PENDING_PASSPORT_REVIEW`

Esta nota consolida, de forma sanitizada, la única ejecución real
histórica de M4-T5. No reinterpreta los hechos más allá de lo que la
evidencia versionada y la captura privada ya registran.

## Vínculo M4-T4 → M4-T5

```
M4-T4: Create Dynamic QR — SUCCESS (XPAY-513)
   ↓
artefacto privado preservado (XPAY-508)
   ↓
M4-T5: Decode del MISMO qr_code_data (XPAY-519)
   ↓
qr_code_reference_match_m4_t4 = true
```

M4-T5 **nunca creó un QR nuevo**. El `qr_code_data` usado como input de
esta única llamada Decode se leyó exclusivamente en memoria desde el
artefacto privado ya preservado de M4-T4 — nunca se copió a ningún
archivo versionado ni se imprimió en ningún momento.

## Ejecución real — SUCCESS

- **Fecha UTC:** `2026-09-23T03:37:19Z`
- **Commit backend/harness:** `1b6b67d6b594af4c6d788f4a1ab457fb0b287ab2`
- **Evidencia:** `evidence-2026-09-23T03-37-19Z.json`
  (SHA256 `cc4eb5cf69f33c5495327170657155c798f617d93adf4b1a895036204a9c5267`)
- **Llamadas:** exactamente 1 Decode real, 0 llamadas de cualquier otro
  tipo (Create QR, Payment, Resolve, mutación de llaves).

### Resultado observado

- `result=PASS`
- `status=ACTIVE`
- `type=DYNAMIC`
- `channel=MPOS`
- `qr_code_reference` presente, y **coincide con el de M4-T4**
  (comparación por fingerprint, nunca por valor crudo).
- `amount` presente, con `value`/`currency` coincidentes con el
  esperado del caso de certificación M4-T5 (`10000.00`/`COP` — el
  mismo valor oficial ya usado para crear el QR en M4-T4, XPAY-512).
- `vat` presente.
- `inc` presente.

## Tip — hallazgo real

`PassportDecodeQrCodeResponse` **no modelaba** el campo `tip` al momento
de esta ejecución (auditoría offline, XPAY-516). Por eso `tip` no
aparece en la evidencia técnica sanitizada, ni en ningún
`response_sanitized` histórico de Decode.

La captura privada del JSON HTTP real (mecanismo `PostWithRawCaptureAsync`,
XPAY-517) **sí preservó** ese campo. La inspección estructural local
(offline, sin ninguna llamada nueva a Passport) confirmó:

- `tip` está presente en la respuesta HTTP real.
- Es un objeto con las propiedades `tip_type`/`tip_value` — la misma
  forma ya conocida por el lado `request`/`response` de Create QR
  (`PassportQrTipRequest`/`PassportQrTipResponse`).

Este hallazgo se documenta de forma puramente factual: no se modificó
ningún DTO productivo en este ticket, y no se generaliza como una regla
de contrato nueva de Passport más allá de esta observación puntual.

## Captura privada — preservación, no versionado

El JSON HTTP completo de la respuesta real quedó preservado
**exclusivamente en el destino privado no versionado de M4-T5**
(`PASSPORT_TEST_M4_T5_PRIVATE_CAPTURE_DIR`, mecanismo `M4T5PrivateCapture`,
XPAY-517) — nunca en el repositorio, nunca impreso, nunca en la
evidencia versionable.

El JSON documental (`documentary/decoded-payload-evidence-2026-09-23.json`)
es una **representación sanitizada** construida a partir de ese material
privado — no es una copia del raw HTTP, y omite deliberadamente
`qr_code_data`, `qr_code_reference` crudo, `key_value` crudo, y
cualquier dato de merchant potencialmente identificante.

## No se ejecutó ningún Decode adicional

Toda la preparación documental de este ticket (XPAY-520) fue offline:
inspección estructural del artefacto privado ya existente, sin ninguna
llamada nueva a Passport.

## Resumen de estado

| Campo | Valor |
|---|---|
| `M4_T5_REAL_EXECUTIONS` | 1 |
| `M4_T5_TECHNICAL_RESULT` | PASS |
| `review_status` | PENDING_PASSPORT_REVIEW |
| `M4_T5_REPEAT_REQUIRED` | NO |

## Alcance de esta nota

No se ejecutó ninguna llamada a Passport para producir esta nota ni el
JSON documental que la acompaña. No se modificó ninguna evidencia
histórica (M4-T1/M4-T2/M4-T3/M4-T4/M4-T5). No se modificó ningún DTO
productivo. No se inició M4-T6.
