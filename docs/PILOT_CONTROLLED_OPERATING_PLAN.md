# XPAY MVP — Plan Operativo de Piloto Controlado

**Versión:** 1.0
**Fecha:** 2026-06-18
**Tipo:** Documento operativo — uso exclusivo piloto controlado / QA / gestión

---

## 1. Propósito

Este documento define cómo ejecutar el piloto controlado de XPAY MVP. Solo se usa **después de cumplir los criterios de salida de QA interno** definidos en `docs/QA_EXIT_CRITERIA_AND_PILOT_READINESS.md` y con el acta de decisión firmada.

**Qué no es el piloto controlado:**

- **No es producción general.** El piloto opera con un grupo acotado y supervisado.
- **No es un lanzamiento comercial.** No hay contratos de servicio, SLAs comerciales ni compromisos financieros.
- **No autoriza dinero real automáticamente.** El uso de dinero real requiere acuerdo explícito por escrito, análisis legal y aprobación formal adicional.
- **No reemplaza aprobación legal, financiera, regulatoria ni de seguridad avanzada.** Este documento no certifica el sistema para operaciones financieras reguladas.

---

## 2. Identificación del piloto

| Campo | Valor |
|-------|-------|
| **Nombre del piloto** | XPAY MVP — Piloto Controlado v0.1 |
| **Versión evaluada** | XPAY MVP QA Candidate v0.1 |
| **Commit de base** | _pendiente de diligenciar_ |
| **Ambiente** | `[ ] Local` `[ ] Azure QA` `[ ] Azure Piloto` `[ ] Otro: ___` |
| **URL backend** | _pendiente_ |
| **URL frontend** | _pendiente_ |
| **Fecha inicio** | _pendiente_ |
| **Fecha cierre** | _pendiente_ |
| **Responsable técnico** | _pendiente_ |
| **Responsable negocio** | _pendiente_ |
| **Responsable soporte** | _pendiente_ |
| **Estado** | `[ ] Planeado` `[ ] En ejecución` `[ ] Suspendido` `[ ] Cerrado` |

---

## 3. Condiciones previas

Confirmar todos los ítems antes de iniciar el piloto:

- [ ] `docs/QA_INTERNAL_CYCLE_01.md` cerrado con estado `Aprobado` o `Aprobado con observaciones`
- [ ] `docs/QA_EXIT_CRITERIA_AND_PILOT_READINESS.md` evaluado — criterios 1–12 verificados
- [ ] Acta de decisión firmada (sección 13 de `QA_EXIT_CRITERIA_AND_PILOT_READINESS.md`) — firma técnica y firma de negocio
- [ ] Todas las incidencias críticas en estado `Cerrada` o `Rechazada`
- [ ] Ambiente de piloto identificado: URL, servidor, base de datos, configuración lista
- [ ] Participantes definidos y habilitados (tabla sección 4 de este documento)
- [ ] Canal de soporte definido y activo
- [ ] Plan de suspensión comunicado a todos los participantes (sección 13)
- [ ] Datos permitidos definidos y comunicados (sección 8)
- [ ] Restricciones del piloto comunicadas a todos los participantes antes del primer acceso

---

## 4. Participantes permitidos

| Tipo de participante | Cantidad máxima sugerida | Rol | Qué puede hacer | Qué no puede hacer |
|---------------------|--------------------------|-----|-----------------|--------------------|
| **Equipo interno XPAY** | Sin límite definido (equipo core) | Operativo / técnico | Usar todas las funcionalidades habilitadas; reportar incidencias; ejecutar scripts QA con autorización técnica | Hacer deploy sin autorización; modificar configuración; acceder a la BD directamente |
| **Usuario negocio observador** | ≤ 5 personas | Observación | Navegar el sistema; consultar dashboard, wallets, comercios, ventas QR, retiros; reportar comportamientos inesperados | Operar transacciones; ingresar datos reales; compartir accesos |
| **Operador interno** | ≤ 3 personas | Operativo controlado | Usar flujos de consulta y validación; revisar estados de retiros y ventas QR; reportar incidencias | Confirmar pagos reales; modificar entidades QA sin supervisión; ejecutar scripts SQL |
| **Aliado autorizado** | ≤ 2 personas (solo si aplica y fue pre-autorizado por el responsable de negocio por escrito) | Observación externa controlada | Solo navegación y consulta supervisada; debe completar inducción; firmó acuerdo de confidencialidad | Acceso sin inducción; ingresar datos propios; integrar sistemas externos |
| **Responsable técnico** | 1 persona (obligatoria) | Supervisión técnica y soporte | Acceso completo al ambiente del piloto; único autorizado para ejecutar scripts, modificar variables y reiniciar servicios | Hacer deploy a producción; compartir credenciales del ambiente; modificar código sin versionamiento |

---

## 5. Participantes no permitidos

Los siguientes perfiles no pueden acceder al piloto controlado bajo ninguna circunstancia:

- **Público general**: ninguna persona que no haya sido identificada, autorizada e inducida antes del inicio.
- **Clientes masivos**: no se admiten grupos de usuarios sin proceso de selección, autorización e inducción individual.
- **Comercios sin autorización**: ningún comercio que no haya sido explícitamente incluido en el listado de participantes del piloto.
- **Usuarios sin inducción**: toda persona debe completar la inducción antes de recibir acceso, sin excepción.
- **Menores de edad**: el sistema no ha sido evaluado para uso por personas menores de 18 años en un contexto financiero.
- **Personas con datos reales no autorizados**: nadie que vaya a ingresar información personal, bancaria o comercial real sin un acuerdo explícito previo.
- **Usuarios externos sin acuerdo**: ninguna persona externa al equipo XPAY sin acuerdo de confidencialidad firmado o sin autorización expresa del responsable de negocio.

> Si alguien solicita acceso durante el piloto, derivar al responsable de negocio. No compartir URLs, usuarios ni contraseñas sin autorización previa.

---

## 6. Alcance funcional permitido

| Funcionalidad | Permitido | Tipo | Evidencia esperada |
|--------------|-----------|------|--------------------|
| Login con usuario piloto | Sí | Operación controlada | Autenticación exitosa; token JWT obtenido; sesión activa en el dashboard |
| Dashboard operacional | Sí | Observación | Las 4 secciones cargan con datos QA simulados visibles |
| Consulta de wallets | Sí | Lectura | Listado de wallets, saldos y movimientos del flujo QA visibles |
| Consulta de comercios | Sí | Lectura | `Comercio Demo XPAY QA` visible con resumen financiero |
| Consulta de ventas QR | Sí | Lectura | Venta QR en estado `LIQUIDADA` visible en el listado |
| Consulta de ledger | Sí | Lectura | Transacciones contables con débitos = créditos visibles |
| Consulta de retiros | Sí | Lectura | Retiros en estados `PAGADO` y `RECHAZADO` visibles |
| Flujo financiero QA simulado | Sí | Observación / validación | El resultado del script A–H es coherente y navegable desde la UI |
| Reporte de incidencias | Sí | Operación controlada | Formato de reporte enviado al canal de soporte; respuesta recibida |
| Logout | Sí | Operación controlada | Sesión cerrada; redirección a pantalla de login |

---

## 7. Alcance funcional no permitido

Las siguientes acciones están **prohibidas** durante el piloto:

- **Dinero real**: no procesar pagos ni transferencias con dinero real.
- **Pagos bancarios reales**: no conectar el sistema a cuentas bancarias ni débitos automáticos reales.
- **Integración bancaria real**: no configurar APIs bancarias (ACH, transferencias interbancarias) reales en el ambiente.
- **PSE / Bre-B real**: no integrar pasarelas de pago colombianas reales durante el piloto.
- **Clientes externos masivos**: no permitir el registro libre o la incorporación de personas fuera del listado autorizado.
- **Campañas comerciales**: no usar el piloto como herramienta de marketing, demostración pública ni captación de clientes.
- **KYC real**: no implementar procesos de verificación de identidad con datos personales reales de clientes.
- **Datos personales reales**: no almacenar ni procesar información personal real sin política de privacidad aprobada y comunicada.
- **Conciliación bancaria real**: no iniciar procesos de reconciliación contra extractos bancarios reales.
- **Producción abierta**: no hacer el sistema accesible al público general sin proceso formal de lanzamiento.
- **Cambios de configuración sin autorización**: ningún participante modifica variables de entorno, base de datos o código sin aprobación del responsable técnico.

---

## 8. Datos permitidos

Durante el piloto solo se usan los siguientes tipos de datos:

| Tipo de dato | Descripción | Ejemplos |
|-------------|-------------|---------|
| **Datos ficticios** | Inventados explícitamente para prueba | Nombre: "Juan Piloto QA", Cédula: 900000001 |
| **Datos QA del seed** | Generados por `database/008_seed_qa_dataset.sql` | Personas CC 900000001–4, usuarios `qa.admin.xpay`, wallets QA |
| **Documentos ficticios** | Números de documento inventados y fuera del rango de documentos reales | CC 900000001, NIT 900999001 |
| **Emails de prueba** | Dominios de prueba o ficticios | `@xpay.test`, `@qa.test`, `@ejemplo.com` |
| **Comercios demo** | Comercio demo creado en el seed QA | `Comercio Demo XPAY QA`, NIT 900000001-1 |
| **QR demo** | Código QR de prueba creado en el seed | `QR-DEMO-XPAY-QA-001` |
| **Saldos simulados** | Generados por el script financiero QA | Recarga +100,000, transferencia 25,000, etc. |

---

## 9. Datos prohibidos

Los siguientes datos **no deben ingresarse** en ningún campo del sistema durante el piloto:

- **Cédulas reales** de personas naturales identificables.
- **Teléfonos reales** activos de personas.
- **Cuentas bancarias reales** (números de cuenta, CBU, IBAN, etc.).
- **Documentos reales** (pasaportes, licencias, contratos escaneados).
- **Fotos reales** de personas (selfies, documentos fotográficos).
- **Contratos reales** de servicios o compromisos comerciales.
- **Claves o contraseñas** de sistemas externos.
- **Tokens JWT** generados en producción (si existiera).
- **Cualquier información sensible** que identifique a personas reales o que pueda ser usada para fraude.

> Si un participante ingresa accidentalmente un dato real, debe notificarlo de inmediato al responsable técnico. El responsable técnico evaluará si es necesario limpiar la base de datos del piloto.

---

## 10. Reglas operativas del piloto

Todo participante debe conocer y aceptar estas reglas antes de recibir acceso:

- [ ] **Inducción completada**: todos los participantes leyeron este documento y el de onboarding antes de recibir acceso.
- [ ] **No es producción**: todos los participantes saben explícitamente que el sistema es un piloto de prueba, no el sistema financiero real.
- [ ] **No compartir tokens**: ningún participante comparte su token JWT con otras personas dentro ni fuera del equipo.
- [ ] **No subir datos reales**: ningún participante ingresa información personal, bancaria ni comercial real (ver sección 9).
- [ ] **Reportar incidencias**: todo comportamiento inesperado se reporta por el canal definido con el formato de `docs/QA_INTERNAL_ISSUES_TRACKING.md` sección 5.
- [ ] **Capturas revisadas**: antes de compartir cualquier captura de pantalla, verificar que no contiene datos sensibles, tokens ni contraseñas.
- [ ] **Horario definido**: las pruebas se ejecutan únicamente en el horario autorizado (ver sección 11).
- [ ] **Responsable técnico disponible**: durante cada sesión del piloto, el responsable técnico debe estar contactable.
- [ ] **Error financiero suspende el piloto**: si se detecta un saldo incorrecto, ledger desbalanceado o cualquier error financiero, el piloto se suspende de inmediato (ver sección 13).

---

## 11. Soporte y monitoreo

| Campo | Valor |
|-------|-------|
| **Canal de soporte** | _pendiente de definir_ (email, Slack, WhatsApp, canal interno) |
| **Horario de atención** | _pendiente de definir_ |
| **Responsable principal** | _pendiente de definir_ |
| **Responsable de backup** | _pendiente de definir_ |
| **Tiempo de respuesta — Crítica** | El mismo día · suspender piloto si aplica |
| **Tiempo de respuesta — Alta** | 1 día hábil · notificar al responsable técnico |
| **Tiempo de respuesta — Media** | 3 días hábiles |
| **Tiempo de respuesta — Baja** | Cuando haya capacidad |

**Cómo escalar incidencias:**

1. El participante detecta el problema.
2. Captura evidencia (screenshot o descripción detallada).
3. Envía el reporte al canal de soporte usando el formato de `docs/QA_INTERNAL_ISSUES_TRACKING.md` sección 5.
4. El responsable de soporte asigna ID (INC-XXX), clasifica la severidad y notifica al responsable técnico si es Crítica o Alta.
5. El responsable técnico investiga y actualiza el estado de la incidencia.

**Cómo comunicar una suspensión del piloto:**

1. El responsable técnico detecta o recibe reporte de criterio de suspensión (sección 13).
2. Notifica inmediatamente a todos los participantes por el canal de soporte: "El piloto está suspendido temporalmente."
3. Registra la causa en `docs/QA_INTERNAL_ISSUES_TRACKING.md`.
4. Comunica tiempo estimado de resolución.
5. Reanuda el piloto solo cuando la incidencia esté `Cerrada` con reprueba aprobada.

---

## 12. Criterios de éxito del piloto

| # | Criterio | Métrica | Evidencia | Responsable |
|---|----------|---------|-----------|-------------|
| 1 | Los participantes pueden iniciar sesión | 100% de participantes logran login con sus credenciales | Capturas de acceso exitoso; sin reportes de error de login | Responsable técnico |
| 2 | Dashboard carga correctamente | Las 4 secciones del dashboard son visibles con datos QA | Capturas del dashboard completo; sin reportes de sección vacía | Tester QA |
| 3 | Consultas funcionan correctamente | Wallets, retiros, ventas QR, ledger y comercios muestran datos | Capturas de cada sección; datos coherentes con el script QA | Tester QA |
| 4 | Operaciones QA simuladas son comprensibles | Los participantes entienden el flujo financiero sin asistencia constante | Feedback de participantes; menos de 3 preguntas de comprensión por sesión | Responsable negocio |
| 5 | Sin error financiero | 0 incidencias críticas de tipo financiero (saldo, ledger) | Tabla de incidencias; ledger balanceado al final del piloto | Responsable técnico |
| 6 | Sin endpoint protegido expuesto | 0 reportes de acceso sin token a datos protegidos | Verificación smoke test antes de cada sesión | Responsable técnico |
| 7 | Participantes reportan incidencias correctamente | ≥ 80% de reportes llegan con formato completo (descripción + evidencia) | Tabla de incidencias en `QA_INTERNAL_ISSUES_TRACKING.md` | Responsable QA |
| 8 | Soporte responde dentro del SLA | 100% de Críticas respondidas el mismo día; 100% de Altas en 1 día | Timestamp de reporte vs timestamp de primera respuesta | Responsable soporte |
| 9 | No se ingresan datos reales | 0 incidentes de dato real detectado en la BD del piloto | Revisión de BD por responsable técnico al cierre; logs de acceso | Responsable técnico |
| 10 | No se incumplen restricciones | 0 accesos no autorizados; 0 intentos de operación prohibida reportados | Tabla de participantes activa; reportes de soporte | Responsable negocio |

---

## 13. Criterios de suspensión inmediata

Suspender el piloto **de inmediato** y notificar a todos los participantes si ocurre cualquiera de las siguientes condiciones:

- **Error financiero**: saldo de wallet incorrecto después de una operación; saldo aplicado a cuenta incorrecta.
- **Ledger desbalanceado**: al menos una transacción contable con débitos ≠ créditos.
- **Endpoint protegido expuesto**: cualquier endpoint retorna datos (200) sin `Authorization: Bearer`.
- **Token filtrado**: un token JWT fue encontrado en un canal no seguro, documento público o captura compartida.
- **Dato real ingresado**: se detectó información personal, bancaria o comercial real en la base de datos del piloto.
- **Participante no autorizado**: se detecta acceso de una persona no incluida en la tabla de participantes (sección 4).
- **Ambiente confundido con producción**: un participante creyó estar operando el sistema financiero real.
- **Dashboard inoperante**: el dashboard no carga por más de 1 hora sin causa conocida e identificada.
- **Login inoperante**: el sistema no permite autenticar a usuarios con credenciales correctas durante más de 30 minutos.
- **Incidente crítico de seguridad**: cualquier comportamiento que sugiera una vulnerabilidad de seguridad activa o una brecha de datos.

**Procedimiento al suspender:**

1. Responsable técnico anuncia la suspensión en el canal de soporte.
2. Se registra la causa como incidencia Crítica en `docs/QA_INTERNAL_ISSUES_TRACKING.md`.
3. Se comunica al responsable de negocio.
4. El piloto se reanuda solo después de que la incidencia esté `Cerrada` con reprueba aprobada.
5. Si la suspensión supera 48 horas sin resolución, el piloto se cancela formalmente y se evalúa si abrir un nuevo ciclo QA.

---

## 14. Registro diario o por sesión

Completar por sesión de piloto o al cierre del día:

```
XPAY MVP — Registro de Sesión de Piloto
========================================

Fecha:                     ___________________________
Hora inicio:               ___________________________
Hora cierre:               ___________________________
Responsable de la sesión:  ___________________________

Participantes presentes:
  _______________________________________________________________

Funcionalidades revisadas:
  [ ] Login / logout
  [ ] Dashboard
  [ ] Wallets
  [ ] Comercios
  [ ] Ventas QR
  [ ] Retiros
  [ ] Ledger
  [ ] Reportes
  Otras: ___________________________________________________________

Incidencias reportadas durante la sesión:
  INC-___: ___________________________________________________________
  INC-___: ___________________________________________________________
  (agregar según necesidad)

Bloqueos o interrupciones:
  [ ] Ninguno
  [ ] Sí: _____________________________________________________________

Decisiones tomadas:
  _______________________________________________________________
  _______________________________________________________________

Observaciones generales:
  _______________________________________________________________
  _______________________________________________________________

¿El piloto continúa mañana?   [ ] Sí   [ ] No   [ ] Suspendido

Firma responsable sesión:      _________________
```

---

## 15. Acta de cierre del piloto

Diligenciar al finalizar el piloto:

```
XPAY MVP — Acta de Cierre del Piloto Controlado v0.1
====================================================

Fecha inicio:               ___________________________
Fecha cierre:               ___________________________
Commit evaluado:            ___________________________
Ambiente:                   ___________________________
URL backend:                ___________________________
URL frontend:               ___________________________

Responsable técnico:        ___________________________
Responsable negocio:        ___________________________
Responsable soporte:        ___________________________

Participantes totales:      ___
Sesiones realizadas:        ___

Total incidencias registradas:   ___

Por severidad:
  Críticas:   ___  (Cerradas: ___ / Abiertas: ___ )
  Altas:      ___  (Cerradas: ___ / Abiertas: ___ )
  Medias:     ___  (Cerradas: ___ / Diferidas: ___)
  Bajas:      ___  (Cerradas: ___ / Diferidas: ___)

Criterios de éxito cumplidos:   ___ / 10

Resultado del piloto:
  [ ] Exitoso: todos los criterios de éxito cumplidos
  [ ] Exitoso con observaciones: la mayoría cumplidos, pendientes documentados
  [ ] No exitoso: criterios críticos incumplidos

Decisión posterior:
  [ ] Repetir piloto (ajustes menores necesarios)
  [ ] Abrir nuevo ciclo QA (bugs críticos encontrados)
  [ ] Preparar fase preproducción (piloto exitoso)
  [ ] Detener avance (problemas mayores identificados)

Restricciones o condiciones para siguiente fase:
  _______________________________________________________________
  _______________________________________________________________

Observaciones finales:
  _______________________________________________________________
  _______________________________________________________________

Firma Responsable técnico:  _________________   Fecha: __________
Firma Responsable negocio:  _________________   Fecha: __________
```

---

## 16. Decisión posterior al piloto

| Decisión | Condición | Siguiente paso |
|----------|-----------|----------------|
| **Repetir piloto** | El piloto tuvo incidencias menores corregibles sin nuevo ciclo QA; alcance funcional no completo por restricciones de tiempo | Corregir las incidencias identificadas. Actualizar la tabla de participantes si es necesario. Planificar una segunda ronda con el mismo o nuevo grupo. Usar este mismo documento con versión 2.0. |
| **Abrir nuevo ciclo QA** | Se encontraron bugs de severidad Alta o Crítica que requieren corrección de código | Crear `docs/QA_INTERNAL_CYCLE_02.md` siguiendo la estructura de `QA_INTERNAL_CYCLE_01.md`. Corregir los bugs. Re-ejecutar el flujo QA completo. Evaluar nuevamente los criterios de salida. |
| **Preparar fase preproducción** | El piloto fue exitoso: ≥ 9/10 criterios cumplidos, 0 incidencias críticas abiertas, todos los participantes pudieron usar el sistema con fluidez | Definir alcance de preproducción (ambiente, participantes, datos). Evaluar necesidades de escalabilidad, seguridad avanzada y cumplimiento legal. No iniciar preproducción sin análisis técnico y legal previo. |
| **Detener avance** | Problemas fundamentales de diseño, seguridad o funcionalidad que no se resuelven con correcciones puntuales | Documentar los problemas en `docs/QA_INTERNAL_ISSUES_TRACKING.md`. Reunión de equipo para redefinir el alcance del MVP. No distribuir acceso adicional hasta resolver los problemas raíz. |

---

## 17. Documentos relacionados

| Documento | Rol en el piloto controlado |
|-----------|----------------------------|
| [`docs/QA_EXIT_CRITERIA_AND_PILOT_READINESS.md`](QA_EXIT_CRITERIA_AND_PILOT_READINESS.md) | Prerequisito: criterios de salida QA que deben cumplirse antes de iniciar el piloto |
| [`docs/QA_INTERNAL_ISSUES_TRACKING.md`](QA_INTERNAL_ISSUES_TRACKING.md) | Registro activo de incidencias durante el piloto; formato de reporte y flujo de atención |
| [`docs/QA_INTERNAL_USERS_ONBOARDING.md`](QA_INTERNAL_USERS_ONBOARDING.md) | Guía de inducción para los participantes del piloto |
| [`docs/QA_MASTER_E2E_CHECKLIST.md`](QA_MASTER_E2E_CHECKLIST.md) | Referencia de smoke test previo a cada sesión del piloto |
| [`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md) | Declaración formal de la versión: alcance, riesgos conocidos y criterios de aprobación |
| [`README.md`](../README.md) | Descripción del proyecto: endpoints, configuración y ecosistema QA completo |

---

*Este documento cubre el MVP XPAY — Fases 1 a 33. Actualizar si cambia el alcance del piloto, los participantes autorizados, las reglas operativas o los criterios de éxito.*
