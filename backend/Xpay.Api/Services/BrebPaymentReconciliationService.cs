using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// XPAY-381 — reconciliación automática server-side de retiros Bre-B reales
// que quedaron transitorios (ENVIADO_PASSPORT) esperando el estado final de
// Passport. Objetivo de producto: el usuario NO debe tener que pulsar
// "Consultar estado" para que un retiro normal finalice — hoy esa era la
// ÚNICA vía (XPAY-380 lo confirmó con evidencia de código: sin webhook, sin
// polling previo, ni server-side ni frontend).
//
// Mismo patrón robusto YA PROBADO en producción por
// CajaVencidaSchedulerService (PeriodicTimer, primera pasada inmediata al
// arrancar, scope de DI nuevo por iteración, excepción global contenida) —
// deliberadamente NO se inventa un patrón nuevo. Toda la lógica financiera
// se delega: BrebPaymentReconciliationSelector.EsCandidato (selección) y
// BrebPaymentReconciliationBatch.ProcesarLoteAsync (orquestación por
// retiro), que a su vez llama únicamente
// IPassportPaymentClient.RetrievePaymentAsync (nunca CreateBrebPaymentAsync
// — este servicio JAMÁS crea un Payment) y
// BrebPaymentService.ApplyPassportPaymentStatusAsync (la única función que
// aplica un estado — ya idempotente por diseño desde XPAY-373: relee el
// retiro dentro de una transacción y no-opea si ya está en un estado local
// terminal, seguro ante llamadas concurrentes del botón manual "Consultar
// estado" o de un futuro webhook).
//
// Nunca acepta un payment_id que no sea el ya persistido server-side por
// XPAY (retiro.PassportPaymentId) — no hay ningún parámetro de entrada
// externo en todo este archivo.
public class BrebPaymentReconciliationService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BrebPaymentReconciliationService> logger) : BackgroundService
{
    public const string EnvIntervalSeconds = "BREB_RECONCILIATION_INTERVAL_SECONDS";
    private const int DefaultIntervalSeconds = 10;
    private const int MinIntervalSeconds     = 5;

    // FASE 4 — umbral de "estancado" a partir del cual se emite un
    // LogWarning para revisión humana. Deliberadamente generoso (los
    // Payments Bre-B observados en Sandbox resuelven en segundos/minutos,
    // ver XPAY-379/380) — el objetivo es detectar una anomalía real, no
    // generar ruido. Nunca dispara ninguna acción financiera por sí mismo.
    private static readonly TimeSpan UmbralEstancado = TimeSpan.FromHours(2);

    private readonly TimeSpan _intervalo = ResolverIntervalo(configuration, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_intervalo);

        // Primera pasada inmediata — mismo criterio que
        // CajaVencidaSchedulerService: no espera el primer tick para
        // recoger, tras un reinicio/deploy, cualquier retiro que ya haya
        // quedado ENVIADO_PASSPORT con payment_id persistido (recovery sin
        // código especial: el estado vive enteramente en DB).
        await EjecutarBarridoSeguroAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await EjecutarBarridoSeguroAsync(stoppingToken);
        }
    }

    // Red de seguridad final — un fallo no previsto (p. ej. al crear el
    // scope de DI) no debe tumbar el host completo (BackgroundService
    // detiene todo el proceso por defecto si una excepción escapa de
    // ExecuteAsync). EjecutarBarridoAsync ya aísla sus propios errores por
    // retiro; esta envoltura cubre cualquier otro caso.
    private async Task EjecutarBarridoSeguroAsync(CancellationToken stoppingToken)
    {
        try
        {
            await EjecutarBarridoAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado normal del host.
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "BREB_RECONCILIACION_SCHEDULER_ERROR: fallo no previsto en el barrido — el servicio continuará en el próximo ciclo.");
        }
    }

    private async Task EjecutarBarridoAsync(CancellationToken stoppingToken)
    {
        using var scope    = scopeFactory.CreateScope();
        var db             = scope.ServiceProvider.GetRequiredService<XpayDbContext>();
        var paymentClient  = scope.ServiceProvider.GetRequiredService<IPassportPaymentClient>();
        var brebPayment    = scope.ServiceProvider.GetRequiredService<BrebPaymentService>();

        // Filtro EF grueso (misma condición de estado, sobre la columna
        // indexada) + BrebPaymentReconciliationSelector.EsCandidato en
        // memoria como segunda capa defensiva — mismas dos condiciones,
        // nunca deben diverger.
        var candidatosCrudos = await db.PassportBrebRetiros
            .Where(r => r.Estado == BrebPaymentReconciliationSelector.EstadoCandidato)
            .ToListAsync(stoppingToken);

        var candidatos = candidatosCrudos.Where(BrebPaymentReconciliationSelector.EsCandidato).ToList();

        LoguearEstancados(candidatos);

        if (candidatos.Count == 0)
        {
            logger.LogInformation("BREB_RECONCILIACION: sin retiros transitorios pendientes.");
            return;
        }

        var resultados = await BrebPaymentReconciliationBatch.ProcesarLoteAsync(
            candidatos,
            paymentClient,
            (idBrebRetiro, paymentId, status, error, ct) =>
                brebPayment.ApplyPassportPaymentStatusAsync(idBrebRetiro, paymentId, status, error, ct),
            logger,
            stoppingToken);

        var exitosos = resultados.Count(r => r.Exitoso);
        var fallidos = resultados.Count - exitosos;
        logger.LogInformation(
            "BREB_RECONCILIACION: candidatos={Candidatos} exitosos={Exitosos} fallidos={Fallidos}",
            candidatos.Count, exitosos, fallidos);
    }

    // FASE 4 — nunca convierte un retiro estancado a ningún estado final:
    // sólo advierte. El estado de Passport manda siempre.
    private void LoguearEstancados(IReadOnlyList<PassportBrebRetiro> candidatos)
    {
        var ahora = DateTime.UtcNow;
        foreach (var retiro in candidatos)
        {
            if (retiro.FechaEnvioPassport is DateTime envio && ahora - envio > UmbralEstancado)
            {
                logger.LogWarning(
                    "BREB_RECONCILIACION_ESTANCADO: retiro={Retiro} horasTranscurridas={Horas:F1}",
                    retiro.IdBrebRetiro, (ahora - envio).TotalHours);
            }
        }
    }

    private static TimeSpan ResolverIntervalo(IConfiguration configuration, ILogger logger)
    {
        var raw = configuration[EnvIntervalSeconds];
        if (string.IsNullOrWhiteSpace(raw))
            return TimeSpan.FromSeconds(DefaultIntervalSeconds);

        if (int.TryParse(raw.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            && seconds >= MinIntervalSeconds)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        logger.LogWarning(
            "BREB_RECONCILIACION_CONFIG: {Key} inválido o menor a {Min}s — usando default {Default}s.",
            EnvIntervalSeconds, MinIntervalSeconds, DefaultIntervalSeconds);
        return TimeSpan.FromSeconds(DefaultIntervalSeconds);
    }
}
