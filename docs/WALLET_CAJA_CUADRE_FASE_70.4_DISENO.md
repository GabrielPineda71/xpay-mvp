# XPAY MVP — Wallet: Diseño Fase 70.4

**Fase:** 70.4 — Apertura, Cuadre y Cierre de Caja
**Fecha UTC:** 2026-07-25
**Estado:** 🔶 **DISEÑO — NO IMPLEMENTADO** (requiere aprobación explícita antes de codear)
**Depende de:** Fase 70.1 (recargas), Fase 70.2 (liquidación XPAY), Fase 70.3 (cierre diario comercio)

---

## 1. Diagnóstico de tablas y servicios actuales

| Pieza existente | Relevancia para 70.4 |
|---|---|
| `wallet_recargas_comercio` | Única operación de efectivo real hoy (recarga). No tiene ningún concepto de "caja"/turno — cada recarga es independiente, atribuida a `id_usuario_cajero` + `id_tienda` (nullable) + `fecha_recarga`. |
| `WalletRecargaComercioService.RecargarWalletAsync` | Punto exacto donde debe **exigirse caja abierta** antes de proceder (punto 2). Hoy no valida nada de "turno". |
| `comercio_usuarios` (`rol_comercio`, `id_establecimiento`) | Ya resuelve quién es `CAJERO`/`ADMIN_SEDE_COMERCIO`/`ADMIN_COMERCIO` y a qué sede pertenece. Se reutiliza tal cual — no se toca. |
| `comercio_establecimientos` | Sedes. No tiene hoy ninguna columna de configuración horaria — se necesita agregar la hora de cierre automático (punto 9). |
| `ComercioScopeService` | Reutilizable sin cambios para resolver alcance (comercio/sede/rol) en los nuevos endpoints. |
| `wallet_cierres_diarios_comercio(_detalle)` (Fase 70.3) | Consolidado **por comercio completo y fecha**, generado explícitamente por `ADMIN_COMERCIO`. No sabe nada de cajas individuales — 70.4 debe alimentarlo sin duplicar ni modificar su lógica de snapshot (punto 10). |
| `wallet_liquidaciones_recaudo_comercio(_detalle)` (Fase 70.2) | Proceso de XPAY para recibir el efectivo ya recaudado — ortogonal a la caja del cajero; una caja no genera liquidaciones. |
| `ventas_qr` / `comercio_ventas_qr_contexto` | Fuente de datos para "actividad digital" del cuadre (punto 8B). **Hallazgo:** `VentaQr.Estado` solo tiene `CONTINGENCIA`/`LIQUIDADA` hoy — no existe un estado de anulación/reverso. El cuadre debe indicar explícitamente **"Reversos QR no soportados actualmente"** — nunca un `0` que sugiera que la función existe y no encontró reversos. El neto QR se calcula con las ventas confirmadas existentes (`CONTINGENCIA` + `LIQUIDADA`, que son los únicos dos estados reales hoy). |
| `Auditoria` (tabla genérica ya existente) | Se mantiene **exclusivamente** para trazabilidad de transiciones (punto 13) — no se reutiliza como bandeja de notificaciones al usuario. Se necesita una tabla nueva y separada para eso (punto 12). |
| Infraestructura de *background jobs* | **No existe** (`grep` confirma cero `BackgroundService`/`Hangfire`/`Quartz` en el proyecto). Afecta directamente el diseño del cierre automático híbrido (punto 9). |
| Infraestructura de notificaciones | **No existe** (cero `EmailService`/`SmsService`/push). Afecta el punto 12 — se diseña una estructura de notificaciones internas propia, separada de `Auditoria`. |

---

## 2. Operaciones que deben exigir caja abierta

| Operación | ¿Exige caja abierta? | Razón |
|---|---|---|
| `POST /api/comercio/wallet-recargas` (recarga efectivo) | **Sí** | Es la operación de efectivo que la caja controla — regla explícita del negocio. |
| Futuros movimientos manuales de caja (retiro, devolución, entrega parcial — 70.4.2) | **Sí** | Son operaciones de la caja misma. |
| Ventas QR (`POST /api/qr/pagar` y similares) | **No** | Instrucción explícita: "no bloquear automáticamente pagos QR del comercio". |
| Cartera Ordinaria, Bre-B, Libranza | **No** | No son operaciones de efectivo físico de un cajero de comercio — quedan fuera del concepto de caja. |
| Liquidación XPAY (Fase 70.2) | **No** | La ejecuta XPAY, no el cajero; opera sobre recargas ya existentes, no sobre la caja. |
| Cierre diario de comercio (Fase 70.3) | **No** directamente, pero **si el cierre diario de una fecha ya está `CERRADO`, no se puede abrir una caja nueva con `fecha_operativa` igual a esa fecha** (regla de bloqueo global, ver sección 11). |

**Decisión de diseño:** la "caja" se ata al **usuario que opera**, no al valor de `rol_comercio`. Hoy `ADMIN_COMERCIO` y `ADMIN_SEDE_COMERCIO` también pueden hacer recargas en efectivo (Fase 70.1 lo permite a los 3 roles) — si lo hacen, también necesitan su propia caja abierta. El diseño de permisos (sección 7) sigue reflejando lo que pediste (CAJERO opera la suya, los admins consultan/revisan/consolidan), pero técnicamente cualquier usuario que recargue efectivo queda sujeto a la misma regla de caja abierta.

---

## 3. Modelo de datos propuesto (migración `028`, nueva — no toca 025/026/027)

```
wallet_cajas_comercio
├─ id_caja                    BIGINT IDENTITY PK
├─ id_unidad_negocio          BIGINT NOT NULL                 -- derivado de comercios.id_unidad_negocio
├─ id_comercio                BIGINT NOT NULL                 -- FK comercios
├─ id_comercio_aliado         BIGINT NULL
├─ id_establecimiento         BIGINT NOT NULL                 -- FK comercio_establecimientos — la caja SIEMPRE es de una sede
├─ id_usuario_cajero          BIGINT NOT NULL                 -- FK usuarios — quien opera la caja
├─ fecha_operativa            DATE NOT NULL                   -- día operativo Colombia (mismo criterio que 70.3)
├─ fecha_apertura_utc         DATETIME2 NOT NULL
├─ fecha_cierre_utc           DATETIME2 NULL
├─ estado                     VARCHAR(30) NOT NULL DEFAULT 'ABIERTA'
├─ fondo_inicial              DECIMAL(18,2) NOT NULL DEFAULT 0
├─ efectivo_esperado          DECIMAL(18,2) NULL              -- snapshot al iniciar cuadre
├─ efectivo_contado           DECIMAL(18,2) NULL              -- informado por el cajero al cerrar
├─ diferencia                 DECIMAL(18,2) NULL              -- efectivo_contado - efectivo_esperado
├─ cerrada_automaticamente    BIT NOT NULL DEFAULT 0
├─ observaciones_cajero       VARCHAR(500) NULL
├─ revisado_por_usuario       BIGINT NULL                     -- FK usuarios (ADMIN_SEDE_COMERCIO/ADMIN_COMERCIO)
├─ fecha_revision             DATETIME2 NULL
├─ observaciones_revision     VARCHAR(500) NULL
├─ created_at                 DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
│
├─ CHECK estado IN ('ABIERTA','EN_CUADRE','CERRADA','CERRADA_AUTOMATICAMENTE','CON_DIFERENCIA','REVISADA')
├─ CHECK fondo_inicial >= 0
├─ CHECK (efectivo_contado IS NULL OR efectivo_contado >= 0)
└─ UNIQUE (id_usuario_cajero, fecha_operativa)                -- una caja por cajero por día (70.4.1; multi-turno mismo día queda para más adelante)

wallet_caja_movimientos
├─ id_movimiento           BIGINT IDENTITY PK
├─ id_caja                 BIGINT NOT NULL                    -- FK wallet_cajas_comercio
├─ tipo_movimiento         VARCHAR(30) NOT NULL                -- FONDO_INICIAL|RECARGA_EFECTIVO|OTRO_INGRESO|RETIRO|DEVOLUCION|ENTREGA_PARCIAL
├─ naturaleza              CHAR(1) NOT NULL                    -- 'E' entrada / 'S' salida de efectivo
├─ valor                   DECIMAL(18,2) NOT NULL
├─ referencia_tipo         VARCHAR(40) NULL                    -- 'wallet_recargas_comercio' cuando aplica
├─ referencia_id           BIGINT NULL                         -- id_recarga cuando tipo_movimiento=RECARGA_EFECTIVO
├─ observaciones           VARCHAR(500) NULL
├─ created_at              DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
│
├─ CHECK tipo_movimiento IN ('FONDO_INICIAL','RECARGA_EFECTIVO','OTRO_INGRESO','RETIRO','DEVOLUCION','ENTREGA_PARCIAL')
├─ CHECK naturaleza IN ('E','S')
├─ CHECK valor >= 0
└─ UNIQUE (referencia_tipo, referencia_id) WHERE referencia_id IS NOT NULL  -- una recarga no se cuenta dos veces en ninguna caja
```

**Por qué una tabla de movimientos genérica y no solo un vínculo a recargas:** el
cuadre pide separar fondo inicial, recargas, "otros ingresos", retiros,
devoluciones y entregas parciales. Solo "recarga en efectivo" tiene hoy una
operación real que la produzca; las demás categorías no tienen todavía un
endpoint que las genere (ver sección 18 — quedan para 70.4.2). Modelar la tabla
genérica desde ya evita rediseñar el esquema cuando esas categorías se
implementen — pero en 70.4.1 solo se insertan filas `FONDO_INICIAL` (al abrir) y
`RECARGA_EFECTIVO` (automáticamente, en la misma transacción de cada recarga).

**Por qué no se toca `wallet_recargas_comercio`:** mismo criterio que 70.3 — el
vínculo recarga↔caja vive en `wallet_caja_movimientos.referencia_id`, nunca como
columna nueva en la tabla de recargas. Evita repetir el patrón de 025/026 de
agregar columnas cada fase.

```
caja_notificaciones_internas
├─ id_notificacion        BIGINT IDENTITY PK
├─ id_usuario_destino     BIGINT NOT NULL                 -- FK usuarios — a quién se dirige (cajero y/o cada admin relevante)
├─ id_caja                BIGINT NULL                     -- FK wallet_cajas_comercio (contexto, cuando aplica)
├─ tipo                   VARCHAR(40) NOT NULL             -- CAJA_CERRADA_AUTOMATICAMENTE|CAJA_CON_DIFERENCIA|CAJA_REVISADA
├─ titulo                 VARCHAR(200) NOT NULL
├─ mensaje                VARCHAR(1000) NOT NULL
├─ leida                  BIT NOT NULL DEFAULT 0
├─ fecha_leida            DATETIME2 NULL
├─ created_at             DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
│
├─ CHECK tipo IN ('CAJA_CERRADA_AUTOMATICAMENTE','CAJA_CON_DIFERENCIA','CAJA_REVISADA')
└─ (FK id_usuario_destino → usuarios, FK id_caja → wallet_cajas_comercio)
```

Tabla **nueva y separada de `Auditoria`** — ver sección 12 para la justificación
de por qué no se reutiliza `Auditoria` como bandeja de mensajes al usuario.

**Configuración de hora de cierre automático** — nueva columna, aditiva, sobre
tabla existente (no rompe CI porque `comercio_establecimientos` ya está fuera del
baseline 001-010):

```sql
ALTER TABLE comercio_establecimientos
    ADD hora_cierre_automatico_caja TIME NULL;   -- NULL = usa el valor por defecto del sistema (propuesto: 21:00 Colombia)
```

---

## 4. Relaciones y restricciones únicas

- `wallet_cajas_comercio.id_usuario_cajero` + `fecha_operativa` → **UNIQUE**: no se
  puede tener dos cajas abiertas el mismo día para el mismo usuario (evita doble
  apertura concurrente, mismo patrón de protección que `wallet_cierres_diarios_comercio`).
- `wallet_caja_movimientos.referencia_id` → **UNIQUE parcial** (`WHERE referencia_id
  IS NOT NULL`): una recarga no puede contarse en dos cajas — protección
  equivalente a `uq_wcdcd_id_recarga` de 70.3, pero a nivel de caja en vez de
  cierre diario. Son dos relaciones independientes sobre la misma fila de
  recarga (ver sección 10).
- `id_establecimiento` en la caja es `NOT NULL` — a diferencia del cierre diario
  (que es del comercio completo), la caja siempre pertenece a una sede concreta.
- FKs hacia `comercios`, `comercio_establecimientos`, `usuarios`,
  `wallet_cajas_comercio`, `wallet_recargas_comercio` — ninguna modifica esas
  tablas.

---

## 5. Estados y transiciones

```
(no existe) ──abrir (cajero/usuario)──▶ ABIERTA
ABIERTA ──iniciar cuadre (mismo usuario)──▶ EN_CUADRE
EN_CUADRE ──cerrar, diferencia = 0──▶ CERRADA                    (terminal, sin revisión obligatoria)
EN_CUADRE ──cerrar, diferencia ≠ 0──▶ CON_DIFERENCIA
ABIERTA ──hora límite alcanzada sin acción──▶ CERRADA_AUTOMATICAMENTE
CON_DIFERENCIA ──revisar (ADMIN_SEDE_COMERCIO/ADMIN_COMERCIO)──▶ REVISADA   (terminal)
CERRADA_AUTOMATICAMENTE ──revisar──▶ REVISADA                                (terminal)
```

Reglas:

- Sin retrocesos. Sin saltos (`ABIERTA` nunca pasa a `CERRADA` sin pasar por
  `EN_CUADRE`, salvo el camino automático que es un estado terminal distinto).
- `CERRADA` (sin diferencia) no requiere revisión — es un cierre limpio.
- `REVISADA` solo es alcanzable desde `CON_DIFERENCIA` o
  `CERRADA_AUTOMATICAMENTE` — nunca desde `CERRADA`.
- Reapertura excepcional ("si se define esa función"): **no se implementa en
  70.4.1** — se reserva un campo `reabierta BIT DEFAULT 0` + `motivo_reapertura
  VARCHAR(500) NULL` en el esquema para no bloquear su adición futura (70.4.2),
  pero sin endpoint ni transición habilitada todavía.

---

## 6. Endpoints propuestos

**Comercio** (`api/comercio/cajas`, rol `COMERCIO`, scope vía `ComercioScopeService`):

- `GET  /api/comercio/cajas/mi-caja-actual` — caja `ABIERTA`/`EN_CUADRE` del
  usuario para hoy, o `null`.
- `POST /api/comercio/cajas/abrir` `{ fondoInicial }` — valida que no exista ya
  una caja abierta para (usuario, fecha) y que el comercio+fecha no tenga ya un
  cierre diario (70.3) en estado `CERRADO`.
- `GET  /api/comercio/cajas/{idCaja}/movimientos` — detalle de efectivo +
  resumen informativo de ventas QR del mismo cajero/sede/fecha (solo lectura).
- `POST /api/comercio/cajas/{idCaja}/iniciar-cuadre` — `ABIERTA→EN_CUADRE`,
  congela `efectivo_esperado`.
- `POST /api/comercio/cajas/{idCaja}/cerrar` `{ efectivoContado, observaciones }`
  — `EN_CUADRE→CERRADA|CON_DIFERENCIA`, calcula `diferencia`.
- `GET  /api/comercio/cajas/{idCaja}/comprobante` — datos estructurados para el
  PDF (frontend).
- `GET  /api/comercio/cajas/mis-cajas` — historial propio.
- `GET  /api/comercio/cajas` (con filtros `idEstablecimiento`, `estado`, fecha) —
  listado acotado por scope: `ADMIN_SEDE_COMERCIO` ve su sede,
  `ADMIN_COMERCIO` ve todas las sedes del comercio.
- `GET  /api/comercio/cajas/{idCaja}` — detalle (scope-checked).
- `POST /api/comercio/cajas/{idCaja}/revisar` `{ observaciones }` —
  `CON_DIFERENCIA|CERRADA_AUTOMATICAMENTE → REVISADA`. Solo
  `ADMIN_SEDE_COMERCIO` (su sede) o `ADMIN_COMERCIO` (cualquier sede).
- `GET  /api/comercio/cajas/consolidado-sede?idEstablecimiento&fecha` —
  agregado **derivado** (suma en vivo, no snapshot propio) de las cajas de una
  sede para una fecha: `ADMIN_SEDE_COMERCIO` (la suya) o `ADMIN_COMERCIO`
  (cualquiera del comercio).
- `GET  /api/comercio/cajas/consolidado-comercio?fecha` — agregado derivado de
  todas las sedes del comercio para una fecha. Solo `ADMIN_COMERCIO`. No
  reemplaza ni modifica el cierre diario de comercio de la Fase 70.3 — es una
  vista adicional sobre el estado de las cajas, independiente del snapshot de
  70.3.

**Interno** (mecanismo de cierre automático, ver sección 9):

- `POST /api/internal/cajas/cerrar-automaticas` — protegido (clave compartida o
  Managed Identity), invocado por el scheduler (Azure Function Timer), no
  pensado para uso desde el frontend. Ejecuta la Capa 1 (autoritativa) del
  mecanismo híbrido.

**Notificaciones internas** (`api/comercio/notificaciones`, rol `COMERCIO`):

- `GET  /api/comercio/notificaciones` — propias, no leídas primero.
- `POST /api/comercio/notificaciones/{id}/marcar-leida`.

---

## 7. Permisos por rol

| Acción | CAJERO | ADMIN_SEDE_COMERCIO | ADMIN_COMERCIO | ADMIN_XPAY/SUPERUSUARIO |
|---|---|---|---|---|
| Abrir su propia caja | ✅ | ✅ (si opera) | ✅ (si opera) | — |
| Consultar sus propios movimientos, efectivo esperado/contado/diferencia | ✅ | ✅ | ✅ | — |
| Iniciar cuadre / cerrar su caja | ✅ | ✅ | ✅ | — |
| Consultar su comprobante/PDF individual | ✅ | ✅ | ✅ | — |
| Cerrar la caja de **otro** cajero | ❌ | ❌ | ❌ | — |
| Consultar cajas individuales de otros cajeros de su propia sede | ❌ | ✅ | ✅ | — |
| Consultar consolidado de una sede | ❌ | ✅ (la suya) | ✅ (cualquiera del comercio) | Solo lectura vía panel XPAY |
| Consultar consolidado general del comercio (todas las sedes) | ❌ | ❌ | ✅ | Solo lectura vía panel XPAY |
| Consultar cajas/consolidados de **otro comercio** | ❌ | ❌ | ❌ | Según permisos administrativos globales de XPAY |
| Revisar diferencias / cierres automáticos | ❌ | ✅ (su sede) | ✅ (todas) | — |
| Autorizar reapertura excepcional | ❌ | 🔶 reservado, no implementado en 70.4.1 | 🔶 reservado | — |
| Consolidar cierre diario de comercio (70.3, sin cambios) | ❌ | ❌ | ✅ (ya existente) | Revisar/Cerrar (ya existente) |

`ADMIN_XPAY`/`SUPERUSUARIO` consulta según sus permisos administrativos
globales ya existentes (mismo patrón que 70.3) — **no sustituye** el cierre
individual del cajero ni actúa como su reemplazo operativo; es visión de
solo lectura sobre lo que cajeros/sedes/comercios ya gestionan.

Todo validado server-side con el mismo patrón de `RequireScopeAsync` +
verificación explícita de `RolComercio`/`PuedeVerTodoComercio` usado en 70.3 —
nunca solo ocultando botones en el frontend.

---

## 8. Cálculo exacto del cuadre

**A. Movimiento de efectivo** (todo desde `wallet_caja_movimientos` de esa caja):

```
efectivo_esperado =
    fondo_inicial
  + SUM(valor WHERE tipo_movimiento IN ('RECARGA_EFECTIVO','OTRO_INGRESO'))
  - SUM(valor WHERE tipo_movimiento IN ('RETIRO','DEVOLUCION','ENTREGA_PARCIAL'))

diferencia = efectivo_contado - efectivo_esperado
```

`efectivo_esperado` se calcula y congela al transicionar `ABIERTA→EN_CUADRE`
(snapshot, igual filosofía que 70.3 — no se recalcula después). `diferencia` se
calcula al cerrar, con el `efectivo_contado` que informa el cajero.

**B. Actividad digital** (informativa, no afecta el cálculo anterior):

```
cantidad_ventas_qr, valor_bruto_qr, valor_neto_qr
    = agregado de VentaQr (estados CONTINGENCIA + LIQUIDADA — las únicas ventas
      confirmadas que existen hoy) filtrado por mismo cajero + sede + rango
      [fecha_apertura_utc, fecha_cierre_utc o ahora] vía ComercioVentaQrContexto

reversosQrSoportado = false
mensajeReversos = "Reversos QR no soportados actualmente"
```

Se muestra en la pantalla de cuadre como bloque separado, explícitamente
etiquetado como "no incluido en el efectivo esperado". El campo de
anulaciones/reversos **nunca se presenta como `0`** — el DTO expone
`reversosQrSoportado: false` y el frontend renderiza el mensaje literal en vez
de un número, para no sugerir que la función existe. El día que se implemente
un estado real de anulación en `VentaQr`, este campo pasa a `true` y se
reemplaza por el conteo real — sin necesidad de cambiar el contrato del DTO.

---

## 9. Cierre automático y mecanismo de ejecución (híbrido)

**Configuración:** `comercio_establecimientos.hora_cierre_automatico_caja TIME
NULL` (por sede); si es `NULL`, se usa un valor por defecto del sistema
(propuesto 21:00 hora Colombia) — no se agrega una segunda columna a nivel de
comercio para no complicar la resolución de fallback en 70.4.1.

El cierre automático **no depende de un único mecanismo**. Se diseñan tres
capas independientes que comparten una sola función de verificación
(`EstaVencida(caja)`), para que el sistema nunca dependa de que el scheduler ya
haya corrido:

```
EstaVencida(caja) := caja.Estado == 'ABIERTA'
                   AND AhoraColombia() > HoraLimiteDe(caja.IdEstablecimiento)
```

**Capa 1 — Scheduler autoritativo (el único que escribe el cierre):** un
proceso programado ejecuta periódicamente (propuesto cada 5 minutos) la lógica
que recorre todas las cajas `ABIERTA` con `EstaVencida(caja) = true` y las
transiciona a `CERRADA_AUTOMATICAMENTE` (snapshot, auditoría, notificación).
Es el único componente que persiste el cambio de estado.

**Capa 2 — Validación previa en endpoints transaccionales:** `RecargarWalletAsync`
(y cualquier endpoint de movimiento manual de caja en 70.4.2) no confía en el
valor de `estado` persistido — antes de operar, evalúa `EstaVencida(caja)` en
el momento exacto de la solicitud. Si la caja está lógicamente vencida (aunque
el scheduler todavía no la haya marcado en la base de datos), la operación se
**rechaza igual**, con el mismo mensaje que si ya estuviera
`CERRADA_AUTOMATICAMENTE`. Esto es lo que garantiza que "el sistema impida una
operación incluso si el scheduler todavía no ha ejecutado".

**Capa 3 — Validación adicional al consultar o consolidar:** los endpoints de
lectura (`GET /api/comercio/cajas/{id}`, listados, y la futura integración con
el detalle del cierre diario 70.3) también evalúan `EstaVencida(caja)` sobre la
marcha; si detectan una caja vencida que el scheduler aún no procesó, la
presentan en la respuesta como `estadoEfectivo: "CERRADA_AUTOMATICAMENTE
(pendiente de proceso)"` en vez de mostrar `ABIERTA` de forma engañosa. Esta
capa puede, opcionalmente, ejecutar el mismo cierre de forma perezosa ahí
mismo (mismo código que la Capa 1) como red de seguridad adicional — pero
**no es el mecanismo principal**, es un respaldo.

**Alternativas concretas para el proceso programado (Capa 1):**

| Alternativa | Cómo opera | Costo | Riesgo | Apta para |
|---|---|---|---|---|
| **Azure Function — Timer Trigger** (recomendada) | Function App nueva (plan Consumption), trigger cada 5 min, llama al endpoint interno `POST /api/internal/cajas/cerrar-automaticas` (autenticado con clave compartida o Managed Identity). | Marginal — a esta frecuencia (~8.640 ejecuciones/mes) queda muy por debajo del millón de ejecuciones gratis del plan Consumption. | Requiere aprovisionar un recurso Azure nuevo (Function App + Storage Account propio) y su propio monitoreo (Application Insights). | QA y producción — mismo mecanismo en ambos ambientes, apuntando cada uno a su propio backend. |
| **Azure Logic App — Recurrence** | Mismo concepto, sin código (diseñador visual), llama al mismo endpoint interno. | Similar a Functions a esta frecuencia. | Menos testeable/versionable que código; sigue siendo un recurso nuevo. | Alternativa válida si se prefiere no mantener un proyecto de código adicional. |
| **WebJob dentro del mismo App Service** | Se empaqueta junto al backend, corre en el mismo plan ya pagado (no crea recurso nuevo). | Cero costo adicional de recursos — usa el App Service Plan `B1` ya existente. | Consume cómputo del mismo proceso que sirve la API; complica el pipeline de deploy manual (zip) que ya se usa. | Opción intermedia si se quiere evitar un recurso Azure nuevo pero se acepta más complejidad de empaquetado. |
| **`BackgroundService` dentro de la propia API** | Un `IHostedService` con un loop interno, mismo proceso y mismo deploy de siempre. | Cero costo, cero recursos nuevos. | **Solo aceptable si se garantiza disponibilidad continua**: requiere "Always On" habilitado (disponible en el tier `B1` ya usado) y **una sola instancia activa** — si el App Service alguna vez escala a más de una instancia, cada una correría su propio barrido en paralelo sobre las mismas cajas (mitigable con el mismo lock `UPDLOCK/ROWLOCK` ya usado en 70.3, pero es complejidad evitable). | Solo si se confirma explícitamente que no habrá scale-out en el App Service de cajas. |

**Recomendación:** **Azure Function con Timer Trigger**, mismo mecanismo en QA y
producción. Es el patrón estándar de Azure para trabajos programados sobre una
app web, no comparte proceso con la API (sin riesgo de reciclaje/sleep del App
Service ni de duplicación por scale-out), y su costo es marginal a esta
frecuencia. La operación requiere: desplegar la Function junto al resto del
proyecto (puede vivir en el mismo repo como proyecto adicional), configurar la
clave/autenticación del endpoint interno, y monitorear sus ejecuciones fallidas
en Application Insights — overhead operativo pequeño y aceptado a cambio de
correctitud garantizada por las Capas 2 y 3 de todas formas.

**Al cerrar automáticamente (cualquier capa que lo detecte primero):** snapshot
de `efectivo_esperado` con lo que haya hasta ese instante,
`estado='CERRADA_AUTOMATICAMENTE'`, `efectivo_contado=NULL`, `diferencia=NULL`
(no hay conteo humano), `cerrada_automaticamente=1`, evento de auditoría
(sección 13) y notificación interna (sección 12). Queda pendiente de revisión
por `ADMIN_SEDE_COMERCIO`/`ADMIN_COMERCIO`.

---

## 10. Integración con Fase 70.3

**Dirección arquitectónica confirmada:** la caja individual del cajero (esta
fase) es la **entidad principal** del control operativo de efectivo. Los
consolidados de sede y de comercio son, conceptualmente, **consultas
derivadas sobre el conjunto de cajas** — no un flujo de generación paralelo e
independiente. Esto es un cambio de dirección respecto a cómo quedó construida
70.3 (que consolida directamente desde `wallet_recargas_comercio`, sin ningún
conocimiento de cajas) — se deja declarado aquí para que las subfases
posteriores diseñen hacia ese objetivo, pero **no implica modificar el
mecanismo de generación ya construido y validado de 70.3** sin una aprobación
explícita y su propia subfase — sería un cambio de mayor alcance (reescribir
cómo se consolida el cierre diario de comercio) que excede lo que pediste
autorizar en 70.4.1/70.4.2/70.4.3.

Para 70.4.1-70.4.3, ambos mecanismos **coexisten sin contradicción**:

- Los cierres de caja **no generan movimientos Wallet ni Ledger** — misma regla
  que 70.3.
- El cierre diario de comercio (70.3) **sigue funcionando exactamente igual**,
  consolidando por `wallet_recargas_comercio` directamente — su lógica de
  generación **no se modifica** en esta fase.
- `CAJERO` no ve el cierre diario de comercio (70.3) — corrección de alcance ya
  aplicada en el frontend (ver `docs/WALLET_CIERRE_DIARIO_COMERCIO_FASE_70.3_CIERRE.md`).
  Con 70.4, `CAJERO` consulta y cierra su **propia caja**, que es la fuente de
  verdad de "su cierre" — no una participación parcial dentro del consolidado
  de comercio.
- Una recarga puede estar vinculada simultáneamente a:
  - una caja (`wallet_caja_movimientos.referencia_id`), y
  - un cierre diario de comercio (`wallet_cierres_diarios_comercio_detalle.id_recarga`).

  Son dos relaciones **independientes** sobre la misma fila — no hay
  duplicación ni conflicto, cada `UNIQUE` protege su propia tabla.
- Mejora opcional (no incluida en 70.4.1): enriquecer el detalle del cierre
  diario (70.3) mostrando a qué caja perteneció cada recarga — un `LEFT JOIN`
  informativo adicional, sin tocar el snapshot existente. Se deja para 70.4.3.
- Los cierres diarios ya generados en Fase 70.3 **no se tocan ni se
  recalculan** — 70.4 es puramente aditivo sobre el esquema.
- Bloqueo cruzado: si el cierre diario de comercio (70.3) para una fecha ya está
  `CERRADO`, no se puede abrir una caja nueva con esa `fecha_operativa` (evita
  operar efectivo sobre un día que XPAY ya cerró administrativamente).

---

## 11. Bloqueo de operaciones

**Al cerrar la caja de un cajero** (`CERRADA`/`CON_DIFERENCIA`/
`CERRADA_AUTOMATICAMENTE`/`REVISADA`):

- Se bloquean nuevas recargas en efectivo **de ese cajero específico**
  (`RecargarWalletAsync` valida `mi-caja-actual` antes de proceder; si no hay
  una `ABIERTA`/`EN_CUADRE`, rechaza con `400`).
- No afecta a otros cajeros con caja abierta (chequeo por `id_usuario_cajero`,
  no global).
- No afecta pagos QR del comercio (instrucción explícita — `VentaQr` no
  consulta cajas en ningún punto).

**Al cerrar globalmente el comercio para una fecha** (cierre diario 70.3 en
estado `CERRADO`):

- Se bloquea la apertura de cajas nuevas con esa `fecha_operativa` (sección 10).
- Cualquier reapertura excepcional queda fuera de 70.4.1 (reservada,
  sección 5) — cuando se implemente, deberá exigir motivo obligatorio y quedar
  auditada (sección 13).

---

## 12. Notificaciones

No existe hoy ningún servicio de notificación (email/SMS/push) en el proyecto —
construir uno real es una decisión de infraestructura que excede el alcance de
diseño de esta fase. **`Auditoria` no se usa para esto** — es trazabilidad
técnica, no un mensaje dirigido a una persona; mezclar ambos conceptos en la
misma tabla dificultaría tanto el filtrado de auditoría (que crecería con
mensajes de UI) como el de notificaciones (que necesita `leida`/`fecha_leida`,
ajenos al propósito de `Auditoria`).

Propuesta para 70.4.1 — tabla nueva `caja_notificaciones_internas` (sección 3):

- Cada evento relevante (cierre automático, caja con diferencia, revisión)
  inserta una fila dirigida al `id_usuario_destino` correspondiente: el cajero
  de la caja afectada, y cada `ADMIN_SEDE_COMERCIO`/`ADMIN_COMERCIO` con
  alcance sobre esa sede/comercio.
- `GET /api/comercio/notificaciones` (propias, no leídas primero) y `POST
  /api/comercio/notificaciones/{id}/marcar-leida`.
- El frontend (Mi Comercio y el panel de sede/comercio) muestra estas
  notificaciones como una sección/badge al cargar la página — **solo en
  pantalla**, sin canal push/email/SMS/sonido todavía.
- Notificación real (correo/push/sonido) queda explícitamente para 70.4.3,
  cuando se decida qué proveedor usar (no hay uno integrado hoy).

Cada evento de notificación **también** genera su fila correspondiente en
`Auditoria` (sección 13) — son dos escrituras independientes con propósitos
distintos, no una sustituye a la otra.

---

## 13. Auditoría

Reutiliza la tabla `Auditoria` ya existente — **no se crea tabla nueva**. Cada
transición registra una fila:

```
Modulo="CAJA", Entidad="wallet_cajas_comercio", IdEntidad={idCaja},
Accion="ABRIR"|"INICIAR_CUADRE"|"CERRAR"|"CIERRE_AUTOMATICO"|"REVISAR",
ValorAnterior={estado previo}, ValorNuevo={estado nuevo},
Observacion={motivo si aplica}, IdUsuario={quien ejecuta}
```

---

## 14. Pantallas frontend

- **`/mi-comercio` — nueva sección "Mi Caja"**: estado actual, botón abrir
  (con fondo inicial), botón iniciar cuadre, formulario de efectivo contado +
  observaciones, botón cerrar, resumen de movimientos (efectivo + bloque QR
  informativo), botón de comprobante PDF.
- La sección **"Recargar Wallet"** existente debe mostrar un aviso claro y
  bloquear el flujo si el usuario no tiene caja abierta hoy.
- **Nueva página** (o sección dentro de una existente) **"Cajas Comercio"**:
  listado con filtros (sede, estado, fecha), detalle con revisión de
  diferencias y vista de cierres automáticos — mismo patrón de tabla/filtros
  que `AdminWalletCierresDiariosComercioPage.tsx`.
- Mejora opcional futura (70.4.3): bloque informativo de cajas dentro del
  detalle del cierre diario de comercio (70.3).

---

## 15. Comprobante PDF

Mismo patrón que 70.3: `jsPDF` en el navegador, a partir del snapshot ya
persistido (fondo inicial, resumen de movimientos, efectivo esperado, efectivo
contado, diferencia, estado, cajero, sede, fecha de apertura/cierre). Se crea un
archivo nuevo `comprobanteCajaPdf.ts` (estructura de datos distinta a la del
cierre diario) siguiendo el mismo estilo que `comprobanteCierrePdf.ts`, con las
fechas mostradas vía `fmtDate` (hora Colombia) — sin repetir el error de fecha
embebida como texto que se corrigió en 70.3.

---

## 16. Casos E2E (principales, para cuando se apruebe implementar)

**Positivos:** abrir caja con fondo inicial → recargar efectivo (verifica
vínculo automático en `wallet_caja_movimientos`) → iniciar cuadre (verifica
`efectivo_esperado` congelado) → cerrar sin diferencia → cerrar con diferencia →
revisión de diferencia por `ADMIN_SEDE_COMERCIO` → cierre automático por hora
límite → revisión de cierre automático por `ADMIN_COMERCIO` → descarga de
comprobante PDF.

**Negativos:** recargar sin caja abierta (bloqueado) → abrir dos cajas el mismo
día (bloqueado por `UNIQUE`) → abrir caja en fecha con cierre diario 70.3 ya
`CERRADO` (bloqueado) → cerrar una caja ya cerrada (bloqueado por estado) →
revisar como `CAJERO` (403) → consultar caja de otra sede como
`ADMIN_SEDE_COMERCIO` (403) → consultar cajas de otro comercio (403).

**Mecanismo híbrido (específico):** simular una caja vencida (hora límite ya
pasada) **antes** de que el scheduler corra → intentar recargar → debe
rechazarse igual por la Capa 2, aunque el `estado` en base de datos siga en
`ABIERTA` → consultar esa misma caja por `GET` → debe mostrarse como vencida
(Capa 3), no como `ABIERTA` → esperar a que corra el scheduler → confirmar que
queda `CERRADA_AUTOMATICAMENTE` de forma persistente (Capa 1).

**Regresión:** recarga Wallet (70.1), liquidación 70.2, cierre diario 70.3, QR
con Wallet, QR con Cupo Ordinario, pago Cartera Ordinaria — todos deben seguir
funcionando exactamente igual. Cero movimientos Wallet/Ledger nuevos generados
por apertura/cuadre/cierre de caja.

---

## 17. Plan de migración sin afectar datos actuales

- Migración **`028_wallet_cajas_comercio.sql`**, mismo patrón que 027: `SET
  XACT_ABORT ON` + `BEGIN TRY/TRANSACTION` + `BEGIN CATCH/ROLLBACK/THROW`,
  idempotente (`IF NOT EXISTS`), verificación final por consulta a
  `sys.tables`/`sys.columns`/`sys.check_constraints`/`sys.foreign_keys`/`sys.indexes`.
- Dos tablas 100% nuevas (`wallet_cajas_comercio`, `wallet_caja_movimientos`) —
  fuera del baseline de CI.
- Una columna nueva y nullable (`hora_cierre_automatico_caja`) sobre
  `comercio_establecimientos` — tabla ya fuera del baseline CI (001-010), sin
  `DEFAULT` obligatorio (nullable, no rompe filas existentes).
- **No se toca** `wallet_recargas_comercio`, `wallet_liquidaciones_recaudo_comercio`
  ni `wallet_cierres_diarios_comercio(_detalle)` — cero riesgo sobre datos ya
  validados de 70.1/70.2/70.3.
- `id_unidad_negocio` derivado de `comercios.id_unidad_negocio` en la
  aplicación, sin `DEFAULT` fijo — mismo criterio ya aplicado en 70.3.

---

## 18. Propuesta de subfases

| Subfase | Alcance |
|---|---|
| **70.4.1** | Apertura de caja (con fondo inicial), vínculo automático de recargas a la caja abierta, bloqueo de recargas sin caja abierta, cuadre básico (solo `FONDO_INICIAL` + `RECARGA_EFECTIVO`), cierre manual (con/sin diferencia), **cierre automático híbrido completo** (scheduler Azure Function + validación en endpoints transaccionales + validación en lectura), revisión de diferencias/automáticos por sede/comercio, comprobante PDF, notificaciones internas en pantalla (tabla propia, separada de `Auditoria`), auditoría de trazabilidad vía `Auditoria`, bloqueo de apertura tras cierre diario 70.3 `CERRADO`. |
| **70.4.2** | Movimientos manuales de caja (`OTRO_INGRESO`, `RETIRO`, `DEVOLUCION`, `ENTREGA_PARCIAL`) con sus propios endpoints y permisos; reapertura excepcional auditada y motivada. |
| **70.4.3** | Notificaciones reales (correo/push/sonido), integración visual del resumen de cajas dentro del detalle del cierre diario de comercio (70.3), soporte real de reversos QR si `VentaQr` incorpora ese estado, reportes/exportables. |

---

## 19. Especificación técnica exacta (antes de autorizar código)

Consolidado de todo lo que ya se describió en las secciones 3-10, en un solo
lugar, para revisión final previa a implementar.

### Tablas, columnas, tipos, PK, FK, índices, únicos, checks

**`wallet_cajas_comercio`**

| Columna | Tipo | Nulo | Restricción |
|---|---|---|---|
| `id_caja` | `BIGINT IDENTITY` | No | PK |
| `id_unidad_negocio` | `BIGINT` | No | — |
| `id_comercio` | `BIGINT` | No | FK → `comercios(id_comercio)` |
| `id_comercio_aliado` | `BIGINT` | Sí | — |
| `id_establecimiento` | `BIGINT` | No | FK → `comercio_establecimientos(id_establecimiento)` |
| `id_usuario_cajero` | `BIGINT` | No | FK → `usuarios(id_usuario)` |
| `fecha_operativa` | `DATE` | No | — |
| `fecha_apertura_utc` | `DATETIME2` | No | — |
| `fecha_cierre_utc` | `DATETIME2` | Sí | — |
| `estado` | `VARCHAR(30)` | No | `DEFAULT 'ABIERTA'`, CHECK enum (sección 5) |
| `fondo_inicial` | `DECIMAL(18,2)` | No | `DEFAULT 0`, CHECK `>= 0` |
| `efectivo_esperado` | `DECIMAL(18,2)` | Sí | — |
| `efectivo_contado` | `DECIMAL(18,2)` | Sí | CHECK `IS NULL OR >= 0` |
| `diferencia` | `DECIMAL(18,2)` | Sí | — |
| `cerrada_automaticamente` | `BIT` | No | `DEFAULT 0` |
| `observaciones_cajero` | `VARCHAR(500)` | Sí | — |
| `revisado_por_usuario` | `BIGINT` | Sí | FK → `usuarios(id_usuario)` |
| `fecha_revision` | `DATETIME2` | Sí | — |
| `observaciones_revision` | `VARCHAR(500)` | Sí | — |
| `reabierta` | `BIT` | No | `DEFAULT 0` (reservado, sin uso hasta 70.4.2) |
| `motivo_reapertura` | `VARCHAR(500)` | Sí | (reservado) |
| `created_at` | `DATETIME2` | No | `DEFAULT SYSUTCDATETIME()` |

Índices: `UNIQUE (id_usuario_cajero, fecha_operativa)`; `INDEX (id_comercio,
fecha_operativa)`; `INDEX (id_establecimiento, estado)`; `INDEX (estado)`.

**`wallet_caja_movimientos`**

| Columna | Tipo | Nulo | Restricción |
|---|---|---|---|
| `id_movimiento` | `BIGINT IDENTITY` | No | PK |
| `id_caja` | `BIGINT` | No | FK → `wallet_cajas_comercio(id_caja)` |
| `tipo_movimiento` | `VARCHAR(30)` | No | CHECK enum (sección 3) |
| `naturaleza` | `CHAR(1)` | No | CHECK `IN ('E','S')` |
| `valor` | `DECIMAL(18,2)` | No | CHECK `>= 0` |
| `referencia_tipo` | `VARCHAR(40)` | Sí | — |
| `referencia_id` | `BIGINT` | Sí | FK → `wallet_recargas_comercio(id_recarga)` cuando `referencia_tipo='wallet_recargas_comercio'` |
| `observaciones` | `VARCHAR(500)` | Sí | — |
| `created_at` | `DATETIME2` | No | `DEFAULT SYSUTCDATETIME()` |

Índices: `INDEX (id_caja)`; `UNIQUE (referencia_tipo, referencia_id) WHERE
referencia_id IS NOT NULL` (filtered unique index).

**`caja_notificaciones_internas`** — ver sección 3 para el detalle completo de
columnas (ya enumeradas ahí). Índices: `INDEX (id_usuario_destino, leida)`.

**`comercio_establecimientos`** (columna aditiva): `hora_cierre_automatico_caja
TIME NULL`.

### Manejo de concurrencia

- Apertura de caja: `UNIQUE (id_usuario_cajero, fecha_operativa)` como
  protección de última instancia contra doble apertura simultánea (mismo
  patrón que `CierreDuplicadoException` de 70.3 — violación de unicidad
  mapeada a `409 Conflict`, no a un `500` genérico).
- Transiciones de estado (`iniciar-cuadre`, `cerrar`, `revisar`, cierre
  automático): lock `WITH (UPDLOCK, ROWLOCK)` sobre la fila de la caja dentro
  de una transacción explícita, exactamente igual que
  `WalletCierreDiarioComercioService.RevisarAsync`/`CerrarAsync` en 70.3 —
  valida el `estado` de origen exacto bajo el lock antes de escribir el nuevo.
- Vínculo recarga↔caja: se inserta en la **misma transacción** que la recarga
  (`WalletRecargaComercioService.RecargarWalletAsync`), protegido además por el
  índice único filtrado sobre `referencia_id` — si dos solicitudes intentaran
  vincular la misma recarga (no debería poder pasar por diseño, pero la
  restricción de base de datos es la garantía final), la segunda falla por
  violación de unicidad, no por condición de carrera silenciosa.
- Cierre automático (Capas 1 y 3, sección 9): el `UPDATE` que transiciona a
  `CERRADA_AUTOMATICAMENTE` también toma `WITH (UPDLOCK, ROWLOCK)` y valida
  `estado='ABIERTA'` en el `WHERE` — si dos ejecuciones concurrentes (por
  ejemplo, el scheduler y una lectura con auto-sanación simultánea) intentan
  cerrar la misma caja, la segunda simplemente no encuentra filas que
  actualizar (no falla, no duplica).

### Snapshot — qué se congela y cuándo

| Momento | Qué se congela | No se recalcula después |
|---|---|---|
| `iniciar-cuadre` (`ABIERTA→EN_CUADRE`) | `efectivo_esperado` | ✅ |
| `cerrar` (`EN_CUADRE→CERRADA/CON_DIFERENCIA`) | `efectivo_contado`, `diferencia`, `fecha_cierre_utc` | ✅ |
| Cierre automático (`ABIERTA→CERRADA_AUTOMATICAMENTE`) | `efectivo_esperado` (con lo acumulado hasta ese instante), `fecha_cierre_utc` | ✅ (`efectivo_contado`/`diferencia` quedan `NULL` permanentemente — no hubo conteo humano) |
| `revisar` (`CON_DIFERENCIA`/`CERRADA_AUTOMATICAMENTE → REVISADA`) | `revisado_por_usuario`, `fecha_revision`, `observaciones_revision` | El resto de campos de la caja no cambia — revisar es solo metadata de auditoría, nunca recalcula el cuadre. |

### Relación caja–cajero–sede–comercio

```
comercios (1) ──< comercio_establecimientos (N, "sedes")
comercio_establecimientos (1) ──< wallet_cajas_comercio (N)
usuarios (1) ──< wallet_cajas_comercio (N, vía id_usuario_cajero)
```

Una caja pertenece a exactamente una sede y a exactamente un usuario operador;
una sede puede tener muchas cajas (una por usuario por día); un comercio tiene
muchas sedes. `id_comercio` y `id_comercio_aliado` en la caja son
desnormalizados desde la sede (mismo criterio ya usado en
`wallet_recargas_comercio`) para no requerir un `JOIN` extra en cada consulta.

### Relación con recargas y ventas QR

- **Recargas:** `wallet_caja_movimientos.referencia_id → wallet_recargas_comercio.id_recarga`
  (1 a 1 cuando `tipo_movimiento='RECARGA_EFECTIVO'`) — vínculo creado en la
  misma transacción de la recarga, nunca retroactivo.
- **Ventas QR:** **no hay vínculo persistido** — se consultan en vivo, filtradas
  por `ComercioVentaQrContexto.IdCajeroUsuario` + `IdEstablecimiento` + rango de
  fecha de la caja, solo para el bloque informativo del cuadre (sección 8B). No
  se guarda una fila por venta QR en `wallet_caja_movimientos` porque no son
  efectivo — mezclarlas ahí rompería la fórmula de `efectivo_esperado`.

### Reglas para impedir que una misma operación se cuente dos veces

1. **Una recarga, una sola caja:** `UNIQUE (referencia_tipo, referencia_id)
   WHERE referencia_id IS NOT NULL` en `wallet_caja_movimientos` — igual
   principio que `uq_wcdcd_id_recarga` de 70.3, aplicado a nivel de caja.
2. **Una recarga, un solo cierre diario:** ya garantizado por 70.3
   (`uq_wcdcd_id_recarga` en `wallet_cierres_diarios_comercio_detalle`) — no se
   modifica. Una recarga puede aparecer en **una** caja y en **un** cierre
   diario simultáneamente porque son tablas de detalle distintas con sus
   propias unicidades — no hay una tercera tabla que las combine ni riesgo de
   que una tabla lea de la otra.
3. **Una caja, un cajero, un día:** `UNIQUE (id_usuario_cajero,
   fecha_operativa)` en `wallet_cajas_comercio` — impide abrir dos cajas
   concurrentes para procesar el mismo efectivo dos veces.
4. **Un cierre automático no se aplica dos veces:** el `UPDATE` de la Capa 1/3
   siempre valida `estado='ABIERTA'` en su `WHERE` bajo lock — una caja ya
   `CERRADA_AUTOMATICAMENTE` no vuelve a procesarse aunque el scheduler la
   evalúe de nuevo en la siguiente corrida (ya no aparece en el filtro
   `estado='ABIERTA'`).

---

## Decisiones abiertas para tu aprobación antes de implementar

1. Confirmar la alternativa de scheduler para la Capa 1 del mecanismo híbrido
   (sección 9) — recomendada: Azure Function con Timer Trigger.
2. Valor por defecto de hora de cierre automático cuando la sede no configura
   una (propuesto 21:00 Colombia).
3. Confirmar que "una caja por cajero por día" (sin multi-turno el mismo día)
   es aceptable para 70.4.1.
4. Confirmar el alcance de subfases propuesto (sección 18) o ajustarlo.
5. Confirmar la especificación técnica exacta de la sección 19 (tablas,
   columnas, concurrencia, snapshot, relaciones, reglas anti-duplicado) antes
   de escribir la migración `028`.

**No se ha escrito ni desplegado ningún código de esta fase.** QA permanece sin
cambios respecto al estado ya validado de la Fase 70.3.
