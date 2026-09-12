# Runbook de activación — Purge B4 (retención MiDecisor)

**Estado del documento:** BORRADOR — pendiente de aprobación de negocio.
**Alcance:** este documento es exclusivamente operativo/documental. No autoriza,
por sí mismo, ninguna activación real. Su existencia no cambia el estado
técnico de Purge B4, que sigue **implementado y dormido** (gate en OFF,
sin scheduler, endpoint sin invocar).

**Referencia técnica:** política de retención XPAY-212 §Q.8. Feature mergeada
a `main` en el commit `65ead9607957aaea3d99951bccb6156fd03a3fca` (PR #20),
certificada por CI (build 0/0, 409 tests pasados, migración 042 aplicada y
verificada). Ver XPAY-217 a XPAY-225 para la evidencia completa.

---

## 1. Qué es esto y para quién es

Este runbook describe cómo, cuándo y por quién se ejecutaría de forma segura
la **primera purga real** de datos crudos de MiDecisor con más de 5 años de
antigüedad. Está escrito para que lo pueda seguir alguien que **no
necesariamente sabe programar** — un operador con acceso administrativo a
XPAY, guiado por la aprobación explícita de negocio.

Si en algún punto algo no está claro o no coincide exactamente con lo
descrito aquí: **deténgase y pida ayuda técnica antes de continuar.**

---

## 2. Qué hace la purga (resumen operativo)

- Busca solicitudes de cupo cuya consulta de riesgo a MiDecisor terminó
  (`fecha_fin` del intento) hace más de **5 años**.
- Si la solicitud está en revisión manual pendiente, **no la toca** — la deja
  para una ejecución futura.
- Si es elegible, borra permanentemente el dato crudo del proveedor (7 campos
  del intento + 9 campos del snapshot de la solicitud) y dos marcas internas
  de fecha quedan grabadas para que esa fila nunca se vuelva a evaluar.
- **Nunca** toca la decisión de crédito, los motivos, el cupo otorgado, ni el
  ledger — sólo el dato crudo del proveedor.
- **Nunca** llama a MiDecisor. Es una operación puramente interna sobre la
  base de datos de XPAY.

---

## 3. Precondiciones obligatorias (checklist previo)

Ninguna ejecución real puede comenzar si falta **cualquiera** de estos puntos:

| # | Precondición | Cómo se verifica |
|---|---|---|
| 1 | Aprobación humana explícita de negocio para **esta** ejecución concreta | Firma/registro del `BUSINESS_OWNER` (ver §10) — no una aprobación genérica pasada |
| 2 | Migración `042_cartera_p0_raw_purga_marca.sql` confirmada aplicada en el ambiente objetivo | Confirmación del equipo de base de datos/deploy — **no asumir** porque ya corrió en CI |
| 3 | Auditoría SQL de sólo lectura ejecutada sobre el ambiente objetivo, antes de tocar nada | Ver §3.1 — bloqueador B7, sigue pendiente hoy |
| 4 | Conteo de candidatos elegibles conocido y razonable | Resultado de la auditoría del punto 3 |
| 5 | Conteo de solicitudes en revisión manual pendiente conocido | Resultado de la auditoría del punto 3 |
| 6 | Confirmado que no existen filas con marcas de purga inconsistentes entre sí | Resultado de la auditoría del punto 3 — si aparece alguna, **detenerse**, no es un caso a purgar hoy |
| 7 | Confirmada la correlación `solicitud.numero_intento` ↔ intento gobernante para la muestra auditada | Resultado de la auditoría del punto 3 |
| 8 | Tamaño del primer lote decidido y aprobado (ver §4) | Documento firmado por `BUSINESS_OWNER` + `TECHNICAL_OPERATOR` |
| 9 | Logs y audit log accesibles y monitoreables durante la ejecución | Confirmación del `TECHNICAL_OPERATOR` antes de empezar |
| 10 | Responsable operativo identificado para esa ejecución concreta | Nombre/rol asignado en el registro de ejecución (§11) |
| 11 | Ventana de ejecución definida (día/hora, con margen para observar resultados) | Acordada entre `BUSINESS_OWNER` y `TECHNICAL_OPERATOR` |
| 12 | Procedimiento de STOP acordado y entendido por quien ejecuta | Leer y confirmar entendimiento de §7 antes de empezar |

### 3.1 Auditoría SQL previa (bloqueador B7 — sigue pendiente)

Antes de la primera ejecución real, alguien con acceso de sólo lectura a la
base de datos del ambiente objetivo debe confirmar, **sin modificar nada**:

- Cuántas filas cumplirían hoy el criterio de purga (más de 5 años vencidas).
- Cómo se distribuyen esas filas por fecha de vencimiento.
- Cuántas de ellas están retenidas por revisión manual pendiente.
- Si existe alguna fila con datos crudos pero marcas de purga ya presentes
  (esto sería un estado inconsistente — si aparece, **detenerse** y escalar
  a soporte técnico antes de continuar, no continuar "a ver qué pasa").
- Cuántas filas no tienen ningún dato crudo que purgar.
- Cuántas filas, si las hubiera, muestran evidencia de haber sido purgadas
  por un mecanismo anterior.
- Que la relación entre el número de intento de la solicitud y el intento
  real que guarda el dato es la esperada, en una muestra representativa.

Esta auditoría **no se ha ejecutado todavía** (ver XPAY-226, bloqueador B7).
No puede saltarse.

---

## 4. Tamaño del primer lote

**Importante — separar dos cosas distintas:**

- **`DEFAULT_TÉCNICO_ACTUAL`** (ya existe en el código, sin cambios en este
  documento): `BatchSize=200`, `MaxBatches=10` (hasta 2000 filas por
  invocación). Éste es el límite técnico de una llamada al endpoint, pensado
  para operación ya rodada, no para una primera ejecución.

- **`PRIMER_LOTE_RECOMENDADO`** (propuesta de este runbook, requiere
  aprobación — el código no impone ni conoce este número):
  Empezar con el **lote más pequeño que el sistema permita observar con
  claridad** — conceptualmente, un puñado de filas (del orden de unas
  decenas), nunca el máximo técnico de 2000 en la primera vez. La idea es
  poder revisar cada resultado individualmente antes de continuar, no
  procesar volumen.

El número exacto del primer lote **no queda fijado por este documento** — es
una decisión que debe tomar `BUSINESS_OWNER` junto con `TECHNICAL_OPERATOR`
en el momento de la ejecución, informada por el resultado de la auditoría
del §3.1 (por ejemplo, si la auditoría muestra sólo 12 candidatos elegibles
en total, ese sería el techo natural del primer lote).

No se modifican los defaults del código en este documento ni en ningún
paso de XPAY-227.

---

## 5. Activación del gate (conceptual — no ejecutar aquí)

El interruptor técnico es una variable de configuración del ambiente:

```
CARTERA_PURGE_B4_ENABLED=true
```

- Sólo debe configurarse en el ambiente objetivo **después** de que las 12
  precondiciones del §3 estén completas y aprobadas.
- Es fail-closed: ausente, `false`, o cualquier otro valor que no sea
  exactamente `true` (sin importar mayúsculas/minúsculas) deja la purga
  deshabilitada. No hay ningún otro interruptor ni override.
- **No se documentan aquí valores, secretos, ni el mecanismo interno del
  proveedor de configuración del ambiente** — eso lo gestiona quien
  administra ese ambiente, fuera de este documento.
- **Volver a apagarlo** es tan simple como borrar la variable o ponerla en
  `false` en ese mismo ambiente — no requiere ningún cambio de código, ni
  redeploy del binario, ni reversión de ningún commit.

Este runbook **no ejecuta** esta configuración. Sólo la describe.

---

## 6. Ejecución manual (flujo conceptual — no ejecutar aquí)

1. Confirmar que el gate está en `true` en el ambiente objetivo (§5).
2. Verificar que el servicio XPAY está saludable en ese ambiente (por
   ejemplo, vía el endpoint de salud/version existente).
3. Un operador con rol `ADMIN_XPAY` o `SUPERUSUARIO` invoca:
   `POST /api/cartera-ordinaria/admin/purge-b4/ejecutar-lote`
   (sin cuerpo, sin parámetros — el endpoint no acepta ninguno).
4. Capturar el `correlationId` de la respuesta y de los logs asociados.
5. Revisar el resultado agregado devuelto: candidatos, purgados, ya
   purgados, retenidos por revisión manual, no elegibles, errores, duración.
6. Revisar los logs y el audit log asociados a ese `correlationId`.
7. Comparar los conteos observados contra lo esperado según la auditoría
   previa del §3.1.
8. Decidir, con criterio conservador: ¿se detiene aquí (recomendado para la
   primera vez) o se continúa con un siguiente lote pequeño?

Ninguno de estos pasos se ejecuta en XPAY-227.

---

## 7. Condiciones de STOP inmediato

Detener toda ejecución de inmediato — sin excepción — si ocurre cualquiera
de los siguientes:

- Aparece una excepción de invariante (`CarteraPurgaB4InvarianteException`)
  en los logs.
- Los conteos post-ejecución no coinciden con lo esperado de la auditoría
  previa.
- Cualquier error de base de datos inesperado.
- La cantidad purgada es mayor o distinta de lo planeado para ese lote.
- Aparece cualquier dato crudo, documento, score, o payload del proveedor en
  un log — esto nunca debería ocurrir; si ocurre, es un incidente de
  seguridad, no sólo un error operativo.
- El resultado de ejecutar el mismo lote dos veces no es idéntico (la
  operación debe ser siempre idempotente).
- Alguna marca de purga queda en un estado que no coincide con lo
  documentado (una presente sin la otra, o una marca sin que el dato
  correspondiente esté realmente vacío).
- Cualquier solicitud en revisión manual pendiente aparece purgada.
- Cualquier cambio inesperado en decisión, cupo o ledger de una solicitud
  tocada por la purga — la purga **nunca** debe afectar estos campos.
- Timeouts repetidos o errores operacionales que se repiten en más de una
  fila sin explicación clara.

**Ante cualquier STOP: no reintentar automáticamente, no volver a invocar el
endpoint "a ver si esta vez funciona".** Se requiere un diagnóstico de sólo
lectura antes de cualquier siguiente intento, y ese diagnóstico debe
completarse antes de decidir si se continúa, se corrige algo, o se cancela
la ejecución completa.

---

## 8. Rollback y recuperación — límites reales (sin ficción)

**La purga de datos crudos es destructiva por diseño.** No existe un
"deshacer" dentro del sistema — una vez que un campo queda en `NULL`, XPAY
no guarda una copia de lo que había ahí. Este documento no presenta ningún
procedimiento de rollback automático porque no existe.

Ante un problema durante o después de una ejecución:

- Volver el gate (`CARTERA_PURGE_B4_ENABLED`) a `false` inmediatamente en el
  ambiente afectado, para impedir cualquier nueva ejecución.
- Detener cualquier ejecución en curso o planeada.
- Preservar intactos todos los logs, el audit log, y el `correlationId` de
  la ejecución en cuestión — son la única evidencia disponible de lo que
  pasó.
- No intentar "reparar a mano" escribiendo valores de vuelta en los campos
  purgados — el sistema no tiene forma de distinguir un dato reparado a mano
  de un dato real, y esto podría violar el invariante de fail-closed que
  protege el resto del sistema.
- Escalar de inmediato al responsable técnico y al responsable de
  gobernanza — no resolver esto de forma unilateral.
- Si existiera un backup autorizado de la base de datos anterior a la
  ejecución, una eventual restauración a partir de ese backup sería un
  **procedimiento completamente separado**, con su propio análisis de
  impacto (afectaría todo lo escrito en la base de datos desde el backup,
  no sólo la purga) — no se describe ni se autoriza aquí.

---

## 9. Checklist post-ejecución

Después de cada ejecución (incluida la primera), confirmar y registrar:

- [ ] Cantidad de candidatos evaluados.
- [ ] Cantidad purgados.
- [ ] Cantidad ya purgados (idempotencia).
- [ ] Cantidad retenidos por revisión manual.
- [ ] Cantidad no elegibles.
- [ ] Cantidad de errores.
- [ ] Duración de la ejecución.
- [ ] `correlationId` capturado y archivado.
- [ ] Verificado (en una muestra) que los 7 campos crudos del intento y los
      9 campos crudos del snapshot P0 quedaron en `NULL` donde se purgó.
- [ ] Verificado que los 3 metadatos derivados (no crudos) siguen presentes.
- [ ] Verificado que la decisión crediticia no cambió.
- [ ] Verificado que los motivos de decisión no cambiaron.
- [ ] Verificado que el cupo otorgado y el ledger no cambiaron.
- [ ] Verificado que las dos marcas de purga (intento y snapshot P0) quedan
      coherentes entre sí en cada fila tocada.
- [ ] Confirmado que ningún log ni el audit log contiene PII, dato crudo, ni
      payload del proveedor.

---

## 10. Modelo de autorización

Tres roles conceptuales — **no nombres de personas**:

- **`BUSINESS_OWNER`** — quien tiene la autoridad de negocio/gobernanza para
  decidir que una ejecución real puede ocurrir. Es quien aprobó
  originalmente la política de retención (XPAY-212) o su delegado formal.
- **`TECHNICAL_OPERATOR`** — quien tiene el acceso técnico (rol `ADMIN_XPAY`
  o `SUPERUSUARIO`) y ejecuta materialmente el paso del §6.
- **`REVIEWER`** — quien revisa el resultado post-ejecución (§9) de forma
  independiente a quien ejecutó, antes de dar por cerrada la ejecución.

**Regla explícita: tener el rol técnico `ADMIN_XPAY` NO equivale, por sí
solo, a tener autorización de negocio.** La primera ejecución real —y
cualquier ejecución que no sea una simple continuación ya aprobada— requiere
la aprobación explícita y específica de `BUSINESS_OWNER`, registrada en el
formato del §11, antes de que `TECHNICAL_OPERATOR` toque el endpoint.

---

## 11. Plantilla de registro de ejecución

Cada ejecución real (incluida la primera) debe quedar registrada con esta
plantilla, en un lugar accesible para auditoría futura. **Nunca incluir PII
ni dato crudo del proveedor en este registro.**

```
RUN_ID:                  <identificador secuencial de la ejecución>
ENVIRONMENT:             <ambiente objetivo>
DATE_UTC:                <fecha y hora UTC de la ejecución>
BUSINESS_APPROVER:       <rol/persona que autorizó>
TECHNICAL_OPERATOR:      <rol/persona que ejecutó>
PRE_AUDIT_REFERENCE:     <referencia al resultado de la auditoría del §3.1>
BATCH_SIZE:              <tamaño de lote usado>
MAX_BATCHES:             <máximo de lotes usado>
CANDIDATES_PRE:          <conteo esperado según auditoría previa>
PURGED:                  <valor devuelto por el endpoint>
ALREADY_PURGED:          <valor devuelto por el endpoint>
HELD_MANUAL_REVIEW:      <valor devuelto por el endpoint>
NOT_ELIGIBLE:            <valor devuelto por el endpoint>
ERRORS:                  <valor devuelto por el endpoint>
DURATION:                <valor devuelto por el endpoint>
CORRELATION_ID:          <correlationId capturado>
POST_VALIDATION:         <PASS / FAIL — resultado del checklist §9>
STOP_TRIGGERED:          <YES / NO — y cuál condición del §7, si aplica>
NOTES:                   <observaciones libres, sin PII ni dato crudo>
```

---

## 12. Estado de los bloqueadores después de este runbook

| Bloqueador | Antes de este documento | Después de este documento |
|---|---|---|
| **B5** — Runbook de ejecución/rollback/incident-response | NOT_READY | **DOCUMENTED** — este documento lo cubre (§6, §7, §8) |
| **B6** — Criterio de primer lote | NOT_READY | **DOCUMENTED** (criterio conservador, §4) — el número final sigue **STILL_REQUIRES_EXECUTION_TIME_APPROVAL** (no hay número fijo, se decide junto con la auditoría real) |
| **B8** — Autorización explícita de primera ejecución real | NOT_READY | El modelo de autorización queda **DOCUMENTED** (§10, §11) — pero la autorización en sí sigue **STILL_REQUIRES_EXECUTION_TIME_APPROVAL**: nadie ha dado esa aprobación todavía, este documento sólo define cómo debería darse |

**No se declaran cerrados** (siguen exactamente como estaban, sin cambio):

- **B7** — Auditoría SQL previa a la primera ejecución real: **sigue
  pendiente**. Este runbook la exige (§3.1) pero no la ejecuta.
- **B10** — Confirmación de que la migración 042 corrió en el ambiente real
  (QA/producción): **sigue pendiente**. Este runbook la exige (precondición
  #2) pero no la verifica.

---

## 13. Recordatorio final

Este documento **no activa nada**. Redactarlo no cambia ningún estado
técnico de Purge B4: el gate sigue en `OFF`, no existe ninguna configuración
de activación en ningún ambiente versionado, el stub de autorización de
MiDecisor sigue activo, y no se ha ejecutado ninguna purga real. La primera
ejecución real sólo puede ocurrir después de completar el checklist del §3
y obtener la aprobación explícita descrita en el §10.
