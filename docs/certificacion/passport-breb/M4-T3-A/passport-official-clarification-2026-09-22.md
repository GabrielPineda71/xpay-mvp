# M4-T3-A — Aclaración oficial de Passport sobre el comportamiento observado

**Tipo de documento:** evidencia documental complementaria (narrativa). Este
archivo **no reemplaza ni modifica** la evidencia técnica cruda — solo la
contextualiza con la interpretación oficial recibida de Passport.

## Evidencia técnica referenciada

- Archivo: `docs/certificacion/passport-breb/M4-T3-A/evidence-2026-09-22T02-42-08Z.json`
- SHA256: `18bf3e5d24877a82f0f049e3242712db446fc676a639b903f2dc5c6c368df345`
- Este archivo permanece **sin modificar** — verificado byte-idéntico en el
  momento de escribir esta nota.

## Contexto

M4-T3-A ejecutó exactamente un `POST /v1/qrcodes` (STATIC/MPOS) usando la
llave desechable dedicada del subcaso (creada en XPAY-476, suspendida en
XPAY-477; `key_id` idéntico — mismo fingerprint — en Create, Suspend, y en
el request/response de este QR). Passport aceptó la operación y generó el
QR en estado `ACTIVE`, pese a que la llave objetivo estaba `SUSPENDED`
(confirmado tanto por la respuesta de Passport al Suspend como visualmente
en el Dashboard de Sandbox por el director).

En su momento (auditoría forense offline, XPAY-479), este resultado quedó
clasificado provisionalmente en la nota de la evidencia técnica como:

> `UNEXPECTED_SUCCESS_FOR_NEGATIVE_TEST_CASE` — sin defecto local
> encontrado, pendiente de revisión contractual explícita por Passport.

## Aclaración oficial recibida

Passport (Gustavo Muñoz), como revisor de certificación de este expediente,
respondió textualmente:

> "Se espera que el QR se genere con la llave suspendida. Durante el pago,
> el procesamiento devolverá un error que indica que la llave está
> suspendida. Puedes continuar con esta evidencia, pues es el
> comportamiento previsto."

Fecha de recepción de la aclaración: 2026-09-22 (misma fecha de cierre
documental, XPAY-485/486).

## Interpretación autorizada (XPAY-485/486)

- La clasificación provisional `UNEXPECTED_SUCCESS_FOR_NEGATIVE_TEST_CASE`
  queda **superada** por esta aclaración oficial — se preserva aquí
  únicamente por trazabilidad histórica (fue la clasificación correcta con
  la información disponible en su momento, antes de recibir la aclaración
  de Passport).
- Estado actualizado: **`EXPECTED_PASSPORT_BEHAVIOR_CONFIRMED_BY_PASSPORT`**.
- La creación exitosa del QR con la llave suspendida **no constituye un
  fallo** de M4-T3-A — es el comportamiento documentado y esperado por el
  proveedor.
- Passport confirmó expresamente que la validación de "llave suspendida"
  ocurre durante el procesamiento de Payment (Módulo 5 del Anexo oficial,
  sección 3.4), no durante la creación del QR estático (Módulo 4).

## Campos de cierre

```
M4_T3_A_STATUS=PASS_EXPECTED_BEHAVIOR_CONFIRMED_BY_PASSPORT
M4_T3_A_REPEAT_REQUIRED=NO
PAYMENT_EXECUTED_FOR_THIS_CASE=NO
```

Ningún Payment fue ejecutado como parte de esta aclaración ni de su cierre
documental. El Anexo ubica las pruebas de pago hacia llaves
suspendidas/eliminadas en el Módulo 5, fuera del alcance de M4-T3.

## review_status

El campo `review_status` de la evidencia técnica cruda permanece
`PENDING_PASSPORT_REVIEW` — ese campo lo actualiza Passport fuera de este
repositorio (ver política en `docs/certificacion/passport-breb/README.md`);
esta nota documental no lo modifica ni lo anticipa.
