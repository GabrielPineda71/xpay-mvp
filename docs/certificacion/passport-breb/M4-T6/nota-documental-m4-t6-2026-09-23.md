# M4-T6 — Casos de error de QR Code DYNAMIC — nota documental de cierre técnico

**Caso:** M4-T6 (Create QR Code — DINÁMICO — casos de error/negativos)
**Endpoint:** `POST /v1/qrcodes`
**Ambiente:** Passport Sandbox
**Estado final:** COMPLETE (técnico) — `review_status=PENDING_PASSPORT_REVIEW`

Esta nota consolida, de forma sanitizada, las tres ejecuciones reales
históricas de M4-T6 (una por subcaso, sin reintentos). No reinterpreta
los hechos más allá de lo que las evidencias versionadas ya registran.

## Objetivo y fuente

M4-T6 corresponde literalmente al requisito del Anexo oficial de
certificación (sección 3.4, "Módulo 4: Flujos de Códigos QR" → "QR
dinámico" → "Validación de errores"):

> "Crear un código QR con una clave Bre-B suspendida. Crear un código QR
> con una clave Bre-B eliminada. Crear un código QR con un customer_id
> incorrecto."

Evidencia requerida por el Anexo para este caso: **"Registros de
respuestas de error"**.

Auditoría previa (XPAY-521) confirmó este requisito leyendo directamente
el Anexo oficial disponible localmente en el equipo.

## Disciplina de ejecución

- **Tres subcasos, tres ejecuciones reales — una por subcaso, sin
  reintentos.**
- **Cero mutaciones nuevas de llave**: los tres subcasos reutilizan
  exclusivamente fixtures históricas ya existentes (M4-T3-A/B para las
  llaves suspendida/eliminada; el mismo generador sintético de M4-T3-C
  para el customer_id incorrecto) — ningún Create/Delete/Suspend/Update
  Key se ejecutó como parte de M4-T6.
- **Payload DYNAMIC alineado con M4-T4**: `type=DYNAMIC`, `channel=MPOS`,
  vat/inc/tip presentes (mismos valores de certificación ya probados),
  `amount` presente (`value="10000.00"`, `currency="COP"`, el mismo valor
  oficial del ejemplo Postman de Passport ya usado en M4-T4/XPAY-512),
  `additional_info` ausente, `qr_code_reference` **fresca y única por
  ejecución** (nunca reutilizada entre subcasos ni con M4-T4/M4-T5).
- **Cero Decode, Payment, Resolve**: ningún subcaso de M4-T6 ejecutó
  ninguna de esas operaciones.
- Cada precondición fail-closed (fingerprint del key_id contra el valor
  histórico esperado) se verificó exitosamente antes de cada llamada —
  ver evidencias técnicas individuales para el detalle.

## Resultados observados

### M4-T6-A — llave Bre-B suspendida

- **Evidencia:** `M4-T6-A/evidence-2026-09-23T11-44-15Z.json`
  (SHA256 `d4d9589a31f8b911bbae231dac2fea6b47a98a024aa337e8a85ed70c552b3400`)
- **Resultado:** `request_accepted=true`, `status=ACTIVE`, `type=DYNAMIC`.
- Passport **aceptó** la creación del QR DYNAMIC pese a que la llave
  objetivo estaba `SUSPENDED` — no se fabricó ni se forzó ningún error
  para este subcaso. Este hecho se documenta tal cual fue observado.

### M4-T6-B — llave Bre-B eliminada

- **Evidencia:** `M4-T6-B/evidence-2026-09-23T11-48-35Z.json`
  (SHA256 `b7fd2746e97a37910f762eb8def625d6ab2fa4b4a78c40868130d1c0ba772326`)
- **Resultado:** `request_accepted=false`, `observed_http_status=404`,
  `safe_error_code=resource_not_found`, `safe_error_message="key not found."`.

### M4-T6-C — customer_id incorrecto

- **Evidencia:** `M4-T6-C/evidence-2026-09-23T11-54-02Z.json`
  (SHA256 `20edf1521718a7d6943d9a96b1bdfc255ae9500ddd42ed774970e03fb8ad6cb9`)
- **Input:** UUID sintético malformado, determinista, el mismo mecanismo
  ya probado en M4-T3-C (`InvalidCustomerIdGenerator`) — nunca el
  customer real, nunca un UUID v4 válido pero inexistente (el Anexo no
  distingue entre ambos subtipos; XPAY-521 confirmó esto).
- **Resultado:** `request_accepted=false`, `observed_http_status=400`,
  `safe_error_code=invalid_parameter_value`,
  `safe_error_message="UUID version 4 expected"`.

## Comparación factual con M4-T3 (STATIC)

| Subcaso | M4-T3 (STATIC) | M4-T6 (DYNAMIC) | Coincidencia |
|---|---|---|---|
| Llave suspendida | Create QR aceptado, `status=ACTIVE` | Create QR aceptado, `status=ACTIVE` | Sí |
| Llave eliminada | HTTP 404, `resource_not_found`, "key not found." | HTTP 404, `resource_not_found`, "key not found." | Sí |
| customer_id incorrecto | HTTP 400, `invalid_parameter_value`, "UUID version 4 expected" | HTTP 400, `invalid_parameter_value`, "UUID version 4 expected" | Sí |

Los tres resultados DYNAMIC coincidieron con sus contrapartes STATIC ya
certificadas. Esto se documenta como **observación empírica puntual**
sobre estos tres subcasos concretos — no se generaliza como una regla
universal de comportamiento de Passport, ni se extiende a ningún
escenario no probado.

## Aclaración sobre M4-T6-A (llave suspendida)

Para M4-T3-A (STATIC), Passport (Gustavo Muñoz) aclaró explícitamente
que la aceptación del Create QR con llave suspendida es el
comportamiento esperado, y que el rechazo correspondiente ocurre durante
el procesamiento de Payment (Módulo 5 del Anexo), no durante la
creación del QR (Módulo 4) — ver
`docs/certificacion/passport-breb/M4-T3-A/passport-official-clarification-2026-09-22.md`.

M4-T6-A **replica el mismo patrón observado** (Create QR aceptado con
llave suspendida) para el caso DYNAMIC. Sin embargo, **Gustavo no ha
confirmado explícitamente que su aclaración se extienda a M4-T6/DYNAMIC**
— esa aclaración fue dada específicamente en el contexto de M4-T3-A
(STATIC). Esta nota documenta la coincidencia empírica de comportamiento,
sin afirmar una confirmación oficial que no existe para el caso DYNAMIC.

Ningún Payment fue ejecutado como parte de M4-T6-A ni de esta nota —
verificar esa hipótesis queda fuera de alcance del Módulo 4.

## Alcance de esta nota

No se ejecutó ninguna llamada a Passport para producir esta nota ni el
índice de evidencia que la acompaña — ambos se construyeron
exclusivamente a partir de las tres evidencias técnicas ya generadas
(XPAY-523/524/525). No se modificó ninguna evidencia histórica
(M4-T1 a M4-T6-C). No se ejecutó ningún Decode, Payment, Resolve, ni
mutación de llave. No se repitió ninguno de los tres subcasos.

## Resumen de estado

| Campo | Valor |
|---|---|
| `M4_T6_REAL_EXECUTIONS_TOTAL` | 3 |
| `M4_T6_A_REAL_EXECUTIONS` | 1 |
| `M4_T6_B_REAL_EXECUTIONS` | 1 |
| `M4_T6_C_REAL_EXECUTIONS` | 1 |
| `M4_T6_TECHNICAL_RESULT` | PASS |
| `review_status` | PENDING_PASSPORT_REVIEW |
| `M4_T6_REPEAT_REQUIRED` | NO |
