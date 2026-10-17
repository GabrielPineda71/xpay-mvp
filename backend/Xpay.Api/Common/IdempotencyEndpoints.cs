namespace Xpay.Api.Common;

// Fase 71.2-E-G: identificadores internos estables para la columna
// `endpoint` de wallet_idempotencia — nunca la URL completa ni el casing
// recibido por HTTP, y nunca un valor que el cliente pueda enviar o influir.
public static class IdempotencyEndpoints
{
    public const string WalletsTransferencia = "wallets.transferencia";
    public const string QrPagar = "qr.pagar";
    // XPAY-712 - desembolso de cupo ordinario a la wallet (AVANCE_WALLET).
    public const string CarteraAvanceWallet = "cartera.avance_wallet";
    // P0 anticipo - desembolso de anticipo de sueldo (libranza) a la wallet.
    public const string LibranzaAnticipoDesembolso = "libranza.anticipo_desembolso";
}
