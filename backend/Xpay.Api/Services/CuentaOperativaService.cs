using Xpay.Api.DTOs;
using Xpay.Api.Integrations.Passport;

namespace Xpay.Api.Services;

// XPAY-372 — servicio dedicado a la CUENTA OPERATIVA de XPAY en Passport/
// Coopcentral (la fuente real del dinero Bre-B — NUNCA una cuenta bancaria
// individual de un usuario, ver PassportOptions.EnvOperationalAccountId).
//
// Deliberadamente SIN dependencia de XpayDbContext: a diferencia de
// BrebService (que gestiona Wallet/llaves/retiros, todos persistidos
// localmente), la cuenta operativa NO tiene todavía ninguna entidad local
// (XPAY-372 FASE 3 — decisión: Alternativa A, "account_id mediante
// configuración segura + consulta live", preferida sobre crear una entidad
// OperationalAccount nueva — RetrieveAccountAsync actúa como única fuente
// de verdad del saldo; no hay nada que cachear/sincronizar todavía porque
// ningún flujo local necesita ese saldo salvo esta consulta admin
// read-only). Esta separación de XpayDbContext es también lo que permite
// testear este servicio completo con fakes/mocks, sin la limitación ya
// documentada en XPAY-371 (ningún test de este repositorio construye un
// XpayDbContext real — ver BrebKeyResolutionRequestBuilderTests.cs).
public class CuentaOperativaService
{
    private readonly IPassportCustomerAccountClient _accountClient;
    private readonly IConfiguration                 _config;
    private readonly ILogger<CuentaOperativaService> _logger;

    public CuentaOperativaService(
        IPassportCustomerAccountClient accountClient, IConfiguration config, ILogger<CuentaOperativaService> logger)
    {
        _accountClient = accountClient;
        _config        = config;
        _logger        = logger;
    }

    // GET /api/breb/admin/cuenta-operativa — el account_id se obtiene
    // EXCLUSIVAMENTE de configuración server-side (nunca del request/admin
    // que llama) — no existe ningún parámetro de entrada a este método
    // porque no hay ningún dato legítimo que el caller deba/pueda aportar.
    public async Task<CuentaOperativaResponse> ObtenerCuentaOperativaAsync(CancellationToken cancellationToken = default)
    {
        var accountId = _config[PassportOptions.EnvOperationalAccountId];
        if (string.IsNullOrWhiteSpace(accountId))
            throw new PassportConfigurationException(
                $"{PassportOptions.EnvOperationalAccountId} no está configurado.");

        var response = await _accountClient.RetrieveAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;

        _logger.LogInformation(
            "CUENTA_OPERATIVA_CONSULTA_OK: estado={Estado} currency={Currency}",
            response.Status ?? "(null)", response.AvailableBalance?.Currency ?? "(null)");

        return CuentaOperativaResponseMapper.ToSanitizedResponse(accountId, response, now);
    }
}
