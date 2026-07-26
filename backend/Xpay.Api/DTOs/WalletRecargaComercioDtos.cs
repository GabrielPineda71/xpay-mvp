namespace Xpay.Api.DTOs;

// ── Búsqueda de usuario destino ──────────────────────────────────────────
// Documento/Celular/Correo/SaldoActual quedan enmascarados u omitidos cuando
// quien busca es CAJERO — ver WalletRecargaComercioService.BuscarUsuariosAsync.
public record BuscarUsuarioWalletDto(
    long     IdUsuario,
    string   NombreUsuario,
    string   NombreCompleto,
    string   Documento,
    string?  Celular,
    string?  Correo,
    long     IdWallet,
    decimal? SaldoActual,
    string   EstadoWallet);

// ── Recarga de Wallet en efectivo ────────────────────────────────────────
public record RecargarWalletComercioRequest(
    long    IdUsuarioWallet,
    decimal Valor,
    string  Pin,
    string? Observaciones);

// SaldoWalletAntes/Despues quedan null en la respuesta cuando el cajero que
// recarga tiene rol_comercio=CAJERO — ver WalletRecargaComercioService.RecargarWalletAsync.
public record RecargarWalletComercioResultDto(
    long     IdRecarga,
    long?    IdTransaccionLedger,
    long     IdWallet,
    long     IdUsuarioWallet,
    decimal  Valor,
    decimal? SaldoWalletAntes,
    decimal? SaldoWalletDespues,
    long     IdComercio,
    long?    IdTienda,
    long     IdUsuarioCajero,
    string   Estado,
    DateTime FechaRecarga,
    string   ComprobanteTexto);

public record RecargaComercioResumenDto(
    long     IdRecarga,
    long     IdUsuarioWallet,
    string   NombreUsuarioWallet,
    long     IdWallet,
    decimal  Valor,
    string   Estado,
    long?    IdTienda,
    long     IdUsuarioCajero,
    DateTime FechaRecarga);
