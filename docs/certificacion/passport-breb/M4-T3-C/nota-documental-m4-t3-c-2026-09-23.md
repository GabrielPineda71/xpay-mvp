# M4-T3-C — Nota documental complementaria (customer_id incorrecto)

**Tipo de documento:** evidencia documental complementaria (narrativa). Este
archivo **no reemplaza ni modifica** ninguna evidencia técnica cruda — solo
la contextualiza para su consolidación posterior.

## Objetivo del escenario

Caso obligatorio del Anexo Passport, Módulo 4: **"Crear QR con customer_id
incorrecto"**. El objetivo fue aislar exclusivamente la variable
`customer_id`, manteniendo una llave Bre-B **ACTIVE válida** (la misma llave
protegida ya usada en M4-T1/M4-T2), para obtener evidencia real y trazable
de ese escenario negativo.

## Ambiente

Passport Sandbox (`environment: "sandbox"` en la evidencia referenciada).

## Fecha/hora de la evidencia real

Derivada directamente de la evidencia: `executed_at_utc: 2026-09-23T00:57:35Z`.

## Preparación del caso

A diferencia de M4-T3-A/M4-T3-B (que requirieron preparar un fixture
desechable dedicado), M4-T3-C no crea ni elimina ninguna llave — utiliza
por diseño, de forma exclusivamente read/reference, la misma llave ACTIVE
protegida ya usada en M4-T1/M4-T2. Antes de la ejecución real (XPAY-500,
XPAY-501) se verificó dos veces, localmente y sin imprimir el valor, que el
fingerprint de esa llave seguía siendo el esperado.

## Confirmación de llave ACTIVE válida

**Fingerprint sanitizado de la llave utilizada:**

```
9315E012C94C
```

Verificaciones realizadas:

- XPAY-500 (previo a la ejecución): `ACTIVE_KEY_FINGERPRINT_MATCH=YES`,
  `USES_ACTIVE_M4_KEY=YES`, `USES_DELETED_M4_T3_B_KEY=NO`,
  `USES_SUSPENDED_M4_T3_A_KEY=NO`, `USES_CONTAMINATED_M3_TARGET=NO`.
- XPAY-501 (inmediatamente antes de la llamada real):
  `ACTIVE_KEY_FINGERPRINT_12=9315E012C94C` == `EXPECTED_ACTIVE_KEY_FINGERPRINT_12`.
- La llave **no fue modificada** en ningún momento
  (`ACTIVE_M4_KEY_MUTATED=NO`) — estructuralmente imposible desde este
  executor, que solo recibe `IPassportQrClient` (sin acceso a
  `IPassportKeyClient`).

Esto permite documentar que la única variable deliberadamente incorrecta
del escenario fue el `customer_id` — la llave permaneció válida y activa en
todo momento.

## customer_id incorrecto

Mecanismo utilizado: `InvalidCustomerIdGenerator.Generate()` — un
identificador **sintético, determinista y dedicado exclusivamente a este
escenario negativo**, nunca derivado de `PASSPORT_TEST_CUSTOMER_ID` ni
persistido en `~/.passport-sandbox.env`. No se incluye aquí el valor
generado, real o sintético.

La ejecución real reveló empíricamente que Passport exige, para este
parámetro, un identificador con formato **UUID versión 4** — esto es un
hecho observado en la respuesta real, no una suposición previa (el propio
código del generador documentaba explícitamente que ese requisito no
estaba confirmado localmente antes de esta ejecución).

## Secuencia ejecutada

Una única llamada real: `create-qr-static-invalid-customer`
(`POST /v1/qrcodes`), usando la llave ACTIVE (fingerprint `9315E012C94C`,
verificado localmente segundos antes) y el customer_id sintético descrito
arriba. Passport respondió:

```
HTTP 400
error_code: invalid_parameter_value
error_message: "UUID version 4 expected"
```

## Interpretación (A/B/C)

**A) Hecho observado:** Passport rechazó la solicitud de creación del QR
con `HTTP 400`, `error_code=invalid_parameter_value`, mensaje literal
`"UUID version 4 expected"`.

**B) Interpretación técnica:** el rechazo fue provocado por el customer_id
sintético incorrecto utilizado en el escenario — la respuesta demuestra
específicamente que el valor enviado no cumplía la validación de formato
que Passport exige para `customer_id` (UUID versión 4). Esta nota **no**
reinterpreta ese mensaje como "customer inexistente" ni como ningún otro
motivo — Passport nunca devolvió esa u otra formulación; se documenta
literalmente la causa observada.

**C) Estado de certificación:** evidencia preparada para revisión de
Passport. `review_status` de la evidencia técnica permanece
`PENDING_PASSPORT_REVIEW` — ese campo lo actualiza Passport fuera de este
repositorio, nunca esta nota ni XPAY.

## Limitación probatoria — request_sanitized vacío

**`REQUEST_SANITIZED_PRESENT=NO`.** La ruta de fallo deja
`request_sanitized` **vacío por diseño** (mismo criterio ya documentado
para M4-T3-B: el evidence builder nunca fabrica un campo que Passport no
confirmó en su respuesta de error). Por tanto, **el `evidence.json` de
M4-T3-C NO contiene un campo directamente cruzable** con el fingerprint de
la llave utilizada — no se afirma que `9315E012C94C` esté almacenado dentro
de ese archivo, porque no lo está.

La identidad de la llave utilizada se sustenta, en cambio, en
**trazabilidad técnica complementaria** — nunca presentada como un dato
devuelto por Passport — compuesta por:

1. el precheck local verificado inmediatamente antes de la llamada
   (`9315E012C94C`, XPAY-501 §3);
2. la variable `PASSPORT_TEST_QR_KEY_ID`, sin modificación;
3. el fingerprint sanitizado `9315E012C94C`;
4. el código de `create-qr-static-invalid-customer`, previamente
   auditado, que lee esa variable de forma exclusiva y sin transformación
   ni fallback;
5. el aislamiento estructural del executor (sin acceso a ninguna API de
   mutación de llaves);
6. la ausencia comprobada de cualquier modificación del entorno
   (`ENV_MODIFIED=NO`).

**Origen del fingerprint reportado en esta nota:** `PRECHECK_LOCAL` — no
`EVIDENCE`.

## Evidencia referenciada

| Caso | Ruta | SHA256 |
|---|---|---|
| QR real M4-T3-C | `docs/certificacion/passport-breb/M4-T3-C/evidence-2026-09-23T00-57-35Z.json` | `94bc2083db730131c3e3210231a6a51e298cfc2ec9ac4adab085082dc2da1a0c` |

Este archivo permanece **sin modificar** — verificado byte-idéntico en el
momento de escribir esta nota.

## Declaraciones explícitas

- **Sin retry:** la llamada se ejecutó exactamente una vez contra Passport
  Sandbox.
- **Sin repetición para buscar otro error:** no se realizó una segunda
  llamada con otro customer_id, ni se intentó obtener artificialmente un
  mensaje de error distinto al observado. La evidencia preserva el
  comportamiento real, único, tal como Passport lo devolvió.
- **M4-T3-C no debe repetirse:** el escenario ya quedó ejecutado y
  documentado.
- **Estado documental final:** completo para este escenario. Pendiente
  únicamente de la revisión formal de Passport
  (`review_status=PENDING_PASSPORT_REVIEW` en la evidencia técnica).

## Resumen para revisión de Passport

> Para el escenario de customer_id incorrecto se utilizó una llave Bre-B
> ACTIVE válida y se envió un customer_id sintético deliberadamente
> incorrecto. Passport rechazó la solicitud de creación del QR con HTTP
> 400, error_code `invalid_parameter_value` y mensaje "UUID version 4
> expected". La llave utilizada permaneció sin modificación y su identidad
> fue verificada inmediatamente antes de la ejecución mediante fingerprint
> sanitizado (`9315E012C94C`) — no se afirma que Passport haya devuelto ese
> fingerprint en su respuesta de error, ni que el archivo de evidencia
> contenga el key_id en ninguna forma.
