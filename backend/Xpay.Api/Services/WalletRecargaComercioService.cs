using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// Fase 70.4-C: además de ComercioScopeService (usado por BuscarUsuariosAsync/
// GetMisRecargasAsync, sin cambios), recibe WalletCajaComercioService para
// reutilizar — no duplicar — ResolverScopeUnicoAsync/EstaVencida/
// AutoSanarAsync/ProyectarAsync (ver visibilidad internal agregada allí).
// Ambos servicios son Scoped (Program.cs) y comparten la misma instancia de
// XpayDbContext dentro de una request — no hay una segunda conexión.
public class WalletRecargaComercioService(
    XpayDbContext db, ComercioScopeService scope, WalletCajaComercioService cajaSvc,
    ILogger<WalletRecargaComercioService> logger)
{
    private const string CodEfectivoRecaudar  = "130107"; // Efectivo por Recaudar en Comercios (ACTIVO, D)
    private const string CodObligacionWallet  = "210101"; // Obligación Wallet Usuarios (PASIVO, C)
    private const decimal ValorMinimo = 1_000m;
    private const decimal ValorMaximo = 2_000_000m;
    private const long IdUnidadNegocio = 1;

    // ── Fecha operativa Colombia (Fase 70.4-C) — misma fuente exacta que
    // WalletCajaComercioService.HoyColombia: nunca DateTime.UtcNow.Date, nunca
    // provista por el cliente. ──────────────────────────────────────────────
    private static DateOnly HoyColombia() =>
        DateOnly.FromDateTime(ColombiaTime.DesdeUtc(DateTime.UtcNow));

    // ── Búsqueda de usuario destino ──────────────────────────────────────
    // CAJERO no puede ver saldo, celular, correo ni el documento completo del
    // cliente — solo lo mínimo para identificarlo y ejecutar la recarga.
    public async Task<List<BuscarUsuarioWalletDto>> BuscarUsuariosAsync(string? query, long idUsuarioCajero)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<BuscarUsuarioWalletDto>();
        var q = query.Trim();
        var esCajero = (await scope.RequireScopeAsync(idUsuarioCajero)).RolComercio == "CAJERO";

        var candidatos = await (
            from u in db.Usuarios
            join p in db.Personas on u.IdPersona equals p.IdPersona
            where u.NombreUsuario.Contains(q)
               || (p.NumeroDocumento != null && p.NumeroDocumento.Contains(q))
               || p.Celular.Contains(q)
               || (p.Email != null && p.Email.Contains(q))
            select new { u, p })
            .Take(10)
            .ToListAsync();

        if (candidatos.Count == 0) return new List<BuscarUsuarioWalletDto>();

        var idsPersona = candidatos.Select(c => c.p.IdPersona).ToList();
        var wallets = await db.Wallets
            .Where(w => idsPersona.Contains(w.IdPersona!.Value) && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA")
            .ToListAsync();
        var idsWallet = wallets.Select(w => w.IdWallet).ToList();
        var saldos = await db.WalletSaldos
            .Where(s => idsWallet.Contains(s.IdWallet))
            .ToDictionaryAsync(s => s.IdWallet, s => s.SaldoDisponible);

        var result = new List<BuscarUsuarioWalletDto>();
        foreach (var c in candidatos)
        {
            var wallet = wallets.FirstOrDefault(w => w.IdPersona == c.p.IdPersona);
            if (wallet is null) continue; // sin wallet PERSONA activa — no puede recibir recarga

            var nombreCompleto = string.Join(" ", new[] { c.p.PrimerNombre, c.p.SegundoNombre, c.p.PrimerApellido, c.p.SegundoApellido }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            result.Add(new BuscarUsuarioWalletDto(
                IdUsuario:     c.u.IdUsuario,
                NombreUsuario: c.u.NombreUsuario,
                NombreCompleto: nombreCompleto,
                Documento:     esCajero ? EnmascararDocumento(c.p.NumeroDocumento ?? string.Empty) : (c.p.NumeroDocumento ?? string.Empty),
                Celular:       esCajero ? null : c.p.Celular,
                Correo:        esCajero ? null : c.p.Email,
                IdWallet:      wallet.IdWallet,
                SaldoActual:   esCajero ? null : saldos.GetValueOrDefault(wallet.IdWallet, 0m),
                EstadoWallet:  wallet.Estado));
        }
        return result;
    }

    private static string EnmascararDocumento(string documento)
    {
        if (string.IsNullOrEmpty(documento)) return documento;
        var visibles = Math.Min(4, documento.Length);
        return new string('*', documento.Length - visibles) + documento[^visibles..];
    }

    // ── Recarga de Wallet en efectivo (Fase 70.4-C: exige caja propia ABIERTA) ──
    public async Task<RecargarWalletComercioResultDto> RecargarWalletAsync(RecargarWalletComercioRequest req, long idUsuarioCajero)
    {
        if (string.IsNullOrEmpty(req.Pin) || req.Pin.Length != 7 || !req.Pin.All(char.IsDigit))
            throw new ArgumentException("El PIN debe ser exactamente 7 dígitos numéricos");
        if (req.Valor < ValorMinimo)
            throw new ArgumentException($"El valor mínimo de recarga es {ValorMinimo:N0}");
        if (req.Valor > ValorMaximo)
            throw new ArgumentException($"El valor máximo de recarga por operación es {ValorMaximo:N0}");

        // Scope estricto (Fase 70.4-C) — mismo criterio que WalletCajaComercioService:
        // 0 scopes activos → 403; exactamente 1 → se resuelve; más de 1 →
        // ScopeComercioAmbiguoException (409). Ya no se usa scope.RequireScopeAsync
        // (que tomaba en silencio la primera fila ante ambigüedad).
        var cajeroScope = await cajaSvc.ResolverScopeUnicoAsync(idUsuarioCajero);

        // Allowlist positiva (decisión de diseño Fase 70.4-C): solo CAJERO y
        // ADMIN_SEDE_COMERCIO pueden recargar. ADMIN_COMERCIO no tiene sede fija
        // en su scope y el DTO no admite idEstablecimiento — queda excluido a
        // propósito, igual que cualquier otro rol no contemplado.
        if (cajeroScope.RolComercio != "CAJERO" && cajeroScope.RolComercio != "ADMIN_SEDE_COMERCIO")
            throw new UnauthorizedAccessException("Tu rol no está autorizado para recargar wallets en efectivo.");

        var idComercio = cajeroScope.IdComercioExistente
            ?? throw new InvalidOperationException("Tu comercio operativo no tiene un comercio existente asociado.");

        // Ambos roles permitidos siempre requieren sede — si faltara, es un estado
        // de configuración inválido (no se continúa), no una recarga sin caja.
        var idEstablecimiento = cajeroScope.IdEstablecimiento
            ?? throw new InvalidOperationException("Tu usuario operativo no tiene una sede asignada.");

        var hoy = HoyColombia();

        // ── Caja propia — chequeo amigable fuera de la transacción (Fase 70.4-C) ──
        // Búsqueda exacta por las 4 claves de scope del solicitante — nunca busca
        // ni reutiliza la caja de otro usuario/comercio/sede. No filtra por estado
        // aquí: EstaVencida/Estado se evalúan explícitamente a continuación, mismo
        // criterio que CorregirFondoInicialAsync/IniciarCuadreAsync/CerrarAsync.
        var caja = await db.WalletCajasComercio.AsNoTracking()
                .FirstOrDefaultAsync(c => c.IdUsuarioCajero == idUsuarioCajero && c.IdComercio == idComercio
                    && c.IdEstablecimiento == idEstablecimiento && c.FechaOperativa == hoy)
            ?? throw new CajaNoAbiertaException(
                "No tienes una caja abierta para realizar recargas en efectivo. Abre tu caja antes de continuar.");

        if (WalletCajaComercioService.EstaVencida(caja))
        {
            var cajaSaneada = await cajaSvc.AutoSanarAsync(caja);
            var cajaSaneadaDto = await cajaSvc.ProyectarAsync(cajaSaneada);
            throw new CajaVencidaException(
                "Tu caja venció y fue cerrada automáticamente — no puedes registrar más recargas.", cajaSaneadaDto);
        }

        if (caja.Estado != "ABIERTA")
            throw new TransicionCajaInvalidaException(
                $"Solo puedes registrar recargas en efectivo mientras tu caja está ABIERTA (estado actual: {caja.Estado}).");

        var idCaja = caja.IdCaja;

        RecargarWalletComercioResultDto? resultado = null;
        WalletCajaComercio? cajaVencidaBajoLock = null;

        // ── Transacción única (Fase 70.4-C) — orden obligatorio de locks: caja
        // primero (UPDLOCK/ROWLOCK), luego wallet_saldos — mismo orden en todo
        // camino de dinero que toca ambos recursos, para no invertirlo respecto a
        // IniciarCuadreAsync/CerrarAsync y reducir el riesgo de deadlock. Una sola
        // conexión, una sola transacción — AutoSanarAsync (si se necesita) solo se
        // invoca después de que esta transacción ya terminó (rollback + dispose).
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            try
            {
                var cajaLock = await db.WalletCajasComercio
                    .FromSqlInterpolated($"SELECT * FROM wallet_cajas_comercio WITH (UPDLOCK, ROWLOCK) WHERE id_caja = {idCaja}")
                    .FirstOrDefaultAsync()
                    ?? throw new CajaNoAbiertaException(
                        "No tienes una caja abierta para realizar recargas en efectivo. Abre tu caja antes de continuar.");

                if (WalletCajaComercioService.EstaVencida(cajaLock))
                {
                    // Venció justo entre el pre-chequeo y la adquisición del lock:
                    // se libera esta transacción (nada que confirmar) para que
                    // AutoSanarAsync pueda abrir la suya propia más abajo — nunca
                    // se anida una transacción dentro de otra.
                    await tx.RollbackAsync();
                    cajaVencidaBajoLock = cajaLock;
                }
                else if (cajaLock.Estado != "ABIERTA")
                {
                    throw new TransicionCajaInvalidaException(
                        $"Solo puedes registrar recargas en efectivo mientras tu caja está ABIERTA (estado actual: {cajaLock.Estado}).");
                }
                else
                {
                    var usuarioDestino = await db.Usuarios
                        .FirstOrDefaultAsync(u => u.IdUsuario == req.IdUsuarioWallet && u.Estado == "ACTIVO")
                        ?? throw new KeyNotFoundException("El usuario destino no existe o no está activo.");

                    var wallet = await db.Wallets
                        .FirstOrDefaultAsync(w => w.IdPersona == usuarioDestino.IdPersona && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA")
                        ?? throw new KeyNotFoundException("El usuario destino no tiene una Wallet activa.");

                    // Lock pesimista sobre el saldo destino — mismo patrón que las fases de Cartera Ordinaria.
                    var saldo = await db.WalletSaldos
                        .FromSqlInterpolated($"SELECT * FROM wallet_saldos WITH (UPDLOCK, ROWLOCK) WHERE id_wallet = {wallet.IdWallet}")
                        .FirstOrDefaultAsync()
                        ?? throw new InvalidOperationException("La wallet destino no tiene registro de saldo.");

                    var now = DateTime.UtcNow;
                    var saldoAntes = saldo.SaldoDisponible;

                    var ledgerTx = new LedgerTransaccion
                    {
                        IdUnidadNegocio  = IdUnidadNegocio,
                        TipoTransaccion  = "WALLET_RECARGA_EFECTIVO_COMERCIO",
                        ReferenciaTipo   = "wallet_recargas_comercio",
                        ReferenciaId     = null,
                        Descripcion      = $"Recarga en efectivo comercio #{idComercio} a wallet #{wallet.IdWallet}",
                        ValorTotal       = req.Valor,
                        Estado           = "REGISTRADA",
                        CreadoPor        = idUsuarioCajero,
                        FechaTransaccion = now,
                    };
                    db.LedgerTransacciones.Add(ledgerTx);
                    await db.SaveChangesAsync();

                    var cuentaEfectivo    = await GetCuentaLedgerAsync(CodEfectivoRecaudar);
                    var cuentaObligacion  = await GetCuentaLedgerAsync(CodObligacionWallet);

                    var movimientos = new List<LedgerMovimiento>
                    {
                        new() {
                            IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                            IdCuenta       = cuentaEfectivo.IdCuenta,
                            Naturaleza     = "D",
                            Valor          = req.Valor,
                            Concepto       = "RECARGA_EFECTIVO_COMERCIO",
                            ReferenciaTipo = "wallet_recargas_comercio",
                            ReferenciaId   = null,
                            Descripcion    = "Efectivo por recaudar en comercio — recarga de wallet.",
                            FechaMovimiento = now,
                        },
                        new() {
                            IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                            IdCuenta       = cuentaObligacion.IdCuenta,
                            Naturaleza     = "C",
                            Valor          = req.Valor,
                            Concepto       = "RECARGA_EFECTIVO_COMERCIO",
                            ReferenciaTipo = "wallet_recargas_comercio",
                            ReferenciaId   = null,
                            Descripcion    = "Obligación wallet usuario por recarga en efectivo.",
                            FechaMovimiento = now,
                        },
                    };
                    db.LedgerMovimientos.AddRange(movimientos);

                    var saldoDespues = saldoAntes + req.Valor;
                    saldo.SaldoDisponible    = saldoDespues;
                    saldo.FechaActualizacion = now;

                    var walletMovimiento = new WalletMovimiento
                    {
                        IdWallet            = wallet.IdWallet,
                        IdTransaccionLedger = ledgerTx.IdTransaccionLedger,
                        TipoMovimiento      = "RECARGA_EFECTIVO_COMERCIO",
                        Naturaleza          = "C",
                        Valor               = req.Valor,
                        SaldoAntes          = saldoAntes,
                        SaldoDespues        = saldoDespues,
                        Descripcion         = $"Recarga en efectivo — comercio #{idComercio}",
                        ReferenciaTipo      = "wallet_recargas_comercio",
                        ReferenciaId        = null,
                        Estado              = "APLICADO",
                        CreadoPor           = idUsuarioCajero,
                        FechaMovimiento     = now,
                    };
                    db.WalletMovimientos.Add(walletMovimiento);

                    var recarga = new WalletRecargaComercio
                    {
                        IdUnidadNegocio      = IdUnidadNegocio,
                        IdComercio           = idComercio,
                        IdComercioAliado     = cajeroScope.IdComercioAliado,
                        IdTienda             = cajeroScope.IdEstablecimiento,
                        IdUsuarioCajero      = idUsuarioCajero,
                        IdUsuarioWallet      = req.IdUsuarioWallet,
                        IdWallet             = wallet.IdWallet,
                        IdTransaccionLedger  = ledgerTx.IdTransaccionLedger,
                        Valor                = req.Valor,
                        Estado               = "APLICADA",
                        MetodoRecaudo        = "EFECTIVO",
                        PinValidadoQa        = true,
                        SaldoWalletAntes     = saldoAntes,
                        SaldoWalletDespues   = saldoDespues,
                        Observaciones        = req.Observaciones,
                        FechaRecarga         = now,
                        CreatedAt            = now,
                    };
                    db.WalletRecargasComercio.Add(recarga);
                    await db.SaveChangesAsync();

                    ledgerTx.ReferenciaId = recarga.IdRecarga;
                    foreach (var m in movimientos) m.ReferenciaId = recarga.IdRecarga;
                    walletMovimiento.ReferenciaId = recarga.IdRecarga;
                    await db.SaveChangesAsync();

                    var totalD = movimientos.Where(m => m.Naturaleza == "D").Sum(m => m.Valor);
                    var totalC = movimientos.Where(m => m.Naturaleza == "C").Sum(m => m.Valor);
                    if (totalD != totalC)
                        throw new InvalidOperationException($"Ledger desbalanceado: DR={totalD} CR={totalC}.");

                    // ── Vínculo con Caja/Cuadre (Fase 70.4-C) — dentro de la misma
                    // transacción, después de que la recarga ya tiene IdRecarga y
                    // antes del commit. Nunca se confirma la recarga sin su
                    // movimiento de caja, ni el movimiento sin la recarga.
                    // Chequeo defensivo (B.8): en flujo normal id_recarga es
                    // recién generado en esta misma transacción y nunca puede
                    // repetirse — si ocurriera, es una inconsistencia interna real,
                    // no un reintento idempotente exitoso.
                    var yaExisteMovimientoCaja = await db.WalletCajaMovimientos
                        .AnyAsync(m => m.IdRecarga == recarga.IdRecarga);
                    if (yaExisteMovimientoCaja)
                        throw new InvalidOperationException(
                            $"La recarga {recarga.IdRecarga} ya tiene un movimiento de caja vinculado — estado inconsistente.");

                    var observacionesMovimiento = $"Recarga en efectivo comercio #{idComercio} — recarga #{recarga.IdRecarga}.";
                    db.WalletCajaMovimientos.Add(new WalletCajaMovimiento
                    {
                        IdCaja         = idCaja,
                        IdRecarga      = recarga.IdRecarga,
                        TipoMovimiento = "RECARGA_EFECTIVO",
                        Naturaleza     = "E",
                        Valor          = req.Valor,
                        Observaciones  = observacionesMovimiento,
                        CreatedAt      = now,
                    });
                    await db.SaveChangesAsync();

                    await tx.CommitAsync();

                    // CAJERO no puede ver saldo anterior/posterior del cliente — el comprobante
                    // y la respuesta se reducen a lo mínimo: operación, valor, referencia y fecha.
                    // La fecha/hora NO se embebe como texto formateado aquí — el frontend la
                    // muestra en hora Colombia a partir del campo estructurado FechaRecarga.
                    var esCajero = cajeroScope.RolComercio == "CAJERO";
                    var comprobante = esCajero
                        ? $"Recarga de {req.Valor:N0} a {usuarioDestino.NombreUsuario} (Wallet #{wallet.IdWallet}). " +
                          $"Comercio #{idComercio}{(recarga.IdTienda.HasValue ? $", sede #{recarga.IdTienda}" : "")}, cajero #{idUsuarioCajero}. " +
                          $"Recarga #{recarga.IdRecarga}."
                        : $"Recarga de {req.Valor:N0} a {usuarioDestino.NombreUsuario} (Wallet #{wallet.IdWallet}). " +
                          $"Saldo anterior: {saldoAntes:N0}. Saldo nuevo: {saldoDespues:N0}. " +
                          $"Comercio #{idComercio}{(recarga.IdTienda.HasValue ? $", sede #{recarga.IdTienda}" : "")}, cajero #{idUsuarioCajero}. " +
                          $"Recarga #{recarga.IdRecarga}.";

                    logger.LogInformation(
                        "WALLET_RECARGA_EFECTIVO_COMERCIO: idRecarga={IdRecarga} idCaja={IdCaja} idComercio={IdComercio} idWallet={IdWallet} valor={Valor}",
                        recarga.IdRecarga, idCaja, idComercio, wallet.IdWallet, req.Valor);

                    resultado = new RecargarWalletComercioResultDto(
                        IdRecarga:           recarga.IdRecarga,
                        IdTransaccionLedger: ledgerTx.IdTransaccionLedger,
                        IdWallet:            wallet.IdWallet,
                        IdUsuarioWallet:     req.IdUsuarioWallet,
                        Valor:               req.Valor,
                        SaldoWalletAntes:    esCajero ? null : saldoAntes,
                        SaldoWalletDespues:  esCajero ? null : saldoDespues,
                        IdComercio:          idComercio,
                        IdTienda:            recarga.IdTienda,
                        IdUsuarioCajero:     idUsuarioCajero,
                        Estado:              recarga.Estado,
                        FechaRecarga:        recarga.FechaRecarga,
                        ComprobanteTexto:    comprobante);
                }
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        if (cajaVencidaBajoLock != null)
        {
            // Transacción anterior ya terminada (Rollback + Dispose del using de
            // arriba liberó el lock). AutoSanarAsync abre y confirma la suya
            // propia de forma completamente independiente.
            var cajaSaneada = await cajaSvc.AutoSanarAsync(cajaVencidaBajoLock);
            var cajaSaneadaDto = await cajaSvc.ProyectarAsync(cajaSaneada);
            throw new CajaVencidaException(
                "Tu caja venció — no puedes registrar más recargas.", cajaSaneadaDto);
        }

        return resultado!;
    }

    // ── Mis recargas ──────────────────────────────────────────────────────
    public async Task<List<RecargaComercioResumenDto>> GetMisRecargasAsync(long idUsuarioCajero, DateTime? desde, DateTime? hasta)
    {
        var s = await scope.RequireScopeAsync(idUsuarioCajero);

        var query = db.WalletRecargasComercio.AsQueryable();
        query = s.RolComercio switch
        {
            "ADMIN_COMERCIO"      => query.Where(r => r.IdComercioAliado == s.IdComercioAliado),
            "ADMIN_SEDE_COMERCIO" => query.Where(r => r.IdComercioAliado == s.IdComercioAliado && r.IdTienda == s.IdEstablecimiento),
            "CAJERO"              => query.Where(r => r.IdUsuarioCajero == idUsuarioCajero),
            _                     => query.Where(r => false),
        };

        if (desde.HasValue) query = query.Where(r => r.FechaRecarga >= desde.Value);
        if (hasta.HasValue) query = query.Where(r => r.FechaRecarga <= hasta.Value);

        var recargas = await query.OrderByDescending(r => r.FechaRecarga).ToListAsync();
        if (recargas.Count == 0) return new List<RecargaComercioResumenDto>();

        var idsUsuario = recargas.Select(r => r.IdUsuarioWallet).Distinct().ToList();
        var nombres = await db.Usuarios
            .Where(u => idsUsuario.Contains(u.IdUsuario))
            .ToDictionaryAsync(u => u.IdUsuario, u => u.NombreUsuario);

        return recargas.Select(r => new RecargaComercioResumenDto(
            IdRecarga:           r.IdRecarga,
            IdUsuarioWallet:     r.IdUsuarioWallet,
            NombreUsuarioWallet: nombres.GetValueOrDefault(r.IdUsuarioWallet, ""),
            IdWallet:            r.IdWallet,
            Valor:               r.Valor,
            Estado:              r.Estado,
            IdTienda:            r.IdTienda,
            IdUsuarioCajero:     r.IdUsuarioCajero,
            FechaRecarga:        r.FechaRecarga)).ToList();
    }

    private async Task<LedgerCuenta> GetCuentaLedgerAsync(string codigo) =>
        await db.LedgerCuentas.FirstOrDefaultAsync(c => c.IdUnidadNegocio == IdUnidadNegocio && c.Codigo == codigo && c.Estado == "ACTIVA")
        ?? throw new InvalidOperationException($"Cuenta ledger {codigo} no encontrada o inactiva");
}
