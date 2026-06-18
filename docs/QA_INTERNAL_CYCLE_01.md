# XPAY MVP — Ciclo QA Interno 01

**Versión:** 1.0
**Fecha:** 2026-06-18
**Tipo:** Paquete de ejecución QA — uso exclusivo QA / desarrollo

---

## 1. Propósito

Este documento se usa para ejecutar y cerrar el **primer ciclo QA interno de XPAY MVP QA Candidate v0.1**. Consolida la identificación del ciclo, los roles, el plan de ejecución, el registro de resultados, los bugs encontrados y el acta de cierre en un solo lugar.

Es el instrumento formal que el equipo interno diligencia durante y después de la ejecución de los 35 casos manuales QA.

**Restricciones vigentes durante todo el ciclo:**

- **No producción.** Este ciclo se ejecuta únicamente en ambiente QA o local.
- **No dinero real.** Todas las operaciones financieras son de prueba controlada.
- **No datos reales.** Personas, documentos, emails, cuentas bancarias y NITs son ficticios.
- **No deploy a producción.** Ninguna acción de este ciclo debe afectar el ambiente productivo.

---

## 2. Identificación del ciclo

| Campo | Valor |
|-------|-------|
| **Ciclo** | QA Interno 01 |
| **Versión evaluada** | XPAY MVP QA Candidate v0.1 |
| **Rama** | main |
| **Commit evaluado** | _pendiente de diligenciar_ |
| **Fecha inicio** | _pendiente_ |
| **Fecha cierre** | _pendiente_ |
| **Ambiente** | `[ ] Local` `[ ] Azure QA` `[ ] Otro: ___` |
| **URL backend** | _pendiente_ |
| **URL frontend** | _pendiente_ |
| **Responsable QA** | _pendiente_ |
| **Responsable técnico** | _pendiente_ |
| **Estado** | `[ ] Pendiente` `[ ] En ejecución` `[ ] Cerrado` |

---

## 3. Roles y responsabilidades

| Rol | Responsabilidad principal | Evidencia esperada |
|-----|--------------------------|-------------------|
| **Responsable técnico** | Confirmar que el código está en estado QA listo: CI verde, commit identificado, variables de entorno configuradas, seed ejecutado | Confirmación escrita de Fase 0 del checklist maestro; commit hash anotado en sección 2 |
| **Responsable QA** | Coordinar la ejecución, asignar tareas, consolidar resultados, emitir la decisión final y firmar el acta | Copia completada de `QA_EXECUTION_TEMPLATE.md` + acta firmada en sección 12 de este documento |
| **Ejecutor de pruebas** | Ejecutar los 35 casos QA-01 a QA-35, capturar screenshots, registrar resultados y bugs en la plantilla | Matriz de ejecución con resultado por caso, screenshots referenciados, bugs numerados |
| **Aprobador** | Revisar el acta de cierre y la decisión antes de comunicar a stakeholders | Firma en la sección 12 de este documento |
| **Observador de negocio** | *(Opcional)* Validar que los flujos cubren los casos de uso del negocio desde la perspectiva funcional | Observaciones documentadas en sección 12 |

---

## 4. Documentos base del ciclo

Leer y tener disponibles estos documentos antes de iniciar la ejecución:

| Documento | Qué aporta a este ciclo |
|-----------|------------------------|
| [`docs/QA_MASTER_E2E_CHECKLIST.md`](QA_MASTER_E2E_CHECKLIST.md) | Secuencia completa de 8 fases: desde CI verde hasta acta de aprobación |
| [`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md) | Alcance oficial de la versión: qué está incluido, qué está excluido, riesgos conocidos |
| [`docs/QA_DEPLOYMENT_RUNBOOK.md`](QA_DEPLOYMENT_RUNBOOK.md) | Cómo preparar el ambiente QA en Azure o local |
| [`docs/QA_OPERATIONS_VARIABLES.md`](QA_OPERATIONS_VARIABLES.md) | Cómo preparar TOKEN e IDs sin secretos en el repositorio |
| [`docs/QA_FINANCIAL_OPERATIONS_API.md`](QA_FINANCIAL_OPERATIONS_API.md) | Cómo generar operaciones financieras QA vía endpoints reales |
| [`docs/QA_MANUAL_TESTING.md`](QA_MANUAL_TESTING.md) | Los 35 casos de prueba con pasos, evidencias esperadas y criterios |
| [`docs/QA_EXECUTION_TEMPLATE.md`](QA_EXECUTION_TEMPLATE.md) | Plantilla que el ejecutor diligencia durante la ejecución |

---

## 5. Checklist de entrada

Antes de iniciar la ejecución de casos, confirmar todos los ítems:

**Ambiente y código:**

- [ ] CI verde: `Backend Validation` → `completed success`
- [ ] CI verde: `Frontend Build` → `completed success`
- [ ] Repositorio limpio: `git status` → `nothing to commit`
- [ ] Commit evaluado identificado y anotado en la sección 2

**Ambiente QA:**

- [ ] Backend accesible: `GET $API_BASE/health` responde OK
- [ ] Frontend accesible: pantalla de login carga sin errores
- [ ] Base de datos preparada: scripts SQL 001–007 ejecutados
- [ ] Seed QA ejecutado: `database/008_seed_qa_dataset.sql` completado sin errores
- [ ] `Comercio Demo XPAY QA` y `QR-DEMO-XPAY-QA-001` existen en BD

**Variables y autenticación:**

- [ ] `ops/qa.env.local` completado con valores reales
- [ ] `source ops/qa.env.local` ejecutado en el shell de trabajo
- [ ] Login con usuario QA funcional: `POST /api/auth/login` retorna token
- [ ] `ops/qa.env.local` no aparece en `git status`

**Datos financieros:**

- [ ] `scripts/generate-qa-financial-ops.sh` ejecutado exitosamente (pasos A–H)
- [ ] Ledger tiene ≥ 6 transacciones generadas
- [ ] Saldos de wallets QA son consistentes con las operaciones ejecutadas

**Documentación:**

- [ ] Copia de `docs/QA_EXECUTION_TEMPLATE.md` creada con nombre `QA_EXECUTION_RUN-YYYYMMDD.md`
- [ ] Responsable QA y ejecutor asignados
- [ ] No se usarán datos reales de clientes, cuentas ni dinero real

---

## 6. Alcance de pruebas del ciclo

| Módulo | Incluido | Casos QA | Documento origen | Evidencia esperada |
|--------|----------|----------|-----------------|-------------------|
| Backend público (`/health`, `/api/version`, Swagger) | Sí | Fase 6 del checklist | `QA_MASTER_E2E_CHECKLIST.md` | Respuestas HTTP correctas |
| Autenticación y sesión | Sí | QA-01 a QA-05 | `QA_MANUAL_TESTING.md` | Login/logout funcional, 401 sin token |
| Dashboard operacional | Sí | QA-30 a QA-32 | `QA_MANUAL_TESTING.md` | 4 secciones cargan con datos reales QA |
| Wallets | Sí | QA-06 a QA-10 | `QA_MANUAL_TESTING.md` | Listado, saldo y movimientos visibles |
| Transferencias | Sí | QA-11 a QA-13 | `QA_MANUAL_TESTING.md` | Transferencia exitosa entre wallets QA |
| Pagos QR | Sí | QA-14 a QA-17 | `QA_MANUAL_TESTING.md` | Venta en CONTINGENCIA y LIQUIDADA |
| Retiros comercio | Sí | QA-21 a QA-25 | `QA_MANUAL_TESTING.md` | PENDIENTE, PAGADO y RECHAZADO |
| Reportes | Sí | QA-26 a QA-29 | `QA_MANUAL_TESTING.md` | Resúmenes de wallet, comercio, ledger |
| Seguridad JWT | Sí | QA-33 a QA-34 | `QA_MANUAL_TESTING.md` | Endpoints protegidos retornan 401 sin token |
| Manejo de errores frontend | Sí | QA-35 | `QA_MANUAL_TESTING.md` | Mensaje de error de red visible, botón retry |

**Fuera de alcance en este ciclo:**

- Pruebas de carga o volumen.
- Pruebas de penetración o seguridad avanzada.
- Flujos de administración de usuarios (no implementado en v0.1).
- Integración con pasarelas de pago externas.

---

## 7. Plan de ejecución

Orden recomendado. Marcar cada paso al completar.

- [ ] **1.** Confirmar Fase 0 del checklist maestro: CI verde, repo limpio, commit identificado
- [ ] **2.** Confirmar ambiente QA: backend accesible, base de datos lista, JWT configurado
- [ ] **3.** Confirmar seed QA: scripts 001–008 ejecutados, entidades QA verificadas
- [ ] **4.** Confirmar variables: `source ops/qa.env.local`, TOKEN e IDs disponibles
- [ ] **5.** Ejecutar `scripts/generate-qa-financial-ops.sh` — confirmar pasos A–H exitosos
- [ ] **6.** Ejecutar smoke test (Fase 6 del checklist maestro) — 13 ítems
- [ ] **7.** Ejecutar QA-01 a QA-35 según `docs/QA_MANUAL_TESTING.md`
- [ ] **8.** Registrar resultado de cada caso en la plantilla de ejecución (`APROBADO` / `FALLIDO` / `BLOQUEADO`)
- [ ] **9.** Registrar bugs encontrados en la sección 9 de este documento y en la plantilla
- [ ] **10.** Reprueba de correcciones si algún bug fue corregido durante el ciclo
- [ ] **11.** Completar resumen de resultados en la sección 8
- [ ] **12.** Verificar criterios de cierre en la sección 10
- [ ] **13.** Emitir decisión y diligenciar acta en la sección 12

---

## 8. Registro resumido de resultados

Completar al finalizar la ejecución:

| Métrica | Valor |
|---------|-------|
| Total de casos | 35 |
| Ejecutados | ___ |
| Aprobados | ___ |
| Fallidos | ___ |
| Bloqueados | ___ |
| Reprueba pendiente | ___ |
| No ejecutados | ___ |
| **% aprobación** | ___ % |
| **% avance** | ___ % |

**Por módulo:**

| Módulo | Casos | Aprobados | Fallidos | Bloqueados |
|--------|-------|-----------|----------|------------|
| Autenticación y sesión (QA-01–05) | 5 | ___ | ___ | ___ |
| Wallets (QA-06–10) | 5 | ___ | ___ | ___ |
| Transferencias (QA-11–13) | 3 | ___ | ___ | ___ |
| Pagos QR (QA-14–17) | 4 | ___ | ___ | ___ |
| Liquidación QR (QA-18–20) | 3 | ___ | ___ | ___ |
| Retiros comercio (QA-21–25) | 5 | ___ | ___ | ___ |
| Reportes (QA-26–29) | 4 | ___ | ___ | ___ |
| Dashboard (QA-30–32) | 3 | ___ | ___ | ___ |
| Seguridad JWT (QA-33–34) | 2 | ___ | ___ | ___ |
| Errores y edge cases (QA-35) | 1 | ___ | ___ | ___ |

---

## 9. Bugs encontrados

Registrar cada bug descubierto durante el ciclo. Ver detalles en la plantilla de ejecución.

| Bug ID | Caso QA | Módulo | Severidad | Estado | Responsable | Decisión | Observaciones |
|--------|---------|--------|-----------|--------|-------------|----------|---------------|
| BUG-001 | | | | | | | |
| BUG-002 | | | | | | | |
| BUG-003 | | | | | | | |
| *(agregar filas según necesidad)* | | | | | | | |

**Valores de Severidad:** `CRÍTICO` / `ALTO` / `MEDIO` / `BAJO`

**Valores de Estado:** `ABIERTO` / `EN CORRECCIÓN` / `CORREGIDO` / `ACEPTADO` / `RECHAZADO`

**Valores de Decisión:** `Bloquea ciclo` / `Aceptado con observación` / `Diferido al próximo ciclo`

---

## 10. Criterios de cierre

Verificar antes de emitir la decisión:

**Cobertura:**

- [ ] 100% de casos críticos ejecutados (no puede quedar ninguno sin resultado)
- [ ] 100% de casos críticos aprobados (`APROBADO`)

**Calidad:**

- [ ] ≥ 90% de casos de prioridad alta aprobados
- [ ] Ningún bug con severidad `CRÍTICO` en estado `ABIERTO`
- [ ] Ningún error en operaciones financieras (ledger desbalanceado, transacción fallida no controlada)
- [ ] Ningún endpoint protegido retorna 200 sin `Authorization: Bearer`

**Documentación:**

- [ ] Evidencias (screenshots) capturadas para todos los casos
- [ ] Plantilla de ejecución `QA_EXECUTION_RUN-YYYYMMDD.md` completada
- [ ] Acta de cierre diligenciada en la sección 12

---

## 11. Decisión del ciclo

Marcar la decisión y registrar la justificación:

**Decisión:**

```
[ ] Aprobado para usuarios internos

[ ] Aprobado con observaciones
    Observaciones aceptadas:
    ________________________________________________________________
    ________________________________________________________________

[ ] No aprobado
    Motivo:
    ________________________________________________________________
    ________________________________________________________________
    Próximo paso: abrir Ciclo QA Interno 02 después de las correcciones
```

---

## 12. Acta de cierre

Diligenciar al cerrar el ciclo:

```
XPAY MVP — Acta de Cierre — Ciclo QA Interno 01
================================================

Fecha inicio:               ___________________________
Fecha cierre:               ___________________________
Commit evaluado:            ___________________________
Ambiente:                   ___________________________
URL backend:                ___________________________
URL frontend:               ___________________________

Responsable QA:             ___________________________
Responsable técnico:        ___________________________
Ejecutor(es) de pruebas:    ___________________________
Aprobador:                  ___________________________

Resultados:
  Total de casos:           35
  Aprobados:                ___
  Fallidos:                 ___
  Bloqueados:               ___

Bugs encontrados:
  Críticos cerrados:        ___
  Críticos abiertos:        ___   ← debe ser 0 para aprobar
  Altos cerrados:           ___
  Altos abiertos:           ___
  Medios/Bajos abiertos:    ___

Decisión:
  [ ] Aprobado
  [ ] Aprobado con observaciones
  [ ] No aprobado

Bugs pendientes aceptados (si aplica):
  _______________________________________________________________
  _______________________________________________________________

Observaciones finales:
  _______________________________________________________________
  _______________________________________________________________

Firma Responsable QA:       _________________   Fecha: __________
Firma Responsable Técnico:  _________________   Fecha: __________
Firma Aprobador:            _________________   Fecha: __________
```

---

## 13. Próximos pasos según resultado

| Resultado | Condición | Próximo paso |
|-----------|-----------|--------------|
| **Aprobado** | Todos los criterios de la sección 10 cumplidos | Comunicar resultado a stakeholders. Seguir `docs/QA_INTERNAL_USERS_ONBOARDING.md` para habilitar usuarios internos. Distribuir el acta firmada. Archivar la plantilla de ejecución completada. |
| **Aprobado con observaciones** | Criterios cumplidos pero con bugs de baja severidad aceptados y documentados | Seguir `docs/QA_INTERNAL_USERS_ONBOARDING.md` para habilitar usuarios internos con la lista de observaciones conocidas adjunta. Abrir tickets de seguimiento para los bugs aceptados. Definir fecha de revisión. |
| **No aprobado** | Al menos un criterio de la sección 10 incumplido | Abrir **Ciclo QA Interno 02**: corregir los bugs bloqueantes, re-ejecutar solo los casos fallidos o bloqueados si los cambios son acotados, o ciclo completo si los cambios afectan múltiples módulos. No compartir con usuarios internos hasta aprobar. |

---

## 14. Documentos relacionados

| Documento | Rol en este ciclo |
|-----------|------------------|
| [`docs/QA_MASTER_E2E_CHECKLIST.md`](QA_MASTER_E2E_CHECKLIST.md) | Secuencia completa de fases operativas; referencia de comandos y señales de bloqueo |
| [`docs/QA_EXECUTION_TEMPLATE.md`](QA_EXECUTION_TEMPLATE.md) | Plantilla de registro de casos, bugs y evidencias durante la ejecución |
| [`docs/QA_MANUAL_TESTING.md`](QA_MANUAL_TESTING.md) | Definición de los 35 casos: pasos, evidencias esperadas, criterios de aprobación |
| [`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md) | Declaración formal de la versión: alcance, exclusiones, riesgos conocidos y criterios |
| [`README.md`](../README.md) | Descripción del backend: endpoints, configuración, ecosistema QA completo |
| [`docs/QA_INTERNAL_USERS_ONBOARDING.md`](QA_INTERNAL_USERS_ONBOARDING.md) | Siguiente paso si el ciclo es aprobado: guía para habilitar usuarios internos QA |
| [`docs/QA_INTERNAL_ISSUES_TRACKING.md`](QA_INTERNAL_ISSUES_TRACKING.md) | Registro y seguimiento de incidencias QA: estados, severidades, flujo de atención y criterios de cierre |

---

*Este documento cubre el Ciclo QA Interno 01 — XPAY MVP QA Candidate v0.1. Al abrir un segundo ciclo, crear `docs/QA_INTERNAL_CYCLE_02.md` siguiendo esta misma estructura.*
