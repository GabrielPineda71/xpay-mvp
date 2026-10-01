# M5 — Registro de envío de evidencias y congelación operativa (XPAY-627)

**Registrado:** 2026-09-30 22:43 (hora local, UTC-05) · `2026-10-01T03:43:50Z`
**Modo:** 100 % local. `PASSPORT_API_CALLS=0`, `FINANCIAL_OPERATIONS_EXECUTED=0`.

---

## 1. Envío

| Dato | Valor |
|---|---|
| Estado | **ENVIADO** |
| Método | **Manual, por el operador**, desde el hilo de correo existente |
| Fuente de la confirmación | **El operador** («listo enviado») |
| Adjuntos | **8** |
| Commit del paquete | `28a96737a89c4b6a1cb3f71b1ee63f19b6bd9767` |
| Asunto | «XPAY — Evidencias restantes Módulo 5 — Certificación Bre-B» |

### Alcance de esta confirmación

Este registro documenta **lo que el operador reportó**. No se verificó Gmail, ningún servidor de
correo ni ninguna bandeja: este entorno no tiene acceso a correo, y no se intentó obtenerlo. La
confirmación de entrega es un hecho operativo reportado, no una comprobación técnica.

### Los 8 adjuntos

Desde `docs/certificacion/passport-breb/entrega-final-m5/`, verificados byte a byte contra el
commit `28a9673` inmediatamente antes del envío:

| # | Archivo | Caso |
|---|---|---|
| 1 | `XPAY_M5_EVIDENCIAS_FINALES_PARA_PASSPORT.md` | narrativa para el proveedor |
| 2 | `M5-T5-E-01-Key-Create.json` | T5-E fase 1 — clave creada (ACTIVE) |
| 3 | `M5-T5-E-02-Key-Delete.json` | T5-E fase 2 — eliminación reconocida (204) |
| 4 | `M5-T5-E-03-Deleted-Key-Resolve.json` | T5-E fase 3 — Resolve HTTP 404 |
| 5 | `M5-T5-C-Over-Limit-Attempt.json` | T5-C — intento > 12M, HTTP 400 |
| 6 | `M5-T6-T7-01-Resolve.json` | soporte T6/T7 — Resolve |
| 7 | `M5-T6-T7-02-Payment-Create.json` | soporte T6/T7 — Payment Create |
| 8 | `M5-T8-Payment-Retrieve-Settled.json` | T8 — Retrieve SETTLED |

No se adjuntó el resumen ejecutivo interno, ni el borrador de correo, ni el paquete del 26 de
septiembre, ni ningún documento interno, ni ningún artefacto privado.

---

## 2. Estado del Módulo 5

| Caso | Estado |
|---|---|
| T5-A, T5-B | CLOSED_WITH_DOCUMENTED_CAVEAT (rechazo por validación local, antes de Passport) |
| **T5-C** | **INCONCLUSIVE** |
| T5-D | CLOSED_WITH_DOCUMENTED_CAVEAT (HTTP 400, U805) |
| T5-E | PASS (Create → Delete 204 → Resolve HTTP 404) |
| T5-F | CLOSED_WITH_DOCUMENTED_CAVEAT (HTTP 400, UUID v4 expected) |
| T6, T7, T8 | PASS |

### T5-C sigue no concluyente

- `T5_C_REQUEST_EXECUTED = YES` — se envió una solicitud real por 12.000.001,00 COP.
- `T5_C_LIMIT_REJECTION_DEMONSTRATED = NO` — Passport respondió HTTP 400 `service_blocked` por
  saldo disponible insuficiente, así que la regla de límite nunca llegó a evaluarse.

Este estado **no cambia** con el envío. Entregarlo no lo convierte en superado.

### Pendiente del lado del proveedor

`M5_PROVIDER_REVIEW_ITEMS = 1`

> **T5-C > 12M:** Sandbox rechazó por fondos disponibles insuficientes antes de que pudiera
> demostrarse de forma concluyente la validación específica del límite.

`PASSPORT_REVIEW_STATUS = AWAITING_PROVIDER_REVIEW`

---

## 3. Congelación operativa

**`PASSPORT_ACTIVE_EXECUTION = FROZEN`** desde este registro.

Hasta recibir una respuesta nueva de Gustavo o Passport, **ninguna** de estas operaciones está
autorizada:

- repetir T5-C · Payment · Resolve · Retrieve
- T6 · T7 · T8
- Create / Delete / Suspend / Activate / List Keys

Cuando llegue una respuesta del proveedor, debe revisarse **antes** de autorizar cualquier
llamada nueva.

### Estado que la congelación debe respetar

- El repeat guard de **T5-C está consumido**. Un segundo intento exigiría archivarlo bajo
  autorización explícita; el ejecutor lo bloquea por diseño.
- Los **tres guards de T5-E están consumidos**; ese subcaso no es repetible sobre el directorio
  privado actual.
- Los artefactos privados (modo 600, directorio 700, fuera del repositorio) se conservan
  íntegros para trazabilidad, incluido el archivo reversible de los Resolve históricos.
- La resolución usada en T5-C quedó **sin consumir** pero venció a las `03:37:13Z`.

---

## 4. Lo que no se hizo en este ticket

Ninguna llamada a Passport. Ningún correo. No se tocó el Dashboard, Azure ni MiDecisor. No se
modificó el paquete enviado, ni la evidencia histórica, ni código funcional. No se registró la
dirección de correo del destinatario.

## Seguridad de este documento

Sin secretos, credenciales, PII, direcciones de correo ni identificadores de Passport. Las únicas
cifras son el monto de la prueba, el estado HTTP y los SHA de commits del propio repositorio.
