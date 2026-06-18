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

```bash
npm run dev
```

La app queda disponible en `http://localhost:5173`.

El backend debe estar corriendo en `VITE_API_BASE_URL` (por defecto `http://localhost:5000`) y tener CORS configurado para aceptar `http://localhost:5173`.

## Build de producción

```bash
npm run build
```

Los archivos estáticos quedan en `dist/`.

---

## Rutas disponibles

| Ruta | Acceso | Descripción |
|------|--------|-------------|
| `/login` | Público | Inicio de sesión |
| `/dashboard` | Protegido | Resumen general del sistema |
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

---

## Autenticación

1. Ir a `/login`
2. Ingresar usuario y contraseña
3. El token JWT se guarda en `localStorage` bajo la clave `xpay_token`
4. Todos los requests a endpoints protegidos incluyen `Authorization: Bearer {token}`
5. Al hacer clic en **Salir**, el token se elimina y se redirige a `/login`
6. Si el backend responde 401, la sesión se invalida automáticamente

---

## Stack

- Vite 5
- React 18
- TypeScript 5
- React Router 6
