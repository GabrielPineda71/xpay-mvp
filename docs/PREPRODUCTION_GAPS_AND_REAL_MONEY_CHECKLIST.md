# XPAY MVP — Checklist de Preproducción y Brechas para Dinero Real

**Versión:** 1.0
**Fecha:** 2026-06-18
**Tipo:** Documento de evaluación estratégica — uso para gestión, legal, financiero y técnico

---

## 1. Propósito

Este documento define qué falta antes de que XPAY MVP pueda pasar del piloto controlado a un ambiente de **preproducción o producción con dinero real**, datos reales, comercios reales o apertura pública.

Identifica las brechas en cinco dimensiones (técnica, financiera/contable, seguridad, legal/regulatoria, operativa) que deben resolverse antes de autorizar cualquier operación financiera real.

**Qué NO hace este documento:**

- **No autoriza producción.** Este checklist no habilita al sistema para operación productiva.
- **No autoriza dinero real.** La autorización para dinero real requiere aprobación multidisciplinaria explícita (técnica, financiera, legal, seguridad).
- **No reemplaza validación legal o regulatoria.** Solo un abogado o equipo de cumplimiento puede certificar el cumplimiento normativo.
- **No reemplaza auditoría de seguridad.** Una auditoría formal de seguridad requiere metodologías y herramientas especializadas.
- **No reemplaza certificación contable o financiera.** Un contador o auditor financiero debe validar los flujos contables antes de operar con dinero real.

---

## 2. Estado actual permitido

Lo que el sistema XPAY MVP puede hacer hoy:

| Actividad | Permitido | Condición | Documento origen |
|-----------|-----------|-----------|-----------------|
| QA interno (35 casos formales) | **Sí** | Ejecutar `docs/QA_MASTER_E2E_CHECKLIST.md` completo | `docs/QA_INTERNAL_CYCLE_01.md` |
| Acceso de usuarios internos | **Sí** | Después de QA Interno 01 aprobado | `docs/QA_INTERNAL_USERS_ONBOARDING.md` |
| Piloto controlado | **Sí** | Después de cumplir criterios de salida QA con firma doble | `docs/QA_EXIT_CRITERIA_AND_PILOT_READINESS.md` |
| Datos ficticios | **Sí** | Solo datos con identificadores QA explícitos | `database/008_seed_qa_dataset.sql` |
| Dinero simulado (saldos QA) | **Sí** | Solo saldos generados por script QA; no representan valor real | `scripts/generate-qa-financial-ops.sh` |
| QR demo | **Sí** | Solo `QR-DEMO-XPAY-QA-001` u otros con prefijo QA/Demo | `database/008_seed_qa_dataset.sql` |
| Retiros simulados | **Sí** | Solo retiros del flujo QA; no implican pagos reales | `docs/QA_FINANCIAL_OPERATIONS_API.md` |
| Ledger QA | **Sí** | Solo con entidades y transacciones del flujo QA controlado | `docs/QA_FINANCIAL_OPERATIONS_API.md` |
| Sin banco real | **Sí** (correcto) | El sistema no tiene integración bancaria real — esto es una condición activa | Todas las fases hasta Fase 33 |

---

## 3. Estado no permitido todavía

Las siguientes actividades **no están autorizadas** con el MVP en su estado actual:

- **Dinero real**: ninguna operación de recarga, transferencia, pago QR ni retiro puede involucrar fondos reales.
- **Saldos reales**: los saldos de wallets no representan ni deben representar dinero real depositado o respaldado.
- **Pagos reales**: ningún pago QR puede aplicarse a una transacción comercial real con valor monetario.
- **Retiros reales**: ningún proceso de retiro puede desembolsar dinero real a cuentas bancarias.
- **Integración bancaria real**: el sistema no puede conectarse a APIs bancarias, ACH, transferencias interbancarias ni cámaras de compensación reales.
- **PSE / Bre-B real**: no se puede integrar PSE, Bre-B ni ninguna pasarela de pago colombiana real.
- **Clientes externos masivos**: no se puede abrir el sistema al registro libre de usuarios del público general.
- **Comercios reales abiertos**: no se pueden incorporar comercios reales que esperen liquidaciones monetarias.
- **KYC real**: no se puede implementar verificación de identidad con datos reales de usuarios sin aprobación legal y técnica.
- **Datos personales reales sin política aprobada**: no se pueden almacenar ni procesar datos personales reales sin una política de privacidad, términos y condiciones y base legal definida.
- **Producción pública**: no se puede abrir el sistema a acceso público sin completar las brechas de esta guía.

---

## 4. Brechas técnicas

| # | Brecha | Riesgo si no se resuelve | Requerido antes de dinero real | Responsable sugerido | Estado |
|---|--------|--------------------------|-------------------------------|---------------------|--------|
| T1 | **Observabilidad y logging formal** | Sin trazas, imposible diagnosticar errores financieros en producción | Sí — crítico | Responsable técnico | `[~]` Fase 35: correlation ID + request logging básico implementados. Pendiente: App Insights / agregación formal / alertas. |
| T2 | **Manejo de errores productivo** | Stack traces expuestos al usuario; información de servidor filtrada | Sí | Responsable técnico | `[~]` Fase 41: ErrorHandlingMiddleware captura excepciones no controladas, loguea internamente con correlationId, responde JSON 500 genérico sin stack trace. Pendiente: catálogo de errores por dominio, ProblemDetails si se decide, mapeo de excepciones de negocio, mensajes localizados, pruebas externas. |
| T3 | **Rate limiting / throttling** | El sistema es vulnerable a abuso por volumen de requests | Sí | Responsable técnico | `[~]` Fase 38: FixedWindow por IP en endpoint de login (20 req/min). Pendiente: límites más finos por endpoint, otros endpoints sensibles, alertas de intentos. |
| T4 | **Backups y restauración probados** | Pérdida de datos contables y de saldos sin posibilidad de recuperación | Sí — crítico | Responsable técnico + Ops | `[ ]` |
| T5 | **Monitoreo de uptime** | Sin alerta de caídas; no hay SLA real posible | Sí | Responsable técnico + Ops | `[~]` Fase 35: `GET /api/diagnostics/ping` disponible como probe básico. Pendiente: monitor externo, alertas automáticas. |
| T6 | **Alertas automáticas** | Errores financieros no detectados en tiempo real | Sí — crítico | Responsable técnico | `[ ]` |
| T7 | **Estrategia de rollback técnico documentada y probada** | Corrección de errores en producción sin plan de reversión genera daño mayor | Sí | Responsable técnico | `[ ]` |
| T8 | **Ambientes separados: QA / Preproducción / Producción** | Datos de prueba y datos reales mezclados; riesgo de corrupción | Sí — crítico | Responsable técnico | `[ ]` |
| T9 | **Configuración de secretos en Key Vault o equivalente** | Credenciales en variables de entorno sin cifrar ni rotación automática | Sí — crítico | Responsable técnico | `[ ]` |
| T10 | **Pruebas de carga básicas** | El sistema puede colapsar ante volumen de transacciones real | Sí | Responsable técnico | `[ ]` |

---

## 5. Brechas financieras/contables

| # | Brecha | Riesgo si no se resuelve | Requerido antes de dinero real | Responsable sugerido | Estado |
|---|--------|--------------------------|-------------------------------|---------------------|--------|
| F1 | **Conciliación bancaria** | Imposible verificar que los saldos del sistema coinciden con las cuentas bancarias reales | Sí — crítico | Responsable financiero | `[ ]` |
| F2 | **Integración real con banco o proveedor de pagos** | Sin mecanismo de entrada o salida de dinero real | Sí | Responsable técnico + financiero | `[ ]` |
| F3 | **Control de saldos vs cuentas reales** | Diferencias entre saldos del sistema y saldos bancarios reales sin detección | Sí — crítico | Responsable financiero | `[ ]` |
| F4 | **Reversos y anulaciones formales** | Sin mecanismo para deshacer operaciones erróneas en producción real | Sí — crítico | Responsable técnico + financiero | `[ ]` |
| F5 | **Cierres diarios / cortes contables** | Sin control de cierre; imposible generar reportes financieros confiables | Sí | Responsable financiero | `[ ]` |
| F6 | **Auditoría contable del flujo** | El flujo de doble entrada no ha sido validado por un contador externo | Sí | Auditor externo / contador | `[ ]` |
| F7 | **Reportes financieros regulatorios** | Sin reportes para entidades de control o impuestos | Sí (si aplica) | Responsable financiero + legal | `[ ]` |
| F8 | **Control de doble gasto** | Una transacción puede ejecutarse dos veces por error o ataque | Sí — crítico | Responsable técnico | `[ ]` |
| F9 | **Manejo de disputas** | Sin proceso formal para resolver disputas entre usuario y comercio sobre pagos | Sí | Responsable operativo + legal | `[ ]` |
| F10 | **Segregación de cuentas contables QA/Producción** | Datos de prueba contaminan los registros contables reales | Sí — crítico | Responsable técnico + financiero | `[ ]` |
| F11 | **Aprobación de responsable financiero** | Sin firma del responsable financiero que avale el sistema para dinero real | Sí — crítico | Responsable financiero | `[ ]` |

---

## 6. Brechas de seguridad

| # | Brecha | Riesgo si no se resuelve | Requerido antes de dinero real | Responsable sugerido | Estado |
|---|--------|--------------------------|-------------------------------|---------------------|--------|
| S1 | **Revisión OWASP Top 10** | Vulnerabilidades conocidas (injection, XSS, IDOR, etc.) sin evaluar | Sí — crítico | Security Lead / auditor externo | `[~]` Fase 37: hardening HTTP básico (headers de seguridad). No reemplaza auditoría formal OWASP. Pendiente: revisión completa por Security Lead o auditor externo. |
| S2 | **Manejo formal de secretos** | Claves y tokens en variables de entorno sin protección ni rotación | Sí — crítico | Responsable técnico | `[ ]` |
| S3 | **Rotación de llaves JWT** | Tokens comprometidos sin mecanismo de invalidación masiva | Sí | Responsable técnico | `[ ]` |
| S4 | **MFA para administradores** | Acceso administrativo protegido solo por contraseña | Sí | Responsable técnico | `[ ]` |
| S5 | **Control de roles y permisos más granular** | Roles actuales (ADMIN_XPAY, OPERADOR_XPAY, COMERCIO) pueden ser insuficientes para operación real | Sí | Responsable técnico | `[ ]` |
| S6 | **Auditoría de accesos** | Sin registro de quién accedió a qué, cuándo y desde dónde | Sí | Responsable técnico | `[~]` Fase 39: auditoría básica por ILogger (LOGIN_SUCCESS/FAILURE, operaciones financieras, accesos admin/ledger). Pendiente: persistencia en BD, dashboard, retención, SIEM/App Insights, trazabilidad completa por usuario/rol. |
| S7 | **Protección contra fuerza bruta** | El endpoint de login no tiene rate limiting ni bloqueo por intentos fallidos | Sí | Responsable técnico | `[~]` Fase 38: rate limiting activo en login (20 req/min por IP, respuesta 429). Pendiente: lockout por usuario, monitoreo de intentos fallidos, WAF/Front Door. |
| S8 | **Hardening de CORS** | Configuración actual puede ser demasiado permisiva para producción | Sí | Responsable técnico | `[~]` Fase 40: orígenes explícitos requeridos; guard startup en no-Development; sin AllowAnyOrigin; startup log de orígenes configurados. Pendiente: política definitiva por dominio productivo, revisión con Front Door/App Gateway, pruebas de seguridad externas. |
| S9 | **HTTPS obligatorio** | Sin certificado SSL/TLS en el ambiente de piloto local; requerido en cualquier ambiente expuesto | Sí — crítico | Responsable técnico + Ops | `[ ]` |
| S10 | **Revisión de exposición de Swagger** | Swagger en producción expone toda la API públicamente | Sí | Responsable técnico | `[~]` Fase 36: configurable vía `ApiDocs:EnableSwagger`; deshabilitado por defecto fuera de Development. Pendiente: política formal, auth adicional si se expone en preprod, decisión final antes de dinero real. |
| S11 | **Política de sesiones** | Sin tiempo máximo de sesión, sin invalidación de sesiones antiguas al cambiar contraseña | Sí | Responsable técnico | `[ ]` |
| S12 | **Análisis de vulnerabilidades** | Sin escaneo de dependencias ni análisis de código estático formal | Sí | Security Lead / herramienta automatizada | `[ ]` |

---

## 7. Brechas legales/regulatorias

| # | Brecha | Riesgo si no se resuelve | Requerido antes de datos/dinero real | Responsable sugerido | Estado |
|---|--------|--------------------------|--------------------------------------|---------------------|--------|
| L1 | **Términos y condiciones** | Sin base legal para el uso del servicio por parte de usuarios | Sí — crítico | Legal / abogado | `[ ]` |
| L2 | **Política de privacidad** | Sin declaración de cómo se tratan los datos personales | Sí — crítico | Legal / abogado | `[ ]` |
| L3 | **Tratamiento de datos personales** | Sin base legal para procesar nombres, documentos, emails de usuarios reales | Sí — crítico | Legal / abogado + DPO | `[ ]` |
| L4 | **Habeas Data / Ley 1581 de 2012 (Colombia)** | Incumplimiento de la ley de protección de datos personales aplicable | Sí — crítico | Legal / abogado | `[ ]` |
| L5 | **Contratos con comercios** | Sin acuerdo legal que regule las obligaciones de XPAY con los comercios | Sí | Legal / abogado | `[ ]` |
| L6 | **Autorización para tratamiento de información** | Sin consentimiento explícito del usuario para tratar sus datos | Sí — crítico | Legal / Product | `[ ]` |
| L7 | **Revisión regulatoria fintech/pagos** | Operar como intermediario de pagos puede requerir autorización de Superfinanciera u otro regulador | Sí — crítico | Legal especializado en fintech | `[ ]` |
| L8 | **Cumplimiento AML / KYC (si aplica)** | Sin controles contra lavado de activos; riesgo regulatorio alto | Sí (si se opera con dinero real) | Legal + Compliance | `[ ]` |
| L9 | **Conservación de registros** | Sin política de retención de datos contables y transaccionales conforme a ley | Sí | Legal / financiero | `[ ]` |
| L10 | **Responsabilidades frente a usuarios** | Sin definición de límites de responsabilidad de XPAY ante errores o pérdidas | Sí | Legal / abogado | `[ ]` |

---

## 8. Brechas operativas/soporte

| # | Brecha | Riesgo si no se resuelve | Requerido antes de usuarios/dinero real | Responsable sugerido | Estado |
|---|--------|--------------------------|----------------------------------------|---------------------|--------|
| O1 | **Mesa de ayuda formal** | Sin canal de soporte estructurado para usuarios con problemas reales | Sí | Responsable operativo | `[ ]` |
| O2 | **Canal de incidentes 24/7 (o con SLA definido)** | Incidentes críticos sin atención fuera de horario de oficina | Sí | Responsable operativo + técnico | `[ ]` |
| O3 | **Responsable de guardia definido** | Sin persona asignada para resolver emergencias en horarios no laborales | Sí | Responsable técnico | `[ ]` |
| O4 | **Manual de soporte operativo** | Operadores sin guía para resolver problemas frecuentes de usuarios reales | Sí | Responsable operativo | `[ ]` |
| O5 | **Procedimiento de bloqueo/desbloqueo de usuarios** | Sin mecanismo formal para suspender usuarios sospechosos | Sí | Responsable técnico + operativo | `[ ]` |
| O6 | **Procedimiento de reverso de transacciones** | Sin protocolo para deshacer operaciones erróneas reportadas por usuarios | Sí — crítico | Responsable financiero + técnico | `[ ]` |
| O7 | **Comunicación de incidentes a usuarios** | Sin plantilla ni protocolo para informar caídas o errores a usuarios reales | Sí | Responsable operativo | `[ ]` |
| O8 | **Escalamiento formal de incidentes** | Sin cadena de escalamiento definida para crisis (financiera, seguridad, técnica) | Sí | Responsable técnico + negocio | `[ ]` |
| O9 | **Horarios de soporte definidos y comunicados** | Usuarios no saben cuándo pueden recibir ayuda | Sí | Responsable operativo | `[ ]` |
| O10 | **Capacitación de operadores** | Operadores sin entrenamiento formal para el sistema; riesgo de errores operativos | Sí | Responsable operativo + técnico | `[ ]` |

---

## 9. Criterios mínimos para autorizar dinero real

Todos los ítems deben estar marcados antes de aprobar cualquier operación con dinero real:

**Aprobaciones:**

- [ ] **Legal aprobado**: abogado o equipo legal firmó que el sistema puede operar con dinero real bajo el marco normativo aplicable
- [ ] **Seguridad aprobado**: Security Lead o auditor externo confirmó que las brechas S1–S12 están resueltas o mitigadas
- [ ] **Financiero/contable aprobado**: responsable financiero o contador externo validó el flujo contable y la conciliación
- [ ] **Operaciones aprobado**: las brechas O1–O10 están resueltas y el equipo de soporte está entrenado y activo
- [ ] **QA final aprobado**: se ejecutó un ciclo QA completo sobre el ambiente de preproducción (no solo QA de desarrollo)
- [ ] **Piloto controlado cerrado sin incidencias críticas**: el acta de cierre del piloto está firmada con resultado exitoso

**Controles técnicos:**

- [ ] **Conciliación bancaria probada**: al menos una prueba de extremo a extremo con banco/proveedor real validada
- [ ] **Reversos de transacciones probados**: el procedimiento de reversión fue ejecutado y validado en preproducción
- [ ] **Soporte activo**: canal de soporte operativo con SLA definido y responsable asignado
- [ ] **Monitoreo activo**: alertas configuradas para errores financieros, caídas y anomalías
- [ ] **Backups probados**: al menos un ciclo de backup y restauración exitoso en el ambiente de preproducción
- [ ] **Responsable ejecutivo firma autorización**: la persona con autoridad legal y de negocio firmó la autorización formal para operar con dinero real

---

## 10. Criterios mínimos para comercios/clientes reales

Antes de incorporar un comercio o cliente real al sistema:

**Por cada comercio:**

- [ ] Contrato o acuerdo de servicio firmado entre XPAY y el comercio
- [ ] Consentimiento de tratamiento de datos del representante legal del comercio
- [ ] Perfil del comercio creado correctamente en el sistema (nombre, NIT, categoría, wallet COMERCIO)
- [ ] Límites de retiro y operación definidos y comunicados al comercio
- [ ] Canal de soporte disponible y comunicado al comercio
- [ ] Reglas de retiro, tiempos y comisiones comunicadas por escrito
- [ ] Canal de reporte de incidencias del comercio definido

**Por cada usuario/cliente:**

- [ ] Consentimiento de tratamiento de datos aceptado por el usuario
- [ ] Comunicación de riesgos entregada al usuario (el sistema no garantiza fondos, es piloto)
- [ ] Prueba de onboarding completada (el usuario navegó el sistema y entendió el flujo)
- [ ] Salida del piloto controlado documentada como condición previa al acceso de cliente real

---

## 11. Matriz de decisión preproducción

| Decisión | Condición | Aprobadores requeridos | Siguiente paso |
|----------|-----------|----------------------|----------------|
| **Mantener en QA/piloto controlado** | Hay brechas críticas abiertas en cualquier dimensión (T, F, S, L, O) | Responsable técnico + Responsable QA | Priorizar resolución de brechas; no avanzar hasta que las brechas críticas estén cerradas |
| **Avanzar a preproducción sin dinero real** | Brechas técnicas y de seguridad resueltas; brechas legales y financieras en proceso | Responsable técnico + Responsable negocio | Preparar ambiente de preproducción segregado. Ejecutar ciclo QA en preproducción. Sin datos ni dinero real todavía. |
| **Avanzar a piloto con dinero real limitado** | Todos los criterios de la sección 9 cumplidos; primeras 3–5 transacciones reales supervisadas | Responsable técnico + Responsable financiero + Legal + Negocio (firma de todos) | Definir monto máximo, número de transacciones y usuarios del piloto con dinero real. Monitoreo en tiempo real. Equipo de respuesta disponible. |
| **No avanzar** | Señales de bloqueo absoluto activas (sección 12) o aprobación faltante de algún actor clave | Responsable técnico notifica al equipo | Resolver señales de bloqueo primero. No hay excepciones a las señales de bloqueo absoluto. |

---

## 12. Señales de bloqueo absoluto

La presencia de cualquiera de las siguientes señales **impide avanzar** a cualquier etapa con dinero real o datos reales:

- **Ledger desbalanceado**: al menos una transacción contable con débitos ≠ créditos.
- **Saldos inconsistentes**: discrepancia entre saldos del sistema y registros esperados.
- **Token JWT filtrado**: un token fue encontrado fuera de los canales seguros del equipo.
- **Endpoint protegido expuesto**: cualquier endpoint retorna datos (200) sin `Authorization: Bearer`.
- **Sin responsable financiero asignado**: nadie puede responder por los flujos contables del sistema.
- **Sin política de privacidad aprobada**: no existe base legal para tratar datos personales reales.
- **Sin soporte activo**: no hay canal operativo para atender usuarios o comercios reales.
- **Sin backups probados**: el sistema puede perder datos irreversiblemente.
- **Sin conciliación bancaria**: imposible verificar que el sistema refleja el estado bancario real.
- **Bug crítico abierto en `docs/QA_INTERNAL_ISSUES_TRACKING.md`**: incidencia crítica sin resolución confirmada.

> Estas señales no admiten excepciones. Si se presenta alguna, **detener el avance** hasta resolverla completamente.

---

## 13. Acta de evaluación preproducción

Diligenciar al evaluar si el sistema puede avanzar hacia preproducción o dinero real:

```
XPAY MVP — Acta de Evaluación de Preproducción
===============================================

Fecha:                         ___________________________
Commit evaluado:               ___________________________
Ambiente actual:               ___________________________
Resultado del piloto:          [ ] Exitoso  [ ] Exitoso con obs.  [ ] No exitoso

Brechas abiertas por dimensión:
  Técnicas (T1–T10):           ___ abiertas de 10
  Financieras (F1–F11):        ___ abiertas de 11
  Seguridad (S1–S12):          ___ abiertas de 12
  Legales (L1–L10):            ___ abiertas de 10
  Operativas (O1–O10):         ___ abiertas de 10

  Total brechas abiertas:      ___ de 53
  Brechas críticas abiertas:   ___   ← debe ser 0 para avanzar a dinero real

Señales de bloqueo absoluto:   ___   ← debe ser 0

Riesgos aceptados (no críticos):
  _______________________________________________________________
  _______________________________________________________________

Decisión:
  [ ] Mantener en QA/piloto controlado
  [ ] Avanzar a preproducción sin dinero real
  [ ] Avanzar a piloto con dinero real limitado (requiere todas las firmas)
  [ ] No avanzar

Restricciones o condiciones para la siguiente etapa:
  _______________________________________________________________
  _______________________________________________________________

Firma Responsable técnico:     _________________   Fecha: __________
Firma Responsable financiero:  _________________   Fecha: __________
Firma Responsable legal:       _________________   Fecha: __________
Firma Responsable seguridad:   _________________   Fecha: __________
Firma Responsable negocio:     _________________   Fecha: __________
```

---

## 14. Roadmap mínimo sugerido antes de producción

| Área | Entregable mínimo | Prioridad | Dependencia | Responsable sugerido |
|------|-------------------|-----------|-------------|---------------------|
| Técnico | Logging y observabilidad con alertas (T1, T5, T6) | Alta | Ninguna | Responsable técnico |
| Técnico | Ambientes separados QA/Preprod/Prod (T8) | Alta | Ninguna | Responsable técnico |
| Técnico | Secretos en Key Vault o equivalente (T9) | Alta | Ninguna | Responsable técnico |
| Técnico | Rate limiting y protección fuerza bruta (T3, S7) | Alta | Ninguna | Responsable técnico |
| Técnico | Backups y restauración probados (T4) | Alta | Ambiente Preprod | Responsable técnico + Ops |
| Seguridad | Revisión OWASP Top 10 (S1) | Alta | Ambiente Preprod | Security Lead / auditor |
| Seguridad | HTTPS obligatorio (S9) | Alta | Dominio + certificado | Responsable técnico |
| Seguridad | Swagger deshabilitado o protegido en Preprod/Prod (S10) | Media | Ambiente Preprod | Responsable técnico |
| Seguridad | MFA para administradores (S4) | Alta | Sistema de identidad | Responsable técnico |
| Financiero | Conciliación bancaria con banco/proveedor real (F1, F2, F3) | Alta | Contrato bancario | Responsable financiero + técnico |
| Financiero | Reversos y anulaciones formales (F4) | Alta | Integración bancaria | Responsable técnico + financiero |
| Financiero | Auditoría contable externa del flujo (F6) | Alta | Flujo estable | Auditor externo |
| Legal | Términos y condiciones + Política de privacidad (L1, L2, L3) | Alta | Ninguna | Abogado / legal |
| Legal | Revisión regulatoria fintech (L7) | Alta — potencialmente bloqueante | Modelo de negocio definido | Legal especializado |
| Legal | Contratos con comercios (L5) | Media | Comercios identificados | Legal |
| Operativo | Mesa de ayuda y canal de soporte formal (O1, O2) | Alta | Equipo operativo | Responsable operativo |
| Operativo | Manual de soporte y capacitación operadores (O4, O10) | Media | Sistema estable | Responsable operativo |
| Operativo | Procedimiento de reverso de transacciones (O6) | Alta | Integración bancaria | Responsable financiero + técnico |

---

## 15. Documentos relacionados

| Documento | Rol en la evaluación de preproducción |
|-----------|--------------------------------------|
| [`docs/PILOT_CONTROLLED_OPERATING_PLAN.md`](PILOT_CONTROLLED_OPERATING_PLAN.md) | Prerequisito: el piloto controlado cuyo resultado alimenta la evaluación de este checklist |
| [`docs/QA_EXIT_CRITERIA_AND_PILOT_READINESS.md`](QA_EXIT_CRITERIA_AND_PILOT_READINESS.md) | Criterios de salida QA que deben cumplirse antes del piloto y antes de esta evaluación |
| [`docs/QA_INTERNAL_ISSUES_TRACKING.md`](QA_INTERNAL_ISSUES_TRACKING.md) | Estado de incidencias QA; bugs críticos abiertos son señal de bloqueo absoluto (sección 12) |
| [`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md) | Declaración formal del alcance del MVP y riesgos conocidos de la versión actual |
| [`README.md`](../README.md) | Descripción del proyecto: endpoints, configuración y ecosistema QA completo |

---

*Este documento cubre el MVP XPAY — Fases 1 a 34. Actualizar si cambia el modelo de negocio, el marco regulatorio aplicable, los requisitos técnicos de preproducción o el alcance del piloto con dinero real.*
