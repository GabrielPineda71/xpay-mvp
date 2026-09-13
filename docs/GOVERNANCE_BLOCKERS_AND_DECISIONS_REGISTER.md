# XPAY MVP — Registro de Blockers y Decisiones Externas

**Versión:** 1.0
**Fecha de creación:** 2026-09-12
**Tipo:** Registro de gobernanza — fuente única y oficial de blockers externos,
contractuales, de ownership, y decisiones humanas pendientes o resueltas que
afectan al sistema.

---

## 1. Propósito y alcance

Este documento centraliza el seguimiento de asuntos que **no se resuelven
escribiendo código** — dependen de una decisión humana, de un tercero externo
a XPAY, o de una confirmación contractual/de titularidad. Nace del hallazgo de
XPAY-251: no existía ningún registro oficial de este tipo, y el número
"bloqueador 037" se usó dos veces, en dos comentarios de código distintos,
para dos asuntos completamente distintos.

A partir de este documento:

- todo blocker externo o decisión pendiente/resuelta relevante para el
  sistema debe tener una entrada aquí con un ID oficial (`XGOV-B-NNNN` o
  `XGOV-D-NNNN`);
- los comentarios de código que necesiten referenciar un asunto de este tipo
  deben citar el ID oficial de este registro, no inventar una numeración
  propia ni reutilizar números de otros contextos (fases, migraciones,
  prompts de sesión, etc.).

## 2. Qué NO es este registro (exclusiones explícitas)

- **No es** un backlog de bugs QA ordinarios — eso es
  `docs/QA_INTERNAL_ISSUES_TRACKING.md`.
- **No es** un changelog de código.
- **No es** un runbook operativo — los pasos de ejecución detallados viven en
  los runbooks correspondientes (p. ej. `docs/MIDECISOR_PURGE_B4_RUNBOOK.md`).
- **No es** un repositorio de secretos — nunca debe contener contraseñas,
  connection strings, tokens, ni ningún valor de credencial. Sólo se registra
  la *existencia o ausencia* de una credencial cuando sea relevante para
  entender el blocker, nunca su valor.
- **No reemplaza** documentos firmados (ACTAs, contratos). Un documento
  firmado sigue siendo su propia fuente de verdad legal — este registro sólo
  lo referencia por nombre/ruta cuando corresponda, nunca reproduce ni
  resume su contenido.

## 3. Convención oficial de IDs

```
XGOV-B-NNNN   → BLOCKER    (un obstáculo externo/contractual/de ownership)
XGOV-D-NNNN   → DECISION   (una decisión humana, pendiente o ya resuelta)
```

- `NNNN`: cuatro dígitos, secuenciales dentro de cada tipo (`XGOV-B-0001`,
  `XGOV-B-0002`, ... ; `XGOV-D-0001`, `XGOV-D-0002`, ...).
- Los IDs se asignan **únicamente en el momento de escribir la entrada** en
  este documento — nunca por adelantado, nunca reservados, nunca inferidos
  de un número que ya aparezca en otro contexto (código, migraciones,
  prompts de sesión).
- Un ID, una vez asignado, **nunca se reutiliza** — ni siquiera si la entrada
  termina en `DESCARTADO`.

## 4. Estados

| Estado | Significado |
|---|---|
| `ABIERTO` | Recién identificado; el diagnóstico todavía no está completo. |
| `BLOQUEADO_EXTERNO` | El diagnóstico ya está completo; el siguiente paso depende de un tercero fuera del control de XPAY. |
| `DECISION_PENDIENTE` | Ya no depende de un tercero externo; depende de que alguien dentro de XPAY tome una decisión explícita. |
| `RESUELTO_CERRADO` | Tiene evidencia de cierre concreta y fecha de cierre. Nunca se marca así sin ambos campos llenos. |
| `DESCARTADO` | Dejó de ser relevante (cambio de alcance, decisión de no perseguirlo). Requiere una línea explicando por qué, igual que un cierre. |

## 5. Reglas de gobernanza

1. Una entrada = un asunto. Nunca mezclar dos temas distintos en un mismo ID.
2. Los IDs nunca se reutilizan, ni siquiera si la entrada se descarta.
3. Los comentarios de código que mencionen un blocker/decisión deben
   referenciar el ID oficial (`XGOV-B-000N` / `XGOV-D-000N`), nunca un número
   ad hoc nuevo.
4. Ninguna entrada se marca `RESUELTO_CERRADO` sin los campos **Evidencia** y
   **Fecha de cierre** llenos.
5. Prohibido registrar secretos, contraseñas, connection strings o cualquier
   valor de credencial — sólo su existencia/ausencia si es relevante.
6. Si el código y este registro se contradicen, la entrada debe señalarlo
   explícitamente en sus notas — nunca resolverse en silencio.
7. Un documento firmado (ACTA, contrato) sigue siendo la fuente de verdad de
   sí mismo — este registro sólo lo referencia por ruta/nombre.
8. Los cambios de estado se anotan como una línea de historial mínima
   (fecha + estado nuevo), no como una bitácora narrativa detallada.
9. Ningún número histórico usado informalmente en comentarios de código
   (p. ej. "bloqueador 037") es, por sí mismo, un ID oficial de este
   registro — ver §7.

---

## 6. Entradas

### XGOV-B-0001 — Titularidad de credenciales MiDecisor: XPAY vs. DAFIN/Xelecredit

| Campo | Valor |
|---|---|
| **ID** | XGOV-B-0001 |
| **Tipo** | BLOCKER |
| **Estado** | DECISION_PENDIENTE |
| **Fecha de apertura** | 2026-09-12 |
| **Owner/Responsable** | SIN_ASIGNAR |

**Descripción:** No está confirmado si las credenciales de MiDecisor/DataCrédito
que XPAY usaría para consultas de riesgo pertenecen a XPAY o son credenciales
históricas de DAFIN/Xelecredit. Mientras esto no se confirme explícitamente,
no deben asumirse ni reutilizarse credenciales históricas de Xelecredit para
XPAY.

**Impacto:** Es el `PRIMARY_NEXT_BLOCKER` identificado en XPAY-250 para avanzar
el frente MiDecisor. Ninguna credencial `MIDECISOR_*` está hoy configurada en
QA (`xpay-api-qa`) — su ausencia es, en parte, consecuencia directa de este
blocker sin resolver (no tendría sentido provisionar credenciales de origen
incierto). El blocker secundario `MIDECISOR_ENDPOINT_CLIENT_VS_PN` (`/client`
vs `/pn`, aún sin ID oficial propio en este registro) es lógicamente posterior
a este: no correspondería negociar/confirmar un contrato técnico con el
proveedor bajo una titularidad de cuenta todavía sin resolver.

**Evidencia:**
- `backend/Xpay.Api/Integrations/MiDecisor/MiDecisorOptions.cs` — comentario
  original que documentaba esto como "bloqueador 037" (número histórico, no
  oficial — ver §7).
- Confirmado por `az webapp config appsettings list` sobre `xpay-api-qa`:
  ninguna clave `MIDECISOR_BASE_URL` / `MIDECISOR_CLIENT_ID` /
  `MIDECISOR_CLIENT_SECRET` / `MIDECISOR_USERNAME` / `MIDECISOR_PASSWORD`
  presente (sólo se verificó *presencia de nombre*, nunca ningún valor).

**Dependencias:** Precede al blocker del endpoint `/client` vs `/pn`
(pendiente de asignación de ID oficial en una entrada futura).

**Criterio de cierre:** Confirmación explícita y documentada de a quién
pertenecen las credenciales MiDecisor/DataCrédito para uso de XPAY, emitida
por la parte con autoridad para decidirlo (negocio/legal, no un desarrollador
inspeccionando código).

**Decisión/Resolución:** _(pendiente)_

**Fecha de cierre:** _(pendiente)_

**Referencias relacionadas:** XPAY-250 (identificó este blocker como
prioritario), XPAY-251 (confirmó la ausencia de un registro formal previo),
XPAY-252 (diseño de este registro), XPAY-253 (creación de este registro).

**Notas de seguridad:** No se debe usar, solicitar, ni imprimir ninguna
credencial de MiDecisor/DataCrédito mientras este blocker permanezca abierto.
No reutilizar ninguna credencial histórica de DAFIN/Xelecredit.

---

### XGOV-D-0001 — Regla de producto para convertir resultados MiDecisor en decisión crediticia XPAY

| Campo | Valor |
|---|---|
| **ID** | XGOV-D-0001 |
| **Tipo** | DECISION |
| **Estado** | RESUELTO_CERRADO |
| **Fecha de apertura** | Anterior a esta auditoría (fecha exacta no verificable objetivamente; el comentario original en `MiDecisorResultado.cs` es de la etapa M1 del proyecto) |
| **Owner/Responsable** | SIN_ASIGNAR |

**Descripción:** El comentario histórico en
`backend/Xpay.Api/Integrations/MiDecisor/MiDecisorResultado.cs` indicaba que
los campos `score`/`viabilidad`/`rating`/`montoSugerido` del resultado
normalizado de MiDecisor no constituían por sí mismos una decisión crediticia,
y que convertirlos en una decisión (APROBADA/RECHAZADA/MontoAprobado)
requería una "regla de producto autorizada" — referenciada informalmente en
ese comentario como "bloqueador 037" (ver §7).

**Esa condición quedó resuelta** mediante la regla de producto implementada
en `CarteraDecisionEngine.cs` (M2.4b), que sí realiza esa conversión de forma
determinística y con respaldo de gobernanza verificado.

**Impacto:** Esta decisión **ya no constituye un blocker activo** para
MiDecisor. No debe confundirse con **XGOV-B-0001** (titularidad de
credenciales), que sigue `DECISION_PENDIENTE` y continúa siendo el blocker
primario actual del frente MiDecisor.

**Evidencia:**
- `backend/Xpay.Api/Common/CarteraDecisionEngine.cs` — motor de decisión
  determinístico (gates de información general, estado documento, tipo
  documento, rango edad, score, comportamiento de pago, viabilidad, rating de
  recaudos, y fórmula final con mínimo/redondeo/tope).
- `backend/Xpay.Api.Tests/Services/CarteraDecisionEngineTests.cs` — 55 casos
  `[Fact]`/`[Theory]` cubriendo cada gate individualmente.
- Política working-copy MiDecisor (documento externo al repositorio,
  SHA256 `61b27597955fd11f5016e5997e9dbe2681dcd0a0dd2c94c70358963847520bb6`,
  verificado sin cambios) — secciones **§A** (score), **§B** (viabilidad),
  **§F** (fórmula/mínimo), **§G** (redondeo/tope) y **§L** (montoSugerido),
  cada una marcada en el propio documento como "APROBADO POR PRODUCTO/RIESGO".
- XPAY-254 — auditoría dedicada de cierre, que verificó coincidencia numérica
  exacta entre el código y la política (bandas de score, factores de
  viabilidad, cupo mínimo, unidad y dirección de redondeo, y el tratamiento
  informativo de `montoSugerido`).

**Dependencias:** Ninguna (no depende de XGOV-B-0001 ni de ningún otro ítem
de este registro).

**Criterio de cierre:** Se considera cerrada cuando, de forma verificable:
1. la regla de producto está implementada;
2. existe cobertura suficiente de tests;
3. existe evidencia de autorización/gobernanza (no sólo un comentario de
   código afirmándolo);
4. código y política están alineados sin diferencias materiales
   identificadas.

Los cuatro criterios fueron verificados y satisfechos en XPAY-254.

**Decisión/Resolución:** La decisión crediticia XPAY se determina por
`CarteraDecisionEngine` conforme a la política vigente verificada;
`MiDecisorResultado` permanece como representación del resultado del
proveedor y no es, por sí mismo, el motor de decisión. `montoSugerido`
permanece informativo y no modifica, limita ni eleva el cupo, conforme a la
política verificada (§L).

**Fecha de cierre:** 2026-09-12

**Referencias relacionadas:** XPAY-251 (identificó la ambigüedad histórica de
"bloqueador 037"), XPAY-252 (diseño de este registro), XPAY-253 (creación de
este registro, dejó este ítem B sin ID hasta auditarse), XPAY-254 (auditoría
dedicada de cierre), XPAY-255 (formalización de esta entrada).

**Notas de seguridad:** Ninguna — esta entrada no involucra credenciales ni
valores sensibles.

---

### XGOV-B-0002 — Ambiente/URL base de MiDecisor asignado a XPAY (MIDECISOR_BASE_URL)

| Campo | Valor |
|---|---|
| **ID** | XGOV-B-0002 |
| **Tipo** | BLOCKER |
| **Estado** | BLOQUEADO_EXTERNO |
| **Fecha de apertura** | 2026-09-12 |
| **Owner/Responsable** | SIN_ASIGNAR |

**Descripción:** XPAY no cuenta actualmente con evidencia autoritativa que
confirme qué ambiente/host Base URL de MiDecisor/DataCrédito debe usar.
`MIDECISOR_BASE_URL` identifica el host/base del proveedor (p. ej.
dev/qa/test/demo/prod) sobre el que se construyen las URLs absolutas de
autenticación y de consulta de riesgo. Este valor **no debe inferirse** de
defaults de código, ejemplos del comentario original, o ambientes históricos
de otro proyecto. No existe actualmente ninguna configuración QA válida de
esta clave (`xpay-api-qa` no tiene ninguna variable `MIDECISOR_*`, confirmado
en XPAY-250).

**Impacto:** Mientras esta entrada permanezca abierta: no debe configurarse
`MIDECISOR_BASE_URL` por inferencia en ningún ambiente ; no puede
considerarse listo el acceso real al proveedor MiDecisor ; no debe iniciarse
UAT real contra el proveedor. Esta entrada, por sí sola, **no constituye
autorización** para activar runtime, credenciales, ni cambiar App Settings.

**Relación con XGOV-B-0001:** `INSUFFICIENT_EVIDENCE`. No se sabe todavía si
DataCrédito/MiDecisor entrega ambiente/Base URL y credenciales a XPAY como un
único paquete de onboarding, o como decisiones separadas. Por tanto: **no**
se fusiona con `XGOV-B-0001` ; **no** se declara que sean independientes
contractual o comercialmente ; ambas entradas permanecen separadas hasta
obtener evidencia externa. Si en el futuro una fuente autoritativa demuestra
que constituyen una sola decisión del proveedor, la duplicidad se resuelve
mediante referencia cruzada y cierre documentado — nunca borrando IDs
históricos (§5, regla 2 — "los IDs nunca se reutilizan").

**Relación con el endpoint `/client` vs `/pn`:** `SEPARATE_INDEPENDENT_ISSUE`
(XPAY-260). `MIDECISOR_BASE_URL` (host/ambiente) y `MIDECISOR_QUERY_PATH`
(ruta de la API, `/client` vs `/pn`) son técnicamente distintos — propiedades
independientes de `MiDecisorOptions`, cada una con su propia variable de
entorno, que sólo se combinan al formar la URL final. El blocker del
endpoint `/client` vs `/pn` no tiene todavía una entrada XGOV propia; no se
crea en esta edición.

**Evidencia:**
- `backend/Xpay.Api/Integrations/MiDecisor/MiDecisorOptions.cs` — comentario
  original que documentaba esto como "bloqueador 037" (número histórico, no
  oficial — ver §7) ; `BaseUrl` sin default estructural, a diferencia de
  `AuthPath`/`QueryPath`.
- `backend/Xpay.Api/Integrations/MiDecisor/MiDecisorTokenProvider.cs` — falla
  cerrado (`MiDecisorConfigurationException`) si `BaseUrl` está ausente o no
  forma una URL absoluta válida junto con `AuthPath`.
- `backend/Xpay.Api/Integrations/MiDecisor/MiDecisorClient.cs` — mismo
  comportamiento fail-closed combinando `BaseUrl` con `QueryPath`.
- XPAY-250 — confirmó ausencia de toda clave `MIDECISOR_*` en QA (sólo
  presencia de nombre verificada, nunca ningún valor).
- XPAY-259 — detectó este tercer uso histórico ambiguo de "bloqueador 037"
  y detuvo la actualización de comentarios hasta esta auditoría.
- XPAY-260 — auditoría dedicada de este asunto: confirmó ausencia de
  evidencia autoritativa de ambiente/URL, y la separación técnica respecto
  al endpoint `/client` vs `/pn`.

**Dependencias:** Ninguna dependencia de cierre confirmada con
`XGOV-B-0001` (ver "Relación con XGOV-B-0001" arriba — la posible
dependencia es, ella misma, el objeto de la incertidumbre).

**Criterio de cierre:** Confirmación explícita y documentada, por una parte
con autoridad para definirlo, del ambiente y host/Base URL que
MiDecisor/DataCrédito asigna a XPAY para el ambiente objetivo. La evidencia
debe permitir identificar inequívocamente el valor/configuración autorizada
de `MIDECISOR_BASE_URL` sin inferirlo de código, defaults, URLs de ejemplo,
documentación genérica, Xelecredit/DAFIN, ni de otro proyecto.
Adicionalmente, al cierre debe documentarse explícitamente si esta
asignación forma parte o no del mismo paquete contractual/provisional que
`XGOV-B-0001`.

**Decisión/Resolución:** _(pendiente)_

**Fecha de cierre:** _(pendiente)_

**Referencias relacionadas:** XPAY-250 (confirmó ausencia de configuración
QA), XPAY-259 (detectó el tercer uso histórico ambiguo), XPAY-260 (auditoría
dedicada de cierre de clasificación), XPAY-261 (formalización de esta
entrada).

**Notas de seguridad:** No se debe usar, solicitar, ni imprimir ningún valor
de `MIDECISOR_BASE_URL` real mientras este blocker permanezca abierto. No
inferir ni reutilizar ningún ambiente/host histórico de otro proyecto.

---

## 7. Referencias históricas ambiguas y mapeo oficial

Antes de la creación de este registro, el número **"bloqueador 037"** se usó
de forma informal en comentarios de código, en **tres lugares distintos, para
tres asuntos completamente distintos**. Ninguno de esos usos es, ni ha sido
nunca, un ID oficial de gobernanza — es un número interno de comentario, sin
ningún sistema de tracking detrás. Se documentan aquí los tres usos para
dejar constancia de la ambigüedad y evitar que se siga interpretando "037"
como un identificador único:

- **A.** `backend/Xpay.Api/Integrations/MiDecisor/MiDecisorOptions.cs` —
  ambiente/host Base URL asignado a XPAY (`MIDECISOR_BASE_URL`). **Ya
  reclasificado formalmente como [`XGOV-B-0002`](#xgov-b-0002--ambienteurl-base-de-midecisor-asignado-a-xpay-midecisor_base_url)**
  (ver §6, arriba). Detectado en XPAY-259, auditado y formalizado en
  XPAY-260/261.

- **B.** `backend/Xpay.Api/Integrations/MiDecisor/MiDecisorOptions.cs` —
  titularidad de credenciales XPAY vs. DAFIN/Xelecredit. **Ya reclasificado
  formalmente como [`XGOV-B-0001`](#xgov-b-0001--titularidad-de-credenciales-midecisor-xpay-vs-dafinxelecredit)**
  (ver §6, arriba).

- **C.** `backend/Xpay.Api/Integrations/MiDecisor/MiDecisorResultado.cs` —
  "convertir score/viabilidad/rating/montoSugerido en una decisión de crédito
  requiere una regla de producto autorizada (bloqueador 037)". **Ya
  reclasificado formalmente como [`XGOV-D-0001`](#xgov-d-0001--regla-de-producto-para-convertir-resultados-midecisor-en-decisión-crediticia-xpay),
  en estado `RESUELTO_CERRADO`**, tras la auditoría dedicada de XPAY-254 (ver
  §6, arriba).

En los tres casos, "037" **nunca fue, ni es, un ID oficial de este
registro** — era únicamente un número de comentario interno, reutilizado por
coincidencia para tres asuntos distintos. Los IDs oficiales que reemplazan
cada uso son `XGOV-B-0002` (A), `XGOV-B-0001` (B), y `XGOV-D-0001` (C),
respectivamente.

**Ningún comentario de código se ha modificado como parte de la creación ni
edición de este registro.** La actualización de los comentarios en
`MiDecisorOptions.cs` y `MiDecisorResultado.cs` para que referencien los IDs
oficiales de este documento es un paso posterior y separado, no autorizado
en la creación/edición de este archivo.

---

## 8. Historial de cambios del registro

- **2026-09-12** — Creación del registro (XPAY-253). Alta de `XGOV-B-0001`
  (`DECISION_PENDIENTE`).
- **2026-09-12** — Alta de `XGOV-D-0001` (`RESUELTO_CERRADO`), tras auditoría
  dedicada XPAY-254. Actualización de §7 para reflejar el mapeo sin
  ambigüedad de los dos usos históricos de "bloqueador 037" (XPAY-255).
- **2026-09-12** — Alta de `XGOV-B-0002` (`BLOQUEADO_EXTERNO`) tras auditoría
  XPAY-260 del tercer uso histórico de "bloqueador 037". Actualización de §7
  para reflejar el mapeo de los tres usos históricos (XPAY-261).
