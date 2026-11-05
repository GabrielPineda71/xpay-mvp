# XPAY — Passport / Bre-B — M5-T6 / M5-T7 — Flujo atómico XPAY-601

**Ticket:** XPAY-601 · **Fecha:** 2026-09-30 (UTC) · **Entorno:** Sandbox
**Resultado:** flujo COMPLETADO hasta el final · **pago ejecutado** · **T6 PASS** · **T7 FAIL**
(la Fase C se desbloqueó con autorización adicional — ver §7 en adelante)

```
ARTIFACT_NORMALIZATION_PERFORMED=YES   ARTIFACTS_DELETED=0   ARTIFACTS_ARCHIVED=3
NEW_RESOLVE_CALLS=1                    RESOLVE_RESULT=PASS   RESOLUTION_VALID=YES
PAYMENT_CREATE_CALLS=0                 FINANCIAL_OPERATION_EXECUTED=NO
```

---

## Fase A — Normalización reversible

Se verificó **en código** antes de tocar nada que `M5ResolveArtifactReader` usa
`Directory.GetFiles(dir, glob)` sin `SearchOption`, es decir **TopDirectoryOnly**: un
subdirectorio no se escanea. También que el outcome `NoArtifactFound` hace `break` y por
tanto **no bloquea** un Resolve nuevo.

Se movieron a `archive/` —dentro del propio directorio privado— tres archivos, todos
inequívocamente históricos o vencidos:

| Archivo | Motivo |
|---|---|
| `M5-T2-RESOLVE-2026-09-26T01-15-01Z` | histórico, **consumido** (su marcador existe) y vencido |
| `M5-T2-RESOLVE-CONSUMED-2026-09-26T01-15-01Z` | marcador de consumo del anterior; se movió junto a su par |
| `M5-T2-RESOLVE-2026-09-30T15-14-23Z` | resolución vencida (XPAY-600) |

`mv`, no `rm`: **cero archivos borrados**, contenido intacto, ningún guard modificado.
`NORMALIZATION_REVERSIBLE=YES` — basta mover los tres de vuelta para restaurar el estado.

Conjunto activo del reader tras la normalización: **0 artefactos** → `NoArtifactFound`.

---

## Fase B — Resolve nuevo

Un único Resolve real de KEY_A, sin reintentos.

```
result=PASS
evidencia=docs/certificacion/passport-breb/M5-T2-RESOLVE/evidence-2026-09-30T21-17-23Z.json
resolved_at = 2026-09-30T21:17:25.828Z
expires_at  = 2026-09-30T21:47:25.828Z     (TTL 1800 s)
```

Verificación inmediata del artefacto, **sin imprimir el `resolution_id`**: exactamente **1**
artefacto activo, `resolution_id` presente, **no consumida**, `key_value_fingerprint`
coincidente con las ejecuciones certificadas (KEY_A vigente), y vigencia confirmada.

---

## Fase C — BLOQUEADA por guard, y el bloqueo evitó un daño real

```
result=LOCAL_BLOCKED
detail=EnvAlreadyHasResolutionId: PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID ya existe
       en el archivo de entorno — no se sobrescribe.
RESOLUTION_CONFIGURATION_LOCAL_ONLY=YES   (cero HTTP, confirmado en código y en ejecución)
```

Al investigar el bloqueo se encontró un problema de fondo que **habría arruinado el pago**:

1. El `PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID` que hay en el entorno **no es el de hoy**.
   Comparación booleana contra el artefacto nuevo: **NO coincide**. Y sí coincide con el
   artefacto archivado del **26 de septiembre** — es decir, es una resolución de hace cuatro
   días, vencida.
2. `PASSPORT_TEST_M5_PAYMENT_RESOLUTION_EXPIRES_AT` está **ausente**.
3. `CreateBrebPaymentCertificationExecutor` sólo valida el vencimiento **si esa variable está
   presente**. Ausente, **omite la comprobación por completo** — gap ya documentado por el
   propio proyecto en XPAY-529 §16: «si `EnvResolutionExpiresAtUtc` está ausente, la vigencia
   de la resolución NO puede verificarse localmente».

Consecuencia si se hubiera continuado a la Fase D: el Payment se habría construido con una
resolución **vencida hace cuatro días**, sin ningún guard local que lo frenara, y habría
gastado la **única** llamada de pago autorizada en una petición condenada al rechazo.

Por eso **no se ejecutó el pago**, pese a estar autorizado: el ticket condiciona el avance
consecutivo a que «todas las precondiciones y guards pasen», y uno no pasó.

### Lo que falta, y por qué no lo hice

La corrección es sustituir en `~/.passport-sandbox.env` el `RESOLUTION_ID` obsoleto y añadir
`..._EXPIRES_AT`. **XPAY-601 autoriza normalizar el directorio privado de artefactos, no
editar el archivo de entorno.** No se improvisó.

La vía limpia **no** es escribir el valor a mano: basta **eliminar la línea obsoleta**
(respaldándola) y volver a ejecutar `configure-m5-payment-resolution`, que entonces
transferirá por sí mismo el `resolution_id` y el `expires_at` de hoy. Así el valor nunca pasa
por manos humanas ni por la herramienta.

---

## Estado de T6 / T7 — SUPERADO dentro de este mismo ticket

> **Aviso de lectura.** Esta sección refleja el estado **intermedio**, cuando la Fase C
> estaba bloqueada y el pago aún no se había ejecutado. Una autorización adicional
> desbloqueó el flujo dentro del mismo ticket: el resultado **real y final de XPAY-601**
> está en §9–§12 y es **T6 = PASS · T7 = FAIL** (por `PAYLOAD_UNPARSEABLE`). Se conserva
> el texto original por trazabilidad, no como conclusión vigente.

```
(estado intermedio, ya superado)
WEBHOOK_DELIVERY_OBSERVED=NO      INBOUND_EVENTS_OBSERVED=NONE
SIGNATURE_MODE=ENFORCE            SIGNATURE_VALID=NOT_OBSERVED
WEBHOOK_PROCESSED=NOT_OBSERVED    HTTP_401_OBSERVED=NO
PASSPORT_AUTOMATIC_RETRIES_OBSERVED=0
T6_STATUS=INCONCLUSIVE            T7_STATUS=INCONCLUSIVE
READY_FOR_T8=NO
```

En ese momento, sin pago no había entrega que observar. `INCONCLUSIVE`, no `FAIL`.

---

## Lo que no se tocó

```
SECOND_RESOLVE_EXECUTED=NO   SECOND_PAYMENT_CREATED=NO   MANUAL_WEBHOOK_POSTS=0
WALLET_MODIFIED=NO   LEDGER_MODIFIED=NO   BREB_RETIROS_MODIFIED=NO
AZURE_MODIFIED=NO    PASSPORT_DASHBOARD_MODIFIED=NO
PRODUCTION_MODIFIED=NO   MIDECISOR_MODIFIED=NO
```

Sin Retrieve HTTP, sin llaves creadas o modificadas, sin QR, sin Test Event, sin webhook
sintético, sin migraciones, sin `deploy`/`stage`/`commit`/`push`.

---

## Seguridad

No contiene `resolution_id`, `payment_id`, llave o BCODE completos, identificadores completos
de cuenta o cliente, tokens, cabeceras de autorización, secretos ni PII. Todas las
comparaciones de identificadores se hicieron de forma **booleana**.


---

## 7. Desbloqueo de la Fase C (autorización adicional)

Respaldo privado creado (`~/.passport-sandbox.env.bak-2026-09-30T21-20Z`, modo `0600`) y
eliminada **exclusivamente** la entrada obsoleta `PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID`.
Comparación de nombres contra el respaldo: **una sola línea de diferencia**, ninguna otra
variable tocada. `ENV_BACKUP_CREATED=YES` · `STALE_RESOLUTION_ID_REMOVED=YES` ·
`OTHER_ENV_VARIABLES_MODIFIED=NO`.

`configure-m5-payment-resolution` → `result=TRANSFERRED`, cero HTTP. Escribió el
`resolution_id` desde el artefacto privado sin imprimirlo y creó el marcador de consumo
separado.

### Aclaración sobre `EXPIRES_AT` — no es una anomalía

`M5ResolveEnvTransfer` declara **únicamente** `PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID`: el
harness **nunca** escribe `..._EXPIRES_AT`, y tampoco estaba en el respaldo original. Es el
gap que el propio proyecto documenta en XPAY-529 §16, no un fallo de esta ejecución.

Consecuencia: la comprobación local de vencimiento del Payment queda **inerte**. Por eso la
vigencia se verificó contra el **artefacto privado autoritativo** inmediatamente antes de la
llamada: `restante = 1337 s`, `VIGENTE = SI`.

`RESOLUTION_MATCHES_XPAY_601=YES` · `RESOLUTION_VALID_AT_PAYMENT_TIME=YES`

---

## 8. Fase D — Payment único

```
PAYMENT_CREATE_CALLS=1        payment_create_calls=1     retry_count=0
operation=POST /v1/payments/breb
executed_at=2026-09-30T21:25:10Z
result=PASS   request_accepted=True   status=PROCESSING
evidencia=docs/certificacion/passport-breb/M5-PAYMENT-CREATE/evidence-2026-09-30T21-25-10Z.json
```

`payment_id` presente (sólo fingerprint en la evidencia), moneda `COP`, `amount_present=True`
con el fixture positivo del harness. `FINANCIAL_OPERATION_EXECUTED=YES`.
`SECOND_PAYMENT_CREATED=NO` · `SECOND_RESOLVE_EXECUTED=NO`.

---

## 9. Fase E — T6: entrega REAL del webhook · **PASS**

Passport entregó el webhook **8 segundos** después del pago. Observado en los logs
read-only del App Service de QA (no hubo acceso a base de datos; ver §11).

```
WEBHOOK_DELIVERY_OBSERVED=YES
entregas POST totales = 36        ventana = 21:25:18.72Z → 21:25:21.71Z
tamaños de cuerpo distintos = 1159, 2163, 2247, 18723, 20970 bytes
```

Hay entrega real, autenticada, desde Passport hacia el endpoint de XPAY por un pago
entrante. **`T6_STATUS=PASS`.**

## 10. Fase E — T7: validación · **firma PASS, payload FAIL**

```
SIGNATURE_MODE=ENFORCE
firmas outcome=Valid      = 36 / 36      (enforcing=True)
firmas no válidas         = 0
HTTP 401 observados       = 0
SIGNATURE_VALID=YES
```

**La verificación HMAC-SHA256 V2 funcionó a la primera sobre payloads reales**, con
`sigLen=64` (hexadecimal) y `tsLen=10` (epoch en segundos), en cuerpos de hasta 20 970 bytes.
Es el resultado más difícil de todo el frente: confirma que el canonicalizador reproduce
exactamente el `json.dumps(sort_keys=True, separators=(',',':'))` de Python sobre datos
reales, y que el Secret Token del Dashboard coincide con el App Setting.

Pero:

```
PASSPORT_WEBHOOK_PAYLOAD_UNPARSEABLE = 36 / 36
HTTP 400 = 36 / 36        HTTP 200 = 0
eventos procesados = 0    INBOUND_EVENTS_OBSERVED=NONE
WEBHOOK_PROCESSED=NO
T7_STATUS=FAIL
```

El Anexo exige para T7 «validar payload y estado del webhook». El payload **no** se validó:
el parser lo rechazó y el endpoint devolvió 400 en las 36 entregas. Nada se persistió en
`passport_webhook_events`.

### Causa raíz, deducida con evidencia

El validador de firma **sí** canonicalizó cada cuerpo, y para ello necesita que
`JsonDocument.Parse` tenga éxito. Por tanto el JSON recibido **es válido**. El parser de
payload sólo puede haber fallado por su única otra condición: exigir que la raíz sea un
objeto (`root.ValueKind == JsonValueKind.Object`).

**Conclusión: la raíz del payload real NO es un objeto — es un array.** Passport agrupa
eventos en lote, a diferencia de los ejemplos de objeto único que muestra su documentación.
Los tamaños de 18–21 KB son coherentes con lotes, no con un evento suelto.

No es un defecto de la firma ni del transporte: es una **suposición equivocada sobre la forma
del payload**, tomada de los ejemplos documentados.

### Reintentos

Las 36 entregas ocurrieron en 3 segundos, con separaciones de 0,1–0,3 s. No corresponden al
retroceso exponencial documentado (1/2/4/8 s), así que combinan entregas distintas con
reintentos rápidos y **no son separables** con la información de los logs.
`PASSPORT_AUTOMATIC_RETRIES_OBSERVED=UNKNOWN` (36 entregas totales). Confirman, eso sí, lo
que Gustavo anticipó: una respuesta distinta de 200 provoca reintento. No se interfirió
ninguno.

**No se cambió `ENFORCE`** — y no habría servido: la firma era válida, el problema es el
parser. No se repitió el pago, no se creó otro, no hubo POST sintético ni replay.

---

## 11. Limitación de verificación

No hay cadena de conexión a la base de datos de QA en este entorno, así que
`passport_webhook_events` **no se consultó**. No se improvisó ningún acceso. La observación
se hizo con los logs del App Service, que son suficientes para T6 y para el veredicto de T7:
registran cada entrega, el resultado de la firma y el código HTTP devuelto.

---

## 12. Estado final

```
T6_STATUS=PASS
T7_STATUS=FAIL
READY_FOR_T8=NO
```

T8 consulta el estado final del pago vía API; tiene sentido ejecutarlo cuando T7 esté
resuelto, para que la narrativa muestre API y webhook coincidiendo sobre el mismo pago.

El pago de hoy queda como evidencia utilizable de T6. Corregir el parser para aceptar un
array en la raíz —y procesar cada elemento del lote con la misma clave de idempotencia— es un
cambio de código acotado que **este ticket prohíbe expresamente**, y requiere su propio
ticket.
