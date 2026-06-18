# XPAY MVP — Registro y Seguimiento de Incidencias QA Internas

**Versión:** 1.0
**Fecha:** 2026-06-18
**Tipo:** Guía operativa QA — uso exclusivo QA / desarrollo

---

## 1. Propósito

Este documento organiza el registro, clasificación, priorización y seguimiento de incidencias reportadas por usuarios internos durante el proceso QA de XPAY MVP.

Cubre los reportes generados por el equipo QA técnico, por usuarios internos habilitados y por herramientas automatizadas (scripts, CI), consolidándolos en un formato único que facilita la toma de decisiones sobre correcciones y ciclos futuros.

**Restricciones vigentes:**

- **Ambiente QA / desarrollo.** Las incidencias corresponden únicamente al ambiente de prueba.
- **No producción.** No aplica a sistemas productivos ni clientes reales.
- **No dinero real.** Los errores financieros reportados son sobre datos ficticios de prueba.
- **No datos reales.** Los registros de incidencias no deben contener información personal real.
- **No sustituye herramienta formal.** Si el equipo adopta Jira, GitHub Issues, Linear u otra plataforma, este documento sirve como plantilla base para migrar la estructura. Ver sección 11.

---

## 2. Fuentes de incidencias

| Fuente | Qué puede reportar | Evidencia esperada |
|--------|-------------------|-------------------|
| **Ciclo QA Interno 01** | Casos fallidos o bloqueados en los 35 casos formales de `QA_MANUAL_TESTING.md` | Resultado `FALLIDO` o `BLOQUEADO` en la plantilla `QA_EXECUTION_RUN-YYYYMMDD.md`; screenshot del fallo |
| **Usuarios internos QA** | Comportamiento inesperado, flujos confusos, errores al navegar el sistema | Reporte en formato de sección 10 de `QA_INTERNAL_USERS_ONBOARDING.md`; screenshot o video |
| **Tester QA** | Bugs exploratorios fuera de los 35 casos formales, regresiones, edge cases | Reporte detallado con pasos reproducibles; comparación de comportamiento esperado vs obtenido |
| **Responsable técnico** | Errores detectados al configurar el ambiente, desplegar artefactos o revisar logs | Log de error del servidor, stack trace, resultado de curl o comando CLI |
| **Observador de negocio** | Comportamiento del sistema que no corresponde al flujo esperado desde perspectiva funcional | Descripción del flujo esperado vs observado; screenshot de la UI |
| **GitHub Actions** | Fallo en compilación de backend (`Backend Validation`) o build de frontend (`Frontend Build`) | URL del run fallido (`gh run view --log-failed`); output del step fallido |
| **Smoke test** | Backend no responde, frontend no carga, endpoints protegidos expuestos, JWT inválido | Output de los comandos `curl` del smoke test; respuesta HTTP con código y cuerpo |
| **Script financiero QA** | Paso A–H fallido, `"success": false` en respuesta, extracción de ID fallida, ledger inconsistente | Output del script `generate-qa-financial-ops.sh`; JSON de la respuesta del endpoint fallido |

---

## 3. Severidades

### Crítica

Requiere atención inmediata. **No liberar a usuarios internos mientras haya una incidencia crítica abierta.**

- Error financiero: saldo incorrecto después de una operación (recarga, transferencia, pago QR, retiro).
- Ledger desbalanceado: débitos ≠ créditos en una transacción contable.
- Endpoint protegido retorna datos (200) sin `Authorization: Bearer`.
- Operación aplica el saldo a la cuenta incorrecta.
- Datos del ambiente QA se mezclan o se pueden visualizar desde el ambiente productivo.
- Login exitoso con credenciales que no corresponden al usuario.

### Alta

Funcionalidad principal no opera. Bloquea la revisión de un módulo completo.

- Login no funciona con credenciales correctas.
- Dashboard no carga ninguna de sus 4 secciones.
- Lista de retiros vacía cuando existen retiros en BD.
- Pago QR falla sistemáticamente con datos QA correctos.
- Liquidación no cambia el estado de la venta.
- Flujo completo A–H del script QA no puede completarse.
- Error 500 no controlado en un flujo principal.

### Media

Funcionalidad opera pero con comportamiento incorrecto o poco claro. No bloquea la revisión general.

- Filtro de fechas o estados no aplica correctamente.
- Mensaje de error técnico visible al usuario (stack trace, excepción .NET).
- Tabla muestra valores incorrectos en columnas secundarias (no financieras).
- Problema intermitente que no se reproduce siempre (documentar pasos y frecuencia).
- Botón de retry no funciona en el primer intento.
- Orden inesperado en listado de movimientos.
- Formato de monto incorrecto (comas, puntos, decimales).

### Baja

Problema visual, de texto o cosmético. No afecta funcionalidad ni datos.

- Texto con error ortográfico o de traducción.
- Ícono desalineado o con tamaño incorrecto.
- Label con nombre que no corresponde al diseño esperado.
- Color de badge de estado que no sigue la convención visual.
- Tooltip faltante o texto del tooltip incorrecto.
- Ajuste menor de documentación QA.
- Mejora de experiencia de usuario sin impacto funcional.

---

## 4. Estados de incidencia

| Estado | Descripción | Responsable típico | Siguiente acción |
|--------|-------------|-------------------|-----------------|
| **Nueva** | Incidencia reportada, sin asignación ni análisis | Responsable QA | Asignar a responsable técnico o QA; iniciar análisis |
| **En análisis** | Se está evaluando si es un bug real, un comportamiento esperado o un error de uso | Responsable técnico o QA | Confirmar o rechazar la incidencia |
| **Confirmada** | Es un bug real. Causa identificada o en proceso de identificación | Responsable técnico | Asignar corrección o deferir |
| **En corrección** | Desarrollador trabajando en la corrección | Responsable técnico | Completar corrección y actualizar con commit de fix |
| **Corregida** | Corrección aplicada en el código o la configuración | Responsable técnico | Solicitar reprueba al tester o responsable QA |
| **En reprueba** | Tester o responsable QA verificando que la corrección resuelve el problema sin regresiones | Tester QA / Responsable QA | Cerrar si la reprueba pasa; reabrir si falla |
| **Cerrada** | Reprueba aprobada o incidencia resuelta. No hay acciones pendientes | Responsable QA | Consolidar en reporte de cierre; considerar en decisión de siguiente ciclo |
| **Rechazada** | No es un bug: comportamiento esperado, error de uso o fuera de alcance del MVP | Responsable técnico o QA | Documentar la razón del rechazo; comunicar al reportador |
| **Diferida** | Bug confirmado pero la corrección se pospone a un ciclo futuro por prioridad o alcance | Responsable técnico + QA | Documentar justificación; incluir en backlog del siguiente ciclo |

---

## 5. Formato de registro

Usar una fila por incidencia. Agregar filas según sea necesario.

| Campo | INC-001 | INC-002 | INC-003 |
|-------|---------|---------|---------|
| **Fecha** | | | |
| **Fuente** | | | |
| **Reportado por** | | | |
| **Módulo** | | | |
| **Severidad** | | | |
| **Estado** | | | |
| **Descripción breve** | | | |
| **Pasos para reproducir** | | | |
| **Resultado esperado** | | | |
| **Resultado obtenido** | | | |
| **Evidencia** | | | |
| **Responsable asignado** | | | |
| **Commit de corrección** | | | |
| **Reprueba requerida** | `Sí / No` | `Sí / No` | `Sí / No` |
| **Resultado reprueba** | `Aprobada / Fallida / Pendiente` | | |
| **Decisión** | `Cerrada / Diferida / Reabierta` | | |
| **Observaciones** | | | |

> Para incidencias críticas y altas, completar todos los campos antes de escalar. Para medias y bajas, al menos Descripción, Módulo, Severidad y Responsable.

---

## 6. Módulos permitidos

Clasificar cada incidencia en uno de estos módulos:

| Módulo | Descripción |
|--------|-------------|
| **Autenticación / sesión** | Login, logout, JWT, sesión expirada, redirección a login |
| **Dashboard** | Las 4 secciones del dashboard operacional |
| **Wallets** | Listado, saldo, movimientos, recarga manual, transferencia |
| **Comercios** | Listado de comercios, resumen financiero del comercio QA |
| **Retiros** | Solicitud, listado, confirmación de pago, rechazo |
| **Ventas QR** | Pago QR, estado CONTINGENCIA / LIQUIDADA, listado |
| **Ledger** | Transacciones contables, balanceo débito/crédito, listado de transacciones |
| **Reportes** | Estado de cuenta wallet, resumen comercio, resumen general, ledger por transacción |
| **Seguridad JWT** | Endpoints protegidos, 401 sin token, manejo de token inválido o expirado |
| **Frontend / UX** | Interfaz, navegación, componentes visuales, mensajes de error en pantalla |
| **Documentación QA** | Errores u omisiones en los documentos del ecosistema QA |
| **Scripts QA** | `generate-qa-financial-ops.sh`, `build-backend-qa.sh`, `build-frontend-qa.sh` |
| **Despliegue / ambiente** | Configuración del servidor, variables de entorno, base de datos, seed |

---

## 7. Flujo de atención

```
Incidencia reportada
       │
       ▼
  ┌─────────┐
  │  NUEVA  │
  └────┬────┘
       │
       ▼
  ┌────────────┐      ┌───────────┐
  │ EN ANÁLISIS│─────▶│ RECHAZADA │  (no es bug / comportamiento esperado)
  └─────┬──────┘      └───────────┘
        │
        ▼
  ┌─────────────┐      ┌──────────┐
  │  CONFIRMADA │─────▶│ DIFERIDA │  (bug real, corrección pospuesta)
  └──────┬──────┘      └──────────┘
         │
         ▼
  ┌──────────────┐
  │ EN CORRECCIÓN│
  └──────┬───────┘
         │
         ▼
  ┌──────────┐
  │ CORREGIDA│
  └─────┬────┘
        │
        ▼
  ┌─────────────┐
  │ EN REPRUEBA │
  └──────┬──────┘
         │                    ┌──────────────────────────────────────┐
         │  Reprueba falla ──▶│ REABIERTA → vuelve a EN CORRECCIÓN   │
         │                    └──────────────────────────────────────┘
         │  Reprueba pasa
         ▼
  ┌─────────┐
  │ CERRADA │
  └─────────┘
```

**Reglas del flujo:**

- Una incidencia solo pasa a `CERRADA` desde `EN REPRUEBA` con resultado aprobado, o desde `EN ANÁLISIS` si se rechaza.
- Si una corrección falla la reprueba, la incidencia se reabre y regresa a `EN CORRECCIÓN` con el ID original (no se crea una nueva).
- Una incidencia `DIFERIDA` no se cierra: se transfiere al backlog del siguiente ciclo QA.
- El responsable QA es el único que puede cambiar el estado a `CERRADA` o `RECHAZADA`.

---

## 8. SLA interno sugerido QA

> **Aviso:** Estos tiempos son referencia interna de equipo QA. No constituyen un compromiso comercial ni un contrato de servicio.

| Severidad | Revisión inicial | Liberación a usuarios internos | Notas |
|-----------|-----------------|-------------------------------|-------|
| **Crítica** | El mismo día de ser reportada | **Bloqueada** — no habilitar usuarios internos mientras haya una abierta | Notificar al responsable técnico de inmediato |
| **Alta** | 1 día hábil | Con advertencia: informar a usuarios internos que existe la incidencia | Documentar impacto en el módulo afectado |
| **Media** | 3 días hábiles | Permitida con observación registrada | Incluir en lista de observaciones conocidas |
| **Baja** | Cuando haya capacidad | Siempre permitida | Diferir al siguiente ciclo si no hay tiempo |

---

## 9. Criterios de cierre por severidad

### Crítica

- [ ] Corrección aplicada y commiteada al repositorio
- [ ] Commit de corrección referenciado en el registro de la incidencia
- [ ] Reprueba ejecutada y aprobada por tester QA o responsable QA
- [ ] Evidencia de reprueba capturada
- [ ] Sin impacto contable pendiente (ledger balanceado después de la corrección)
- [ ] CI (`Backend Validation` y `Frontend Build`) verde después del commit de corrección

### Alta

- [ ] Corrección aplicada y commiteada
- [ ] Flujo principal afectado probado de principio a fin
- [ ] Evidencia de reprueba registrada
- [ ] CI verde después del commit de corrección

### Media

- [ ] Corrección aplicada y commiteada, **O**
- [ ] Diferida con justificación documentada en la columna "Observaciones" del registro

### Baja

- [ ] Corrección aplicada, **O**
- [ ] Diferida al siguiente ciclo con registro de la decisión

---

## 10. Cuándo abrir Ciclo QA Interno 02

Abrir `docs/QA_INTERNAL_CYCLE_02.md` (siguiendo la estructura de `QA_INTERNAL_CYCLE_01.md`) si se cumple al menos una de estas condiciones:

| Condición | Justificación |
|-----------|--------------|
| Hay una o más incidencias de severidad **Crítica** en estado `Confirmada`, `En corrección` o con reprueba fallida | Una crítica abierta invalida la decisión de aprobación previa |
| Hay más de **3 incidencias de severidad Alta** abiertas simultáneamente | Indica que múltiples flujos principales están afectados |
| Se modifica **lógica financiera** del backend (controladores de wallets, QR, liquidación, retiros, ledger) | Cambios financieros requieren re-validación completa del flujo A–H y las cuentas contables |
| Se modifica **seguridad o JWT** (middleware, configuración de claims, expiración, CORS) | Cambios de seguridad requieren re-validar los casos QA-33 y QA-34 y el smoke test |
| Se modifica **frontend de flujos principales** (login, dashboard, wallets, retiros, ventas QR) | Cambios de UI en flujos críticos pueden introducir regresiones |
| Una corrección **falla la reprueba** y genera una regresión en un módulo previamente aprobado | Una regresión invalida los resultados del ciclo anterior para ese módulo |
| Hay un **cambio estructural en la base de datos** (nueva migración, columna nueva, tabla modificada) | Requiere re-ejecutar scripts 001–008 y validar integridad de datos QA |

> Si ninguna de estas condiciones aplica pero hay bugs medios/bajos diferidos: no abrir ciclo 02. Registrarlos en el backlog y resolverlos antes del siguiente ciclo mayor.

---

## 11. Relación con GitHub Issues

Si el equipo decide adoptar GitHub Issues como herramienta formal de seguimiento, cada incidencia registrada en este documento puede migrarse como un issue. Lineamientos:

**Labels sugeridas:**

| Label | Uso |
|-------|-----|
| `qa` | Toda incidencia originada en el proceso QA |
| `bug` | Comportamiento confirmado como incorrecto |
| `critical` | Severidad Crítica |
| `high` | Severidad Alta |
| `medium` | Severidad Media |
| `low` | Severidad Baja |
| `financial` | Incidencia que involucra saldos, ledger o flujo financiero |
| `security` | Incidencia que involucra autenticación, JWT o endpoints protegidos |
| `frontend` | Incidencia en la interfaz o experiencia de usuario |
| `backend` | Incidencia en el API, controladores o lógica de negocio |
| `docs` | Error u omisión en documentación QA |

**Reglas de seguridad al usar GitHub Issues:**

- **Nunca** pegar tokens JWT en el cuerpo del issue ni en los comentarios.
- **Nunca** incluir contraseñas, cédulas reales ni cuentas bancarias.
- **Nunca** adjuntar capturas de pantalla con datos personales reales visibles.
- Ante duda sobre si un dato es sensible, omitirlo y describir el problema sin el dato.
- Los issues son públicos si el repositorio es público. Verificar la visibilidad antes de crear.

**Mapeo de estados a labels de GitHub Issues:**

| Estado (este doc) | Label sugerida en GitHub Issues |
|-------------------|---------------------------------|
| Nueva | `qa`, `bug`, severidad |
| Confirmada | `confirmed` |
| En corrección | `in progress` |
| Corregida / En reprueba | `needs testing` |
| Cerrada | Cerrar el issue |
| Rechazada | `wontfix` o cerrar con comentario |
| Diferida | `deferred` o milestone del siguiente ciclo |

---

## 12. Reporte resumen de cierre

Completar al finalizar el período de acceso de usuarios internos o al cerrar el ciclo de incidencias:

```
XPAY QA Interno — Reporte Resumen de Incidencias
=================================================

Período:                   ___________________________
Ciclo QA asociado:         QA Interno 01
Responsable QA:            ___________________________
Responsable técnico:       ___________________________

Total incidencias registradas:   ___

Por severidad:
  Críticas:   ___  (Abiertas: ___ / Cerradas: ___ / Rechazadas: ___ / Diferidas: ___)
  Altas:      ___  (Abiertas: ___ / Cerradas: ___ / Rechazadas: ___ / Diferidas: ___)
  Medias:     ___  (Abiertas: ___ / Cerradas: ___ / Rechazadas: ___ / Diferidas: ___)
  Bajas:      ___  (Abiertas: ___ / Cerradas: ___ / Rechazadas: ___ / Diferidas: ___)

Por estado final:
  Cerradas:   ___
  Rechazadas: ___
  Diferidas:  ___
  Abiertas:   ___   ← debe ser 0 en críticas para continuar

Bugs críticos abiertos:    ___   ← debe ser 0 para aprobar
Bugs altos abiertos:       ___

Decisión:
  [ ] Continuar sin nuevo ciclo QA (sin críticas ni altas abiertas)
  [ ] Abrir Ciclo QA Interno 02 (ver sección 10)
  [ ] Diferir correcciones al siguiente ciclo mayor del MVP

Riesgos pendientes:
  _______________________________________________________________
  _______________________________________________________________

Próximas acciones:
  _______________________________________________________________
  _______________________________________________________________
```

---

## 13. Checklist de seguridad del reporte

Verificar antes de compartir cualquier reporte de incidencias con personas fuera del equipo técnico:

- [ ] No contiene tokens JWT (parciales ni completos)
- [ ] No contiene contraseñas en texto claro
- [ ] No contiene datos personales reales (nombres, cédulas, emails reales)
- [ ] No contiene cuentas bancarias reales ni NITs reales
- [ ] Evidencias (screenshots, videos) revisadas antes de adjuntar — sin datos sensibles visibles
- [ ] Capturas que muestran respuestas JSON sin tokens en los headers
- [ ] Si hay stack traces del servidor, no exponen rutas absolutas del servidor productivo

---

## 14. Documentos relacionados

| Documento | Rol en el seguimiento de incidencias |
|-----------|-------------------------------------|
| [`docs/QA_INTERNAL_USERS_ONBOARDING.md`](QA_INTERNAL_USERS_ONBOARDING.md) | Fuente principal de reportes de usuarios internos; formato de reporte de incidencias sección 10 |
| [`docs/QA_INTERNAL_CYCLE_01.md`](QA_INTERNAL_CYCLE_01.md) | Fuente de casos fallidos del ciclo QA formal; tabla de bugs sección 9 |
| [`docs/QA_MASTER_E2E_CHECKLIST.md`](QA_MASTER_E2E_CHECKLIST.md) | Señales de bloqueo que pueden generar incidencias críticas automáticamente |
| [`docs/QA_EXECUTION_TEMPLATE.md`](QA_EXECUTION_TEMPLATE.md) | Registro formal de ejecución; los fallidos de la plantilla se convierten en incidencias |
| [`docs/QA_MANUAL_TESTING.md`](QA_MANUAL_TESTING.md) | Define los 35 casos formales; criterios para clasificar un resultado como bug |
| [`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md) | Riesgos conocidos de la versión; incidencias que coincidan con riesgos conocidos se documentan como confirmados |
| [`README.md`](../README.md) | Descripción del proyecto: endpoints, configuración y ecosistema QA completo |
| [`docs/QA_EXIT_CRITERIA_AND_PILOT_READINESS.md`](QA_EXIT_CRITERIA_AND_PILOT_READINESS.md) | Criterios de salida QA: el estado de incidencias en este tracker es entrada directa para la evaluación de criterios 4 y 5 |

---

*Esta guía cubre el MVP XPAY — Fases 1 a 32. Actualizar si se adopta herramienta formal de tickets, si cambian las severidades o si se agregan nuevos módulos al alcance QA.*
