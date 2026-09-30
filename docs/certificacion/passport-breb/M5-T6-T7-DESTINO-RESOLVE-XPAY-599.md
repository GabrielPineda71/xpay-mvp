# XPAY — Passport / Bre-B — M5-T6 / M5-T7 — Confirmación del destino activo

**Ticket:** XPAY-599 · **Fecha:** 2026-09-30 (UTC) · **Entorno objetivo:** Sandbox
**Resultado:** **Resolve EJECUTADO** — `KEY_A` resuelve correctamente y es reutilizable como destino

```
PASSPORT_API_CALLS=1
RESOLVE_CALLS=1
PAYMENT_CALLS=0
RETRIEVE_CALLS=0
FINANCIAL_OPERATIONS=0
MONEY_MOVED=NO
```

La única llamada autorizada **se consumió el 2026-09-30 a las 15:14:23Z** y devolvió `PASS`.
Ver §6.

---

## 1. Candidata identificada

```
CANDIDATE_IDENTIFIED=YES
CANDIDATE_REFERENCE=KEY_A — llave Bre-B histórica de XPAY (Customer A), key_type BCODE,
                    referenciada por PASSPORT_TEST_BREB_KEY / PASSPORT_TEST_BREB_KEY_TYPE
```

El valor completo de la llave **no aparece en este documento ni en ningún registro**: vive
exclusivamente en el artefacto privado del harness, fuera del repositorio. Las evidencias del
proyecto almacenan únicamente *fingerprints*, que tampoco se reproducen aquí.

### Razón de la selección

Cumple las tres prioridades del ticket de forma inequívoca:

1. **Es la llave del receptor A del flujo positivo M5.** El propio harness lo documenta:
   `M5-T2-RESOLVE` → `endpoint=POST /v1/resolve-key`,
   `mutating=NO (read-only, resuelve la misma llave KEY_A ya usada desde M3-T2)`. El código
   declara además «Guard #4 — targets de KEY_A/CUSTOMER_A, MISMOS ya usados desde M3-T2».
2. **Tiene historial de resolución exitosa.** Las cuatro ejecuciones registradas de
   `M5-T2-RESOLVE` (25-sep → 26-sep) dan `result = PASS`, con
   `owner_present`, `participant_present`, `account_present` y `expires_at_present` en
   verdadero, y `resolve_calls = 1` / `retry_count = 0` cada vez. Es la llave que alimentó el
   pago B→A certificado en T2.
3. **No es un fixture negativo.** Las llaves usadas como fixtures de pruebas negativas son
   otras y tienen sus propias variables dedicadas: M4-T3-A (suspendida), M4-T3-B (eliminada),
   M5-T5-D (suspendida). KEY_A no está entre ellas.

### Observación relevante: el valor de KEY_A cambió una vez

Comparando las cuatro evidencias de `M5-T2-RESOLVE` **sin exponer los fingerprints**:

| Elemento | Resultado |
|---|---|
| `customer_id_fingerprint` | **Idéntico en las cuatro** — siempre el mismo Customer A |
| `key_value_fingerprint` | **Dos valores distintos**: uno en la ejecución del 2026-09-25T03:46Z y otro en las tres posteriores |
| `result` | `PASS` en las cuatro |

Es decir: la llave de A fue reemplazada en algún momento entre la primera y la segunda
ejecución, y las **tres últimas** —incluida la más reciente, 2026-09-26T01:15:01Z, la que
precedió al pago certificado— usaron el mismo valor.

**Consecuencia para quien ejecute el Resolve:** debe confirmar que
`PASSPORT_TEST_BREB_KEY` contiene el valor vigente —el del grupo de las tres últimas— y no el
histórico anterior. Resolver el valor antiguo daría un resultado engañoso.

---

## 2. Por qué no se ejecutó el Resolve

La llamada es **técnicamente imposible desde la herramienta**, no una decisión discrecional.

`M5ResolveExecutor` es *fail-closed* y exige, antes de cualquier HTTP:

- un directorio de captura privado válido (`PASSPORT_TEST_M5_PAYMENT_PRIVATE_CAPTURE_DIR`);
- los targets de KEY_A/CUSTOMER_A (`PASSPORT_TEST_BREB_KEY`, `PASSPORT_TEST_BREB_KEY_TYPE`,
  customer id);
- credenciales Sandbox (`PASSPORT_BASE_URL`, `PASSPORT_API_KEY`, `PASSPORT_API_SECRET`).

Verificado **por booleano, sin leer ningún valor**: las **cinco** credenciales Passport y las
**veinte** variables del harness están **ausentes** en el proceso de la herramienta. Es la
misma situación estructural ya documentada para otros frentes: esas variables viven en la
sesión de Terminal del operador, y Claude Code corre en otro árbol de procesos.

Sin ellas el executor devuelve `LocalBlocked` **antes** de construir petición, obtener token o
abrir el artefacto privado. No hubo, por tanto, ninguna llamada a Passport ni ningún intento.

---

## 3. Clasificación

```
DESTINATION_CLASSIFICATION=ACTIVE_REUSABLE
ACTIVE_DESTINATION_AVAILABLE=YES
```

Clasificación fundada en el **contenido de la respuesta**, no en el mero éxito de la llamada
— ver §6.

---

## 4. Cómo completar el Resolve (una sola llamada, pendiente de ejecución)

El harness ya tiene el comando y opera en dry-run por defecto:

La configuración se carga desde `~/.passport-sandbox.env` (formato `VAR=valor` sin `export`,
permisos 0600), por lo que el `set -a` es obligatorio: el harness lee variables de **entorno
del proceso** y nunca parsea el archivo. Carga y comando deben ir en la **misma** invocación.

```
set -a; . ~/.passport-sandbox.env; set +a
dotnet run --project tools/Xpay.PassportSandboxHarness -- resolve-key-m5-t2
```

Eso es el dry-run. La ejecución real exige **DOS** banderas, no una:

```
dotnet run --project tools/Xpay.PassportSandboxHarness -- \
  resolve-key-m5-t2 --execute --confirm-resolve-key-m5-t2
```

### Dry-run ya ejecutado (XPAY-599A, 2026-09-30)

Salida: `command=ResolveKeyM5T2` · `PASSPORT_BASE_URL/API_KEY/API_SECRET = AVAILABLE` ·
`case=M5-T2-RESOLVE` · `endpoint=POST /v1/resolve-key` ·
`mutating=NO (read-only, resuelve la misma llave KEY_A ya usada desde M3-T2)` ·
`dry_run=YES http_call=NO oauth_token_requested=NO` · `result=DRY_RUN`.

Confirma el enrutamiento del comando, la identidad de la candidata (**KEY_A**) y que las tres
credenciales Sandbox cargan. **No confirma** que los guards vayan a pasar: la validación del
directorio de captura privado y el guard anti-repetición corren únicamente en modo
`ReadyToExecute`, nunca en dry-run — el propio código lo declara.

> **Nota del harness que NO debe malinterpretarse.** El dry-run imprime
> `M5-T2-RESOLVE: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-545)`. Es una cadena
> **estática y desactualizada** heredada de XPAY-545: las evidencias del propio repositorio
> registran **cuatro** ejecuciones reales en Sandbox con `result=PASS`. No tomar esa línea
> como estado vigente.

### Guard anti-repetición

`M5ResolveArtifactReader.IsBlockingForRepeat` permite una nueva ejecución real **sólo** si la
resolución anterior fue consumida **y** su `expires_at` es parseable **y** ya venció. La
última resolución es del 2026-09-26T01:15:01Z y fue consumida por el pago de T2, así que a
esta fecha debería estar vencida y no bloquear. Si aun así devuelve `LocalBlocked`, **no
forzar nada**: reportarlo.

---

## 5. Estado de lo que este ticket NO tocó

```
KEY_CREATED=NO
KEY_MODIFIED=NO
QR_CREATED=NO
DATABASE_MODIFIED=NO
AZURE_MODIFIED=NO
PASSPORT_DASHBOARD_MODIFIED=NO
SIGNATURE_MODE=ENFORCE
WEBHOOK_EVENTS_TRIGGERED_BY_THIS_TICKET=0
PRODUCTION_MODIFIED=NO
MIDECISOR_MODIFIED=NO
STAGE_PERFORMED=NO
COMMIT_PERFORMED=NO
PUSH_PERFORMED=NO
```

Ningún POST al webhook, ningún Test Event, ningún pago. El webhook Sandbox sigue activo y sin
modificar, y el backend QA sigue en `ENFORCE`.

```
READY_FOR_T6_T7_PAYMENT_AUTHORIZATION=NO
```

Bloqueado por el mismo paso que este ticket pretendía despejar: **confirmar que existe un
destino XPAY activo**.

---

## Seguridad de este documento

No contiene el valor de ninguna llave, BCODE, fingerprint, `resolution_id`, nombre de titular,
identificación, teléfono, correo, dirección, token, cabecera de autorización ni identificador
completo de cuenta o cliente.


---

## 6. Ejecución real del Resolve — 2026-09-30

**Autorizada expresamente.** Una sola llamada, sin reintentos.

```
PASSPORT_API_CALLS=1
RESOLVE_CALLS=1
result=PASS
evidencia=docs/certificacion/passport-breb/M5-T2-RESOLVE/evidence-2026-09-30T15-14-23Z.json
review_status=PENDING_PASSPORT_REVIEW
```

Respuesta observada, en forma sanitizada:

| Campo | Valor |
|---|---|
| `request_executed` · `request_accepted` | `True` · `True` |
| `resolution_id_fingerprint` | presente (valor crudo sólo en el artefacto privado) |
| `owner_present` | `True` |
| `participant_present` | `True` |
| `account_present` | `True` |
| `expires_at_present` | `True` |
| `resolve_calls` · `retry_count` | `1` · `0` |
| `key_type` | `BCODE` |

El `resolution_id` crudo **nunca se imprimió ni se escribió en la evidencia**: queda sólo en
el artefacto privado, fuera del repositorio.

### Por qué la clasificación es `ACTIVE_REUSABLE` y no un simple «HTTP OK»

No se infiere del éxito de la llamada. Se funda en el **contraste con el caso negativo ya
certificado**: en `M5-T5-SUSPENDED-KEY`, un Resolve contra una llave **suspendida** produjo
`result=FAIL`, `observed_http_status=400`, `resolution_returned=False` y
`current_suspension_confirmed=True`.

Hoy, contra KEY_A, Passport devolvió lo contrario: una **resolución completa** con titular,
participante, cuenta y vencimiento. Eso es exactamente lo que un pago necesita como destino.
Por tanto KEY_A **no está suspendida ni eliminada** y sirve como destino del pago entrante de
T6/T7.

### Restricción temporal que hereda el pago

La resolución trae `expires_at`. Las resoluciones Bre-B son de vida corta, así que
`ACTIVE_REUSABLE` califica a la **llave**, no a este `resolution_id` concreto. El pago de
T6/T7 deberá usar una resolución vigente: o se crea dentro de la ventana de la obtenida hoy,
o se ejecuta un Resolve nuevo inmediatamente antes —lo que exigiría su propia autorización—.
El paso intermedio previsto por el harness es `configure-m5-payment-resolution`, que
transfiere `resolution_id`/`expires_at` al entorno del Payment Create.

```
READY_FOR_T6_T7_PAYMENT_AUTHORIZATION=YES
```

Despejado el bloqueador que este ticket perseguía. Lo único que falta para producir T6/T7 es
la **autorización expresa del pago real**, que sigue sin concederse.
