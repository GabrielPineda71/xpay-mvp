# M4-T3-B — Nota documental complementaria (llave Bre-B eliminada)

**Tipo de documento:** evidencia documental complementaria (narrativa). Este
archivo **no reemplaza ni modifica** ninguna evidencia técnica cruda — solo
las contextualiza y las enlaza para su consolidación posterior.

## Objetivo del escenario

Caso obligatorio del Anexo Passport, Módulo 4: **"Crear QR con una llave
Bre-B eliminada"**. El objetivo de esta secuencia fue obtener evidencia real
y trazable de ese escenario, usando una llave desechable dedicada
exclusivamente a este caso (no la llave histórica del ciclo M3, cuyo target
quedó contaminado y fue abandonado — ver XPAY-487/488/490/491).

## Ambiente

Passport Sandbox (`environment: "sandbox"` en las tres evidencias
referenciadas).

## Fecha/hora de la evidencia real

Derivada directamente de la evidencia del intento de QR:
`executed_at_utc: 2026-09-23T00:37:43Z` (evidencia QR). El fixture fue
creado el `2026-09-23T00:17:10Z` y eliminado el `2026-09-23T00:31:34Z` (ver
evidencias respectivas).

## Preparación del fixture

Se creó una llave Bre-B (`key_type=BCODE`) desechable, dedicada
exclusivamente a este escenario, mediante el comando
`create-m4-t3-deleted-fixture-key` (XPAY-492/494). Esa misma llave fue
eliminada mediante `delete-m4-t3-fixture-key` (XPAY-492/496) antes de
intentar crear el QR. Ninguna otra llave del expediente (activa de M4-T1/T2,
suspendida de M4-T3-A, o la histórica contaminada de M3) participó en esta
secuencia.

## Fingerprint sanitizado de la llave

```
8A15DB69E7E0
```

## Secuencia ejecutada

1. **CREATE FIXTURE** — `create-m4-t3-deleted-fixture-key`, resultado
   `PASS`, llave creada en estado `ACTIVE`. `id_fingerprint` de la respuesta:
   `8A15DB69E7E0`.
2. **DELETE MISMO FIXTURE** — `delete-m4-t3-fixture-key`, resultado `PASS`.
   `key_id_fingerprint` del request: `8A15DB69E7E0` (idéntico al paso 1).
3. **INTENTO REAL DE CREATE QR** — `create-qr-static-deleted-key`. Antes de
   esta llamada se verificó localmente, en el mismo proceso, que el
   fingerprint del target configurado (`PASSPORT_TEST_QR_DELETED_KEY_ID`)
   seguía siendo `8A15DB69E7E0`. Passport respondió:

   ```
   HTTP 404
   error_code: resource_not_found
   error_message: "key not found."
   ```

## Interpretación (A/B/C)

**A) Hecho observado:** Passport rechazó la creación del QR porque la
llave objetivo no fue encontrada (`HTTP 404`, `resource_not_found`, `"key
not found."`).

**B) Interpretación técnica:** el resultado es **consistente** con el
escenario negativo solicitado — un intento de creación de QR utilizando una
llave previamente eliminada. Esta nota no afirma más que eso.

**C) Estado de certificación:** evidencia preparada para revisión de
Passport. `review_status` de la evidencia técnica permanece
`PENDING_PASSPORT_REVIEW` — ese campo lo actualiza Passport fuera de este
repositorio, nunca esta nota ni XPAY.

## Explicación de la cadena de identidad

Los tres pasos comparten el mismo fingerprint sanitizado (`8A15DB69E7E0`),
verificable así:

- **Paso 1 → Paso 2:** el `id_fingerprint` de la respuesta de Create
  (`M4-T3-B-KEY-PREP-CREATE/evidence-2026-09-23T00-17-10Z.json`) es
  idéntico al `key_id_fingerprint` del request de Delete
  (`M4-T3-B-KEY-PREP-DELETE/evidence-2026-09-23T00-31-34Z.json`) —
  **ambos campos están presentes y son directamente cruzables dentro de
  sus respectivos `evidence.json`.**
- **Paso 2 → Paso 3:** aquí existe una **limitación probatoria explícita**
  que esta nota documenta deliberadamente: el `evidence.json` generado
  para la respuesta de error de M4-T3-B
  (`M4-T3-B/evidence-2026-09-23T00-37-43Z.json`) deja `request_sanitized`
  **vacío por diseño** (mismo criterio aplicado uniformemente a toda
  respuesta de error de este harness — nunca fabrica un campo que Passport
  no confirmó en su respuesta). Por tanto, **ese archivo NO contiene un
  `key_id_fingerprint` propio** que pueda cruzarse directamente contra los
  dos anteriores.

  La continuidad de identidad para este tercer paso se sustenta, en
  cambio, en **trazabilidad técnica complementaria** — no en un dato
  devuelto por Passport ni contenido en ese evidence.json — compuesta por:

  1. el fingerprint local verificado inmediatamente antes de la llamada
     (mismo valor, `8A15DB69E7E0`);
  2. `PASSPORT_TEST_QR_DELETED_KEY_ID` sin modificación entre la
     verificación y la llamada;
  3. el código de `create-qr-static-deleted-key`, previamente auditado,
     que lee esa variable de forma exclusiva y sin transformación;
  4. ausencia comprobada de cualquier fallback hacia otra llave;
  5. el mismo target dedicado utilizado durante toda la secuencia (nunca
     sustituido).

## Evidencias referenciadas

| Paso | Ruta | SHA256 |
|---|---|---|
| Create fixture | `docs/certificacion/passport-breb/M4-T3-B-KEY-PREP-CREATE/evidence-2026-09-23T00-17-10Z.json` | `db5838904df336a878b7a43239e6892829390de21a6559c74e7f4eca3f9e30f6` |
| Delete fixture | `docs/certificacion/passport-breb/M4-T3-B-KEY-PREP-DELETE/evidence-2026-09-23T00-31-34Z.json` | `a02e394a30e70b1e8d48bfc74a50c15ad0765972e0b57297fa09642d17f3cb4d` |
| QR real M4-T3-B | `docs/certificacion/passport-breb/M4-T3-B/evidence-2026-09-23T00-37-43Z.json` | `d1dd2a0efbda572a120bc15e19a2fd200a9dcf762fd8a571e5f2095d568535f0` |

Estos tres archivos permanecen **sin modificar** — verificados byte-idénticos
en el momento de escribir esta nota.

## Declaraciones explícitas

- **Sin retry:** cada uno de los tres pasos (Create, Delete, Create QR) se
  ejecutó exactamente una vez contra Passport Sandbox. No hubo reintentos
  en ninguno.
- **M4-T3-B no debe repetirse:** la secuencia completa ya quedó ejecutada y
  documentada. No debe volver a ejecutarse ningún paso (Create fixture,
  Delete fixture, ni el intento de QR) salvo autorización explícita nueva
  y justificada.
- **Estado documental final:** completo para este escenario. Pendiente
  únicamente de la revisión formal de Passport
  (`review_status=PENDING_PASSPORT_REVIEW` en la evidencia técnica).

## Resumen para revisión de Passport

> Se creó una llave Bre-B dedicada para el escenario, se eliminó y,
> posteriormente, se intentó generar un QR estático utilizando la misma
> llave. Passport respondió HTTP 404 con error_code `resource_not_found` y
> mensaje "key not found.". La identidad del fixture se mantuvo mediante el
> fingerprint sanitizado `8A15DB69E7E0` a lo largo de la secuencia Create →
> Delete → intento de QR, verificado localmente en cada paso — no se
> afirma que Passport haya devuelto ese fingerprint en su respuesta de
> error, ni que el archivo de evidencia del intento de QR contenga el
> key_id en ninguna forma.
