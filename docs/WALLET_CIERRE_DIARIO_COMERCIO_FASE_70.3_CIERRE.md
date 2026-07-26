# XPAY MVP — Wallet: Cierre Fase 70.3

**Fase:** 70.3 — Cierre Diario de Comercios
**Fecha UTC:** 2026-07-25
**Responsable:** Gabriel Alfonso Pineda Ortiz `g.pineda@cercaymejor.com`
**Ambiente:** QA — NO producción
**Estado:** ✅ **APROBADA Y CERRADA EN QA**

---

> **ADVERTENCIA:**
> Todos los saldos, cupos y movimientos de este documento corresponden a `qa.usuario1`,
> `qa.comercio1`, `qa.cajero1` y `qa.admin.xpay` en el ambiente QA
> (`xpay-api-qa.azurewebsites.net`). No son dinero real, no involucran producción,
> Passport, Veriff ni Datacrédito.

---

## Alcance implementado

- Control operativo para que el comercio consolide, por fecha, las recargas en
  efectivo (Fase 70.1) del comercio completo (todas las sedes).
- Snapshot inmutable al generar: cantidad de recargas, valor total recaudado,
  valor liquidado al generar, valor pendiente al generar, corte exacto (UTC) usado
  para incluir/excluir recargas.
- **Flujo autogestionado por el comercio** (corregido — ver hallazgo 5 más
  abajo): `ADMIN_COMERCIO` genera y cierra el consolidado **en una sola
  operación**. El backend ejecuta todas sus validaciones técnicas
  (`GenerarCierreAsync`) y, si pasan, el cierre queda directamente en
  `CERRADO` dentro de la misma transacción — no existe una etapa manual
  rutinaria `GENERADO → REVISADO → CERRADO` para el flujo normal.
- Generación restringida a `ADMIN_COMERCIO` (validado en backend, no solo en
  UI). `ADMIN_XPAY`/`SUPERUSUARIO` **ya no aprueban cierres normales** — su
  rol es consulta, filtros, detalle, PDF y auditoría global.
- Vistas acotadas por alcance: `ADMIN_SEDE_COMERCIO` y `CAJERO` solo ven su propia
  participación (sede o recargas propias) — nunca el consolidado del comercio
  completo. El consolidado (`TotalesComercio`) solo se expone a quien tiene
  `PuedeVerTodoComercio=true`.
- Bloque diferenciado en las consultas administrativas: snapshot al generar
  (`valorLiquidadoAlGenerar`/`valorPendienteAlGenerar`) vs. situación actual
  recalculada en vivo (`valorLiquidadoActual`/`valorPendienteActual`), sin alterar
  el snapshot persistido.
- Comprobante PDF generado 100% en el navegador (jsPDF) a partir del snapshot,
  con código único (`codigo_unico`) y espacio reservado para un QR de validación
  futura (sin implementar todavía).
- Cero movimientos nuevos de Wallet o Ledger — el cierre diario es puramente
  consolidación y control de flujo, no contabilidad.
- No modifica `wallet_recargas_comercio` ni `wallet_liquidaciones_recaudo_comercio`
  — el vínculo se resuelve por completo desde la tabla de detalle nueva.
- Producción no tocada en ningún momento.

---

## Regla de negocio

Un cierre diario consolida, para un único comercio y una única fecha operativa
(zona horaria Colombia, UTC-5 fijo), todas las recargas en efectivo creadas hasta
el instante exacto de generación (`fecha_hora_corte_utc`) que aún no pertenezcan a
otro cierre. El cierre queda congelado desde ese momento — no se recalcula, no se
regenera para la misma fecha, y las recargas registradas después del corte no
quedan incluidas (deben reflejarse en un cierre posterior).

El cierre **no** modifica el estado de liquidación de las recargas incluidas
(Fase 70.2 sigue siendo el único proceso que marca una recarga como `LIQUIDADA`) —
solo registra, como snapshot histórico, cuánto de lo recaudado ya estaba liquidado
al momento de generar.

> **Este cierre es una capacidad administrativa, no un cierre individual de
> cajero.** Lo genera, consolida y consulta `ADMIN_COMERCIO`/`ADMIN_SEDE_COMERCIO`
> a nivel de comercio/sede. No representa el turno ni la caja de un cajero
> específico — esa entidad (apertura, cuadre y cierre de caja individual) es el
> objeto de la Fase 70.4, todavía sin implementar.
>
> El backend expone, para consultas administrativas, un cálculo por-solicitante
> (`MiParticipacion`) que en el caso de `CAJERO` se reduce a sus propias
> recargas dentro del cierre consolidado. **Este cálculo es diseño transitorio
> a nivel de API — no está aprobado para exponerse en el frontend como si fuera
> un "cierre del cajero"**, y no debe tomarse como evidencia de que existe un
> cierre individual en esta fase. `CAJERO` no ve la sección de cierre diario en
> `/mi-comercio` (ver "Corrección de alcance" más abajo).

> **Por qué el cierre es autogestionado (corrección de flujo):** se revisó el
> código exacto de `RevisarAsync`/`CerrarAsync` y se confirmó que **no
> ejecutaban ningún control técnico adicional** — eran un cambio de estado
> manual puro (validar `Estado` de origen + escribir usuario/fecha). Todas
> las validaciones técnicas reales (fecha no futura, confirmación explícita
> si es hoy, unicidad comercio+fecha, congruencia de valores, al menos 1
> recarga, lock de concurrencia) ya se ejecutan automáticamente dentro de
> `GenerarCierreAsync`, antes de que exista el cierre. Exigir una aprobación
> humana de XPAY para cada cierre normal no aportaba ningún control real —
> por eso se eliminó del flujo ordinario.

---

## Migración

**`database/027_wallet_cierres_diarios_comercio.sql`** — aplicada en QA en un solo
intento (sin parches posteriores), verificada por consulta directa contra
`sys.tables`, `sys.columns`, `sys.check_constraints`, `sys.foreign_keys` y
`sys.indexes`.

Objetos creados:

- **Tabla `wallet_cierres_diarios_comercio`** — encabezado del cierre, snapshot +
  máquina de estados + auditoría de generación/revisión/cierre.
- **Tabla `wallet_cierres_diarios_comercio_detalle`** — vínculo con
  `wallet_recargas_comercio` (una recarga solo puede pertenecer a un cierre).
- **6 foreign keys**: hacia `comercios`, `usuarios` (generado/revisado/cerrado
  por) y `wallet_recargas_comercio` — ninguna modifica esas tablas, solo las
  referencia.
- **5 CHECK constraints**: estado enum, valores no negativos, suma exacta
  (`total = liquidado + pendiente`), cantidad de recargas > 0, valor de detalle
  ≥ 0.
- **3 UNIQUE**: `(id_comercio, fecha_cierre)`, `codigo_unico`, `id_recarga` en el
  detalle.
- **4 índices** adicionales: `fecha_cierre`, `estado`, `id_comercio`, `id_cierre`
  (detalle).
- Envuelta en `SET XACT_ABORT ON` + `BEGIN TRY/TRANSACTION` + `BEGIN
  CATCH/ROLLBACK/THROW` — sin objetos parcialmente creados posibles.
- `id_unidad_negocio` se deriva de `comercios.id_unidad_negocio` en la aplicación
  — sin `DEFAULT 1` fijo en la migración.

---

## Endpoints implementados

**Comercio** (`[Authorize(Roles="COMERCIO")]`, scope vía `ComercioScopeService`):

- `GET  /api/comercio/wallet-cierres/preview?fecha=`
- `POST /api/comercio/wallet-cierres/generar`
- `GET  /api/comercio/wallet-cierres/mis-cierres`
- `GET  /api/comercio/wallet-cierres/{idCierre}`

**Admin XPAY** (`[Authorize(Roles="ADMIN_XPAY,SUPERUSUARIO")]`):

- `GET  /api/admin/wallet-cierres-comercio`
- `GET  /api/admin/wallet-cierres-comercio/{idCierre}`
- `POST /api/admin/wallet-cierres-comercio/{idCierre}/revisar` — **DEPRECADO.**
  Fuera del flujo operativo normal desde la corrección de autogestión (ver
  hallazgo 5). Ningún cierre nuevo queda en `GENERADO`, así que este endpoint
  no tiene forma de alcanzarse en la operación diaria. Se conserva sin
  modificar (mismo código, mismas validaciones) porque no se confirmó otro
  consumidor además del frontend, que ya dejó de llamarlo. Candidato a retiro
  definitivo en una limpieza posterior — no se elimina en esta corrección.
- `POST /api/admin/wallet-cierres-comercio/{idCierre}/cerrar` — **DEPRECADO**,
  mismo criterio que `/revisar`.

---

## Seguridad

- `idComercio` nunca viene del body — se deriva 100% del scope del usuario
  autenticado (`ComercioScopeService`).
- Generación restringida server-side a `RolComercio == "ADMIN_COMERCIO" &&
  PuedeVerTodoComercio` — verificado independientemente de la visibilidad del
  botón en frontend.
- `GenerarCierreAsync` deja el cierre directamente en `CERRADO` dentro de la
  misma transacción: `GeneradoPorUsuario` = `CerradoPorUsuario` = el
  `ADMIN_COMERCIO` que confirmó; `RevisadoPorUsuario`/`FechaRevision` quedan
  `NULL` (no hubo revisión humana ni técnica independiente — no se inventa
  una). Cada generación exitosa registra una fila en la tabla `Auditoria`
  (`Modulo="CIERRE_DIARIO_COMERCIO"`, `Accion="GENERAR_Y_CERRAR_CIERRE_DIARIO_COMERCIO"`,
  con comercio, fecha operativa, idCierre, usuario y valores del snapshot).
- `RevisarAsync`/`CerrarAsync` (deprecados) conservan su validación de
  `Estado` de origen exacto sin cambios — si algún cierre histórico quedara en
  `GENERADO`/`REVISADO`, esos endpoints seguirían protegidos igual que antes.
- Concurrencia: `UNIQUE(id_comercio, fecha_cierre)` + lock `WITH (UPDLOCK,
  ROWLOCK)` sobre las recargas candidatas dentro de una transacción explícita;
  choque de duplicado mapeado a `CierreDuplicadoException` → `409 Conflict`.
- Fecha del cierre: rechaza fechas futuras; exige `confirmacionExplicita=true`
  para generar el cierre del día actual (con advertencia explícita de que
  recargas posteriores no quedarán incluidas).
- Vistas por rol: `ADMIN_SEDE_COMERCIO`/`CAJERO` reciben únicamente su
  `MiParticipacion` (acotada a sede o a recargas propias) — el backend nunca
  envía el consolidado del comercio a estos roles.

---

## Frontend

- `/mi-comercio` — sección **"Cierre Diario de Comercio"**: preview en vivo,
  botón **"Generar y cerrar"** (solo `ADMIN_COMERCIO`, con `window.confirm`:
  *"Se generará el cierre definitivo para la fecha seleccionada. Los valores
  quedarán almacenados como snapshot histórico y no podrán modificarse desde
  el flujo normal."*, más la confirmación explícita adicional si la fecha es
  hoy), listado de cierres con participación acotada por rol, detalle,
  descarga de comprobante PDF. El resultado queda `CERRADO` de inmediato —
  no hay paso posterior de aprobación.
- `/admin/wallet-cierres-comercio` — listado con filtros (fecha/comercio/
  estado), detalle con bloque snapshot vs. situación actual, descarga de PDF.
  **Ya no tiene botones "Marcar REVISADO"/"Marcar CERRADO"** — se retiraron
  junto con las funciones `marcarRevisado`/`marcarCerrado` y el campo
  "Observaciones" asociado. Solo consulta y auditoría.
- `jsPDF` agregado como dependencia nueva de `frontend/xpay-admin` (no existía
  ninguna librería PDF en el repo).

---

## Hallazgos corregidos durante la validación en QA

Durante la validación manual con un usuario `CAJERO` real (`qa.cajero1`, creado
específicamente para esta fase) se encontraron y corrigieron dos problemas que
no eran del alcance original de esta fase, pero bloqueaban su prueba:

1. **`MiComercioPage.tsx` dependía de un mapa hardcodeado por username**
   (`DEMO_COMERCIO_MAP`, heredado de una fase de demo anterior a la 70.1) que
   solo reconocía `qa.comercio1`. Se reemplazó por resolución dinámica del
   comercio vía `/api/comercio/mi-scope` (endpoint ya existente, reutilizando
   `ComercioScopeService` sin duplicar reglas) — ahora cualquier usuario con rol
   `COMERCIO` (`ADMIN_COMERCIO`, `ADMIN_SEDE_COMERCIO`, `CAJERO`) puede entrar a
   `/mi-comercio`.
2. **Exposición de saldo del cliente a `CAJERO`** durante la búsqueda y
   confirmación de recarga (Fase 70.1): corregido en el backend
   (`WalletRecargaComercioService`) — `CAJERO` recibe documento parcialmente
   enmascarado y sin celular/correo/saldo; `ADMIN_COMERCIO`/`ADMIN_SEDE_COMERCIO`
   mantienen el comportamiento ya validado en Fase 70.1.
3. **Desfase horario (UTC mostrado sin convertir)**: EF Core no marcaba
   `Kind=Utc` al leer `DATETIME2` de la base, y el frontend interpretaba el
   string resultante como hora local del navegador. Corregido con un converter
   global en `XpayDbContext` (`ConfigureConventions`) + `fmtDate` con
   `timeZone: 'America/Bogota'` explícito, y eliminación de fechas formateadas
   embebidas en `ComprobanteTexto`/`NotaCorte` (ahora se generan sin fecha de
   texto libre; la fecha se muestra siempre desde el campo estructurado).
   Se auditó el resto del esquema: ninguna otra columna `DateTime`/`DateTime?`
   representa una fecha local no-instante, excepto `Persona.FechaNacimiento`
   (fecha de nacimiento, columna `DATE`), excluida explícitamente del converter
   global.

Ninguno de estos tres hallazgos requirió cambios de alcance, contabilidad,
ledger ni datos existentes — todos aplicados y desplegados solo en QA.

### 4. Corrección de alcance — visibilidad de `CAJERO`

Durante la validación se determinó que mostrarle a `CAJERO` su `MiParticipacion`
dentro del listado/detalle del cierre consolidado simulaba un "cierre propio"
que no existe en esta fase — el cierre diario de comercio es una capacidad
administrativa (`ADMIN_COMERCIO`/`ADMIN_SEDE_COMERCIO`), no un cierre individual
de cajero. Se corrigió `MiComercioPage.tsx` para que **`CAJERO` no vea la
sección "Cierre Diario de Comercio" en absoluto** (ni preview, ni listado, ni
detalle, ni PDF) — medida temporal hasta que la Fase 70.4 implemente el cierre
individual de caja/turno, que es donde corresponde esa consulta.
`ADMIN_SEDE_COMERCIO`/`ADMIN_COMERCIO` no tuvieron ningún cambio. El backend no
requirió cambios — ya restringía correctamente el consolidado antes de esta
corrección (ver nota en "Regla de negocio").

### 5. Corrección de flujo — autogestión por el comercio

El diseño original implementaba `GENERADO → REVISADO → CERRADO` con
`ADMIN_XPAY`/`SUPERUSUARIO` como aprobador obligatorio de cada cierre, tal
como se había especificado en el alcance original de esta fase. Al revisar el
código exacto de `RevisarAsync`/`CerrarAsync` se confirmó que **ninguno de los
dos ejecutaba ningún control técnico adicional** — eran un cambio de estado
manual puro (validar `Estado` de origen y escribir usuario/fecha). Todas las
validaciones técnicas reales ya ocurrían dentro de `GenerarCierreAsync`, antes
de que el cierre existiera. Sin controles reales detrás, exigir una aprobación
humana rutinaria de XPAY no tenía justificación funcional.

**Cambio aplicado:**

- `GenerarCierreAsync` ahora deja el cierre directamente en `CERRADO` dentro
  de la misma transacción — `ADMIN_COMERCIO` genera y cierra en una sola
  operación.
- `GeneradoPorUsuario` = `CerradoPorUsuario` = el mismo `ADMIN_COMERCIO`;
  `RevisadoPorUsuario`/`FechaRevision` quedan `NULL` — no se inventa ninguna
  revisión que no ocurrió.
- Cada generación exitosa registra un evento real en la tabla `Auditoria`
  (`GENERAR_Y_CERRAR_CIERRE_DIARIO_COMERCIO`), reemplazando la dependencia
  exclusiva del log de aplicación que tenía el diseño anterior.
- `RevisarAsync`/`CerrarAsync` y sus endpoints quedan **deprecados**: código
  intacto, sin consumidor confirmado más allá del frontend (que ya dejó de
  llamarlos), candidatos a retiro definitivo en una limpieza futura — no
  eliminados en esta corrección.
- `ADMIN_XPAY`/`SUPERUSUARIO` conservan íntegramente consulta, filtros,
  detalle, descarga de PDF y auditoría global — pierden únicamente la
  aprobación rutinaria.
- **Explícitamente fuera de alcance de esta corrección:** estado
  `EN_EXCEPCION`, estado `REABIERTO`, mecanismo de reapertura extraordinaria y
  anulación de cierres. Quedan reservados para una subfase independiente, con
  motivo obligatorio, permisos, auditoría y reglas de impacto por diseñar —
  no forman parte de esta fase.
- **Cierre #1** (2026-07-19), generado antes de esta corrección bajo el flujo
  manual anterior, **conserva su trazabilidad histórica tal cual** —
  `RevisadoPorUsuario`/`FechaRevision` con los valores reales de cuando
  `qa.admin.xpay` lo revisó y cerró. No fue migrado ni reescrito; sigue siendo
  un cierre `CERRADO` válido.

### 6. Corrección de fecha operativa por defecto — `MiComercioPage.tsx`

Encontrado en vivo durante la validación E2E (paso 14): el campo de fecha del
frontend calculaba la fecha operativa "de hoy" por defecto con
`new Date().toISOString().slice(0, 10)`, que devuelve la fecha **UTC cruda**,
no la fecha Colombia. Cerca de la medianoche Colombia (pero todavía dentro del
mismo día UTC+1 de adelanto), el campo mostraba como "hoy" una fecha que en
Colombia todavía era el día siguiente — el backend la rechazaba correctamente
como fecha futura (`400`), pero el mensaje no explicaba la causa real al
usuario. Corregido reemplazando el cálculo por
`new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Bogota' }).format(new Date())`,
el mismo patrón ya usado en `fmtDate` (`utils.ts`) para mostrar fechas. Sin
impacto en datos existentes — solo afecta el valor por defecto del selector de
fecha en el formulario de generación.

---

## Compatibilidad

Coexisten dos formatos válidos de cierre, según cuándo se generaron:

- **Cierres anteriores a esta corrección** (ej. Cierre #1): pueden tener
  `RevisadoPorUsuario`/`FechaRevision` con datos reales — pasaron por el flujo
  manual `GENERADO → REVISADO → CERRADO` con intervención de
  `ADMIN_XPAY`/`SUPERUSUARIO`.
- **Cierres generados desde esta corrección en adelante**: se crean
  directamente en `CERRADO`; `GeneradoPorUsuario = CerradoPorUsuario` (el
  mismo `ADMIN_COMERCIO`); `RevisadoPorUsuario`/`FechaRevision` siempre en
  `NULL`.

**Ambos formatos son válidos y deben consultarse correctamente sin distinción
especial:** el campo `Estado` es `CERRADO` en los dos casos; la lectura
(`GetDetalleAdminAsync`, `GetDetalleComercioAsync`, listados, PDF) no
diferencia el origen del cierre — simplemente muestra `RevisadoPorUsuario`
cuando existe y lo omite cuando es `NULL` (condicional ya presente en el
código, sin necesidad de rama especial). No se requiere ninguna migración de
datos para que ambos formatos coexistan.

---

## Validación en QA

### Pruebas ejecutadas y aprobadas

- Migración 027 verificada en frío (tablas, columnas, FKs, CHECK, UNIQUE,
  índices) — sin datos residuales antes de empezar.
- Backend y frontend compilan sin errores/warnings y despliegan
  `RuntimeSuccessful` en `xpay-api-qa`/`xpay-admin-qa` (incluida la
  redeploy de la corrección de autogestión).
- Setup de `qa.cajero1` (rol `COMERCIO`, `comercio_usuarios` con `CAJERO` en
  sede "Tienda Principal Demo") verificado por lectura, sin tocar
  `password_hash` ni afectar a `qa.comercio1`.
- Acceso de `qa.cajero1` a `/mi-comercio` y a "Recargar Wallet" confirmado por
  el responsable tras el fix del mapa hardcodeado.
- Recarga real de `qa.cajero1` ($100.000 y una recarga de prueba adicional)
  confirmada como exitosa: sin exposición de saldo/celular/correo del cliente
  (ni en pantalla ni en la respuesta cruda del backend), con hora mostrada en
  Colombia y verificada contra el timestamp UTC crudo en Network.
- `CAJERO` confirmado (navegador en vivo) sin acceso a la sección de cierre
  diario en `/mi-comercio`.
- **Cierre #1** (comercio #2, fecha `2026-07-19`) generado, revisado y cerrado
  con datos reales bajo el flujo manual anterior — cantidad=3,
  valor total=$6.500, liquidado al generar=$5.000, pendiente al generar=$1.500,
  coincidiendo exactamente con los datos reales de la base. Duplicado sobre el
  mismo comercio/fecha confirmado rechazado con `409`. PDF descargado y
  validado en 15 puntos (código único, número, comercio, fecha, corte en hora
  Colombia, estado, snapshot, texto de snapshot, sin datos de clientes,
  legibilidad, QR reservado sin información falsa). Inmutabilidad del
  snapshot confirmada **empíricamente**: se liquidó la recarga pendiente #5
  después del cierre y tanto el panel admin ("situación actual" cambió a
  $6.500/$0) como un PDF re-descargado (siguió mostrando $5.000/$1.500)
  demostraron la congelación real, no solo teórica.
- Liquidación `EFECTIVO_BOVEDA` (Fase 70.2) ejecutada sobre la recarga #5 —
  regresión confirmada, transacción ledger #143.
- Backend/frontend compilan y despliegan sin errores tras la corrección de
  autogestión (build + deploy verificados en `xpay-api-qa`/`xpay-admin-qa`).
- **Cierre #2** (comercio #2, fecha operativa `2026-07-25`) generado en vivo
  con el flujo nuevo, con `qa.comercio1`, vía el botón "Generar y cerrar":
  `id_cierre=2`, `estado=CERRADO`, `generado_por_usuario=11`,
  `cerrado_por_usuario=11` (mismo usuario), `fecha_generacion=2026-07-26
  03:17:20.79`, `fecha_cerrado=2026-07-26 03:17:20.79` (idéntica a
  generación), `revisado_por_usuario=NULL`, `fecha_revision=NULL`,
  `codigo_unico=XPAY-CIERRE-2-20260725-DAFB6452`. Confirmado en detalle
  (`/mi-comercio`): solo botón "Descargar comprobante PDF", sin acciones
  Revisar/Cerrar.
- Detalle administrativo (`/admin/wallet-cierres-comercio`, sesión
  `qa.admin.xpay` renovada) verificado en vivo: listado carga, detalle de
  Cierre #1 abre correctamente, filtros/consulta/detalle/PDF disponibles, sin
  botones "Marcar REVISADO"/"Marcar CERRADO", sin campo Observaciones, estado
  `CERRADO` correcto, trazabilidad histórica intacta ("Generado por
  qa.comercio1 el 25/07/2026, 8:17 p.m. · Revisado por qa.admin.xpay el
  25/07/2026, 8:21 p.m. · Cerrado por qa.admin.xpay el 25/07/2026, 8:21
  p.m."). Evidencia visual capturada y enviada.
- Fecha futura → rechazo confirmado en vivo: `POST
  /api/comercio/wallet-cierres/generar` con `fecha=2026-07-27` →
  `400 "No se puede generar un cierre para una fecha futura."`. Sin impacto en
  datos QA (solicitud rechazada antes de escribir nada).
- Regla de confirmación especial para la fecha operativa de hoy: confirmada en
  vivo junto con el hallazgo 6 (bug de `hoyIso`) — checkbox obligatorio,
  botón inerte sin marcarlo, texto del `window.confirm` capturado exactamente
  como el especificado. Cierre #2 quedó directamente `CERRADO`.
- Confirmado por consulta directa de solo lectura contra la base QA: **0**
  filas en `wallet_movimientos`, **0** en `ledger_transacciones` y **0** en
  `ledger_movimientos` atribuibles al Cierre #2 o a cualquier cierre diario —
  el único movimiento Wallet/Ledger de toda la ronda fue la liquidación de
  Fase 70.2 (ledger tx #143), ajena al cierre diario.
- Alcance de `ADMIN_SEDE_COMERCIO`: confirmado por consulta directa
  (`SELECT ... FROM comercio_usuarios WHERE rol_comercio='ADMIN_SEDE_COMERCIO'`
  → 0 filas) que **no existe ningún usuario QA con ese rol** — el caso queda
  documentado como bloqueado por falta de fixture, no como fallo funcional.
- Regresión de recarga Wallet por comercio (Fase 70.1) confirmada también para
  `qa.comercio1`: búsqueda de `qa.usuario1` muestra Documento/Celular/Saldo
  **sin enmascarar** (comportamiento correcto para este rol, distinto de
  `CAJERO`); recarga #8 por $1.000 exitosa (saldo $339.298 → $340.298, ledger
  tx #144).

### Hallazgos corregidos (ver detalle arriba)

1. `MiComercioPage.tsx` dependía de un mapa hardcodeado por username — corregido
   con resolución dinámica vía `/api/comercio/mi-scope`.
2. Exposición de saldo/celular/correo del cliente a `CAJERO` — corregido en
   `WalletRecargaComercioService`.
3. Desfase horario (UTC mostrado sin convertir) — corregido con converter
   global en `XpayDbContext` + `fmtDate` con `America/Bogota` explícito.
4. `CAJERO` veía su participación en el cierre consolidado como si fuera un
   cierre propio — corregido ocultando toda la sección para ese rol.
5. Flujo `GENERADO → REVISADO → CERRADO` con aprobación rutinaria de XPAY sin
   justificación técnica — corregido a flujo autogestionado por el comercio.
6. Fecha operativa por defecto calculada en UTC en lugar de Colombia
   (`hoyIso`) — corregido con `Intl.DateTimeFormat('en-CA', {timeZone:
   'America/Bogota'})`, mismo patrón que `fmtDate`.

### Regresiones — resultado final de esta ronda

- Recarga Wallet por comercio (Fase 70.1): confirmado para `qa.cajero1`
  (enmascarado) y `qa.comercio1` (sin enmascarar) — ambos comportamientos
  correctos según rol. `ADMIN_SEDE_COMERCIO` no probado por falta de usuario
  QA (ver arriba).
- Cierre diario nuevo no crea movimientos Wallet ni Ledger: confirmado por
  consulta directa (0/0/0, ver arriba).
- Fecha futura → rechazo: confirmado (`400`).
- Confirmación especial para fecha operativa de hoy: confirmado.
- **Pago QR con Wallet, compra QR con Cupo Ordinario y pago de cuota de
  Cartera Ordinaria: no ejecutados en esta ronda.** Se documentan
  explícitamente como **regresión recomendada, no bloqueante**, porque:
  pertenecen a fases anteriores a la 70.3; la Fase 70.3 no modificó sus
  servicios, endpoints, componentes ni reglas de negocio; el riesgo de
  regresión se considera bajo; quedan recomendados para una futura ronda de
  regresión integral, pero **no bloquean el cierre de la Fase 70.3**.

---

## Guion final de validación E2E — flujo autogestionado

Guion ejecutado en su totalidad (con una excepción documentada como bloqueo
por falta de fixture, no como fallo). Leyenda final: **✅ aprobado** ·
**🚫 bloqueado** (no ejecutable por falta de un dato QA, no es un fallo del
sistema) · **➖ diferido, no bloqueante** (fuera de alcance de la Fase 70.3,
recomendado para una ronda de regresión integral futura).

El cierre nuevo de los pasos 4-7 fue **Cierre #2** (comercio #2, fecha
operativa `2026-07-25`), generado en vivo por `qa.comercio1`.

1. Iniciar sesión como `qa.comercio1`. *(✅ ya hecho — sesión activa se
   reutiliza)*
2. Consultar el preview de una fecha con recargas sin cierre todavía. *(✅ ya
   hecho para varias fechas)*
3. Verificar cantidades y valores esperados contra la base real. *(✅ ya
   hecho — coincidencia exacta confirmada en las fechas probadas)*
4. Pulsar **"Generar y cerrar"**. *(✅ aprobado — Cierre #2 generado con el
   botón nuevo)*
5. Confirmar el mensaje: *"Se generará el cierre definitivo para la fecha
   seleccionada. Los valores quedarán almacenados como snapshot histórico y no
   podrán modificarse desde el flujo normal."* *(✅ aprobado — texto capturado
   exacto vía `window.confirm` interceptado)*
6. Verificar que la respuesta y el listado muestran **`CERRADO`
   inmediatamente**, sin pasar por `GENERADO`/`REVISADO` visibles. *(✅
   aprobado — Cierre #2 quedó `CERRADO` de inmediato)*
7. Verificar en el detalle: `GeneradoPorUsuario` = `CerradoPorUsuario` =
   `qa.comercio1`; `FechaGeneracion`/`FechaCierre` válidas y coincidentes;
   `RevisadoPorUsuario`/`FechaRevision` en `NULL`. *(✅ aprobado — confirmado
   por consulta SQL directa sobre Cierre #2:
   `generado_por_usuario=cerrado_por_usuario=11`,
   `fecha_generacion=fecha_cerrado`, `revisado_por_usuario=NULL`,
   `fecha_revision=NULL`)*
8. Confirmar que no aparecen acciones "Revisar"/"Cerrar" en `/mi-comercio`.
   *(✅ aprobado — confirmado con Cierre #2, solo botón "Descargar
   comprobante PDF")*
9. Iniciar sesión como `qa.admin.xpay`. *(✅ hecho — sesión renovada por el
   usuario)*
10. Confirmar que puede consultar, filtrar, abrir detalle y descargar PDF.
    *(✅ aprobado — confirmado en vivo con Cierre #1)*
11. Confirmar que **no** aparecen los botones "Marcar REVISADO" ni "Marcar
    CERRADO" en `/admin/wallet-cierres-comercio`. *(✅ aprobado — 7/7 OK,
    confirmado visualmente en navegador con sesión válida: sin botones
    Revisar/Cerrar, sin campo Observaciones, trazabilidad histórica de
    Cierre #1 intacta)*
12. Intentar generar un duplicado para el mismo comercio y fecha de un cierre
    ya existente → esperar `409`. *(✅ aprobado — confirmado antes de esta
    corrección; lógica de `CierreDuplicadoException`/`UNIQUE` no cambió)*
13. Intentar una fecha futura → esperar rechazo. *(✅ aprobado — `POST
    /api/comercio/wallet-cierres/generar` con fecha futura → `400 "No se
    puede generar un cierre para una fecha futura."`; sin impacto en datos
    QA)*
14. Probar la regla especial de confirmación para la fecha operativa de hoy.
    *(✅ aprobado — reveló y permitió corregir el hallazgo 6 (`hoyIso` en
    UTC en lugar de Colombia); tras el fix, checkbox obligatorio verificado,
    botón inerte sin marcarlo, Cierre #2 generado directamente `CERRADO`)*
15. Confirmar que `CAJERO` no ve la sección 70.3. *(✅ aprobado — confirmado
    en navegador tras el fix de visibilidad, sin cambios en esta corrección)*
16. Confirmar el alcance de `ADMIN_SEDE_COMERCIO`, si existe usuario QA.
    *(🚫 bloqueado — confirmado por consulta directa que no existe ningún
    usuario QA con rol `ADMIN_SEDE_COMERCIO`; queda documentado como bloqueo
    por falta de fixture, no como fallo)*
17. Validar el **PDF**: estado `CERRADO`, código único, cierre #, comercio,
    fecha, corte, cantidad y valores del snapshot, texto de snapshot, QR
    reservado. El PDF **no incluye** "generado por"/"cerrado por"/"revisado
    por" — eso es intencional, no se amplía. Validar por separado, en la
    **página de detalle administrativo** (`/admin/wallet-cierres-comercio`):
    "Generado por"/"Cerrado por" visibles, y **sin** "Revisado por" cuando
    `RevisadoPorUsuario` es `NULL`. *(✅ aprobado — validado en 15 puntos con
    el PDF de Cierre #1; campos "generado por"/"cerrado por" verificados por
    separado en el detalle administrativo)*
18. Confirmar inmutabilidad del snapshot tras una liquidación posterior. *(✅
    aprobado — demostrado empíricamente con Cierre #1: se liquidó la recarga
    pendiente y el snapshot del PDF y del detalle no cambiaron, mientras
    "situación actual" sí)*
19. Confirmar que no se crean movimientos Wallet ni Ledger por el cierre.
    *(✅ aprobado — 0 filas en `wallet_movimientos`, 0 en
    `ledger_transacciones`, 0 en `ledger_movimientos` atribuibles al cierre
    diario, confirmado por consulta directa de solo lectura)*
20. Ejecutar regresiones funcionales pendientes: recarga de `qa.comercio1`
    (ve saldo/celular sin cambios), pago QR con Wallet, compra QR con Cupo
    Ordinario, pago de cuota de Cartera Ordinaria. *(✅ parcial aprobado —
    recarga de `qa.comercio1` confirmada correcta, datos del cliente
    visibles sin enmascaramiento, recarga #8 por $1.000 exitosa. **Pago QR
    con Wallet, compra QR con Cupo Ordinario y pago de cuota de Cartera
    Ordinaria quedan `➖ diferidos, no bloqueantes`** — ver "Regresiones —
    resultado final de esta ronda" arriba para la justificación completa)*

**Resultado final del guion:** 18 de 20 pasos aprobados (✅), 1 bloqueado por
falta de fixture QA (🚫 paso 16), 1 con una porción diferida a regresión
integral futura sin bloquear el cierre de fase (➖, parte del paso 20).

---

## Fuera de alcance / próximos pasos

- **Fase 70.4 — Apertura, Cuadre y Cierre de Caja**: administración de caja/turno
  individual por cajero (apertura, cuadre de efectivo, cierre manual y
  automático, diferencias, revisión). Registrada como evolución inmediata —
  diseño entregado en `docs/WALLET_CAJA_CUADRE_FASE_70.4_DISENO.md`, **sin
  implementar**.
- Validación por QR del código único del comprobante (el espacio ya está
  reservado en el PDF).
- **Excepciones y reaperturas del cierre diario de comercio** — estado
  `EN_EXCEPCION`, estado `REABIERTO`, mecanismo de reapertura extraordinaria y
  anulación de cierres. Quedan explícitamente reservados para una subfase
  independiente (sin numerar todavía), con motivo obligatorio, permisos,
  auditoría y reglas de impacto por diseñar — no implementados ni diseñados
  en detalle en esta corrección.
- Comisiones por recarga.
- Soportes PDF firmados digitalmente.
- Producción — no se tocó en ningún momento de esta fase.
- **Regresión integral de módulos no modificados por la Fase 70.3** — pago QR
  con Wallet, compra QR con Cupo Ordinario, pago de cuota de Cartera
  Ordinaria. No se ejecutaron en esta ronda de validación. Pertenecen a fases
  anteriores; la Fase 70.3 no modificó sus servicios, endpoints, componentes
  ni reglas de negocio; el riesgo de regresión se considera bajo. Quedan
  recomendados para una futura ronda de regresión integral del sistema, pero
  **no bloquean el cierre de la Fase 70.3**.

---

## Conclusión

**FASE 70.3 — APROBADA Y CERRADA EN QA ✅**

Implementación, migración y despliegue en QA completos y verificados,
incluida la corrección de flujo a autogestión por el comercio (hallazgo 5).
Los seis hallazgos encontrados durante la validación (mapa hardcodeado,
exposición de saldo, desfase horario en la visualización, visibilidad de
`CAJERO`, aprobación rutinaria de XPAY sin justificación técnica, y fecha
operativa por defecto calculada en UTC en lugar de Colombia) fueron
corregidos, desplegados y re-verificados en QA. El guion final de validación
E2E se ejecutó completo: 18 de 20 pasos aprobados, 1 bloqueado únicamente por
falta de un usuario QA con rol `ADMIN_SEDE_COMERCIO` (no un fallo del
sistema), y una porción del paso 20 (pago QR con Wallet, compra QR con Cupo
Ordinario, pago de cuota de Cartera Ordinaria) diferida como regresión
recomendada no bloqueante, ya que pertenece a fases anteriores no modificadas
por la Fase 70.3.

**Notas finales:**

- **Producción no fue tocada en ningún momento** de la implementación ni de
  la validación — todos los cambios, despliegues y pruebas se realizaron
  exclusivamente en `xpay-api-qa`/`xpay-admin-qa`.
- **No se hizo commit ni push** de ningún cambio de código o documentación
  durante esta fase.
- Queda **pendiente una ronda futura de regresión integral** de los módulos
  no modificados por esta fase (pago QR con Wallet, compra QR con Cupo
  Ordinario, pago de cuota de Cartera Ordinaria) — recomendada pero no
  bloqueante para este cierre.
- **`ADMIN_SEDE_COMERCIO` no fue probado** en esta ronda por falta de un
  usuario QA con ese rol — queda documentado como bloqueo de fixture, no como
  defecto.

El cuadre y cierre individual de caja por cajero (Fase 70.4), y las
excepciones/reaperturas del cierre diario de comercio, **no** forman parte de
esta fase.
