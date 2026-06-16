# XPAY MVP

Este paquete ya contiene el primer backend base y los scripts SQL iniciales.

## Orden de ejecución

1. Crear base de datos SQL Server / Azure SQL llamada `XPAY_MVP`.
2. Ejecutar `database/001_security_identity.sql`.
3. Ejecutar `database/002_wallet_ledger.sql`.
4. Ajustar la cadena de conexión en `backend/Xpay.Api/appsettings.json`.
5. Ejecutar el backend:

```bash
cd backend/Xpay.Api
dotnet restore
dotnet run
```

6. Abrir Swagger y probar:

- `POST /api/usuarios/registro-final`
- `POST /api/auth/login`
- `GET /api/wallets/persona/{idPersona}`
- `GET /api/wallets/{idWallet}/saldo`
- `POST /api/wallets/{idWallet}/recarga-manual`

## Primer flujo implementado

Crear persona + usuario + rol USUARIO_FINAL + wallet + saldo + auditoría.

## Primera operación financiera implementada

Recarga manual de wallet con:

- ledger_transacciones
- ledger_movimientos
- wallet_movimientos
- wallet_saldos
- auditoría
