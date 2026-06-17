namespace Xpay.Api.DTOs;

public class SolicitarRetiroComercioRequest
{
    public long IdComercio { get; set; }
    public decimal Valor { get; set; }
    public string? MedioRetiro { get; set; }
    public string? Banco { get; set; }
    public string? TipoCuenta { get; set; }
    public string? NumeroCuenta { get; set; }
    public string? TitularCuenta { get; set; }
    public string? DocumentoTitular { get; set; }
    public string? Observacion { get; set; }
    public long? CreadoPor { get; set; }
}
