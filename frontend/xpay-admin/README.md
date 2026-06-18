# XPAY Admin — Frontend

Panel administrativo para el sistema de pagos XPAY.

---

## Instalación

```bash
cd frontend/xpay-admin
npm install
```

## Configuración

Copiar el archivo de ejemplo y ajustar la URL del backend:

```bash
cp .env.example .env
```

Contenido de `.env`:
```
VITE_API_BASE_URL=http://localhost:5000
```

Para apuntar a un ambiente QA o producción, cambiar el valor de `VITE_API_BASE_URL`.

## Correr en desarrollo

### Contra backend local

```bash
# .env debe contener:
# VITE_API_BASE_URL=http://localhost:5000
npm run dev
```

La app queda disponible en `http://localhost:5173`. El backend debe estar corriendo en `http://localhost:5000`.

### Contra backend QA

```bash
# Copiar el ejemplo de configuración QA y ajustar si el nombre del App Service difiere
cp .env.qa.example .env
# El archivo contiene: VITE_API_BASE_URL=https://xpay-api-qa.azurewebsites.net
npm run dev
```

El backend QA debe tener CORS configurado para aceptar `http://localhost:5173`.

## Build QA

```bash
cp .env.qa.example .env
# Verificar que VITE_API_BASE_URL apunta al backend QA correcto
npm run build
# Los archivos estáticos quedan en dist/ — desplegarlo en Azure App Service o Static Web App
```

## Build de producción / genérico

```bash
npm run build
```

Los archivos estáticos quedan en `dist/`.

## Configurar VITE_API_BASE_URL en Azure

Si se usa **Azure Static Web Apps**, definir la variable de entorno `VITE_API_BASE_URL` en:
> Portal Azure → Static Web App → Configuration → Application settings

Si se usa **Azure App Service** para el frontend (servir los estáticos con un servidor), generar el build localmente con el `.env` correcto antes de desplegar `dist/`, ya que Vite inyecta el valor en tiempo de compilación.

---

## Rutas disponibles

| Ruta | Acceso | Descripción |
|------|--------|-------------|
| `/login` | Público | Inicio de sesión |
| `/dashboard` | Protegido | Dashboard operativo: accesos rápidos, métricas generales y últimos retiros, ventas QR y transacciones ledger |
| `/wallets/listado` | Protegido | Listado de wallets con filtros (tipo, estado, persona) |
| `/wallets/:idWallet` | Protegido | Estado de cuenta de una wallet |
| `/comercios/listado` | Protegido | Listado de comercios con filtros (estado, nombre/NIT) |
| `/comercios/:idComercio` | Protegido | Resumen financiero de un comercio |
| `/ventas-qr/listado` | Protegido | Listado de ventas QR con filtros (estado, comercio, tienda, fechas) |
| `/ledger/listado` | Protegido | Listado de transacciones ledger con filtros (tipo, fechas) |
| `/ledger/:idTransaccion` | Protegido | Detalle de una transacción en el ledger |
| `/retiros` | Protegido | Búsqueda de retiros por ID |
| `/retiros/:idRetiro` | Protegido | Gestión de un retiro: consulta, confirmar pago o rechazar |
| `/retiros/listado` | Protegido | Listado de retiros con filtros (estado, comercio, fechas) |

Las rutas protegidas redirigen a `/login` si no hay sesión activa.

Para pruebas manuales completas de todas las rutas, ver **[../../docs/QA_MANUAL_TESTING.md](../../docs/QA_MANUAL_TESTING.md)**.

---

## Autenticación

1. Ir a `/login`
2. Ingresar usuario y contraseña
3. El token JWT se guarda en `localStorage` bajo la clave `xpay_token`
4. Todos los requests a endpoints protegidos incluyen `Authorization: Bearer {token}`
5. Al hacer clic en **Cerrar sesión**, el token se elimina y se redirige a `/login`
6. Si el backend responde 401, la sesión se invalida automáticamente

---

## Manejo de sesión y errores

### Sesión expirada
Si cualquier endpoint protegido responde 401, el cliente API dispara el evento `xpay:unauthorized`.
`AuthContext` lo captura, invalida el estado de sesión y redirige a `/login` vía `PrivateRoute`.
La página de login muestra: *"Tu sesión ha expirado. Inicia sesión nuevamente."*

### Identificar qué API está consumiendo el frontend
- **Login:** debajo del botón aparece `API: {VITE_API_BASE_URL}` para confirmar el backend apuntado.
- **Header (tras iniciar sesión):** el navbar muestra `API: local` en desarrollo o el hostname en QA/producción.

### Error de conexión con el backend
Si el backend no responde (red caída, URL incorrecta, CORS mal configurado), el frontend muestra:
> *"No fue posible conectar con el backend XPAY. Verifica la URL del API o la conexión."*

Pasos para diagnosticar:
1. Verificar que `VITE_API_BASE_URL` en `.env` apunta al backend correcto.
2. Confirmar que el backend está corriendo (`GET /health` debe responder).
3. En QA, confirmar que CORS incluye el origen del frontend.

### Botón Reintentar en Dashboard
Si alguna sección del dashboard falla al cargar, aparece un botón **↺ Reintentar** que relanza todos los fetches del dashboard sin recargar la página.

---

## Stack

- Vite 5
- React 18
- TypeScript 5
- React Router 6
