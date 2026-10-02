# MiDecisor / DataCrédito — Validación OAuth UAT desde Azure QA

**Tickets:** XPAY-631 (egress) · XPAY-632 / 632R / 632R2 (ejecución) · XPAY-633 (este registro)
**Estado:** EJECUTADO Y CONGELADO — resultado `AZURE_WHITELIST_VALIDATION=PASS` (HTTP 200)
**Fecha de validación:** 2026-10-01 (UTC)
**Ventana UTC de la petición:** 2026-10-01T21:01:09Z .. 2026-10-01T21:01:12Z
**Alcance:** exclusivamente autenticación OAuth2. Cero consultas de crédito.

---

## 1. Resumen

Se ejecutó **una sola** solicitud OAuth2 real contra el endpoint de token UAT de
DataCrédito Experian, originada en el runtime de Azure QA (`xpay-api-qa`) y no
en una máquina local. El gateway respondió **HTTP 200** y emitió token.

Es la primera autenticación exitosa de XPAY contra este endpoint. La prueba
previa (XPAY-588, ver `MIDECISOR-UAT-AUTH-VALIDATION.md`) había recibido HTTP 403
con la misma estructura de petición, pero desde la máquina local de desarrollo.

---

## 2. Parámetros de ejecución

```
TEST_ENVIRONMENT=AZURE_QA
EXECUTION_ORIGIN=AZURE_XPAY_API_QA
APP_SERVICE=xpay-api-qa
RESOURCE_GROUP=rg-xpay-qa
EXECUTION_MECHANISM=KUDU_SCM_API_COMMAND (autenticación AAD)
REQUEST_UTC_WINDOW=2026-10-01T21:01:09Z .. 2026-10-01T21:01:12Z
TARGET_HOST=uat-api.datacredito.com.co
TOKEN_ENDPOINT=/spla/oauth2/v1/token
HTTP_METHOD=POST
CREDENTIAL_ENVIRONMENT=UAT (credenciales oficiales suministradas por DataCrédito/Connectivity)
CREDENTIAL_SOURCE=EPHEMERAL_PARENT_SHELL
EGRESS_FINGERPRINT=9CB22EA37278
REPEAT_GUARD=XPAY-632R2 (CONSUMED)
```

Estructura de la petición, idéntica a la verificada en XPAY-586/588: cabeceras
`client_id`, `client_secret` y `Content-Type: application/json`, sin ninguna
adicional; cuerpo JSON con `username` y `password`; sin `grant_type`.

---

## 3. Resultado

```
OAUTH_TOKEN_REQUESTS=1
OAUTH_HTTP_STATUS=200
SSL_VERIFY_RESULT=0        (validación TLS correcta)
CONNECTS=1
REDIRECTS=0
RESPONSE_PARSED=YES

ACCESS_TOKEN_RECEIVED=YES
TOKEN_TYPE_PRESENT=YES
EXPIRES_IN_PRESENT=YES
REFRESH_TOKEN_PRESENT=YES

AZURE_WHITELIST_VALIDATION=PASS
```

### Contadores de alcance

```
MIDECISOR_QUERY_CALLS=0
DATACREDITO_FUNCTIONAL_QUERY_CALLS=0
CREDIT_QUERIES=0
CUSTOMER_PII_SENT=0
RETRIES=0
REFRESH_TOKEN_REQUESTS=0
REVOKE_TOKEN_REQUESTS=0
```

---

## 4. Cadena de evidencia

El resultado se apoya en tres hechos de origen distinto, que conviene no
confundir:

1. **XPAY-631 — egress del contexto Kudu.** Demostró empíricamente que el
   contexto Kudu `/api/command` de `xpay-api-qa` sale a Internet por el egress
   esperado de Azure QA (VNet → Route All → NAT Gateway → IP pública dedicada),
   con fingerprint `9CB22EA37278`.
2. **Verificación manual del operador.** El operador confirmó que esa IP
   corresponde exactamente a la IP registrada por Connectivity en la whitelist
   de Okta. Esta verificación es manual y no consta como evidencia técnica en
   el repositorio.
3. **XPAY-632R2 — OAuth.** La solicitud OAuth UAT ejecutada desde ese mismo
   contexto recibió HTTP 200 y `access_token`.

El egress **no se volvió a comprobar** durante XPAY-632R2: el fingerprint citado
aquí proviene de XPAY-631, no de una medición hecha en la misma ejecución.

---

## 5. Lectura técnica del resultado

Lo que XPAY-632R2 demuestra por sí solo:

- Las credenciales UAT son aceptadas por el gateway.
- El flujo OAuth funciona desde el runtime de Azure QA.
- La estructura de petición documentada en XPAY-586/588 es correcta.

Sobre el HTTP 403 histórico de XPAY-588: aquella petición se ejecutó desde la
máquina local del desarrollador, no desde Azure QA. Que la misma estructura
reciba 403 desde un origen no registrado y 200 desde el origen registrado es
**consistente con una restricción por IP de origen**. No debe presentarse como
causa demostrada: XPAY-632R2 no aisló la variable (no se repitió la petición
desde un origen no registrado con las mismas credenciales en la misma ventana),
y entre ambas pruebas pudieron cambiar otros factores del lado del proveedor.

```
ROOT_CAUSE_403_XPAY_588=CONSISTENT_WITH_SOURCE_IP_RESTRICTION
ROOT_CAUSE_403_CONFIRMED=NO
```

---

## 6. Intentos previos del mismo día

Antes del intento exitoso hubo un intento fallido que no generó tráfico hacia
DataCrédito:

- **XPAY-632R (2026-10-01, 17:03:17Z .. 17:03:20Z).** El comando remoto terminó
  con exit code 2 y sin respuesta HTTP. Un preflight posterior, sin secretos y
  sin tráfico externo, reprodujo el mismo fallo: Kudu `/api/command` no pasa el
  comando por un shell; lo divide por espacios sin respetar comillas, de modo
  que `bash -c '<script>'` recibe un script truncado con una comilla sin cerrar.
  `curl` no llegó a ejecutarse. `OAUTH_TOKEN_REQUESTS=0` para ese intento se
  establece por firma de fallo idéntica, no por lectura directa del error
  original, que se descartó sin imprimir.
- **Forma corregida.** El script se envía como un único token sin espacios ni
  comillas, usando `${IFS}` como separador, y la configuración de `curl` viaja
  codificada en base64 por la entrada estándar. Esta forma se validó offline
  contra `file:///dev/null` antes de XPAY-632R2.

Total del día hacia DataCrédito: **una** solicitud OAuth.

---

## 7. Controles de seguridad respetados

- Validación TLS activa. Sin `-k`, sin `--insecure`.
- Sin redirecciones automáticas (`max-redirs 0`).
- **Exactamente una** solicitud bajo el guard `XPAY-632R2`. Sin reintentos, sin
  refresh, sin revoke, sin llamada al API de producto.
- Las credenciales se tomaron del entorno de la sesión del operador y existieron
  únicamente en memoria. No se escribieron en archivos locales, App Settings,
  repositorio ni logs. Viajaron codificadas dentro del cuerpo del comando hacia
  Kudu y fueron visibles momentáneamente como argumentos del proceso remoto.
- La respuesta se procesó en memoria para extraer únicamente booleanos. El
  `access_token` y el `refresh_token` no se imprimieron, no se guardaron y no se
  reutilizaron.
- Los tokens emitidos **no fueron revocados** (revocar habría sido una segunda
  llamada no autorizada); caducan por su propia vigencia.
- Nunca se imprimieron ni registraron: valores de credenciales, username,
  tokens, cabeceras sensibles, longitudes, hashes ni huellas de secretos.
- Ninguna IP pública completa consta en este documento.
- Sin cambios en Azure, App Settings, networking, código, deploy, stage, commit
  ni push.

---

## 8. Estado

```
AZURE_WHITELIST_VALIDATION=PASS
MIDECISOR_FUNCTIONAL_VALIDATION=NOT_STARTED
```

Este documento **no autoriza** nuevas llamadas OAuth, consultas MiDecisor,
activación de runtime, persistencia de credenciales en Azure ni inicio de UAT
funcional. La integración MiDecisor sigue dormida.
