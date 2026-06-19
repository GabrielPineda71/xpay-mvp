using System.Security.Claims;

namespace Xpay.Api.Services;

// Auditoría básica por logs. No registra passwords, tokens, Authorization header,
// bodies completos, cédulas, cuentas bancarias completas ni datos personales sensibles.
// No reemplaza auditoría persistente en BD ni SIEM — ver docs/PREPRODUCTION_GAPS S6.
public class AuditLogService
{
    private readonly ILogger<AuditLogService> _logger;
    private readonly bool                     _enabled;

    public AuditLogService(ILogger<AuditLogService> logger, IConfiguration config)
    {
        _logger  = logger;
        _enabled = config.GetValue("Audit:EnableAuditLogs", defaultValue: true);
    }

    public void LogLoginSuccess(HttpContext context, string usuario)
    {
        if (!_enabled) return;
        _logger.LogInformation(
            "AUDIT audit={Audit} event={Event} user={User} path={Path} method={Method} correlationId={CorrelationId}",
            true, "LOGIN_SUCCESS", usuario,
            context.Request.Path.Value, context.Request.Method,
            GetCorrelationId(context));
    }

    public void LogLoginFailure(HttpContext context, string usuario, string reason)
    {
        if (!_enabled) return;
        _logger.LogWarning(
            "AUDIT audit={Audit} event={Event} user={User} reason={Reason} path={Path} method={Method} correlationId={CorrelationId}",
            true, "LOGIN_FAILURE", usuario, reason,
            context.Request.Path.Value, context.Request.Method,
            GetCorrelationId(context));
    }

    public void LogSensitiveAction(HttpContext context, string action, object? metadata = null)
    {
        if (!_enabled) return;
        var user = context.User?.FindFirst(ClaimTypes.Name)?.Value
                ?? context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "-";
        if (metadata is null)
        {
            _logger.LogInformation(
                "AUDIT audit={Audit} event={Event} user={User} path={Path} method={Method} correlationId={CorrelationId}",
                true, action, user,
                context.Request.Path.Value, context.Request.Method,
                GetCorrelationId(context));
        }
        else
        {
            _logger.LogInformation(
                "AUDIT audit={Audit} event={Event} user={User} path={Path} method={Method} correlationId={CorrelationId} metadata={Metadata}",
                true, action, user,
                context.Request.Path.Value, context.Request.Method,
                GetCorrelationId(context), metadata);
        }
    }

    private static string GetCorrelationId(HttpContext context) =>
        context.Items.TryGetValue("CorrelationId", out var cid)
            ? cid?.ToString() ?? "-"
            : "-";
}
