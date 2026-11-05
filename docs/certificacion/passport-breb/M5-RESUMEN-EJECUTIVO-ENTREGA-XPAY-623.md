# M5 — Resumen ejecutivo para revisión interna antes del envío (XPAY-623)

**Fecha (UTC):** 2026-10-01 · **Modo:** 100 % offline, `PASSPORT_API_CALLS=0`
**Uso:** revisión interna. **NO es para enviar a Passport.**

---

## Matriz de estado

| Caso | STATUS | EVIDENCE | CAVEAT | SEND_STATUS |
|---|---|---|---|---|
| M5-T1 | Cubierto con recursos existentes | — (sin JSON dedicado) | Se usaron cliente y cuenta Sandbox ya vinculados, según indicación previa de Passport | ALREADY_SENT |
| M5-T2 | Documentado | `M5-T2-Resolve.json`, `M5-T2-Payment-Create.json` (+ nuevos `M5-T6-T7-01/02`) | — | ALREADY_SENT + NEW |
| M5-T3 | Documentado | `M5-T3-T4-Payment-Retrieve.json` | — | ALREADY_SENT |
| M5-T4 | Documentado | los dos anteriores correlacionados (+ `M5-T8`) | — | ALREADY_SENT + NEW |
| M5-T5-A | CLOSED_WITH_DOCUMENTED_CAVEAT | `M5-T5-A-Missing-Amount.json` | **Rechazo por validación LOCAL, antes de llegar a Passport.** No es respuesta de error del proveedor | ALREADY_SENT |
| M5-T5-B | CLOSED_WITH_DOCUMENTED_CAVEAT | `M5-T5-B-Missing-Account.json` | **Rechazo por validación LOCAL**, igual que T5-A | ALREADY_SENT |
| **M5-T5-C** | **INCONCLUSIVE** · PROVIDER_ENVIRONMENT_BLOCKED | `M5-T5-C-Over-Limit-Attempt.json` | **Ejecutado con 12.000.001,00 COP. Passport respondió HTTP 400 `service_blocked` por saldo disponible insuficiente — la regla de límite nunca se evaluó.** `REQUEST_EXECUTED=YES`, `LIMIT_REJECTION_DEMONSTRATED=NO` | NEW |
| M5-T5-D | CLOSED_WITH_DOCUMENTED_CAVEAT | `M5-T5-D-Suspended-Key.json` | Rechazo real de Passport: HTTP 400, `U805` «Key has been suspended by the Client.» El caveat es que el `safe_error_code` llegó como `generic_error`, con el `U805` dentro del mensaje | ALREADY_SENT |
| **M5-T5-E** | **PASS** | `M5-T5-E-01/02/03` | El 404 llegó sin código estructurado (`generic_error`, mensaje nulo). La señal es el estado HTTP | NEW |
| M5-T5-F | CLOSED_WITH_DOCUMENTED_CAVEAT | `M5-T5-F-Invalid-Resolution.json` | Rechazo real: HTTP 400, «UUID version 4 expected». El caveat es que valida **formato**, no inexistencia del recurso | ALREADY_SENT |
| **M5-T6** | **PASS** | §4 de la narrativa + `M5-T6-T7-01/02` | No hay JSON de harness: la evidencia es el comportamiento observado del endpoint de QA (3/3 entregas con HTTP 200) | NEW |
| **M5-T7** | **PASS** | §4 de la narrativa | Igual que T6. Firma validada en las 3 entregas, 5 registros idempotentes, 0 duplicados | NEW |
| **M5-T8** | **PASS** | `M5-T8-Payment-Retrieve-Settled.json` | `settled_at` no se proyecta en la evidencia saneada; no significa que Passport no lo devolviera | NEW |

**Ningún caveat se convirtió en PASS.** T5-A, T5-B, T5-D y T5-F siguen como
`CLOSED_WITH_DOCUMENTED_CAVEAT`; T5-C queda `INCONCLUSIVE`.

---

## Qué es nuevo y qué no

### A. ALREADY_SENT — 8 archivos (26 de septiembre de 2026)

`M5-T2-Resolve.json` · `M5-T2-Payment-Create.json` · `M5-T3-T4-Payment-Retrieve.json` ·
`M5-T5-A-Missing-Amount.json` · `M5-T5-B-Missing-Account.json` · `M5-T5-D-Suspended-Key.json` ·
`M5-T5-F-Invalid-Resolution.json` · `XPAY_M5_AVANCE_EVIDENCIAS_PARA_PASSPORT.md`

Conservados intactos en `XPAY_M5_ENVIO_PASSPORT/`. **No se modifican ni se reenvían.**

### B. NEW_EVIDENCE_TO_SEND — 8 archivos

| Archivo del paquete | Caso | Origen |
|---|---|---|
| `M5-T6-T7-01-Resolve.json` | soporte T6/T7 | `M5-T2-RESOLVE/evidence-2026-09-30T21-49-26Z.json` |
| `M5-T6-T7-02-Payment-Create.json` | soporte T6/T7 | `M5-PAYMENT-CREATE/evidence-2026-09-30T21-49-51Z.json` |
| `M5-T8-Payment-Retrieve-Settled.json` | T8 | `M5-PAYMENT-RETRIEVE/evidence-2026-09-30T22-05-50Z.json` |
| `M5-T5-E-01-Key-Create.json` | T5-E fase 1 | `M5-T5-E-KEY-PREP-CREATE/…01-51-59Z.json` |
| `M5-T5-E-02-Key-Delete.json` | T5-E fase 2 | `M5-T5-E-KEY-PREP-DELETE/…01-57-36Z.json` |
| `M5-T5-E-03-Deleted-Key-Resolve.json` | T5-E fase 3 | `M5-T5-DELETED-KEY/…02-05-06Z.json` |
| `M5-T5-C-Over-Limit-Attempt.json` | T5-C | `M5-T5-OVER-LIMIT/…03-14-00Z.json` |
| `XPAY_M5_EVIDENCIAS_FINALES_PARA_PASSPORT.md` | narrativa | creado en este ticket |

Copiados con bytes idénticos (sha256 verificado), solo renombrados para legibilidad.

### C. INTERNAL_ONLY — no se comparten

Documentos de trabajo con referencias a tickets internos, SHAs de Git, discusión de decisiones
y de errores propios. Útiles para nosotros, ruido o riesgo para el proveedor:

`M5-T5-DESIGN-PREFLIGHT.md` · `M5-T5-EVIDENCIAS-DISPONIBLES-REVISION-INTERNA.md` ·
`M5-T5-C-E-AUDITORIA-XPAY-609.md` · `M5-T5-CONSULTA-GUSTAVO-XPAY-610.md` ·
`M5-T5-INVESTIGACION-DOCUMENTAL-XPAY-611.md` · `M5-T5-PLAN-CIERRE-XPAY-612.md` ·
`M5-T5-E-DELETED-KEY-IMPLEMENTACION-XPAY-613.md` · `M5-T5-C-PLAN-EJECUCION-XPAY-618.md` ·
`M5-T5-C-IMPLEMENTACION-XPAY-619.md` · los 13 documentos `M5-T6-T7-*` / `M5-T7-*` / `M5-T8-*` ·
`M5-AUDITORIA-PRE-ENVIO-GUSTAVO.md` · `M5-T2-T3-T4-DRAFT-CONSOLIDADO.md` ·
este resumen ejecutivo · el borrador de correo.

### D. PRIVATE_NEVER_SHARE — fuera del repositorio

Todos los `*.private.json` del directorio privado de captura (modo 600, directorio 700, fuera
del repositorio, nunca en Git): contienen `key_value`, `key_id` y `resolution_id` en claro.
**0 incluidos en el paquete.**

---

## Recomendación de formato de envío

**Paquete incremental** (los 8 archivos nuevos), que es lo preparado.

Razón: Gustavo ya tiene los 8 anteriores, su narrativa sigue siendo válida, y la nueva los
referencia explícitamente en §6. Reenviar duplicados invita a confusión sobre qué versión es la
vigente.

La narrativa ofrece explícitamente preparar un paquete consolidado si lo prefieren, así que la
decisión queda del lado del proveedor sin coste para nosotros.

---

## Lo que queda abierto

| Ítem | Naturaleza |
|---|---|
| T5-C — regla de límite no demostrada | **Precondición del entorno del proveedor.** La cuenta operativa de Sandbox no tiene fondos para un monto > 12M, y la validación de saldo precede a la de límite. No es resoluble desde nuestro lado: no existe monto que sea a la vez «> 12M» y «≤ saldo disponible» |
| M4 | Completo y congelado, pendiente de revisión del proveedor |

`M5_TECHNICAL_TESTS_REMAINING=0` — no queda ninguna prueba técnica por ejecutar de nuestro lado.

**Decisión deliberada de este ticket:** no pedimos fondeo ni preguntamos «cómo lo probamos»
antes de entregar. Gustavo pidió evidencias («Quedamos atento a lo restante de las evidencias»),
así que entregamos evidencias y dejamos la puerta abierta en una sola frase.
