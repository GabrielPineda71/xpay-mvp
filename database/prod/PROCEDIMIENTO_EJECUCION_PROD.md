# Procedimiento de ejecución controlada — migraciones XPAY PROD

> Documento de preparación. **No se ha ejecutado.** Cada paso que escribe en Azure o en la base requiere autorización expresa e individual.

## 0. Condiciones previas (todas obligatorias)

1. Paquete commiteado (autorización aparte) y árbol limpio: `git status --short database/` sin cambios.
2. Validador local en OK: `python3 database/prod/tools/validar_paquete.py` → `RESULTADO: OK`.
3. Decisiones tomadas sobre 045a y los datos pendientes 016b, 021b y 045b (README, "Decisiones pendientes"). 009 y 034 ya están en el conjunto ejecutable.
4. Base todavía vacía: comprobación de solo lectura antes del primer bloque (ver 3.1).
5. Hora UTC de inicio registrada: es el punto de restauración (PITR 35 días configurado en C6).
6. go-sqlcmd aislado verificado (`~/xpay-tools/go-sqlcmd-1.10.0/sqlcmd`, SHA conocido).
7. Directorio de registro: `~/xpay-tools/xpay-prod-migraciones/logs/` (700), fuera del repositorio.
8. Definir `XPAY_PROD_SQL_SERVIDOR`, `XPAY_PROD_SQL_BASE` y `XPAY_PROD_SQL_ADMIN` desde un archivo local fuera del repositorio (por ejemplo `~/xpay-tools/xpay-prod-migraciones/entorno.local`, permisos 600). Los valores reales nunca se guardan en el repositorio.

## 1. Vía de acceso a SQL (decisión pendiente)

El servidor tiene el acceso público deshabilitado. Por orden de preferencia:

| Opción | Descripción | Ventajas | Inconvenientes |
|---|---|---|---|
| **P1. Equipo dentro de la VNet** (recomendada para evaluar) | VM Linux temporal sin IP pública en una subred nueva de la VNet de PROD, accedida con Azure Bastion. La zona DNS privada ya está enlazada a la VNet, así que el nombre del servidor resuelve a la IP privada del endpoint privado. | Sin exposición pública de SQL. Sin límite estricto de tiempo. | Crea infraestructura nueva (VM, Bastion, subred), con coste y autorización. Sin navegador: exige `ActiveDirectoryDeviceCode` o `az login --use-device-code` + `ActiveDirectoryAzCli`. Hay que copiar el paquete a la VM y verificar sus SHA allí. |
| **P2. Ventanas públicas temporales por bloque** | Procedimiento de ventana temporal ya probado (registro interno), con un bloque de scripts por ventana. | Procedimiento probado, sin infraestructura nueva, MFA en navegador. | Exposición pública breve (IP /32, Entra-only). Varias ventanas de ≤30 min. |

Riesgo común a ambas: el ejecutor invoca go-sqlcmd una vez por script; con autenticación interactiva **podría pedir inicio de sesión/MFA en cada script**. No está verificado si go-sqlcmd reutiliza el token entre invocaciones. Se comprobará en el primer bloque; si pide MFA por script, los bloques deben ser más pequeños.

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

## 5. Lo que este procedimiento nunca hace

- No abre acceso público ni crea infraestructura por sí mismo.
- No ejecuta 008, 018, 020 ni los fixtures de CI.
- No ejecuta entradas bloqueadas, salvo que se desbloqueen en la especificación, se regenere el manifiesto y se valide.
- No crea usuarios, cupos ni movimientos financieros.
- No guarda credenciales ni tokens.
