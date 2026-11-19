# Piloto privado PROD — configuración de controles

Claves de configuración (App Service) introducidas por las correcciones mínimas
del piloto. Aquí solo hay **nombres y reglas**; los valores reales viven fuera
del repositorio. Cambiar un ajuste del App Service reinicia la aplicación: ese
es el tiempo de reacción del interruptor.

## Corrección 1 — Veriff por ambiente

| Clave | Regla |
|---|---|
| `VERIFF_CALLBACK_URL` | Obligatoria en todo ambiente. Es el **retorno del navegador** al terminar Veriff (`verification.callback`), no el webhook: página del frontend `https` absoluta, sin credenciales, query ni fragmento y fuera de `/api/` (PROD: `https://app.xpaycol.com/mi-wallet`). Si falta o es inválida, no se crean sesiones (503). |
| Webhook Veriff | Se configura en Veriff Station (eventos y decisiones) → `https://<api>/api/kyc/veriff/webhook`. Las decisiones actualizan el KYC; los eventos se aceptan con firma válida (200) y no cambian estado. |

`vendorData`: `XPAY-PROD-USUARIO-{id}` en productivo, `XPAY-QA-USUARIO-{id}` en el resto.

## Corrección 2 — alta controlada de usuarios

| Pieza | Regla |
|---|---|
| `database/prod/operacion/crear_primer_admin.sql` | Primer SUPERUSUARIO + ADMIN_XPAY. Un solo uso. Hash desde `database/prod/tools/hash_clave`. Cambio de clave obligatorio en el primer ingreso. |
| `XPAY_PILOTO_ALTA_HABILITADA` | Solo `true` exacto habilita `POST api/admin/usuarios/piloto`; en otro caso responde 404. Recomendado: activarla solo durante el alta y retirarla después. |
| `XPAY_PILOTO_MAX_USUARIOS` | Entero > 0. Máximo de altas por esta vía en toda la vida de la base (cuenta `auditoria.ADMIN_ALTA_USUARIO_PILOTO`). |

El usuario del piloto nace `ACTIVO` sin clave utilizable, con `email_verificado = 0`,
sin documento y con KYC `NO_INICIADO`. La clave se emite con
`POST api/admin/usuarios/{id}/restablecer-clave` (cambio obligatorio en el primer
ingreso). El alta exige versiones de T&C, política de datos y autorización KYC,
fecha de aceptación y referencia de evidencia, que quedan en auditoría. Cuentas
separadas: el administrador no puede restablecer su propia clave.

El18Nov2026 el propietario autorizó ampliar a **50** el valor de
`XPAY_PILOTO_MAX_USUARIOS`. Ese máximo cuenta las altas administrativas
auditadas desde el inicio; no agrega50 usuarios ni convierte autorregistros
en participantes financieros. La apertura del registro público crea cuentas
limitadas según `REGISTRO_PUBLICO_PROD_CONFIGURACION.md`.

## Corrección 3 — interruptor y límites

| Clave | Productivo | No productivo |
|---|---|---|
| `XPAY_OPERACIONES_FINANCIERAS_HABILITADAS` | Solo `true` exacto permite anticipos y retiros nuevos. | Ausente = permitido; presente distinto de `true` = pausa. |
| `XPAY_UVB_VALOR_COP` | Obligatoria: valor oficial de la UVB del año, entero en pesos. | Ausente = sin tope por UVB. |
| `XPAY_UVB_ANIO` | Obligatoria: debe ser el año en curso de Colombia; si no, los retiros se bloquean (1 de enero incluido). | Igual, si se configura la UVB. |
| `XPAY_TOPE_RETIRO_DIARIO_COP` | Obligatoria: límite **interno de XPAY** (inicial 500000) por usuario y día calendario de Colombia. No es una regla de Bre-B. | Ausente = sin límite diario. |

- Anticipos: sin tope fijo nuevo; siguen limitados por el porcentaje del pago de
  nómina, el cupo disponible y el mínimo del convenio. Solo los afecta el interruptor.
- Retiro Bre-B: saldo disponible y máximo 1.000 UVB por operación (constante en
  código). El acumulado diario cuenta todo salvo `RECHAZADO`, `ERROR` y
  `CANCELADO`; los envíos inciertos cuentan. Se calcula bajo el AppLock
  `XPAY:BREB_RETIRO_USUARIO:{id}` y el `UPDLOCK` del saldo.
- Con el interruptor cerrado también responden 503: crear un lote de recaudo,
  generar su QR, el pago simulado de empresa (solo QA) y, en la revisión de
  retiros, el rechazo confirmado, liberar un pendiente de envío y actualizar el
  estado. Siguen disponibles las consultas y cancelar un lote.
- El webhook de Passport exige el interruptor para aplicar el recaudo: con el
  interruptor cerrado la aplicación falla y el webhook responde 500. Que Passport
  reintente la entrega, y durante cuánto tiempo, no está verificado: no cerrar el
  interruptor con lotes en `PENDIENTE_PAGO` o `PAGO_EN_PROCESO`.
- Respuestas: pausa o configuración inválida → 503; tope o límite diario → 400.

## Corrección 4 — PIN transaccional

Sin claves de configuración: usa la tabla existente `pin_transaccional`.
La migración `053_pin_transaccional_seis_digitos.sql` agrega `longitud_pin`
nullable y debe aplicarse antes de publicar la API nueva. No modifica hashes,
intentos ni bloqueos de los PIN existentes; su longitud queda desconocida
(`NULL`) hasta que su titular cambie el PIN.

| Pieza | Regla |
|---|---|
| PIN | 6 dígitos numéricos al crear un PIN o confirmar una operación. Se rechazan los PIN nuevos de dígitos todos iguales o consecutivos. Se guarda el hash BCrypt y la longitud conocida, nunca el PIN en claro. |
| Crear | `POST api/pin` con la contraseña de ingreso del usuario. Se presenta al ingresar a las cuentas con Wallet que aún no tienen PIN; también está disponible en Perfil. |
| Cambiar | `POST api/pin/cambiar` con el PIN actual (cuenta intentos). Los PIN anteriores de 5 o 7 dígitos solo sirven para este cambio autenticado a uno nuevo de 6. No se convierten ni se reinician automáticamente. |
| Exigido en | `POST api/libranza/cliente/anticipos`, `POST api/breb/retiros/real`, `POST api/wallets/transferencia`, `POST api/qr/pagar` (`[RequierePinTransaccional]`, después de `[OperacionFinanciera]`). |
| Respuestas | 400 `PIN_REQUERIDO` / `PIN_INCORRECTO` (con intentos restantes), 428 `PIN_NO_CONFIGURADO` / `PIN_ACTUALIZACION_REQUERIDA`, 423 `PIN_BLOQUEADO`. Un PIN anterior no consume intentos al rechazarse para operar; comprobarlo para cambiarlo sí cuenta intentos. |
| Bloqueo | 5 intentos fallidos. Cada intento se reserva con un UPDATE atómico antes de comparar: ni en paralelo se prueban más de 5. Un acierto reinicia el contador. |
| Restablecer | `POST api/admin/usuarios/{id}/restablecer-pin` (ADMIN_XPAY/SUPERUSUARIO, nunca sobre la propia cuenta). El PIN queda `RESTABLECIDO` y el usuario crea uno nuevo. Auditoría `PIN_RESTABLECER`. |

Después del PIN se presenta el registro de una llave Bre-B propia. Puede
posponerse: la llave solo es obligatoria para retirar al banco, no para
transferir entre Wallets ni pagar con QR. El retiro conserva la exigencia de
llave propia validada y resolución Passport vigente. Las cuentas de registro
público limitado siguen en Perfil, sin consultar PIN/llave ni habilitar Wallet.

Publicación: aplicar y verificar la migración 053, publicar la API y después
la interfaz. No hacer rollback automático a una API que solo admite PIN de 5
dígitos una vez que usuarios hayan creado o cambiado su PIN a 6. Conservar la
columna y los hashes ante una recuperación; resolver mediante una versión
compatible. Este cambio no autoriza operaciones de dinero ni altera saldos.

Fuera de alcance: la cartera ordinaria (aplazada) y la recarga en comercio
siguen validando solo el formato del campo `pin` que ya traían.

## Corrección 5 — verificación de correo y celular

Migración nueva `database/050_verificacion_contactos.sql` (tablas
`contacto_codigos_verificacion` y `usuario_consentimientos_comunicacion`).
Configuración de Azure y Meta: `docs/VERIFICACION_CONTACTOS_CONFIGURACION.md`.

| Pieza | Regla |
|---|---|
| Canales | Correo: Azure Communication Services Email (identidad administrada, sin secreto). Celular: WhatsApp Cloud API con plantilla de autenticación. Sin configuración, el canal queda «no disponible»; nunca simula éxito. |
| Código | 6 dígitos; solo se guarda SHA-256(sal ‖ código). Vigencia 10 min, un solo uso, 5 intentos fallidos lo agotan. Nunca se registra en logs. |
| Reenvío | 60 s entre códigos; 5 por usuario y canal en 24 h; 8 por destino y canal en 24 h. Los envíos fallidos cuentan. Además `ContactosCodigoPolicy` (10 solicitudes / 5 min por usuario). |
| Verificado | Solo cuando el usuario escribe el código correcto. La entrega del proveedor no verifica nada. No se verifica un contacto que otra cuenta ya tiene verificado. |
| Cambio de dato | Perfil revoca la verificación y anula los códigos pendientes; además cada código queda atado al destino al que se envió. |
| Consentimiento | WhatsApp exige autorización de mensajes de SERVICIO (se registra al pedir el código). La PUBLICIDAD por correo o WhatsApp es otra autorización, desactivada por defecto. Historial solo de inserción. |
| Endpoints | `GET api/contactos/estado`, `POST api/contactos/{email|celular}/codigo`, `POST api/contactos/{email|celular}/verificar`, `PUT api/contactos/preferencias`. No dependen del interruptor financiero ni de KYC. |
| Requisito | `[RequiereContactosVerificados]` en los mismos 4 endpoints del PIN, antes del PIN. Solo es efectivo con `XPAY_CONTACTOS_VERIFICADOS_REQUERIDOS=true` **y** ambos canales configurados; si no, deja pasar. Respuesta 428 `CONTACTOS_NO_VERIFICADOS`. Webhooks, conciliación, saldos, perfil, KYC y verificación no lo llevan. |
