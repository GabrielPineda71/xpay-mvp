namespace Xpay.Api.Models;

public class ComercioVentaQrContexto
{
    public long      IdContexto            { get; set; }
    public long      IdVentaQr             { get; set; }
    public long      IdComercioAliado      { get; set; }
    public long      IdComercioExistente   { get; set; }
    public long?     IdEstablecimiento     { get; set; }
    public long?     IdCajeroUsuario       { get; set; }
    public DateTime  CreatedAt             { get; set; }
}
