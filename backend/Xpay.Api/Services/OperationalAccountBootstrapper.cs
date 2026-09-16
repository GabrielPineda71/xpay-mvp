using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// XPAY-385 FASE 3 — inicialización IDEMPOTENTE de la fila de
// cuentas_operativas (CAPA 2), ejecutada UNA VEZ al arrancar la app
// (Program.cs, después de builder.Build() y antes de app.Run()).
//
// POR QUÉ CÓDIGO Y NO UNA MIGRACIÓN SQL: el fingerprint de la cuenta
// depende de PASSPORT_ACCOUNT_ID, un valor de configuración/secreto sólo
// disponible en tiempo de ejecución de la app — una migración .sql estática
// de este repo (todas las demás, incluida 044, se ejecutan SIN parámetros)
// no tiene forma segura de recibirlo sin romper esa convención uniforme ni
// sin arriesgar que el secreto termine en un artefacto de migración. El
// director aprobó explícitamente este enfoque en XPAY-385 (ver ticket).
//
// IDEMPOTENCIA — dos capas independientes:
//   1) Verificación previa (AnyAsync) antes de insertar — evita el camino
//      feliz de crear una fila duplicada en el caso normal (un solo
//      proceso, arranques secuenciales).
//   2) La restricción UNIQUE (proveedor, institucion, moneda, ambiente) de
//      la migración 044 es la garantía REAL ante una carrera (dos
//      instancias arrancando casi simultáneamente) — SqlExceptionHelper.
//      IsUniqueViolation() atrapa ese caso como no-op, mismo patrón ya
//      usado para wallet_idempotencia en WalletOperacionService/PagoQrService.
//
// NUNCA lee ni loguea el valor de PASSPORT_ACCOUNT_ID — sólo su
// Fingerprint.Compute(...) y el NOMBRE de la variable (ConfigKeyReference).
public static class OperationalAccountBootstrapper
{
    public const string Proveedor          = "PASSPORT";
    public const string Institucion        = "COOPCENTRAL";
    public const string Moneda             = "COP";
    public const string LedgerCuentaCodigo = "110103";

    // Envoltura de seguridad: este bootstrap es una MEJORA opcional de
    // arranque, nunca un requisito para que la API sirva tráfico — si la
    // migración 044 todavía no corrió en este ambiente (tabla/columna
    // ausente) o hay cualquier otro fallo no previsto (p. ej. de
    // conectividad), NO debe tumbar el arranque completo de la app.
    public static async Task EnsureSeededAsync(
        IServiceProvider services, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureSeededCoreAsync(services, logger, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "CUENTA_OPERATIVA_BOOTSTRAP_ERROR: fallo no previsto — la app continúa arrancando sin la fila de cuentas_operativas.");
        }
    }

    private static async Task EnsureSeededCoreAsync(
        IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db     = scope.ServiceProvider.GetRequiredService<XpayDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var accountId = config[PassportOptions.EnvOperationalAccountId];
        if (string.IsNullOrWhiteSpace(accountId))
        {
            logger.LogWarning(
                "CUENTA_OPERATIVA_BOOTSTRAP_SKIP: {Key} no configurado — no se crea fila todavía.",
                PassportOptions.EnvOperationalAccountId);
            return;
        }

        var ambiente    = PassportEnvironmentClassifier.Clasificar(config[PassportOptions.EnvBaseUrl]);
        var fingerprint = Fingerprint.Compute(accountId);

        var yaExiste = await db.CuentasOperativas.AnyAsync(
            c => c.Proveedor == Proveedor && c.Institucion == Institucion
                 && c.Moneda == Moneda && c.Ambiente == ambiente,
            cancellationToken);
        if (yaExiste)
        {
            logger.LogInformation(
                "CUENTA_OPERATIVA_BOOTSTRAP_NOOP: ya existe fila para ambiente={Ambiente}.", ambiente);
            return;
        }

        var cuentaLedger = await db.LedgerCuentas.FirstOrDefaultAsync(
            c => c.Codigo == LedgerCuentaCodigo, cancellationToken);
        if (cuentaLedger is null)
        {
            logger.LogWarning(
                "CUENTA_OPERATIVA_BOOTSTRAP_SKIP: cuenta ledger {Codigo} no encontrada — ¿migración 044 pendiente?",
                LedgerCuentaCodigo);
            return;
        }

        try
        {
            db.CuentasOperativas.Add(new CuentaOperativa
            {
                Proveedor            = Proveedor,
                Institucion          = Institucion,
                Moneda               = Moneda,
                Ambiente             = ambiente,
                AccountIdFingerprint = fingerprint,
                ConfigKeyReference   = PassportOptions.EnvOperationalAccountId,
                IdCuentaLedger       = cuentaLedger.IdCuenta,
                Estado               = "ACTIVA",
                FechaCreacion        = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "CUENTA_OPERATIVA_BOOTSTRAP_OK: fila creada ambiente={Ambiente} accountFingerprint={Fingerprint}.",
                ambiente, fingerprint);
        }
        catch (Exception ex) when (SqlExceptionHelper.IsUniqueViolation(ex))
        {
            logger.LogInformation(
                "CUENTA_OPERATIVA_BOOTSTRAP_RACE: otra instancia ya creó la fila para ambiente={Ambiente} — no-op.",
                ambiente);
        }
    }
}
