# XPAY — Passport/Bre-B Certification — M3 Evidence Manifest

## Environment

`sandbox`

## Backend

Commit actual de referencia (HEAD al momento de consolidar este manifiesto):

`a11acf9ec157772faf75be1f5b1576f8d5f57da2`

Cada evidencia individual registra, además, el `backend_commit_sha` exacto
vigente en el momento de su propia ejecución (columna "Backend commit" de
la tabla siguiente) — no todas coinciden con el commit de referencia de
arriba, porque los nueve casos se ejecutaron en momentos distintos a lo
largo de varias iteraciones.

Este documento es un **inventario técnico**, no una certificación. Ninguna
entrada de este manifiesto implica que Passport ya revisó, aprobó, o
autorizó producción para ningún caso — eso corresponde exclusivamente a
Passport, fuera de este repositorio, y se refleja únicamente cuando
`review_status` deje de ser `PENDING_PASSPORT_REVIEW`.

## Evidence inventory

| Case | Description | Evidence | SHA256 | Backend commit | Observed result | Technical classification | Passport review status |
|---|---|---|---|---|---|---|---|
| M3-T1 | Create Key | [`M3-T1/evidence-2026-09-15T03-47-42Z.json`](M3-T1/evidence-2026-09-15T03-47-42Z.json) | `9acceeac83c5b4936d2d26af9bce3a7a054e7ddcd9ea56866b63e1ab818db907` | `c76ab5feb21a9f258e37f6f50a205ce31cafd47b` | 2xx, `result=PASS` | `SANDBOX_PASS` | `PENDING_PASSPORT_REVIEW` |
| M3-T2 | Resolve Key | [`M3-T2/evidence-2026-09-15T17-26-46Z.json`](M3-T2/evidence-2026-09-15T17-26-46Z.json) | `57975662beb93b8ef455f18b4ff18584b4fe7b0793db07502c2c23cce47715e8` | `90f6f964170b9a058173a3d71233df2077ebd642` | 2xx, `result=PASS` | `SANDBOX_PASS` | `PENDING_PASSPORT_REVIEW` |
| M3-T3 | Suspend Key | [`M3-T3/evidence-2026-09-15T12-48-22Z.json`](M3-T3/evidence-2026-09-15T12-48-22Z.json) | `3139f11eaed2a68ec441f0e944cca3879b61ee84eda2914333bbdf45b76ab73c` | `70c7dc7985ca3e686e0fc681b589fd3bee120da8` | 2xx, `result=PASS` | `REAL_EXECUTION_PASS` (evidence published) | `PENDING_PASSPORT_REVIEW` |
| M3-T4 | Activate Key | [`M3-T4/evidence-2026-09-15T15-00-12Z.json`](M3-T4/evidence-2026-09-15T15-00-12Z.json) | `17b3e9b112c0f4cf1458f2c8de0eb7c37b3d96fa28374ae0bd2f8d1c4afe3da0` | `32a2eff1319d4557f8720c07b75a69a231f24538` | 2xx, `result=PASS` | `SANDBOX_PASS` | `PENDING_PASSPORT_REVIEW` |
| M3-T5 | Delete Key | [`M3-T5/evidence-2026-09-15T15-39-44Z.json`](M3-T5/evidence-2026-09-15T15-39-44Z.json) | `cdc641fe26b0f44c04e9f6729b9b02aece4cf4062988b4241cdf008fa7b2ea11` | `faf3ebe25803876942428871512b65a0e99edc3f` | 204/2xx, `result=PASS` | `SANDBOX_PASS` | `PENDING_PASSPORT_REVIEW` |
| M3-T6-MISSING | Create Key — missing `key_value` (validación local, sin llamada a Passport) | [`M3-T6-MISSING/evidence-2026-09-15T21-16-48Z.json`](M3-T6-MISSING/evidence-2026-09-15T21-16-48Z.json) | `b76416e682f173ccd3694ed2ecebe699c6a2e4e0a84cca78b7add6c0b053db3c` | `a9fca1ff68caec707d94b2cbf778f5c5cd4975f4` | N/A — bloqueado antes de transporte (`passport_http_attempted=false`) | `LOCAL_VALIDATION_PASS` | `PENDING_PASSPORT_REVIEW` |
| M3-T6-INVALID | Create Key — `key_value` de formato inválido para BCODE | [`M3-T6-INVALID/evidence-2026-09-15T20-55-23Z.json`](M3-T6-INVALID/evidence-2026-09-15T20-55-23Z.json) | `4a4838d4b69a348ac5bd8bd03a0e85091e156045dc3b7657eb6b38fb4f478aba` | `bf2e1980a5197aa39a1b08c24993604de94c3aba` | HTTP 400 observado | `SANDBOX_PASS` (técnico) | `PENDING_PASSPORT_REVIEW` |
| M3-T6-DUPLICATE | Create Key — `key_value` ya registrada | *(no ejecutado)* | N/A | N/A | N/A | `BLOCKED_PENDING_CONTRACT_CONFIRMATION` | N/A |
| M3-T7 | Delete Already-Deleted Key | [`M3-T7/evidence-2026-09-15T16-33-17Z.json`](M3-T7/evidence-2026-09-15T16-33-17Z.json) | `9c4e90057f03b27b5463da52d03555b003c7c6cc7410a7874421664ac5d17f18` | `57ca1820e200a8cc81b737d854b89ae1c5e03b86` | HTTP 404 observado | `HTTP_404_OBSERVED` | `PENDING_PASSPORT_REVIEW` |

WALLET (referencia de integridad, ajena a esta certificación —
`docs/WALLET_CAJA_CUADRE_FASE_70.4_DISENO.md`):
`0845e401af9c8e742995283056b019483e93bc634020bb4319c59f197f072139`.

### Nota sobre M3-T6-INVALID y M3-T7 — transporte vs. certificación

`M3-T6-INVALID` y `M3-T7` registran `result=FAIL` a **nivel transporte**
dentro de su propio `evidence.json` (la llamada HTTP a Passport terminó en
una excepción, por diseño del stack productivo, ante cualquier respuesta
no-2xx). Eso **no** equivale a que el caso de certificación haya fallado:
en ambos casos, un rechazo de Passport es precisamente el comportamiento
que el caso busca observar y documentar.

- `M3-T6-INVALID`: HTTP 400 observado — coincide con la documentación
  oficial de Passport para `POST /v1/keys` ("faltan campos requeridos o
  contienen valores incorrectos") — clasificación técnica `SANDBOX_PASS`.
- `M3-T7`: HTTP 404 observado — sin contrato oficial confirmado
  específicamente para este escenario — clasificación técnica
  `HTTP_404_OBSERVED`, entregado a Passport para su interpretación.

## Outstanding items

### M3-T6-DUPLICATE

**Estado**: `BLOCKED_PENDING_CONTRACT_CONFIRMATION`

**Reason**: Passport Create Key documentation reviewed does not define an
explicit duplicate-key HTTP/error contract.

**Action requested from Passport**: Confirm expected behavior/status/error
for attempting to register a key that is already registered, so XPAY can
execute the certification case safely and capture the expected evidence.

### M3-T7

HTTP 404 observed on delete of already-deleted key. Evidence available.
Pending Passport interpretation/review.

---

**Este documento no afirma que Passport ya aprobó ningún caso, ni que la
certificación M3 está completa, ni que XPAY está autorizado para
producción.** Todos los `review_status` permanecen `PENDING_PASSPORT_REVIEW`
hasta que Passport realice su propia revisión, fuera de este repositorio.
