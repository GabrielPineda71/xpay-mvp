namespace Xpay.Api.Models;

public class RetiroComercio
{
    public long IdRetiro { get; set; }
    public long IdUnidadNegocio { get; set; }
    public long IdComercio { get; set; }
    public long IdWalletComercio { get; set; }
    public long? IdTransaccionLedger { get; set; }
    public decimal Valor { get; set; }
    public string Estado { get; set; } = "PENDIENTE";
    public string? MedioRetiro { get; set; }
    public string? Banco { get; set; }
    public string? TipoCuenta { get; set; }
    public string? NumeroCuenta { get; set; }
    public string? TitularCuenta { get; set; }
    public string? DocumentoTitular { get; set; }
    public string? Observacion { get; set; }
    public long? CreadoPor { get; set; }
    public DateTime FechaSolicitud { get; set; }
}
