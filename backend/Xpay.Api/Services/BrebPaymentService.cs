using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// XPAY-373 — orquesta el flujo REAL de Payment Bre-B: reserva Wallet
// (primer uso real de SaldoRetenido), envío a Passport, y aplicación
// idempotente del estado resultante. Deliberadamente SEPARADO de
// BrebService (que sigue gestionando el flujo SIMULADO — Fase 64/
// XPAY-371 — sin cambios funcionales): un usuario/retiro que pasa por este
// servicio usa dinero real de la cuenta operativa QA; uno que pasa por
// BrebService.SimularRetiroAsync no llama a Passport nunca. Mismo criterio
// de separación explícita ya aplicado entre ResolverMiLlaveAsync (real) y
// SimularValidacionAsync (QA) en XPAY-371.
//
// Los métodos de este servicio NO están cubiertos por tests de integración
// con base de datos real — NINGÚN servicio de este repositorio lo está
// (confirmado en XPAY-371/372: no hay paquete EF InMemory/Sqlite
// referenciado en Xpay.Api.Tests). Toda la lógica financiera que SÍ
// requiere garantías fuertes vive en funciones PURAS ya testeadas
// exhaustivamente (WalletReservationCalculator, BrebPaymentStateMachine,
// BrebPaymentRequestBuilder) — este archivo es orquestación fina que las
// invoca dentro de transacciones con lock, validada por inspección de
// código y por analogía directa con el patrón ya maduro y en producción de
// WalletOperacionService.TransferirWalletAsync/RecargarWalletManualAsync
// (mismo WITH (UPDLOCK, ROWLOCK), misma verificación DR=CR).
public class BrebPaymentService
{
    private readonly XpayDbContext             _db;
    private readonly IPassportPaymentClient    _paymentClient;
    private readonly IConfiguration            _config;
    private readonly ILogger<BrebPaymentService> _logger;

    public BrebPaymentService(
        XpayDbContext db, IPassportPaymentClient paymentClient, IConfiguration config,
        ILogger<BrebPaymentService> logger)
    {
        _db            = db;
        _paymentClient = paymentClient;
        _config        = config;
        _logger        = logger;
    }

    // ═══════════════════════════════════════════════════════════════════
    // FASE 4/6 — Solicitar retiro real: valida llave RESUELTA REALMENTE
    // (nunca una validada sólo por simular-validacion-llave — XPAY-371
    // ResolucionVerificadaPassport), reserva Wallet atómicamente, persiste
    // el retiro con el snapshot inmutable de la resolución. NO llama
    // Passport todavía (eso es EnviarPaymentAsync) — permite que un fallo
    // de validación (llave no resuelta, resolución vencida, saldo
    // insuficiente) ocurra ANTES de reservar nada.
    // ═══════════════════════════════════════════════════════════════════
    public async Task<PassportBrebRetiro> SolicitarRetiroRealAsync(
        long idPersona, long idUsuario, decimal monto, CancellationToken cancellationToken = default)
    {
        if (monto <= 0)
            throw new InvalidOperationException("El valor del retiro debe ser mayor a cero.");

        var wallet = await _db.Wallets.FirstOrDefaultAsync(
            w => w.IdPersona == idPersona && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA",
            cancellationToken)
            ?? throw new InvalidOperationException("No se encontró wallet activa para este usuario.");

        var llave = await _db.PassportBrebLlaves.FirstOrDefaultAsync(
            l => l.IdWallet == wallet.IdWallet && l.EsActiva && l.Estado == "VALIDADA", cancellationToken)
            ?? throw new InvalidOperationException("Debes tener una llave Bre-B validada para solicitar un retiro real.");

        // FASE 4 (XPAY-373) — sólo una llave VALIDADA por una resolución
        // Passport REAL puede usarse para mover dinero real. Una llave
        // marcada VALIDADA únicamente por el botón admin QA
        // (SimularValidacionAsync) nunca puebla estos campos — ver
        // BrebKeyResolutionResponseMapper.WasResolvedByPassport.
        if (!BrebKeyResolutionResponseMapper.WasResolvedByPassport(llave))
            throw new InvalidOperationException(
                "La llave debe estar resuelta realmente vía Passport (POST /api/breb/mi-llave/resolver) antes de solicitar un retiro real.");

        var nowUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(llave.PassportResolutionId)
            || llave.PassportResolutionExpiresAtUtc is null
            || llave.PassportResolutionExpiresAtUtc <= nowUtc)
            throw new BrebPaymentRequestBuilder.ResolutionExpiradaException(
                "La resolución Passport de tu llave está vencida o ausente. Vuelve a resolverla (POST /api/breb/mi-llave/resolver) antes de solicitar el retiro.");

        await using var dbTx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Mismo patrón de lock pesimista ya probado en
            // WalletOperacionService.TransferirWalletAsync/
            // RecargarWalletManualAsync — necesario para que dos
            // solicitudes concurrentes sobre la misma wallet no lean el
            // mismo SaldoDisponible antes de que cualquiera confirme.
            var saldo = await _db.WalletSaldos
                .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {wallet.IdWallet}")
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("La wallet no tiene registro de saldo.");

            var reserva = WalletReservationCalculator.TryReserve(saldo.SaldoDisponible, saldo.SaldoRetenido, monto);
            if (!reserva.Success)
                throw new InvalidOperationException(reserva.MotivoRechazo);

            var cta210101 = await RequireCuenta(wallet.IdUnidadNegocio, "210101", "Obligación Wallet Usuarios", cancellationToken);
            var cta210204 = await RequireCuenta(wallet.IdUnidadNegocio, "210204", "Retiros Bre-B Pendientes", cancellationToken);

            var saldoDisponibleAntes = saldo.SaldoDisponible;

            var ledgerTx = new LedgerTransaccion
            {
                IdUnidadNegocio  = wallet.IdUnidadNegocio,
                TipoTransaccion  = "BREB_RETIRO_REAL_RESERVA",
                ReferenciaTipo   = "passport_breb_retiros",
                Descripcion      = "Reserva de saldo para retiro Bre-B real (payment pendiente de enviar).",
                ValorTotal       = monto,
                Estado           = "REGISTRADA",
                CreadoPor        = idUsuario,
                FechaTransaccion = nowUtc,
            };
            _db.LedgerTransacciones.Add(ledgerTx);
            await _db.SaveChangesAsync(cancellationToken);

            _db.LedgerMovimientos.AddRange(
                new LedgerMovimiento { IdTransaccionLedger = ledgerTx.IdTransaccionLedger, IdCuenta = cta210101.IdCuenta, Naturaleza = "D", Valor = monto, Concepto = "BREB_RETIRO_REAL_RESERVA", ReferenciaTipo = "wallets", ReferenciaId = wallet.IdWallet, Descripcion = $"DR 210101 — reserva retiro Bre-B real wallet #{wallet.IdWallet}", FechaMovimiento = nowUtc },
                new LedgerMovimiento { IdTransaccionLedger = ledgerTx.IdTransaccionLedger, IdCuenta = cta210204.IdCuenta, Naturaleza = "C", Valor = monto, Concepto = "BREB_RETIRO_REAL_RESERVA", ReferenciaTipo = "wallets", ReferenciaId = wallet.IdWallet, Descripcion = $"CR 210204 — retiro Bre-B real pendiente de pago wallet #{wallet.IdWallet}", FechaMovimiento = nowUtc }
            );
            _db.WalletMovimientos.Add(new WalletMovimiento
            {
                IdWallet = wallet.IdWallet, IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                TipoMovimiento = "RETIRO_BREB_REAL_RESERVA", Naturaleza = "D", Valor = monto,
                SaldoAntes = saldoDisponibleAntes, SaldoDespues = reserva.SaldoDisponibleResultante,
                Descripcion = "Reserva de saldo disponible para retiro Bre-B real (pendiente de payment).",
                Estado = "APLICADO", CreadoPor = idUsuario, FechaMovimiento = nowUtc,
            });

            saldo.SaldoDisponible    = reserva.SaldoDisponibleResultante;
            saldo.SaldoRetenido      = reserva.SaldoRetenidoResultante;
            saldo.FechaActualizacion = nowUtc;

            var referencia  = Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
            var idempotency = $"REAL-{nowUtc:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..50];

            var retiro = new PassportBrebRetiro
            {
                TipoSujeto                     = "USUARIO",
                IdUsuario                       = idUsuario,
                IdWallet                        = wallet.IdWallet,
                IdBrebLlave                     = llave.IdBrebLlave,
                Valor                           = monto,
                Moneda                          = "COP",
                Estado                          = "PENDIENTE_ENVIO_PASSPORT",
                // Snapshot INMUTABLE — ver comentario de clase en
                // PassportBrebRetiro.cs — independiente de la caché de la
                // llave, que podría refrescarse después.
                PassportResolutionId            = llave.PassportResolutionId,
                PassportResolutionExpiresAtUtc  = llave.PassportResolutionExpiresAtUtc,
                ReferenciaInterna               = referencia,
                IdempotencyKey                  = idempotency,
                FechaSolicitud                  = nowUtc,
                IdTransaccionLedger             = ledgerTx.IdTransaccionLedger,
                CreatedByUsuario                = idUsuario,
            };
            _db.PassportBrebRetiros.Add(retiro);
            await _db.SaveChangesAsync(cancellationToken);

            await AssertBalanced(ledgerTx.IdTransaccionLedger, cancellationToken);
            await dbTx.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "BREB_RETIRO_REAL_RESERVA_OK: wallet={Wallet} retiro={Retiro} ledger={Ledger}",
                wallet.IdWallet, retiro.IdBrebRetiro, ledgerTx.IdTransaccionLedger);

            return retiro;
        }
        catch { await dbTx.RollbackAsync(cancellationToken); throw; }
    }

    // ═══════════════════════════════════════════════════════════════════
    // FASE 5/6 — Envía el Payment a Passport para un retiro YA RESERVADO.
    // Separado de SolicitarRetiroRealAsync a propósito: permite que un
    // fallo de reserva (saldo insuficiente) nunca llegue siquiera a
    // intentar construir un request Passport.
    // ═══════════════════════════════════════════════════════════════════
    public async Task<PassportBrebRetiro> EnviarPaymentAsync(
        long idBrebRetiro, CancellationToken cancellationToken = default)
    {
        var retiro = await _db.PassportBrebRetiros.FindAsync([idBrebRetiro], cancellationToken)
            ?? throw new InvalidOperationException($"Retiro {idBrebRetiro} no encontrado.");
        if (retiro.Estado != "PENDIENTE_ENVIO_PASSPORT")
            throw new InvalidOperationException(
                $"Sólo se puede enviar a Passport un retiro PENDIENTE_ENVIO_PASSPORT. Estado actual: {retiro.Estado}.");

        var operationalAccountId = _config[PassportOptions.EnvOperationalAccountId];
        var nowUtc = DateTime.UtcNow;

        // FASE 3/6 — construcción y validación PURA (resolución vigente,
        // account_id configurado, monto válido) ANTES de cualquier llamada
        // HTTP. Cualquier fallo aquí es un fallo LOCAL — Passport nunca
        // pudo haber recibido nada — es seguro liberar la reserva
        // (FASE 5: "fallo antes de que Passport pueda haber aceptado el
        // payment").
        PassportCreatePaymentRequest request;
        try
        {
            request = BrebPaymentRequestBuilder.Build(retiro, operationalAccountId, nowUtc);
        }
        catch (Exception ex) when (ex is PassportConfigurationException or InvalidOperationException)
        {
            await LiberarPorFalloLocalAsync(retiro.IdBrebRetiro, ex.Message, cancellationToken);
            throw;
        }

        // FASE 5 — se marca ENVIADO_PASSPORT y se hace COMMIT ANTES de la
        // llamada HTTP (no dentro de la misma transacción que la
        // llamada): si el proceso falla/se reinicia entre el commit y la
        // respuesta HTTP, el retiro queda correctamente varado en
        // ENVIADO_PASSPORT (estado indeterminado) en vez de revertir a un
        // estado que permitiría un segundo intento y un posible doble
        // envío del mismo payment. resolution_id ya garantiza
        // documentalmente (XPAY-372/373) que un reintento con el MISMO
        // resolution_id nunca crea un segundo payment — pero esta marca
        // local es una segunda capa de defensa, no depende únicamente de
        // esa garantía de Passport.
        retiro.Estado             = "ENVIADO_PASSPORT";
        retiro.FechaEnvioPassport = nowUtc;
        await _db.SaveChangesAsync(cancellationToken);

        PassportPaymentResponse response;
        try
        {
            response = await _paymentClient.CreateBrebPaymentAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Xpay.Api.Integrations.Passport.PassportException)
        {
            // FASE 5 — INCERTIDUMBRE: no sabemos si Passport creó el
            // payment (timeout, error 4xx/5xx, o fallo de protocolo).
            // Deliberadamente NO se libera la reserva ni se marca ERROR/
            // RECHAZADO aquí — el retiro permanece en ENVIADO_PASSPORT
            // (ya committeado arriba) hasta que una consulta explícita
            // (ConsultarEstadoAsync, FASE 10) o un futuro webhook (FASE 11)
            // aporte una respuesta DEFINITIVA de Passport. Ver razonamiento
            // completo en el comentario de clase.
            _logger.LogWarning(
                "BREB_RETIRO_REAL_ENVIO_INCIERTO: retiro={Retiro} tipo={Tipo} — permanece ENVIADO_PASSPORT hasta reconciliar.",
                retiro.IdBrebRetiro, ex.GetType().Name);
            return retiro;
        }

        // Éxito de transporte — aplicar el estado devuelto vía la MISMA
        // función idempotente que usará el polling/webhook futuro
        // (FASE 11).
        return await ApplyPassportPaymentStatusAsync(
            retiro.IdBrebRetiro, response.Id, response.Status, response.Error, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════
    // FASE 10 — consulta GET /v1/payments/{payment_id} y aplica el estado
    // resultante vía la misma función idempotente. NO ejecuta red real en
    // XPAY-373 (el propio IPassportPaymentClient real requiere
    // PASSPORT_ACCOUNT_ID/PASSPORT_BASE_URL configurados, ausentes en todo
    // ambiente hoy).
    // ═══════════════════════════════════════════════════════════════════
    public async Task<PassportBrebRetiro> ConsultarEstadoAsync(
        long idBrebRetiro, CancellationToken cancellationToken = default)
    {
        var retiro = await _db.PassportBrebRetiros.FindAsync([idBrebRetiro], cancellationToken)
            ?? throw new InvalidOperationException($"Retiro {idBrebRetiro} no encontrado.");
        if (string.IsNullOrWhiteSpace(retiro.PassportPaymentId))
            throw new InvalidOperationException("El retiro todavía no tiene un payment_id de Passport — no hay nada que consultar.");

        var response = await _paymentClient.RetrievePaymentAsync(retiro.PassportPaymentId, cancellationToken).ConfigureAwait(false);
        return await ApplyPassportPaymentStatusAsync(
            retiro.IdBrebRetiro, response.Id, response.Status, response.Error, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════
    // FASE 7/8/9/11 — ÚNICA función que aplica un estado Payment de
    // Passport a un retiro, reutilizable por EnviarPaymentAsync (síncrono),
    // ConsultarEstadoAsync (polling) y, en el futuro, un webhook handler —
    // sin duplicar lógica financiera (FASE 11). Idempotente por
    // construcción: relee el retiro y decide con
    // BrebPaymentStateMachine.Decide, que hace NO-OP si el retiro YA está
    // en un estado local terminal (LIQUIDADO/RECHAZADO) — procesar el
    // mismo SETTLED o REJECTED dos veces nunca vuelve a mover dinero.
    // ═══════════════════════════════════════════════════════════════════
    public async Task<PassportBrebRetiro> ApplyPassportPaymentStatusAsync(
        long idBrebRetiro, string? passportPaymentId, string? passportStatusRaw,
        PassportPaymentErrorResponse? error, CancellationToken cancellationToken = default)
    {
        await using var dbTx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Relectura fresca DENTRO de la transacción — condición
            // necesaria para que la idempotencia sea real ante llamadas
            // concurrentes/repetidas (polling + webhook llegando casi al
            // mismo tiempo).
            var retiro = await _db.PassportBrebRetiros.FindAsync([idBrebRetiro], cancellationToken)
                ?? throw new InvalidOperationException($"Retiro {idBrebRetiro} no encontrado.");

            if (!string.IsNullOrWhiteSpace(passportPaymentId) && string.IsNullOrWhiteSpace(retiro.PassportPaymentId))
                retiro.PassportPaymentId = passportPaymentId;

            var clase  = BrebPaymentStateMachine.Classify(passportStatusRaw);
            var accion = BrebPaymentStateMachine.Decide(retiro.Estado, clase);
            var nowUtc = DateTime.UtcNow;

            switch (accion)
            {
                case BrebPaymentStateMachine.RetiroTransitionAction.NoOpYaFinalizado:
                    _logger.LogInformation(
                        "BREB_PAYMENT_STATUS_NOOP: retiro={Retiro} yaFinalizadoComo={Estado} statusRecibido={Status}",
                        retiro.IdBrebRetiro, retiro.Estado, passportStatusRaw ?? "(null)");
                    break;

                case BrebPaymentStateMachine.RetiroTransitionAction.MantenerRetenido:
                    // FASE 9 — nada que mover; sólo se actualizó
                    // PassportPaymentId arriba si era la primera vez que
                    // se conocía.
                    _logger.LogInformation(
                        "BREB_PAYMENT_STATUS_TRANSITORIO: retiro={Retiro} status={Status} clase={Clase}",
                        retiro.IdBrebRetiro, passportStatusRaw ?? "(null)", clase);
                    break;

                case BrebPaymentStateMachine.RetiroTransitionAction.FinalizarLiquidado:
                    await AplicarLiquidacionAsync(retiro, nowUtc, cancellationToken);
                    break;

                case BrebPaymentStateMachine.RetiroTransitionAction.LiberarRechazado:
                    var motivo = error is not null
                        ? $"Passport error_code={error.ErrorCode ?? "(none)"}"
                        : "Payment REJECTED por Passport.";
                    await AplicarRechazoAsync(retiro, motivo, nowUtc, cancellationToken);
                    break;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await dbTx.CommitAsync(cancellationToken);
            return retiro;
        }
        catch { await dbTx.RollbackAsync(cancellationToken); throw; }
    }

    // ── Helpers privados ──────────────────────────────────────────────────

    // FASE 7 — SETTLED: SaldoRetenido baja; SaldoDisponible NUNCA vuelve a
    // tocarse aquí (ya bajó en la reserva). DR 210204 / CR 110102 — mismas
    // cuentas y misma dirección que BrebService.LiquidarRetiroAsync (Fase
    // 64), aplicadas ahora automáticamente en vez de por click de admin.
    private async Task AplicarLiquidacionAsync(PassportBrebRetiro retiro, DateTime nowUtc, CancellationToken ct)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == retiro.IdWallet, ct)
            ?? throw new InvalidOperationException("Wallet no encontrada.");
        var saldo = await _db.WalletSaldos
            .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {retiro.IdWallet}")
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Saldo de wallet no encontrado.");

        var cta210204 = await RequireCuenta(wallet.IdUnidadNegocio, "210204", "Retiros Bre-B Pendientes", ct);
        var cta110102 = await RequireCuenta(wallet.IdUnidadNegocio, "110102", "Banco Coopcentral XPAY", ct);

        var settle = WalletReservationCalculator.Settle(saldo.SaldoRetenido, retiro.Valor);
        var saldoRetenidoAntes = saldo.SaldoRetenido;
        saldo.SaldoRetenido      = settle.SaldoRetenidoResultante;
        saldo.FechaActualizacion = nowUtc;

        var ledgerTx = new LedgerTransaccion
        {
            IdUnidadNegocio = wallet.IdUnidadNegocio, TipoTransaccion = "BREB_RETIRO_REAL_LIQUIDAR",
            ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro,
            Descripcion = $"Liquidación (SETTLED) retiro Bre-B real #{retiro.IdBrebRetiro}.",
            ValorTotal = retiro.Valor, Estado = "REGISTRADA", CreadoPor = retiro.CreatedByUsuario ?? 0, FechaTransaccion = nowUtc,
        };
        _db.LedgerTransacciones.Add(ledgerTx);
        await _db.SaveChangesAsync(ct);

        _db.LedgerMovimientos.AddRange(
            new LedgerMovimiento { IdTransaccionLedger = ledgerTx.IdTransaccionLedger, IdCuenta = cta210204.IdCuenta, Naturaleza = "D", Valor = retiro.Valor, Concepto = "BREB_RETIRO_REAL_LIQUIDAR", ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro, Descripcion = "DR 210204 — liquidación (SETTLED) retiro Bre-B real", FechaMovimiento = nowUtc },
            new LedgerMovimiento { IdTransaccionLedger = ledgerTx.IdTransaccionLedger, IdCuenta = cta110102.IdCuenta, Naturaleza = "C", Valor = retiro.Valor, Concepto = "BREB_RETIRO_REAL_LIQUIDAR", ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro, Descripcion = "CR 110102 — salida real Banco Coopcentral XPAY", FechaMovimiento = nowUtc }
        );
        _db.WalletMovimientos.Add(new WalletMovimiento
        {
            IdWallet = retiro.IdWallet, IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
            TipoMovimiento = "RETIRO_BREB_REAL_LIQUIDADO", Naturaleza = "D", Valor = retiro.Valor,
            SaldoAntes = saldoRetenidoAntes, SaldoDespues = settle.SaldoRetenidoResultante,
            Descripcion = $"Liquidación (SETTLED) retiro Bre-B real #{retiro.IdBrebRetiro} — saldo RETENIDO.",
            ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro,
            Estado = "APLICADO", CreadoPor = retiro.CreatedByUsuario, FechaMovimiento = nowUtc,
        });

        retiro.Estado           = BrebPaymentStateMachine.RetiroEstadoLiquidado;
        retiro.FechaLiquidacion = nowUtc;
        retiro.IdTransaccionLedger = ledgerTx.IdTransaccionLedger;

        await AssertBalanced(ledgerTx.IdTransaccionLedger, ct);
        _logger.LogInformation("BREB_RETIRO_REAL_LIQUIDADO: retiro={Retiro} ledger={Ledger}", retiro.IdBrebRetiro, ledgerTx.IdTransaccionLedger);
    }

    // FASE 8 — REJECTED: libera reserva íntegra (SaldoRetenido↓,
    // SaldoDisponible↑). DR 210204 / CR 210101 — mismas cuentas que la
    // reversión CONFIRMADO→RECHAZADO de BrebService.RechazarRetiroAsync.
    private async Task AplicarRechazoAsync(PassportBrebRetiro retiro, string motivo, DateTime nowUtc, CancellationToken ct)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == retiro.IdWallet, ct)
            ?? throw new InvalidOperationException("Wallet no encontrada.");
        var saldo = await _db.WalletSaldos
            .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {retiro.IdWallet}")
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Saldo de wallet no encontrado.");

        var cta210204 = await RequireCuenta(wallet.IdUnidadNegocio, "210204", "Retiros Bre-B Pendientes", ct);
        var cta210101 = await RequireCuenta(wallet.IdUnidadNegocio, "210101", "Obligación Wallet Usuarios", ct);

        var release = WalletReservationCalculator.Release(saldo.SaldoDisponible, saldo.SaldoRetenido, retiro.Valor);
        var saldoDisponibleAntes = saldo.SaldoDisponible;
        saldo.SaldoDisponible    = release.SaldoDisponibleResultante;
        saldo.SaldoRetenido      = release.SaldoRetenidoResultante;
        saldo.FechaActualizacion = nowUtc;

        var ledgerTx = new LedgerTransaccion
        {
            IdUnidadNegocio = wallet.IdUnidadNegocio, TipoTransaccion = "BREB_RETIRO_REAL_RECHAZAR",
            ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro,
            Descripcion = $"Rechazo (REJECTED) retiro Bre-B real #{retiro.IdBrebRetiro}.",
            ValorTotal = retiro.Valor, Estado = "REGISTRADA", CreadoPor = retiro.CreatedByUsuario ?? 0, FechaTransaccion = nowUtc,
        };
        _db.LedgerTransacciones.Add(ledgerTx);
        await _db.SaveChangesAsync(ct);

        _db.LedgerMovimientos.AddRange(
            new LedgerMovimiento { IdTransaccionLedger = ledgerTx.IdTransaccionLedger, IdCuenta = cta210204.IdCuenta, Naturaleza = "D", Valor = retiro.Valor, Concepto = "BREB_RETIRO_REAL_RECHAZAR", ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro, Descripcion = "DR 210204 — reverso retiro Bre-B real rechazado", FechaMovimiento = nowUtc },
            new LedgerMovimiento { IdTransaccionLedger = ledgerTx.IdTransaccionLedger, IdCuenta = cta210101.IdCuenta, Naturaleza = "C", Valor = retiro.Valor, Concepto = "BREB_RETIRO_REAL_RECHAZAR", ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro, Descripcion = "CR 210101 — devolución obligación wallet", FechaMovimiento = nowUtc }
        );
        _db.WalletMovimientos.Add(new WalletMovimiento
        {
            IdWallet = retiro.IdWallet, IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
            TipoMovimiento = "RETIRO_BREB_REAL_RECHAZADO", Naturaleza = "C", Valor = retiro.Valor,
            SaldoAntes = saldoDisponibleAntes, SaldoDespues = release.SaldoDisponibleResultante,
            Descripcion = $"Rechazo (REJECTED) retiro Bre-B real #{retiro.IdBrebRetiro} — reserva liberada.",
            ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro,
            Estado = "APLICADO", CreadoPor = retiro.CreatedByUsuario, FechaMovimiento = nowUtc,
        });

        retiro.Estado          = BrebPaymentStateMachine.RetiroEstadoRechazado;
        retiro.FechaRechazo    = nowUtc;
        retiro.MotivoRechazo   = motivo;
        retiro.IdTransaccionLedger = ledgerTx.IdTransaccionLedger;

        await AssertBalanced(ledgerTx.IdTransaccionLedger, ct);
        _logger.LogInformation("BREB_RETIRO_REAL_RECHAZADO: retiro={Retiro} ledger={Ledger}", retiro.IdBrebRetiro, ledgerTx.IdTransaccionLedger);
    }

    // FASE 5 — fallo LOCAL antes de cualquier HTTP (config ausente,
    // resolución vencida) — libera la reserva de forma segura porque
    // Passport nunca pudo haber recibido nada. Reutiliza el mismo cálculo
    // puro que un REJECTED real, pero el retiro queda en ERROR (estado ya
    // permitido por el CHECK constraint desde Fase 64), NUNCA en RECHAZADO
    // — RECHAZADO se reserva exclusivamente para un REJECTED confirmado
    // POR Passport (FASE 5: "no inventar un FAILED local que pueda ocultar
    // un payment creado" — aquí no hay ambigüedad posible, así que ERROR
    // es honesto: nunca se intentó el envío).
    private async Task LiberarPorFalloLocalAsync(long idBrebRetiro, string motivo, CancellationToken ct)
    {
        await using var dbTx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var retiro = await _db.PassportBrebRetiros.FindAsync([idBrebRetiro], ct)
                ?? throw new InvalidOperationException($"Retiro {idBrebRetiro} no encontrado.");
            // Defensa en profundidad — sólo se libera un retiro que
            // realmente nunca fue marcado ENVIADO_PASSPORT.
            if (retiro.Estado != "PENDIENTE_ENVIO_PASSPORT")
            {
                await dbTx.RollbackAsync(ct);
                return;
            }

            var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.IdWallet == retiro.IdWallet, ct)
                ?? throw new InvalidOperationException("Wallet no encontrada.");
            var saldo = await _db.WalletSaldos
                .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {retiro.IdWallet}")
                .FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("Saldo de wallet no encontrado.");

            var cta210204 = await RequireCuenta(wallet.IdUnidadNegocio, "210204", "Retiros Bre-B Pendientes", ct);
            var cta210101 = await RequireCuenta(wallet.IdUnidadNegocio, "210101", "Obligación Wallet Usuarios", ct);

            var release = WalletReservationCalculator.Release(saldo.SaldoDisponible, saldo.SaldoRetenido, retiro.Valor);
            var saldoDisponibleAntes = saldo.SaldoDisponible;
            saldo.SaldoDisponible    = release.SaldoDisponibleResultante;
            saldo.SaldoRetenido      = release.SaldoRetenidoResultante;
            var nowUtc = DateTime.UtcNow;
            saldo.FechaActualizacion = nowUtc;

            var ledgerTx = new LedgerTransaccion
            {
                IdUnidadNegocio = wallet.IdUnidadNegocio, TipoTransaccion = "BREB_RETIRO_REAL_ERROR_LOCAL",
                ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro,
                Descripcion = $"Reverso por fallo LOCAL antes de enviar a Passport: {motivo}",
                ValorTotal = retiro.Valor, Estado = "REGISTRADA", CreadoPor = retiro.CreatedByUsuario ?? 0, FechaTransaccion = nowUtc,
            };
            _db.LedgerTransacciones.Add(ledgerTx);
            await _db.SaveChangesAsync(ct);

            _db.LedgerMovimientos.AddRange(
                new LedgerMovimiento { IdTransaccionLedger = ledgerTx.IdTransaccionLedger, IdCuenta = cta210204.IdCuenta, Naturaleza = "D", Valor = retiro.Valor, Concepto = "BREB_RETIRO_REAL_ERROR_LOCAL", ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro, Descripcion = "DR 210204 — reverso por fallo local pre-envío", FechaMovimiento = nowUtc },
                new LedgerMovimiento { IdTransaccionLedger = ledgerTx.IdTransaccionLedger, IdCuenta = cta210101.IdCuenta, Naturaleza = "C", Valor = retiro.Valor, Concepto = "BREB_RETIRO_REAL_ERROR_LOCAL", ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro, Descripcion = "CR 210101 — devolución obligación wallet", FechaMovimiento = nowUtc }
            );
            _db.WalletMovimientos.Add(new WalletMovimiento
            {
                IdWallet = retiro.IdWallet, IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                TipoMovimiento = "RETIRO_BREB_REAL_ERROR_LOCAL", Naturaleza = "C", Valor = retiro.Valor,
                SaldoAntes = saldoDisponibleAntes, SaldoDespues = release.SaldoDisponibleResultante,
                Descripcion = $"Reverso por fallo local: {motivo}",
                ReferenciaTipo = "passport_breb_retiros", ReferenciaId = retiro.IdBrebRetiro,
                Estado = "APLICADO", CreadoPor = retiro.CreatedByUsuario, FechaMovimiento = nowUtc,
            });

            retiro.Estado        = "ERROR";
            retiro.MotivoRechazo = motivo;
            retiro.IdTransaccionLedger = ledgerTx.IdTransaccionLedger;

            await AssertBalanced(ledgerTx.IdTransaccionLedger, ct);
            await _db.SaveChangesAsync(ct);
            await dbTx.CommitAsync(ct);

            _logger.LogWarning("BREB_RETIRO_REAL_ERROR_LOCAL: retiro={Retiro} motivo={Motivo}", retiro.IdBrebRetiro, motivo);
        }
        catch { await dbTx.RollbackAsync(ct); throw; }
    }

    private async Task<LedgerCuenta> RequireCuenta(long idUnidadNegocio, string codigo, string nombreDescriptivo, CancellationToken ct) =>
        await _db.LedgerCuentas.FirstOrDefaultAsync(c =>
            c.IdUnidadNegocio == idUnidadNegocio && c.Codigo == codigo && c.Estado == "ACTIVA", ct)
        ?? throw new InvalidOperationException($"No existe cuenta {codigo} ({nombreDescriptivo}).");

    private async Task AssertBalanced(long idTransaccionLedger, CancellationToken ct)
    {
        var dr = await _db.LedgerMovimientos.Where(m => m.IdTransaccionLedger == idTransaccionLedger && m.Naturaleza == "D").SumAsync(m => m.Valor, ct);
        var cr = await _db.LedgerMovimientos.Where(m => m.IdTransaccionLedger == idTransaccionLedger && m.Naturaleza == "C").SumAsync(m => m.Valor, ct);
        if (dr != cr) throw new InvalidOperationException("Transacción ledger no balanceada.");
    }
}
