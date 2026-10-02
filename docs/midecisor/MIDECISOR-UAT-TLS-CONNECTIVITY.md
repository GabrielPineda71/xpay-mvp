# MiDecisor / DataCrédito — Validación controlada de conectividad TLS (UAT/DEMO)

**Ticket:** XPAY-584
**Estado:** EJECUTADO Y CERRADO
**Fecha de ejecución:** 2026-09-29 (UTC) — ventana 02:45:49Z – 02:46:34Z
**Documento:** evidencia sanitizada de una ejecución única. No es un procedimiento a repetir.

---

## 1. Resumen ejecutivo

Se validó, de forma controlada y sin autenticación, que el dominio de ambiente
DEMO/UAT de DataCrédito Experian —`uat-api.datacredito.com.co`— es alcanzable
desde la máquina local de desarrollo y que su cadena TLS renovada valida
correctamente contra los trust stores del sistema **sin instalar certificados,
sin modificar el trust store y sin deshabilitar la validación TLS**.

La cadena observada coincide con la renovación SSL/TLS anunciada oficialmente por
el Connectivity & Support Team de DataCrédito Experian (Sectigo, renovación
programada para el 21 de septiembre de 2026).

Esta validación cubre **exclusivamente conectividad y confianza TLS**. No
constituye —ni implica— autenticación MiDecisor funcional.

---

## 2. Parámetros y resultado

```
TEST_ENVIRONMENT=LOCAL_DEVELOPMENT_MACHINE
TARGET_HOST=uat-api.datacredito.com.co
DNS_RESOLUTION=PASS
TCP_443_CONNECTIVITY=PASS
TLS_HANDSHAKE=PASS
TLS_HOSTNAME_VALIDATION=PASS
TLS_TRUST_VALIDATION=PASS
HTTPS_BASE_REQUEST=PASS
HTTP_STATUS=404
CONNECTIVITY_CLASSIFICATION=CONNECTIVITY_TLS_PASS
AZURE_CONNECTIVITY_TESTED=NO
```

### Contadores de alcance

```
OAUTH_REAL_TOKEN_REQUESTS=0
MIDECISOR_FUNCTIONAL_CALLS=0
CREDIT_QUERIES=0
PII_SENT=0
HTTPS_BASE_REQUESTS=1
```

> `AZURE_CONNECTIVITY_TESTED=NO` es una afirmación deliberada: la prueba se
> originó en la máquina local de desarrollo. **No** debe presentarse ni citarse
> como evidencia de conectividad desde Azure.

---

## 3. Secuencia validada

| # | Etapa | Resultado | Observación |
|---|-------|-----------|-------------|
| 1 | Resolución DNS | PASS | El nombre resuelve vía CNAME a infraestructura Imperva y de ahí a una dirección IPv4 |
| 2 | Conectividad TCP 443 | PASS | Conexión aceptada por el puerto 443 |
| 3 | TLS handshake | PASS | TLS 1.3, suite `TLS_AES_128_GCM_SHA256`, grupo negociado `X25519MLKEM768`, firma `rsa_pss_rsae_sha256` |
| 4 | Validación de hostname | PASS | El nombre solicitado coincide con el certificado presentado (verificación de peername correcta) |
| 5 | Validación de cadena de confianza | PASS | Cadena completa verificada hasta raíz confiable; resultado de verificación sin errores |
| 6 | Petición HTTPS base no autenticada | PASS | Una (1) sola petición `GET /` sobre HTTP/2; respuesta `404`; una conexión, cero redirecciones |

---

## 4. Cadena TLS observada (sanitizada)

```
leaf   CN=uat-api.datacredito.com.co
         O=Experian Information Solutions, Inc.  (C=US, ST=Texas)
         SAN: uat-api.datacredito.com.co, www.uat-api.datacredito.com.co
         Clave: RSA 2048 · firma: sha256WithRSAEncryption
         Vigencia: 2026-09-21 → 2027-04-07
  ↓ emitido por
inter  CN=Sectigo Public Server Authentication CA OV R36
         O=Sectigo Limited (C=GB) · Clave: RSA 3072
         Vigencia: 2021-03-22 → 2036-03-21
  ↓ emitido por
root   CN=Sectigo Public Server Authentication Root R46
         O=Sectigo Limited (C=GB) · Clave: RSA 4096 · self-signed
         Vigencia: 2021-03-22 → 2046-03-21
```

**Correspondencia con la comunicación oficial de DataCrédito.** La comunicación
de Connectivity & Support Team anunció el cambio a cadena Sectigo (Root R46 más
CA EV R36 / CA OV R36) con fecha de renovación 21-sep-2026. La cadena realmente
observada usa la variante **OV R36** y el certificado de servidor tiene
`notBefore` = **2026-09-21**, es decir exactamente la fecha anunciada. Se
concluye que la renovación está aplicada en el ambiente UAT/DEMO y que la cadena
es confiable desde este equipo sin intervención alguna.

*No se transcriben material PEM, huellas, cookies, cabeceras de trazabilidad del
CDN ni ningún otro dato sensible.*

---

## 5. Interpretación del HTTP 404

La petición base devolvió `404` con un cuerpo JSON de error de gateway indicando
que no existe un proxy de API mapeado en la ruta raíz `/`
(`ApplicationNotFound`).

Esto es una respuesta **sana** y **esperada**:

- El handshake TLS terminó correctamente y la petición llegó a la capa de
  aplicación del gateway, que respondió con semántica propia.
- El origen está vivo y la ruta de red end-to-end es operativa.
- La ruta raíz no es un endpoint de negocio; ningún endpoint MiDecisor fue
  invocado por diseño.

El `404` **no** indica fallo de conectividad y **no** aporta información sobre el
estado de la autenticación MiDecisor.

Las cabeceras de respuesta mostraron postura de seguridad estándar del borde:
HSTS activo, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` y
`Content-Security-Policy: frame-ancestors 'none'`, servido a través de CDN
Imperva sobre HTTP/2.

---

## 6. Conformidad y controles respetados

- Validación TLS **activa** en todo momento. No se usó `-k`, `--insecure` ni
  `verify=false`.
- Verificación cruzada con **dos** trust stores independientes (el del sistema
  operativo y el de una instalación OpenSSL separada); ambos aceptaron la cadena.
- Sin modificar trust store, sin instalar certificados, sin añadir anclas de
  confianza.
- Sin cambios en código, variables de entorno ni configuración.
- Sin base de datos, sin deploy, sin stage, sin commit ni push durante la
  ejecución de la prueba.
- Sin tocar Passport / BRE-B.
- Sin OAuth, sin credenciales, sin PII, sin payload de negocio.
- Exactamente **una** petición HTTPS. Los handshakes de diagnóstico adicionales
  fueron TLS puro, sin tráfico HTTP.

---

## 7. Fuera del alcance de XPAY-584

Este ticket **no** resolvió ni evaluó:

- Flujo OAuth2 de MiDecisor (`client_id`, `client_secret`, `username`,
  `password`) conforme al *Manual de implementación API MiDecisor v1.0*.
- Emisión o validación de tokens.
- Cualquier consulta de riesgo o endpoint funcional MiDecisor.
- Equivalencia entre las credenciales Okta entregadas por DataCrédito para
  pruebas y las credenciales de API MiDecisor. Siguen siendo cosas distintas
  hasta que se confirme lo contrario.
- Conectividad desde Azure.

---

## 7bis. Efecto sobre los blockers de gobernanza (leer antes de citar este documento)

Este documento **no cierra** ningún blocker del registro
`docs/GOVERNANCE_BLOCKERS_AND_DECISIONS_REGISTER.md`.

- **XGOV-B-0002 (`MIDECISOR_BASE_URL` no autoritativa) — SIGUE ABIERTO.** Lo que
  aquí se acredita es que el host `uat-api.datacredito.com.co` existe, es de
  DataCrédito Experian (certificado emitido a *Experian Information Solutions,
  Inc.* para ese nombre exacto) y es alcanzable con TLS válido. Eso eleva el host
  de "forma de URL vista en chat" a **dominio UAT/DEMO documentado por el
  proveedor**, respaldado por comunicación oficial del Connectivity & Support Team.
  Pero **no** establece la URL base asignada a XPAY, ni el ambiente asignado, ni
  la ruta de consulta contractual. Un host alcanzable no es una URL base
  autorizada.
- **XGOV-B-0001 (propiedad de credenciales XPAY vs DAFIN/Xelecredit) — SIGUE
  ABIERTO** y continúa siendo el bloqueador primario.
- **XGOV-B-0003 (ruta de consulta `/client` vs `/pn`) — SIGUE ABIERTO.** No se
  probó ninguna ruta funcional.

Uso correcto de esta evidencia: descartar la red y el TLS como causa de un fallo
futuro. Uso incorrecto: presentarla como autorización de ambiente, de URL base o
de credenciales.

## 8. Estado del frente MiDecisor tras este cierre

La barrera de consentimiento sigue activa: la implementación registrada de
`IConsultaRiesgoAutorizacion` continúa siendo
`AutorizacionConsultaRiesgoNoDisponible`. El stack MiDecisor permanece **dormante**
por diseño. XPAY-584 no lo activa ni lo acerca a activación; solo despeja la
duda de conectividad/TLS del ambiente UAT.

El siguiente paso del frente es de credenciales y contrato OAuth2, no de red.
