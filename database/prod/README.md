# Paquete de migraciones XPAY PROD

Paquete reproducible para crear el esquema de la base XPAY PROD (vacía) a partir de los scripts `database/001`–`049`, **sin datos QA/demo y sin valores comerciales no aprobados**.

> Nada de este directorio se ha ejecutado contra ninguna base de datos. Ninguna ejecución está autorizada: todas las entradas del manifiesto tienen `autorizacion_ejecucion = NO_OTORGADA`.

## Contenido

| Ruta | Qué es |
|---|---|
| `especificacion_paquete.json` | Fuente de verdad: orden, origen, rangos de líneas conservados, dependencias, estado y exclusiones. |
| `MANIFIESTO_PROD.json` | Generado: añade SHA-256 de origen y de archivo, y el commit de origen. No editar a mano. |
| `variantes/*.prod.sql` | Variantes PROD de los scripts clase B (y de 009 y 045a): copia exacta de los rangos conservados del original, con encabezado ASCII. |
| `pendientes/*.pendiente.sql` | Bloques de datos comerciales separados (016b, 021b, 045b). **No ejecutables** hasta aprobación de negocio. |
| `tools/construir_paquete.py` | Regenera variantes, pendientes y manifiesto desde la especificación. |
| `tools/validar_paquete.py` | Validador local (no ejecuta SQL, no se conecta a nada). |
| `tools/ejecutar_migraciones.py` | Ejecutor controlado por bloques. Por defecto **solo muestra el plan**. |
| `verificacion_post_migracion.sql` | Comprobaciones de solo lectura tras migrar. |
| `PROCEDIMIENTO_EJECUCION_PROD.md` | Procedimiento de ejecución controlada. |

Regenerar y validar (desde la raíz del repositorio):

```
python3 database/prod/tools/construir_paquete.py
python3 database/prod/tools/validar_paquete.py
```

El validador no sustituye una prueba real de SQL: no compila ni ejecuta T-SQL.

## Clasificación aplicada

- **Originales sin cambios (A):** 001, 002, 005, 006, 007, 010, 022–033, 035–044, 046–049.
- **Variantes PROD (B):** 003, 004, 011, 012, 013, 014, 015, 016, 017, 019, 021. Solo se quitaron bloques demo/QA y operaciones con IDs de prueba (ver `eliminado` en la especificación).
- **Variante PROD de 009 (D, resuelto):** sin el encabezado "NO EJECUTAR EN PRODUCCIÓN", sin la consulta de usuarios QA y sin mensajes QA. Estado `LISTO` por autorización del usuario; 034 pasa también a `LISTO`.
- **Excluidos (C):** 008, 018, 020, `scripts/ci/ci_admin_xpay_fixture.sql`, `scripts/ci/ci_comercio_caja_fixture.sql`.
- **Bloqueados:** 045a (autorización); 016b, 021b y 045b (decisión de negocio).

### Desviación respecto a "conservar catálogos": 016 y 021

Los bloques de 016 (tasas de liquidación anticipada 0,5 % diario) y de 021 (tasas, plazos y montos de utilización, gastos de cobranza y política de crédito inicial) **no son demo**, pero son **valores comerciales**. Para no fijar políticas comerciales en PROD sin aprobación, se separaron sin modificarlos en `pendientes/016b` y `pendientes/021b`. Consecuencia: hasta su aprobación, las funciones de cartera (`AVANCE_WALLET`, `COMPRA_COMERCIO`, solicitud de cupo) y la liquidación anticipada fallarán al usarse. No afecta al arranque ni a la autenticación.

Además, 016b inserta con `created_by_usuario = 1`, que en PROD apuntaría al primer usuario que se cree.

## Cuentas contables y roles exigidos por el backend

Comprobado por inspección local del código (`backend/Xpay.Api`) y de los scripts que quedan en el paquete:

| Código | Lo crea (en el paquete) |
|---|---|
| 110101 | 006 |
| 110102 | 010 |
| 110103 | 044 |
| 110202, 130104, 210101 | 002 |
| 130105 | 022 |
| 130106 | 024 |
| 130107 | 025 |
| 210201 | variante 003 (bloque de catálogo conservado) |
| 210202 | variante 004 (bloque de catálogo conservado) |
| 210203 | 005 |
| 210204 | 010 |
| 230103, 310103 | variante 013 (bloque de catálogo conservado) |
| 240802, 410201, 410202 | variante 016 (y 240802 también en variante 019) |
| 240803, 410301, 410302, 410303 | 023 |

Los 22 códigos que usa el backend existen en el paquete. **No falta ninguno.**

Roles de la tabla `roles` usados por el backend: `USUARIO_FINAL`, `SUPERUSUARIO`, `CARTERA_XPAY`, `COMERCIAL_XPAY`, `GERENTE_XPAY` (001) y `ADMIN_XPAY`, `OPERADOR_XPAY`, `COMERCIO` (007). **No falta ninguno.** `ADMIN_COMERCIO`, `ADMIN_SEDE_COMERCIO`, `CAJERO`, `ADMIN_EMPRESA` y `OPERADOR_EMPRESA` son valores de columna validados por CHECK en 017 y 012 (estructura conservada), no filas de `roles`.

## Script 009 (incluido como variante PROD, estado LISTO)

- Su encabezado dice "USO EXCLUSIVO: QA / DESARROLLO — NO EJECUTAR EN PRODUCCIÓN" y "Prerrequisito: scripts 001 a 008".
- Su contenido es solo estructura con guardas: columnas `usuarios.estado_kyc_actual` (NOT NULL, default `NO_INICIADO`) y `usuarios.fecha_kyc_actualizacion`, y la tabla `kyc_verificaciones`. No inserta ni modifica datos. Su verificación lee (SELECT) usuarios `qa.*`, que en PROD no devuelve filas.
- **Justificación técnica para incluirlo:** el backend mapea esas dos columnas en la entidad `Usuario` (`XpayDbContext.cs`) y la entidad `KycVerificacion`. Sin 009, **cualquier consulta EF sobre `usuarios` fallaría** (columnas inexistentes), lo que rompe inicio de sesión, registro y recuperación. 034 también depende de 009. Solo depende de 001, no de 008.
- **Resuelto:** se usa la variante `variantes/009_kyc_verificaciones.prod.sql` (rangos 26-28, 36-135, 137-142, 155-166 del original). Las sentencias de estructura son idénticas al original; se eliminaron el encabezado QA, los mensajes QA y el SELECT de usuarios `qa.*`.
- Sigue siendo **imprescindible** para la primera prueba técnica: sin 009 la aplicación no puede autenticar. Su ejecución, como la del resto, requiere autorización expresa.

## Script 045 (pendiente de decisión)

- **045a (variante, estructura):** `monto_solicitado` pasa a NULL y su CHECK a `NULL o > 0`, en una transacción. Bloqueado por autorización.
- **045b (pendiente, datos):** alinea la fila ACTIVA de `cartera_politicas_credito` a 200000–700000 y 18–65. Requiere exactamente una política ACTIVA (la de 021b). Bloqueado por decisión de negocio.
- **Hallazgo:** según el propio código (`CarteraOrdinariaService.cs`, comentario XPAY-676B), los valores de esa fila **no gobiernan el motor**: el motor usa `CarteraPoliticaAplicada.Vigente()` (código). La fila solo aporta `id_politica_aplicada` para trazabilidad. Por tanto, hoy **no existe** administración de políticas variables desde la base: cambiar la política exige cambiar código. Conservar esa posibilidad implica, como mínimo, mantener la tabla y una fila ACTIVA; hacerla efectiva requeriría un cambio de backend (fuera de este paquete).

## Integridad afectada por 013, 014 y 019

No se introdujo ningún cambio estructural nuevo. Efectos documentados, pendientes de aprobación:

1. **013 → 014 (periodicidad):** 013 deja `ck_lec_periodicidad` y `ck_le_periodicidad` en (MENSUAL, QUINCENAL, DECADAL) y 014 los amplía con SEMANAL. Ejecutados una vez y en orden, el resultado final es correcto. **Riesgo:** reejecutar 013 después de 014 elimina el CHECK y lo intenta recrear sin SEMANAL; si hay filas SEMANAL, falla y la tabla queda **sin CHECK de periodicidad**. Mitigación de procedimiento: no reejecutar 013 (el ejecutor no repite scripts). Corrección posible (no aplicada): hacer la recreación condicional.
2. **014 (dia_pago_4):** añade `dia_pago_4` a convenio y empleados **sin** CHECK de rango (1–31), a diferencia de `dia_pago_1..3`. Corrección posible (no aplicada): `CHECK (dia_pago_4 IS NULL OR dia_pago_4 BETWEEN 1 AND 31)`.
3. **014 (cortes):** amplía `ck_lecp_corte` a 1–4 con guarda correcta. Sin riesgo.
4. **019 (`uq_xpla_dias`):** elimina la restricción única (dias_faltantes, estado) para permitir parámetros por aliado, y **no crea sustituta**. Efecto: pueden coexistir varios parámetros ACTIVOS para el mismo aliado (o globales) y mismo día; el código elegiría uno de ellos. Corrección posible (no aplicada): índice único filtrado sobre (id_comercio_aliado, dias_faltantes) `WHERE estado = 'ACTIVO'`, teniendo en cuenta que en SQL Server los NULL de un índice único cuentan como iguales (un solo global por día). Requiere revisar el código que consulta la tabla antes de aprobarla.
5. **019 (`id_comercio_aliado`):** columna sin FK a `comercios_aliados`.

## Decisiones pendientes (resumen)

1. ~~Autorizar 009~~ **Resuelto:** variante 009 y 034 en estado LISTO (sin autorización de ejecución todavía).
2. Autorizar 045a (estructura).
3. Aprobar o sustituir los valores de 016b, 021b y 045b.
4. Aprobar o descartar las correcciones de integridad propuestas (013, 014, 019).
5. Confirmar a qué cuenta bancaria real corresponde 110102 "Banco Coopcentral XPAY" en PROD, y la configuración de `PASSPORT_ACCOUNT_ID` (el arranque inserta en `cuentas_operativas` si está definida).
6. Primer administrador de PROD: procedimiento controlado aparte (ningún script lo crea).
7. Vía de acceso para ejecutar (ver procedimiento).
8. Autorizar el commit de este paquete para fijar su origen.
