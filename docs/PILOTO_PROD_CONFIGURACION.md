# Piloto privado PROD — configuración de controles

Claves de configuración (App Service) introducidas por las correcciones mínimas
del piloto. Aquí solo hay **nombres y reglas**; los valores reales viven fuera
del repositorio. Cambiar un ajuste del App Service reinicia la aplicación: ese
es el tiempo de reacción del interruptor.

## Corrección 1 — Veriff por ambiente

| Clave | Regla |
|---|---|
| `VERIFF_CALLBACK_URL` | Obligatoria en todo ambiente. `https` absoluta, sin credenciales, query ni fragmento, ruta exacta `/api/kyc/veriff/webhook`. Si falta o es inválida, no se crean sesiones (503). |

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
- Nunca se pausan: lotes y QR de recaudo, webhook de Passport, aplicación del
  recaudo, conciliación y resolución de envíos inciertos.
- Respuestas: pausa o configuración inválida → 503; tope o límite diario → 400.
