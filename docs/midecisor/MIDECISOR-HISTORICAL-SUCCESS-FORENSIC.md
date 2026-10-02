# MiDecisor / DataCrédito — Forense de la ejecución histórica exitosa

**Ticket:** XPAY-592 · **Fecha:** 2026-09-29 (UTC) · **Modo:** 100 % offline, cero red, cero OAuth
**Pregunta:** ¿qué cambió entre la prueba histórica que funcionó y el HTTP 403 de XPAY-588?

**Conclusión:** no existe **ninguna** evidencia local de una consulta MiDecisor ejecutada con
éxito por XPAY. Sí existe un artefacto que prueba que **algún** token válido fue emitido en algún
momento, pero ese artefacto fue **entregado**, no producido por una ejecución local de XPAY.

---

## 1. Qué se buscó y qué se encontró

Se revisaron: el repositorio XPAY completo, `docs/midecisor/`, el historial de git, los scratchpads
de sesión disponibles, los **seis** transcripts locales de sesiones (agosto a hoy), los documentos
de continuidad y las carpetas `~/Documents/datafin/` y `~/Documents/xelecredit/`.

| Búsqueda | Resultado |
|---|---|
| Llamadas de red a DataCrédito registradas en transcripts | **Solo las de hoy** (prueba TLS de XPAY-584, 2026-09-29). Las demás sesiones: **0** |
| Respuestas reales de MiDecisor guardadas (`infoTransaccion`) | **0** en el repo, **0** en las carpetas de documentación, **0** en scratchpads |
| Respuestas guardadas dentro de la colección Postman | **0** en las tres peticiones |
| Estado HTTP histórico del endpoint de producto | **No registrado en ninguna parte** |
| App Postman instalada localmente | **No instalada** (ni aplicación ni workspace) |

Los recuentos altos de términos como `informacionRiesgo` o `numeroIdentificacion` en sesiones
antiguas corresponden a **lecturas de código, tests y ejemplos oficiales**, no a respuestas del
proveedor. Se verificó caso por caso: las apariciones de `issued_at` en la sesión del 25–26 de
septiembre provienen del bloque `example` del Swagger, no de una respuesta real. Una auditoría de
esa época dejó escrito además `AUTH_REAL_EXECUTED: NO`.

---

## 2. El único artefacto relevante

`Mi DECISOR_PJ_PN_EJEMPLO.postman_collection.json`

| Atributo | Valor |
|---|---|
| Creado y modificado | **2026-07-28T18:48Z**, en una **única escritura** |
| Tamaño | 3 678 bytes |
| Peticiones | `Generar_TOKEN`, `Revocar_TOKEN`, `Servicio` — las tres contra `uat-api.datacredito.com.co` |
| Respuestas guardadas | **0** |
| `Authorization` de `Servicio` | Contiene un **JWT real de tres segmentos**, no un marcador de posición |
| `Servicio` → `tipoIdentificacion` | `1` (cédula de ciudadanía, persona natural) con un número de documento presente |
| Cabeceras de token | `client_id`, `client_secret`, `Content-Type` — ninguna adicional |
| Username | Dominio de ambiente `demo`; prefijo de dígito-guion; la parte local **no** lleva punto, a diferencia de la forma de ejemplo del manual |

**Lo que este artefacto prueba:** que un token válido fue emitido en algún momento previo al
28-jul-2026, porque un JWT real de tres segmentos no puede existir sin una respuesta exitosa del
endpoint de token.

**Lo que NO prueba:** que la consulta de crédito se haya ejecutado ni con qué resultado. No hay
respuesta guardada, ni código HTTP, ni salida del producto en ningún lugar del equipo.

**Indicio sobre su procedencia:** el archivo tiene fecha de creación y de modificación idénticas
—una sola escritura— y la aplicación Postman no está instalada en este equipo. El patrón es el de
un archivo **recibido**, no el de una colección construida y usada localmente a lo largo de
sesiones de prueba.

---

## 3. Matriz comparativa

| Atributo | Histórico (artefacto Postman) | XPAY-588 | Veredicto | Evidencia |
|---|---|---|---|---|
| Fecha | 2026-07-28T18:48Z | 2026-09-29, ventana 03:34–03:45Z | DIFERENTE | metadatos de archivo · evidencia XPAY-588 |
| Host | `uat-api.datacredito.com.co` | `uat-api.datacredito.com.co` | **IGUAL** | colección · evidencia |
| Ruta de token | `/spla/oauth2/v1/token` | `/spla/oauth2/v1/token` | **IGUAL** | colección · evidencia |
| Método | POST | POST | **IGUAL** | colección · evidencia |
| Estructura de cabeceras | `client_id`, `client_secret`, `Content-Type: application/json` | `Client_id`, `Client_secret`, `Content-Type: application/json` | **IGUAL** (RFC 9110: insensible a caso) | colección · evidencia |
| Estructura del cuerpo | JSON `{username, password}` | JSON `{username, password}` | **IGUAL** | colección · evidencia |
| Ambiente de las credenciales | dominio `demo` | dominio `demo` | **IGUAL** | booleanos verificados en ambos |
| Origen de ejecución | Desconocido; sin Postman local | LOCAL_DEVELOPMENT_MACHINE | DESCONOCIDO | sin app Postman instalada |
| Juego de credenciales | — | — | **DESCONOCIDO** | no comparable sin leer valores |
| Fuente de las credenciales | Ejemplo suministrado (según dirección técnica) | Propias de XPAY | DIFERENTE *(no verificado localmente)* | afirmación de dirección + patrón de entrega |
| Resultado del token | Token emitido (JWT real almacenado) | **HTTP 403**, sin token | **DIFERENTE** | colección · evidencia |
| Endpoint de producto | Preparado con documento real | No invocado | DIFERENTE | colección · evidencia |
| Resultado del producto | **DESCONOCIDO** (0 respuestas guardadas) | No aplica | DESCONOCIDO | — |

---

## 4. Lectura

Los siete atributos técnicos de la petición —host, ruta, método, cabeceras, cuerpo, ambiente de
las credenciales y protocolo— son **idénticos** entre ambas ejecuciones. XPAY-588 no se desvió de
lo que el artefacto histórico hacía.

La única diferencia estructural candidata es **de qué juego de credenciales se trata**. Si se
confirma que el artefacto usa credenciales de ejemplo del proveedor y XPAY-588 usó las propias de
XPAY, entonces **XPAY nunca ha autenticado con éxito con sus propias credenciales**, y el 403 no
sería una regresión sino un estado inicial que nunca llegó a funcionar. Esa distinción es
relevante para el escalamiento, porque cambia la pregunta de «por qué dejó de funcionar» a «qué
falta para habilitarlo por primera vez».

**Esto no está demostrado.** No es posible comparar los juegos de credenciales sin leer valores,
lo que estos tickets prohíben. Correlación no es causalidad: que el token histórico se emitiera y
el de hoy no, con credenciales presuntamente distintas, no demuestra por sí solo que la diferencia
de credenciales sea la causa del 403.

Sobre la referencia temporal: el recuerdo sitúa la consulta exitosa «hace algunos días», mientras
que el único artefacto local data del 28 de julio de 2026, unos dos meses antes. No hay registro
local de actividad MiDecisor en los días recientes salvo los tickets XPAY-584 a XPAY-591. Si la
consulta ocurrió efectivamente hace pocos días, sucedió fuera de este equipo —otra máquina,
Postman web o un tercero—, y su evidencia no está aquí.

---

## 5. Controles respetados

Cero llamadas de red, cero OAuth, cero llamadas MiDecisor, cero consultas de crédito. El total de
solicitudes OAuth del proyecto permanece en **1**. No se imprimieron ni registraron cédulas,
nombres, apellidos, resultados crediticios, `client_id`, `client_secret`, `username`, `password`,
tokens, JWT, IP ni PII; todas las verificaciones sobre el artefacto fueron **booleanas o
estructurales**. No se ejecutó ninguna petición de la colección. Sin cambios de código, entorno,
configuración, base de datos, deploy, stage, commit ni push.
