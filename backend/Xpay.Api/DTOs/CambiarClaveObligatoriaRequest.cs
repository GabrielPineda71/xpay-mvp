namespace Xpay.Api.DTOs;

public class CambiarClaveObligatoriaRequest
{
    public string ClaveActual { get; set; } = string.Empty;
    public string ClaveNueva { get; set; } = string.Empty;
    public string ConfirmacionClaveNueva { get; set; } = string.Empty;
}
