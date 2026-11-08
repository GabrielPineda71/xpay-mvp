# XPAY — Passport / Bre-B — M5-T7 — Corrección: payload en LOTE (raíz array)

**Ticket:** XPAY-602 · **Fecha:** 2026-09-30 (UTC)
**Estado:** implementado y probado OFFLINE · **no desplegado** · **sin nuevo pago**

```
ROOT_OBJECT_SUPPORTED=YES        ROOT_ARRAY_SUPPORTED=YES
SIGNATURE_VALIDATION_CHANGED=NO  SIGNATURE_VALIDATION_SCOPE=FULL_BODY
FOCUSED_TESTS=PASS 127/127       FULL_BACKEND_TESTS=PASS 1045/1045
PASSPORT_API_CALLS=0             PAYMENT_CALLS=0   RESOLVE_CALLS=0   WEBHOOK_QA_POSTS=0
```

---

## 1. El defecto observado

XPAY-601 creó un pago real B→A en Sandbox y Passport entregó el webhook 8 segundos después.
Lo observado en los logs del App Service de QA:

| Hecho | Valor |
|---|---|
| Entregas POST recibidas | **36** (21:25:18.72Z → 21:25:21.71Z) |
| Firmas HMAC-SHA256 V2 válidas | **36 / 36**, con `enforcing=True` |
| HTTP 401 | **0** |
| Tamaños de cuerpo | 1159, 2163, 2247, 18723, 20970 bytes |
| Resultado del endpoint | **36 / 36 → HTTP 400 `PAYLOAD_UNPARSEABLE`** |
| Eventos procesados / persistidos | **0** |

T6 quedó en `PASS` —hubo entrega real— y T7 en `FAIL`: el payload no se validó.

## 2. Causa raíz

El validador de firma **canonicalizó con éxito** cada cuerpo, y para hacerlo necesita que
`JsonDocument.Parse` funcione. Por tanto **el JSON recibido era válido**. El parser de payload
sólo podía fallar por su única otra condición: exigir `root.ValueKind == JsonValueKind.Object`.

**Conclusión: la raíz del payload real no es un objeto, es un array.** Passport Sandbox
entrega **lotes**, a diferencia de los ejemplos de objeto único de su documentación. Los
cuerpos de 18–21 KB son coherentes con lotes y no con un evento suelto.

No fue un problema de firma, canonicalización, secreto, TLS, ruteo ni configuración: fue una
suposición equivocada sobre la forma del payload, tomada de los ejemplos documentados.

## 3. La firma NO se tocó

`SIGNATURE_IMPLEMENTATION_STATUS = PROVEN_AGAINST_REAL_PASSPORT_PAYLOAD`

Ni el canonicalizador ni el validador se modificaron: una sola línea de esos archivos habría
puesto en riesgo lo único que ya estaba demostrado contra tráfico real. Se mantiene:

- la firma se valida **una vez**, sobre el **cuerpo completo** recibido
  (`SIGNATURE_VALIDATION_SCOPE=FULL_BODY`);
- la descomposición del lote ocurre **después** de establecer la autenticidad;
- **no** se intenta validar cada elemento como si tuviera firma propia.

## 4. La solución

### Parser — `PassportWebhookPayloadParser.TryParseDelivery`

Nuevo tipo `PassportWebhookDeliveryShape` con cuatro formas explícitas:

| Forma | Significado |
|---|---|
| `SingleEvent` | raíz objeto — **comportamiento original intacto** |
| `Batch` | raíz array con uno o más objetos |
| `EmptyBatch` | raíz array sin elementos |
| `Invalid` | no es JSON, la raíz no es objeto ni array, o algún elemento del array no es objeto |

La lógica de proyección de un evento se extrajo a `ParseElement(JsonElement)` y **no cambió**:
el soporte de lotes la invoca por elemento en lugar de reimplementarla. `TryParse(string)`
sigue existiendo y delega, de modo que la API previa no sufre regresión.

### Servicio — `ProcessDeliveryAsync`

Recorre los eventos llamando a `ProcessAsync`, **el mismo pipeline ya probado**. Clasificación,
sanitización e idempotencia se aplican **por evento**.

### Controller

`Invalid` → 400 · `EmptyBatch` → 200 · resto → procesar y 200. La respuesta incluye
`events`, `processedCount` y `duplicateCount`, sin dato sensible alguno.

## 5. Política de errores del lote

**`BATCH_PREVALIDATION_IMPLEMENTED=YES` · `PARTIAL_PROCESSING_ON_STRUCTURAL_ERROR=NO`**

La validación estructural recorre **todos** los elementos **antes** de proyectar cualquiera. Si
uno solo no es un objeto, se devuelve `Invalid` con la lista **vacía**: el servicio nunca
recibe un lote a medias, así que no puede persistir parcialmente. Respuesta 400.

Un elemento que **sí** es objeto pero cuyo evento es desconocido o saliente **no** invalida el
lote: se registra y no se procesa, exactamente como ya ocurría con un objeto único. Tratarlo
como error estructural rompería el caso legítimo de un lote mixto.

Un fallo interno **inesperado** durante la persistencia sí propaga y produce 500, permitiendo
el reintento de Passport. Los elementos ya registrados quedan protegidos por su propia
idempotencia, así que el reintento no los duplica. Es la distinción deliberada entre error
**estructural** —nada se persiste— y error **interno** —lo persistido es idempotente—.

### Array vacío

Estructuralmente válido y sin nada que procesar. Se responde **200**: reintentarlo sería
inútil. Queda auditado como `PASSPORT_WEBHOOK_EMPTY_BATCH`.

## 6. Idempotencia

**`PER_EVENT_IDEMPOTENCY=YES` · `IDEMPOTENCY_KEY=(event_type, passport_payment_id)`**

Sin cambios respecto a XPAY-594, y sigue siendo la clave correcta precisamente por los lotes
reales: un mismo lote puede traer `received`, `confirmed` y `settled` del **mismo**
`payment_id`. Con `payment_id` solo, dos de esas tres entregas legítimas se perderían como
falsos duplicados. `payload_hash` permanece como evidencia secundaria, nunca como identidad.

**Riesgo residual sin cambios:** la tabla no tiene índice único sobre esa clave, así que la
deduplicación es una comprobación previa a la inserción y **no es atómica**. Dos entregas
simultáneas del mismo evento pueden insertar dos filas. Impacto sólo de auditoría —este flujo
no mueve dinero— y su cierre exige una migración, fuera del alcance de este ticket.
`DB_MIGRATION_CREATED=NO`.

## 7. Seguridad y ausencia de efectos financieros

```
RAW_BODY_PERSISTED=NO   PII_PERSISTED=NO
WALLET_MODIFIED=NO      LEDGER_MODIFIED=NO      BREB_RETIROS_MODIFIED=NO
OUTBOUND_EVENTS_FINANCIALLY_INERT=YES
```

Cada elemento se sanitiza individualmente: `sender` y `receiver` se descartan por completo, y
de `destination_key` sólo se conservan el tipo y un booleano de presencia — nunca el valor,
porque una llave Bre-B puede ser un teléfono, un correo o un documento. El cuerpo crudo no se
persiste ni se registra.

El aislamiento financiero está verificado por pruebas que fallarían ante una regresión: el
escaneo del código fuente —con los comentarios retirados— prohíbe
`ApplyPassportPaymentStatusAsync`, `BrebPaymentStateMachine`, `BrebPaymentService`,
`WalletMovimientos`, `LedgerMovimientos`, `PassportBrebRetiros`, `IPassportHttpClient` e
`IPassportPaymentClient`, y ahora se aplica también al código modificado por XPAY-602. Otra
prueba exige que la validación estructural **preceda** al procesamiento en el controller.

**No se copió ningún payload real al repositorio.** Los fixtures son sintéticos y reproducen
sólo características estructurales; ningún identificador, nombre, cuenta, `payment_id` o
`resolution_id` real aparece en el código ni en esta evidencia.

## 8. Pruebas

```
FOCUSED_TESTS      = PASS 127/127   (antes 95 — 32 nuevas)
FULL_BACKEND_TESTS = PASS 1045/1045 (antes 1013 — cero regresiones)
SKIPPED            = 0
```

| Caso exigido | Cubierto por |
|---|---|
| 1 · raíz objeto → sigue igual | `Caso01_RaizObjeto_SigueSiendoEventoUnico` (+ la API previa `TryParse`) |
| 2 · array con un evento | `Caso02_ArrayConUnEventoEntrante` |
| 3 · array con varios | `Caso03_ArrayConVariosEventosEntrantes_TodosProyectados` |
| 4 · received/confirmed/settled del mismo pago | `Caso04_LoteConTresEstadosDelMismoPago_ProcesaLosTres` (SQL) |
| 5 · duplicado exacto | `Caso05_DuplicadoExactoDentroDelLote_EsIdempotente` (SQL) |
| 6 · salientes sin efecto financiero | `Caso06_SalientesEnElLote_SeClasificanNotInbound` + su variante SQL |
| 7 · array vacío | `Caso07_ArrayVacio_EsFormaPropia_NoInvalido` |
| 8 · elemento no-objeto | `Caso08_ElementoNoObjeto_EsInvalido` (5 variantes) |
| 9 · inválido entre válidos, sin parcial | `Caso09_UnElementoInvalidoEntreValidos_InvalidaTodoElLote_SinProyeccionParcial` |
| 10 · firma válida sobre array | `Caso10_FirmaValidaSobreUnCuerpoArray_EsAceptada` + independencia de formato |
| 11 · firma inválida sobre array | `Caso11_...Rechaza401EnEnforce` + alteración de un elemento |
| 12 · timestamp inválido | `Caso12_TimestampFueraDeVentana...` + firma ausente en ENFORCE/OBSERVE |
| 13 · reintento del lote | `Caso13_ReintentoDelMismoLote_NoDuplicaFilas` (SQL) |
| 14 · varios estados ≠ duplicados | `Caso14_TresEstadosDelMismoPago_SonEventosDistintos` |
| 15 · Wallet/Ledger/retiros intactos | `ElSoporteDeLotes_NoIntroduceDependenciasNiRutasFinancieras` + `IdBrebRetiro` nulo en los SQL |

**`REAL_PASSPORT_HMAC_REGRESSION_PROTECTED=YES`**, mediante vectores fijos para raíz objeto
(XPAY-594) y raíz array (XPAY-602), ambos generados con la implementación de referencia del
contrato (`json.dumps` de Python + HMAC-SHA256) y marcados `XPAY_INTERNAL_TEST_VECTOR`:
Passport no publica ninguno. La confirmación contra tráfico **real** es la evidencia
documental de XPAY-601 (36/36 válidas), no una prueba ejecutable.

Las pruebas SQL de lote siguen el patrón del repositorio: se saltan en local sin cadena de
conexión y son obligatorias en CI. En esta ejecución **su cuerpo SQL no corrió**.

## 9. Riesgos residuales

1. **La forma del lote es una deducción, no un contrato publicado.** Se sostiene en que la
   canonicalización tuvo éxito y el parser sólo podía fallar por la raíz. El payload real
   nunca se persistió —por diseño—, así que no hay una captura que lo confirme campo por
   campo. Si el lote llegara envuelto en un objeto contenedor en lugar de un array desnudo,
   este arreglo no bastaría y volvería a verse `PAYLOAD_UNPARSEABLE`. La próxima entrega real
   lo confirmará o lo refutará en un solo intento.
2. **Concurrencia sin índice único** (§6), sin cambios.
3. **Ningún elemento del lote se correlaciona con un retiro**, así que la acreditación
   contable de un pago entrante sigue siendo alcance de Fase 65, fuera de T6/T7.

```
IMPLEMENTATION_READY_FOR_QA_DEPLOY=YES
```

## Seguridad de este documento

Sin payload real, secretos, tokens, firmas, cuerpos crudos, identificadores reales de pago o
resolución, cuentas bancarias ni PII.
