# Procedimiento de ejecución controlada — migraciones XPAY PROD

> Documento de preparación. **No se ha ejecutado contra PROD.**
> Cada paso que escribe en Azure o en la base requiere autorización
> expresa e individual. B1 (001–009) se ha probado solo en un SQL
> Server 2022 local (sección 6.1); nada está validado en Azure SQL.
> **Estado: no ejecutable** hasta completar T6B–T6E (sección 6.12).

## 0. Condiciones previas (todas obligatorias)

1. Paquete commiteado (autorización aparte) y árbol limpio: `git status --short database/` sin cambios.
2. Validador local en OK: `python3 database/prod/tools/validar_paquete.py` → `RESULTADO: OK`.
3. Antes de B2–B5 (no aplica a B1): decisiones tomadas sobre 045a y
   los datos pendientes 016b, 021b y 045b (README, "Decisiones
   pendientes"). 009 y 034 ya están en el conjunto ejecutable.
4. Base todavía vacía: comprobación de solo lectura antes del primer bloque (ver 3.1).
5. Hora UTC de inicio registrada: es el punto de restauración (PITR 35 días configurado en C6).
6. go-sqlcmd aislado verificado (`~/xpay-tools/go-sqlcmd-1.10.0/sqlcmd`, SHA conocido).
7. Directorio de registro: `~/xpay-tools/xpay-prod-migraciones/logs/` (700), fuera del repositorio.
8. Definir `XPAY_PROD_SQL_SERVIDOR`, `XPAY_PROD_SQL_BASE` y `XPAY_PROD_SQL_ADMIN` desde un archivo local fuera del repositorio (por ejemplo `~/xpay-tools/xpay-prod-migraciones/entorno.local`, permisos 600). Los valores reales nunca se guardan en el repositorio.
9. Ningún proceso de aplicación conectado a la base durante la
   ejecución ni durante la recuperación (ver 6.2). La aplicación no
   tiene en el código ningún interruptor para no conectarse: el
   control es externo (App Service detenido o sin desplegar, o
   permiso de conexión denegado, según la decisión de 6.12).

## 1. Vía de acceso a SQL (decisión pendiente)

El servidor tiene el acceso público deshabilitado. Por orden de preferencia:

| Opción | Descripción | Ventajas | Inconvenientes |
|---|---|---|---|
| **P1. Equipo dentro de la VNet** (recomendada para evaluar) | VM Linux temporal sin IP pública en una subred nueva de la VNet de PROD, accedida con Azure Bastion. La zona DNS privada ya está enlazada a la VNet, así que el nombre del servidor resuelve a la IP privada del endpoint privado. | Sin exposición pública de SQL. Sin límite estricto de tiempo. | Crea infraestructura nueva (VM, Bastion, subred), con coste y autorización. Sin navegador: exige `ActiveDirectoryDeviceCode` o `az login --use-device-code` + `ActiveDirectoryAzCli`. Hay que copiar el paquete a la VM y verificar sus SHA allí. |
| **P2. Ventanas públicas temporales por bloque** | Procedimiento de ventana temporal ya probado (registro interno), con un bloque de scripts por ventana. | Procedimiento probado, sin infraestructura nueva, MFA en navegador. | Exposición pública breve (IP /32, Entra-only). Varias ventanas de ≤30 min. |

Riesgo común a ambas: el ejecutor invoca go-sqlcmd una vez por script; con autenticación interactiva **podría pedir inicio de sesión/MFA en cada script**. No está verificado si go-sqlcmd reutiliza el token entre invocaciones. Se comprobará en el primer bloque; si pide MFA por script, los bloques deben ser más pequeños.
Para B1, el tratamiento del MFA y de la ventana está en 6.3 y 6.4.

### Bloques propuestos (si se usa P2)

| Bloque | Scripts (orden del manifiesto) | Nº |
|---|---|---|
| B1 | 001 → 009 | 8 |
| B2 | 010 → 019 | 9 |
| B3 | 021 → 031 (021b omitido) | 11 |
| B4 | 032 → 040 | 9 |
| B5 | 041 → 049 (045a y 045b omitidos) | 8 |
| Posteriores | 045a; 016b / 021b / 045b | según autorización |

Comando de plan (no conecta): `python3 database/prod/tools/ejecutar_migraciones.py --desde 010 --hasta 019 --omitir-bloqueados`

## 2. Ejecución de un bloque

1. Validador local en OK (el ejecutor lo repite automáticamente).
2. Plan del bloque revisado y autorizado (`--modo plan`, por defecto).
3. Ejecución: `--modo ejecutar` y escribir la frase exacta `EJECUTAR <desde>-<hasta> EN <base PROD>`.
4. El ejecutor, por cada script:
   - recalcula el SHA-256 y lo compara con el manifiesto (si difiere, se detiene sin ejecutar);
   - ejecuta `sqlcmd -S $XPAY_PROD_SQL_SERVIDOR -d $XPAY_PROD_SQL_BASE --authentication-method ActiveDirectoryInteractive -U $XPAY_PROD_SQL_ADMIN -N mandatory -b -i <archivo>`;
   - guarda la salida completa en `logs/<sello>_<id>.log` y una línea en `logs/ejecucion_<sello>_<bloque>.tsv` (inicio, fin, id, archivo, SHA, código);
   - **se detiene ante el primer código distinto de 0.**
5. Nunca se reintenta automáticamente. Ante un fallo: no reejecutar 001–007 (sin guardas) ni 013 (ver README); analizar el log y decidir.

## 3. Verificaciones

### 3.1 Antes del primer bloque (solo lectura)

Para B1, la verificación previa completa está en 6.6. Como mínimo:

```sql
SELECT COUNT(*) AS tablas_usuario FROM sys.tables WHERE is_ms_shipped = 0;  -- se espera 0
```

### 3.2 Después de cada bloque

Revisar el TSV: todos los códigos en 0. Revisar en los logs que no haya mensajes `THROW`/`Msg` inesperados.

### 3.3 Después del último bloque

Ejecutar `database/prod/verificacion_post_migracion.sql` (solo SELECT) y comparar con los resultados esperados anotados en cada consulta (R1–R11).

## 4. Respuesta ante fallos

| Situación | Acción |
|---|---|
| SHA distinto | Detenerse. No ejecutar. Investigar el cambio local. |
| Fallo en un script con guardas (009–049 salvo excepciones) | Detenerse. Analizar. Una corrección o reejecución requiere autorización. |
| Fallo a mitad de un script sin guardas (001–007) o sin transacción (043, 044, 047, 048, 049) | Estado parcial posible. Opciones a decidir: corrección puntual, o restaurar la base al punto previo (PITR) / recrearla vacía. Ambas requieren autorización. |
| Fallo de acceso o autenticación | Cerrar la vía de acceso (P2: K1→K2) y replanificar. |

Para B1 (001–009), la fila «Fallo a mitad de un script sin guardas»
queda sustituida por las secciones 6.8 y 6.9.

## 5. Lo que este procedimiento nunca hace

- No abre acceso público ni crea infraestructura por sí mismo.
- No ejecuta 008, 018, 020 ni los fixtures de CI.
- No ejecuta entradas bloqueadas, salvo que se desbloqueen en la especificación, se regenere el manifiesto y se valide.
- No crea usuarios, cupos ni movimientos financieros.
- No guarda credenciales ni tokens.

## 6. B1 (001–009): modelo secuencial aprobado

Decisión del propietario (2026-10-24): B1 se ejecuta con el ejecutor
secuencial original (commit 842c232), un sqlcmd por script y parada
ante el primer error. Un fallo a mitad deja un estado parcial que se
recupera (6.9) antes de una nueva ejecución completa en otra ventana.
Esta decisión sustituye, solo para B1, la exigencia anterior de
demostrar atomicidad. El prototipo atómico está suspendido en la rama
proto/prod-ejecutor-atomico y no forma parte de este procedimiento.

### 6.1 Qué está demostrado y qué no

Demostrado solo en local (SQL Server 2022 Developer en contenedor,
go-sqlcmd 1.10.0, ejecutor original sin cambios, pruebas T0–T4):
- B1 completo deja 24 tablas y los valores de 6.7.
- Ante un error, sqlcmd con -b se detiene en el lote que falla y el
  ejecutor no inicia los scripts siguientes. El estado parcial es:
  scripts anteriores completos más los lotes ya confirmados del script
  que falla (prueba con un fallo provocado en el lote 6 de 004).
- La limpieza de laboratorio devuelve la base a un inventario idéntico
  al inicial, conserva usuarios, permisos y esquemas, y es repetible.
- Una nueva ejecución completa da un inventario idéntico a la primera.

Estimado (T5, local con emulación, sin MFA): B1 tarda 2–3 s y la
limpieza menos de 1 s. No sirve para dimensionar la ventana en Azure.

Pendiente de validación real en Azure SQL: ver 6.11.

Las evidencias locales se conservan fuera del repositorio y no
contienen credenciales.

### 6.2 Procesos que se conectan a la base

Según el código (lectura de backend/Xpay.Api, sin ejecutarlo):
- No hay migraciones ni creación automática de esquema al arrancar.
- Al arrancar, OperationalAccountBootstrapper (Program.cs:431) solo
  escribe si existe PASSPORT_ACCOUNT_ID y si existen la tabla
  cuentas_operativas y la cuenta 110103, que crea 044 (B5). Con solo
  B1 no puede escribir, pero se conecta.
- Dos tareas en segundo plano se conectan solas: CajaVencidaScheduler
  (al arrancar y cada 15 min, Program.cs:74) y
  BrebPaymentReconciliation (al arrancar y cada 10 s por defecto,
  Program.cs:79). Con solo B1 sus consultas fallan y no escriben,
  pero abren conexiones continuamente.
- El registro público anónimo (POST /api/usuarios/registro) escribe
  en personas, usuarios, usuario_roles, wallets, wallet_saldos y
  auditoria, que son tablas de B1, dentro de una transacción que
  además necesita usuario_tokens (048). Con solo B1 probablemente no
  confirma, pero consume valores IDENTITY.
Consecuencia: durante B1 y la recuperación la aplicación no debe
poder conectarse (condición previa 9).

### 6.3 Ventana (P2) y criterio de cierre

Secuencia: A1 acceso público → A2 regla /32 → V → E → P → K1 → K2.
Cada paso con autorización y hora UTC en el registro de ventana.

- E se lanza con --limite-inicio-utc = hora de A2 + D_max − M_cierre
  (valores iniciales propuestos: D_max 30 min, M_cierre 10 min). Es un
  límite de seguridad, no una estimación de duración.
- Se crea el archivo DETENER si un inicio de sesión con MFA supera
  3 min o falla dos veces seguidas.
- K1/K2 se ejecutan cuando se cumple lo primero de: (a) el ejecutor ha
  terminado (código conocido, TSV cerrado) y P se ha completado o
  descartado; (b) se alcanzó el límite y no hay ningún sqlcmd en
  marcha (proceso comprobado); (c) cualquier fallo de acceso o de
  autenticación.
- No se cierra el acceso con un sqlcmd en marcha salvo por incidencia
  de seguridad. Si ocurre, el servidor revierte el lote en curso y el
  resultado es un estado parcial recuperable (6.8).
- La primera ventana registra el tiempo de cada inicio de sesión; con
  esa evidencia se revisan D_max y M_cierre.

### 6.4 Autenticación Entra y MFA

ActiveDirectoryInteractive con el administrador Entra. No está
verificado si go-sqlcmd reutiliza el token entre los 8 scripts: puede
pedir inicio de sesión y MFA en cada uno. Un MFA fallido o expirado
impide ese script; el ejecutor se detiene y el estado es «scripts
anteriores completos», que se trata como fallo parcial (6.8).

### 6.5 Orden exacto

001, 002, 003 (variante PROD), 004 (variante PROD), 005, 006, 007,
009 (variante PROD), con los SHA-256 del manifiesto (el ejecutor los
recalcula antes de cada script). Comando:
  ejecutar_migraciones.py --desde 001 --hasta 009 --modo ejecutar
    --limite-inicio-utc <hora>
Frase de confirmación: EJECUTAR 001-009 EN <base PROD>.

### 6.6 V: verificación previa (solo lectura)

Con operacion/inventario_estado.sql (se creará en T6B):
- inventario inv_PROD_previo: referencia de la recuperación; se exige
  RESUMEN|0|0|0;
- se anotan intercalación, compatibilidad y RCSI (línea BASE) y los
  permisos de xpay-api-prod (líneas PRIN y PERM);
- sin otras sesiones de usuario en la base; si no se puede garantizar
  la visibilidad de las sesiones, se registra como no verificable y
  no se ejecuta E.
Cualquier objeto de usuario o cualquier sesión ajena: no se ejecuta E.

### 6.7 Criterios de aceptación de E y P

1. Ejecutor con código 0 y TSV con 8 filas, en orden, con código 0.
2. Ningún error de nivel 11 o superior en los registros por script.
3. operacion/verificacion_b1.sql (T6B): TABLAS 24, UNIDADES 1,
   ROLES 18, PERMISOS 27, CUENTAS 25, USUARIOS 0, COL_KYC 30,
   ACENTOS 1.
4. Inventario inv_PROD_posterior: líneas de estructura (OBJ, COL, IDX,
   FK, CK, DF, FILAS) iguales a la referencia local; principales,
   permisos y esquemas iguales a inv_PROD_previo. Cualquier diferencia
   se analiza antes de B2 y no se corrige dentro de la ventana.
5. Registro de ventana completo, con las horas UTC de cada paso.

### 6.8 Fallo parcial

El ejecutor se detiene (código 4, 5 o 6). Dentro de la misma ventana
solo se permite lectura (TSV, registros, inventario inv_PROD_parcial)
y el cierre K1/K2. Prohibido reejecutar scripts, corregir o limpiar
en esa ventana.

### 6.9 R: recuperación (autorización aparte, otra ventana)

Mientras la limpieza de PROD no esté diseñada, revisada y probada
(T6C y T6D), las únicas vías de recuperación son la PITR a la hora
UTC anterior a E (restaura en una base nueva) o recrear la base vacía
y repetir S1–S3. Ambas exigen un plan aparte para el nombre de la
base, el usuario de la identidad administrada y la conexión.

Cuando exista la limpieza de PROD:
1. Comparación del inventario parcial con las referencias locales,
   revisada por una persona.
2. Limpieza en modo INFORME: comprueba, informa e imprime una huella;
   no cambia nada.
3. Autorización expresa del modo BORRAR con esa huella; si el estado
   cambió, BORRAR se aborta.
4. Inventario posterior idéntico a inv_PROD_previo.
5. B1 completo en una ventana nueva (6.3–6.7).
Si la limpieza se aborta por cualquier protección, no se fuerza: se
pasa a PITR o a recrear la base.

### 6.10 Cierre seguro de accesos

K1: borrar la regla /32. K2: desactivar el acceso público. Después,
comprobación de solo lectura de que no quedan reglas y de que el
acceso público está desactivado. Se ejecuta aunque E o P fallen. Si
se aplicó un permiso de conexión denegado (6.12), su retirada es un
paso aparte con autorización, antes de arrancar la aplicación.

### 6.11 Pendiente de validación real en Azure SQL

- Si go-sqlcmd pide MFA en cada script; tiempos y latencia reales.
- @@SERVERNAME y EngineEdition reales.
- Intercalación, compatibilidad y RCSI de la base, y su efecto en
  ACENTOS.
- Visibilidad de metadatos y de sesiones para el administrador Entra
  (VIEW DATABASE STATE).
- DDL transaccional y bloqueos exclusivos en la limpieza.
- Formato de salida de go-sqlcmd contra Azure.
- Estado real de la aplicación de PROD.
- Efecto de un permiso de conexión denegado sobre la identidad
  administrada.
- PITR: conservación del usuario y sus permisos en la base restaurada.

### 6.12 Etapas y decisiones antes de ejecutar B1

Etapas: T6B (inventario y verificación de solo lectura), T6C (diseño
de la limpieza de PROD), T6D (pruebas locales de la limpieza), T6E
(validaciones de Azure de solo lectura, en una ventana propia antes
de la de B1). Cada una con autorización independiente.

Decisiones del propietario:
1. Denegar la conexión a xpay-api-prod durante B1 y la recuperación:
   sí o no.
2. Ventana de T6E solo de lectura antes de B1: sí o no.
3. Valores iniciales de D_max y M_cierre.
4. Vía P1 o P2.
5. Recuperación preferida si la limpieza se aborta: PITR o recrear la
   base vacía.
