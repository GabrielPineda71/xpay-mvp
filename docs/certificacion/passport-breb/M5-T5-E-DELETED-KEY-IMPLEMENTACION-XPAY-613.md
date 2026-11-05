# M5-T5-E — Clave Bre-B eliminada: implementación de los ejecutores (XPAY-613)

**Fecha (UTC):** 2026-09-30
**Alcance del ticket:** código + pruebas offline. **Cero llamadas a Passport.**
**Estado del subcaso:** `T5_E_STATUS=READY_FOR_CONTROLLED_EXECUTION` — **no PASS.**
**Commit base:** `fd0b654` (nada staged, nada commiteado, nada pusheado en este ticket).

---

## 1. Qué exige el subcaso y por qué hacía falta un fixture nuevo

M5-T5-E es el subcaso negativo "envío/resolución hacia una clave Bre-B **eliminada**".
La letra (D = suspendida, E = eliminada) proviene del Anexo, no de documentos internos
(establecido en XPAY-609/611).

Ningún fixture existente sirve, y la razón es estructural, no de conveniencia:

| Fixture candidato | Estado final | Por qué NO se puede usar |
|---|---|---|
| M5-T5-D (XPAY-575/576/577) | **SUSPENDIDO** | No está eliminado: un Resolve contra él prueba el caso D, no el E. Además su evidencia final ya fue generada y no debe alterarse. |
| M4-T3-B (XPAY-492) | **ELIMINADO** | Nació bajo el patrón antiguo **sin private capture**: su `key_value` se perdió de forma irrecuperable (misma causa raíz de XPAY-574). `POST /v1/resolve-key` exige `key_value`, nunca `key_id`. |

De ahí el diseño aprobado en XPAY-612: crear un fixture desechable **nuevo**, eliminarlo,
y resolverlo — en tres fases independientes.

---

## 2. Las tres fases (independientes por construcción)

Cada fase es un comando propio, con su propio flag de confirmación y su propio
repeat guard permanente. **No existe ningún ejecutor que haga las tres llamadas
en una sola ejecución.**

| Fase | Comando | Endpoint | `case_id` de evidencia | Guard de repetición |
|---|---|---|---|---|
| **A — PREP-CREATE** | `create-m5-t5-deleted-fixture` | `POST /v1/keys` | `M5-T5-E-KEY-PREP-CREATE` | glob `M5-T5-DELETED-KEY-FIXTURE-*` |
| **B — PREP-DELETE** | `delete-m5-t5-fixture` | `DELETE /v1/keys/{key_id}` | `M5-T5-E-KEY-PREP-DELETE` | marcador fijo `M5-T5-E-DELETE-CONSUMED-MARKER` |
| **C — RESOLVE-DELETED** | `resolve-m5-t5-deleted-key` | `POST /v1/resolve-key` | **`M5-T5-DELETED-KEY`** (evidencia final) | marcador fijo `M5-T5-E-RESOLVE-CONSUMED-MARKER` |

Cada fase requiere **dos** flags para ejecutar de verdad:
`--execute --confirm-<comando>`. El flag de otra fase —o de M5-T5-D, o de M3-T5
`delete-key`— **nunca** autoriza ninguna de estas (probado).

Las Fases A y B emiten evidencia marcada `certification_test_result=NOT_YET_EXECUTED`
(`fixture_phase=CREATED_NOT_DELETED` y `DELETED_NOT_TESTED`): documentan la
preparación, **nunca** afirman que el caso de certificación corrió.

---

## 3. Cadena de identidad: cómo se evita repetir XPAY-573/574

```
Fase A  ──escribe──▶  PREPARED (key_value, antes de HTTP)
                      CREATED  (key_value + key_id + ambos fingerprints)
                                        │
Fase B  ──lee key_id SOLO de aquí──────┘
        ──escribe──▶  MARKER (fingerprint-only, delete_acknowledged=true)
                                        │
Fase C  ──lee key_value SOLO del CREATED, y exige que el MARKER
           corresponda al MISMO fixture (ambos fingerprints) y confirme
           la eliminación──────────────┘
```

- El `key_value` se genera **localmente** con `DisposableBcodeGenerator` y se persiste
  **antes** de la llamada HTTP. Nunca se lee de configuración, nunca se deriva de
  `PASSPORT_TEST_NEW_KEY_*`, `PASSPORT_TEST_BREB_KEY`, KEY_A ni de ningún fixture histórico.
- Las Fases B y C **no aceptan** `key_id`/`key_value` por env ni por CLI: sólo del
  artefacto privado. Es imposible apuntarlas a otra llave por error de operación.
- `Delete` elimina la llave **en Passport**, nunca el registro privado local — por eso
  la Fase C todavía tiene el `key_value` que Resolve necesita.
- La Fase C es **fail-closed**: sin marcador de Delete, con fingerprints que no cuadran,
  o con la eliminación no reconocida, se detiene **antes** de cualquier HTTP.

### Confirmación de la eliminación
`DELETE /v1/keys/{key_id}` devuelve **204 No Content, sin body**: no hay ningún
`status` que leer (a diferencia del marcador de Suspend de M5-T5-D, que registra
`returned_status="SUSPENDED"`). La confirmación del proveedor **es** el 204, que la
Fase B registra como `delete_acknowledged=true`. No se bundleó ninguna verificación
extra vía `List Keys` en la Fase B: habría violado la regla de responsabilidad única
del ticket. Si el director quiere corroboración independiente, el comando
`list-keys` ya existe y es read-only y separado.

---

## 4. Separación de pago — `PAYMENT_CAPABILITY_PRESENT=NO`

La garantía es **estructural**, no condicional:

- Ninguno de los tres ejecutores recibe `IPassportPaymentClient` como parámetro.
- Ninguno lo declara como campo ni lo devuelve.
- La única dependencia remota declarada por las tres fases es `IPassportKeyClient`.
- Las únicas apariciones textuales de `IPassportPaymentClient` en estos archivos son
  comentarios que documentan la prohibición.

Verificado por reflexión en `M5T5DeletedKeySeparationAndWiringTests`: si alguien
añadiera esa dependencia en el futuro, esos tests fallan. Además, la evidencia final
siempre declara `payment_real_calls=0` y `retrieve_real_calls=0` — incluido el caso
`UNEXPECTED_RESOLVE_SUCCESS`, que **nunca** dispara un Payment.

---

## 5. Protección de llaves y de PII

- **Guard de llave activa** en las tres fases: comparación directa en memoria contra
  `PASSPORT_TEST_BREB_KEY_VALUE`. Si coincidiera, se aborta sin HTTP.
- **Aislamiento respecto a M5-T5-D**: globs y nombres de marcador disjuntos; además el
  reader exige `purpose == "M5-T5-E-DELETED-KEY-FIXTURE"`, de modo que un artefacto de D
  nunca satisface un guard de E (probado explícitamente).
- **Artefacto privado vs. evidencia saneada**: los valores crudos (`key_value`, `key_id`)
  existen **sólo** en `*.private.json` (modo 600, directorio 700, fuera del repo, excluido
  de Git). La evidencia pública lleva únicamente fingerprints SHA-256 truncados. Los
  marcadores de consumo son **fingerprint-only**: no duplican ningún valor crudo.
- Ningún `key_value`, `key_id` completo, `customer_id`, `account_id` ni PII aparece en
  evidencia pública, en este documento o en la salida del harness. Probado con asserts
  `DoesNotContain` sobre el JSON serializado.
- Ningún secreto ni valor real está hardcodeado en código ni en tests: los tests usan
  únicamente valores sintéticos auto-descriptivos.

---

## 6. Archivos

**Nuevos — harness (7):**

| Archivo | Líneas |
|---|---|
| `CreateM5T5DeletedKeyFixtureExecutor.cs` | 296 |
| `CreateM5T5DeletedKeyFixtureEvidenceBuilder.cs` | 118 |
| `M5T5DeletedFixtureArtifactReader.cs` | 146 |
| `DeleteM5T5FixtureKeyExecutor.cs` | 244 |
| `DeleteM5T5FixtureKeyEvidenceBuilder.cs` | 135 |
| `ResolveM5T5DeletedKeyExecutor.cs` | 286 |
| `ResolveM5T5DeletedKeyEvidenceBuilder.cs` | 164 |

**Nuevos — tests (4):** `CreateM5T5DeletedKeyFixtureExecutorTests.cs` (363),
`DeleteM5T5FixtureKeyExecutorTests.cs` (383), `ResolveM5T5DeletedKeyExecutorTests.cs` (407),
`M5T5DeletedKeySeparationAndWiringTests.cs`.

**Modificados (2):** `HarnessDecision.cs` (3 comandos + 3 flags + enum + mapeos),
`HarnessApp.cs` (help + 3 bloques dry-run + 3 bloques execute).

**Sin tocar:** `IPassportKeyClient`, `PassportKeyClient`, `DisposableBcodeGenerator`,
`M5PaymentPrivateCapture`, `Fingerprint`, y todo el stack de M5-T5-D / M4 / M3.

---

## 7. Resultados de pruebas

| Suite | Resultado |
|---|---|
| Focalizada M5-T5-E | **82/82 PASS** |
| Harness completa | **968/968 PASS**, 0 fallos, 0 omitidos |
| Backend completa | **1045/1045 PASS**, 0 fallos, 0 omitidos |
| Dry-run de los tres comandos | `dry_run=YES http_call=NO oauth_token_requested=NO` |

`NETWORK_CALLS_FROM_TESTS=0`: el único colaborador remoto en los tests es un
`FakeKeyClient` en memoria; ningún test abre un socket. `PASSPORT_API_CALLS=0` en todo
el ticket.

---

## 8. Qué falta y qué NO se hizo

- **No se ejecutó nada real.** Las tres fases están listas pero no corridas. La ejecución
  controlada requiere un ticket aparte, **fase por fase**, con autorización explícita de
  cada una (la Fase B es irreversible).
- **No se hizo stage, commit ni push.**
- Resultado esperado de la Fase C: rechazo del proveedor. Corroboración documental
  disponible: M4-T3-B obtuvo `404 resource_not_found` («key not found.») desde
  `POST /v1/qrcodes` contra una llave eliminada. **No se asume** que `resolve-key`
  responda igual: `ClassifyDeletionSignal` registra literalmente lo que Passport
  devuelva y clasifica `deletion_signal_confirmed` como `true` sólo ante una señal
  reconocible de recurso inexistente — nunca `false`, porque la ausencia de señal no
  prueba lo contrario.
- M5-T5-C sigue bloqueado por separado (`FINANCIAL_TEST_REQUIRED`,
  `OVER_12M_EXECUTION_AUTHORIZED=NO`, XPAY-612).
