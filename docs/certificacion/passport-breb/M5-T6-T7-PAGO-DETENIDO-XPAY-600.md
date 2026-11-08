# XPAY — Passport / Bre-B — M5-T6 / M5-T7 — Pago autorizado, DETENIDO por precondición

**Ticket:** XPAY-600 · **Fecha:** 2026-09-30 (UTC) · **Entorno:** Sandbox
**Resultado:** **NO se ejecutó el pago.** La resolución de XPAY-599 venció antes de usarse.

```
PAYMENT_CREATE_CALLS=0
NEW_RESOLVE_CALLS=0
FINANCIAL_OPERATION_EXECUTED=NO
MONEY_MOVED=NO
```

El pago estaba autorizado; la precondición crítica del propio ticket no se cumplió, y el
ticket ordena detenerse en ese caso sin ejecutar Payment ni un Resolve nuevo.

---

## 1. Verificación de la precondición (sin ninguna llamada a Passport)

Se inspeccionó el artefacto privado de XPAY-599 **sin imprimir el `resolution_id`**.

| Comprobación | Resultado |
|---|---|
| La resolución existe en el artefacto privado | **SÍ** |
| Pertenece a KEY_A | **SÍ** — `key_value_fingerprint` idéntico al de las ejecuciones certificadas |
| Marcada como consumida | **NO** |
| `resolved_at` | 2026-09-30T15:14:24.750Z |
| `expires_at` | 2026-09-30T15:44:24.750Z |
| Momento de la verificación | 2026-09-30T15:46:11Z |
| **Vigente** | **NO — vencida por 107 segundos** |

```
RESOLUTION_AVAILABLE=YES
RESOLUTION_VALID_AT_PAYMENT_TIME=NO
```

La ventana de vigencia de una resolución Bre-B en este Sandbox es de **30 minutos exactos**
(15:14:24.750Z → 15:44:24.750Z). Es el dato operativo más importante que deja este ticket.

Como efecto colateral positivo, esto **cierra la duda abierta en XPAY-599**: el
`key_value_fingerprint` de la resolución de hoy coincide con el de las tres ejecuciones
certificadas de septiembre, de modo que KEY_A es la misma llave vigente y no la histórica
reemplazada.

---

## 2. Segundo obstáculo, detectado y no modificado

En el directorio de captura privado conviven ahora **dos** artefactos sin prefijo
`CONSUMED-`: el del 26-sep (que sí tiene su marcador de consumo, pero cuyo archivo base
permanece) y el del 30-sep.

`M5ResolveArtifactReader` exige **exactamente uno** y, ante varios, devuelve
`MultipleArtifactsFound` → `LocalBlocked`. Por tanto, tal como está el directorio, tanto
`configure-m5-payment-resolution` como un futuro Resolve quedarían bloqueados localmente.

**No se movió, renombró ni eliminó ningún archivo**, y no se tocó el guard: el ticket prohíbe
forzarlo. Queda reportado para que se resuelva con autorización explícita antes del próximo
intento.

---

## 3. Defensa en profundidad verificada

Aunque se hubiera intentado el pago, no habría salido ninguna llamada a Passport:

- `configure-m5-payment-resolution` es una operación **exclusivamente local**
  (`M5ResolveEnvTransfer` → `M5ResolveArtifactReader`, lectura del perfil de usuario). No
  abre ningún cliente HTTP. `RESOLUTION_CONFIGURATION_LOCAL_ONLY=YES`.
- `CreateBrebPaymentCertificationExecutor` valida `PASSPORT_TEST_M5_PAYMENT_RESOLUTION_EXPIRES_AT`
  **antes** de cualquier HTTP y, si está vencida, aborta con el mensaje «No se realizó
  ninguna llamada a Passport».

La parada de este ticket y el fail-closed del harness coinciden.

---

## 4. Estado de T6 / T7

```
WEBHOOK_DELIVERY_OBSERVED=NO
INBOUND_EVENTS_OBSERVED=NONE
SIGNATURE_VALID=NOT_OBSERVED
WEBHOOK_PROCESSED=NOT_OBSERVED
HTTP_401_OBSERVED=NO
PASSPORT_AUTOMATIC_RETRIES_OBSERVED=0

T6_STATUS=INCONCLUSIVE
T7_STATUS=INCONCLUSIVE
READY_FOR_T8=NO
```

No hubo pago, luego no hubo entrega que observar. `INCONCLUSIVE`, no `FAIL`: nada falló —
simplemente no llegó a ejercitarse.

Se deja constancia de una limitación de verificación: la comprobación read-only de
`passport_webhook_events` **no es posible desde esta herramienta**, porque no hay cadena de
conexión a la base de datos de QA en este entorno. Cuando haya una entrega real que
verificar, la consulta deberá ejecutarse desde donde sí exista ese acceso.

---

## 5. Lo que este ticket NO tocó

```
SECOND_PAYMENT_CREATED=NO
MANUAL_WEBHOOK_POSTS=0
WALLET_MODIFIED=NO
LEDGER_MODIFIED=NO
BREB_RETIROS_MODIFIED=NO
AZURE_MODIFIED=NO
PASSPORT_DASHBOARD_MODIFIED=NO
SIGNATURE_MODE=ENFORCE
PRODUCTION_MODIFIED=NO
MIDECISOR_MODIFIED=NO
```

Sin POST al webhook, sin Test Event, sin replay, sin escritura en base de datos, sin
`stage`/`commit`/`push`.

---

## 6. Condición para el próximo intento

La lección operativa es de **secuenciación**, no de diseño: con una ventana de 30 minutos, el
Resolve y el Payment Create deben autorizarse y ejecutarse **en la misma sesión y de forma
consecutiva**. Separarlos en tickets distintos, con una revisión humana intermedia, agota la
ventana antes de usarla — que es exactamente lo que ocurrió aquí.

Un intento futuro necesita, en este orden:

1. resolver el bloqueo de `MultipleArtifactsFound` en el directorio privado —requiere decidir
   qué hacer con el artefacto base del 26-sep ya consumido—;
2. una autorización que cubra **Resolve + configure + Payment Create de forma consecutiva**,
   no un Resolve aislado.

---

## Seguridad de este documento

No contiene `resolution_id`, `payment_id`, llave o BCODE completos, identificadores completos
de cuenta o cliente, tokens, cabeceras de autorización, secretos ni PII.
