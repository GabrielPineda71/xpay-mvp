# XPAY MVP — Variables Operativas QA

**Versión:** 1.0
**Fecha:** 2026-06-18
**Tipo:** Guía operativa QA — uso exclusivo QA / desarrollo

---

## 1. Propósito

Esta guía explica cómo preparar las variables de entorno locales necesarias para ejecutar scripts QA como `scripts/generate-qa-financial-ops.sh` sin guardar secretos, tokens ni contraseñas en el repositorio.

El repositorio versiona únicamente el archivo de **ejemplo** con placeholders. El archivo con valores reales vive exclusivamente en el equipo del ejecutor y nunca es commiteado.

---

## 2. Archivos involucrados

| Archivo | Estado en repo | Propósito |
|---------|---------------|-----------|
| `ops/qa.env.example` | **Versionado** — solo placeholders | Plantilla de referencia para el equipo |
| `ops/qa.env.local` | **Ignorado** por `.gitignore` | Archivo local con valores reales — nunca en repo |
| `scripts/generate-qa-financial-ops.sh` | Versionado | Consume las variables para ejecutar el flujo financiero QA |
| `frontend/xpay-admin/.env` | Ignorado por `.gitignore` | Variables Vite para build QA del frontend (`VITE_API_BASE_URL`) |

---

## 3. Cómo crear el archivo local

```bash
# 1. Copiar la plantilla
cp ops/qa.env.example ops/qa.env.local

# 2. Editar con los valores reales del ambiente QA
nano ops/qa.env.local
# (o usar VS Code, vim, etc.)

# 3. Cargar las variables en la sesión de shell actual
source ops/qa.env.local

# 4. Verificar que las variables están disponibles
echo "API_BASE=$API_BASE"
echo "TOKEN=${TOKEN:0:20}..."   # muestra solo los primeros 20 caracteres
echo "ID_WALLET_USUARIO_1=$ID_WALLET_USUARIO_1"
echo "ID_WALLET_USUARIO_2=$ID_WALLET_USUARIO_2"
echo "ID_USUARIO_QA=$ID_USUARIO_QA"
echo "ID_COMERCIO_QA=$ID_COMERCIO_QA"
```

> `source` (o `. ops/qa.env.local`) carga las variables en el shell actual. Las variables no persisten entre sesiones; repetir `source ops/qa.env.local` al inicio de cada sesión de trabajo QA.

---

## 4. Variables obligatorias

| Variable | Descripción | Ejemplo placeholder | Cómo obtenerla | Sensible |
|----------|-------------|---------------------|----------------|----------|
| `API_BASE` | URL base del backend sin barra final | `http://localhost:5000` | Configurar según ambiente (local o Azure QA) | No |
| `TOKEN` | JWT obtenido al hacer login | `eyJhbGciOiJIUzI1NiIs...` | `POST /api/auth/login` (ver sección 6) | **Sí** |
| `ID_WALLET_USUARIO_1` | ID de la wallet QA del usuario uno | `1` | `GET /api/admin/wallets` (ver sección 7) | No |
| `ID_WALLET_USUARIO_2` | ID de la wallet QA del usuario dos | `2` | `GET /api/admin/wallets` | No |
| `ID_USUARIO_QA` | ID del usuario QA para el campo `creadoPor` | `1` | `GET /api/admin/wallets` o consulta SQL directa | No |
| `ID_COMERCIO_QA` | ID del comercio demo QA | `2` | `GET /api/admin/comercios` (ver sección 7) | No |

---

## 5. Variables opcionales

Normalmente `scripts/generate-qa-financial-ops.sh` las captura automáticamente de las respuestas JSON (con `jq` o por grep). Solo es necesario pre-exportarlas si:

- Se retoma el script a partir de un paso intermedio (por ejemplo, el pago QR ya se hizo y solo se quiere liquidar).
- `jq` no está instalado y la extracción automática falló.
- Se quiere reutilizar una venta o retiro existente.

| Variable | Descripción | Origen |
|----------|-------------|--------|
| `ID_VENTA_QR` | ID de la venta QR en estado `CONTINGENCIA` | Capturado en paso C, o `GET /api/admin/ventas-qr` |
| `ID_RETIRO_1` | ID del primer retiro en estado `PENDIENTE` | Capturado en paso E, o `GET /api/comercios/retiros` |
| `ID_RETIRO_2` | ID del segundo retiro en estado `PENDIENTE` | Capturado en paso G, o `GET /api/comercios/retiros` |

---

## 6. Cómo obtener TOKEN

El token JWT se obtiene haciendo login con un usuario QA habilitado.

```bash
curl -s -X POST "$API_BASE/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{
    "usuario": "<usuario-qa>",
    "password": "<contraseña-qa>"
  }'
```

Copiar el valor del campo `token` de la respuesta y exportarlo:

```bash
export TOKEN="eyJhbGciOiJIUzI1NiIs..."
```

**Reglas de seguridad para el TOKEN:**

- No guardar la contraseña ni el token en ningún archivo del repositorio.
- No pegar el token en documentos, issues, Slack ni correos.
- Exportar `TOKEN` solo en el shell local de la sesión actual.
- El token expira según la configuración `Jwt__ExpirationHours` del backend. Si recibe `401`, repetir el login.
- Si el token se comparte accidentalmente, rotarlo inmediatamente reiniciando el backend con una nueva `Jwt__Key` en el ambiente QA.

**Si el usuario del seed 008 no puede hacer login:**

El script `database/008_seed_qa_dataset.sql` crea usuarios con un hash BCrypt placeholder que no permite autenticación. Ver la sección 3 de `docs/QA_FINANCIAL_OPERATIONS_API.md` para las opciones de habilitación. Como alternativa, usar los usuarios creados por `scripts/validate-backend.sh` (`carlos_ci_test`, `maria_ci_test`) si el script de CI ya fue ejecutado en el ambiente.

---

## 7. Cómo obtener IDs

Una vez cargado el token, consultar las entidades QA:

### Wallets QA

```bash
curl -s "$API_BASE/api/admin/wallets" \
  -H "Authorization: Bearer $TOKEN" | jq .
```

Buscar las wallets con nombres `Wallet QA Usuario Uno` y `Wallet QA Usuario Dos` (creadas por el seed 008). Anotar sus `idWallet`.

### Comercio QA

```bash
curl -s "$API_BASE/api/admin/comercios" \
  -H "Authorization: Bearer $TOKEN" | jq .
```

Buscar `Comercio Demo XPAY QA`. Anotar su `idComercio`.

### Retiros existentes

```bash
curl -s "$API_BASE/api/comercios/retiros" \
  -H "Authorization: Bearer $TOKEN" | jq .
```

### Ventas QR existentes

```bash
curl -s "$API_BASE/api/admin/ventas-qr" \
  -H "Authorization: Bearer $TOKEN" | jq .
```

> Si `jq` no está instalado, las respuestas se mostrarán como JSON crudo. Buscar manualmente los campos `idWallet`, `idComercio`, etc.

---

## 8. Cómo ejecutar el script financiero QA

```bash
# Asegurarse de estar en la raíz del repositorio
cd /path/to/xpay-mvp

# 1. Cargar variables
source ops/qa.env.local

# 2. Ejecutar el flujo financiero completo
bash scripts/generate-qa-financial-ops.sh
```

El script ejecuta los pasos A–H (recarga, transferencia, pago QR, liquidación, retiros confirmado y rechazado) y muestra un resumen al final. Ver `docs/QA_FINANCIAL_OPERATIONS_API.md` para la descripción completa de cada paso.

---

## 9. Seguridad

- [ ] `ops/qa.env.local` no está versionado (verificar con `git status`).
- [ ] El archivo `TOKEN` nunca es commiteado al repositorio.
- [ ] Las contraseñas de usuarios QA no están en ningún archivo del repo.
- [ ] No usar credenciales de producción en este ambiente.
- [ ] No usar saldos ni cuentas bancarias reales.
- [ ] No usar datos de clientes reales (nombres, cédulas, emails).
- [ ] Rotar el token si se comparte por error (ver sección 6).
- [ ] Ejecutar `git status` y `git diff` antes de cada commit para confirmar que no se incluyen archivos locales.
- [ ] No commitear respuestas JSON, logs con tokens ni artifacts generados.

---

## 10. Troubleshooting

| Síntoma | Causa probable | Solución |
|---------|---------------|----------|
| Script falla con `Missing required environment variable: TOKEN` | `source ops/qa.env.local` no fue ejecutado o `TOKEN` está vacío | Completar `ops/qa.env.local` con el token y ejecutar `source ops/qa.env.local` |
| `401 Unauthorized` en cualquier endpoint | Token expirado o incorrecto | Repetir `POST /api/auth/login` y actualizar `TOKEN` en `ops/qa.env.local` |
| `curl: (7) Failed to connect` | `API_BASE` apunta a URL incorrecta o backend no está corriendo | Verificar que el backend está activo y que `API_BASE` no tiene barra final |
| `400 Bad Request` con "Wallet no encontrada" | `ID_WALLET_USUARIO_1` o `ID_WALLET_USUARIO_2` incorrectos | Consultar `GET /api/admin/wallets` para obtener los IDs correctos |
| `400 Bad Request` con "Comercio no encontrado" | `ID_COMERCIO_QA` incorrecto | Consultar `GET /api/admin/comercios` para obtener el ID del comercio QA |
| `source ops/qa.env.local` no carga variables | El archivo `ops/qa.env.local` no existe | Ejecutar `cp ops/qa.env.example ops/qa.env.local` y completar los valores |
| Variables están vacías después del `source` | Las líneas en `ops/qa.env.local` no tienen `export` | Verificar que cada línea comience con `export VARIABLE="valor"` |
| Script ejecuta sin efectos, variables en blanco | Las variables se exportaron en un subshell diferente | Usar `source` (no `bash ops/qa.env.local`) para cargar en el shell actual |
| `ops/qa.env.local` aparece en `git status` | La entrada en `.gitignore` no aplica | Verificar que `.gitignore` contiene `ops/qa.env.local` y ejecutar `git rm --cached ops/qa.env.local` si ya fue añadido |

---

## 11. Documentos relacionados

| Documento | Propósito |
|-----------|-----------|
| [`docs/QA_FINANCIAL_OPERATIONS_API.md`](QA_FINANCIAL_OPERATIONS_API.md) | Guía completa de operaciones financieras QA vía API con DTOs exactos |
| [`scripts/generate-qa-financial-ops.sh`](../scripts/generate-qa-financial-ops.sh) | Script que consume las variables para ejecutar el flujo financiero QA |
| [`docs/QA_DEPLOYMENT_RUNBOOK.md`](QA_DEPLOYMENT_RUNBOOK.md) | Runbook operativo: recursos Azure, variables de entorno backend, scripts SQL |
| [`database/008_seed_qa_dataset.sql`](../database/008_seed_qa_dataset.sql) | Seed QA: crea las entidades base (wallets, comercio, QR) antes de obtener IDs |
| [`README.md`](../README.md) | Descripción del backend: endpoints, configuración, fases completadas |
| [`docs/QA_MASTER_E2E_CHECKLIST.md`](QA_MASTER_E2E_CHECKLIST.md) | Checklist maestro QA end-to-end: contexto completo de dónde encaja la preparación de variables |

---

*Esta guía cubre el MVP XPAY — Fases 1 a 28. Actualizar si cambian las variables requeridas por los scripts QA.*
