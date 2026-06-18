# XPAY MVP — Backend API

Sistema de pagos digitales con wallets, QR, liquidaciones, retiros y reportes transaccionales.

---

## Cómo correr el backend

### Requisitos

- .NET 8 SDK
- SQL Server 2022 (local, Docker o Azure SQL)

### Paso a paso

```bash
# 1. Crear base de datos y ejecutar migraciones en orden
sqlcmd -S localhost -U sa -P "<contraseña>" -Q "CREATE DATABASE XPAY_MVP"
sqlcmd -S localhost -U sa -P "<contraseña>" -d XPAY_MVP -i database/001_security_identity.sql
sqlcmd -S localhost -U sa -P "<contraseña>" -d XPAY_MVP -i database/002_wallet_ledger.sql
sqlcmd -S localhost -U sa -P "<contraseña>" -d XPAY_MVP -i database/003_comercios_qr.sql
sqlcmd -S localhost -U sa -P "<contraseña>" -d XPAY_MVP -i database/004_liquidacion_qr.sql
sqlcmd -S localhost -U sa -P "<contraseña>" -d XPAY_MVP -i database/005_retiros_comercio.sql
sqlcmd -S localhost -U sa -P "<contraseña>" -d XPAY_MVP -i database/006_gestion_retiros_comercio.sql
sqlcmd -S localhost -U sa -P "<contraseña>" -d XPAY_MVP -i database/007_security_roles_jwt.sql

# 2. Ajustar cadena de conexión en backend/Xpay.Api/appsettings.json

# 3. Correr el backend
cd backend/Xpay.Api
dotnet restore
dotnet run
```

El backend queda disponible en `http://localhost:5000`.
Swagger UI disponible en `http://localhost:5000/swagger`.

---

## Endpoints públicos (sin autenticación)

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| `POST` | `/api/usuarios/registro-final` | Registro de usuario final con wallet |
| `POST` | `/api/auth/login` | Login — devuelve JWT |
| `GET`  | `/health` | Health check del servicio |
| `GET`  | `/api/version` | Versión y ambiente de la API |

---

## Cómo autenticarse

```bash
# 1. Registrar usuario
curl -X POST http://localhost:5000/api/usuarios/registro-final \
  -H "Content-Type: application/json" \
  -d '{"tipoDocumento":"CC","numeroDocumento":"1099001234","primerNombre":"Carlos",
       "primerApellido":"Gomez","celular":"3001112233","email":"carlos@demo.com",
       "usuario":"carlos_demo","password":"Demo@2024!","idUnidadNegocio":1}'

# 2. Login — copiar el campo data.token
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"usuario":"carlos_demo","password":"Demo@2024!"}'

# 3. Usar el token en llamadas protegidas
curl http://localhost:5000/api/wallets/persona/1 \
  -H "Authorization: Bearer <token>"
```

En Swagger UI (`/swagger`), hacer clic en **Authorize** e ingresar el token JWT para probar todos los endpoints protegidos directamente desde el navegador.

---

## Endpoints protegidos principales (requieren `Authorization: Bearer {token}`)

### Wallets

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| `GET`  | `/api/wallets/persona/{idPersona}` | Wallet activa de un usuario |
| `GET`  | `/api/wallets/{idWallet}/saldo` | Saldo disponible |
| `GET`  | `/api/wallets/{idWallet}/movimientos` | Historial de movimientos |
| `POST` | `/api/wallets/{idWallet}/recarga-manual` | Recarga manual de saldo |
| `POST` | `/api/wallets/transferencia` | Transferencia entre wallets |

### Pagos QR

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| `POST` | `/api/qr/pagar` | Pago a comercio por código QR |

### Comercios

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| `POST` | `/api/comercios/liquidar-venta-qr` | Liquidar venta QR al comercio |
| `POST` | `/api/comercios/solicitar-retiro` | Solicitar retiro de saldo del comercio |
| `POST` | `/api/comercios/retiros/confirmar-pago` | Confirmar pago de un retiro |
| `POST` | `/api/comercios/retiros/rechazar` | Rechazar un retiro pendiente |

### Reportes

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| `GET`  | `/api/reportes/wallet/{idWallet}/estado-cuenta` | Estado de cuenta de una wallet |
| `GET`  | `/api/reportes/comercios/{idComercio}/resumen` | Resumen financiero de un comercio |
| `GET`  | `/api/reportes/ledger/transaccion/{idTransaccion}` | Detalle de una transacción en el ledger |
| `GET`  | `/api/reportes/operaciones/resumen-general` | Resumen global del sistema |

---

## Configuración QA / Azure App Service

El backend acepta configuración por variables de entorno usando la notación `__` (doble guión bajo) de .NET como separador de jerarquía.

### Variables de entorno esperadas

| Variable de entorno | Equivalente en appsettings | Descripción |
|---------------------|---------------------------|-------------|
| `ConnectionStrings__XpayConnection` | `ConnectionStrings.XpayConnection` | Cadena de conexión SQL Server |
| `Jwt__Key` | `Jwt.Key` | Llave secreta HS256 (mínimo 32 caracteres) |
| `Jwt__Issuer` | `Jwt.Issuer` | Emisor del token (ej. `Xpay.Api`) |
| `Jwt__Audience` | `Jwt.Audience` | Audiencia del token (ej. `Xpay.App`) |
| `Jwt__ExpirationHours` | `Jwt.ExpirationHours` | Horas de validez del token (ej. `8`) |
| `Api__Name` | `Api.Name` | Nombre de la API en `/api/version` |
| `Api__Version` | `Api.Version` | Versión de la API en `/api/version` |
| `Cors__AllowedOrigins__0` | `Cors.AllowedOrigins[0]` | Primer origen permitido para CORS |
| `Cors__AllowedOrigins__1` | `Cors.AllowedOrigins[1]` | Segundo origen permitido para CORS |

### Ejemplo de configuración QA en Azure App Service

```
ConnectionStrings__XpayConnection = Server=xpay-sql-qa.database.windows.net;Database=XPAY_MVP_QA;User Id=xpay_app;Password=<secret>;TrustServerCertificate=True;
Jwt__Key          = <llave-secreta-qa-minimo-32-chars>
Jwt__Issuer       = Xpay.Api
Jwt__Audience     = Xpay.App
Jwt__ExpirationHours = 8
Api__Name         = XPAY API
Api__Version      = 0.1.0-mvp
Cors__AllowedOrigins__0 = http://localhost:3000
Cors__AllowedOrigins__1 = http://localhost:5173
Cors__AllowedOrigins__2 = https://xpay-frontend-qa.azurewebsites.net
```

### URLs frontend soportadas en CORS

| Ambiente | URL |
|----------|-----|
| Local React / Vite | `http://localhost:5173` |
| Local React CRA | `http://localhost:3000` |
| Local HTTPS | `https://localhost:3000`, `https://localhost:5173` |
| QA Azure (futuro) | `https://xpay-frontend-qa.azurewebsites.net` |

---

## Estado actual del MVP

| Fase | Contenido | Estado |
|------|-----------|--------|
| 1 | Registro de usuario final, login, wallet, recarga manual | Completa |
| 2 | Transferencias XPAY a XPAY entre wallets | Completa |
| 3 | Pago a comercio por código QR | Completa |
| 4 | Liquidación de ventas QR al wallet del comercio | Completa |
| 5 | Solicitud de retiro de saldo del comercio | Completa |
| 6 | Gestión de retiros: confirmar pago y rechazar | Completa |
| 7 | Consultas y reportes transaccionales (4 endpoints) | Completa |
| 8 | Seguridad JWT: `[Authorize]`, 401 sin token, roles | Completa |
| 9 | Health check, versión API, Swagger con JWT Bearer | Completa |
| 10 | CORS para frontend, configuración por ambiente, preparación QA | Completa |

---

## Despliegue QA

Para preparar y verificar un ambiente QA en Azure App Service, ver la guía completa:

**[docs/QA_DEPLOYMENT.md](docs/QA_DEPLOYMENT.md)**

Incluye arquitectura propuesta, variables de entorno, orden de despliegue, checklist de verificación y troubleshooting.

---

## Release QA Candidate

**[docs/RELEASE_QA_CANDIDATE.md](docs/RELEASE_QA_CANDIDATE.md)**

Declara formalmente que XPAY MVP QA Candidate v0.1 está listo para despliegue QA y pruebas internas controladas. Incluye alcance, fases completadas, funcionalidades incluidas y no incluidas, riesgos, prerrequisitos y criterios de aprobación.

> ⚠️ Esta versión **no es producción** y **no debe usarse con dinero real**.

---

## Runbook despliegue QA

**[docs/QA_DEPLOYMENT_RUNBOOK.md](docs/QA_DEPLOYMENT_RUNBOOK.md)**

Guía operativa paso a paso para ejecutar el despliegue real de XPAY QA Candidate v0.1 en Azure: recursos a crear, variables de entorno, orden de scripts SQL, despliegue backend y frontend, smoke test y rollback básico.

---

## Pruebas QA

| Documento | Propósito |
|-----------|-----------|
| **[docs/QA_MANUAL_TESTING.md](docs/QA_MANUAL_TESTING.md)** | 35 casos de prueba, prerrequisitos, orden de ejecución, criterios de aprobación y checklist de salida |
| **[docs/QA_EXECUTION_TEMPLATE.md](docs/QA_EXECUTION_TEMPLATE.md)** | Plantilla para registrar cada ciclo de pruebas: responsable, estado, evidencias, bugs y acta de cierre |

---

## Validación CI

El flujo completo está validado automáticamente en GitHub Actions mediante `scripts/validate-backend.sh`.
Cada push a `main` ejecuta SQL Server en Docker, aplica las 7 migraciones, corre el backend y valida todos los endpoints de Fases 1 a 10.
