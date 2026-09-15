# Expediente de evidencias de certificación Passport/Bre-B

## Propósito

Este directorio conserva la evidencia técnica que XPAY presenta a Passport
(Gustavo Muñoz) para revisión, como parte del proceso de certificación
Bre-B como Servicio descrito en el anexo oficial:

> `_Anexo Formato de Certification Passport Paso a producción Bre-b v1 (3).docx`

Ese anexo define 33 casos (M1-T1 a M7-T3, ver módulos M1-M7). Cada
subdirectorio de este expediente corresponde a UN caso (`M{módulo}-T{caso}`,
p. ej. `M3-T1/`) y contiene uno o más archivos `evidence-<timestamp>.json`
producidos por una ejecución real, --execute, del harness Sandbox.

## Relación con los casos M1-M7 del anexo

El anexo exige, para cada caso, un tipo de evidencia (p. ej. "Registros de
la API", "Captura de pantalla"). Este expediente cubre los casos cuya
evidencia son *registros de solicitud/respuesta de la API* — los casos M1
(dashboard/MFA) y M7 (SLA/soporte) son operativos y no producen
`evidence.json` aquí.

## Diferencia entre tests offline, dry-run, ejecución Sandbox real y revisión Passport

Estas **cuatro cosas son completamente distintas** y no deben confundirse
entre sí — ni siquiera cuando una ejecución real produce `result=PASS`:

| | ¿Llama a Passport? | ¿Produce `evidence.json`? | ¿Cuenta para certificación? |
|---|---|---|---|
| **Tests offline** (`dotnet test`) | No — `FakeHttpMessageHandler` en memoria | No | No — sólo prueban que el código XPAY es correcto |
| **Harness dry-run** (`create-key` sin `--execute`) | No | No | No — sólo confirma config/host/targets antes de ejecutar |
| **Harness `--execute` real (Sandbox)** | Sí — Passport Sandbox real | Sí, `result=PASS` o `FAIL` | Es la evidencia técnica, pero **todavía no** una certificación aceptada |
| **Revisión de Passport** | — | Passport actualiza `review_status` fuera de este repo | Sólo esto cierra el caso ante Passport |

**Regla explícita: DRY-RUN ≠ CERTIFICATION EVIDENCE, y SANDBOX PASS ≠ PASSPORT REVIEWED.**
Únicamente una ejecución real (`--execute` + la bandera de confirmación del
comando) contra Passport Sandbox puede generar un `evidence.json`. Un
dry-run, un aborto por configuración/host/`key_type` inválido, o un fallo
LOCAL antes de intentar HTTP, **nunca** producen `evidence.json` (ver
"Bloqueo local vs. fallo remoto" más abajo). Y un `evidence.json` con
`result=PASS` demuestra que Passport aceptó la operación en Sandbox — **no**
que Passport ya revisó/aprobó el caso para efectos de certificación; eso
sólo ocurre cuando Passport actualiza `review_status`.

### Estados conceptuales de un caso (M3-T1 u otro)

- **`IMPLEMENTED`** — el cliente productivo XPAY y el camino `--execute`
  del harness existen y están cubiertos por tests offline (incluyendo un
  test end-to-end con handler HTTP fake). **Ninguna llamada real ha
  ocurrido todavía.** Éste es el estado actual de M3-T1 tras XPAY-325.
- **`SANDBOX_PASS`** — una ejecución real `--execute` contra Passport
  Sandbox produjo `evidence.json` con `result=PASS`. Esto **todavía no
  existe** para M3-T1.
- **`PENDING_PASSPORT_REVIEW`** — valor literal del campo `review_status`
  en todo `evidence.json` recién generado (ver schema abajo).
- **`PASSPORT_ACCEPTED`** — Passport confirmó (fuera de este repo, por su
  propio canal de revisión) que el caso es válido para certificación. No es
  un valor que XPAY escriba en `evidence.json`.

**M3-T1 está hoy en estado `IMPLEMENTED`. No se marca `SANDBOX_PASS`.**

## Política de redacción

**Nunca se escribe en `evidence.json`:**

- `Authorization` / Bearer token / `access_token`
- API key / API secret / `client_secret`
- BCODE u otro `key_value` real completo
- `identification_number` completo
- `account_number` completo
- datos personales completos del `owner` (nombre, etc.)
- cualquier credencial

**IDs opacos de Passport** (`id`, `account_id`, `customer_id`, etc.) se
redactan por defecto mediante un **fingerprint determinístico SHA-256
truncado** (`Fingerprint.Compute`, 12 caracteres hexadecimales por
defecto) — nunca `GetHashCode` ni ningún hash no criptográfico, y el valor
original nunca se guarda junto al fingerprint. Esto permite correlacionar
la misma entidad entre varias evidencias sin revelar su valor.

`key_value` es un caso especial: nunca se guarda ni siquiera como
fingerprint — siempre aparece literalmente como la cadena `"REDACTED"`,
porque a diferencia de un ID opaco de Passport, el valor de una llave puede
ser PII directa (teléfono, email, cédula) según su `key_type`.

`display_name` se trata con la misma política conservadora: nunca se
conserva el texto literal, sólo si estaba presente (`true`/`false`).

## Estructura de `evidence.json`

```json
{
  "case_id": "M3-T1",
  "executed_at_utc": "2026-01-01T00:00:00Z",
  "environment": "sandbox",
  "backend_commit_sha": "<sha completo del commit que ejecutó la operación>",
  "operation": "POST /v1/keys",
  "http_status": 200,
  "result": "PASS",
  "request_sanitized": { "...": "campos saneados, ver política arriba" },
  "response_sanitized": { "...": "campos saneados, ver política arriba" },
  "automated_test_reference": "PassportKeyClientTests.CreateKeyAsync_...",
  "notes": null,
  "review_status": "PENDING_PASSPORT_REVIEW"
}
```

`backend_commit_sha` nunca puede estar vacío — `EvidenceWriter` lo rechaza
explícitamente si falta.

### `http_status` — por qué puede ser `null`

El stack productivo actual (`IPassportHttpClient`/`PassportHttpClient`) NO
expone el código HTTP crudo a sus llamadores: internamente clasifica la
respuesta como éxito (2xx → objeto tipado) o como una de tres excepciones
saneadas (`PassportAuthenticationException`/`PassportTransportException`/
`PassportProtocolException`), por diseño, para no filtrar detalles de
transporte a cada cliente de negocio. Extender esa abstracción sólo para
fines cosméticos de evidencia no está autorizado (XPAY-325). Por tanto:

- `http_status=null` + `result=PASS` significa: **Passport respondió con un
  2xx y el cliente XPAY validó el protocolo de la respuesta** (p. ej. que
  trae un `id`) — no se afirma el código exacto (200 vs 201, etc.).
- `http_status=null` + `result=FAIL` con `notes` iniciando en
  `PASSPORT_HTTP_FAILURE:` significa: Passport respondió con un error real
  (autenticación/transporte/protocolo) — el mensaje de la excepción, ya
  saneado por diseño desde su origen, se conserva en `notes`; no se
  intenta parsear un código numérico desde el texto del mensaje.

### Bloqueo local vs. fallo remoto

`notes` distingue explícitamente, mediante un prefijo, dos situaciones muy
distintas cuando `result=FAIL`:

- `LOCAL_BLOCKED: ...` — la operación **nunca llegó a intentarse** contra
  Passport (`key_type` inválido, commit SHA no resoluble, targets
  ausentes). **Estos casos NO generan `evidence.json` en absoluto** — sólo
  se informan por consola como `result=LOCAL_BLOCKED`. Un bloqueo local
  nunca debe presentarse como si fuera una respuesta (fallida) de Passport.
- `PASSPORT_HTTP_FAILURE: ...` — Passport sí respondió, y respondió con un
  error. Esto **sí genera** `evidence.json` con `result=FAIL`.

### Commit SHA — regla operativa (no forzada por código)

Antes de ejecutar `--execute` realmente contra Sandbox, el commit HEAD del
harness debe corresponder a un **commit publicado** (con push realizado),
no a un working tree con cambios sin confirmar — de lo contrario
`backend_commit_sha` documentaría un estado que nadie más puede reproducir
ni auditar. Esta es una regla operativa para quien ejecute el harness, no
una validación de árbol de trabajo limpio implementada en código (se
consideró innecesario para el alcance de XPAY-325: `ICommitShaProvider`
garantiza únicamente que el SHA no esté vacío, no que el working tree esté
limpio).

## Estados posibles

- `result`: únicamente `"PASS"` o `"FAIL"`. No se agregan estados
  adicionales no solicitados.
- `review_status`: únicamente `"PENDING_PASSPORT_REVIEW"` al generarse. La
  revisión final y el cambio de este estado corresponden a **Passport**, no
  a XPAY.
- Internamente (nunca como campo nuevo del schema, sólo dentro de `notes`
  cuando `result=FAIL`), se distingue:
  - `LOCAL_BLOCKED: ...` — la operación nunca llegó a intentarse contra
    Passport (config/host/guard inválido antes de HTTP).
  - `PASSPORT_HTTP_FAILURE: ...` — Passport respondió con un error real.

  Esta distinción evita que un bloqueo puramente local se presente como si
  fuera una respuesta real (fallida) de Passport.

## Nombre de archivo y protección contra sobrescritura

Cada ejecución real escribe `evidence-<executed_at_utc saneado>.json`
dentro de `M{módulo}-T{caso}/` — el timestamp en el nombre permite
múltiples ejecuciones legítimas (p. ej. un re-intento documentado) del
mismo caso sin colisionar. Si ya existe un archivo con exactamente el mismo
nombre, `EvidenceWriter` falla explícitamente en vez de sobrescribir en
silencio. La escritura es atómica (archivo temporal + `rename`) para que un
fallo a mitad de escritura nunca deje un archivo parcial y engañoso.
