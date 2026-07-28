# XPAY — Corrección de Autorización e IDOR (Fase 71.2-E-B, ampliado en 71.2-E-C y 71.2-E-D)

**Fecha UTC:** 2026-07-27 (71.2-E-B) · ampliado 2026-07-27 (71.2-E-C) · ampliado 2026-07-27 (71.2-E-D)
**Ambiente:** Cambios de código backend y frontend, compilados/build verificados localmente. **No desplegado, no probado contra QA en vivo, sin commit/push.**
**Origen:** hallazgos de las etapas de diagnóstico 71.2-D y 71.2-E-A. Sección 12 documenta el cierre de seguridad Wallet y la compatibilidad mínima de frontend autorizados en 71.2-E-C. Sección 14 documenta la integridad transaccional (concurrencia), el IDOR de Pago QR y el logging real agregados en 71.2-E-D.

---

## 1. Vulnerabilidades encontradas

| # | Endpoint | Tipo | Descripción |
|---|---|---|---|
| V1 | `GET /api/wallets/persona/{idPersona}` | IDOR confirmado | Cualquier autenticado podía consultar la wallet de cualquier `idPersona` cambiando el parámetro de ruta |
| V2 | `GET /api/wallets/{idWallet}/saldo` | IDOR confirmado | Cualquier autenticado podía consultar el saldo real de cualquier wallet |
| V3 | `GET /api/wallets/{idWallet}/movimientos` | IDOR confirmado | Cualquier autenticado podía consultar hasta 100 movimientos reales de cualquier wallet |
| V4 | `GET /api/reportes/wallet/{idWallet}/estado-cuenta` | IDOR confirmado | Mismo patrón que V2/V3 — era el endpoint usado por `UserWalletPage.tsx` hasta 71.2-E-B; migrado a `mi-estado-cuenta` en 71.2-E-C (ver sección 12) |
| V16 | `POST /api/wallets/transferencia` | IDOR confirmado (escritura) | `IdWalletOrigen` venía directo del body del cliente, sin validar — permitía transferir **desde el saldo de cualquier wallet ajena**. Corregido en 71.2-E-C, ver sección 12.1 |
| V17 | `POST /api/wallets/{idWallet}/recarga-manual` | Autorización ausente (escritura financiera) | `[Authorize]` genérico permitía a cualquier autenticado acreditar saldo (sin respaldo de pago) a cualquier wallet. Corregido en 71.2-E-C, ver sección 12.2 |
| V18 | `POST /api/qr/pagar` | IDOR confirmado (escritura) | `IdWalletUsuario` venía directo del body del cliente, sin validar — permitía pagar un QR de comercio **descontando el saldo de cualquier wallet ajena**. Mismo patrón que V16. Corregido en 71.2-E-D, ver sección 14.4 |
| V5 | `GET /api/reportes/comercios/{idComercio}/resumen` | Autorización ausente | Cualquier autenticado podía ver el resumen financiero de cualquier comercio |
| V6 | `GET /api/reportes/ledger/transaccion/{idTransaccion}` | Autorización ausente | Cualquier autenticado podía ver el detalle de cualquier transacción contable |
| V7 | `GET /api/reportes/operaciones/resumen-general` | Autorización ausente | Métricas globales del sistema expuestas a cualquier autenticado (ya se auditaba como `"ADMIN_REPORT_ACCESS"` sin reforzarlo) |
| V8 | `GET /api/admin/wallets` | Autorización ausente | Listado completo de wallets del sistema, sin restricción de rol |
| V9 | `GET /api/admin/comercios` | Autorización ausente | Listado completo de comercios, sin restricción de rol |
| V10 | `GET /api/admin/ventas-qr` | Autorización ausente | Listado completo de ventas QR, sin restricción de rol |
| V11 | `GET /api/admin/ledger-transacciones` | Autorización ausente | Libro contable completo, sin restricción de rol |
| V12 | `GET /api/comercios/retiros`, `GET .../retiros/{id}` | IDOR confirmado + autorización ausente | Cualquier autenticado (incluido `USUARIO_FINAL`) podía listar/consultar retiros de cualquier comercio |
| V13 | `POST /api/comercios/solicitar-retiro` | IDOR confirmado (escritura) | `IdComercio` venía directo del body del cliente, sin validar — permitía solicitar un retiro **contra el saldo de otro comercio** |
| V14 | `POST /api/comercios/retiros/confirmar-pago`, `POST .../retiros/rechazar` | Autorización ausente (escritura financiera) | Cualquier autenticado podía marcar como pagado o rechazar cualquier retiro pendiente del sistema |
| V15 | `POST /api/comercios/liquidar-venta-qr` | Autorización ausente (escritura financiera) | Sin restricción de rol; sin consumidor frontend confirmado |

## 2. Endpoints afectados

15 endpoints en 4 controllers: `WalletsController` (3), `ReportesController` (4), `AdminController` (4), `ComerciosController` (6, incluye `retiros`/`retiros/{id}` como una fila).

## 3. Severidad

| Severidad | Hallazgos | Justificación |
|---|---|---|
| **Crítica** | V13 | Escritura financiera real: permite iniciar un retiro (débito de saldo) contra un comercio ajeno |
| **Alta** | V1-V4, V12, V14 | Exposición de datos financieros/personales de terceros (lectura) o aprobación/rechazo financiero de recursos ajenos (escritura) |
| **Media** | V5-V11, V15 | Exposición de datos agregados/administrativos a cualquier autenticado, sin ser IDOR de un recurso específico de un tercero identificable por id manipulado |

## 4. Causa raíz

Patrón repetido en todo el backend: controllers decorados con `[Authorize]` genérico (verifica solo autenticación) en vez de `[Authorize(Roles=...)]`, combinado con acciones que reciben identificadores (`idPersona`, `idWallet`, `idComercio`, `idRetiro`) directamente de la ruta/query/body del cliente sin validarlos contra el usuario autenticado. No hay evidencia de que esto fuera intencional — es la ausencia de un patrón de ownership que sí existe correctamente en otros módulos del mismo proyecto (`ComercioScopeService`, ya usado en Fase 70.1/70.3/70.4).

## 5. Corrección aplicada

- **`GET /api/wallets/mi-wallet`** (nuevo): resuelve la wallet propia exclusivamente desde el claim `idPersona` del JWT — sin parámetro manipulable.
- **`GET /api/wallets/persona/{idPersona}`, `{idWallet}/saldo`, `{idWallet}/movimientos`**: restringidos a `ADMIN_XPAY,SUPERUSUARIO`.
- **`GET /api/reportes/mi-estado-cuenta`** (nuevo): resuelve la wallet propia vía `WalletService.ObtenerWalletPersonaAsync(idPersona)` (mismo claim), luego consulta el estado de cuenta de esa wallet.
- **`GET /api/reportes/wallet/{idWallet}/estado-cuenta`**: restringido a `ADMIN_XPAY,SUPERUSUARIO`.
- **`GET /api/reportes/comercios/{idComercio}/resumen`, `ledger/transaccion/{idTransaccion}`**: restringidos a `ADMIN_XPAY,SUPERUSUARIO`.
- **`GET /api/reportes/operaciones/resumen-general`**: restringido a `ADMIN_XPAY,SUPERUSUARIO,OPERADOR_XPAY`.
- **`AdminController`** (los 4 endpoints, a nivel de clase): restringido a `ADMIN_XPAY,SUPERUSUARIO`.
- **`ComerciosController`**: restringido a nivel de clase a `ADMIN_XPAY,SUPERUSUARIO,OPERADOR_XPAY,COMERCIO` (excluye explícitamente `USUARIO_FINAL` y cualquier usuario de empresa sin uno de estos roles); **ownership fino agregado en `RetiroComercioService`**, no solo en el atributo:
  - `ListarRetirosAsync`/`GetRetiroByIdAsync`: si el solicitante no es administrativo (`ADMIN_XPAY`/`SUPERUSUARIO`/`OPERADOR_XPAY`), el `idComercio` recibido del cliente se **ignora por completo** y se fuerza el de su propio scope (`ComercioScopeService.RequireScopeAsync`) — mismo criterio ya aprobado en `AbrirAsync` (Fase 70.4). Un retiro fuera de ese comercio responde con el mismo mensaje que "no existe" (no revela existencia fuera de alcance).
  - `SolicitarRetiroAsync`: mismo criterio — `request.IdComercio` se fuerza desde el scope real para solicitantes `COMERCIO`; `request.CreadoPor` se sobrescribe siempre con el `idUsuario` autenticado (nunca se confía en el valor del body).
  - `ConfirmarRetiroPagadoAsync`/`RechazarRetiroAsync`: restringidos por atributo a `ADMIN_XPAY,SUPERUSUARIO,OPERADOR_XPAY` (nunca `COMERCIO` — un comercio no puede auto-aprobar su propio retiro); `CreadoPor` también sobrescrito con el `idUsuario` autenticado.
  - `liquidar-venta-qr`: restringido a `ADMIN_XPAY,SUPERUSUARIO` — sin consumidor frontend confirmado, decisión conservadora documentada, no una capacidad comprobada de otro rol.

## 6. Roles finales por endpoint

| Endpoint | Roles permitidos |
|---|---|
| `GET wallets/mi-wallet` | cualquier autenticado |
| `GET wallets/persona/{idPersona}` | `ADMIN_XPAY,SUPERUSUARIO` |
| `GET wallets/{idWallet}/saldo` | `ADMIN_XPAY,SUPERUSUARIO` |
| `GET wallets/{idWallet}/movimientos` | `ADMIN_XPAY,SUPERUSUARIO` |
| `GET reportes/mi-estado-cuenta` | cualquier autenticado |
| `GET reportes/wallet/{idWallet}/estado-cuenta` | `ADMIN_XPAY,SUPERUSUARIO` |
| `GET reportes/comercios/{idComercio}/resumen` | `ADMIN_XPAY,SUPERUSUARIO` |
| `GET reportes/ledger/transaccion/{idTransaccion}` | `ADMIN_XPAY,SUPERUSUARIO` |
| `GET reportes/operaciones/resumen-general` | `ADMIN_XPAY,SUPERUSUARIO,OPERADOR_XPAY` |
| `GET admin/wallets`, `/comercios`, `/ventas-qr`, `/ledger-transacciones` | `ADMIN_XPAY,SUPERUSUARIO` |
| `GET comercios/retiros`, `retiros/{id}` | `ADMIN_XPAY,SUPERUSUARIO,OPERADOR_XPAY` (cualquier comercio) + `COMERCIO` (solo el propio, forzado en servicio) |
| `POST comercios/solicitar-retiro` | `ADMIN_XPAY,SUPERUSUARIO` (cualquier comercio) + `COMERCIO` (solo el propio, forzado) |
| `POST comercios/retiros/confirmar-pago`, `retiros/rechazar` | `ADMIN_XPAY,SUPERUSUARIO,OPERADOR_XPAY` |
| `POST comercios/liquidar-venta-qr` | `ADMIN_XPAY,SUPERUSUARIO` |

## 7. Validaciones de ownership

Implementadas en `RetiroComercioService` (no solo en atributos `[Authorize]`):
- `esAdministrativo` se calcula en el controller (`User.IsInRole("ADMIN_XPAY") || ... "SUPERUSUARIO" || ... "OPERADOR_XPAY"`) y se pasa al servicio.
- Cuando `esAdministrativo == false`, el servicio resuelve el scope real del solicitante vía `ComercioScopeService.RequireScopeAsync(idUsuario)` y fuerza/valida `IdComercio` contra ese scope — nunca contra el valor recibido del cliente.
- `GetRetiroByIdAsync` responde con el mismo mensaje genérico de "no existe" tanto si el retiro realmente no existe como si existe pero pertenece a otro comercio — no revela la existencia de recursos fuera de alcance.

`WalletsController.ObtenerMiWallet`/`ReportesController.MiEstadoCuenta` no necesitan ownership adicional — el `idPersona` viene exclusivamente del claim, no hay parámetro que validar.

## 8. Compatibilidad

| Consumidor frontend | Endpoint que llama | ¿Sigue funcionando? |
|---|---|---|
| `DashboardPage.tsx` | `GET /api/comercios/retiros` | Sí — se asume rol admin/operador ya presente |
| `RetirosListPage.tsx`, `RetiroPage.tsx` | `GET/POST /api/comercios/retiros*` | Sí — mismo supuesto |
| `MiComercioPage.tsx` | `GET /api/comercios/retiros?idComercio=...` | Sí — el parámetro ahora se ignora y se fuerza desde el scope real del comercio, que ya coincide con lo que la página esperaba mostrar |
| `WalletsListPage.tsx`, `ComerciosListPage.tsx`, `VentasQrListPage.tsx`, `LedgerTransaccionesListPage.tsx` | `GET /api/admin/*` | Sí, **si y solo si** el usuario admin real ya tiene `ADMIN_XPAY`/`SUPERUSUARIO` — no verificado contra datos QA reales en esta etapa (sin ejecución en vivo) |
| **`UserWalletPage.tsx`** | `GET /api/wallets/mi-wallet`, `GET /api/reportes/mi-estado-cuenta`, `POST /api/wallets/transferencia` | **Resuelto en 71.2-E-C** — migrada de `DEMO_MAP` + endpoints admin-only a los contratos propios por claim. Ver sección 12. |

Ningún otro consumidor frontend fue identificado para los endpoints de `WalletsController`/`ReportesController`/`AdminController` restringidos, más allá de los ya listados.

## 9. Pruebas realizadas

**No se ejecutó ninguna prueba contra QA en vivo** (sin credenciales de base de datos en esta sesión, consistente con el resto de esta fase). Se verificó por inspección de código + `dotnet build` que:
- Los 4 archivos modificados compilan sin errores ni advertencias.
- Los atributos `[Authorize(Roles=...)]` están sintácticamente correctos y en el nivel esperado (clase vs. método, con los overrides de método donde corresponde).
- La lógica de `EsAdministrativo`/ownership en `RetiroComercioService` fue revisada manualmente línea por línea contra la matriz de la sección 10 del reporte de entrega.

**Matriz manual/técnica** (diseñada, no ejecutada en vivo — ver sección 12 de la entrega para el detalle completo por caso).

## 10. Pendientes frontend

1. ~~Migrar `UserWalletPage.tsx` de `DEMO_MAP` + `reportes/wallet/{idWallet}/estado-cuenta` a `wallets/mi-wallet` + `reportes/mi-estado-cuenta`.~~ **Resuelto en 71.2-E-C** (sección 12.3).
2. Confirmar que los usuarios admin QA reales (`qa.admin.xpay`, `qa.operador.xpay`) efectivamente tienen los roles `ADMIN_XPAY`/`OPERADOR_XPAY` esperados, ejecutando la matriz de pruebas contra QA cuando haya acceso. **Sigue pendiente** — sin credenciales QA en vivo en 71.2-E-C tampoco.
3. `MiComercioPage.tsx` puede simplificarse eventualmente para dejar de enviar `idComercio` en la query de retiros (ya es ignorado), pero no es urgente — no rompe nada mientras tanto.
4. **Nuevo (71.2-E-C):** `WALLET_USER_MAP` en `UserWalletPage.tsx` sigue siendo un mapa estático de 2 entradas (`qa.usuario1`/`qa.usuario2`) usado solo para mostrar el nombre del contrapartida en movimientos/toasts; para cualquier otra wallet cae a `Wallet #{id}` (no rompe, solo pierde el nombre legible). Ver sección 12.5.
5. **Nuevo (71.2-E-C):** `UserWalletPage.handlePagarQr` sigue llamando `POST /api/qr/pagar`, endpoint no auditado ni modificado en esta etapa (fuera de alcance — "transferencia" fue el único contrato de escritura Wallet autorizado). Ver riesgo residual en sección 11.

## 11. Riesgos residuales

- ~~`WalletsController.RecargarManual`/`Transferir` no fueron corregidos en esta etapa~~ — **corregidos en 71.2-E-C**, ver sección 12.1/12.2.
- ~~`TransferirWalletAsync` — race condition / doble gasto (no mitigado)~~ — **corregido en 71.2-E-D** con lock pesimista `WITH (UPDLOCK, ROWLOCK)` en orden ascendente de `IdWallet`, ver sección 14.2/14.4.
- ~~`RetiroComercioService.ConfirmarRetiroPagadoAsync`/`RechazarRetiroAsync` — misma clase de race condition sobre el guard `Estado == PENDIENTE`~~ — **corregido en 71.2-E-D** con el mismo lock pesimista sobre `retiros_comercio`, ver sección 14.8.
- ~~`POST /api/qr/pagar` — no auditado en 71.2-E-C~~ — **auditado y corregido en 71.2-E-D** (IDOR V18 + race condition), ver sección 14.4/14.7.
- **`WalletOperacionService.RecargarWalletManualAsync` — mismo patrón de race condition, no mitigado:** lee `WalletSaldos` con `FirstOrDefaultAsync` sin lock pesimista. No estaba en el alcance explícito de 71.2-E-D (que solo nombró Transferencia, Pago QR y Retiros). Como está restringido a `ADMIN_XPAY,SUPERUSUARIO` desde 71.2-E-C, el actor que podría explotarlo ya es de confianza elevada — severidad baja, pero técnicamente presente. Reportado, no corregido.
- **`RetiroComercioService.SolicitarRetiroAsync`/`RechazarRetiroAsync` — race condition sobre `wallet_saldos` del comercio (distinta de la ya corregida sobre `retiros_comercio`):** ambos métodos leen y escriben `saldoComercio` con `FirstOrDefaultAsync` sin lock. El lock agregado en 71.2-E-D sobre la fila `retiros_comercio` protege el guard de estado de **un mismo retiro**, pero no protege el saldo del comercio si dos operaciones distintas (p. ej. dos `SolicitarRetiroAsync` concurrentes, o un `RechazarRetiroAsync` y un `SolicitarRetiroAsync` simultáneos) sobre la **misma wallet de comercio** pero **distintos retiros** compiten por la misma fila de saldo. Mismo mecanismo que V16/V18 antes de su corrección. No estaba en el alcance explícito de la sección 8 de 71.2-E-D (que solo pedía revisar el guard `Estado == PENDIENTE`). Reportado, no corregido.
- **Idempotencia — ningún endpoint de Wallet la implementa** (`transferencia`, `qr/pagar`, `recarga-manual`, `solicitar-retiro`, `confirmar-pago`, `rechazar`): un reintento de red, un doble clic que escapa a la ventana de `envBusy`/`pagBusy` en el cliente, o un replay literal del mismo request HTTP, se procesan como una operación nueva e independiente — el lock de concurrencia agregado en esta etapa **no** protege contra esto (ver distinción explícita en sección 14.5). Diseño presentado en sección 14.5, requiere una tabla nueva (migración) — no implementado, a la espera de autorización.
- **QR de comercio reutilizable sin vencimiento ni firma:** `QrComercios.Estado` nunca cambia tras un pago exitoso — el mismo `codigoQr` puede pagarse un número ilimitado de veces, cada vez creando una `VentaQr` independiente. Esto puede ser el comportamiento de negocio correcto para un QR fijo de mostrador, o un defecto de replay para un QR de cobro puntual — no se puede determinar solo por el código. Ver sección 14.4. Reportado como pregunta abierta de producto, no como vulnerabilidad de acceso.
- **`ReportesController.comercios/{idComercio}/resumen`**: quedó restringido a admin, pero no se construyó todavía el endpoint acotado por `ComercioScopeService` para que el propio comercio consulte su resumen — ese caso de uso queda sin servir hasta una etapa futura.
- **Sin ejecución contra QA real**: todo lo anterior (71.2-E-B, 71.2-E-C y 71.2-E-D) está verificado por inspección de código y compilación/build, no por pruebas en vivo — riesgo estándar de cualquier cambio no desplegado. Ver matriz de pruebas preparada (no ejecutada) en sección 14.11.
- **`WalletService.ObtenerWalletPersonaAsync`/`ObtenerSaldoAsync`/`ObtenerMovimientosAsync`** siguen siendo métodos "abiertos" a nivel de servicio (sin ownership propio) — la protección ahora vive enteramente en el controller (rol) y en `mi-wallet`/`transferencia`/`qr/pagar` (claim). Si en el futuro se agrega un nuevo consumidor de estos métodos de servicio sin pasar por un controller ya protegido, el riesgo podría reintroducirse.

## 12. Fase 71.2-E-C — Cierre de seguridad Wallet y compatibilidad mínima

### 12.1 Auditoría y corrección de `Transferir`

**Flujo antes de la corrección:** `WalletsController.Transferir` recibía `TransferenciaWalletRequest` con `IdWalletOrigen` y `CreadoPor` como campos del body, y los pasaba sin validar a `WalletOperacionService.TransferirWalletAsync`. El servicio solo validaba: `Valor > 0`, `idWalletOrigen != idWalletDestino`, wallets origen/destino existen y están `ACTIVA`, tipo `PERSONA`, saldo suficiente en origen, transacción atómica con verificación de balance débito=crédito. Ninguna de esas validaciones comprobaba que la wallet origen perteneciera al solicitante — un atacante autenticado podía transferir dinero **desde el saldo de cualquier wallet ajena** simplemente cambiando `idWalletOrigen` en el body (V16, IDOR de escritura, severidad crítica).

**Corrección aplicada:**
- `TransferenciaWalletRequest` (`backend/Xpay.Api/DTOs/TransferenciaWalletRequest.cs`) se redujo a `{ IdWalletDestino, Valor, Descripcion }` — `IdWalletOrigen` y `CreadoPor` se eliminaron por completo del contrato, no solo se ignoran.
- `WalletOperacionService.TransferirWalletAsync` cambió su firma a `(long idWalletOrigen, long creadoPor, TransferenciaWalletRequest request)` — ambos valores llegan como parámetros propios del método, nunca del DTO deserializado del cliente.
- `WalletsController.Transferir` resuelve `idWalletOrigen` exclusivamente vía `_walletService.ObtenerWalletPersonaAsync(idPersona)`, con `idPersona` leído del claim JWT (401 si falta/inválido, 404 si la persona no tiene wallet activa); `creadoPor` se lee del claim `idUsuario` (mismo patrón). El resto de la lógica de negocio (saldo suficiente, wallets activas, tipo PERSONA, transacción atómica, balance ledger, auditoría) **no se modificó**.
- No existe hoy un flujo administrativo separado que transfiera en nombre de un tercero — único consumidor confirmado (por grep) es `UserWalletPage.tsx`. Si en el futuro se necesita un flujo así, debe ser un endpoint/contrato distinto, no reutilizar este.

### 12.2 Auditoría y decisión sobre `RecargarManual`

**Hallazgo:** `POST /api/wallets/{idWallet}/recarga-manual` no tiene ningún consumidor frontend (confirmado por grep en todo `frontend/xpay-admin/src`). El método `RecargarWalletManualAsync` acredita saldo (`SaldoDisponible += valor`) sin validar ningún respaldo de pago real — es una operación que crea dinero contablemente hablando (débito a la cuenta de liquidez, crédito a obligación wallet usuarios), no una transferencia entre wallets existentes. Bajo `[Authorize]` genérico, cualquier autenticado podía acreditar saldo a cualquier wallet arbitraria sin restricción.

**Decisión técnica:** restringido a `[Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]`, igual que los demás endpoints administrativos ya corregidos en 71.2-E-B sobre el mismo controller. Se implementó (no solo se reportó) porque el hallazgo es inequívoco, cae directamente dentro del objetivo explícito de la etapa ("cerrar completamente los riesgos de seguridad de Wallet") y usa una combinación de roles ya en alcance (`ADMIN_XPAY,SUPERUSUARIO`, ya usada en el mismo controller). No se tocó la lógica interna del método ni el DTO `RecargaWalletRequest`.

### 12.3 Compatibilidad mínima de `UserWalletPage.tsx`

Cambios aplicados, todos dentro de `frontend/xpay-admin/src/pages/UserWalletPage.tsx`, sin rediseñar la pantalla, sin tocar `AppShell`, sin cambiar el flujo/UX visible:

- **`DEMO_MAP` eliminado por completo.** Se agregó estado `miWallet` (`idWallet, idPersona, nombreWallet, estado`) cargado mediante `GET /api/wallets/mi-wallet` en un nuevo `loadMiWallet()`, llamado junto a `loadCuenta`/`loadKyc`/`loadBreb` en el `useEffect` de carga inicial. Mientras carga se muestra un estado de "Cargando wallet..." (necesario porque la resolución pasó de síncrona — lookup en un objeto local — a asíncrona vía API; si no falla no hay wallet activa, se muestra el mismo tipo de mensaje de error que antes).
- **`loadCuenta`/`pollRefresh`** migrados de `GET /api/reportes/wallet/{idWallet}/estado-cuenta` (ahora admin-only) a `GET /api/reportes/mi-estado-cuenta` (resuelve la wallet propia por claim, sin parámetro). Ya no dependen de `demoInfo`, solo de que `user` exista.
- **`handleEnviar`** (transferencia): el body enviado a `POST /api/wallets/transferencia` ahora es exactamente `{ idWalletDestino, valor, descripcion }` — ya no envía `idWalletOrigen` ni `creadoPor` (el DTO backend tampoco los acepta ya). La comparación "no puedes transferirte a tu propia wallet" usa `miWallet.idWallet`.
- **`handleGenerarQr`** (QR para recibir) y el texto informativo de la pestaña "Recibir" usan `miWallet.idWallet` en vez de `demoInfo.idWallet`.
- **`handlePagarQr`**: sigue llamando a `POST /api/qr/pagar` (endpoint no tocado en esta etapa), pero el body ahora usa `miWallet.idWallet` (antes `demoInfo.idWallet`) y `user.idUsuario` (antes `demoInfo.idUsuario`) como fuente de los mismos campos que ya se enviaban — mismo contrato, distinta fuente de los valores, consistente con la instrucción de eliminar toda dependencia funcional de `demoInfo`.
- **`defaultDestWallet`** no requirió eliminación de código adicional más allá de quitar `DEMO_MAP`: se confirmó por revisión completa del archivo que ese campo nunca se leía en ningún handler — el destino de una transferencia ya partía vacío (`envDest = null` inicial) y solo se llenaba por QR escaneado/pegado o entrada manual. No había comportamiento que cambiar.
- **Dependencia funcional de `qa.usuario1`/`qa.usuario2` eliminada**: ningún camino del componente depende ya de esos usernames específicos; cualquier usuario autenticado con una wallet `PERSONA` activa puede usar la pantalla completa.
- Build verificado: `npm run build` (`tsc && vite build`) — **0 errores TypeScript, build de producción exitoso** (advertencia preexistente de tamaño de chunk, no relacionada con estos cambios).

### 12.4 Revisión rápida de retiros (solo inspección, sin cambios de código)

Revisado `RetiroComercioService.ConfirmarRetiroPagadoAsync`/`RechazarRetiroAsync`/`SolicitarRetiroAsync` completos:

| Caso | Resultado de la inspección |
|---|---|
| Confirmar dos veces | Bloqueado — segunda llamada encuentra `Estado == "PAGADO"` (≠ `PENDIENTE`) y lanza `InvalidOperationException` antes de tocar el ledger |
| Rechazar dos veces | Bloqueado — mismo guard, segunda llamada encuentra `Estado == "RECHAZADO"` |
| Confirmar luego de rechazado | Bloqueado — `Estado == "RECHAZADO"` ≠ `PENDIENTE` |
| Rechazar luego de pagado | Bloqueado — `Estado == "PAGADO"` ≠ `PENDIENTE` |
| Estados permitidos | Ambas transiciones parten siempre de `PENDIENTE`; no hay camino de código que permita otra transición de origen |
| Usuario ejecutor | `idUsuario` viene del claim JWT vía `ComerciosController.TryGetUsuarioId`, se fuerza en `request.CreadoPor` dentro del servicio — nunca se confía en el valor recibido del body |
| Transacción | Ambos métodos usan `BeginTransactionAsync`/commit/rollback explícito, con verificación de balance débito=crédito antes de confirmar |

**Falta una validación — reportada, no implementada** (instrucción explícita de no ampliar alcance): ninguno de los dos métodos usa `WITH (UPDLOCK, ROWLOCK)` ni un token de concurrencia sobre la fila `RetiroComercio` leída al inicio. El guard de estado es correcto para llamadas secuenciales pero no impide una carrera entre dos llamadas concurrentes que lean `PENDIENTE` antes de que cualquiera de las dos confirme su cambio de estado (p. ej. doble clic en "confirmar pago", o "confirmar" y "rechazar" casi simultáneos). Ver riesgo residual en sección 11.

### 12.5 `WALLET_USER_MAP` — usos restantes

Único propósito confirmado por grep en todo el archivo: mostrar el nombre de usuario de la contraparte en la descripción de movimientos (`descripcionVisible`, tipos `TRANSFERENCIA_SALIDA`/`TRANSFERENCIA_ENTRADA`) y en el toast de "recibiste dinero" (`pollRefresh`). En ambos casos, si el `idWallet` de la contraparte no está en el mapa, cae a `Wallet #{id}` — no bloquea ni oculta funcionalidad. No interviene en permisos, ownership, origen, destino, autorización ni ninguna decisión de negocio: es puramente texto mostrado en pantalla. Conclusión: puede permanecer temporalmente sin riesgo de seguridad, tal como prevé la instrucción de la etapa; queda registrado como pendiente frontend menor (sección 10, ítem 4).

### 12.6 Logging

Los endpoints nuevos/modificados en 71.2-E-C (`Transferir`, `RecargarManual`) siguen el patrón ya existente en el proyecto: `_audit.LogSensitiveAction(HttpContext, "EVENTO", new { ... })` en intento y éxito, con solo identificadores numéricos y montos en el payload (`idWallet`, `idWalletOrigen`, `idWalletDestino`, `valor`, `idMovimiento`/`idTransaccion`) — nunca tokens, documentos, números de cuenta ni payloads financieros completos. Las excepciones no controladas siguen cayendo en el `catch` genérico que responde `500` con un mensaje fijo genérico al cliente (`"Error interno..."`), sin exponer detalles internos ni volcar el stack trace a la respuesta. No se agregó ningún logging nuevo distinto del patrón preexistente.

### 12.7 Resultados QA — matriz de la etapa

**No se ejecutó ninguna prueba contra QA en vivo** — sin credenciales de base de datos ni entorno corriendo en esta sesión, igual que en 71.2-E-B. Todo lo siguiente es trazado de código (lectura de la lógica real que se ejecutaría), no una prueba "aprobada":

| Caso | Evidencia (solo inspección de código, no ejecución) |
|---|---|
| `mi-wallet` → 200 | `ObtenerMiWallet` retorna 200 con `idPersona` válido y wallet activa encontrada |
| `mi-wallet` sin wallet → 404 | `wallet == null` → `NotFound` |
| sin token → 401 | `[Authorize]` de clase en `WalletsController` rechaza antes de llegar a la acción |
| `mi-estado-cuenta` → 200 | Mismo patrón, vía `ReportesController.MiEstadoCuenta` |
| wallet ajena (IDOR) → 403/404 | No aplica 403 explícito: `mi-wallet`/`mi-estado-cuenta` no aceptan ningún id de wallet ajena como parámetro — estructuralmente no hay forma de pedir "la wallet de otro" desde estos endpoints |
| saldo ajeno, movimientos ajenos, estado de cuenta ajeno vía endpoints admin | Bloqueados por `[Authorize(Roles="ADMIN_XPAY,SUPERUSUARIO")]` para cualquier rol fuera de esos dos (403 esperado por el pipeline de autorización de ASP.NET Core, no probado en vivo) |
| Transferencia válida | Trazado: claims válidos → wallet origen resuelta → validaciones de servicio (saldo, activa, tipo) → commit |
| Transferencia con saldo insuficiente | `saldoOrigen.SaldoDisponible < request.Valor` → `InvalidOperationException` → 400 |
| Transferencia origen=destino | `idWalletOrigen == request.IdWalletDestino` → `InvalidOperationException` → 400 |
| Modificar manualmente `idWalletOrigen` | **No es posible** — el campo ya no existe en el DTO; cualquier valor adicional enviado en el body es ignorado por el binder de ASP.NET Core |
| Doble clic / doble envío | **No mitigado** — ver riesgo residual sección 11 (sin `UPDLOCK`/`ROWLOCK` ni `CHECK >= 0`). No se pudo reproducir en vivo (sin DB), reportado como riesgo de código, no como prueba aprobada |
| Recarga — actor autorizado (`ADMIN_XPAY`/`SUPERUSUARIO`) | Trazado: pasa el `[Authorize(Roles=...)]`, llega al servicio |
| Recarga — actor no autorizado | Bloqueado por el mismo atributo antes de llegar a la acción |
| Retiros: comercio solo propios, operador, administrador | Ver sección 12.4 — trazado completo, sin ejecución en vivo |

Ninguno de los casos anteriores se declara "aprobado" — todos son trazado de código con `dotnet build`/`npm run build` exitosos como única verificación ejecutada realmente.

## 14. Fase 71.2-E-D — Integridad transaccional Wallet, Pago QR y Logging

### 14.1 Verificación del estado final (antes de modificar nada)

Verificado leyendo el contenido real de los 4 archivos antes de tocar cualquiera:

- **`TransferenciaWalletRequest.cs`**: `{ IdWalletDestino, Valor, Descripcion }` — confirmado que **no** contiene `IdWalletOrigen` ni `CreadoPor`.
- **`WalletsController.Transferir`**: confirmado que obtiene `idPersona` e `idUsuario` desde `TryGetIdPersona`/`TryGetUsuarioId` (claims JWT), nunca del body.
- **`WalletOperacionService.TransferirWalletAsync`**: confirmado que su firma es `(long idWalletOrigen, long creadoPor, TransferenciaWalletRequest request)` — ambos valores llegan como parámetros del método, no del DTO.
- **`WalletsController.RecargarManual`**: confirmado `[Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]` sobre el método.
- **Frontend (`UserWalletPage.tsx`, `handleEnviar`)**: confirmado que el body enviado a `POST /api/wallets/transferencia` es exactamente `{ idWalletDestino, valor, descripcion }`.

**No se encontró ninguna diferencia con el informe entregado en 71.2-E-C** — el estado real coincide en los 5 puntos con lo reportado. No fue necesario corregir nada antes de continuar.

### 14.2 Auditoría completa de concurrencia en `TransferirWalletAsync` (estado previo a esta etapa)

Código auditado línea por línea: apertura `await _db.Database.BeginTransactionAsync()` (sin `IsolationLevel` explícito → usa el nivel por defecto de la conexión SQL Server), lectura de `WalletSaldos` vía `FirstOrDefaultAsync` (LINQ→SELECT normal, sin locking hint), mutación en memoria de `SaldoDisponible`, `SaveChangesAsync()` (genera un `UPDATE` sin cláusula `WHERE` sobre el valor anterior, porque `WalletSaldo` no tiene `[ConcurrencyCheck]`/`[Timestamp]`/`RowVersion` — confirmado por inspección del modelo), inserciones de `LedgerMovimiento`/`WalletMovimiento`/`Auditoria`, verificación de balance débito=crédito, y `transaction.CommitAsync()`.

**⚠️ Corrección (Fase 71.2-E-E):** el párrafo original de esta sección afirmaba, sin matices, que la base de datos "opera bajo READ COMMITTED con locking, no bajo snapshot isolation". Esa conclusión **no está confirmada** — se basó únicamente en que el repositorio no contiene ningún script que active RCSI, pero **Azure SQL Database crea las bases nuevas con `READ_COMMITTED_SNAPSHOT` en `ON` por defecto** (a diferencia de SQL Server on-premises, donde el default es `OFF`) — si la base de este proyecto se aprovisionó con el comportamiento estándar de Azure SQL y nadie lo desactivó explícitamente (lo cual tampoco está en el repositorio, porque desactivarlo tampoco requeriría un script versionado necesariamente), RCSI podría estar activo hoy. Ver la distinción exacta entre lo confirmado y lo no confirmado, y la consulta preparada para verificarlo, en la sección 15.1.

**Cómo se comporta SQL Server exactamente con el código anterior a esta etapa** (el razonamiento de los puntos 1-5 de abajo asume READ COMMITTED **sin** RCSI — es decir, el escenario que sí está confirmado por código como la configuración que el proyecto no anula explícitamente, pero no necesariamente la que corre hoy en Azure SQL; ver sección 15.1 para el caso con RCSI activo):
1. Bajo READ COMMITTED con locking, un `SELECT` (la lectura de `WalletSaldos`) toma un lock compartido (S) sobre la fila, pero **ese lock se libera inmediatamente después de leer la fila**, no se mantiene hasta el `COMMIT`. Esto es distinto de un lock exclusivo (X) o de un `UPDLOCK`, que sí se retienen hasta el fin de la transacción.
2. Por lo tanto, dos transacciones concurrentes que llaman a `TransferirWalletAsync` sobre la **misma wallet origen** pueden ambas ejecutar su `SELECT` y leer el mismo `SaldoDisponible` (p. ej. 100) sin bloquearse entre sí, siempre que ninguna haya llegado todavía a su `UPDATE`.
3. Ambas transacciones validan `saldoOrigen.SaldoDisponible < request.Valor` en memoria contra ese mismo valor leído (100) — **ambas pueden pasar la validación de saldo suficiente** aunque juntas excedan el saldo real disponible.
4. Cuando la primera transacción llega a `SaveChangesAsync()`, su `UPDATE ... SET saldo_disponible = @nuevoValor WHERE id_wallet = @id` toma un lock exclusivo (X) y se ejecuta.
5. La segunda transacción, al llegar a su propio `SaveChangesAsync()`, intenta el mismo `UPDATE` y **se bloquea** esperando a que la primera libere el lock X (commit o rollback) — pero como el `UPDATE` no tiene ninguna cláusula `WHERE saldo_disponible = @valorLeído` (no hay concurrency token), no falla al desbloquearse: **simplemente sobrescribe** la fila con su propio valor, calculado en memoria a partir del saldo obsoleto (100) que leyó antes de que la primera transacción committeara.

**Respuestas explícitas:**
- **¿Dos solicitudes concurrentes pueden leer el mismo saldo?** Sí — confirmado, el `SELECT` bajo READ COMMITTED no bloquea a un segundo `SELECT` concurrente.
- **¿Ambas pueden validar saldo suficiente?** Sí, contra el mismo valor obsoleto.
- **¿Ambas pueden descontar?** Sí — ambos `UPDATE` se ejecutan (uno bloqueado brevemente detrás del otro), ninguno falla.
- **¿Puede existir doble gasto?** Sí, en el sentido de que ambos ledgers (`LedgerMovimiento`/`WalletMovimiento`) registran el débito completo — la wallet "gasta" dos veces en el libro contable.
- **¿Puede existir pérdida de actualización (lost update)?** Sí — es el mecanismo exacto: el `UPDATE` de la segunda transacción sobrescribe con un valor calculado sobre datos obsoletos, sin reflejar el débito ya aplicado por la primera.
- **¿Puede quedar saldo negativo?** No necesariamente negativo (el ejemplo anterior deja el saldo en 20, no negativo), pero sí **incorrecto**: el saldo final no refleja ambos débitos, aunque el ledger sí los registró — esto es peor que un simple saldo negativo porque es una inconsistencia silenciosa entre `WalletSaldo` y `Ledger`.
- **¿Puede existir inconsistencia entre `WalletSaldo` y `Ledger`?** Sí — es la consecuencia directa del punto anterior: el ledger (auditable, inmutable por diseño) muestra más movimiento del que el saldo cacheado en `wallet_saldos` refleja.

`BeginTransactionAsync()` por sí solo **no elimina este riesgo** — solo garantiza atomicidad (todo o nada dentro de una misma transacción) y aislamiento parcial según el nivel configurado, pero no serializa automáticamente lecturas seguidas de escrituras entre transacciones distintas sin un lock explícito o un token de concurrencia.

### 14.3 Revisión del repositorio — patrones ya aprobados

Antes de proponer una solución se buscó en todo `backend/Xpay.Api` cada patrón listado en la instrucción. Resultado (grep sobre todo el proyecto):

| Patrón | ¿Existe ya en el proyecto? |
|---|---|
| `WITH (UPDLOCK, ROWLOCK)` vía `FromSqlInterpolated` | **Sí — extensamente usado**: `CarteraOrdinariaService.cs` (5 usos, sobre `cartera_cupos_ordinarios`, `wallet_saldos`, `cartera_cuotas`), `WalletRecargaComercioService.cs` (sobre `wallet_saldos`), `WalletLiquidacionRecaudoComercioService.cs`, `WalletCierreDiarioComercioService.cs`, `WalletCajaComercioService.cs` (sobre sus tablas respectivas) |
| `HOLDLOCK` | No |
| `SERIALIZABLE` | No |
| `RowVersion`/`ConcurrencyCheck` (atributo o Fluent API `IsRowVersion()`/`IsConcurrencyToken()`) | No — ningún modelo del proyecto lo usa, confirmado por grep |
| `ExecuteSqlRaw`/`ExecuteSqlInterpolated` (para `UPDATE`s condicionales) | No — el proyecto usa `FromSqlInterpolated` solo para `SELECT ... WITH (UPDLOCK, ROWLOCK)`, nunca para `UPDATE`s condicionales |
| Procedimientos almacenados | No — cero referencias en el código ni en `database/*.sql` |
| `sp_getapplock` | No — mencionado únicamente en un comentario de `WalletCajaComercioService.cs` explicando por qué **no** se usa ahí ("no implementa sp_getapplock ni el patrón de aplicación... únicamente la apertura en sí") |

**Conclusión:** el proyecto ya tiene un patrón aprobado y usado repetidamente — lock pesimista `WITH (UPDLOCK, ROWLOCK)` leído vía `FromSqlInterpolated` dentro de la transacción EF ya abierta, exactamente sobre la misma tabla (`wallet_saldos`) que necesita `TransferirWalletAsync`. No se inventó un patrón distinto.

### 14.4 Propuesta de corrección — alternativas comparadas

| Alternativa | Seguridad | Riesgo doble gasto | Compatible Azure SQL | Cambios requeridos | Riesgo deadlock | ¿Requiere SQL/migración? | Complejidad | Facilidad de pruebas |
|---|---|---|---|---|---|---|---|---|
| **A) UPDLOCK + ROWLOCK** | Alta — serializa lectura+validación+escritura por fila | Eliminado (para operaciones concurrentes sobre la(s) misma(s) fila(s)) | Sí — sintaxis T-SQL estándar, ya usada en el proyecto en Azure SQL | Ninguno de esquema; solo código C# (`FromSqlInterpolated`) | Bajo, manejable con orden de lock consistente (ver abajo) | **No** — no crea tablas ni migraciones | Baja — patrón ya usado 5 veces en el proyecto | Alta — mismo patrón ya cubierto implícitamente por las pruebas existentes de Cartera Ordinaria |
| B) `UPDATE` condicional (`SET saldo = saldo - @v WHERE saldo >= @v`, validando filas afectadas) | Alta — atómico a nivel de una sola sentencia | Eliminado | Sí | Reescribir la lógica de actualización de saldo como SQL crudo condicional (patrón no usado hoy en el proyecto) + manejar el caso "0 filas afectadas" como saldo insuficiente | Ninguno (una sola sentencia) | Sí — es una sentencia SQL nueva no usada como patrón hoy; requiere introducir un patrón distinto al aprobado | Media — cambia la forma en que se lee `SaldoAntes` (ya no viene de un SELECT previo, requeriría un SELECT adicional o devolver el valor anterior vía `OUTPUT`) | Media |
| C) `RowVersion`/optimistic concurrency | Media — detecta el conflicto, no lo previene; requiere reintentar | Mitigado solo si el llamador reintenta correctamente | Sí | **Migración**: agregar columna `rowversion`/`timestamp` a `wallet_saldos` + configurar `IsRowVersion()` en el modelo + manejar `DbUpdateConcurrencyException` con lógica de reintento | Ninguno | **Sí — requiere migración de esquema** | Media-alta (lógica de reintento) | Media |
| D) Procedimiento almacenado | Alta, si se implementa con el mismo locking interno | Eliminado si el proc usa locking correcto | Sí | **Nuevo objeto de base de datos** (script SQL), nueva forma de invocación desde EF (`FromSqlInterpolated`/`ExecuteSqlInterpolated` a un proc), sin precedente en el proyecto (todo el acceso a datos es EF Core puro) | Igual que A si se implementa igual | **Sí — requiere script SQL nuevo** | Alta — nuevo patrón de acceso a datos, lógica de negocio saliendo de C# | Baja — más difícil de testear/depurar que código C# |
| E) Combinación (A + idempotencia) | La más alta de todas | Eliminado (concurrencia) + eliminado (duplicación) | Sí | A (sin SQL nuevo) + tabla de idempotencia (con SQL nuevo, ver 14.5) | Igual que A | **Parcialmente** — A no requiere SQL, la parte de idempotencia sí | Media | Alta para A, media para la parte de idempotencia |

**Solución elegida: A) `WITH (UPDLOCK, ROWLOCK)`**, por ser la única alternativa que resuelve completamente la concurrencia **sin requerir una migración ni un objeto SQL nuevo** — es el patrón ya aprobado y usado 5 veces en este mismo proyecto sobre esta misma tabla (sección 14.3). Al no requerir SQL nuevo, se implementó directamente (no cae bajo la instrucción "si requiere SQL, no implementarla, solo documentar"). Las alternativas C y D sí requieren SQL/migración y **no se implementaron** — quedan documentadas arriba como diseño únicamente, a la espera de autorización si en el futuro se prefiere optimistic concurrency o procedimientos almacenados sobre el locking pesimista.

**Cambios implementados** (`WalletOperacionService.TransferirWalletAsync`):
- Las dos filas de `wallet_saldos` (origen y destino) se leen con `FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {id}")` dentro de la transacción ya abierta.
- **Orden de lock determinístico:** se calcula `idWalletMenor`/`idWalletMayor` (comparando `idWalletOrigen` vs `request.IdWalletDestino`) y siempre se bloquea primero la fila de `IdWallet` menor, luego la mayor — sin importar cuál es origen y cuál destino. Esto es necesario porque, a diferencia de los usos previos del patrón en el proyecto (que siempre bloquean una sola fila), una transferencia bloquea **dos** filas de la misma tabla; sin un orden consistente, dos transferencias cruzadas entre las mismas dos wallets en sentido opuesto (A→B y B→A simultáneas) podrían deadlockearse (cada una esperando la fila que la otra ya tiene bloqueada). Con orden ascendente consistente, esa situación no puede ocurrir.
- El resto de la lógica (validación de saldo suficiente, mensajes de error, cálculo de `SaldoAntes`/`SaldoDespues`, ledger, auditoría) no se modificó — solo cambió **cómo** se obtienen las filas de saldo.
- El mismo patrón se aplicó a `PagoQrService.PagarQrAsync` (una sola fila, sin necesidad de orden — ver 14.6/14.7) y a `RetiroComercioService.ConfirmarRetiroPagadoAsync`/`RechazarRetiroAsync` (una fila de `retiros_comercio` — ver 14.8).

### 14.5 Idempotencia — diseño (no implementado, requiere SQL)

**Auditoría de la transferencia actual:** el único mecanismo existente contra doble envío es el estado `envBusy` en `UserWalletPage.tsx`, que deshabilita el botón de "Enviar dinero" mientras la petición está en curso (`disabled={envBusy || ...}`), restaurado siempre en el bloque `finally`. Esto cubre el caso de un doble clic normal dentro de la misma pestaña/instancia del componente, pero **no** protege contra:
- Un reintento automático de red/navegador tras un timeout.
- Un usuario reabriendo la pestaña o reenviando el formulario después de un error de conexión.
- Dos pestañas/dispositivos distintos.
- Un replay literal del mismo request HTTP capturado (p. ej. por un proxy intermedio).

**Distinción explícita pedida por la etapa:**
- **Concurrencia** (resuelta en 14.2/14.4): dos operaciones — iguales o distintas — que compiten por la misma fila de saldo al mismo tiempo. El lock `UPDLOCK/ROWLOCK` garantiza que el resultado final sea correcto sin importar el orden de llegada.
- **Duplicación**: el mismo request lógico llega más de una vez, en momentos potencialmente distintos (no necesariamente al mismo tiempo — puede ser 30 segundos después). El lock de concurrencia **no** ayuda aquí: ambos requests, procesados uno tras otro, son transferencias igualmente válidas desde la perspectiva del backend — no hay forma de distinguir "un reintento del mismo intento" de "una segunda transferencia intencional idéntica" sin información adicional.
- **Idempotencia**: la propiedad que sí resuelve la duplicación — requiere que el cliente adjunte un identificador único por intento (no por reintento) que el servidor pueda usar para reconocer "ya procesé esto" y devolver el resultado ya obtenido en vez de repetir la operación.

**Diseño propuesto (no implementado):**
1. **Frontend**: generar un `idempotencyKey` (UUID) una única vez por intento de transferencia/pago (al abrir el formulario o al construir el primer request), y reenviar el **mismo** valor en cualquier reintento automático del mismo intento (nunca regenerarlo hasta que el usuario inicie una transferencia nueva). Enviarlo como header `Idempotency-Key` o campo del body.
2. **Backend — nueva tabla** (requiere migración, no creada):
   ```sql
   CREATE TABLE wallet_idempotencia (
       id_idempotencia   BIGINT IDENTITY PRIMARY KEY,
       idempotency_key   UNIQUEIDENTIFIER NOT NULL,
       endpoint          VARCHAR(100)     NOT NULL,
       id_usuario        BIGINT           NOT NULL,
       id_recurso        BIGINT           NULL,      -- p.ej. id_transaccion_ledger resultante
       respuesta_json    NVARCHAR(MAX)    NULL,       -- respuesta cacheada a devolver en un reintento
       fecha_creacion    DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
       CONSTRAINT uq_wallet_idempotencia UNIQUE (idempotency_key, endpoint)
   );
   ```
3. **Lógica de servicio**: al inicio de cada método (`TransferirWalletAsync`, `PagarQrAsync`), dentro de la misma transacción, verificar si ya existe una fila para `(idempotency_key, endpoint)`; si existe, devolver la respuesta cacheada sin reprocesar; si no existe, procesar normalmente e insertar la fila de idempotencia **como parte de la misma transacción atómica** (se confirma o se revierte junto con el resto).
4. La restricción `UNIQUE (idempotency_key, endpoint)` a nivel de base de datos actúa como respaldo final: incluso si dos requests idénticos llegan verdaderamente al mismo tiempo y ambos pasan la verificación en memoria, solo uno podrá insertar la fila de idempotencia — el otro fallará con una violación de restricción única, que el servicio debe capturar y traducir a "ya procesado, devolviendo resultado existente".

**No implementado** — requiere una migración nueva (tabla `wallet_idempotencia`), fuera de lo permitido en esta etapa ("No crear migraciones"). Presentado solo como diseño, a la espera de autorización explícita.

### 14.6 Auditoría completa de `POST /api/qr/pagar`

Flujo inspeccionado completo: `QrController.Pagar` → `PagoQrService.PagarQrAsync` → lectura `QrComercios`/`Comercios`/`ComercioTiendas`/`Wallets`/`WalletSaldos` → cuentas ledger → `LedgerTransaccion`/`LedgerMovimiento` → `WalletMovimiento` → `VentaQr` → disponibilidad para comercios aliados (best-effort) → `Auditoria`.

**Respuestas, estado antes de esta etapa:**
- **¿`idWalletUsuario` viene del frontend?** Sí, directo del body (`request.IdWalletUsuario`), sin transformar.
- **¿Se valida ownership?** No — ninguna comprobación de que la wallet pertenezca al usuario autenticado. `QrController` ni siquiera leía ningún claim del JWT.
- **¿`CreadoPor` viene del frontend?** Sí, campo opcional (`long? CreadoPor`) tomado directo del body.
- **¿Puede alterarse?** Sí, ambos campos eran completamente controlables por el cliente.
- **¿Existe IDOR?** **Sí, confirmado — V18** (severidad crítica, mismo patrón que V16/`Transferir` antes de su corrección): un atacante autenticado podía pagar un QR de comercio descontando el saldo de **cualquier wallet ajena** con solo cambiar `idWalletUsuario` en el body.
- **¿La wallet pagadora realmente pertenece al usuario autenticado?** Antes de esta etapa: no se validaba. Después: sí, se resuelve exclusivamente desde el claim `idPersona`.
- **¿Puede reutilizarse el QR?** Sí, estructuralmente — `QrComercios.Estado` nunca cambia a partir de un pago exitoso; cada pago crea una `VentaQr` nueva sobre el mismo `codigoQr`, sin límite. Esto puede ser el diseño intencional para un QR fijo de comercio (como un QR pegado en el mostrador que acepta pagos ilimitados de distintos clientes), o un defecto si el QR estuviera pensado como un cobro puntual de un solo uso — **no se puede determinar la intención de producto solo leyendo el código**. Se reporta como pregunta abierta (ver riesgos residuales, sección 11), no se corrige sin confirmación de negocio.
- **¿Puede pagarse dos veces?** El mismo request duplicado/reintentado: sí (sin idempotencia, ver 14.5). Dos pagos intencionales distintos al mismo QR: sí, y es coherente con un QR reusable de comercio.
- **¿Puede existir replay?** Si se entiende como reenviar literalmente el mismo request capturado: sí, indistinguible de una duplicación legítima — no hay nonce, timestamp de expiración ni firma en el payload del QR (el propio código frontend ya documenta "sin firma criptográfica en esta fase" en el comentario de `XpayMerchantQR`).
- **¿Puede modificarse el valor?** El valor SIEMPRE lo determina el pagador en el frontend (`pagValor`), no viene fijado por el QR en el modelo `QrComercio` actual (no existe un campo `ValorEsperado` contra el cual validar `request.Valor`). Esto es consistente con un flujo "el pagador ingresa el monto", no es en sí un IDOR — pero significa que si el comercio esperara cobrar un monto fijo codificado en su QR, nada en el backend lo garantiza. Reportado como hallazgo de integridad de negocio, no de autorización.

**Corrección aplicada** (mismo principio que `Transferir`, JWT → idPersona → wallet propia → wallet pagadora):
- `PagoQrRequest` reducido a `{ CodigoQr, Valor, Descripcion }` — se eliminaron `IdWalletUsuario` y `CreadoPor` del contrato.
- `PagoQrService.PagarQrAsync` cambió su firma a `(long idWalletUsuario, long creadoPor, PagoQrRequest request)`.
- `QrController.Pagar` ahora inyecta `WalletService`, agrega `TryGetIdPersona`/`TryGetUsuarioId` (mismo patrón que `WalletsController`), resuelve la wallet propia vía `_walletService.ObtenerWalletPersonaAsync(idPersona)` (401 si el claim falta, 404 si la persona no tiene wallet activa), y pasa `walletPropia.IdWallet`/`idUsuario` explícitamente al servicio. Nunca se confía en un `idWalletUsuario` recibido del cliente.
- Frontend (`UserWalletPage.handlePagarQr`): body actualizado a `{ codigoQr, valor, descripcion }` — ya no envía `idWalletUsuario` ni `creadoPor` (el DTO backend tampoco los acepta ya).

### 14.7 Concurrencia en Pago QR

Mismo mecanismo de race condition que 14.2 (una sola wallet involucrada, sin necesidad de orden de lock): dos pagos QR simultáneos desde la misma wallet podían, antes de esta etapa, leer el mismo saldo, ambos validar "saldo suficiente" y ambos descontar, produciendo la misma pérdida de actualización descrita en 14.2. El QR en sí (`qr_comercios`) no forma parte de la carrera de saldo — su único riesgo de reutilización ya está descrito en 14.6 (no es un problema de concurrencia sino de ausencia de invalidación tras el pago).

**Corrección aplicada**: la lectura de `WalletSaldos` en `PagarQrAsync` ahora usa `FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {idWalletUsuario}")`, mismo patrón que 14.4. No requirió SQL/migración nueva — implementado directamente.

**No corregido en esta etapa** (fuera del guard de saldo): la posibilidad de que el mismo `codigoQr` genere múltiples `VentaQr` — eso depende de la decisión de producto de la sección 14.6, no de una carrera de concurrencia.

### 14.8 Concurrencia en Retiros

Confirmado en `ConfirmarRetiroPagadoAsync`/`RechazarRetiroAsync`: antes de esta etapa, ambos leían `RetirosComercio` con `FirstOrDefaultAsync` (sin lock) y validaban `Estado == "PENDIENTE"` en memoria — dos llamadas casi simultáneas sobre el **mismo** `idRetiro` (doble clic en "confirmar", o "confirmar" y "rechazar" casi al mismo tiempo) podían ambas leer `PENDIENTE` antes de que cualquiera confirmara su cambio de estado, y ambas pasar el guard.

**Transición atómica implementada**: ambos métodos ahora leen la fila con `FromSqlInterpolated($"SELECT * FROM retiros_comercio WITH (UPDLOCK, ROWLOCK) WHERE id_retiro = {request.IdRetiro}")` dentro de la transacción — la segunda llamada concurrente sobre el mismo `idRetiro` queda bloqueada hasta que la primera haga commit/rollback, y al desbloquearse relee el estado ya actualizado (`PAGADO`/`RECHAZADO`), por lo que el guard `Estado != "PENDIENTE"` la rechaza correctamente. No requirió SQL/migración nueva.

**No cubierto por este lock** (residual, ver sección 11): el lock protege el guard de estado de un mismo retiro, pero no la fila de `wallet_saldos` del comercio, que `SolicitarRetiroAsync`/`RechazarRetiroAsync` siguen leyendo/escribiendo sin lock — dos operaciones sobre **distintos** retiros de la **misma** wallet de comercio podrían competir por esa fila de saldo con el mismo mecanismo de 14.2. No estaba en el alcance explícito de esta sección (que pedía específicamente el guard `Estado == PENDIENTE`).

### 14.9 Logging

Se agregó `ILogger<T>` (inyectado por constructor, patrón estándar de ASP.NET Core, ya usado en `PagoQrService`) a `WalletsController`, `QrController` y `ComerciosController`, y se reemplazó el `catch { return StatusCode(500, ...) }` genérico (que no dejaba ningún rastro del error real) por `catch (Exception ex) { _logger.LogError(ex, "...", idsRelevantes); return StatusCode(500, ...); }` en:
- `WalletsController.RecargarManual` y `Transferir`.
- `QrController.Pagar`.
- `ComerciosController.ConfirmarPago` y `RechazarRetiro`.

En todos los casos el mensaje de log incluye únicamente identificadores numéricos ya conocidos por el propio flujo (`idWallet`, `idWalletOrigen`, `idRetiro`) — nunca tokens, documentos, números de cuenta ni el payload completo del request. `ex` se pasa al logger (queda en el log del servidor, no en la respuesta HTTP) para preservar el stack trace donde corresponde: en el log, no en el cliente. El mensaje devuelto al cliente sigue siendo el mismo texto genérico fijo que ya existía (`"Error interno..."`), sin cambios. `_audit.LogSensitiveAction` se mantiene intacto para los eventos de intento/éxito — el logging nuevo es un canal adicional, no un reemplazo.

**No se agregó** logging a los endpoints que no fueron tocados en esta etapa (`SolicitarRetiro`, `ListarRetiros`, `GetRetiro`, `LiquidarVentaQr`, `AdminController`, `ReportesController`) — fuera del alcance explícito de la sección 9, que pedía logging sobre las excepciones inesperadas de los flujos bajo auditoría (Transferencia, Pago QR, Retiros).

### 14.10 Frontend — verificación

- **Busy correctamente restaurado**: confirmado — `handleEnviar` y `handlePagarQr` restauran `envBusy`/`pagBusy` a `false` en su bloque `finally`, sin importar éxito o error.
- **Doble submit**: los botones de envío usan `disabled={envBusy || ...}`/`disabled={pagBusy || ...}` — protege el caso de doble clic dentro de la misma instancia del componente. No es idempotencia real (ver 14.5) — no protege contra reintentos de red ni múltiples pestañas.
- **Contrato nuevo**: confirmado que `handleEnviar` nunca envió `idWalletOrigen`/`creadoPor` (ya corregido en 71.2-E-C). `handlePagarQr` sí enviaba `idWalletUsuario`/`creadoPor` hasta esta etapa — **corregido ahora** para que coincida con el nuevo `PagoQrRequest` (`{ codigoQr, valor, descripcion }`).
- Se modificó únicamente `handlePagarQr` — el único cambio derivado directamente del nuevo contrato del backend. No se tocó UX, diseño ni ningún otro archivo.

### 14.11 Builds, `git diff --check` y matriz QA preparada

**Backend**: `dotnet build` → `Build succeeded. 0 Warning(s). 0 Error(s).`
**Frontend**: `npm run build` (`tsc && vite build`) → 0 errores TypeScript, build de producción exitoso (misma advertencia preexistente de tamaño de chunk, no relacionada).
**`git diff --check`**: sin errores de espacio en blanco.

**Matriz QA preparada para ejecución real (no ejecutada — sin credenciales/entorno QA en esta sesión):**

| Área | Caso | Qué debe verificar la prueba real |
|---|---|---|
| Transferencias | Válida | Débito origen, crédito destino, ledger balanceado, saldo final correcto |
| Transferencias | Saldo insuficiente | 400, ningún movimiento aplicado |
| Transferencias | Doble clic | Con el lock nuevo: dos transferencias válidas secuenciales (ninguna se "pierde"), no una sola aplicada dos veces exitosamente por error — **esto NO prueba idempotencia**, solo que la concurrencia no corrompe el saldo |
| Transferencias | Doble request (mismo idempotency-key hipotético) | No aplicable hasta implementar 14.5 — hoy ambos se procesan como transferencias independientes, resultado esperado: **dos débitos reales**, documentar como comportamiento actual, no como bug de esta etapa |
| Transferencias | Dos requests simultáneos (dos usuarios distintos, misma wallet destino) | Ambos créditos deben aplicarse correctamente sin pérdida, gracias al lock ordenado |
| Transferencias | Wallet origen alterada manualmente | Estructuralmente imposible — el campo no existe en el DTO |
| Transferencias | Wallet destino inválida | 400 ("La wallet destino no existe o no está activa") |
| Pago QR | Válido | Débito wallet pagadora, ledger balanceado, `VentaQr` creada |
| Pago QR | QR vencido/inactivo | 400 ("El QR no existe o no está activo") |
| Pago QR | QR duplicado (mismo QR, dos pagos intencionales) | Ambos deben aplicarse — comportamiento esperado si el QR es reusable por diseño (ver 14.6) |
| Pago QR | Replay (mismo request exacto reenviado) | Hoy: se procesa como un segundo pago válido — documentar como riesgo de duplicación (14.5), no como prueba "fallida" de esta etapa |
| Pago QR | Wallet ajena | 401/404 según el claim — estructuralmente ya no es posible especificar una wallet ajena |
| Pago QR | Doble pago simultáneo (misma wallet) | Con el lock nuevo: ambos se procesan correctamente en secuencia, sin pérdida de saldo |
| Recarga | ADMIN | 200 |
| Recarga | SUPERUSUARIO | 200 |
| Recarga | OPERADOR | 403 (no está en la lista de roles permitidos) |
| Recarga | COMERCIO | 403 |
| Recarga | USUARIO | 403 |
| Retiros | Confirmar dos veces | Con el lock nuevo: segunda llamada bloqueada hasta la primera, luego 400 (estado ya no PENDIENTE) |
| Retiros | Rechazar dos veces | Igual, 400 en la segunda |
| Retiros | Confirmar/rechazar simultáneamente (mismo retiro) | Una de las dos se serializa detrás de la otra por el lock, y falla — **actualizado en 71.2-E-E: 409, no 400** (ver sección 15.10) — al desbloquearse, ninguna corrupción de estado |

Ningún caso de esta matriz se declara "aprobado" — es la matriz preparada para ejecución real contra QA, no un resultado.

## 15. Fase 71.2-E-E — Cierre de integridad residual, logging y diseño final de idempotencia

### 15.1 Corrección sobre la incertidumbre de RCSI

**Hecho confirmado por código** (sin ambigüedad, verificable leyendo el repositorio):
- `BeginTransactionAsync()` en todos los servicios del proyecto se llama sin `IsolationLevel` explícito.
- Ningún script en `database/*.sql` ni en `Program.cs` contiene `ALTER DATABASE ... SET READ_COMMITTED_SNAPSHOT ON` (ni `OFF`), ni `ALLOW_SNAPSHOT_ISOLATION`.
- Antes de esta etapa, ninguna lectura de `wallet_saldos`/`retiros_comercio` en los métodos auditados usaba `UPDLOCK`.

**⚠️ Actualización (Fase 71.2-E-G.3) — pendiente cerrado:** el usuario ejecutó manualmente la consulta de solo lectura de la sección 15.2 contra la base real de QA (`sqldb-xpay-qa`). Resultado confirmado:

| Columna | Valor |
|---|---|
| `base_datos` | `sqldb-xpay-qa` |
| `is_read_committed_snapshot_on` | `True` |
| `snapshot_isolation_state_desc` | `ON` |

**RCSI está activo en QA**, confirmando la hipótesis planteada abajo (Azure SQL crea las bases nuevas con RCSI en `ON` por defecto). El razonamiento de "por qué esto no invalida la corrección aplicada" (párrafo siguiente) ya no es un caso hipotético a cubrir por si acaso — es el escenario real y confirmado de QA. El texto original de esta subsección (que sigue abajo, sin editar, como registro histórico de lo que estaba confirmado por código vs. lo que no) queda complementado por este resultado real.

**Lo que seguía sin confirmar antes de esta actualización** (ahora resuelto para QA específicamente — no necesariamente para producción, si existe un entorno separado no verificado):
- El valor real de `is_read_committed_snapshot_on` en la base de QA — **ahora confirmado: `True`**.
- El valor real de `snapshot_isolation_state_desc` — **ahora confirmado: `ON`**.
- **Dato relevante que la sección 14.2 original pasó por alto:** Azure SQL Database **crea las bases nuevas con `READ_COMMITTED_SNAPSHOT` en `ON` por defecto** — a diferencia de SQL Server on-premises (default `OFF`). El resultado real de QA confirma que este proyecto sigue ese default de la plataforma (nadie lo desactivó explícitamente).

**Por qué esto no invalida la corrección aplicada:** `UPDLOCK` es un hint de bloqueo **explícito** que fuerza un lock de actualización real sobre la fila leída, sin importar si la sesión está bajo RCSI o no. RCSI cambia el comportamiento de las lecturas **planas** (un `SELECT` sin hints deja de tomar locks y en su lugar lee una versión consistente de la fila mediante versionado de filas en tempdb, sin bloquear escritores) — pero un `SELECT ... WITH (UPDLOCK, ROWLOCK)` sigue tomando y reteniendo el lock de actualización explícitamente solicitado hasta el commit/rollback, **incluso con RCSI activo**. Es decir: la protección aplicada en 71.2-E-D/71.2-E-E es correcta y necesaria en ambos escenarios (RCSI ON o OFF) — no depende de cuál resulte ser el real. Lo único que cambiaba según RCSI era **el diagnóstico exacto de cómo se producía la carrera antes de la corrección** (bajo RCSI, dos lecturas planas ven versiones consistentes sin bloquearse por versionado en vez de por liberación rápida de locks S — el resultado final observable, la actualización perdida, es el mismo mecanismo de fondo: falta de lock de escritura anticipado).

### 15.2 Consulta de verificación de RCSI — ejecutada por el usuario en 71.2-E-G.3

```sql
SELECT
    name,
    is_read_committed_snapshot_on,
    snapshot_isolation_state_desc
FROM sys.databases
WHERE name = DB_NAME();
```

Preparada en esta etapa (71.2-E-E) para que el usuario la ejecutara manualmente contra la base real — **ejecutada por el usuario en la Fase 71.2-E-G.3**, de solo lectura, directamente contra `sqldb-xpay-qa`. Resultado: `is_read_committed_snapshot_on = True`, `snapshot_isolation_state_desc = ON`. Ver el resultado completo y su interpretación en la sección 15.1 (actualización) y en la nueva sección 17.24.

### 15.3 Aclaración sobre UPDLOCK y ROWLOCK

Corrección de comentarios/documentación previos que podían leerse como si `ROWLOCK` por sí solo garantizara un bloqueo de fila:
- **`UPDLOCK`** es el elemento que realmente importa para la corrección: es lo que hace que el lock de actualización se **retenga hasta el fin de la transacción** (commit/rollback), en vez de liberarse inmediatamente como un lock compartido normal bajo READ COMMITTED. Es la pieza que serializa el ciclo leer-validar-escribir entre transacciones concurrentes.
- **`ROWLOCK`** es un **hint**, no una garantía absoluta: le pide al motor que aplique el lock a nivel de fila en lugar de página/tabla. SQL Server generalmente lo honra, pero bajo presión de memoria o en ciertos planes de ejecución puede escalar el lock a un nivel más amplio (page/table lock escalation) — el hint reduce la probabilidad de contención innecesaria sobre filas no relacionadas, pero no es una promesa incondicional de granularidad de fila en el 100% de los casos.
- Los comentarios en `WalletOperacionService.cs` y `RetiroComercioService.cs` se actualizaron en esta etapa para reflejar esta distinción (ver 15.3 en el código: comentario de `TransferirWalletAsync`).

### 15.4 Aclaración sobre deadlocks

Corregido el comentario de `TransferirWalletAsync` que afirmaba que el orden ascendente de `IdWallet` hacía que dos transferencias cruzadas "no puedan deadlockearse" — reformulado a: el orden ascendente **reduce el riesgo** de deadlock específicamente entre dos transferencias que compiten por las mismas dos filas de `wallet_saldos` en sentido opuesto (A→B y B→A simultáneas), pero **no elimina todos los deadlocks posibles del sistema**: dentro de la misma transacción también se escriben `ledger_transacciones`, `ledger_movimientos`, `wallet_movimientos` y `auditoria`, cuyos propios índices (claves primarias autoincrementales, índices en `id_transaccion_ledger`, etc.) pueden en teoría participar en un deadlock no relacionado con el orden de `wallet_saldos` — un caso de baja probabilidad dado que esas inserciones son mayormente append-only sin contención de lectura-modificación-escritura, pero no descartable con una garantía absoluta. No se retiró el orden determinístico implementado — sigue siendo la mitigación correcta para el caso que sí cubre.

### 15.5 Logging de `mi-wallet`

`WalletsController.ObtenerMiWallet`: se agregó `catch (Exception ex) { _logger.LogError(ex, "Error interno consultando mi-wallet para idPersona {IdPersona}.", idPersona); ... }`, distinguiendo: claim inválido → `401` sin log (no es una falla del sistema); wallet inexistente → `404` sin log (resultado de negocio esperado); solo la excepción inesperada se registra. El mensaje de log solo incluye `idPersona` (identificador técnico ya disponible en el flujo) — nunca JWT completo, documentos, nombres, correos ni teléfonos.

### 15.6 Logging de `mi-estado-cuenta`

`ReportesController.MiEstadoCuenta`: mismo patrón — `catch (InvalidOperationException ex)` (regla de negocio esperada) → `400` sin log; `catch (Exception ex)` → `LogError(ex, "...", idPersona)` + `500` genérico. Claim inválido → `401` sin log; wallet inexistente → `404` sin log.

### 15.7 Auditoría y corrección — concurrencia en Recarga Manual

**Auditoría**: confirmado que `RecargarWalletManualAsync` hacía exactamente `leer WalletSaldo (FirstOrDefaultAsync, sin lock) → sumar en memoria → SaveChangesAsync()`, mismo patrón de riesgo ya corregido en `TransferirWalletAsync`/`PagarQrAsync` — dos recargas concurrentes sobre la misma wallet podían perder una actualización.

**Corrección aplicada**: la lectura de `WalletSaldos` ahora usa `FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {idWallet}")`, dentro de la transacción ya existente, antes de calcular `saldoDespues`. Se conservó sin cambios: restricción `[Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]`, lógica de ledger/movimientos/auditoría, verificación de balance, mensajes de error existentes, atomicidad de la transacción. No se amplió ningún permiso.

### 15.8 Auditoría y corrección — concurrencia de `wallet_saldos` en Retiros

**Escenarios analizados** (sobre `SolicitarRetiroAsync`/`RechazarRetiroAsync`, los dos métodos que leen+escriben `wallet_saldos` del comercio):

| Escenario | Antes de esta etapa | Después de esta etapa |
|---|---|---|
| A) Dos `SolicitarRetiroAsync` simultáneos, misma wallet | Sin lock — riesgo de actualización perdida sobre `SaldoDisponible` | Serializado por `UPDLOCK/ROWLOCK` sobre `wallet_saldos` |
| B) `SolicitarRetiroAsync` y `RechazarRetiroAsync` simultáneos, misma wallet | Sin lock — mismo riesgo | Serializado — ambos toman el mismo lock sobre la misma fila |
| C) Dos `RechazarRetiroAsync` de retiros distintos, misma wallet | Sin lock sobre `wallet_saldos` (el lock agregado en 71.2-E-D solo cubre `retiros_comercio`, filas distintas en este escenario, sin conflicto entre sí) | Serializado — ambos compiten por el mismo lock de `wallet_saldos` tras adquirir (sin conflicto) sus respectivos locks de `retiros_comercio` |
| D) Retiro y otra operación financiera sobre la misma wallet de comercio (p. ej. una liquidación) | Depende del otro método — no auditado en este alcance si no usa el mismo patrón | Cualquier método que ya use `WITH (UPDLOCK, ROWLOCK)` sobre `wallet_saldos` queda serializado correctamente contra los de esta etapa; los que no lo usen (fuera del alcance de esta etapa) conservan el riesgo — ver riesgos residuales |

**Corrección aplicada**: `SolicitarRetiroAsync` y `RechazarRetiroAsync` ahora leen `wallet_saldos` del comercio con `FromSqlInterpolated(... WITH (UPDLOCK, ROWLOCK) ...)` dentro de sus transacciones existentes, antes de calcular el nuevo saldo. Se conservó sin cambios: ownership vía `ComercioScopeService`, roles por endpoint, ledger, auditoría, transacción explícita con rollback, balance débito=crédito, `idUsuario` forzado desde el claim.

**Orden de adquisición de locks** (ver 15.9).

**No se afirma que se eliminan todos los deadlocks** — ver 15.4.

### 15.9 Orden de adquisición de locks

- `ConfirmarRetiroPagadoAsync`: adquiere **únicamente** el lock sobre `retiros_comercio` — no toca `wallet_saldos` (la confirmación de pago solo mueve cuentas ledger entre "pendientes" y "bóveda", no descuenta la wallet del comercio, que ya se descontó en la solicitud).
- `RechazarRetiroAsync`: adquiere el lock sobre `retiros_comercio` **primero** (ya existente desde 71.2-E-D), y el lock sobre `wallet_saldos` **después** (agregado en esta etapa).
- `SolicitarRetiroAsync`: adquiere **únicamente** el lock sobre `wallet_saldos` — no bloquea ninguna fila existente de `retiros_comercio` porque el retiro se inserta como fila nueva (sin contención posible sobre una fila que no existe todavía).

**Por qué este orden es consistente y no genera un ciclo de espera entre estos tres métodos**: un deadlock requiere que dos transacciones cada una sostenga un lock que la otra necesita. Ningún método aquí adquiere `wallet_saldos` y luego intenta adquirir una fila *ya existente* de `retiros_comercio` — el único método que adquiere dos locks (`RechazarRetiroAsync`) siempre lo hace en el mismo orden relativo (`retiros_comercio` → `wallet_saldos`), y los otros dos métodos solo adquieren uno de los dos recursos. Mientras ningún método futuro invierta este orden (bloquear primero `wallet_saldos` y luego una fila ya existente de `retiros_comercio`), no puede formarse un ciclo entre estos métodos. Esto no cubre otros métodos fuera del alcance de esta etapa que pudieran tocar ambas tablas en un orden distinto — ver riesgos residuales.

### 15.10 Revisión de códigos HTTP

Clasificación según la tabla de la instrucción (400/401/403/404/409/500), aplicada a los 7 endpoints en alcance:

| Endpoint | Caso | Código antes | Código después | Cambiado |
|---|---|---|---|---|
| Transferencia, Recarga, Pago QR, mi-wallet, mi-estado-cuenta | Claim JWT ausente/inválido | 401 | 401 | No |
| Todos con `[Authorize(Roles=...)]` | Rol insuficiente | 403 (pipeline ASP.NET Core) | 403 | No |
| Confirmar/Rechazar retiro | Retiro no está en estado PENDIENTE (ya PAGADO, ya RECHAZADO) | 400 | **409** | **Sí — implementado** (nueva excepción tipada `TransicionRetiroInvalidaException`, ver abajo) |
| Todos | Excepción inesperada | 500 (sin log) | 500 (con `LogError`) | Log agregado, código sin cambio |
| Transferencia/Recarga/Pago QR/Retiros | "No existe"/"no está activa" (wallet, QR, comercio, tienda, retiro) | 400 (`InvalidOperationException` genérica) | **400 (sin cambio — ver nota)** | No implementado |
| Transferencia/Recarga/Pago QR/Retiros | Reglas de negocio (saldo insuficiente, valor ≤ 0, tipo de wallet incorrecto, etc.) | 400 | 400 | No |

**Implementado**: el caso más claro y explícitamente señalado por la instrucción — "retiro ya PAGADO", "retiro ya RECHAZADO", "transición desde un estado distinto de PENDIENTE" — ahora es `409 Conflict`. Se creó `Exceptions/TransicionRetiroInvalidaException.cs` (hereda de `InvalidOperationException`, mismo patrón ya usado en el proyecto por `TransicionCajaInvalidaException` de la Fase 70.4), lanzada específicamente en el guard `if (retiro.Estado != "PENDIENTE")` de `ConfirmarRetiroPagadoAsync`/`RechazarRetiroAsync`, capturada en `ComerciosController` **antes** del `catch (InvalidOperationException ex)` genérico (una excepción más específica debe capturarse primero en C#) y mapeada a `Conflict()`.

**No implementado — propuesta mínima documentada, sin ampliar alcance**: reclasificar los mensajes "no existe"/"no está activa" de `400` a `404` en `TransferirWalletAsync`, `PagarQrAsync`, `RecargarWalletManualAsync` y `SolicitarRetiroAsync`/`GetRetiroByIdAsync` requeriría, para hacerse correctamente (sin adivinar por el texto del mensaje, que es frágil), introducir un segundo tipo de excepción dedicado (p. ej. `RecursoNoEncontradoException : InvalidOperationException`, mismo patrón que `TransicionRetiroInvalidaException`) y reemplazar cada `throw new InvalidOperationException("... no existe ...")` relevante en 4 servicios distintos — una decena de sitios de código, todos con el mismo patrón mecánico pero fuera del alcance explícito de esta etapa (que nombró específicamente el caso PENDIENTE de retiros). Se documenta como propuesta lista para autorizar en una etapa futura, no se implementa ahora. Tampoco se evaluó el caso "QR dinámico ya pagado" para 409 — depende de la decisión de producto pendiente (sección 15.11/15.12), no aplicable al modelo actual (QR fijo, ver abajo).

### 15.11 Decisión de producto sobre QR — no modificada

No se tocó el comportamiento de reutilización del QR. Se documentan los dos modelos posibles tal como pide la instrucción, sin asumir cuál es el correcto.

### 15.12 Comparación QR fijo vs. QR dinámico — a cuál se parece el código actual

Inspeccionado `Models/QrComercio.cs`: campos `IdQr, IdComercio, IdTienda, CodigoQr, Estado ("ACTIVO" por defecto), FechaCreacion`. **No existen** campos de `Valor`/`ValorEsperado`, `FechaVencimiento`, ni ningún estado más allá de un `Estado` string que nunca se transiciona en ningún lugar del código (confirmado: `PagoQrService.PagarQrAsync` nunca escribe `qr.Estado`). No existe relación 1:1 entre `QrComercio` y `VentaQr` — un mismo `CodigoQr` puede tener N filas `VentaQr` sin ninguna restricción de unicidad. El valor del pago siempre lo determina el pagador (`request.Valor`), nunca el QR.

**Esto coincide estructuralmente y por completo con MODELO A (QR fijo de comercio)**: reutilizable, monto abierto ingresado por el pagador, no se consume tras el pago, puede recibir múltiples pagos, permanece `ACTIVO` sin lógica de expiración. No hay **ningún** rastro de código relacionado con MODELO B (sin campo de valor fijo, sin vencimiento, sin máquina de estados PENDIENTE/PAGADO/VENCIDO/CANCELADO, sin relación única con una venta) — no parece un modelo B incompleto o a medio implementar, sino la ausencia total de esa funcionalidad.

**Recomendación de producto (no decisión — a la espera de la del usuario):** el código actual fue diseñado, de forma consistente y completa, como MODELO A. Si el comportamiento observado en 71.2-E-D ("el mismo QR puede pagarse un número ilimitado de veces") es indeseado, **no es un bug de la implementación actual** — sería un cambio de alcance de producto hacia MODELO B, que requeriría diseñar e implementar desde cero los campos/estados/relación única que hoy no existen (fuera del alcance de esta etapa: "No crear columnas, tablas ni migraciones para QR dinámico"). Si el comportamiento actual es el esperado (QR físico de mostrador, pagos múltiples e independientes), no se requiere ningún cambio.

### 15.13 Diseño final de idempotencia — ⚠️ REEMPLAZADO por la sección 16 (Fase 71.2-E-F)

**Este diseño (basado en dos transacciones separadas) quedó corregido y reemplazado en 71.2-E-F — ver sección 16.4/16.5.** Se conserva aquí sin editar, únicamente como registro histórico de cómo evolucionó el análisis; **no debe usarse como referencia para implementar nada** — la premisa central de esta sección 15.13 (que una reserva dentro de la misma transacción financiera "no protege contra un segundo request idéntico que llega mientras el primero todavía está en curso") era **incorrecta**: confundía la visibilidad de una fila para un `SELECT` bajo READ COMMITTED con el comportamiento de bloqueo de un `INSERT` concurrente sobre una clave de índice único, que sí serializa correctamente ambas solicitudes incluso dentro de una única transacción. Ver la explicación completa en la sección 16.2.

<details>
<summary>Contenido original de 71.2-E-E (histórico, no vigente)</summary>

### 15.13 Diseño final de idempotencia (histórico)

**Corrección sobre el diseño de 71.2-E-D**: aquel diseño reservaba la clave de idempotencia dentro de la **misma** transacción financiera, insertando la fila de idempotencia junto con el resto de las operaciones y confirmando todo junto al final. Ese diseño tiene un defecto: si la reserva solo es visible (committeada) al final, junto con el resultado, **no protege contra un segundo request idéntico que llega mientras el primero todavía está en curso** — ambos verían la clave como "no reservada todavía" y ambos procesarían la operación completa. La instrucción de esta etapa es correcta al señalar que la clave debe reservarse **al inicio**, de forma durable, antes de iniciar la operación financiera.

**Diseño corregido — dos confirmaciones separadas (no una sola transacción):**

1. **Transacción 1 (corta, se confirma inmediatamente):** `INSERT` de la fila de idempotencia con estado `EN_PROCESO` y `COMMIT` inmediato — antes de tocar cualquier saldo o ledger.
   - Si el `INSERT` falla por violación de la restricción única (ver más abajo), la operación financiera **no se inicia** — se responde según el estado de la fila existente.
2. **Transacción 2 (la operación financiera real, sin cambios respecto al patrón ya implementado):** el `BeginTransactionAsync()` ya existente — locks `UPDLOCK/ROWLOCK`, ledger, movimientos, auditoría, balance débito=crédito.
3. Justo antes del `CommitAsync()` de la transacción 2, `UPDATE` de la misma fila de idempotencia a `COMPLETADA`, con `id_recurso` y `respuesta_json` — como parte de la misma transacción 2, así que se confirma o se revierte junto con la operación financiera.
4. Si la transacción 2 falla (excepción, rollback): la fila de idempotencia queda en `EN_PROCESO` de la transacción 1 (que ya se confirmó por separado). Se elimina esa fila en una tercera operación corta e independiente al capturar la excepción, liberando la clave para un reintento legítimo — **no se diseñó un estado `FALLIDA` persistente**: un intento fallido no tiene un "resultado" que valga la pena cachear (a diferencia de uno exitoso), y mantener la fila solo agregaría una tabla de intentos fallidos sin valor de negocio claro para este caso de uso; si en el futuro se necesita auditar intentos fallidos por separado, se puede reconsiderar.
5. **Rows `EN_PROCESO` huérfanas** (el proceso murió entre el paso 1 y el paso 4, p. ej. por un crash del servidor): se definen como abandonadas si `fecha_creacion` supera un umbral (propuesto: 5 minutos, mayor que cualquier operación financiera legítima) — un request posterior con la misma clave que encuentre una fila `EN_PROCESO` vencida la trata como si no existiera (la reemplaza) en vez de bloquear indefinidamente.

**Estados**: `EN_PROCESO`, `COMPLETADA` (sin `FALLIDA` persistente, ver punto 4).

**Flujo completo** (transferencia, como ejemplo):
1. Cliente envía `POST /api/wallets/transferencia` con header `Idempotency-Key: <uuid>`.
2. Controller calcula `request_hash = SHA256(idWalletDestino|valor|descripcion normalizado)`.
3. Intenta `INSERT` en `wallet_idempotencia` con `(idUsuario, endpoint, idempotency_key, request_hash, estado='EN_PROCESO', fecha_creacion=now, fecha_expiracion=now+24h)`, commit inmediato.
4. Si el `INSERT` tiene éxito → continúa al paso 5.
5. Si el `INSERT` falla por clave duplicada → lee la fila existente:
   - `estado == COMPLETADA` y `request_hash` coincide → devuelve `respuesta_json` cacheada con su código de estado original (200/400/etc., normalmente 200).
   - `estado == COMPLETADA` y `request_hash` **no** coincide → `409` ("La misma clave de idempotencia ya se usó para una operación distinta").
   - `estado == EN_PROCESO` y no vencida → `409` ("Operación en curso, reintente en unos segundos").
   - `estado == EN_PROCESO` y vencida (> umbral) → se trata como si no existiera; se reintenta el `INSERT` (reemplazando la fila abandonada).
6. Ejecuta la operación financiera real (transacción 2, sin cambios respecto al patrón actual).
7. Antes del commit de la transacción 2, guarda el resultado (`id_recurso`, `respuesta_json`) y marca `COMPLETADA`.
8. Si la transacción 2 falla, elimina la fila `EN_PROCESO` (paso 4 del diseño arriba) y propaga el error normalmente (400/500 según corresponda) — sin fila de idempotencia persistida, un reintento con la misma clave simplemente vuelve a intentar desde cero.

**Unicidad — opción recomendada: `idUsuario + endpoint + idempotency_key`** (no clave global sola, no clave+endpoint sola). Justificación: una clave global sola confía enteramente en que el cliente nunca genere una colisión (accidental o deliberada) entre usuarios distintos; clave+endpoint reduce el riesgo de colisión entre operaciones de distinto tipo pero sigue sin acotar el radio de impacto entre usuarios — si dos usuarios distintos llegaran a coincidir en la misma clave (bug del cliente, o un intento deliberado de adivinar/reutilizar la clave de otro), uno podría recibir la `respuesta_json` cacheada de la transferencia de otro usuario. Acotando también por `idUsuario`, el peor caso de una colisión de clave queda limitado a negar (con 409) una segunda operación legítima del **mismo** usuario — nunca una fuga de datos entre usuarios distintos. Es el mismo criterio que usan APIs financieras de referencia (p. ej. claves de idempotencia scoped por cuenta/API key).

### 15.14 SQL propuesto (no ejecutado, no implementado)

```sql
CREATE TABLE wallet_idempotencia (
    id_idempotencia   BIGINT IDENTITY PRIMARY KEY,
    id_usuario        BIGINT           NOT NULL,
    endpoint          VARCHAR(100)     NOT NULL,       -- p.ej. 'wallets/transferencia', 'qr/pagar'
    idempotency_key   UNIQUEIDENTIFIER NOT NULL,
    request_hash      VARBINARY(32)    NOT NULL,        -- SHA-256 del payload normalizado
    estado            VARCHAR(20)      NOT NULL DEFAULT 'EN_PROCESO',  -- EN_PROCESO | COMPLETADA
    id_recurso        BIGINT           NULL,             -- p.ej. id_transaccion_ledger resultante
    respuesta_json    NVARCHAR(MAX)    NULL,
    fecha_creacion    DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
    fecha_completado  DATETIME2        NULL,
    fecha_expiracion  DATETIME2        NOT NULL,         -- fecha_creacion + política de retención
    CONSTRAINT uq_wallet_idempotencia UNIQUE (id_usuario, endpoint, idempotency_key)
);

CREATE INDEX ix_wallet_idempotencia_limpieza
    ON wallet_idempotencia (fecha_expiracion)
    WHERE estado = 'EN_PROCESO';
```

**Cambios de DTO/header**: **no** se agrega ningún campo a `TransferenciaWalletRequest`/`PagoQrRequest` — la clave viaja en un header `Idempotency-Key: <uuid>` (convención estándar de la industria, p. ej. Stripe/PayPal), separando el concern de idempotencia del contrato de negocio, que este proyecto ha mantenido deliberadamente mínimo (solo destino/valor/descripción) en 71.2-E-C/D.

**Cambios del cliente** (`UserWalletPage.tsx`, no implementados todavía — a la espera de aprobación): generar `crypto.randomUUID()` una única vez por intento (al abrir el formulario o construir el primer request), reenviar la **misma** clave en cualquier reintento automático del mismo intento, y solo generar una clave nueva cuando el usuario inicia explícitamente una operación nueva (p. ej. tras `resetEnviar()`).

**Manejo de violación única**: capturar la excepción específica de SQL Server (`SqlException` con `Number == 2627`, violación de restricción única) al hacer el `INSERT` de la transacción 1; en respuesta, `SELECT` la fila existente y aplicar la lógica de branching del paso 5 del flujo (15.13).

**Limpieza/retención**: `fecha_expiracion` propuesta en `fecha_creacion + 24 horas` para filas `COMPLETADA` (tiempo suficiente para cubrir reintentos de red razonables, sin conservar indefinidamente); filas `EN_PROCESO` se consideran abandonadas a los 5 minutos (ver 15.13, punto 5). La limpieza física (borrado de filas vencidas) puede hacerse con un job periódico o de forma perezosa (el propio request que encuentra una fila vencida la reemplaza) — no se diseñó un mecanismo de limpieza automática programada en esta etapa, por no requerirlo la instrucción explícitamente.

**Seguridad frente a reutilización de una clave con payload diferente**: el `request_hash` (SHA-256 de una serialización normalizada de los campos relevantes del request) se compara en el paso 5 del flujo — si la clave ya existe con un hash distinto, se rechaza con `409` en vez de (a) devolver por error el resultado cacheado de una operación distinta, o (b) procesar silenciosamente el nuevo payload ignorando la protección de idempotencia.

**NO se ejecutó SQL. NO se creó ninguna migración. NO se implementó la tabla ni ningún código de idempotencia** — el diseño queda a la espera de aprobación explícita del diseño y del SQL antes de iniciar cualquier implementación. **[Fin del contenido histórico de 71.2-E-E — reemplazado, ver sección 16.]**

</details>

### 15.15 Builds

**Backend**: `dotnet build` → `Build succeeded. 0 Warning(s). 0 Error(s).`
**Frontend**: no se modificó ningún archivo de `frontend/` en esta etapa — `npm run build` no se ejecutó (no aplica, por instrucción explícita: "npm run build solo si se modifica frontend").
**`git diff --check`**: sin errores de espacio en blanco.

### 15.16 Pruebas realmente ejecutadas

Ninguna contra QA en vivo — sin credenciales/entorno de base de datos en esta sesión, consistente con toda la fase 71.2-E. Lo único "ejecutado" en esta etapa fue `dotnet build` y `git diff --check`, ambos exitosos.

### 15.17 Pruebas pendientes

Toda la matriz QA de 71.2-E-D (sección 14.11) sigue pendiente de ejecución real, más los casos nuevos de esta etapa: recarga manual concurrente (dos recargas simultáneas sobre la misma wallet — verificar que ninguna se pierda), retiro rechazado/solicitado concurrente sobre la misma wallet de comercio, y verificación de los nuevos códigos `409` en confirmar/rechazar retiro con estado no-PENDIENTE. La consulta de la sección 15.2 también queda pendiente de ejecución manual por el usuario contra QA real.

### 15.18 Riesgos residuales (actualizados)

- ~~`WalletOperacionService.RecargarWalletManualAsync` sin lock~~ — **corregido en 71.2-E-E**.
- ~~`RetiroComercioService.SolicitarRetiroAsync`/`RechazarRetiroAsync` sin lock sobre `wallet_saldos`~~ — **corregido en 71.2-E-E**.
- **Reclasificación 400→404 de mensajes "no existe"** en Transferencia/Pago QR/Recarga/Retiros — documentada como propuesta mínima (15.10), no implementada.
- **Idempotencia — diseño finalizado, no implementado**: ningún endpoint tiene protección real contra duplicación/replay todavía. Ver 15.13/15.14, a la espera de aprobación de diseño + SQL.
- **QR de comercio reutilizable** — comportamiento confirmado como coincidente con Modelo A (diseño, no defecto) — a la espera de decisión de producto explícita (15.11/15.12).
- ~~RCSI real de la base — desconocido hasta que se ejecute la consulta de 15.2 contra QA~~ — **cerrado en 71.2-E-G.3**: confirmado `ON` en `sqldb-xpay-qa`. La protección `UPDLOCK` aplicada era y sigue siendo válida en ambos escenarios (15.1).
- **Otros métodos financieros fuera del alcance de 71.2-E-B/C/D/E** que pudieran tocar `wallet_saldos` sin lock (p. ej. `PagoQrService`/`CarteraOrdinariaService` en flujos no auditados en esta cadena de etapas) no fueron revisados de nuevo en esta etapa — el patrón UPDLOCK/ROWLOCK ya está aplicado en los que sí se auditaron (Transferencia, Pago QR, Recarga Manual, Retiros).
- **Deadlocks no relacionados con el orden de `wallet_saldos`/`retiros_comercio`** (índices de ledger/movimientos/auditoría) — posibles en teoría, no descartables con garantía absoluta (15.4).
- Sin ejecución contra QA real para nada de lo anterior (15.16/15.17).

## 16. Fase 71.2-E-F — Corrección del diseño de idempotencia

**Etapa de diseño y documentación únicamente. No se modificó ningún archivo de código (`.cs`/`.tsx`) en esta etapa.**

### 16.1 Error identificado en el diseño de dos transacciones (71.2-E-E, sección 15.13/15.14)

El diseño anterior afirmaba: *"si la reserva solo es visible (committeada) al final, junto con el resultado, no protege contra un segundo request idéntico que llega mientras el primero todavía está en curso"* — y por eso proponía confirmar la reserva `EN_PROCESO` en una transacción corta e independiente, **antes** de iniciar la operación financiera.

**Esa afirmación es incorrecta.** Confunde dos mecanismos distintos de SQL Server:
1. **Visibilidad de lectura bajo READ COMMITTED** (un `SELECT` plano de otra transacción no ve filas no confirmadas — esto es cierto, pero irrelevante aquí).
2. **Bloqueo de escritura sobre una clave de índice único** (lo que realmente ocurre cuando dos transacciones intentan `INSERT` la misma clave — esto **sí** serializa correctamente a las dos transacciones, sin necesitar que la primera haga commit por separado).

La sección 16.2 explica el mecanismo exacto. La conclusión práctica: **una sola transacción, con el `INSERT` de la reserva como primer paso, es suficiente para serializar correctamente dos solicitudes concurrentes** — no se necesitan dos transacciones separadas.

### 16.2 Comportamiento de la restricción UNIQUE ante `INSERT` concurrentes

Cuando dos transacciones (Tx1, Tx2) ejecutan, de forma concurrente, `INSERT INTO wallet_idempotencia (...) VALUES (...)` con la misma combinación de columnas protegida por `UNIQUE (id_usuario, endpoint, idempotency_key)`:

1. **Qué locks se toman**: al insertar una fila nueva, SQL Server debe verificar la unicidad de la clave e insertar una nueva entrada en el índice único subyacente a la restricción. Para eso adquiere un **lock exclusivo (X) sobre la clave del índice** que se está insertando. Este lock, como todo lock de escritura, **se retiene hasta que la transacción termina** (commit o rollback) — no se libera anticipadamente como un lock de lectura bajo READ COMMITTED.
2. **Qué ocurre con la segunda transacción**: Tx2, al intentar su propio `INSERT` con la misma clave mientras Tx1 todavía no ha terminado, necesita el mismo lock X sobre esa clave del índice — que Tx1 ya tiene. **Tx2 queda bloqueada** (esperando), no falla inmediatamente ni procede en paralelo. Este bloqueo ocurre **sin importar el nivel de aislamiento** (READ COMMITTED, RCSI, SERIALIZABLE) porque es un conflicto de escritura-escritura, no de lectura — RCSI cambia cómo se comportan las lecturas planas, no cómo se serializan dos escrituras que compiten por la misma clave.
3. **Qué ocurre después del commit de la primera**: el lock X de Tx1 se libera. Tx2, que estaba bloqueada, se reactiva e intenta completar su propio `INSERT` — pero ahora encuentra que la clave **ya existe** (la fila de Tx1 quedó confirmada y visible). La restricción única rechaza la inserción duplicada.
4. **Qué ocurre después del rollback de la primera**: el lock X de Tx1 se libera, pero la fila que Tx1 había insertado **desaparece** (el rollback la deshace por completo, como cualquier otra operación de la transacción). Tx2, reactivada, encuentra que la clave **no existe** — su propio `INSERT` se completa con éxito, sin ningún error, exactamente como si hubiera sido la única solicitud. Tx2 no necesita "reintentar" nada — simplemente continúa su flujo normal.
5. **Cuándo aparece 2601 vs. 2627**: **2627** es la violación de una restricción con nombre creada mediante `CONSTRAINT ... UNIQUE` (como `uq_wallet_idempotencia` en el diseño propuesto, sección 16.10) — es el código que se debe esperar aquí. **2601** aplica a un índice único creado directamente con `CREATE UNIQUE INDEX` sin pasar por una restricción con nombre — no es el caso de este diseño, pero el manejo de errores (16.11) debe contemplar ambos por robustez, ya que el comportamiento de bloqueo/conflicto es idéntico en ambos casos, solo cambia el número de error según cómo se haya declarado la unicidad.
6. **Por qué la restricción UNIQUE puede arbitrar solicitudes concurrentes aunque la primera inserción no esté aún confirmada**: porque el arbitraje no depende de que la fila sea *visible* para una lectura — depende de que el **lock de escritura sobre la clave del índice** sea exclusivo y se mantenga hasta el final de la transacción. Es el mismo mecanismo (lock de escritura retenido hasta commit/rollback) que ya se usa y se documentó para `UPDLOCK` sobre `wallet_saldos`/`retiros_comercio` en 71.2-E-D/E — aquí no se necesita un hint explícito como `UPDLOCK` porque el propio motor de restricciones únicas ya se comporta de esa manera para cualquier `INSERT`.

### 16.3 Comparación formal: una transacción vs. dos transacciones

| Criterio | Diseño A — una transacción | Diseño B — dos transacciones |
|---|---|---|
| Atomicidad | Total: reserva + operación + resultado se confirman o revierten juntos, siempre | Parcial: la reserva se confirma por separado; puede quedar confirmada sin que la operación financiera se complete nunca |
| Protección ante solicitudes simultáneas | Sí — el `INSERT` duplicado se bloquea y se resuelve de forma determinística al terminar la primera transacción (16.2) | Sí, mediante el mismo mecanismo, pero solo para la reserva — no aporta nada adicional |
| Protección ante reintentos posteriores (no concurrentes) | Sí — la clave ya existe como `COMPLETADA`, se detecta y se devuelve el resultado cacheado | Igual |
| Bloqueo por índice UNIQUE | Es el único mecanismo de serialización necesario | Se usa igual para la reserva, pero ya no protege nada en la segunda transacción (el commit financiero no compite por la clave única) |
| Crash entre pasos | Sin filas huérfanas — cualquier crash antes del commit final deja la transacción sin confirmar, se revierte sola | Puede dejar la reserva `EN_PROCESO` confirmada y huérfana si el crash ocurre entre el commit de la reserva y el commit financiero |
| Registros EN_PROCESO huérfanos | No se generan por diseño | Posibles, requieren mitigación explícita |
| Limpieza | Solo retención normal de filas `COMPLETADA` antiguas | Requiere limpieza/expiración activa de filas `EN_PROCESO` abandonadas |
| Recuperación | No se necesita ningún proceso de recuperación | Se necesita un mecanismo de expiración/recuperación para `EN_PROCESO` abandonados |
| Complejidad | Baja — un solo bloque transaccional, mismo patrón que el resto del proyecto | Media-alta — dos ciclos de transacción, manejo de estados intermedios |
| Riesgo de operación financiera confirmada sin idempotencia COMPLETADA | Imposible — están en el mismo commit atómico | Bajo pero no nulo si el `UPDATE` a `COMPLETADA` ocurre en la misma transacción financiera (mitigado), pero el riesgo de la reserva huérfana persiste igual |
| Riesgo de borrar una reserva aún válida | No aplica — no hay borrado manual, todo es rollback atómico del motor | Sí — la limpieza de una reserva "fallida" es una operación separada con su propia ventana de carrera frente a un tercer request que la lea justo antes de que se borre |
| Multi-instancia del backend | Funciona igual — la serialización ocurre en la base de datos, no en memoria del proceso | Igual |
| Compatibilidad con EF Core/Azure SQL | Total, y es el único patrón transaccional que ya usa el resto del proyecto (`BeginTransactionAsync`/commit/rollback por operación) | Total, pero introduce el único caso del proyecto con dos transacciones separadas para una operación lógica — rompe la convención uniforme |
| Facilidad de pruebas | Alta — un solo camino transaccional, comportamiento determinístico en rollback | Media — requiere probar también reserva-confirmada-sin-operación, limpieza y expiración |
| Cantidad de transacciones por request | 1 (más una lectura de branching en el camino de colisión) | 2-3 (reserva, financiera, limpieza condicional) |
| Necesidad de procesos de recuperación | Ninguno | Sí — job de limpieza o expiración perezosa |

**Diseño elegido: A (una sola transacción).** No se encontró ninguna razón técnica específica de este repositorio que impida usarlo — al contrario, es el diseño más consistente con el patrón transaccional que ya usa el 100% de los servicios financieros existentes (`TransferirWalletAsync`, `PagarQrAsync`, `RecargarWalletManualAsync`, `SolicitarRetiroAsync`, `ConfirmarRetiroPagadoAsync`, `RechazarRetiroAsync` — todos usan un único `BeginTransactionAsync`/commit/rollback).

### 16.4 Diseño recomendado — flujo completo de una sola transacción

1. El controller recibe el header `Idempotency-Key`.
2. Valida que sea un GUID válido (`Guid.TryParse`) — si no lo es, `400`.
3. Resuelve `idUsuario`/`idWallet` desde los claims JWT y el ownership ya existente (mismo patrón de `TryGetIdPersona`/`TryGetUsuarioId` + `ObtenerWalletPersonaAsync`, sin cambios respecto a lo ya implementado en 71.2-E-C/D).
4. Construye `request_hash` (SHA-256 del payload normalizado, ver 16.8) a partir de los valores ya resueltos por el servidor — nunca de un campo que el cliente pudiera manipular.
5. Inicia la transacción financiera (`BeginTransactionAsync`) — la misma que ya existe hoy en `TransferirWalletAsync`/`PagarQrAsync`, sin una transacción adicional por fuera.
6. Como primer paso dentro de esa transacción, intenta `INSERT` la fila de idempotencia (`estado = 'EN_PROCESO'`, sin `http_status`/`id_recurso` todavía).
7. Si la inserción se realiza sin error:
   - continúa con los locks `UPDLOCK/ROWLOCK` de `wallet_saldos` ya implementados;
   - ejecuta ledger, movimientos y auditoría, sin cambios respecto al código actual;
   - una vez calculado el resultado, `UPDATE` de la misma fila: `estado = 'COMPLETADA'`, `http_status`, `id_recurso`, `id_transaccion_ledger`, `respuesta_data_json`, `fecha_completado`;
   - `CommitAsync()` — todo (reserva, operación financiera, resultado) se confirma en un único commit atómico.
8. Si el `INSERT` del paso 6 falla por la restricción única:
   - `RollbackAsync()` de la transacción actual (necesario para que la conexión/sesión pueda ejecutar más comandos tras un error de SQL Server);
   - consulta la fila existente en una operación de lectura separada, **siempre** filtrada por `idUsuario + endpoint + idempotencyKey` (nunca solo por `idempotencyKey`, ver 16.7);
   - compara `request_hash` y aplica la lógica de la sección 16.5.
9. Si la operación financiera falla después del paso 6 (cualquier excepción dentro de la transacción, incluida una regla de negocio como saldo insuficiente):
   - `RollbackAsync()` completo — **la fila de idempotencia insertada en el paso 6 desaparece junto con todo lo demás**, porque nunca llegó a confirmarse; no requiere ninguna operación de limpieza adicional.
   - un reintento legítimo posterior con la misma clave simplemente repite el paso 6 desde cero y tiene éxito (la clave ya no existe).

**Por qué no hace falta eliminar manualmente filas `EN_PROCESO` en un rollback normal**: `EN_PROCESO`, en este diseño, nunca es un estado *durable* por sí mismo — solo existe dentro de una transacción todavía no confirmada. Un `ROLLBACK` (automático, del motor de base de datos, ante cualquier excepción) deshace **todas** las modificaciones de la transacción, incluida la fila recién insertada — no queda ningún registro en la base para "limpiar". La única fila que un observador externo (otra transacción) puede llegar a ver alguna vez es una fila ya `COMPLETADA` (si todo tuvo éxito) — nunca una fila `EN_PROCESO` "abandonada", porque esa combinación de estado nunca se confirma sola.

### 16.5 Comportamiento ante request concurrente — casos A-D

**A. La primera transacción confirma antes de que la segunda continúe**: la segunda recibe el error de restricción única (2627) al ejecutar su propio `INSERT` (paso 6), tras haber estado bloqueada esperando el lock de la primera. Hace `ROLLBACK` de su propia transacción, y en una consulta de lectura separada busca la fila por `idUsuario + endpoint + idempotencyKey`. La encuentra en `COMPLETADA`. Si `request_hash` coincide, reconstruye la respuesta desde las columnas estructuradas (`http_status`, `id_recurso`, `id_transaccion_ledger`, `respuesta_data_json`) y la devuelve con el código de estado original — **sin volver a ejecutar la operación financiera**.

**B. La primera transacción hace rollback**: como se explicó en 16.2/16.4, la segunda transacción, que estaba bloqueada, simplemente completa su propio `INSERT` sin ningún error en cuanto se libera el lock — continúa su flujo normal como si fuera la única solicitud. No hay una decisión de "continuar o reintentar": su operación ya tuvo éxito de forma transparente.

**C. Violación única pero la fila no puede leerse inmediatamente**: bajo el comportamiento estándar de SQL Server, esto no debería ocurrir — en cuanto la primera transacción hace `COMMIT`, su fila es inmediata y consistentemente visible a cualquier lectura posterior bajo READ COMMITTED (con o sin RCSI). Aun así, como medida defensiva ante condiciones de infraestructura atípicas (p. ej. failover, latencia de red), se define un número acotado de reintentos de lectura: **hasta 3 intentos**, con backoff corto (50ms, 100ms, 200ms) — nunca un bucle sin límite. Si tras 3 intentos la fila sigue sin poder leerse de forma coherente, se responde `503 Service Unavailable` con un mensaje genérico (no `409`, porque en este punto no hay certeza de que exista un conflicto real — podría ser un problema transitorio de infraestructura, y `503` invita correctamente a un reintento por parte del cliente).

**D. La clave existe con un hash diferente**: se responde `409` inmediatamente ("Esta clave de idempotencia ya se usó para una operación distinta"), **no** se devuelve ninguna respuesta almacenada, **no** se ejecuta la operación financiera.

### 16.6 Estados de idempotencia

Comparadas las tres opciones de la instrucción:

- **A) EN_PROCESO y COMPLETADA** — dos valores posibles para la columna `estado`, pero (per 16.4) `EN_PROCESO` nunca es durable por sí solo fuera de la transacción en curso.
- **B) Solo una fila final COMPLETADA** — insertar directamente con el resultado ya calculado, sin un estado intermedio. Técnicamente correcto (la atomicidad de la transacción lo garantiza igual), pero **menos eficiente**: obligaría a ejecutar toda la operación financiera (incluido el lock de `wallet_saldos`) antes de poder detectar una clave duplicada, en vez de fallar rápido en el primer `INSERT` sin haber tocado ningún saldo.
- **C) EN_PROCESO, COMPLETADA y FALLIDA** — agrega un tercer estado persistente para intentos fallidos.

**Recomendación: opción A, con una precisión importante** — se usan dos sentencias (`INSERT` con `EN_PROCESO` como primer paso, `UPDATE` a `COMPLETADA` como último paso) dentro de una **única transacción**, para poder fallar rápido ante una clave duplicada sin pagar el costo de ejecutar la operación financiera completa primero (ventaja de A sobre B). Pero, como ya se estableció, `EN_PROCESO` nunca se confirma de forma independiente — solo se observa externamente como `COMPLETADA` o no se observa en absoluto. Esto captura la eficiencia de A sin incurrir en el riesgo de estado huérfano que tenía el diseño de dos transacciones de 71.2-E-E.

**No se agrega `FALLIDA`**: un intento fallido no deja ninguna fila (rollback atómico) y no tiene un "resultado" que valga la pena cachear — a diferencia de una operación exitosa, no hay nada que un reintento posterior debería recibir en su lugar (el reintento simplemente vuelve a intentar la operación desde cero, que es el comportamiento correcto). Si en el futuro se necesita auditar intentos fallidos con fines de fraude/monitoreo, es un requisito distinto (logging/auditoría, ya cubierto parcialmente por `AuditLogService`/`ILogger`) y no debería resolverse sobrecargando la tabla de idempotencia.

**No se diseña limpieza de filas `EN_PROCESO` huérfanas como requisito principal**: como el flujo elegido hace rollback atómico y no las deja persistidas, no existen filas `EN_PROCESO` huérfanas que limpiar en operación normal. La retención (16.10) se centra en filas `COMPLETADA`.

### 16.7 Unicidad y aislamiento entre usuarios

Comparadas las tres variantes:

- **`UNIQUE (idempotency_key)` sola**: depende completamente de que el cliente nunca genere una colisión entre usuarios distintos (accidental o deliberada). Si dos usuarios coincidieran en la misma clave, uno podría —si el código de consulta posterior no fuera cuidadoso— terminar leyendo o siendo bloqueado por la fila del otro.
- **`UNIQUE (endpoint, idempotency_key)`**: reduce colisiones entre operaciones de tipo distinto, pero sigue sin acotar el impacto entre usuarios — dos usuarios distintos llamando al mismo endpoint con la misma clave seguirían colisionando entre sí.
- **`UNIQUE (id_usuario, endpoint, idempotency_key)`** — **recomendada**: acota cualquier colisión de clave al mismo usuario. El peor caso posible de una colisión accidental o deliberada queda limitado a que ese mismo usuario reciba un `409` en su propia segunda operación — **nunca** a que un usuario reciba o interfiera con la respuesta cacheada de otro usuario.

**¿Incluir `id_usuario` permite que la misma UUID sea usada por usuarios diferentes sin colisión? Sí, y es aceptable — es el comportamiento deseado.** Dos usuarios distintos pueden usar literalmente el mismo string UUID como su clave de idempotencia sin ninguna interferencia entre sí, porque la restricción única es sobre la tupla completa `(id_usuario, endpoint, idempotency_key)`, no sobre `idempotency_key` de forma aislada. Cada usuario tiene su propio espacio de claves, completamente aislado.

**Regla obligatoria de implementación**: la consulta posterior a una violación única (paso 8 de 16.4) debe estar **siempre** filtrada por los tres campos (`id_usuario`, `endpoint`, `idempotency_key`) — nunca solo por `idempotency_key`. Aunque la restricción de base de datos ya lo garantiza a nivel de esquema, el código de la aplicación debe reforzar esta misma condición explícitamente en el `WHERE` de la consulta, para que un descuido futuro (p. ej. alguien simplifica la consulta "porque total la clave ya es única") no reintroduzca el riesgo de fuga entre usuarios si en algún momento la restricción de base de datos cambiara.

### 16.8 Hash normalizado

**Transferencia** — JSON canónico con propiedades en orden alfabético fijo, UTF-8:
```json
{"descripcion":"Enviado a juan — Wallet #67","idUsuario":123,"idWalletDestino":67,"idWalletOrigen":45,"op":"wallets.transferencia.v1","valor":"1500.00"}
```

**Pago QR**:
```json
{"codigoQr":"QR-DEMO-XPAY-QA-001","descripcion":"Pago a Comercio Demo XPAY QA","idUsuario":123,"idWalletPagadora":45,"op":"qr.pagar.v1","valor":"25000.00"}
```

Reglas de construcción:
- **Codificación**: UTF-8 sobre el JSON serializado, antes de aplicar SHA-256.
- **Serialización**: JSON canónico con **orden alfabético fijo y documentado** de las propiedades (evita ambigüedad sin depender de convenciones más complejas como JCS/RFC 8785 completo, suficiente para este caso de uso interno).
- **`op`**: identificador de operación + versión (`wallets.transferencia.v1`, `qr.pagar.v1`) — si el contrato de negocio cambia en el futuro (nuevos campos relevantes), se incrementa la versión en vez de reinterpretar hashes antiguos.
- **`idUsuario`, `idWalletOrigen`/`idWalletPagadora`**: siempre los valores **resueltos por el servidor** desde el claim JWT — nunca un campo que el cliente pudiera enviar. Esto es intencional: `IdWalletOrigen`/`IdWalletUsuario`/`CreadoPor` fueron eliminados de los DTO en 71.2-E-C/D precisamente para que el cliente no pueda influir en ellos; el hash tampoco debe reintroducir esa superficie construyéndose sobre un valor que el cliente controle sin restricción.
- **Decimales**: formato invariante (`CultureInfo.InvariantCulture`), punto decimal, sin separador de miles, **2 decimales fijos** (`"1500.00"`, nunca `"1500"` ni `"1,500.00"`) — para que variaciones de formato/cultura no produzcan hashes distintos para el mismo valor económico.
- **Trim de strings**: `descripcion` se recorta (espacios al inicio/fin) antes de hashear, para que diferencias triviales de espacios en blanco no generen falsos negativos de "operación distinta".
- **`null` vs. cadena vacía**: se tratan como **valores distintos** explícitamente en la serialización — `"descripcion":null` y `"descripcion":""` deben producir hashes diferentes, para no colapsar dos intenciones potencialmente distintas del usuario.
- **Algoritmo**: SHA-256 sobre los bytes UTF-8 del JSON canónico. Almacenamiento: `VARBINARY(32)`.

### 16.9 Respuesta almacenada

Comparadas las tres opciones:
- **A) `respuesta_json` completa**: simple de implementar, pero corre el riesgo de que alguien, en el futuro, agregue al objeto de respuesta un campo no pensado para persistencia (p. ej. un dato de diagnóstico agregado temporalmente a `data` para depurar algo) sin darse cuenta de que también quedaría guardado indefinidamente en esta tabla.
- **B) Columnas estructuradas puras**: más segura y explícita, pero requeriría una columna por cada variación de forma de respuesta entre distintos endpoints (transferencia y pago QR no devuelven exactamente los mismos campos en `data`), lo cual es rígido.
- **C) Combinación — recomendada**: columnas estructuradas para los campos comunes a **todas** las operaciones (`http_status`, `id_recurso`, `id_transaccion_ledger`, `fecha_completado`) + una columna `respuesta_data_json` **acotada en tamaño** (`NVARCHAR(1000)`, no `MAX`) que contiene **únicamente** el objeto `data` que el endpoint ya devuelve hoy al cliente en la respuesta exitosa (que en ambos endpoints ya es no sensible por diseño: ids numéricos y montos, nunca JWT/documentos/nombres/correos/teléfonos — mismo criterio de logging ya aplicado en todas las etapas anteriores). No se agrega ninguna exposición nueva porque es exactamente lo que ya se envía por HTTPS al cliente en la primera respuesta.

**No almacenar**: JWT, datos personales, documentos, nombres, correos, teléfonos, el payload completo del request, stack traces, o cualquier información financiera más allá de lo que el endpoint ya devuelve hoy en `data`.

### 16.10 SQL corregido (no ejecutado, no implementado)

```sql
CREATE TABLE wallet_idempotencia (
    id_idempotencia        BIGINT           IDENTITY(1,1) NOT NULL,
    id_usuario             BIGINT           NOT NULL,
    endpoint                VARCHAR(60)      NOT NULL,       -- 'wallets.transferencia' | 'qr.pagar'
    idempotency_key         UNIQUEIDENTIFIER NOT NULL,
    request_hash             VARBINARY(32)    NOT NULL,       -- SHA-256 del payload normalizado (16.8)
    estado                   VARCHAR(12)      NOT NULL,       -- 'EN_PROCESO' | 'COMPLETADA'
    http_status               SMALLINT         NULL,
    id_recurso                 BIGINT           NULL,
    id_transaccion_ledger       BIGINT           NULL,
    respuesta_data_json           NVARCHAR(1000)   NULL,        -- solo el objeto "data" ya público (16.9)
    fecha_creacion                 DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    fecha_completado                DATETIME2(3)     NULL,
    fecha_expiracion                 DATETIME2(3)     NOT NULL,   -- política de retención, ver abajo

    CONSTRAINT pk_wallet_idempotencia PRIMARY KEY CLUSTERED (id_idempotencia),
    CONSTRAINT uq_wallet_idempotencia UNIQUE (id_usuario, endpoint, idempotency_key),
    CONSTRAINT ck_wallet_idempotencia_estado CHECK (estado IN ('EN_PROCESO', 'COMPLETADA'))
);

-- Índice de limpieza/retención — centrado en COMPLETADA, que es el único estado
-- que normalmente queda persistido (ver 16.6: EN_PROCESO no sobrevive un rollback,
-- por lo que un índice filtrado exclusivamente para EN_PROCESO no tendría uso
-- práctico bajo el diseño de una sola transacción).
CREATE INDEX ix_wallet_idempotencia_expiracion
    ON wallet_idempotencia (fecha_expiracion)
    WHERE estado = 'COMPLETADA';
```

**Política de retención — comparada:**

| Retención | A favor | En contra |
|---|---|---|
| 24 horas | Cubre reintentos de red inmediatos/mismo día; tabla más pequeña | Insuficiente si un cliente reintenta tras un fin de semana largo o una app móvil retoma una cola offline al día siguiente — el reintento tardío se procesaría como una **segunda operación financiera real**, no como un duplicado detectado |
| 72 horas | Cubre la mayoría de escenarios de reintento razonables (fin de semana largo) | Sigue siendo corto para un cliente que reabre la app varios días después con un draft pendiente |
| **7 días — recomendada** | En un sistema de movimiento de dinero, el costo de una ventana **demasiado corta** (un reintento tardío legítimo ejecuta una segunda transferencia/pago real) es mucho más grave que el costo de una ventana más larga (unas filas adicionales, de bajo volumen — una por operación exitosa, no por cada lectura) | Tabla ligeramente más grande, mitigado por el índice de limpieza y por el bajo volumen esperado |

**Recomendación: 7 días**, justificada específicamente por tratarse de una wallet financiera: el riesgo asimétrico (dinero movido dos veces por una ventana de idempotencia expirada demasiado pronto, frente al costo trivial de almacenamiento de mantener filas `COMPLETADA` una semana) favorece claramente la retención más larga de las tres evaluadas.

### 16.11 Manejo de errores SQL en EF Core

- **Excepción externa**: al ejecutar el `INSERT` mediante EF Core (ya sea vía `Add()` + `SaveChangesAsync()`, o vía `ExecuteSqlInterpolatedAsync`), un error de SQL Server se recibe como `Microsoft.EntityFrameworkCore.DbUpdateException` (si pasa por el pipeline de `SaveChangesAsync`) — la excepción original `Microsoft.Data.SqlClient.SqlException` está disponible en `dbUpdateException.InnerException as SqlException`. Si se usa `ExecuteSqlInterpolatedAsync` directamente, el comportamiento de envoltura puede variar según la versión del proveedor — el manejo debe revisar defensivamente tanto `ex as SqlException` como `ex.InnerException as SqlException`.
- **Identificar 2601/2627 sin depender del texto del mensaje**: usar la propiedad numérica `SqlException.Number` (o, más robusto, iterar `SqlException.Errors` — una `SqlErrorCollection` que puede contener más de un error — buscando cualquier entrada con `Number == 2627 || Number == 2601`). Nunca inspeccionar `.Message` con comparación de texto, porque el mensaje varía por idioma/versión del servidor.
- **Distinguir duplicación de un error inesperado**: `Number == 2627` (o `2601`) → duplicación de clave, tratado según 16.5. Cualquier otro `SqlException.Number`, o cualquier excepción que no sea `SqlException`/`DbUpdateException` con esa causa → error inesperado.
- **Deadlock (1205)**: SQL Server ya eligió y revirtió automáticamente a la transacción "víctima" del deadlock — no hay nada que deshacer manualmente. La acción correcta es **reintentar la transacción completa desde el principio** (no solo el `INSERT`), con un número acotado de reintentos (p. ej. hasta 3, mismo criterio que 16.5.C) antes de responder al cliente. No es un error del cliente ni de idempotencia — es contención transitoria del motor.
- **Timeout**: puede surgir como `SqlException` con número **1222** ("Lock request time out period exceeded") o como una excepción de timeout de comando a nivel de cliente ADO.NET — se trata como transitorio, se responde `503` con mensaje genérico, y se registra (`LogWarning` o `LogError`, ya que un timeout sostenido señala contención real que vale la pena investigar, aunque la instancia puntual no sea un "bug").
- **Error inesperado** (cualquier otra excepción): `_logger.LogError(ex, ...)` + `500` genérico — mismo patrón ya aplicado en todos los controllers de esta cadena de etapas.
- **Qué registrar**: `idUsuario`, `endpoint`, `idempotencyKey` (un GUID generado por el cliente, no secreto) — identificadores técnicos suficientes para correlacionar el incidente. **Nunca** el request completo, el hash como si fuera información sensible (no lo es, pero tampoco aporta nada al diagnóstico sin el payload original), el JWT, ni ningún dato personal.
- **Nada de esto se implementa en esta etapa** — queda documentado para cuando se apruebe la implementación.

### 16.12 Alcance inicial recomendado

**Operaciones que crean un nuevo movimiento financiero cada vez que se ejecutan con éxito, sin ningún guard natural contra repetición:**
- `POST /api/wallets/transferencia`
- `POST /api/qr/pagar`
- `POST /api/wallets/{idWallet}/recarga-manual`
- `solicitar-retiro` (cada llamada exitosa crea un nuevo `RetiroComercio` independiente — un reintento duplica el retiro pendiente y el descuento de saldo del comercio)

**Transiciones de estado que ya tienen un guard atómico** (71.2-E-D/E): `confirmar-retiro`, `rechazar-retiro` — un segundo intento sobre el mismo retiro ya `PAGADO`/`RECHAZADO` falla limpiamente con `409` gracias al lock + `TransicionRetiroInvalidaException` ya implementados; no crean un movimiento financiero nuevo en cada intento, solo transicionan un estado una única vez con éxito. El riesgo de duplicación financiera aquí ya está mitigado — la prioridad de agregar idempotencia por clave es baja.

**Propuesta de primera implementación mínima**: `POST /api/wallets/transferencia` y `POST /api/qr/pagar` primero — comparten prácticamente el mismo diseño (mismo servicio auxiliar reutilizable de idempotencia), y son los dos flujos más auditados y maduros de toda esta cadena de etapas. `recarga-manual` (uso administrativo, menor frecuencia) y `solicitar-retiro` (ya tiene ownership/scope validado, y una duplicación ahí es detectable/reversible administrativamente sin pérdida irreversible de fondos — el dinero permanece "pendiente" duplicado dentro del sistema, no sale de él) quedan para una segunda fase. **Nada de esto se implementa todavía.**

### 16.13 Decisión de producto QR — registrada

**Decisión registrada en esta etapa**: el modelo actual se conserva como **MODELO A — QR fijo de comercio** (reutilizable, monto abierto ingresado por el pagador, múltiples pagos independientes, permanece activo mientras el comercio/tienda lo mantenga activo, no se consume después de un pago). No se implementa QR dinámico en esta fase. Si se solicita posteriormente, un QR dinámico sería una **funcionalidad nueva e independiente** — no una corrección del modelo actual — con: monto fijo, vencimiento, máquina de estados (`PENDIENTE/PAGADO/VENCIDO/CANCELADO`), un solo uso, protección de replay, y relación uno-a-uno con una orden/cobro. No se modificó ningún archivo relacionado con QR en esta etapa.

### 16.14 RCSI — cerrado para QA en 71.2-E-G.3 (histórico: pendiente al momento de escribir esta sección)

En el momento en que se escribió esta sección (71.2-E-F), la consulta de 15.2 seguía sin ejecutarse y este apartado documentaba correctamente esa incertidumbre. **Actualización posterior:** el usuario la ejecutó manualmente contra `sqldb-xpay-qa` en la Fase 71.2-E-G.3 — resultado: `is_read_committed_snapshot_on = True`, `snapshot_isolation_state_desc = ON`. Ver el detalle completo en 15.1 (actualización) y 17.24. **Sigue sin verificar el valor en un eventual entorno de producción separado**, si existiera — esta confirmación es específica de QA.

### 16.15 Inventario para una futura reclasificación 404 (no implementada)

Inventario exacto de los `throw` en los tres servicios auditados, clasificados sin modificar código:

**Recurso inexistente/inactivo (candidatos a `RecursoNoEncontradoException` → 404):**
- `PagoQrService`: "El QR no existe o no está activo.", "El comercio no existe o no está activo.", "La tienda no existe o no está activa.", "La wallet del usuario no existe o no está activa.", "La wallet del usuario no tiene registro de saldo."
- `RetiroComercioService`: "No existe el retiro con id {id}." (×2, `GetRetiroByIdAsync`), "El comercio no existe o no está activo.", "La wallet del comercio no existe o no está activa." (×2), "La wallet del comercio no tiene registro de saldo." (×2), "El retiro no existe." (×2, `ConfirmarRetiroPagadoAsync`/`RechazarRetiroAsync`)
- `WalletOperacionService`: "La wallet no existe o no está activa.", "La wallet no tiene registro de saldo.", "La wallet origen no existe o no está activa.", "La wallet destino no existe o no está activa.", "La wallet origen no tiene registro de saldo.", "La wallet destino no tiene registro de saldo."

**Regla de negocio (permanecen en 400, sin cambio propuesto):** valores ≤ 0, "wallet origen/destino no pueden ser la misma", saldo insuficiente (3 variantes), tipo de wallet incorrecto, "comercio no tiene wallet asignada", "comercio operativo no tiene comercio existente asociado", identificadores ≤ 0.

**Conflicto de estado (ya resuelto en 71.2-E-E, `409` vía `TransicionRetiroInvalidaException`):** "El retiro no está en estado PENDIENTE..." (×2).

**Hallazgo adicional, fuera del pedido explícito de 404 pero relacionado**: los mensajes "No existe la cuenta ledger ..." (7 ocurrencias entre los 3 servicios) y "La transacción ledger ... no está balanceada." (4 ocurrencias) representan **errores de configuración/integridad del sistema**, no errores del solicitante — hoy se mapean a `400` igual que todo lo demás, lo cual le indica incorrectamente al cliente que "su solicitud está mal" cuando en realidad el sistema tiene una cuenta ledger faltante o una inconsistencia contable. Candidato razonable a `500` en una reclasificación futura, no evaluado a fondo en esta etapa por no ser el pedido explícito.

**Excepción tipada recomendada**: `Exceptions/RecursoNoEncontradoException.cs` (`: InvalidOperationException`, mismo patrón que `TransicionRetiroInvalidaException`/`TransicionCajaInvalidaException`).

**Controllers donde tendría que capturarse** (antes del `catch (InvalidOperationException ex)` genérico): `WalletsController.Transferir`, `WalletsController.RecargarManual`, `QrController.Pagar`, `ComerciosController.SolicitarRetiro`, `ComerciosController.ConfirmarPago`, `ComerciosController.RechazarRetiro`, `ComerciosController.GetRetiro`.

**No se modificó ningún servicio ni controller en esta etapa** — este inventario queda listo para una futura autorización explícita.

### 16.16 Riesgos residuales (actualizados)

- **Idempotencia — diseño corregido y finalizado, sigue sin implementar**: el diseño de una sola transacción (16.4-16.11) reemplaza al de 71.2-E-E. Ningún endpoint tiene protección real contra duplicación/replay todavía.
- **Reclasificación 400→404** — inventariada (16.15), no implementada.
- **QR de comercio reutilizable** — decisión de producto registrada como Modelo A definitivo para esta fase (16.13); Modelo B queda como posible funcionalidad futura, no autorizada.
- ~~RCSI real de la base — sigue desconocido (16.14), consulta preparada y no ejecutada~~ — **cerrado en 71.2-E-G.3**: confirmado `ON` en `sqldb-xpay-qa` (ver 15.1, 15.2, 17.24).
- **Mensajes "no existe cuenta ledger"/"transacción no balanceada" mapeados a 400 en vez de 500** — hallazgo nuevo de esta etapa (16.15), no evaluado a fondo, no implementado.
- Todos los riesgos residuales de 71.2-E-D/E-E no mencionados arriba siguen vigentes sin cambios (ver secciones 11 y 15.18).

## 17. Fase 71.2-E-G — Implementación controlada de idempotencia en Transferencia y Pago QR

**No reescribe las decisiones ya aprobadas en 71.2-E-F (sección 16)** — este apartado documenta la implementación concreta de ese diseño, sin cambiarlo.

### 17.1 Inspección previa

- **EF Core**: `Microsoft.EntityFrameworkCore.SqlServer` 8.0.28 (`Xpay.Api.csproj`), que trae `Microsoft.Data.SqlClient` como dependencia transitiva — ya usado directamente en `WalletCajaComercioService.cs`/`WalletCierreDiarioComercioService.cs`, sin necesidad de instalar ningún paquete nuevo.
- **Convención de tablas/columnas**: 100% Fluent API (`modelBuilder.Entity<T>(e => { e.ToTable("..."); e.HasKey(...); MapT(e); })` + método privado estático `MapT` con `e.Property(x => x.X).HasColumnName("x")`), **cero Data Annotations**, **cero `HasMaxLength`** en todo el `DbContext` (los tamaños de columna viven solo en el SQL, no en el modelo EF) — confirmado por grep antes de escribir el modelo nuevo.
- **IDs**: `long` (BIGINT) en todos los modelos, sin excepción.
- **Namespace de modelos**: `Xpay.Api.Models`, POCOs planos sin lógica.
- **DbSet**: se agrega siempre en `XpayDbContext` como `public DbSet<T> Xs => Set<T>();` — patrón confirmado y seguido.
- **`Guid`**: no había ningún uso previo en el proyecto — es la primera vez que se usa; EF Core lo mapea a `uniqueidentifier` sin configuración adicional (comportamiento estándar del proveedor SQL Server).
- **Patrón de transacciones**: `await using var transaction = await _db.Database.BeginTransactionAsync(); try { ...; await transaction.CommitAsync(); } catch { await transaction.RollbackAsync(); throw; }` — idéntico en los 8 métodos financieros existentes del proyecto.
- **Precedente exacto para violaciones UNIQUE**: `WalletCajaComercioService.EsViolacionUniqueUsuarioComercioFecha(DbUpdateException ex) => ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601) && ...`, usado como `catch (DbUpdateException ex) when (...)` — generalizado en esta etapa a `Common/SqlExceptionHelper.cs` porque dos servicios distintos lo necesitan.
- **`WalletOperacionService.TransferirWalletAsync`/`PagoQrService.PagarQrAsync`/`WalletsController.Transferir`/`QrController.Pagar`**: inspeccionados en su estado real (no asumido) inmediatamente antes de modificar — coinciden exactamente con lo documentado en 71.2-E-D/E-E.
- **Respuestas reales actuales**: `Transferir` devolvía `{success,message,data:{idTransaccion,idWalletOrigen,idWalletDestino,valor}}`; `Pagar` devolvía `{success,message,data:{idVentaQr,idTransaccion,idComercio,idTienda,idWalletUsuario,valor,estado}}`. IDs generados: `tx.IdTransaccionLedger` (ambos endpoints), `venta.IdVentaQr` (solo pago QR). Estas formas se preservaron exactamente — `TransferenciaResultadoDto`/`PagoQrResultadoDto` son un espejo 1:1 de estos campos.
- **`UserWalletPage.tsx`/`api/client.ts`**: `post()` es un wrapper `fetch` simple, sin interceptores ni reintentos automáticos — confirmado que no hay riesgo de que un reintento silencioso duplique una Idempotency-Key sin que el código lo controle explícitamente.
- **Proyecto de pruebas**: no existe ningún `.csproj` de tests en el repositorio (`find` no encontró ninguno) — no se creó uno nuevo, según instrucción explícita. Ver 17.17 para los vectores de prueba manuales documentados en su lugar.

**Discrepancias encontradas entre el código real y la documentación de 71.2-E-F**: ninguna de fondo. Único ajuste respecto al diseño narrado en 16.4: el `try` que envuelve el `INSERT` de la reserva se fusionó con el `try` de la operación financiera en un único bloque (en vez de dos `try` consecutivos) para que un deadlock/timeout ocurrido durante la propia inserción de la reserva —no solo durante la lógica posterior— también se clasifique como `TransientDatabaseException` (503) en vez de cachear un 500 genérico. No cambia el diseño aprobado (sigue siendo una sola transacción, un solo `INSERT` inicial, un solo `COMMIT` final) — es un refinamiento de cobertura de errores, reportado aquí explícitamente.

### 17.2 Script SQL — completo, no ejecutado

`database/029_wallet_idempotencia.sql` — mismo estilo que 027/028 (`SET XACT_ABORT ON`, `BEGIN TRY/TRANSACTION`, guards `IF NOT EXISTS` por tabla/índice, `BEGIN CATCH/ROLLBACK/THROW`, verificación final por `sys.*`). Una sola tabla nueva (`wallet_idempotencia`), sin `DROP TABLE`, sin tocar ninguna otra tabla. Columnas: `id_idempotencia` (PK IDENTITY), `id_usuario`, `endpoint` VARCHAR(60), `idempotency_key` UNIQUEIDENTIFIER, `request_hash` VARBINARY(32), `estado` VARCHAR(12) (`CHECK IN ('EN_PROCESO','COMPLETADA')`), `http_status` SMALLINT, `id_recurso`/`id_transaccion_ledger` BIGINT, `respuesta_data_json` NVARCHAR(1000), `fecha_creacion`/`fecha_completado`/`fecha_expiracion` DATETIME2(3) con defaults UTC (`SYSUTCDATETIME()`, `fecha_expiracion` default `DATEADD(DAY,7,SYSUTCDATETIME())` como red de seguridad aunque la app siempre la establece explícitamente). `CONSTRAINT uq_wallet_idempotencia_usuario_endpoint_key UNIQUE (id_usuario, endpoint, idempotency_key)`. Índice `ix_wallet_idempotencia_expiracion` filtrado por `estado = 'COMPLETADA'` (no por `EN_PROCESO`, que no queda persistido en operación normal bajo este diseño). Retención: **7 días**, justificada en §16.10 (riesgo asimétrico de una wallet financiera: una ventana corta permite que un reintento tardío legítimo mueva dinero dos veces).

**Confirmación explícita: el script NO fue ejecutado.** Ningún comando SQL se envió a ninguna base de datos en esta sesión. El usuario lo ejecutará manualmente tras revisarlo.

### 17.3 Modelo y configuración EF Core

`Models/WalletIdempotencia.cs` (POCO plano, sin anotaciones) + mapeo en `XpayDbContext` (`ToTable("wallet_idempotencia")`, `HasKey(x => x.IdIdempotencia)`, `MapWalletIdempotencia` con `HasColumnName` por propiedad, mismo estilo que el resto del archivo). `DbSet<WalletIdempotencia> WalletIdempotencias` agregado junto a los demás. **Sin `HasMaxLength`** (consistente con que ningún otro modelo del proyecto lo usa — los tamaños viven en el SQL). **Sin `HasCheckConstraint`** vía Fluent API (confirmado que el proyecto nunca lo usa — el `CHECK` vive solo en el script SQL). **Sin navegación** agregada (no se modeló ninguna relación de navegación hacia/desde `WalletIdempotencia` — no era necesaria). El modelo **no se expone en ningún controller** — solo se usa internamente desde `WalletOperacionService`/`PagoQrService`/`Common/IdempotencyStore`.

### 17.4 Constantes de endpoint lógico

`Common/IdempotencyEndpoints.cs`: `WalletsTransferencia = "wallets.transferencia"`, `QrPagar = "qr.pagar"` — constantes fijas en código, nunca derivadas de la URL HTTP ni del casing recibido, nunca aceptadas desde el cliente.

### 17.5 Validación de Idempotency-Key

`WalletsController`/`QrController` (helper privado duplicado en cada uno, mismo criterio que `TryGetIdPersona`/`TryGetUsuarioId` — sin clase base compartida en este proyecto): valida header presente, un único valor, no vacío, `Guid.TryParse` válido. Si falla: `400` con mensaje claro no técnico ("Falta el encabezado Idempotency-Key." / "Se recibió más de un valor..." / "...debe ser un identificador válido."), **sin iniciar la operación financiera** (la validación ocurre antes de resolver la wallet e invocar el servicio) y **sin registrar nada como falla del sistema** (no hay `LogError`, es una solicitud mal formada del cliente). El backend **nunca genera una clave** cuando falta — la ausencia es un error 400, no un valor por defecto.

### 17.6 Contexto seguro

Sin cambios respecto a 71.2-E-C/D: `idUsuario`/`idPersona` desde claims JWT, `idWalletOrigen`/`idWalletPagadora` resueltos en servidor vía `WalletService.ObtenerWalletPersonaAsync`, nunca aceptados del request. La idempotencia se added como una capa **encima** de estas validaciones ya existentes, sin tocarlas ni ampliar ningún permiso o rol.

### 17.7 Normalización y hash

`Common/IdempotencyHashHelper.cs` — implementa exactamente el diseño de §16.8: `SortedDictionary<string, object?>(StringComparer.Ordinal)` (orden alfabético fijo, **independiente de la cultura del servidor** — refinamiento sobre el diseño original, que no había especificado el comparador explícitamente), JSON canónico vía `System.Text.Json`, UTF-8, `SHA256.HashData` (32 bytes), valor con `ToString("F2", CultureInfo.InvariantCulture)`, `descripcion?.Trim()` (preserva `null` como `null`, nunca lo colapsa a cadena vacía). `op` fijo (`wallets.transferencia.v1`/`qr.pagar.v1`) incluido siempre. Los valores resueltos por servidor (`idWalletOrigen`/`idWalletPagadora`) nunca provienen de un campo eliminado del DTO (`IdWalletOrigen`/`IdWalletUsuario`/`CreadoPor` no existen desde 71.2-E-C/D). **Sin pruebas unitarias automatizadas** (no existe proyecto de tests) — vectores de prueba manuales documentados en 17.17.

### 17.8 Helper de detección 2601/2627 (y 1205/1222)

`Common/SqlExceptionHelper.cs` — generaliza `WalletCajaComercioService.EsViolacionUniqueUsuarioComercioFecha`. `TryGetSqlException` cubre: `SqlException` directa, `DbUpdateException.InnerException`, y `InnerException` de cualquier otra excepción (defensivo). Clasifica por `.Number` exclusivamente (nunca por texto). `IsUniqueViolation` (2627/2601) e `IsTransient` (1205 deadlock, 1222 lock timeout) se tratan igual entre sí en el sentido de que ambos números de cada categoría producen el mismo resultado (2601 y 2627 nunca se distinguen entre sí en el manejo — mismo comportamiento para ambos, como pedía la instrucción).

### 17.9 Integración en transferencia

`WalletOperacionService.TransferirWalletAsync` — firma nueva: `(long idWalletOrigen, long creadoPor, Guid idempotencyKey, TransferenciaWalletRequest request)`, retorna `Task<IdempotentOperationResult<TransferenciaResultadoDto>>`. Flujo: validaciones existentes (valor > 0, origen≠destino) → hash → `BeginTransactionAsync` → `Add` reserva `EN_PROCESO` + primer `SaveChangesAsync` (fuerza la restricción UNIQUE **antes** de cualquier lock de saldo) → resto de la lógica **sin ningún cambio** (locks `wallet_saldos` ordenados, ledger, movimientos, auditoría, balance) → construir `TransferenciaResultadoDto` → `IdempotencyStore.MarcarCompletada` (mismo tracked entity, se actualiza en el `SaveChangesAsync` final ya existente) → `CommitAsync` → retorna `Replayed: false`.

### 17.10 Integración en pago QR

`PagoQrService.PagarQrAsync` — mismo patrón exacto, firma `(long idWalletUsuario, long creadoPor, Guid idempotencyKey, PagoQrRequest request)`, retorna `Task<IdempotentOperationResult<PagoQrResultadoDto>>`. `TryRegistrarDisponibilidadAsync` (best-effort, comercios aliados) no se tocó — sigue siendo un `try/catch` interno que nunca propaga.

### 17.11 Manejo de rollback

Un único `try` envuelve reserva + operación financiera completa (ver 17.1 sobre el refinamiento respecto al diseño narrado). Tres `catch`, en orden de especificidad: `IsUniqueViolation` → rollback + `ChangeTracker.Clear()` + `IdempotencyStore.ResolverReplayAsync`; `IsTransient` → rollback + `throw new TransientDatabaseException`; genérico → rollback + `throw` (preserva 100% el manejo de `InvalidOperationException` existente para reglas de negocio — saldo insuficiente, wallet inactiva, etc. — sin cambio alguno). Si la operación financiera falla por cualquier motivo no relacionado con idempotencia, la fila `EN_PROCESO` de la reserva se revierte junto con todo lo demás — no requiere ninguna operación de limpieza adicional (misma razón documentada en §16.4: nunca se confirma por separado).

### 17.12 Manejo del tracking de EF Core

Tras el `catch (Exception ex) when (SqlExceptionHelper.IsUniqueViolation(ex))`: `await transaction.RollbackAsync()` primero, luego `_db.ChangeTracker.Clear()` — limpia **todo** el tracking del contexto (no solo la entidad de idempotencia; en este punto del flujo no hay nada más tracked porque la reserva es el primer statement de la transacción, así que es equivalente a desacoplar solo esa entidad, pero más simple y robusto ante cualquier cambio futuro del método). Después de esto, `IdempotencyStore.ResolverReplayAsync` usa exclusivamente `AsNoTracking()` para su lectura — el contexto queda en un estado limpio y válido para el resto del ciclo de vida del request (que termina inmediatamente después, al construir la respuesta HTTP).

### 17.13 Reconstrucción de respuestas

`respuesta_data_json` almacena la serialización directa de `TransferenciaResultadoDto`/`PagoQrResultadoDto` (nunca un objeto anónimo ni el `IActionResult`). Al reproducir: `JsonSerializer.Deserialize<TData>(fila.RespuestaDataJson)`, tipado al DTO correspondiente (genérico en `IdempotencyStore.ResolverReplayAsync<TData>`). El controller arma el mismo `{success, message, data}` de siempre a partir de `resultado.Data` — **ningún cambio de contrato** para el frontend. Se agrega el header de respuesta opcional `Idempotent-Replayed: true` únicamente cuando `Replayed == true` — no sensible, no altera el JSON, documentado aquí.

### 17.14 Logging

`LogInformation` cuando `Replayed == true` (idUsuario, endpoint lógico, idempotencyKey — nunca el hash, nunca la descripción, nunca el código QR completo). `LogWarning` en `IdempotencyUnavailableException` (reintentos agotados) y en `TransientDatabaseException` (deadlock/timeout) — ambos con idUsuario/idempotencyKey, la excepción interna (con su `SqlException.Number`) se pasa al logger pero no se expone al cliente. `LogError` reservado para el `catch (Exception ex)` genérico ya existente, sin cambios. Ningún duplicado legítimo (replay) se registra como error.

### 17.15 Cambios frontend

Solo `UserWalletPage.tsx` (`handleEnviar`, `handlePagarQr`, `resetEnviar`, `resetPagar`) y `api/client.ts` (`post()` acepta un tercer parámetro opcional `extraHeaders`, retrocompatible). `crypto.randomUUID()` generado y comparado contra una "firma" de los campos relevantes (destino+valor+descripción para transferencia; código QR+valor para pago QR) guardada en un `useRef` — mismo intento (firma sin cambios) reutiliza la clave, cualquier campo distinto genera una nueva. La clave se limpia (`= null`) tras éxito y en `resetEnviar`/`resetPagar`. Enviada como header `Idempotency-Key`, nunca en el body, nunca mostrada en la UI. **Sin rediseño visual.**

### 17.16 Comportamiento ante doble clic

`envBusy`/`pagBusy` deshabilitan el botón de envío durante el request (ya existente, sin cambios) — evita que un doble clic real dispare dos submits desde la misma instancia del componente. Como la clave se genera/reutiliza por **firma de campos**, no por clic, incluso si un doble clic lograra escapar al `disabled` (p. ej. por un evento sintético duplicado), ambos requests usarían la **misma** clave — el segundo sería deduplicado correctamente por el backend en vez de producir un segundo movimiento financiero.

### 17.17 Pruebas — realmente ejecutadas vs. pendientes

**Ejecutadas**: `dotnet build` (backend) y `npm run build` (frontend) — ambos exitosos, ver 17.20/17.21. `git diff --check` sin errores. **Ninguna prueba contra una base de datos real fue ejecutada** — no hay entorno QA en esta sesión, y el script SQL no fue aplicado (sin la tabla creada, ningún flujo de idempotencia puede ejecutarse de punta a punta hoy).

**Vectores de prueba preparados para QA real** (no ejecutados):
1. Primera transferencia con clave nueva → 200, movimiento aplicado.
2. Repetición exacta con la misma clave → 200, `Idempotent-Replayed: true`, sin nuevo movimiento.
3. Misma clave, valor diferente → 409, sin ejecutar.
4. Misma clave, destino diferente → 409, sin ejecutar.
5. Dos requests concurrentes idénticos (misma clave) → uno 200 fresco, el otro 200 reproducido (o ambos 200 con `Replayed` distinto) — nunca dos movimientos.
6. Primera transacción falla por saldo insuficiente → 400, sin fila de idempotencia persistida.
7. Reintento posterior con la misma clave tras el rollback del caso 6 → se procesa como intento nuevo (la clave quedó libre).
8. Pago QR inicial → 200.
9. Replay de pago QR (misma clave) → 200, `Idempotent-Replayed: true`.
10. Misma clave QR, valor distinto → 409.
11. Mismo UUID usado por dos usuarios distintos → ambos 200 independientes, sin colisión (constraint incluye `id_usuario`).
12. Forzar 2601 (violación de índice único sin restricción nombrada) — no aplica con el script actual (usa `CONSTRAINT ... UNIQUE`, produce 2627) — documentado como caso no reproducible con este esquema, cubierto igualmente por el helper.
13. Forzar 2627 (violación de la restricción nombrada) → comportamiento de los casos 2/3/4/9/10.
14. Forzar 1205 (deadlock) → 503, sin commit, `LogWarning`.
15. Forzar timeout/1222 → 503, `LogWarning`.
16. `respuesta_data_json` que excediera 1000 caracteres → `InvalidOperationException` interna → rollback completo → 500 + `LogError` (caso defensivo: con los DTOs actuales, de solo ids/montos, no se espera alcanzar este límite en la práctica).
17. `Idempotency-Key` ausente → 400, sin tocar la base.
18. `Idempotency-Key` inválida (no-GUID) → 400.
19. Doble clic en el frontend → un solo request efectivo (botón deshabilitado) o, en el peor caso, dos requests con la misma clave (deduplicados por el backend).
20. Cambio de campo del formulario entre dos envíos → clave nueva, tratado como operación distinta.

Ninguno de estos 20 casos se declara "aprobado" — son los vectores preparados para cuando exista acceso a QA real.

### 17.18 Documento actualizado

Esta misma sección (17). No se modificó el contenido de la sección 16 (diseño aprobado en 71.2-E-F) — permanece como la referencia normativa del diseño; esta sección documenta su implementación concreta.

### 17.19 Riesgos residuales (nuevos de esta etapa)

- **Sin ejecución contra base real**: la tabla `wallet_idempotencia` no existe todavía (script no ejecutado) — nada de este flujo puede probarse de punta a punta hasta que el usuario ejecute `database/029_wallet_idempotencia.sql`.
- **Retry transaccional automático no implementado**: un deadlock/timeout hoy responde `503` sin reintentar la operación — el usuario/frontend debe reintentar manualmente (documentado como mejora futura, no implementada por decisión explícita de esta etapa).
- **`recarga-manual`, `solicitar-retiro`, `confirmar-retiro`, `rechazar-retiro`** siguen sin idempotencia por clave — fuera del alcance explícito de esta etapa (ver alcance inicial en §16.12).
- **QR dinámico**: sin cambios — sigue siendo Modelo A por decisión registrada en 71.2-E-F/71.2-E-E.
- Todos los riesgos residuales de etapas anteriores no mencionados aquí siguen vigentes (secciones 11, 15.18, 16.16).

### 17.20 Corrección E-G.1 — alcance del catch de UNIQUE

**Riesgo detectado**: la implementación de 71.2-E-G fusionó el `try` de la reserva de idempotencia con el `try` de toda la operación financiera en un único bloque. Esto hacía que `catch (Exception ex) when (SqlExceptionHelper.IsUniqueViolation(ex))` capturara **cualquier** violación UNIQUE ocurrida en **cualquier punto** de la operación — no solo en el `INSERT` de `wallet_idempotencia`, sino también en `ledger_transacciones`, `ledger_movimientos`, `wallet_movimientos`, `ventas_qr`, `auditoria` o el `SaveChangesAsync` final. Una violación UNIQUE real en cualquiera de esas tablas (un problema de integridad genuino) se habría interpretado incorrectamente como "esta operación ya se procesó" y habría devuelto una respuesta de reproducción (replay) — potencialmente incorrecta o inconsistente — en vez de un error 500, silenciando una corrupción de datos real.

**Por qué `IsUniqueViolation` no identifica una constraint específica**: a diferencia de `WalletCajaComercioService.EsViolacionUniqueUsuarioComercioFecha` (que además exige que el mensaje de SQL Server mencione el nombre exacto del índice `uq_wcc_usuario_comercio_fecha`), `SqlExceptionHelper.IsUniqueViolation` solo verifica el número de error (2627/2601) — deliberadamente, para no depender del texto del mensaje (que varía por idioma/versión de SQL Server). La consecuencia es que es verdadero ante **cualquier** violación UNIQUE de **cualquier** tabla. Por eso su alcance de uso debe controlarse en el código que lo invoca (el `try/catch` anidado), no en el helper mismo — documentado ahora explícitamente en el propio `SqlExceptionHelper.cs`.

**Corrección de try/catch**: en ambos métodos (`TransferirWalletAsync`, `PagarQrAsync`) se restauró la estructura anidada — un `try` **externo** que envuelve la operación completa (reserva + lógica financiera) con dos `catch` externos (`IsTransient` → `TransientDatabaseException`; genérico → rollback + rethrow), y dentro de él, un `try` **interno** que envuelve **exclusivamente** el `Add` de la reserva y su primer `SaveChangesAsync`, con un único `catch (Exception ex) when (IsUniqueViolation(ex))` que hace rollback, limpia el `ChangeTracker` y resuelve el replay. Cualquier violación UNIQUE posterior (ledger, movimientos, ventas QR, auditoría, `SaveChangesAsync` final) ya no coincide con ningún catch interno — cae directamente al `catch` genérico externo, se revierte la transacción y se relanza sin traducir, llegando al controller como una excepción no reconocida (no `InvalidOperationException`, no `IdempotencyConflictException`, no `TransientDatabaseException`) → cae en el `catch (Exception ex)` genérico del controller → `500` + `LogError`, exactamente el comportamiento pedido.

**Salvaguarda adicional no pedida explícitamente en el patrón, pero necesaria**: se agregó una variable local `rolledBack` porque el `catch` interno de `IsUniqueViolation` está lexicalmente anidado dentro del `try` externo — si `IdempotencyStore.ResolverReplayAsync` lanza una excepción (el caso más común: `IdempotencyConflictException` cuando el hash no coincide — **no es un caso raro**, es exactamente el vector de prueba "misma clave, valor diferente"), esa excepción es interceptable por los `catch` externos, que intentarían un segundo `RollbackAsync()` sobre una transacción ya revertida — esto lanza su propia excepción de ADO.NET y enmascararía el `409`/`503` real con un `500` genérico incorrecto. La bandera `rolledBack` evita ese segundo `RollbackAsync()` y garantiza que `IdempotencyConflictException`/`IdempotencyUnavailableException` lleguen al controller intactas. Esto no cambia el diseño pedido — es una corrección de correctitud necesaria para que el patrón anidado solicitado funcione sin efectos secundarios.

**Comportamiento de UNIQUE durante la reserva** (catch interno): `INSERT` en `wallet_idempotencia` compite por `uq_wallet_idempotencia_usuario_endpoint_key` → 2627 → rollback, `ChangeTracker.Clear()`, `ResolverReplayAsync` (replay/409/503 según corresponda, ver 17.5 de la etapa anterior).

**Comportamiento de UNIQUE posterior** (fuera de cualquier catch de idempotencia): una violación UNIQUE en cualquier tabla posterior a la reserva → no coincide con `IsUniqueViolation` de ningún catch activo en ese punto (el catch interno ya se cerró) → cae al `catch` genérico externo → rollback + rethrow → `500` + `LogError` en el controller, tratado como error de integridad inesperado, nunca como replay.

**Revisión completa del SQL**: ver sección 17.21 más abajo (contenido íntegro + resultado detallado de la revisión).

**Pruebas ejecutadas**: `dotnet build` y `npm run build`, ambos exitosos (ver 17.22 más abajo). `git diff --check` sin errores. **Ninguna prueba contra base de datos real** — el script sigue sin ejecutarse.

**Pendientes de QA**: sin cambios respecto a los 20 vectores de la sección 17.17 — ahora con la corrección de alcance del catch, los vectores 3/4/10 (misma clave, payload distinto → 409) y cualquier escenario de violación UNIQUE fuera de la reserva son los que más dependían de esta corrección para comportarse como se documentó.

**Pendiente visual — descripción fija "Pago a Comercio Demo XPAY QA"**: proviene de dos literales de cadena en `UserWalletPage.tsx` — línea con `return 'Pago a Comercio Demo XPAY QA';` dentro de `descripcionVisible()` (usada para mostrar el movimiento en el historial cuando `tipoMovimiento === 'PAGO_QR'`) y la línea `descripcion: 'Pago a Comercio Demo XPAY QA',` dentro de `handlePagarQr` (enviada como `descripcion` en el body de `POST /api/qr/pagar`). Es una constante textual sin ninguna implicación funcional ni de seguridad — no se modificó en esta etapa. Recomendación para la futura fase visual: reemplazarla por (a) el nombre real del comercio resuelto desde el QR (`comercio.Nombre` o equivalente, si el backend lo expusiera en la respuesta de pago), o (b) un texto neutro como "Pago mediante QR" si no se quiere depender de datos del comercio en el frontend. Registrado como pendiente, no implementado.

### 17.21 Revisión completa del script SQL (no ejecutado)

**Contenido completo de `database/029_wallet_idempotencia.sql`** (para revisión manual del usuario — reproducido íntegro):

```sql
-- =====================================================================
-- Migración 029: Idempotencia de Wallet (Fase 71.2-E-G)
-- Idempotente — no borra datos, no altera columnas existentes, no toca
-- ninguna otra tabla. Una sola tabla 100% nueva (wallet_idempotencia).
--
-- Diseño aprobado en docs/security/FASE_71.2_E_B_AUTORIZACION_IDOR.md,
-- secciones 16 (Fase 71.2-E-F, diseño de una sola transacción) y 17
-- (Fase 71.2-E-G, implementación). Resumen del uso previsto:
--   - EN_PROCESO se inserta como primer paso de la transacción financiera
--     existente (TransferirWalletAsync/PagarQrAsync) y se actualiza a
--     COMPLETADA justo antes del mismo COMMIT — nunca se confirma por
--     separado. Un rollback normal deshace la fila junto con el resto de
--     la operación: en operación normal NO deben quedar filas EN_PROCESO
--     persistidas (por eso el índice de limpieza de abajo está filtrado
--     por COMPLETADA, no por EN_PROCESO).
--   - La restricción UNIQUE (id_usuario, endpoint, idempotency_key) es el
--     mecanismo real de arbitraje entre solicitudes concurrentes con la
--     misma clave (ver sección 16.2 del documento de seguridad) — no un
--     candado aplicativo adicional.
--   - Sin FALLIDA: un intento fallido no deja fila (rollback atómico), no
--     hay nada que cachear para un reintento con la misma clave.
--
-- Esta migración NO crea: job de limpieza automática (retención de 7 días
-- documentada, sin proceso programado todavía), tabla de auditoría de
-- intentos fallidos, ni ningún objeto relacionado con QR dinámico.
--
-- Envuelta en transacción explícita (SET XACT_ABORT ON + BEGIN TRY/BEGIN
-- TRANSACTION + BEGIN CATCH/ROLLBACK/THROW), mismo patrón que las
-- migraciones 027/028.
--
-- NO EJECUTADA POR EL AGENTE — preparada para revisión y ejecución manual
-- del usuario.
-- =====================================================================

SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT '=== INICIO MIGRACIÓN 029: Idempotencia de Wallet ===';

    -- ── 1. Tabla wallet_idempotencia ─────────────────────────────────────

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='wallet_idempotencia')
    BEGIN
        CREATE TABLE wallet_idempotencia (
            id_idempotencia        BIGINT           IDENTITY(1,1) PRIMARY KEY,
            id_usuario              BIGINT           NOT NULL,
            endpoint                 VARCHAR(60)      NOT NULL,                 -- identificador lógico interno, p.ej. 'wallets.transferencia' — nunca la URL ni el casing HTTP
            idempotency_key           UNIQUEIDENTIFIER NOT NULL,                 -- generada por el cliente (crypto.randomUUID())
            request_hash               VARBINARY(32)    NOT NULL,                 -- SHA-256 (32 bytes) del payload normalizado
            estado                      VARCHAR(12)      NOT NULL DEFAULT 'EN_PROCESO',
            http_status                  SMALLINT         NULL,                     -- código HTTP de la respuesta original (200 en el único caso hoy soportado)
            id_recurso                    BIGINT           NULL,                     -- p.ej. id_transaccion_ledger (mismo valor que la columna siguiente en esta primera versión)
            id_transaccion_ledger           BIGINT           NULL,
            respuesta_data_json               NVARCHAR(1000)   NULL,                     -- solo el objeto "data" ya público de la respuesta — nunca JWT/documentos/nombres/correos/teléfonos
            fecha_creacion                     DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
            fecha_completado                     DATETIME2(3)     NULL,
            fecha_expiracion                       DATETIME2(3)     NOT NULL DEFAULT DATEADD(DAY, 7, SYSUTCDATETIME()),  -- ver justificación de 7 días en el documento de seguridad §16.10

            CONSTRAINT uq_wallet_idempotencia_usuario_endpoint_key
                UNIQUE (id_usuario, endpoint, idempotency_key),

            CONSTRAINT ck_wallet_idempotencia_estado
                CHECK (estado IN ('EN_PROCESO', 'COMPLETADA'))
        );
        PRINT 'OK: tabla wallet_idempotencia creada';
    END
    ELSE
        PRINT 'SKIP: tabla wallet_idempotencia ya existe';

    -- ── 2. Índice de limpieza/retención ──────────────────────────────────
    -- Filtrado por COMPLETADA (no por EN_PROCESO): bajo el diseño de una
    -- sola transacción, EN_PROCESO no queda persistido en operación normal
    -- (ver comentario superior) — un índice filtrado por ese estado no
    -- tendría filas que indexar en el caso esperado. La retención se
    -- centra en operaciones ya completadas.

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wallet_idempotencia_expiracion' AND object_id=OBJECT_ID('wallet_idempotencia'))
        CREATE INDEX ix_wallet_idempotencia_expiracion
            ON wallet_idempotencia (fecha_expiracion)
            WHERE estado = 'COMPLETADA';

    PRINT 'OK: índices de wallet_idempotencia verificados/creados';

    COMMIT TRANSACTION;
    PRINT '=== FIN MIGRACIÓN 029 (COMMIT OK) ===';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '=== ERROR EN MIGRACIÓN 029 — ROLLBACK EJECUTADO ===';
    THROW;
END CATCH;

-- ── 3. Verificación final ────────────────────────────────────────────────

SELECT name AS tabla FROM sys.tables WHERE name = 'wallet_idempotencia';

SELECT c.name AS columna, ty.name AS tipo, c.max_length, c.is_nullable
FROM sys.columns c
JOIN sys.tables t ON t.object_id = c.object_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE t.name = 'wallet_idempotencia'
ORDER BY c.column_id;

SELECT cc.name AS check_constraint, cc.definition
FROM sys.check_constraints cc
JOIN sys.tables t ON t.object_id = cc.parent_object_id
WHERE t.name = 'wallet_idempotencia';

SELECT i.name AS indice, i.is_unique, i.has_filter, i.filter_definition
FROM sys.indexes i
JOIN sys.tables t ON t.object_id = i.object_id
WHERE t.name = 'wallet_idempotencia' AND i.name IS NOT NULL;

PRINT '=== VERIFICACIÓN COMPLETA ===';
```

**Resultado detallado de la revisión** (checklist punto por punto):

| Verificación | Resultado |
|---|---|
| `SET XACT_ABORT ON;` | Presente (línea 35) |
| `TRY/CATCH` correctamente cerrado | `BEGIN TRY`/`END TRY`/`BEGIN CATCH`/`END CATCH` balanceados y anidados correctamente |
| `BEGIN TRANSACTION`/`COMMIT`/`ROLLBACK` | Presentes; `ROLLBACK` condicionado a `@@TRANCOUNT > 0` (evita error si la transacción ya no existe) |
| Guard de tabla | `IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='wallet_idempotencia')` — presente |
| Guard de constraint UNIQUE | **Sin guard independiente** — la restricción `UNIQUE` (y el `CHECK`) solo se crean como parte del `CREATE TABLE`, dentro del mismo bloque `IF NOT EXISTS` de la tabla. Si la tabla ya existe, no hay ningún `ALTER TABLE ... ADD CONSTRAINT` de respaldo. **Esto replica exactamente la misma limitación ya presente en la migración 028** (sus `CHECK`/`UNIQUE` tampoco tienen guard independiente) — no es una desviación introducida en este script, es el patrón ya aceptado en el repositorio. Reportado, no modificado. |
| Guard de índice de expiración | **Sí tiene guard independiente**: `IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_wallet_idempotencia_expiracion' AND object_id=OBJECT_ID('wallet_idempotencia'))` — se crearía aunque la tabla ya existiera de una ejecución anterior sin este índice |
| Nombres exactos | `wallet_idempotencia`, `uq_wallet_idempotencia_usuario_endpoint_key`, `ck_wallet_idempotencia_estado`, `ix_wallet_idempotencia_expiracion` — consistentes con la convención `uq_`/`ck_`/`ix_` + tabla + descriptor ya usada en 028 |
| Tipos y tamaños | `BIGINT` (IDs, coincide con `long` en C#), `VARCHAR(60)` endpoint (holgado para "wallets.transferencia"/8-21 caracteres reales), `UNIQUEIDENTIFIER` (coincide con `Guid`), `VARBINARY(32)` (SHA-256 = exactamente 32 bytes), `VARCHAR(12)` estado (10 caracteres reales, 2 de holgura), `SMALLINT` http_status (200 cabe cómodo), `NVARCHAR(1000)` respuesta_data_json — **coincide exactamente** con el límite verificado en código (`if (respuestaDataJson.Length > 1000) throw ...`), `DATETIME2(3)` en las 3 columnas de fecha (precisión de milisegundos; 028 usa `DATETIME2` sin precisión explícita — diferencia menor de estilo, no un defecto) |
| `CHECK` de estados | `CHECK (estado IN ('EN_PROCESO', 'COMPLETADA'))` — confirmado, sin `FALLIDA` |
| Default UTC | `fecha_creacion DEFAULT SYSUTCDATETIME()` — presente, mismo patrón que 027/028 |
| Default de `fecha_expiracion` | `DEFAULT DATEADD(DAY, 7, SYSUTCDATETIME())` — presente como red de seguridad (la app siempre la establece explícitamente en 7 días) |
| Ausencia de `DROP` | Confirmado — cero ocurrencias de `DROP` en todo el archivo |
| Ausencia de cambios a otras tablas | Confirmado — ningún `ALTER TABLE` sobre ninguna tabla distinta de `wallet_idempotencia` (a diferencia de 028, que sí alteraba `comercio_establecimientos`) |
| Comportamiento si la tabla existe parcialmente | El script no valida la estructura de una tabla preexistente — si existiera con columnas incorrectas o faltantes, el script la ignora silenciosamente (`PRINT 'SKIP...'`) sin detectar el problema |
| Comportamiento si existe la tabla pero falta constraint o índice | El índice se repararía en una segunda ejecución (guard independiente); el `UNIQUE`/`CHECK` **no** se repararían (ver fila "Guard de constraint UNIQUE" arriba) |
| Verificación final | 4 consultas de verificación (`sys.tables`, `sys.columns`+`sys.types`, `sys.check_constraints`, `sys.indexes`) + `PRINT` final — presente, mismo patrón que 028 |
| Ejecución repetida sin destruir datos | **Segura** — ambos guards (`IF NOT EXISTS`) evitan recreación/error; sin `DROP`; las consultas de verificación son de solo lectura |

**No se modificó el script** — no se identificó ningún problema concreto que lo requiriera; la única observación (ausencia de guard independiente para la restricción UNIQUE/CHECK) replica un patrón ya existente y aceptado en la migración 028, no un defecto nuevo.

### 17.22 Confirmación: SQL no ejecutado

**Ningún comando SQL fue ejecutado contra ninguna base de datos en esta sesión ni en ninguna anterior de esta cadena de fases.** El script permanece exactamente como se preparó, a la espera de revisión y ejecución manual del usuario.

### 17.23 Corrección E-G.2 — verificación estructural final

**¿La duplicación observada era real o un artefacto del diff?** **Artefacto.** Se leyó el contenido final completo de ambos métodos directamente del disco (no un diff) y se verificó con conteos exactos (`grep -c`) sobre el rango de líneas de cada método: `IdempotencyStore.NuevaReserva` → 1, `_db.WalletIdempotencias.Add` → 1, `catch (...) when (SqlExceptionHelper.IsUniqueViolation(...))` → 1, `transaction.CommitAsync()` → 1, en **ambos** métodos. No existe ninguna segunda reserva, ningún segundo `Add`, ningún segundo `SaveChangesAsync` de reserva ni una segunda transacción. La estructura ya coincidía exactamente con el patrón corregido en 71.2-E-G.1 — no se modificó ningún archivo `.cs` en esta etapa salvo el script SQL.

**Contenido final del patrón** (idéntico en ambos métodos, ver 17.9 más abajo para la reproducción completa): `BeginTransactionAsync` → `var rolledBack = false` → `try` externo → (`NuevaReserva` + `Add` + `try` interno con `SaveChangesAsync` + único `catch IsUniqueViolation` que hace rollback/marca `rolledBack`/limpia tracking/resuelve replay) → resto de la operación financiera sin cambios → `MarcarCompletada` → `SaveChangesAsync` final → validación de balance → `CommitAsync` → `catch IsTransient` externo (con guard `!rolledBack`) → `catch` genérico externo (con guard `!rolledBack`).

**Conteos verificados** (`WalletOperacionService.TransferirWalletAsync`, líneas 124-384 del archivo; `PagoQrService.PagarQrAsync`, líneas 29-252):

| Elemento | TransferirWalletAsync | PagarQrAsync |
|---|---|---|
| `IdempotencyStore.NuevaReserva` | 1 | 1 |
| `WalletIdempotencias.Add` | 1 | 1 |
| `catch (...) when (IsUniqueViolation(...))` | 1 | 1 |
| `catch (...) when (IsUniqueViolation(...))` fuera del try interno | 0 | 0 |
| `transaction.CommitAsync()` | 1 | 1 |
| `SaveChangesAsync` total (código real, sin contar comentarios) | 3 (reserva; ledger tx; final) | 4 (reserva; ledger tx; venta QR; final) |
| `SaveChangesAsync` antes del try/catch interno de la reserva | 0 | 0 |

Los recuentos de `SaveChangesAsync` (3 y 4) coinciden exactamente con la cantidad que cada método ya tenía **antes** de agregar idempotencia (2 y 3 respectivamente) más el nuevo `SaveChangesAsync` de la reserva — no hay ninguno adicional inexplicado.

**Orden de `MarcarCompletada`, balance y `Commit`** (sección 3 de esta etapa): en ambos métodos el orden real es idéntico: construir el DTO de resultado → serializar y verificar el límite de 1000 caracteres → `IdempotencyStore.MarcarCompletada(idem, ...)` → `SaveChangesAsync` final (persiste ledger/movimientos/auditoría ya agregados **y** la actualización de `idem` a `COMPLETADA`, todos tracked en el mismo contexto) → cálculo y verificación de balance débito=crédito → `transaction.CommitAsync()`. **Ningún `CommitAsync` ocurre antes de la verificación de balance** — el orden es correcto y coincide con el criterio aceptado explícitamente por la instrucción ("si la validación de balance ocurre después de actualizar la entidad a COMPLETADA pero antes del commit, es aceptable porque un fallo hace rollback de todo").

**Usos de `SqlExceptionHelper.IsUniqueViolation`** — búsqueda exhaustiva en todo el proyecto (`grep -rn` sobre `*.cs`): exactamente 2 usos reales en todo el código (más una mención en un comentario de `IdempotencyStore.cs`, sin efecto):
- `Services/WalletOperacionService.cs:168` — catch interno de `TransferirWalletAsync`, acotado exclusivamente al `SaveChangesAsync` de la reserva.
- `Services/PagoQrService.cs:62` — catch interno de `PagarQrAsync`, mismo alcance.

Ningún uso envuelve una operación financiera completa. `SqlExceptionHelper.IsTransient` se usa 2 veces, ambas como catch **externo** (cobertura de toda la operación, incluida la reserva) — comportamiento correcto y ya documentado en 17.20.

`SqlExceptionHelper.cs` confirmado sin cambios necesarios en esta etapa (ya corregido en 71.2-E-G.1): recorre `InnerException` con límite defensivo de 10 niveles (`TryGetSqlException`), inspecciona `SqlException.Errors` completo (`HasErrorNumber`), reconoce 2601/2627 y 1205/1222 exclusivamente por `.Number`, sin depender de texto. El propio archivo documenta explícitamente que `IsUniqueViolation` no identifica qué restricción falló y por qué su uso debe acotarse al primer `SaveChangesAsync` de la reserva.

**Endurecimiento fail-fast del script SQL** (`database/029_wallet_idempotencia.sql`, reescrito completo — ver contenido íntegro en la entrega): usa `OBJECT_ID('dbo.wallet_idempotencia', 'U')` en vez de `sys.tables WHERE name=...`, y filtra `sys.columns`/`sys.key_constraints`/`sys.check_constraints`/`sys.indexes` por ese `object_id`, no por nombre de tabla suelto. Comportamiento:
- **Tabla no existe**: la crea completa (PK, UNIQUE, CHECK, y luego el índice de expiración).
- **Tabla existe y su estructura crítica coincide**: no la toca, solo verifica y continúa (crea el índice si faltara).
- **Tabla existe pero le faltan columnas requeridas, o alguna columna crítica tiene tipo/tamaño distinto al esperado, o falta la PK/UNIQUE/CHECK**: `THROW` con un mensaje explícito identificando exactamente qué falta o no coincide, sin agregar columnas, sin recrear constraints, sin borrar nada — la migración aborta (rollback automático por `SET XACT_ABORT ON` + el `BEGIN CATCH` existente).
- El índice de expiración solo se evalúa/crea **después** de que todas las verificaciones estructurales anteriores superaron (o la tabla se acabó de crear completa).
- La verificación final ya no es una simple lista de objetos: calcula banderas `tabla_ok`/`columnas_ok`/`unique_ok`/`check_ok`/`indice_ok` y una columna `resultado` con un mensaje explícito — es una confirmación legible independiente de la aplicación de la política (que ya ocurrió vía `THROW` dentro de la transacción).
- Se mantienen sin cambios: `SET XACT_ABORT ON`, `BEGIN TRY/CATCH`, transacción explícita, rollback condicionado (`IF @@TRANCOUNT > 0`), ausencia de `DROP`, retención de 7 días, `CHECK` solo `EN_PROCESO`/`COMPLETADA`, `UNIQUE (id_usuario, endpoint, idempotency_key)`, sin cambios a ninguna otra tabla.

**Resultados de builds**: `dotnet build` → `Build succeeded. 0 Warning(s). 0 Error(s).` `npm run build` → 0 errores TypeScript (advertencia preexistente de tamaño de chunk, no relacionada). `git diff --check` sin errores.

**SQL no ejecutado**: confirmado — ningún comando se envió a ninguna base de datos en esta etapa.

**Pendientes para QA y revisión visual**: sin cambios — siguen bloqueados hasta autorización explícita de una etapa posterior. El texto fijo `"Pago a Comercio Demo XPAY QA"` (registrado en 17.20) sigue pendiente para la fase visual, sin modificar.

### 17.24 Cierre del pendiente RCSI — resultado real de QA (71.2-E-G.3)

**Acción realizada por el usuario, no por el agente**: ejecución manual, de solo lectura, de la consulta preparada en la sección 15.2, directamente contra la base `sqldb-xpay-qa`.

**Consulta ejecutada:**
```sql
SELECT name, is_read_committed_snapshot_on, snapshot_isolation_state_desc
FROM sys.databases WHERE name = DB_NAME();
```

**Resultado reportado:**

| Campo | Valor |
|---|---|
| `base_datos` | `sqldb-xpay-qa` |
| `snapshot_isolation_state_desc` | `ON` |
| `is_read_committed_snapshot_on` | `True` |

**Interpretación**: RCSI está habilitado en QA, y `SNAPSHOT` isolation también está disponible (no solo RCSI). Esto confirma la hipótesis planteada desde 71.2-E-E (sección 15.1): Azure SQL Database aprovisionó esta base con `READ_COMMITTED_SNAPSHOT = ON` por defecto, y nadie lo desactivó.

**Qué cambia en la práctica**: nada en el código. Como se documentó desde 71.2-E-E, `WITH (UPDLOCK, ROWLOCK)` es un lock explícito que se comporta igual con RCSI activo o no — la protección ya aplicada en `TransferirWalletAsync`, `PagarQrAsync`, `RecargarWalletManualAsync`, `SolicitarRetiroAsync`, `RechazarRetiroAsync` y `ConfirmarRetiroPagadoAsync`/`RechazarRetiroAsync` (los locks sobre `retiros_comercio`) es correcta y suficiente bajo el escenario real confirmado. Lo único que cambia es que el diagnóstico narrado en la sección 14.2 (cómo se producía la carrera *antes* de la corrección) debe leerse bajo el mecanismo de versionado de filas de RCSI (lecturas planas consistentes sin bloqueo) en vez de bajo locks S de liberación rápida — el resultado observable (actualización perdida) es el mismo, el mecanismo de fondo que lo permitía es el que corresponde a RCSI activo.

**Qué NO se cierra con este resultado**: el valor de RCSI en un eventual entorno de producción separado (si existiera y difiriera de QA) sigue sin verificar — esta confirmación es específica de `sqldb-xpay-qa`. No se afirma nada sobre producción.

**Estado**: pendiente cerrado para QA. Documentación actualizada en las secciones 15.1, 15.2, 16.14, y en los riesgos residuales de 15.18/16.16 (marcados como resueltos con referencia cruzada a esta sección).

**No se realizó ningún otro cambio en esta actualización**: no se ejecutó `database/029_wallet_idempotencia.sql`, no se creó ninguna migración adicional, no se hizo commit, push ni despliegue, no se inició QA de la aplicación.

### 17.25 Ejecución de la migración 029 — aplicada y verificada en QA

**Acción realizada por el usuario, no por el agente**: ejecución manual de `database/029_wallet_idempotencia.sql` (versión endurecida de 71.2-E-G.2 — validación estructural fail-fast, esquema `dbo.` explícito, sin `DROP`, sin cambios a otras tablas) contra la base de QA.

| Campo | Valor |
|---|---|
| Fecha de ejecución | 2026-07-28 |
| Base de datos | `sqldb-xpay-qa` |
| Ejecutado por | Usuario (manual, no por el agente) |
| Ejecución en producción | **No realizada** — sin cambios en ningún entorno distinto de QA |

**Resultado de la verificación final del script** (banderas `tabla_ok`/`columnas_ok`/`unique_ok`/`check_ok`/`indice_ok` definidas en la sección final del script, ver 17.20/17.21):

| Verificación | Resultado |
|---|---|
| `tabla_ok` | 1 |
| `columnas_ok` | 1 |
| `unique_ok` | 1 |
| `check_ok` | 1 |
| `indice_ok` | 1 |

Las cinco verificaciones estructurales dieron `1` — confirma que `dbo.wallet_idempotencia` existe con las 13 columnas requeridas, la restricción `UNIQUE (id_usuario, endpoint, idempotency_key)`, el `CHECK (estado IN ('EN_PROCESO','COMPLETADA'))` y el índice `ix_wallet_idempotencia_expiracion`, tal como fueron diseñados y endurecidos en 71.2-E-G.2.

**Estado**: la migración 029 queda **aplicada y verificada correctamente en QA** (`sqldb-xpay-qa`). El código de `WalletOperacionService.TransferirWalletAsync`/`PagoQrService.PagarQrAsync` (ya implementado desde 71.2-E-G, corregido en 71.2-E-G.1/G.2) ahora tiene la tabla real que necesita para operar — la idempotencia por `Idempotency-Key` en transferencia y pago QR es, a partir de este punto, ejecutable de punta a punta en QA por primera vez en esta cadena de fases.

**Lo que sigue sin ocurrir**: sin commit, sin push, sin despliegue, sin ejecución en producción, sin QA técnico/funcional todavía iniciado (plan preparado en la siguiente sección, a la espera de autorización explícita para ejecutarlo).
