using System.ComponentModel.DataAnnotations;

namespace Xpay.Api.DTOs;

public class SimularEstadoKycRequest
{
    [Required]
    public string Usuario   { get; set; } = string.Empty;
    [Required]
    public string EstadoKyc { get; set; } = string.Empty;
}
