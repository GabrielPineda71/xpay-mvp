# XPAY MVP — Onboarding de Usuarios Internos QA

**Versión:** 1.0
**Fecha:** 2026-06-18
**Tipo:** Guía operativa QA — uso exclusivo QA / desarrollo

---

## 1. Propósito

Este documento se usa para habilitar usuarios internos una vez que el **Ciclo QA Interno 01** haya sido aprobado o aprobado con observaciones no bloqueantes.

Define quién puede acceder al ambiente QA, qué puede hacer, qué no puede hacer, cómo reportar incidencias y qué controles deben estar activos antes, durante y después del acceso.

**Restricciones vigentes durante todo el período de acceso interno:**

- **Ambiente QA / desarrollo.** No es el sistema productivo de XPAY.
- **No producción.** Ninguna acción afecta clientes reales ni cuentas reales.
- **No dinero real.** Todos los saldos, recargas y retiros son datos ficticios de prueba.
- **No datos reales de clientes.** Ningún usuario debe ingresar información personal, bancaria o comercial real.
- **No uso comercial.** Este acceso es para observación y prueba controlada, no para operar el negocio.
- **No aprobación regulatoria.** El MVP QA Candidate v0.1 no tiene licencia regulatoria de operación financiera.

---

## 2. Condiciones previas

Confirmar todos los ítems antes de habilitar cualquier usuario interno:

- [ ] **Ciclo QA Interno 01** está en estado `Aprobado` o `Aprobado con observaciones` (ver `docs/QA_INTERNAL_CYCLE_01.md` sección 11)
- [ ] Commit evaluado identificado y comunicado al equipo
- [ ] Backend QA disponible y respondiendo en `/health`
- [ ] Frontend QA disponible y cargando sin errores críticos
- [ ] `database/008_seed_qa_dataset.sql` ejecutado correctamente
- [ ] `scripts/generate-qa-financial-ops.sh` ejecutado: datos financieros QA disponibles
- [ ] Variables y accesos QA preparados por el responsable técnico
- [ ] **Responsable técnico asignado** con disponibilidad para soporte durante el período
- [ ] **Responsable de soporte QA asignado** para recibir reportes de incidencias
- [ ] Canal de reporte definido y comunicado (ver sección 10)
- [ ] Ningún bug crítico abierto que afecte las funcionalidades habilitadas
- [ ] Los bugs con decisión `Aprobado con observaciones` están documentados y comunicados

---

## 3. Usuarios internos permitidos

Solo los siguientes perfiles pueden acceder al ambiente QA interno:

| Perfil | Objetivo | Alcance permitido | Restricciones |
|--------|----------|-------------------|---------------|
| **Administrador interno XPAY** | Validar flujos de administración y reportes desde la perspectiva operativa | Dashboard, wallets, comercios, retiros, ventas QR, ledger — solo lectura y consulta | No crear entidades reales; no modificar configuración; no ejecutar scripts |
| **Operador interno XPAY** | Validar flujos operativos del día a día | Consulta de wallets, retiros y ventas QR; revisión de estados de transacciones QA | No confirmar pagos reales; no ejecutar operaciones financieras fuera del flujo QA generado |
| **Usuario negocio observador** | Entender el sistema desde la perspectiva del usuario final | Solo navegación y consulta del dashboard y resúmenes | No operar; solo observar; debe completar inducción antes del acceso |
| **Tester QA** | Ejecutar casos de prueba adicionales o exploratorios bajo supervisión | Todos los módulos habilitados según el plan de pruebas asignado | No modificar seed ni variables; reportar toda incidencia encontrada |
| **Responsable técnico** | Soporte, diagnóstico y supervisión durante el período de acceso interno | Acceso completo al ambiente QA | Único autorizado para ejecutar scripts, modificar variables o reiniciar el ambiente |

---

## 4. Usuarios no permitidos

Los siguientes perfiles **no pueden acceder** al ambiente QA interno bajo ninguna circunstancia:

- **Clientes reales** de XPAY o futuros clientes del producto.
- **Comercios reales** o representantes de comercios que vayan a operar con XPAY.
- **Usuarios finales externos** que no forman parte del equipo interno.
- **Aliados externos** (bancos, proveedores, integradores) que no hayan sido autorizados por el responsable técnico.
- **Usuarios con dinero real** que crean estar operando en un sistema financiero real.
- **Personas sin inducción previa** que no hayan leído las reglas de uso de este documento.

> Si alguien externo solicita acceso al ambiente QA, derivar al responsable técnico. No compartir URLs, usuarios ni contraseñas sin autorización.

---

## 5. Reglas de uso del ambiente

Todo usuario habilitado debe aceptar estas reglas antes de recibir acceso:

- [ ] Usar únicamente datos ficticios: nombres, documentos, correos y números de cuenta inventados.
- [ ] No cargar documentos reales (cédulas, extractos bancarios, facturas).
- [ ] No simular transacciones con dinero real ni interpretar saldos como balances financieros reales.
- [ ] No compartir tokens JWT con otras personas.
- [ ] No compartir capturas de pantalla que contengan tokens, contraseñas o datos de otras personas.
- [ ] No modificar configuraciones del sistema (variables de entorno, base de datos, CORS) sin autorización del responsable técnico.
- [ ] Reportar todo error o comportamiento inesperado con evidencia (screenshot + pasos) usando el formato de la sección 10.
- [ ] No usar el ambiente fuera del período autorizado (ver sección 8, campo "Fecha de expiración").
- [ ] No interpretar el sistema como producción ni tomarlo como base para decisiones de negocio reales.
- [ ] Comunicar al soporte QA si no entiende el comportamiento de alguna funcionalidad antes de asumir que es un bug.

---

## 6. Funcionalidades permitidas para usuarios internos

| Funcionalidad | Tipo | Descripción |
|--------------|------|-------------|
| Login con usuario QA | Operación | Autenticarse con credenciales QA proporcionadas por el responsable técnico |
| Dashboard operacional | Lectura | Ver las 4 secciones del dashboard: wallets, comercios, retiros y ventas QR |
| Consulta de wallets | Lectura | Ver listado de wallets, saldos y movimientos de las wallets QA |
| Consulta de comercios | Lectura | Ver `Comercio Demo XPAY QA` y su resumen financiero |
| Consulta de retiros | Lectura | Ver historial de retiros QA: `PENDIENTE`, `PAGADO` y `RECHAZADO` |
| Consulta de ventas QR | Lectura | Ver ventas QR generadas: `CONTINGENCIA` y `LIQUIDADA` |
| Consulta de ledger | Lectura | Ver transacciones contables del ledger QA |
| Revisión del flujo financiero QA | Validación | Verificar que los pasos A–H del script QA generaron datos coherentes y navegables desde la UI |
| Pruebas de errores controlados | Validación | Probar comportamiento del sistema ante errores de red, sesión expirada, token inválido y datos incorrectos |
| Logout | Operación | Cerrar sesión correctamente |

---

## 7. Funcionalidades restringidas

Los siguientes acciones están **prohibidas** para todos los usuarios internos:

- **Crear entidades reales:** no registrar personas, usuarios, comercios ni wallets con datos reales.
- **Ejecutar scripts SQL:** no acceder ni ejecutar scripts bajo `database/` o `scripts/`.
- **Cambiar variables de entorno:** no modificar `appsettings.json`, `appsettings.QA.json`, `frontend/.env` ni `ops/qa.env.local`.
- **Modificar la base de datos:** no insertar, actualizar ni eliminar registros directamente en la BD.
- **Hacer deploy:** no subir código ni artefactos a ningún servidor.
- **Confirmar pagos reales:** no intentar conectar el sistema a pasarelas de pago externas reales.
- **Integrar bancos reales:** no configurar cuentas bancarias reales en el ambiente QA.
- **Usar el ambiente productivo:** no intentar acceder a ninguna URL que no sea la del ambiente QA autorizado.
- **Compartir credenciales:** no compartir usuario, contraseña ni token con personas fuera del listado de la sección 8.

---

## 8. Checklist de habilitación de usuarios

Completar una fila por cada usuario habilitado:

| # | Nombre interno | Correo | Rol asignado | Autorizado por | Fecha habilitación | Fecha expiración | Estado | Observaciones |
|---|---------------|--------|--------------|----------------|--------------------|------------------|--------|---------------|
| 1 | | | | | | | `[ ] Activo` `[ ] Expirado` `[ ] Revocado` | |
| 2 | | | | | | | `[ ] Activo` `[ ] Expirado` `[ ] Revocado` | |
| 3 | | | | | | | `[ ] Activo` `[ ] Expirado` `[ ] Revocado` | |
| 4 | | | | | | | `[ ] Activo` `[ ] Expirado` `[ ] Revocado` | |
| 5 | | | | | | | `[ ] Activo` `[ ] Expirado` `[ ] Revocado` | |
| *(agregar filas según necesidad)* | | | | | | | | |

> El campo "Autorizado por" debe contener el nombre del responsable técnico o QA que aprobó el acceso.
> La fecha de expiración es obligatoria. Sin fecha de expiración no se habilita el usuario.

---

## 9. Comunicación de acceso

Usar esta plantilla al enviar accesos a los usuarios internos habilitados. **No incluir contraseñas en correos sin cifrar ni en canales públicos.**

---

```
Asunto: Acceso al ambiente QA interno — XPAY MVP v0.1

Hola [Nombre],

Te habilitamos acceso al ambiente QA interno de XPAY MVP para que puedas
revisar y validar el sistema antes de su lanzamiento.

IMPORTANTE — Lee esto antes de ingresar:
─────────────────────────────────────────
• Este ambiente es EXCLUSIVO para pruebas internas.
• NO es el sistema productivo de XPAY.
• NO hay dinero real. Los saldos son datos ficticios de prueba.
• NO uses datos personales reales (nombre, cédula, cuenta bancaria).
• NO compartas este acceso con nadie fuera del equipo autorizado.

Datos de acceso:
─────────────────────────────────────────
URL del sistema:  [URL_FRONTEND_QA]
Usuario:          [USUARIO_QA_ASIGNADO]
Contraseña:       [Enviada por canal seguro — ver indicación adjunta]

Período de acceso:
─────────────────────────────────────────
Desde:   [FECHA_INICIO]
Hasta:   [FECHA_EXPIRACION]

Qué puedes hacer:
─────────────────────────────────────────
✓ Navegar el dashboard y las secciones del sistema.
✓ Consultar wallets, comercios, retiros y ventas QR.
✓ Revisar el flujo financiero QA generado.
✓ Reportar cualquier error o comportamiento inesperado.

Qué NO puedes hacer:
─────────────────────────────────────────
✗ Ingresar datos reales (personas, cuentas, documentos).
✗ Compartir tu usuario o contraseña.
✗ Usar este sistema para operaciones comerciales.
✗ Acceder fuera del período indicado.

Si encuentras un error:
─────────────────────────────────────────
Canal de soporte:  [CANAL_DE_SOPORTE — email, Slack, WhatsApp, etc.]
Responsable:       [NOMBRE_RESPONSABLE_SOPORTE_QA]
Formato de reporte: Ver guía adjunta o solicitar el formulario al soporte.

Cualquier duda antes de ingresar, escríbenos a [CANAL_DE_SOPORTE].

Gracias por apoyar el proceso de validación interna.

Equipo XPAY
```

---

> **Nota de seguridad:** La contraseña debe enviarse por un canal separado y seguro (mensaje directo cifrado, gestor de contraseñas compartido, llamada, etc.). Nunca incluir la contraseña en el mismo correo que la URL y el usuario.

---

## 10. Reporte de incidencias

Usar este formato para reportar cualquier error o comportamiento inesperado. Enviar al canal de soporte definido.

```
XPAY QA Interno — Reporte de Incidencia
========================================

ID incidencia:       INC-____
Fecha:               ___________
Usuario que reporta: ___________
Módulo:              ___________
  [ ] Login     [ ] Dashboard  [ ] Wallets   [ ] Comercios
  [ ] Retiros   [ ] Ventas QR  [ ] Ledger    [ ] Otro: ___

Descripción del problema:
  _______________________________________________________________
  _______________________________________________________________

Pasos para reproducir:
  1. ___________________________________________________________
  2. ___________________________________________________________
  3. ___________________________________________________________
  (agregar pasos necesarios)

Resultado esperado:
  _______________________________________________________________

Resultado obtenido:
  _______________________________________________________________

Evidencia adjunta:
  [ ] Screenshot(s)  [ ] Video  [ ] Ninguna
  Nombre(s) de archivo: ___________________________________________

Severidad estimada:
  [ ] Crítica   [ ] Alta   [ ] Media   [ ] Baja
  (ver guía de severidades en el documento de onboarding)

Estado inicial:
  [ ] Abierta

Responsable asignado:
  _______________________________________________________________

Observaciones adicionales:
  _______________________________________________________________
  _______________________________________________________________
```

---

## 11. Severidades

| Severidad | Descripción | Ejemplos en XPAY |
|-----------|-------------|-----------------|
| **Crítica** | El sistema produce un resultado incorrecto que podría causar daño real si estuviera en producción, o una funcionalidad esencial no opera. Requiere atención inmediata. | Ledger desbalanceado (débitos ≠ créditos); endpoint protegido retorna datos sin token; retiro confirmado pero saldo no cambia; login exitoso con credenciales incorrectas |
| **Alta** | Funcionalidad principal no funciona correctamente pero no hay riesgo financiero directo. Bloquea la revisión de un módulo completo. | Dashboard no carga ninguna sección; lista de wallets vacía con datos en BD; pago QR falla con mensaje de error genérico; retiro no aparece después de ser creado |
| **Media** | Funcionalidad opera pero con comportamiento incorrecto o poco claro. No bloquea la revisión general. | Mensaje de error con texto técnico no comprensible para el usuario; monto mostrado con formato incorrecto; orden inesperado en listado de movimientos; botón retry no funciona en el primer intento |
| **Baja** | Problema visual, de texto o cosmético sin impacto en la funcionalidad. | Texto con error ortográfico; ícono desalineado; label con nombre incorrecto; color de estado no corresponde al diseño esperado; tooltip faltante |

> Ante la duda, escalar al soporte QA antes de clasificar. Es mejor sobre-escalar una incidencia que ignorarla.

---

## 12. Checklist de cierre de acceso

Completar al finalizar el período de acceso de cada usuario o del ciclo completo:

- [ ] Todos los usuarios internos terminaron sus pruebas o llegaron a la fecha de expiración
- [ ] Todas las incidencias reportadas fueron registradas con ID único (INC-XXX)
- [ ] Evidencias recibidas y archivadas por el responsable QA
- [ ] Accesos expirados o revocados (tabla sección 8 actualizada con estado)
- [ ] Tokens JWT invalidados si aplica (reinicio del backend QA o rotación de `Jwt__Key` en ambiente QA)
- [ ] Observaciones de usuarios internos consolidadas por el responsable QA
- [ ] Incidencias clasificadas por severidad y estado (`Abierta`, `Cerrada`, `Diferida`)
- [ ] Decisión sobre el siguiente ciclo tomada y documentada:
  - [ ] Ciclo QA Interno completado sin bugs bloqueantes → preparar siguiente fase del MVP
  - [ ] Bugs de severidad alta/crítica encontrados → abrir Ciclo QA Interno 02 después de correcciones
  - [ ] Bugs menores diferidos → documentar y continuar

---

## 13. Riesgos y controles

| Riesgo | Impacto | Control |
|--------|---------|---------|
| Un usuario ingresa datos personales reales (cédula, cuenta bancaria) | Exposición de información sensible en base de datos QA | Inducción obligatoria antes del acceso; instrucción explícita en la comunicación de acceso (sección 9); monitoreo por responsable técnico |
| Un usuario confunde el ambiente QA con producción y toma decisiones de negocio basadas en él | Decisiones incorrectas basadas en datos ficticios | Mensaje de advertencia en la URL del frontend QA; etiqueta `API: [entorno]` visible en el layout; comunicación de acceso aclara explícitamente que no es producción |
| Un token JWT es compartido con personas no autorizadas | Acceso no controlado al ambiente QA | Tokens tienen expiración configurada (`Jwt__ExpirationHours`); instrucción explícita de no compartir; el responsable técnico puede rotar la `Jwt__Key` en el ambiente QA para invalidar todos los tokens |
| Una operación financiera QA es malinterpretada como real | Confusión sobre el estado del sistema o sobre saldos | Todos los datos de seed tienen identificadores QA explícitos (`QA`, `Demo`, `XPAY QA`); saldos se generan con el script QA y son documentados como ficticios |
| Un bug crítico encontrado por usuarios internos no es reportado | El problema no es corregido antes de un ciclo más amplio | Canal de soporte activo; formato de reporte claro y accesible (sección 10); responsable QA disponible durante el período |
| Un acceso no es revocado al terminar el período autorizado | Acceso no controlado después del período de prueba | Fecha de expiración obligatoria en la tabla de sección 8; checklist de cierre de acceso en sección 12 |

---

## 14. Relación con QA Interno 01

Este documento se usa **después** de `docs/QA_INTERNAL_CYCLE_01.md`:

- El Ciclo QA Interno 01 es ejecutado por el equipo QA técnico con los 35 casos formales.
- Solo si el ciclo QA Interno 01 es **aprobado o aprobado con observaciones no bloqueantes**, se abre el acceso a usuarios internos siguiendo esta guía.
- Este documento **no reemplaza** el ciclo QA formal. Los usuarios internos no ejecutan los 35 casos de `QA_MANUAL_TESTING.md`; realizan una revisión libre y operativa del sistema.
- El objetivo del acceso interno es ampliar la revisión a perspectivas no técnicas: negocio, operaciones, administración.
- Los reportes de incidencias de usuarios internos se consolidan y pueden alimentar un **Ciclo QA Interno 02** si se encuentran bugs bloqueantes.

---

## 15. Documentos relacionados

| Documento | Rol en el onboarding |
|-----------|---------------------|
| [`docs/QA_INTERNAL_CYCLE_01.md`](QA_INTERNAL_CYCLE_01.md) | Prerequisito: el ciclo QA formal que debe estar aprobado antes de habilitar usuarios internos |
| [`docs/QA_MASTER_E2E_CHECKLIST.md`](QA_MASTER_E2E_CHECKLIST.md) | Secuencia completa de fases del ciclo QA; contexto de dónde encaja el onboarding |
| [`docs/QA_MANUAL_TESTING.md`](QA_MANUAL_TESTING.md) | Referencia de los 35 casos formales; útil para entender el alcance cubierto antes del onboarding |
| [`docs/QA_EXECUTION_TEMPLATE.md`](QA_EXECUTION_TEMPLATE.md) | Plantilla del ciclo QA formal; no usada por usuarios internos pero referencia de evidencias |
| [`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md) | Declaración formal de la versión: alcance, exclusiones y riesgos conocidos comunicados a usuarios internos |
| [`README.md`](../README.md) | Descripción del proyecto: endpoints, configuración y ecosistema QA completo |
| [`docs/QA_INTERNAL_ISSUES_TRACKING.md`](QA_INTERNAL_ISSUES_TRACKING.md) | Registro y seguimiento de incidencias QA: estados, severidades, flujo de atención y criterios de cierre |

---

*Esta guía cubre el MVP XPAY — Fases 1 a 31. Actualizar si cambian los perfiles de usuarios internos, las reglas de uso o los controles de seguridad del ambiente QA.*
