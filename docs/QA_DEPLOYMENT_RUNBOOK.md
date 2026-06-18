# XPAY MVP — Runbook Operativo de Despliegue QA

**Versión:** 1.0  
**Fecha:** 2026-06-17  
**Tipo:** Guía operativa manual/controlada  

---

## 1. Propósito

Este runbook es la guía paso a paso para ejecutar el **despliegue real** de XPAY MVP QA Candidate v0.1 en Azure, siguiendo el alcance definido en [`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md).

**Qué es este documento:**
- Una guía operativa concreta con comandos, validaciones y checklist.
- Una referencia para el responsable técnico que ejecuta el despliegue.
- Un instrumento de control que deja trazabilidad de cada paso.

**Qué NO es este documento:**
- **No contiene secretos reales.** Todas las credenciales usan placeholders.
- **No ejecuta el despliegue automáticamente.** Cada paso debe ejecutarse manualmente.
- No reemplaza el juicio del responsable técnico ante situaciones imprevistas.
- No autoriza el uso de este ambiente con dinero real o usuarios de producción.

---

## 2. Información de versión

| Campo | Valor |
|-------|-------|
| **Versión** | XPAY MVP QA Candidate v0.1 |
| **Rama** | main |
| **Commit recomendado** | `52ec9cc` (o HEAD actual de `main`) |
| **Backend** | .NET 8 ASP.NET Core |
| **Frontend** | React 18 / Vite 5 |
| **Base de datos** | Azure SQL — `XPAY_MVP_QA` |
| **Tipo de despliegue** | Manual / controlado |
| **Ambiente** | QA |
| **Responsable técnico** | Por designar antes del despliegue |
| **Fecha prevista** | Por definir |

---

## 3. Recursos Azure a crear

Crear los siguientes recursos en el portal Azure o mediante Azure CLI antes de iniciar el despliegue. El orden sugerido es de arriba hacia abajo.

| Recurso | Nombre sugerido | Propósito | Tier mínimo sugerido | Obligatorio |
|---------|----------------|-----------|---------------------|-------------|
| Resource Group | `rg-xpay-qa` | Contenedor lógico de todos los recursos QA | — | Sí |
| SQL Server | `xpay-sql-qa` | Servidor de base de datos para Azure SQL | — (se define en SQL Database) | Sí |
| SQL Database | `XPAY_MVP_QA` | Base de datos relacional del sistema | Basic (5 DTU) — puede escalar | Sí |
| App Service Plan Backend | `asp-xpay-api-qa` | Plan de cómputo para el App Service del backend | B1 (Basic) | Sí |
| App Service Backend | `xpay-api-qa` | Servicio que ejecuta el backend .NET 8 | Usa `asp-xpay-api-qa` | Sí |
| App Service Plan Frontend | `asp-xpay-admin-qa` | Plan de cómputo para el frontend (si no se usa Static Web App) | B1 o Free | Condicional |
| Static Web App | `xpay-admin-qa` | Alternativa al App Service para servir el frontend estático | Free | Condicional |
| Application Insights | `ai-xpay-qa` | Telemetría y logs del backend | Free tier | No (recomendado) |
| Key Vault | `kv-xpay-qa` | Gestión segura de secretos (JWT Key, Connection String) | Standard | No (recomendado) |

> **Nota:** Usar App Service Plan + App Service para el frontend **o** Static Web App — no ambos.  
> Static Web App es más simple y económico para sitios estáticos React/Vite.

---

## 4. Variables de entorno — Backend

Configurar en **Azure App Service → Configuration → Application settings** usando doble guión bajo (`__`) como separador de jerarquía .NET.

| Variable | Descripción | Ejemplo (placeholder) | Obligatoria | Sensible |
|----------|-------------|----------------------|-------------|----------|
| `ConnectionStrings__XpayConnection` | Cadena de conexión completa a Azure SQL QA | `Server=xpay-sql-qa.database.windows.net;Database=XPAY_MVP_QA;User Id=<db-user>;Password=<db-password>;Encrypt=True;TrustServerCertificate=False;` | Sí | **Sí** |
| `Jwt__Key` | Clave secreta HS256 para firmar tokens JWT | `<CLAVE_ALEATORIA_MINIMO_32_CARACTERES>` | Sí | **Sí** |
| `Jwt__Issuer` | Emisor declarado en el token JWT | `Xpay.Api.QA` | Sí | No |
| `Jwt__Audience` | Audiencia declarada en el token JWT | `Xpay.Admin.QA` | Sí | No |
| `Jwt__ExpirationHours` | Validez del token en horas | `8` | Sí | No |
| `Api__Name` | Nombre de la API (visible en `/api/version`) | `XPAY API QA` | Sí | No |
| `Api__Version` | Versión de la API (visible en `/api/version`) | `0.1.0-mvp-qa` | Sí | No |
| `Cors__AllowedOrigins__0` | Origen del frontend QA permitido por CORS | `https://xpay-admin-qa.azurewebsites.net` | Sí | No |

**Reglas críticas:**

- `Jwt__Key` debe tener **mínimo 32 caracteres** (idealmente 64+). Una clave corta hace que el backend falle al arrancar.
- `ConnectionStrings__XpayConnection` **no debe subirse nunca al repositorio**. Configurar solo en Azure App Settings.
- `Cors__AllowedOrigins__0` debe ser la URL **exacta** del frontend QA, incluyendo `https://` y sin barra final.
- Si se agrega un segundo origen, usar `Cors__AllowedOrigins__1` con la misma estructura.

---

## 5. Variables de entorno — Frontend

| Variable | Descripción | Ejemplo (placeholder) | Obligatoria |
|----------|-------------|----------------------|-------------|
| `VITE_API_BASE_URL` | URL base del backend QA | `https://xpay-api-qa.azurewebsites.net` | Sí |

**Importante — Build time vs Runtime:**

Vite inyecta `VITE_API_BASE_URL` **en tiempo de compilación**, no en tiempo de ejecución. Esto significa:
- Si la URL del backend cambia, el frontend **debe reconstruirse** (`npm run build`) con el nuevo valor.
- En Azure Static Web App no se puede cambiar la URL sin rebuild y redeploy.
- Para QA, usar la URL real del App Service backend QA, no `localhost`.

**Flujo recomendado:**
```bash
# 1. Copiar el template de variables QA
cp frontend/xpay-admin/.env.qa.example frontend/xpay-admin/.env

# 2. Verificar/ajustar la URL si difiere del ejemplo
# Editar .env y cambiar VITE_API_BASE_URL si es necesario

# 3. Construir el frontend con las variables QA
cd frontend/xpay-admin
npm install
npm run build
# Los archivos estáticos quedan en dist/
```

---

## 6. Preparación de base de datos

Ejecutar los scripts SQL en el siguiente **orden estricto**. No saltarse ninguno ni ejecutarlos en paralelo.

| Orden | Script | Contenido |
|-------|--------|-----------|
| 1 | `database/001_security_identity.sql` | Tablas de seguridad: usuarios, personas, unidades de negocio |
| 2 | `database/002_wallet_ledger.sql` | Tablas de wallets y ledger de transacciones |
| 3 | `database/003_comercios_qr.sql` | Tablas de comercios, tiendas y QR |
| 4 | `database/004_liquidacion_qr.sql` | Tabla de ventas QR y liquidaciones |
| 5 | `database/005_retiros_comercio.sql` | Tabla de retiros de comercio |
| 6 | `database/006_gestion_retiros_comercio.sql` | Stored procedures y lógica de gestión de retiros |
| 7 | `database/007_security_roles_jwt.sql` | Roles, permisos y configuración JWT en BD |

**Herramientas para ejecutar:**
- Azure Data Studio (recomendado para conexión a Azure SQL)
- SQL Server Management Studio (SSMS)
- Azure Cloud Shell con `sqlcmd`

**Checklist de base de datos:**

- [ ] Script 001 ejecutado sin errores
- [ ] Script 002 ejecutado sin errores
- [ ] Script 003 ejecutado sin errores
- [ ] Script 004 ejecutado sin errores
- [ ] Script 005 ejecutado sin errores
- [ ] Script 006 ejecutado sin errores
- [ ] Script 007 ejecutado sin errores
- [ ] Tablas principales confirmadas: `usuarios`, `wallets`, `ledger_transacciones`, `comercios`, `ventas_qr`, `retiros_comercio`
- [ ] Seed de datos confirmado: existe registro `Comercio Demo XPAY` en tabla `comercios`
- [ ] Seed de datos confirmado: existe QR `QR-DEMO-XPAY-001` vinculado al comercio demo
- [ ] Cuentas ledger confirmadas: existen entradas de tipo `SISTEMA` en la tabla de ledger

> ⚠️ **No modificar los scripts SQL.** Si alguno falla, detener, revisar el error y corregir el problema de entorno (conexión, permisos, orden) antes de continuar.

---

## 7. Despliegue backend

Ejecutar estos pasos después de que la base de datos esté lista y los recursos Azure creados.

**Paso 1 — Confirmar CI verde**  
Verificar en GitHub Actions que el commit a desplegar (`52ec9cc` o HEAD de `main`) tiene:
- `Backend Validation` → `completed success`
- `Frontend Build` → `completed success`

**Paso 2 — Crear App Service backend en Azure**  
En Azure Portal: Crear recurso → App Service → Nombre: `xpay-api-qa`, Runtime: `.NET 8`, OS: Linux, Plan: `asp-xpay-api-qa`.

**Paso 3 — Configurar runtime .NET 8**  
En `xpay-api-qa` → Configuration → General settings → Stack: `.NET`, Version: `.NET 8`.

**Paso 4 — Configurar App Settings**  
En `xpay-api-qa` → Configuration → Application settings → agregar las 7 variables de la sección 4 (excepto Connection String).

**Paso 5 — Configurar Connection String**  
En `xpay-api-qa` → Configuration → Connection strings → agregar `XpayConnection` de tipo `SQLAzure` con el valor completo de la connection string.

**Paso 6 — Desplegar backend**

*Opción A — Desde GitHub (manual zip deploy):*
```bash
# Compilar el backend localmente
dotnet publish backend/Xpay.Api/Xpay.Api.csproj -c Release -o ./publish

# Comprimir para deploy
cd publish && zip -r ../xpay-api-qa.zip . && cd ..

# Deploy via Azure CLI
az webapp deploy --resource-group rg-xpay-qa \
  --name xpay-api-qa \
  --src-path xpay-api-qa.zip \
  --type zip
```

*Opción B — Desde Azure Portal:*  
App Service → Deployment Center → seleccionar repositorio GitHub, rama `main`.

**Paso 7 — Probar `/health`**
```bash
curl https://xpay-api-qa.azurewebsites.net/health
# Esperado: HTTP 200 {"status":"Healthy"}
```

**Paso 8 — Probar `/api/version`**
```bash
curl https://xpay-api-qa.azurewebsites.net/api/version
# Esperado: HTTP 200 con nombre y versión
```

**Paso 9 — Probar `/swagger`**  
Abrir `https://xpay-api-qa.azurewebsites.net/swagger` en el navegador. La UI debe cargar con el botón **Authorize**.

**Paso 10 — Probar login**
```bash
curl -X POST https://xpay-api-qa.azurewebsites.net/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"usuario":"<usuario_prueba>","password":"<password_prueba>"}'
# Esperado: HTTP 200, campo data.token presente
```

---

## 8. Despliegue frontend

Ejecutar después de que el backend QA esté desplegado y validado.

**Paso 1 — Confirmar `VITE_API_BASE_URL`**  
Verificar que la URL del backend QA es accesible:
```bash
curl https://xpay-api-qa.azurewebsites.net/health
```

**Paso 2 — Crear `.env` desde el template QA**
```bash
cp frontend/xpay-admin/.env.qa.example frontend/xpay-admin/.env
# Verificar que VITE_API_BASE_URL apunta al backend QA correcto
cat frontend/xpay-admin/.env
```

**Paso 3 — Construir el frontend**
```bash
cd frontend/xpay-admin
npm install
npm run build
# Resultado: dist/ con index.html y assets/
```

**Paso 4 — Desplegar `dist/`**

*Opción A — Azure Static Web App (recomendado):*
```bash
# Instalar CLI de Azure Static Web Apps
npm install -g @azure/static-web-apps-cli

# Deploy
swa deploy frontend/xpay-admin/dist \
  --deployment-token <TOKEN_SWA_QA> \
  --env production
```

*Opción B — Azure App Service (zip deploy):*
```bash
cd frontend/xpay-admin/dist
zip -r ../../xpay-admin-qa.zip .
az webapp deploy --resource-group rg-xpay-qa \
  --name xpay-admin-qa \
  --src-path xpay-admin-qa.zip \
  --type zip
```

**Paso 5 — Verificar que abre `/login`**  
Abrir `https://xpay-admin-qa.azurewebsites.net/login` en el navegador. Debe mostrar el formulario de login.

**Paso 6 — Verificar API visible en login**  
Debajo del formulario debe aparecer: `API: https://xpay-api-qa.azurewebsites.net`. Si muestra `localhost`, el build se generó con `.env` incorrecto → repetir paso 2 y 3.

**Paso 7 — Probar login**  
Ingresar con el usuario de prueba. Debe redirigir a `/dashboard`.

**Paso 8 — Probar dashboard**  
Verificar que las métricas y las 3 tablas cargan sin errores de consola (F12 → Console).

---

## 9. Smoke test post-despliegue

Ejecutar inmediatamente después del despliegue, antes de abrir el ambiente a testers:

- [ ] `GET /health` → HTTP 200
- [ ] `GET /api/version` → HTTP 200, nombre y versión correctos
- [ ] `/swagger` → UI carga con botón Authorize
- [ ] Login con usuario de prueba → redirige a `/dashboard`
- [ ] `GET /api/wallets/persona/1` sin token → **HTTP 401** (no 200, no 500)
- [ ] Dashboard carga con métricas y 3 tablas sin errores
- [ ] `/wallets/listado` carga con datos
- [ ] `/comercios/listado` carga con datos
- [ ] `/retiros/listado` carga con datos
- [ ] `/ventas-qr/listado` carga con datos
- [ ] `/ledger/listado` carga con datos
- [ ] CORS correcto: no hay error `Access-Control-Allow-Origin` en consola del navegador
- [ ] `API:` en header del frontend muestra el hostname QA (no `localhost`)
- [ ] `API:` en pantalla de login muestra la URL QA correcta
- [ ] No hay datos reales de clientes ni saldos reales en el sistema

---

## 10. Validaciones financieras mínimas QA

Ejecutar después del smoke test para confirmar integridad del sistema financiero con datos de prueba:

- [ ] Saldo de wallet visible en `/wallets/:idWallet` (número positivo, no nulo)
- [ ] Estado de cuenta de wallet carga con movimientos en el historial
- [ ] Detalle ledger en `/ledger/:idTransaccion` muestra descripción, valor y tipo
- [ ] Débitos y créditos en el ledger son **numéricamente consistentes**: para una venta QR, el débito en wallet usuario y el crédito en wallet comercio tienen el mismo valor
- [ ] Retiro con estado `PAGADO` o `RECHAZADO` **no muestra botones de acción** (confirmar/rechazar)
- [ ] Confirmar/rechazar retiro se ejecuta **únicamente en ambiente QA con datos de prueba** — registrar el ID del retiro usado en evidencias

> ⚠️ **Nunca ejecutar confirmación o rechazo de retiros sobre datos que puedan confundirse con transacciones reales.**

---

## 11. Rollback básico

No existe una estrategia de rollback productiva automatizada en esta versión. Las siguientes acciones son para el ambiente QA únicamente.

### Si el backend falla al arrancar o tras el despliegue

1. Revisar logs en Azure Portal: `xpay-api-qa` → Log stream.
2. Verificar variables de entorno (sección 4): JWT Key ≥ 32 chars, Connection String correcta.
3. Si el problema es de código: redeployar el commit anterior.
   ```bash
   git checkout <commit-anterior>
   dotnet publish backend/Xpay.Api/Xpay.Api.csproj -c Release -o ./publish
   # Repetir deploy zip
   ```

### Si el frontend falla o apunta al backend incorrecto

1. Verificar `VITE_API_BASE_URL` en el `.env` antes del build.
2. Reconstruir con la URL correcta y redesplegar `dist/`.
3. Limpiar caché del navegador (`Ctrl+Shift+R`) antes de verificar.

### Si una migración SQL falla

1. **Detener inmediatamente.** No ejecutar los scripts siguientes.
2. Revisar el mensaje de error exacto (tabla ya existente, constraint violado, etc.).
3. Si el ambiente QA tiene datos relevantes, tomar backup antes de continuar.
4. Corregir el problema de entorno (no modificar el script) y volver a ejecutar desde el script que falló.

### Si CORS falla (error `Access-Control-Allow-Origin`)

1. Verificar que `Cors__AllowedOrigins__0` en Azure App Settings es exactamente la URL del frontend QA (sin barra final, con `https://`).
2. Guardar los cambios en Configuration y **reiniciar el App Service** (`Overview → Restart`).
3. Esperar 30 segundos y probar nuevamente.

### Si JWT falla (login devuelve 500 o token inválido)

1. Verificar que `Jwt__Key` tiene mínimo 32 caracteres.
2. Verificar que `Jwt__Issuer` y `Jwt__Audience` coinciden exactamente entre backend y cualquier validación externa.
3. Guardar y reiniciar el App Service.

> **Para QA:** siempre tomar un backup del estado de la BD antes de ejecutar migraciones si hay datos de prueba valiosos. Azure SQL permite crear backups desde el portal con un clic.

---

## 12. Checklist de "QA listo para testers"

Completar todos los ítems antes de comunicar al equipo QA que el ambiente está disponible:

- [ ] Recursos Azure creados: Resource Group, SQL Server, SQL Database, App Service(s)
- [ ] Scripts SQL 001 a 007 ejecutados en orden, sin errores
- [ ] Backend desplegado y respondiendo en `https://xpay-api-qa.azurewebsites.net`
- [ ] Frontend desplegado y accesible en su URL QA
- [ ] Variables de entorno configuradas en Azure App Settings (sin secretos en repo)
- [ ] GitHub Actions `Backend Validation` en `completed success` para el commit desplegado
- [ ] GitHub Actions `Frontend Build` en `completed success` para el commit desplegado
- [ ] Smoke test completo (sección 9) ejecutado y todos los ítems ✅
- [ ] `docs/QA_MANUAL_TESTING.md` compartido con el equipo QA
- [ ] Copia de `docs/QA_EXECUTION_TEMPLATE.md` preparada para registrar la ejecución real
- [ ] `docs/RELEASE_QA_CANDIDATE.md` revisado: responsable técnico confirma que el alcance es correcto
- [ ] Responsable técnico firma/aprueba formalmente el inicio de pruebas

---

## 13. Problemas comunes

| Síntoma | Causa probable | Solución |
|---------|---------------|----------|
| Login devuelve HTTP 401 | Usuario no existe en la BD o contraseña incorrecta | Verificar que los scripts SQL se ejecutaron; crear usuario de prueba manualmente si es necesario |
| Frontend muestra *"No fue posible conectar con el backend XPAY"* | `VITE_API_BASE_URL` incorrecto, backend caído, o CORS bloqueando | Verificar URL en pantalla de login; probar `/health` directamente; revisar CORS |
| Error `Access-Control-Allow-Origin` en consola | `Cors__AllowedOrigins__0` no coincide con la URL del frontend | Corregir la variable en Azure App Settings y reiniciar el App Service |
| Swagger abre pero endpoints devuelven 500 | Variables de entorno incompletas o DB no migrada | Revisar Log Stream del App Service; verificar Connection String y migraciones |
| SQL Server login failed | Credenciales de BD incorrectas o firewall bloqueando | Verificar `User Id` y `Password` en la connection string; agregar IP del App Service al firewall de Azure SQL |
| JWT Key corta — backend no arranca | `Jwt__Key` menor a 32 caracteres | Reemplazar con una clave aleatoria de mínimo 32 caracteres en Azure App Settings |
| Dashboard no carga métricas | Endpoint `/api/reportes/operaciones/resumen-general` falla | Verificar token en request; revisar logs del App Service; confirmar que las tablas tienen datos seed |
| Frontend apunta a `localhost` en QA | Build generado con `.env` local en lugar de QA | Reconstruir con `cp .env.qa.example .env` y `npm run build`; redesplegar `dist/` |
| Retiros no aparecen en listado | No hay retiros creados en la BD QA | Crear retiro de prueba mediante `POST /api/comercios/solicitar-retiro` con datos de prueba |
| Ledger vacío en listado | Scripts SQL ejecutados pero no hay transacciones | Ejecutar al menos una operación de prueba (login + recarga + pago QR) para generar entradas en el ledger |

---

## 14. Documentos relacionados

| Documento | Propósito |
|-----------|-----------|
| [`docs/RELEASE_QA_CANDIDATE.md`](RELEASE_QA_CANDIDATE.md) | Declaración formal de la versión: alcance, fases, riesgos y criterios |
| [`docs/QA_DEPLOYMENT.md`](QA_DEPLOYMENT.md) | Guía de configuración de variables y arquitectura QA |
| [`docs/QA_MANUAL_TESTING.md`](QA_MANUAL_TESTING.md) | 35 casos de prueba manuales con pasos y resultados esperados |
| [`docs/QA_EXECUTION_TEMPLATE.md`](QA_EXECUTION_TEMPLATE.md) | Plantilla de registro de ejecución, evidencias y acta de cierre |
| [`README.md`](../README.md) | Descripción del backend: endpoints, configuración, fases completadas |
| [`frontend/xpay-admin/README.md`](../frontend/xpay-admin/README.md) | Configuración del frontend: instalación, build, rutas, errores |

---

*Este runbook cubre el MVP XPAY QA Candidate v0.1 — Fases 1 a 22. Actualizar si cambia la arquitectura de despliegue o se agregan servicios Azure.*
