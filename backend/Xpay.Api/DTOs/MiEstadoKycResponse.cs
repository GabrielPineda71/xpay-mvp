namespace Xpay.Api.DTOs;

public class MiEstadoKycResponse
{
    public string    EstadoKyc          { get; set; } = "NO_INICIADO";
    public DateTime? FechaActualizacion { get; set; }
    public string?   SessionUrl         { get; set; }
    public string    Nota               { get; set; } = string.Empty;
}
