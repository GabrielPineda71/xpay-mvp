namespace Xpay.Api.DTOs;

public sealed record WalletRecargaBrebItemResponse(long IdRecarga, decimal Valor, DateTime Fecha, string Estado);

public sealed record WalletRecargaBrebQrResponse(
    long IdRecargaQr, string Referencia, string? QrCodeData, string Estado,
    IReadOnlyList<WalletRecargaBrebItemResponse> Recargas);
