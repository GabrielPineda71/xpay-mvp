namespace Xpay.Api.Models;

public class WalletRecargaComercio
{
    public long     IdRecarga            { get; set; }
    public long     IdUnidadNegocio      { get; set; } = 1;
    public long     IdComercio           { get; set; }
    public long?    IdComercioAliado     { get; set; }
    public long?    IdTienda             { get; set; }
    public long     IdUsuarioCajero      { get; set; }
    public long     IdUsuarioWallet      { get; set; }
    public long     IdWallet             { get; set; }
    public long?    IdTransaccionLedger  { get; set; }
    public decimal  Valor                { get; set; }
    public string   Estado               { get; set; } = "APLICADA";
    public string   MetodoRecaudo        { get; set; } = "EFECTIVO";
    public string?  Referencia           { get; set; }
    public bool     PinValidadoQa        { get; set; }
    public decimal  SaldoWalletAntes     { get; set; }
    public decimal  SaldoWalletDespues   { get; set; }
    public string?  Observaciones        { get; set; }
    public DateTime FechaRecarga         { get; set; }
    public DateTime CreatedAt            { get; set; }
    public long?    IdLiquidacionRecaudo { get; set; }
    public DateTime? FechaLiquidacion    { get; set; }
    public long?    LiquidadoPorUsuario  { get; set; }
}
