# MiDecisor / DataCrédito — Validación de autenticación UAT/DEMO

**Tickets:** XPAY-588 (ejecución) · XPAY-589 (diagnóstico forense offline)
**Estado:** EJECUTADO Y CONGELADO — resultado `AUTH_REJECTED` (HTTP 403)
**Fecha de ejecución:** 2026-09-29 (UTC) — hora local 2026-09-28 (UTC-05)
**Ventana UTC acotada de la petición:** entre 03:34:26Z y 03:44:41Z del 2026-09-29.
La hora exacta **no fue capturada** por el ejecutor; la ventana se deriva de la creación del
script de la prueba y de la escritura de esta evidencia. No debe presentarse una hora exacta.
**Alcance:** exclusivamente autenticación OAuth2. Cero consultas de crédito.

---

## 1. Resumen

Se ejecutó **una sola** solicitud OAuth2 real contra el endpoint de token del
ambiente de pruebas de cliente de DataCrédito Experian, con las credenciales
de ambiente `demo` de XPAY SAS (antes Xelecredit). El transporte TLS funcionó
correctamente y el gateway respondió conforme a su contrato documentado, pero
**denegó el acceso con HTTP 403**. No se obtuvo token.

La causa del 403 **no está determinada** y no puede determinarse con la
documentación oficial disponible: el propio manual del proveedor lista el
código 403 **sin documentar ningún escenario que lo produzca**, a diferencia
de los demás códigos de la misma tabla.

---

## 2. Parámetros de ejecución

```
TEST_ENVIRONMENT=LOCAL_DEVELOPMENT_MACHINE
REQUEST_UTC_WINDOW=2026-09-29T03:34:26Z .. 2026-09-29T03:44:41Z
REQUEST_EXACT_TIMESTAMP=NOT_CAPTURED
TARGET_HOST=uat-api.datacredito.com.co
TOKEN_ENDPOINT=/spla/oauth2/v1/token
HTTP_METHOD=POST
CREDENTIAL_ENVIRONMENT=demo
OAUTH_REAL_TOKEN_REQUESTS=1
```

`AZURE_CONNECTIVITY_TESTED=NO` — la prueba se originó en la máquina local de
desarrollo. No debe citarse como conectividad desde Azure.

---

## 3. Resultado

```
HTTP_STATUS=403
AUTH_RESULT=AUTH_REJECTED
SSL_VERIFY_RESULT=0        (validación TLS correcta)
REDIRECTS=0
CONNECTS=1
RESPONSE_PARSED=YES

ACCESS_TOKEN_PRESENT=NO
REFRESH_TOKEN_PRESENT=NO

SAFE_ERROR_CODE=Forbidden
SAFE_ERROR_CATEGORY=Access to the requested resource is Forbidden
```

### Contadores de alcance

```
TOKENS_OBTAINED=0
MIDECISOR_FUNCTIONAL_CALLS=0
CREDIT_QUERIES=0
CUSTOMER_PII_SENT=0
RETRIES=0
REFRESH_TOKEN_REQUESTS=0
REVOKE_TOKEN_REQUESTS=0
```

---

## 4. Lectura técnica del resultado

El resultado es **informativo, no un fallo de integración**:

- **El transporte es sano.** `SSL_VERIFY_RESULT=0`, una sola conexión, cero
  redirecciones. Coherente con la validación TLS previa del mismo host.
- **Se alcanzó el servicio correcto.** El cuerpo de la respuesta respetó
  exactamente el envelope de error documentado por el proveedor
  (`errors[].errorType` + `errors[].message` + `success:false`), lo que
  confirma que la petición llegó al servicio Apigee documentado y fue
  rechazada por política, no descartada por estar mal formada.
- **La estructura de la petición coincide con el contrato oficial.** Método
  `POST`, `Client_id` y `Client_secret` en cabeceras, `Content-Type:
  application/json`, y cuerpo JSON con `username`/`password` — la misma forma
  del ejemplo de integración del Manual de Conectividad. Los nombres de
  cabecera son insensibles a mayúsculas por RFC 9110.
- **Los errores de forma quedan descartados por la propia tabla del
  proveedor**, que les asigna códigos distintos: atributos ausentes → 400,
  método HTTP distinto de POST → 405, `Content-Type` incorrecto → 415.
  No se recibió ninguno de ellos.
- **Las credenciales inválidas también tienen códigos propios**: usuario o
  contraseña inválidos → 400; `client_id`/`client_secret` inválidos → 401;
  cuenta en estado inválido → 401. Tampoco se recibió ninguno de ellos.

El 403 no encaja, por tanto, en ninguno de los escenarios que el proveedor
documenta como causa concreta.

---

## 5. Lo que la documentación oficial NO dice

Revisadas las cinco fuentes oficiales locales (Manual de Conectividad API
Experian Clientes v1.0, Manual de implementación API MiDecisor v1.0, los dos
Swagger contractuales y la guía oficial de errores de conexión TLS):

- El código **403 aparece listado sin escenario documentado** en el manual de
  conectividad, que es la fuente autoritativa para el endpoint de token.
  Todos los demás códigos de esa tabla sí traen escenario.
- **Cero menciones** de `whitelist`, `lista blanca`, `IP pública`,
  autorización por origen, `access control`, `suscripción` o `subscription`
  en ninguna de las cinco fuentes.
- La única mención a `firewall`/`proxy` está en la guía TLS y se refiere al
  proxy o firewall **del propio cliente** bloqueando tráfico SSL — un
  escenario ya descartado por el handshake TLS exitoso.
- El manual de implementación sí atribuye 403 a permisos/scope insuficientes
  del cliente, pero esa fila pertenece a la tabla del **API de producto**, que
  presupone un token ya emitido; no aplica estructuralmente al endpoint de
  token.

---

## 6. Controles de seguridad respetados

- Validación TLS activa. Sin `-k`, sin `--insecure`, sin `verify=false`.
- Sin redirecciones automáticas (`--no-location`, `--max-redirs 0`).
- **Exactamente una** solicitud. Sin reintentos, sin segundo intento, sin
  probar variantes de cabeceras, sin probar otro host, sin refresh, sin revoke.
- Las credenciales **no pasaron por la línea de comandos** (fichero de
  configuración con `umask 077`), por lo que no fueron visibles a `ps`.
- La respuesta se capturó en un directorio temporal privado, se procesó para
  extraer únicamente booleanos, y se eliminó de inmediato (`trap` en
  `EXIT`/`INT`/`TERM`). `TEMP_TOKEN_ARTIFACTS_DELETED=YES`.
- Nunca se imprimieron ni registraron: valores de credenciales, username,
  tokens, JWT, cookies, cabeceras sensibles, longitudes, prefijos, sufijos,
  hashes ni huellas. No se decodificó ningún JWT.
- Ninguna IP pública consta en este documento.
- Sin cambios de código, configuración persistente, entorno, Azure, base de
  datos, deploy, stage, commit ni push. Passport no fue tocado.

---

## 7. Estado y siguiente dato faltante

```
ROOT_CAUSE_CONFIRMED=NO
ROOT_CAUSE=UNKNOWN
```

Las hipótesis que la documentación permite sostener (habilitación/entitlement
del `client_id`, especificidad de credenciales por ambiente, u otra política
del gateway Apigee) **no son distinguibles con información local**. El dato
que falta no existe del lado de XPAY: sólo DataCrédito Experian puede indicar
por qué su gateway denegó la petición, consultando sus propios registros.

Este documento **no autoriza** reintentos, nuevas llamadas, activación de
runtime, cambios de configuración ni inicio de UAT funcional.
