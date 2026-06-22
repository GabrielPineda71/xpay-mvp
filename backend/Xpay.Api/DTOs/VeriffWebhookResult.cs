namespace Xpay.Api.DTOs;

public class VeriffWebhookResult
{
    public bool    Processed     { get; init; }
    public string? EstadoMapeado { get; init; }
    public string? SessionIdHint { get; init; }
}
