# XPAY MVP — Plantilla de Ejecución QA

**Versión:** 1.0  
**Fecha:** 2026-06-17  
**Estado:** Plantilla activa  

---

## 1. Objetivo

Este documento se usa para registrar la **ejecución real** de los casos definidos en [`docs/QA_MANUAL_TESTING.md`](QA_MANUAL_TESTING.md).

Mientras el manual QA describe **qué probar** y **cuál es el resultado esperado**, esta plantilla registra **qué ocurrió realmente**, quién lo ejecutó, qué evidencia se capturó, qué bugs se encontraron y cuál fue la decisión final.

Cada ciclo de pruebas QA debe generar una copia completada de este documento como evidencia formal de la ejecución.

---

## 2. Datos de la ejecución

Completar al inicio de cada ciclo de pruebas:

| Campo | Valor |
|-------|-------|
| **ID ejecución** | QA-RUN-___ (ej: QA-RUN-001) |
| **Fecha inicio** | YYYY-MM-DD |
| **Fecha cierre** | YYYY-MM-DD |
| **Ambiente** | Local / QA Azure / otro |
| **URL backend** | http://localhost:5000 o URL Azure |
| **URL frontend** | http://localhost:5173 o URL Azure |
| **Responsable QA** | Nombre completo |
| **Versión / commit evaluado** | Hash commit git (ej: 98d8ba9) |
| **Backend Validation run** | ID run GitHub Actions |
| **Frontend Build run** | ID run GitHub Actions |
| **Navegador** | Chrome 130 / Firefox 132 / otro |
| **Observaciones generales** | Condiciones especiales, limitaciones, contexto |

---

## 3. Resumen ejecutivo

Completar al cierre de la ejecución:

| Métrica | Valor |
|---------|-------|
| **Total casos** | 35 |
| **Casos ejecutados** | ___ |
| **Casos aprobados (✅)** | ___ |
| **Casos fallidos (❌)** | ___ |
| **Casos bloqueados (⚠️)** | ___ |
| **Casos no ejecutados (⬜)** | ___ |
| **Casos en reprueba (🔁)** | ___ |
| **% avance** | (ejecutados / 35) × 100 = __% |
| **% aprobación** | (aprobados / ejecutados) × 100 = __% |
| **Críticos aprobados** | ___ / 8 |
| **Altos aprobados** | ___ / 21 |
| **Medios aprobados** | ___ / 6 |
| **Bugs abiertos** | ___ |
| **Bugs críticos abiertos** | ___ |

### Decisión final

```
[ ] Aprobado
[ ] Aprobado con observaciones
[ ] No aprobado
```

**Justificación:**

```
[Completar]
```

---

## 4. Matriz de ejecución de casos

### Estados permitidos

| Símbolo | Estado |
|---------|--------|
| ⬜ | No ejecutado |
| ✅ | Aprobado |
| ❌ | Fallido |
| ⚠️ | Bloqueado |
| 🔁 | Reprueba pendiente |

### Tabla de ejecución

| ID | Módulo | Severidad | Caso | Responsable | Fecha ejecución | Estado | Evidencia | Bug relacionado | Observaciones |
|----|--------|-----------|------|-------------|-----------------|--------|-----------|-----------------|---------------|
| QA-01 | Backend público | Medio | Health check `/health` → HTTP 200 | | | ⬜ | | | |
| QA-02 | Backend público | Medio | Versión API `/api/version` → nombre y versión | | | ⬜ | | | |
| QA-03 | Backend público | Medio | Swagger UI carga con botón Authorize | | | ⬜ | | | |
| QA-04 | Autenticación | **Crítico** | Login exitoso → redirige a `/dashboard` | | | ⬜ | | | |
| QA-05 | Autenticación | Medio | Login fallido → error visible en formulario | | | ⬜ | | | |
| QA-06 | Autenticación | **Crítico** | Endpoint protegido sin token → HTTP 401 | | | ⬜ | | | |
| QA-07 | Autenticación | Alto | Cierre de sesión → redirige a `/login` sin mensaje de expiración | | | ⬜ | | | |
| QA-08 | Dashboard | **Crítico** | Dashboard carga sin errores con sesión activa | | | ⬜ | | | |
| QA-09 | Dashboard | Alto | 11 métricas visibles con valores numéricos/monetarios | | | ⬜ | | | |
| QA-10 | Dashboard | Alto | QuickCard Wallets navega a `/wallets/listado` | | | ⬜ | | | |
| QA-11 | Dashboard | Alto | QuickCard Retiros navega a `/retiros/listado` | | | ⬜ | | | |
| QA-12 | Dashboard | Medio | Botón ↺ Reintentar aparece en sección con error | | | ⬜ | | | |
| QA-13 | Wallets | Alto | Listado wallets carga sin filtros con paginación | | | ⬜ | | | |
| QA-14 | Wallets | Alto | Filtro Estado = ACTIVA muestra solo wallets ACTIVAS | | | ⬜ | | | |
| QA-15 | Wallets | Alto | Filtro Tipo = USUARIO muestra solo wallets de tipo USUARIO | | | ⬜ | | | |
| QA-16 | Wallets | **Crítico** | Estado de cuenta wallet muestra saldo y movimientos | | | ⬜ | | | |
| QA-17 | Comercios | Alto | Listado comercios carga con nombre, NIT, estado, saldo | | | ⬜ | | | |
| QA-18 | Comercios | Alto | Filtro texto muestra solo comercios que coinciden | | | ⬜ | | | |
| QA-19 | Comercios | Alto | Navegar a resumen de comercio desde listado | | | ⬜ | | | |
| QA-20 | Comercios | Alto | Resumen comercio muestra ventas, retiros y saldo coherentes | | | ⬜ | | | |
| QA-21 | Retiros | Alto | Listado retiros carga con ID, valor, estado, fecha | | | ⬜ | | | |
| QA-22 | Retiros | Alto | Filtro Estado = PENDIENTE muestra solo PENDIENTES | | | ⬜ | | | |
| QA-23 | Retiros | Alto | Buscar retiro por ID muestra su detalle | | | ⬜ | | | |
| QA-24 | Retiros | **Crítico** | Confirmar pago de retiro PENDIENTE → estado PAGADO | | | ⬜ | | | |
| QA-25 | Retiros | **Crítico** | Rechazar retiro PENDIENTE con motivo → estado RECHAZADO | | | ⬜ | | | |
| QA-26 | Retiros | Alto | Retiro PAGADO/RECHAZADO no muestra botones de acción | | | ⬜ | | | |
| QA-27 | Ventas QR | Alto | Listado ventas QR carga con columnas completas | | | ⬜ | | | |
| QA-28 | Ventas QR | Alto | Filtro Estado = LIQUIDADA muestra solo LIQUIDADAS | | | ⬜ | | | |
| QA-29 | Ventas QR | Alto | Ver comercio navega a `/comercios/:id` correcto | | | ⬜ | | | |
| QA-30 | Ledger | Alto | Listado ledger carga con tipo, referencia, valor, fecha | | | ⬜ | | | |
| QA-31 | Ledger | Alto | Filtro Tipo = PAGO_QR muestra solo PAGO_QR | | | ⬜ | | | |
| QA-32 | Ledger | Alto | Ver detalle navega a `/ledger/:id` con detalle completo | | | ⬜ | | | |
| QA-33 | Ledger | **Crítico** | Débitos y créditos del ledger son consistentes (ledger balanceado) | | | ⬜ | | | |
| QA-34 | Manejo de errores | **Crítico** | Sesión expirada → redirige a `/login` con banner amarillo | | | ⬜ | | | |
| QA-35 | Manejo de errores | Medio | Backend no disponible → mensaje claro de conexión | | | ⬜ | | | |

**Distribución de severidades:**
- 🔴 Crítico: QA-04, QA-06, QA-08, QA-16, QA-24, QA-25, QA-33, QA-34 **(8 casos)**
- 🟡 Alto: QA-07, QA-09 a QA-11, QA-13 a QA-15, QA-17 a QA-23, QA-26 a QA-32 **(21 casos)**
- 🔵 Medio: QA-01 a QA-03, QA-05, QA-12, QA-35 **(6 casos)**

---

## 5. Registro de evidencias

Agregar una fila por cada evidencia capturada durante la ejecución:

| ID evidencia | Caso QA | Tipo evidencia | Archivo / URL | Descripción | Fecha/hora | Responsable | Observaciones |
|-------------|---------|---------------|---------------|-------------|------------|-------------|---------------|
| EV-001 | QA-___ | Screenshot | QA-XX_descripcion.png | | YYYY-MM-DD HH:MM | | |
| EV-002 | QA-___ | | | | | | |
| EV-003 | QA-___ | | | | | | |

### Tipos de evidencia sugeridos

| Tipo | Cuándo usarlo |
|------|---------------|
| **Screenshot** | Estado de la pantalla antes/después de una acción |
| **Video corto** | Flujos con múltiples pasos (confirmar retiro, sesión expirada) |
| **Log navegador** | Errores de consola, mensajes de red en DevTools |
| **Respuesta API** | Cuerpo y status HTTP desde Postman/Bruno o DevTools → Network |
| **Registro GitHub Actions** | URL del run para Backend Validation y Frontend Build |
| **Captura base de datos** | Antes/después de confirmar retiro o rechazar; verificación de saldo |

> **Convención de nombres:** `{ID-caso}_{acción}_{antes|despues}.{ext}`  
> Ejemplos: `QA-24_confirmar_pago_antes.png`, `QA-24_confirmar_pago_despues.png`, `QA-33_ledger_suma.png`

---

## 6. Registro de bugs

Agregar una fila por cada bug encontrado durante la ejecución:

| Bug ID | Caso QA | Título | Severidad | Estado | Responsable | Fecha reporte | Fecha solución | Commit solución | Evidencia | Observaciones |
|--------|---------|--------|-----------|--------|-------------|---------------|----------------|-----------------|-----------|---------------|
| BUG-001 | QA-___ | | | Abierto | | YYYY-MM-DD | | | EV-___ | |
| BUG-002 | QA-___ | | | | | | | | | |

### Estados de bug

| Estado | Descripción |
|--------|-------------|
| **Abierto** | Reportado, aún no analizado |
| **En análisis** | El equipo técnico lo está revisando |
| **Corregido** | Fix implementado, pendiente reprueba |
| **Reprobando** | Se está volviendo a ejecutar el caso QA asociado |
| **Cerrado** | Fix verificado por QA |
| **No reproducible** | No se pudo volver a reproducir; documentar condiciones |
| **Diferido** | Aceptado como deuda técnica para versión futura |

> Para el detalle de cada bug, usar la plantilla del sección 8 de [`docs/QA_MANUAL_TESTING.md`](QA_MANUAL_TESTING.md).

---

## 7. Criterios para decisión final

### Aprobado ✅

Se cumplen **todas** las condiciones:

- 100% de los **8 casos Críticos** con estado ✅ Aprobado.
- ≥ 90% de los **21 casos Altos** con estado ✅ Aprobado (mínimo 19 de 21).
- **Cero bugs críticos** abiertos.
- **Cero errores financieros**: ledger balanceado, saldos de wallets coherentes con movimientos.
- **Cero endpoints protegidos** accesibles sin token (QA-06 debe ser ✅).

### Aprobado con observaciones ⚠️

Se cumplen **todas** estas condiciones:

- 100% de los **8 casos Críticos** con estado ✅ Aprobado.
- Existen bugs de severidad **Media o Baja** que no bloquean operación.
- Los bugs observados están documentados con responsable y fecha estimada de solución.
- El equipo técnico y el responsable QA acuerdan y firman el acta de cierre.

### No aprobado ❌

**Cualquiera** de estas condiciones:

| Condición de bloqueo | Caso relacionado |
|---------------------|-----------------|
| Cualquier caso Crítico con estado ❌ | QA-04, QA-06, QA-08, QA-16, QA-24, QA-25, QA-33, QA-34 |
| Error en integridad del ledger | QA-33 |
| Saldo de wallet incorrecto tras operación | QA-16 |
| Endpoint protegido accesible sin token | QA-06 |
| Login no funciona | QA-04 |
| Dashboard no carga | QA-08 |
| Confirmar/rechazar retiro no cambia estado | QA-24, QA-25 |
| Bug crítico abierto sin commit de solución | — |

---

## 8. Acta de cierre QA

Completar al finalizar la ejecución y antes de comunicar la decisión:

```
═══════════════════════════════════════════════════════════════
           ACTA DE CIERRE QA — XPAY MVP
═══════════════════════════════════════════════════════════════

ID Ejecución:         QA-RUN-___
Fecha cierre:         YYYY-MM-DD
Commit evaluado:      [hash]
Ambiente:             [Local / QA Azure]

────────────────────────────────────────────────────────────────
RESUMEN DE RESULTADOS
────────────────────────────────────────────────────────────────
Total casos:          35
Aprobados:            ___  ( __% )
Fallidos:             ___
Bloqueados:           ___
No ejecutados:        ___

Críticos aprobados:   ___ / 8
Altos aprobados:      ___ / 21
Medios aprobados:     ___ / 6

Bugs abiertos:        ___
  Críticos:           ___
  Altos:              ___
  Medios:             ___

────────────────────────────────────────────────────────────────
DECISIÓN FINAL
────────────────────────────────────────────────────────────────
[ ] Aprobado
[ ] Aprobado con observaciones
[ ] No aprobado

────────────────────────────────────────────────────────────────
OBSERVACIONES
────────────────────────────────────────────────────────────────
[Detalles relevantes, limitaciones conocidas, acuerdos]

────────────────────────────────────────────────────────────────
BUGS PENDIENTES ACEPTADOS (si aplica)
────────────────────────────────────────────────────────────────
Bug ID | Título | Severidad | Fecha estimada solución
-------|--------|-----------|------------------------
       |        |           |

────────────────────────────────────────────────────────────────
FIRMAS / APROBACIÓN
────────────────────────────────────────────────────────────────
Responsable QA:       _______________________   Fecha: __________
Responsable técnico:  _______________________   Fecha: __________

═══════════════════════════════════════════════════════════════
```

---

## 9. Instrucciones de uso

Seguir estos pasos en orden para cada ciclo de pruebas QA:

**Paso 1 — Confirmar que CI está verde**  
Ir a GitHub Actions en el repositorio. Verificar que los últimos runs de `Backend Validation` y `Frontend Build` muestran `completed success` para el commit a evaluar.

**Paso 2 — Copiar el documento para la ejecución**  
Opcionalmente, hacer una copia de este archivo con el nombre `QA_EXECUTION_RUN_001.md` (o el número de ejecución correspondiente) para preservar el historial de cada ciclo sin sobreescribir la plantilla.

**Paso 3 — Completar los datos de ejecución**  
Llenar la tabla de la sección 2: fecha, ambiente, URL, responsable, commit, navegador.

**Paso 4 — Ejecutar casos en orden QA-01 a QA-35**  
Seguir los pasos descritos en [`docs/QA_MANUAL_TESTING.md`](QA_MANUAL_TESTING.md) para cada caso. Actualizar el estado en la matriz de la sección 4.

**Paso 5 — Guardar evidencias**  
Capturar screenshots, videos y logs para cada caso ejecutado. Registrar cada evidencia en la tabla de la sección 5 con su ID, tipo, archivo y descripción.

**Paso 6 — Registrar bugs**  
Cada vez que un caso falla, crear un registro en la tabla de la sección 6. Usar la plantilla de bug de `QA_MANUAL_TESTING.md` para el detalle completo.

**Paso 7 — Reprobar casos corregidos**  
Cuando el equipo técnico entregue un fix con commit, volver a ejecutar los casos afectados. Actualizar el estado de `❌ Fallido` o `🔁 Reprueba pendiente` a `✅ Aprobado` si el fix es satisfactorio.

**Paso 8 — Completar el resumen ejecutivo**  
Con todos los casos ejecutados, calcular los totales y porcentajes en la sección 3.

**Paso 9 — Emitir decisión final**  
Aplicar los criterios de la sección 7 para determinar si el sistema está Aprobado, Aprobado con observaciones o No aprobado.

**Paso 10 — Firmar el acta de cierre**  
Completar y firmar el acta de la sección 8. Este documento es el registro formal del ciclo de pruebas.

---

## 10. Relación con otros documentos

| Documento | Propósito |
|-----------|-----------|
| [`docs/QA_MANUAL_TESTING.md`](QA_MANUAL_TESTING.md) | Define los 35 casos de prueba, prerrequisitos, evidencias esperadas y criterios |
| [`docs/QA_DEPLOYMENT.md`](QA_DEPLOYMENT.md) | Guía de despliegue en Azure App Service para preparar el ambiente QA |
| [`README.md`](../README.md) | Descripción del backend: endpoints, configuración, estado del MVP |
| [`frontend/xpay-admin/README.md`](../frontend/xpay-admin/README.md) | Configuración del frontend: instalación, rutas, autenticación, errores |

---

*Esta plantilla cubre el MVP XPAY — Fases 1 a 19. Actualizar la matriz de casos si se agregan módulos en fases posteriores.*
