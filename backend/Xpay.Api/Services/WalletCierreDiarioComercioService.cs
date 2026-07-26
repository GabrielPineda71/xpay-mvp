using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class WalletCierreDiarioComercioService(XpayDbContext db, ComercioScopeService scope, ILogger<WalletCierreDiarioComercioService> logger)
{
    private static DateOnly HoyColombia() =>
        DateOnly.FromDateTime(ColombiaTime.DesdeUtc(DateTime.UtcNow));

    private async Task<ComercioScope> RequireAdminComercioAsync(long idUsuario)
    {
        var s = await scope.RequireScopeAsync(idUsuario);
        if (s.RolComercio != "ADMIN_COMERCIO" || !s.PuedeVerTodoComercio)
            throw new UnauthorizedAccessException("Solo ADMIN_COMERCIO puede generar o previsualizar el cierre diario del comercio.");
        if (!s.IdComercioExistente.HasValue)
            throw new InvalidOperationException("Tu comercio operativo no tiene un comercio existente asociado.");
        return s;
    }

    // ── Preview en vivo (ABIERTO virtual) ────────────────────────────────
    public async Task<PreviewCierreDiarioDto> PreviewAsync(long idUsuario, DateOnly fecha)
    {
        var s = await RequireAdminComercioAsync(idUsuario);
        var idComercio = s.IdComercioExistente!.Value;

        var existente = await db.WalletCierresDiariosComercio
            .FirstOrDefaultAsync(c => c.IdComercio == idComercio && c.FechaCierre == fecha);

        if (existente is not null)
        {
            return new PreviewCierreDiarioDto(
                idComercio, fecha, YaGenerado: true, existente.IdCierre, existente.Estado,
                existente.CantidadRecargas, existente.ValorTotalRecaudado,
                existente.ValorLiquidadoAlGenerar, existente.ValorPendienteAlGenerar,
                VistaEnVivo: false,
                Mensaje: $"Ya existe un cierre para esta fecha (estado {existente.Estado}). " +
                         "Estos valores son el snapshot congelado al generarlo, no una vista en vivo.");
        }

        var candidatas = await ObtenerCandidatasParaPreviewAsync(idComercio, fecha);
        var valorTotal     = candidatas.Sum(r => r.Valor);
        var valorLiquidado = candidatas.Where(r => r.IdLiquidacionRecaudo != null).Sum(r => r.Valor);

        return new PreviewCierreDiarioDto(
            idComercio, fecha, YaGenerado: false, null, null,
            candidatas.Count, valorTotal, valorLiquidado, valorTotal - valorLiquidado,
            VistaEnVivo: true,
            Mensaje: "Vista en vivo — estos valores pueden cambiar hasta que generes el cierre. " +
                     "Al generar, se congelan como snapshot y las recargas registradas después ya no se incluyen.");
    }

    // ── Generar cierre ────────────────────────────────────────────────────
    public async Task<GenerarCierreDiarioResultDto> GenerarCierreAsync(long idUsuario, GenerarCierreDiarioRequest req)
    {
        var s = await RequireAdminComercioAsync(idUsuario);
        var idComercio = s.IdComercioExistente!.Value;

        var hoy = HoyColombia();
        if (req.Fecha > hoy)
            throw new ArgumentException("No se puede generar un cierre para una fecha futura.");
        if (req.Fecha == hoy && !req.ConfirmacionExplicita)
            throw new ArgumentException(
                "Para generar el cierre del día actual debes confirmar explícitamente. " +
                "Las recargas registradas después de este momento no quedarán incluidas en este cierre.");

        if (await db.WalletCierresDiariosComercio.AnyAsync(c => c.IdComercio == idComercio && c.FechaCierre == req.Fecha))
            throw new CierreDuplicadoException($"Ya existe un cierre para el comercio {idComercio} en la fecha {req.Fecha:yyyy-MM-dd}.");

        var comercio = await db.Comercios.FindAsync(idComercio)
            ?? throw new InvalidOperationException("Comercio no encontrado.");

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var corteUtc = DateTime.UtcNow;
            var candidatas = await ObtenerCandidatasParaGenerarAsync(idComercio, req.Fecha, corteUtc);

            if (candidatas.Count == 0)
                throw new InvalidOperationException("No hay recargas para consolidar en el comercio y fecha indicados.");

            var valorTotal     = candidatas.Sum(r => r.Valor);
            var valorLiquidado = candidatas.Where(r => r.IdLiquidacionRecaudo != null).Sum(r => r.Valor);
            var valorPendiente = valorTotal - valorLiquidado;

            var sufijo      = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var codigoUnico = $"XPAY-CIERRE-{idComercio}-{req.Fecha:yyyyMMdd}-{sufijo}";

            // Flujo autogestionado: ADMIN_COMERCIO genera y el cierre queda CERRADO
            // en la misma transacción — no existe una etapa manual de REVISADO/CERRADO
            // por XPAY para el flujo normal. Las únicas validaciones técnicas que
            // existen (fecha, duplicado, congruencia de valores, concurrencia) ya se
            // ejecutaron arriba en este mismo método; RevisarAsync/CerrarAsync nunca
            // agregaban ningún control adicional, así que no hay nada que "saltarse".
            // RevisadoPorUsuario/FechaRevision quedan NULL — no hubo revisión humana.
            var now = DateTime.UtcNow;
            var cierre = new WalletCierreDiarioComercio
            {
                IdUnidadNegocio         = comercio.IdUnidadNegocio,
                IdComercio              = idComercio,
                IdComercioAliado        = s.IdComercioAliado,
                FechaCierre             = req.Fecha,
                FechaHoraCorteUtc       = corteUtc,
                CodigoUnico             = codigoUnico,
                CantidadRecargas        = candidatas.Count,
                ValorTotalRecaudado     = valorTotal,
                ValorLiquidadoAlGenerar = valorLiquidado,
                ValorPendienteAlGenerar = valorPendiente,
                Estado                  = "CERRADO",
                GeneradoPorUsuario      = idUsuario,
                FechaGeneracion         = now,
                CerradoPorUsuario       = idUsuario,
                FechaCerrado            = now,
                CreatedAt               = now,
            };
            db.WalletCierresDiariosComercio.Add(cierre);
            await db.SaveChangesAsync();

            var detalles = candidatas.Select(r => new WalletCierreDiarioComercioDetalle
            {
                IdCierre                 = cierre.IdCierre,
                IdRecarga                = r.IdRecarga,
                Valor                    = r.Valor,
                EstabaLiquidadaAlGenerar = r.IdLiquidacionRecaudo != null,
                CreatedAt                = now,
            }).ToList();
            db.WalletCierresDiariosComercioDetalle.AddRange(detalles);

            db.Auditorias.Add(new Auditoria
            {
                IdUsuario  = idUsuario,
                Modulo     = "CIERRE_DIARIO_COMERCIO",
                Accion     = "GENERAR_Y_CERRAR_CIERRE_DIARIO_COMERCIO",
                Entidad    = "wallet_cierres_diarios_comercio",
                IdEntidad  = cierre.IdCierre.ToString(),
                Resultado  = "EXITOSO",
                Observacion =
                    $"comercio={idComercio} fechaOperativa={req.Fecha:yyyy-MM-dd} " +
                    $"cantidadRecargas={candidatas.Count} valorTotal={valorTotal} " +
                    $"valorLiquidado={valorLiquidado} valorPendiente={valorPendiente}",
                FechaEvento = now,
            });
            await db.SaveChangesAsync();

            await tx.CommitAsync();

            logger.LogInformation(
                "WALLET_CIERRE_DIARIO_GENERAR_Y_CERRAR: idCierre={IdCierre} idComercio={IdComercio} idUsuario={IdUsuario} rol={Rol} fecha={Fecha} corteUtc={CorteUtc} cantidadRecargas={Cantidad} valorTotal={ValorTotal}",
                cierre.IdCierre, idComercio, idUsuario, s.RolComercio, req.Fecha, corteUtc, candidatas.Count, valorTotal);

            var notaCorte =
                $"Se incluyeron únicamente recargas creadas hasta {ColombiaTime.FormatoTexto(corteUtc)} (momento de generación). " +
                "Recargas registradas después de esa marca no quedan incluidas en este cierre.";

            return new GenerarCierreDiarioResultDto(
                cierre.IdCierre, idComercio, req.Fecha, corteUtc, codigoUnico,
                candidatas.Count, valorTotal, valorLiquidado, valorPendiente,
                cierre.Estado, idUsuario, now, notaCorte);
        }
        catch (DbUpdateException ex) when (EsViolacionUnica(ex))
        {
            await tx.RollbackAsync();
            throw new CierreDuplicadoException($"Ya existe un cierre para el comercio {idComercio} en la fecha {req.Fecha:yyyy-MM-dd}.");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static bool EsViolacionUnica(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601);

    // ── Revisar / Cerrar — DEPRECADO, sin consumidor en el flujo normal ─────
    // Desde el flujo autogestionado, GenerarCierreAsync deja el cierre en
    // CERRADO directamente — ningún cierre nuevo queda en GENERADO, así que
    // estos métodos ya no tienen forma de alcanzarse en la operación diaria.
    // Se conservan sin modificar (no tienen otro consumidor confirmado más
    // que el frontend, que ya dejó de llamarlos) por si algún cierre histórico
    // quedó en GENERADO/REVISADO — hoy no hay ninguno en QA. Candidatos a
    // retiro definitivo en una limpieza posterior, no en este cambio.
    public async Task<TransicionCierreDiarioResultDto> RevisarAsync(long idUsuarioAdmin, long idCierre, RevisarCierreDiarioRequest req)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var cierre = await db.WalletCierresDiariosComercio
                .FromSqlInterpolated($"SELECT * FROM wallet_cierres_diarios_comercio WITH (UPDLOCK, ROWLOCK) WHERE id_cierre = {idCierre}")
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException($"Cierre {idCierre} no encontrado.");

            if (cierre.Estado != "GENERADO")
                throw new InvalidOperationException($"Solo se puede marcar REVISADO un cierre en estado GENERADO (actual: {cierre.Estado}).");

            var now = DateTime.UtcNow;
            cierre.Estado             = "REVISADO";
            cierre.RevisadoPorUsuario = idUsuarioAdmin;
            cierre.FechaRevision      = now;
            if (!string.IsNullOrWhiteSpace(req.Observaciones)) cierre.ObservacionesAdmin = req.Observaciones;
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            logger.LogInformation("WALLET_CIERRE_DIARIO_REVISAR: idCierre={IdCierre} idUsuarioAdmin={IdUsuarioAdmin} fechaRevision={Fecha}",
                idCierre, idUsuarioAdmin, now);

            return new TransicionCierreDiarioResultDto(cierre.IdCierre, cierre.Estado, idUsuarioAdmin, now);
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    public async Task<TransicionCierreDiarioResultDto> CerrarAsync(long idUsuarioAdmin, long idCierre, CerrarCierreDiarioRequest req)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var cierre = await db.WalletCierresDiariosComercio
                .FromSqlInterpolated($"SELECT * FROM wallet_cierres_diarios_comercio WITH (UPDLOCK, ROWLOCK) WHERE id_cierre = {idCierre}")
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException($"Cierre {idCierre} no encontrado.");

            if (cierre.Estado != "REVISADO")
                throw new InvalidOperationException(
                    $"Solo se puede marcar CERRADO un cierre en estado REVISADO (actual: {cierre.Estado}). No se permite saltar de GENERADO a CERRADO.");

            var now = DateTime.UtcNow;
            cierre.Estado            = "CERRADO";
            cierre.CerradoPorUsuario = idUsuarioAdmin;
            cierre.FechaCerrado      = now;
            if (!string.IsNullOrWhiteSpace(req.Observaciones)) cierre.ObservacionesAdmin = req.Observaciones;
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            logger.LogInformation("WALLET_CIERRE_DIARIO_CERRAR: idCierre={IdCierre} idUsuarioAdmin={IdUsuarioAdmin} fechaCerrado={Fecha}",
                idCierre, idUsuarioAdmin, now);

            return new TransicionCierreDiarioResultDto(cierre.IdCierre, cierre.Estado, idUsuarioAdmin, now);
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    // ── Consulta lado comercio — respeta el alcance del solicitante ─────────
    public async Task<CierreDiarioDetalleDto> GetDetalleComercioAsync(long idUsuario, long idCierre)
    {
        var s = await scope.RequireScopeAsync(idUsuario);
        if (!s.IdComercioExistente.HasValue)
            throw new InvalidOperationException("Tu comercio operativo no tiene un comercio existente asociado.");

        var cierre = await db.WalletCierresDiariosComercio.FindAsync(idCierre)
            ?? throw new KeyNotFoundException($"Cierre {idCierre} no encontrado.");
        if (cierre.IdComercio != s.IdComercioExistente.Value)
            throw new UnauthorizedAccessException("No tienes acceso a este cierre.");

        var (recargas, alcance) = await ObtenerRecargasVisiblesAsync(idCierre, s, idUsuario);

        var miParticipacion = new TotalesCierreDto(
            recargas.Count,
            recargas.Sum(r => r.Valor),
            recargas.Where(r => r.EstabaLiquidadaAlGenerar).Sum(r => r.Valor),
            recargas.Where(r => !r.EstabaLiquidadaAlGenerar).Sum(r => r.Valor));

        TotalesCierreDto? totalesComercio = s.PuedeVerTodoComercio
            ? new TotalesCierreDto(cierre.CantidadRecargas, cierre.ValorTotalRecaudado, cierre.ValorLiquidadoAlGenerar, cierre.ValorPendienteAlGenerar)
            : null;

        var comercio = await db.Comercios.FindAsync(cierre.IdComercio);

        return new CierreDiarioDetalleDto(
            cierre.IdCierre, cierre.IdComercio, comercio?.NombreComercial, cierre.FechaCierre,
            cierre.FechaHoraCorteUtc, cierre.CodigoUnico, cierre.Estado, cierre.FechaGeneracion,
            cierre.FechaRevision, cierre.FechaCerrado,
            totalesComercio, miParticipacion, alcance, recargas);
    }

    public async Task<List<CierreDiarioResumenDto>> GetMisCierresAsync(
        long idUsuario, DateOnly? desde, DateOnly? hasta, string? estado)
    {
        var s = await scope.RequireScopeAsync(idUsuario);
        if (!s.IdComercioExistente.HasValue)
            throw new InvalidOperationException("Tu comercio operativo no tiene un comercio existente asociado.");

        var q = db.WalletCierresDiariosComercio.Where(c => c.IdComercio == s.IdComercioExistente.Value);
        if (desde.HasValue) q = q.Where(c => c.FechaCierre >= desde.Value);
        if (hasta.HasValue) q = q.Where(c => c.FechaCierre <= hasta.Value);
        if (!string.IsNullOrWhiteSpace(estado)) q = q.Where(c => c.Estado == estado.Trim().ToUpperInvariant());

        var cierres = await q.OrderByDescending(c => c.FechaCierre).ToListAsync();
        var result = new List<CierreDiarioResumenDto>();
        foreach (var c in cierres)
        {
            var (recargas, alcance) = await ObtenerRecargasVisiblesAsync(c.IdCierre, s, idUsuario);
            var mi = new TotalesCierreDto(
                recargas.Count, recargas.Sum(r => r.Valor),
                recargas.Where(r => r.EstabaLiquidadaAlGenerar).Sum(r => r.Valor),
                recargas.Where(r => !r.EstabaLiquidadaAlGenerar).Sum(r => r.Valor));
            result.Add(new CierreDiarioResumenDto(c.IdCierre, c.FechaCierre, c.Estado, c.CodigoUnico, mi, alcance));
        }
        return result;
    }

    // ── Consulta lado admin XPAY — snapshot vs. situación actual ────────────
    public async Task<List<CierreDiarioAdminResumenDto>> ListarAdminAsync(
        DateOnly? desde, DateOnly? hasta, long? idComercio, string? estado)
    {
        var q = db.WalletCierresDiariosComercio.AsQueryable();
        if (desde.HasValue) q = q.Where(c => c.FechaCierre >= desde.Value);
        if (hasta.HasValue) q = q.Where(c => c.FechaCierre <= hasta.Value);
        if (idComercio.HasValue) q = q.Where(c => c.IdComercio == idComercio.Value);
        if (!string.IsNullOrWhiteSpace(estado)) q = q.Where(c => c.Estado == estado.Trim().ToUpperInvariant());

        var cierres = await q.OrderByDescending(c => c.FechaCierre).ToListAsync();
        if (cierres.Count == 0) return new List<CierreDiarioAdminResumenDto>();

        var idsComercio = cierres.Select(c => c.IdComercio).Distinct().ToList();
        var nombresComercio = await db.Comercios.Where(c => idsComercio.Contains(c.IdComercio))
            .ToDictionaryAsync(c => c.IdComercio, c => c.NombreComercial);

        var actuales = await ComputeTotalesActualesAsync(cierres.Select(c => c.IdCierre).ToList());

        return cierres.Select(c =>
        {
            var (liqActual, pendActual) = actuales.GetValueOrDefault(c.IdCierre, (c.ValorLiquidadoAlGenerar, c.ValorPendienteAlGenerar));
            return new CierreDiarioAdminResumenDto(
                c.IdCierre, c.IdComercio, nombresComercio.GetValueOrDefault(c.IdComercio), c.FechaCierre,
                c.Estado, c.CodigoUnico, c.CantidadRecargas, c.ValorTotalRecaudado,
                c.ValorLiquidadoAlGenerar, c.ValorPendienteAlGenerar, liqActual, pendActual);
        }).ToList();
    }

    public async Task<CierreDiarioAdminDetalleDto> GetDetalleAdminAsync(long idCierre)
    {
        var cierre = await db.WalletCierresDiariosComercio.FindAsync(idCierre)
            ?? throw new KeyNotFoundException($"Cierre {idCierre} no encontrado.");

        var detalle = await db.WalletCierresDiariosComercioDetalle.Where(d => d.IdCierre == idCierre).ToListAsync();
        var idsRecarga = detalle.Select(d => d.IdRecarga).ToList();
        var recargas = await db.WalletRecargasComercio.Where(r => idsRecarga.Contains(r.IdRecarga)).ToDictionaryAsync(r => r.IdRecarga);

        var idsUsuario = recargas.Values.Select(r => r.IdUsuarioCajero)
            .Concat(recargas.Values.Select(r => r.IdUsuarioWallet))
            .Concat(new[] { cierre.GeneradoPorUsuario })
            .Concat(cierre.RevisadoPorUsuario.HasValue ? new[] { cierre.RevisadoPorUsuario.Value } : Array.Empty<long>())
            .Concat(cierre.CerradoPorUsuario.HasValue ? new[] { cierre.CerradoPorUsuario.Value } : Array.Empty<long>())
            .Distinct().ToList();
        var idsTienda = recargas.Values.Where(r => r.IdTienda.HasValue).Select(r => r.IdTienda!.Value).Distinct().ToList();

        var nombresUsuario = await db.Usuarios.Where(u => idsUsuario.Contains(u.IdUsuario)).ToDictionaryAsync(u => u.IdUsuario, u => u.NombreUsuario);
        var nombresTienda = await db.ComercioEstablecimientos.Where(e => idsTienda.Contains(e.IdEstablecimiento)).ToDictionaryAsync(e => e.IdEstablecimiento, e => e.NombreEstablecimiento);
        var comercio = await db.Comercios.FindAsync(cierre.IdComercio);

        var recargasDto = detalle.Select(d =>
        {
            var r = recargas[d.IdRecarga];
            return new RecargaEnCierreDto(
                r.IdRecarga, r.IdTienda, r.IdTienda.HasValue ? nombresTienda.GetValueOrDefault(r.IdTienda.Value) : null,
                r.IdUsuarioCajero, nombresUsuario.GetValueOrDefault(r.IdUsuarioCajero),
                r.IdUsuarioWallet, nombresUsuario.GetValueOrDefault(r.IdUsuarioWallet),
                d.Valor, d.EstabaLiquidadaAlGenerar, r.FechaRecarga);
        }).OrderBy(x => x.FechaRecarga).ToList();

        var valorLiquidadoActual = recargas.Values.Where(r => r.IdLiquidacionRecaudo != null).Sum(r => r.Valor);
        var valorPendienteActual = recargas.Values.Where(r => r.IdLiquidacionRecaudo == null).Sum(r => r.Valor);

        return new CierreDiarioAdminDetalleDto(
            cierre.IdCierre, cierre.IdComercio, comercio?.NombreComercial, cierre.IdComercioAliado,
            cierre.FechaCierre, cierre.FechaHoraCorteUtc, cierre.CodigoUnico, cierre.Estado,
            cierre.CantidadRecargas, cierre.ValorTotalRecaudado, cierre.ValorLiquidadoAlGenerar, cierre.ValorPendienteAlGenerar,
            valorLiquidadoActual, valorPendienteActual,
            cierre.GeneradoPorUsuario, nombresUsuario.GetValueOrDefault(cierre.GeneradoPorUsuario), cierre.FechaGeneracion,
            cierre.RevisadoPorUsuario, cierre.RevisadoPorUsuario.HasValue ? nombresUsuario.GetValueOrDefault(cierre.RevisadoPorUsuario.Value) : null, cierre.FechaRevision,
            cierre.CerradoPorUsuario, cierre.CerradoPorUsuario.HasValue ? nombresUsuario.GetValueOrDefault(cierre.CerradoPorUsuario.Value) : null, cierre.FechaCerrado,
            cierre.ObservacionesAdmin, recargasDto);
    }

    private async Task<Dictionary<long, (decimal LiquidadoActual, decimal PendienteActual)>> ComputeTotalesActualesAsync(List<long> idsCierre)
    {
        var detalle = await db.WalletCierresDiariosComercioDetalle
            .Where(d => idsCierre.Contains(d.IdCierre))
            .ToListAsync();
        var idsRecarga = detalle.Select(d => d.IdRecarga).Distinct().ToList();
        var estadoLiquidacion = await db.WalletRecargasComercio
            .Where(r => idsRecarga.Contains(r.IdRecarga))
            .Select(r => new { r.IdRecarga, r.IdLiquidacionRecaudo })
            .ToDictionaryAsync(r => r.IdRecarga, r => r.IdLiquidacionRecaudo);

        return detalle.GroupBy(d => d.IdCierre).ToDictionary(
            g => g.Key,
            g => (
                LiquidadoActual: g.Where(d => estadoLiquidacion.GetValueOrDefault(d.IdRecarga) != null).Sum(d => d.Valor),
                PendienteActual: g.Where(d => estadoLiquidacion.GetValueOrDefault(d.IdRecarga) == null).Sum(d => d.Valor)
            ));
    }

    // ── Helpers de candidatas (recargas del comercio+fecha operativa Colombia) ──
    private async Task<List<WalletRecargaComercio>> ObtenerCandidatasParaPreviewAsync(long idComercio, DateOnly fecha)
    {
        var fechaDate = fecha.ToDateTime(TimeOnly.MinValue);
        return await db.WalletRecargasComercio
            .FromSqlInterpolated($@"
                SELECT r.* FROM wallet_recargas_comercio r
                WHERE r.id_comercio = {idComercio}
                  AND CAST(DATEADD(HOUR, {ColombiaTime.OffsetHoras}, r.fecha_recarga) AS DATE) = {fechaDate}
                  AND NOT EXISTS (
                      SELECT 1 FROM wallet_cierres_diarios_comercio_detalle d WHERE d.id_recarga = r.id_recarga
                  )")
            .ToListAsync();
    }

    private async Task<List<WalletRecargaComercio>> ObtenerCandidatasParaGenerarAsync(long idComercio, DateOnly fecha, DateTime corteUtc)
    {
        var fechaDate = fecha.ToDateTime(TimeOnly.MinValue);
        return await db.WalletRecargasComercio
            .FromSqlInterpolated($@"
                SELECT r.* FROM wallet_recargas_comercio r WITH (UPDLOCK, ROWLOCK)
                WHERE r.id_comercio = {idComercio}
                  AND CAST(DATEADD(HOUR, {ColombiaTime.OffsetHoras}, r.fecha_recarga) AS DATE) = {fechaDate}
                  AND r.created_at <= {corteUtc}
                  AND NOT EXISTS (
                      SELECT 1 FROM wallet_cierres_diarios_comercio_detalle d WHERE d.id_recarga = r.id_recarga
                  )")
            .ToListAsync();
    }

    // ── Filtrado por alcance del solicitante — un cajero solo ve sus propias
    // recargas, un admin de sede solo las de su sede, ADMIN_COMERCIO ve todo. ──
    private async Task<(List<RecargaEnCierreDto> Recargas, string Alcance)> ObtenerRecargasVisiblesAsync(
        long idCierre, ComercioScope s, long idUsuarioSolicitante)
    {
        var detalle = await db.WalletCierresDiariosComercioDetalle
            .Where(d => d.IdCierre == idCierre)
            .ToListAsync();

        var idsRecarga = detalle.Select(d => d.IdRecarga).ToList();
        var recargas = await db.WalletRecargasComercio
            .Where(r => idsRecarga.Contains(r.IdRecarga))
            .ToDictionaryAsync(r => r.IdRecarga);

        var visibles = (s.RolComercio switch
        {
            "ADMIN_COMERCIO"      => detalle,
            "ADMIN_SEDE_COMERCIO" => detalle.Where(d => recargas.TryGetValue(d.IdRecarga, out var r) && r.IdTienda == s.IdEstablecimiento).ToList(),
            "CAJERO"              => detalle.Where(d => recargas.TryGetValue(d.IdRecarga, out var r) && r.IdUsuarioCajero == idUsuarioSolicitante).ToList(),
            _                     => new List<WalletCierreDiarioComercioDetalle>(),
        });

        var alcance = s.RolComercio switch
        {
            "ADMIN_COMERCIO"      => "COMERCIO_COMPLETO",
            "ADMIN_SEDE_COMERCIO" => "SEDE",
            "CAJERO"              => "PROPIO",
            _                     => "NINGUNO",
        };

        var idsUsuario = visibles.Select(d => recargas[d.IdRecarga].IdUsuarioCajero)
            .Concat(visibles.Select(d => recargas[d.IdRecarga].IdUsuarioWallet))
            .Distinct().ToList();
        var idsTienda = visibles.Where(d => recargas[d.IdRecarga].IdTienda.HasValue)
            .Select(d => recargas[d.IdRecarga].IdTienda!.Value).Distinct().ToList();

        var nombresUsuario = await db.Usuarios.Where(u => idsUsuario.Contains(u.IdUsuario))
            .ToDictionaryAsync(u => u.IdUsuario, u => u.NombreUsuario);
        var nombresTienda = await db.ComercioEstablecimientos.Where(e => idsTienda.Contains(e.IdEstablecimiento))
            .ToDictionaryAsync(e => e.IdEstablecimiento, e => e.NombreEstablecimiento);

        var dtos = visibles.Select(d =>
        {
            var r = recargas[d.IdRecarga];
            return new RecargaEnCierreDto(
                r.IdRecarga, r.IdTienda, r.IdTienda.HasValue ? nombresTienda.GetValueOrDefault(r.IdTienda.Value) : null,
                r.IdUsuarioCajero, nombresUsuario.GetValueOrDefault(r.IdUsuarioCajero),
                r.IdUsuarioWallet, nombresUsuario.GetValueOrDefault(r.IdUsuarioWallet),
                d.Valor, d.EstabaLiquidadaAlGenerar, r.FechaRecarga);
        }).OrderBy(x => x.FechaRecarga).ToList();

        return (dtos, alcance);
    }
}
