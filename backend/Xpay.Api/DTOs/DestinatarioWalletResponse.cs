namespace Xpay.Api.DTOs;

// GET /api/wallets/destinatario/{idWallet} — lo justo para que el ordenante
// confirme a quién envía antes de transferir. Nunca documento, celular,
// correo ni saldo del destinatario.
public sealed record DestinatarioWalletResponse(long IdWallet, string Usuario, string? NombreVisible);
