using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// Mejora operativa pre-lanzamiento — cierre automático activo de cajas
// vencidas, en lugar de depender únicamente de la auto-sanación perezosa
// (que solo corre cuando alguien toca la caja). No implementa ninguna
// lógica de negocio nueva: reutiliza WalletCajaComercioService.EstaVencida
// y AutoSanarAsync tal cual, sin duplicar ni modificar sus cálculos —
// ninguno de los dos toca wallet_movimientos, ledger, recargas ni balances.
//
// Corre cada 15 minutos. IHostedService es Singleton; XpayDbContext y
// WalletCajaComercioService son Scoped — se crea un scope de DI por
// iteración (patrón estándar de BackgroundService en ASP.NET Core).
//
// Nota de infraestructura (no se modifica Azure desde aquí): este servicio
// solo puede ejecutarse de forma confiable si el App Service tiene
// "Always On" activado. Con Always On apagado (estado actual verificado en
// xpay-api-qa), la app puede descargarse tras ~20 min sin tráfico entrante,
// y este temporizador en memoria no correrá mientras la app esté descargada.
// Activar Always On queda pendiente como configuración de Azure a autorizar
// por separado — ver informe de cierre de esta fase.
public class CajaVencidaSchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<CajaVencidaSchedulerService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);

        // Primera pasada inmediata al arrancar — no espera 15 minutos para la
        // primera ejecución, útil también para verificar en QA que corre.
        await EjecutarBarridoSeguroAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await EjecutarBarridoSeguroAsync(stoppingToken);
        }
    }

    // Capa de seguridad adicional: ninguna excepción no prevista debe escapar
    // de ExecuteAsync — desde .NET 6, BackgroundServiceExceptionBehavior por
    // defecto es StopHost, así que una excepción sin capturar aquí tumbaría
    // la API completa, no solo este servicio. EjecutarBarridoAsync ya captura
    // sus propios errores esperables (fallo de consulta, fallo por caja); esta
    // envoltura es la red de seguridad final para cualquier otro caso no
    // anticipado (p. ej. fallo al crear el scope de DI). Se deja propagar
    // OperationCanceledException — es el apagado normal del host.
    private async Task EjecutarBarridoSeguroAsync(CancellationToken stoppingToken)
    {
        try
        {
            await EjecutarBarridoAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado normal del host — no es un error, no se registra como tal.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WALLET_CAJA_SCHEDULER_ERROR: fallo no previsto en el barrido — el servicio continuará en el próximo ciclo.");
        }
    }

    private async Task EjecutarBarridoAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db     = scope.ServiceProvider.GetRequiredService<XpayDbContext>();
        var cajaSvc = scope.ServiceProvider.GetRequiredService<WalletCajaComercioService>();

        // Solo estados no terminales — mismo universo que EstaVencida ya
        // filtra internamente; se trae ese subconjunto para no evaluar la
        // fórmula de fecha contra toda la tabla. Sin try/catch propio aquí:
        // cualquier fallo (de esta consulta o de la resolución de DI de
        // arriba) lo captura EjecutarBarridoSeguroAsync.
        var candidatas = await db.WalletCajasComercio
            .Where(c => c.Estado == "ABIERTA" || c.Estado == "EN_CUADRE")
            .ToListAsync(stoppingToken);

        var vencidas = candidatas.Where(WalletCajaComercioService.EstaVencida).ToList();
        if (vencidas.Count == 0)
        {
            logger.LogInformation("WALLET_CAJA_SCHEDULER: sin cajas vencidas encontradas.");
            return;
        }

        var cantidadCerradas = 0;
        var cantidadFallidas = 0;

        foreach (var caja in vencidas)
        {
            try
            {
                await cajaSvc.AutoSanarAsync(caja);
                cantidadCerradas++;
            }
            catch (Exception ex)
            {
                cantidadFallidas++;
                logger.LogError(ex,
                    "WALLET_CAJA_SCHEDULER_ERROR: fallo al auto-sanar idCaja={IdCaja}.", caja.IdCaja);
            }
        }

        logger.LogInformation(
            "WALLET_CAJA_SCHEDULER: encontradas={Encontradas} cerradas={Cerradas} fallidas={Fallidas}",
            vencidas.Count, cantidadCerradas, cantidadFallidas);
    }
}
