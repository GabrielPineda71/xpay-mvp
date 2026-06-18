# XPAY MVP — Criterios de Salida QA Interno y Preparación de Piloto Controlado

**Versión:** 1.0
**Fecha:** 2026-06-18
**Tipo:** Documento de decisión — uso exclusivo QA / desarrollo / gestión

---

## 1. Propósito

Este documento define cuándo el MVP XPAY puede salir del proceso de QA interno para pasar a un **piloto controlado limitado** con un grupo acotado de participantes autorizados.

**Qué no es esta decisión:**

- **No equivale a producción general.** Un piloto controlado no es un lanzamiento público.
- **No autoriza dinero real automáticamente.** El uso de dinero real requiere autorización adicional, análisis legal y financiero, y acuerdo explícito por escrito.
- **No autoriza clientes externos masivos.** El piloto opera con un número limitado de participantes definidos y autorizados previamente.
- **No reemplaza aprobación legal.** Este documento no tiene validez jurídica, financiera, regulatoria ni de cumplimiento.
- **No reemplaza aprobación de seguridad avanzada.** Este documento no certifica el sistema como seguro para operaciones financieras reguladas.

> La decisión de avanzar a piloto controlado es una decisión de equipo, no de una sola persona. Requiere firma del responsable técnico y del responsable de negocio.

---

## 2. Prerrequisitos

Confirmar todos los ítems antes de evaluar los criterios de salida:

**Ciclo QA:**

- [ ] `docs/QA_INTERNAL_CYCLE_01.md` cerrado con estado `Aprobado` o `Aprobado con observaciones`
- [ ] Acta de cierre del Ciclo QA Interno 01 firmada (sección 12 de `QA_INTERNAL_CYCLE_01.md`)

**Incidencias:**

- [ ] Todas las incidencias de severidad **Crítica** en estado `Cerrada` o `Rechazada`
- [ ] Todas las incidencias de severidad **Alta** con decisión registrada (`Cerrada`, `Diferida` con justificación, o `Rechazada`)
- [ ] `docs/QA_INTERNAL_ISSUES_TRACKING.md` actualizado con el estado final de cada incidencia

**Ambiente y validaciones técnicas:**

- [ ] Ledger validado: todas las operaciones QA tienen débitos = créditos
- [ ] Smoke test aprobado (Fase 6 del `QA_MASTER_E2E_CHECKLIST.md`)
- [ ] `Backend Validation` en GitHub Actions → `completed success` en el commit evaluado
- [ ] `Frontend Build` en GitHub Actions → `completed success` en el commit evaluado

**Personas y recursos:**

- [ ] Ciclo de onboarding interno completado (al menos un usuario interno validó el sistema)
- [ ] Canal de soporte interno definido y activo
- [ ] Ambiente QA o piloto identificado (URL, servidor, base de datos)
- [ ] Commit de release identificado y anotado
- [ ] **Responsable técnico** asignado con disponibilidad durante el piloto
- [ ] **Responsable de negocio** asignado para coordinar participantes y comunicación

---

## 3. Criterios obligatorios de salida QA

Todos los criterios de esta tabla deben cumplirse para avanzar a piloto controlado. Un criterio incumplido es bloqueante.

| # | Criterio | Evidencia requerida | Documento origen | Responsable | Estado |
|---|----------|---------------------|-----------------|-------------|--------|
| 1 | `Backend Validation` en CI → `completed success` | URL del run en GitHub Actions | `.github/workflows/backend-validation.yml` | Responsable técnico | `[ ]` |
| 2 | `Frontend Build` en CI → `completed success` | URL del run en GitHub Actions | `.github/workflows/frontend-build.yml` | Responsable técnico | `[ ]` |
| 3 | QA Interno 01 aprobado | Acta firmada sección 12 de `QA_INTERNAL_CYCLE_01.md` | `docs/QA_INTERNAL_CYCLE_01.md` | Responsable QA | `[ ]` |
| 4 | 0 incidencias críticas abiertas | Tabla de incidencias sin filas en estado ≠ Cerrada/Rechazada con severidad Crítica | `docs/QA_INTERNAL_ISSUES_TRACKING.md` | Responsable QA | `[ ]` |
| 5 | 0 errores financieros abiertos (saldo incorrecto, ledger desbalanceado) | Consulta a `GET /api/admin/ledger-transacciones`; validación manual de débitos = créditos | `docs/QA_FINANCIAL_OPERATIONS_API.md` | Responsable técnico | `[ ]` |
| 6 | 0 endpoints protegidos expuestos sin token | Pruebas QA-33 y QA-34 aprobadas; `curl` sin `Authorization` retorna 401 | `docs/QA_MANUAL_TESTING.md` | Responsable técnico | `[ ]` |
| 7 | Ledger balanceado en todas las operaciones QA | Evidencia de paso A–H del script QA; consulta de ledger sin inconsistencias | `scripts/generate-qa-financial-ops.sh` | Responsable técnico | `[ ]` |
| 8 | Login y sesión estables | QA-01 a QA-05 aprobados; login con usuario QA funcional | `docs/QA_MANUAL_TESTING.md` | Tester QA | `[ ]` |
| 9 | Dashboard operativo | QA-30 a QA-32 aprobados; las 4 secciones cargan con datos | `docs/QA_MANUAL_TESTING.md` | Tester QA | `[ ]` |
| 10 | Wallets, retiros, ventas QR y ledger consultables | QA-06 a QA-29 en estado ≥ 90% aprobados | `docs/QA_MANUAL_TESTING.md` | Tester QA | `[ ]` |
| 11 | Documentación QA completa y disponible | Los 10 documentos del ecosistema QA existen y están referenciados en `README.md` | `README.md` | Responsable QA | `[ ]` |
| 12 | Canal de soporte definido y comunicado | Canal activo; al menos un responsable asignado con horario | Sección 11 de este documento | Responsable negocio | `[ ]` |

---

## 4. Criterios bloqueantes

La presencia de cualquiera de los siguientes criterios **impide avanzar al piloto controlado**:

- **Bug crítico abierto** en `docs/QA_INTERNAL_ISSUES_TRACKING.md` con estado distinto de `Cerrada` o `Rechazada`.
- **Ledger desbalanceado**: al menos una transacción contable con débitos ≠ créditos.
- **Error de saldo**: wallet con saldo incorrecto después de operaciones QA A–H.
- **Endpoint protegido expuesto**: cualquier endpoint bajo `/api/` que retorne 200 sin `Authorization: Bearer`.
- **Login inoperante**: `POST /api/auth/login` no retorna token con credenciales correctas.
- **Dashboard inoperante**: ninguna de las 4 secciones del dashboard carga datos.
- **Incidencia alta sin decisión registrada**: severidad Alta en estado `Nueva`, `En análisis` o `Confirmada` sin fecha de resolución ni justificación de diferimiento.
- **Ambiente no identificado**: no existe URL de piloto ni base de datos preparada.
- **No existe responsable técnico**: nadie asignado con disponibilidad durante el piloto.
- **No existe canal de soporte**: participantes del piloto no tienen a quién reportar.
- **Dato real detectado en ambiente QA**: se encontró información personal, bancaria o comercial real en la base de datos QA.
- **Token expuesto**: un token JWT fue encontrado en documentos, issues, capturas o commits del repositorio.

> Si se detecta cualquier criterio bloqueante después de haber decidido avanzar, el piloto debe suspenderse de inmediato. Ver sección 12.

---

## 5. Alcance permitido del piloto controlado

El piloto controlado opera bajo estas condiciones:

- **Número limitado de participantes:** máximo definido explícitamente antes del inicio (ej. 5–15 personas internas o aliadas autorizadas).
- **Solo usuarios internos o aliados explícitamente autorizados:** cada participante debe estar en la tabla de habilitación de `QA_INTERNAL_USERS_ONBOARDING.md` o en un listado equivalente firmado por el responsable de negocio.
- **Datos ficticios o controlados:** ningún participante ingresa información personal real, bancaria real ni comercial real durante el piloto.
- **Sin dinero real**, salvo autorización expresa posterior por escrito con análisis legal, financiero y de riesgo previo.
- **Sin apertura pública:** no se publica la URL del piloto, no se hacen anuncios públicos, no se permite registro libre.
- **Sin integración bancaria real:** no se conectan cuentas bancarias, pasarelas de pago ni APIs financieras externas reales.
- **Sin campañas comerciales:** no se usa el piloto para marketing, ventas ni captación de clientes.
- **Sin promesa de disponibilidad:** el piloto puede ser interrumpido sin previo aviso por el responsable técnico si se detecta un criterio de suspensión.

---

## 6. Alcance no permitido

Las siguientes actividades están **prohibidas** durante el piloto controlado:

- **Producción abierta**: no hacer el sistema accesible al público general.
- **Clientes reales masivos**: no incorporar cientos o miles de usuarios sin evaluación de escalabilidad y seguridad.
- **Comercios reales sin contrato**: no operar con comercios que esperen recibir pagos o liquidaciones reales.
- **Dinero real**: no procesar pagos con dinero real hasta tener autorización expresa, análisis legal y acuerdo formal.
- **Publicidad del sistema**: no anunciar el sistema en redes sociales, medios o canales comerciales durante el piloto.
- **Operaciones bancarias reales**: no conectar el sistema a cuentas bancarias reales ni realizar transferencias bancarias.
- **Datos personales reales sin política aprobada**: no almacenar ni procesar información personal de personas reales sin política de privacidad aprobada.
- **Uso regulado sin aprobación**: no operar como institución financiera regulada sin las licencias correspondientes.

---

## 7. Matriz de decisión

| Decisión | Condición | Responsable que aprueba | Siguiente paso |
|----------|-----------|------------------------|----------------|
| **Avanzar a piloto controlado** | Los 12 criterios de la sección 3 cumplidos y 0 criterios bloqueantes activos | Responsable técnico + Responsable negocio (ambas firmas requeridas) | Completar el plan de piloto (sección 8); comunicar a participantes; iniciar piloto con fecha definida |
| **Avanzar con restricciones** | Los criterios mínimos críticos cumplidos (criterios 1–8), pero 1–3 criterios medios pendientes con justificación documentada | Responsable técnico + Responsable negocio con restricciones anotadas en el acta (sección 13) | Documentar las restricciones; comunicarlas a participantes; definir fecha de revisión de los criterios pendientes |
| **No avanzar** | Al menos un criterio bloqueante activo o un criterio obligatorio incumplido sin justificación aceptada | Responsable QA notifica al equipo | Corregir los bloqueantes; abrir Ciclo QA Interno 02 si aplica (ver `QA_INTERNAL_ISSUES_TRACKING.md` sección 10); re-evaluar criterios después de correcciones |

---

## 8. Plan mínimo de piloto controlado

Completar antes de iniciar el piloto:

- [ ] **Participantes definidos:** listado con nombre, correo, rol y fecha de autorización
- [ ] **Fechas definidas:** fecha de inicio, fecha de cierre, duración total
- [ ] **Ambiente identificado:** URL backend, URL frontend, base de datos, servidor
- [ ] **Datos permitidos:** solo ficticios o datos expresamente autorizados por cada participante
- [ ] **Casos de uso permitidos:** qué flujos pueden probar los participantes (consulta, navegación, validación funcional)
- [ ] **Canal de soporte definido:** canal activo, responsable, horario de atención
- [ ] **Horario de soporte definido:** horas y días en que el responsable técnico responde incidencias
- [ ] **Responsable técnico de guardia:** persona identificada con contacto directo durante el piloto
- [ ] **Mecanismo de reporte de incidencias:** formato de reporte según `QA_INTERNAL_ISSUES_TRACKING.md` sección 5
- [ ] **Criterio de suspensión definido y comunicado:** los participantes saben bajo qué condiciones se suspende el piloto (ver sección 12)
- [ ] **Acta de cierre del piloto preparada:** plantilla lista para diligenciar al finalizar

---

## 9. Controles de seguridad mínimos

Verificar antes de iniciar el piloto y mantener activos durante toda su duración:

- [ ] JWT configurado: `Jwt__Key` ≥ 32 caracteres, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__ExpirationHours` definidos
- [ ] CORS restringido: `Cors__AllowedOrigins__0` apunta únicamente a la URL del frontend del piloto
- [ ] URLs del piloto diferenciadas de producción (si existiera): nunca reutilizar dominios productivos para pilotos
- [ ] Sin secretos en el repositorio: ningún token, contraseña, clave ni cadena de conexión commiteada
- [ ] Tokens no compartidos en documentos, capturas ni canales públicos
- [ ] Accesos con fecha de expiración obligatoria: ningún usuario activo sin fecha límite
- [ ] Usuarios no autorizados bloqueados: solo los participantes de la tabla de habilitación pueden acceder
- [ ] Capturas de pantalla revisadas antes de compartir: sin tokens, contraseñas ni datos personales visibles
- [ ] Logs del servidor sin información sensible en texto claro (si se tienen logs centralizados)

---

## 10. Controles financieros mínimos

Verificar antes de iniciar el piloto y durante toda su duración:

- [ ] **No hay dinero real** en ninguna operación del piloto
- [ ] Ledger balanceado: todas las operaciones tienen débitos = créditos (verificar con `GET /api/admin/ledger-transacciones`)
- [ ] Cuentas contables identificadas y documentadas: `110101`, `210101`, `210201`, `210202`, `210203`
- [ ] Retiros simulados claramente marcados como QA/piloto en la BD
- [ ] QR de prueba claramente identificado: `QR-DEMO-XPAY-QA-001` u otro código con prefijo piloto
- [ ] Saldos QA/piloto **no interpretados** como saldos financieros reales por ningún participante
- [ ] Sin conexión bancaria real: ningún endpoint del sistema apunta a APIs bancarias externas
- [ ] Sin conciliación real: no existe proceso de reconciliación contra extractos bancarios reales
- [ ] Sin obligación comercial real: ningún participante del piloto tiene expectativa de recibir o pagar dinero real

---

## 11. Soporte y respuesta a incidentes

| Campo | Valor |
|-------|-------|
| **Canal de soporte** | _pendiente de definir_ (email, Slack, WhatsApp, etc.) |
| **Horario de atención** | _pendiente de definir_ |
| **Responsable principal** | _pendiente de definir_ |
| **Responsable de backup** | _pendiente de definir_ |

**Severidades y tiempos internos sugeridos durante piloto:**

| Severidad | Tiempo de respuesta sugerido | Acción inmediata |
|-----------|------------------------------|-----------------|
| Crítica | El mismo día, preferiblemente en horas | Suspender el piloto si aplica (ver sección 12); notificar al responsable técnico de inmediato |
| Alta | 1 día hábil | Notificar al responsable técnico; evaluar si el piloto puede continuar con advertencia |
| Media | 3 días hábiles | Registrar en `QA_INTERNAL_ISSUES_TRACKING.md`; continuar piloto |
| Baja | Cuando haya capacidad | Registrar; no impacta el piloto |

> Usar el formato de registro de `docs/QA_INTERNAL_ISSUES_TRACKING.md` sección 5 para todas las incidencias del piloto.

**Cuándo suspender el piloto:** ver sección 12.

---

## 12. Criterios de suspensión del piloto

Suspender el piloto de inmediato y notificar a todos los participantes si se cumple cualquiera de estas condiciones:

- **Bug crítico** confirmado con impacto en el flujo principal (financiero, login, sesión).
- **Error financiero**: saldo incorrecto, ledger desbalanceado, retiro aplicado a cuenta incorrecta.
- **Endpoint protegido expuesto**: cualquier endpoint retorna datos sin token válido.
- **Token filtrado**: un token JWT fue compartido públicamente o encontrado fuera del ambiente seguro.
- **Dato real ingresado**: se detectó información personal, bancaria o comercial real en la base de datos del piloto.
- **Ambiente confundido con producción**: algún participante creyó estar operando el sistema productivo real.
- **Falla repetida de login**: el sistema no permite autenticar usuarios con credenciales correctas durante más de 30 minutos.
- **Dashboard no disponible**: el dashboard no carga por más de 1 hora sin causa identificada.
- **Operación principal falla sistemáticamente**: retiros, ventas QR, wallets o consultas de ledger no responden correctamente de forma repetida.

**Procedimiento de suspensión:**

1. Responsable técnico notifica a todos los participantes que el piloto está suspendido temporalmente.
2. Se registra la incidencia como Crítica en `QA_INTERNAL_ISSUES_TRACKING.md`.
3. Se comunica la causa y el tiempo estimado de resolución.
4. El piloto se reanuda solo después de que la incidencia sea `Cerrada` con reprueba aprobada.
5. Si la suspensión es por incidencia no resuelta en 48 horas, el piloto se cancela formalmente y se abre Ciclo QA Interno 02.

---

## 13. Acta de decisión

Diligenciar al tomar la decisión de avanzar o no avanzar al piloto controlado:

```
XPAY MVP — Acta de Decisión de Salida QA Interno
=================================================

Fecha:                         ___________________________
Commit evaluado:               ___________________________
Ambiente identificado:         ___________________________
URL backend piloto:            ___________________________
URL frontend piloto:           ___________________________

Resultado Ciclo QA Interno 01: [ ] Aprobado  [ ] Aprobado con observaciones  [ ] No aplica
Incidencias críticas abiertas: ___   ← debe ser 0
Incidencias altas abiertas:    ___
Incidencias medias diferidas:  ___
Incidencias bajas diferidas:   ___

Criterios obligatorios:        ___ / 12 cumplidos
Criterios bloqueantes activos: ___   ← debe ser 0

Riesgos aceptados:
  _______________________________________________________________
  _______________________________________________________________

Decisión:
  [ ] Avanzar a piloto controlado
  [ ] Avanzar con restricciones:
      ___________________________________________________________
      ___________________________________________________________
  [ ] No avanzar. Motivo:
      ___________________________________________________________

Restricciones del piloto (si aplica):
  _______________________________________________________________
  _______________________________________________________________

Responsable técnico:           ___________________________
Responsable negocio:           ___________________________

Firma Responsable técnico:     _________________   Fecha: __________
Firma Responsable negocio:     _________________   Fecha: __________
```

---

## 14. Próximos pasos según decisión

| Decisión | Condición | Próximo paso |
|----------|-----------|--------------|
| **Avanzar a piloto controlado** | 12/12 criterios cumplidos, 0 bloqueantes | Completar el plan de piloto (sección 8). Habilitar participantes usando `QA_INTERNAL_USERS_ONBOARDING.md`. Activar canal de soporte. Iniciar con fecha definida. Mantener `QA_INTERNAL_ISSUES_TRACKING.md` activo durante el piloto. |
| **Avanzar con restricciones** | Criterios mínimos críticos cumplidos, restricciones documentadas | Comunicar las restricciones a los participantes antes del inicio. Definir fecha de revisión para los criterios pendientes. Piloto con alcance reducido hasta resolver las restricciones. |
| **No avanzar** | Al menos un criterio bloqueante activo | Registrar el bloqueante en `QA_INTERNAL_ISSUES_TRACKING.md`. Abrir Ciclo QA Interno 02 si aplica. Corregir y volver a evaluar criterios de salida con este mismo documento. |

---

## 15. Documentos relacionados

| Documento | Rol en la decisión de salida |
|-----------|------------------------------|
| [`docs/QA_INTERNAL_CYCLE_01.md`](QA_INTERNAL_CYCLE_01.md) | Prerequisito: el ciclo QA formal cuya acta de cierre es evidencia para el criterio 3 |
| [`docs/QA_INTERNAL_USERS_ONBOARDING.md`](QA_INTERNAL_USERS_ONBOARDING.md) | Guía para habilitar participantes del piloto controlado |
| [`docs/QA_INTERNAL_ISSUES_TRACKING.md`](QA_INTERNAL_ISSUES_TRACKING.md) | Fuente de verdad sobre incidencias abiertas; entrada directa a la evaluación de criterios 4 y 5 |
| [`docs/QA_MASTER_E2E_CHECKLIST.md`](QA_MASTER_E2E_CHECKLIST.md) | Smoke test (Fase 6) que se verifica en el criterio 7; señales de bloqueo |
| [`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md) | Declaración formal de la versión: alcance, riesgos conocidos y criterios de aprobación |
| [`README.md`](../README.md) | Descripción del proyecto: endpoints, configuración y ecosistema QA completo |
| [`docs/PILOT_CONTROLLED_OPERATING_PLAN.md`](PILOT_CONTROLLED_OPERATING_PLAN.md) | Siguiente paso si la decisión es avanzar: plan operativo completo del piloto controlado |

---

*Este documento cubre el MVP XPAY — Fases 1 a 33. Actualizar si cambian los criterios de salida, el alcance del piloto o las condiciones de seguridad y financieras requeridas.*
