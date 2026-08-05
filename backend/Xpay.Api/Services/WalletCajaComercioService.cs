using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// Backend funcional base de Caja/Cuadre (Fase 70.4.1) — los 7 métodos
// públicos están implementados: GetCajaActualAsync, AbrirAsync,
// CorregirFondoInicialAsync, IniciarCuadreAsync, CerrarAsync, RevisarAsync,
// ListarAsync. Ningún NotImplementedException pendiente.
//
// Fase 70.4-B: AbrirAsync sincroniza con
// WalletCierreDiarioComercioService.GenerarCierreAsync mediante
// AppLockHelper (sp_getapplock, clave XPAY:CAJA_CIERRE:{idComercio}:{fecha}).
//
// Permanecen fuera de este alcance (no implementado todavía):
// - integración con recargas en efectivo de Fase 70.1
//   (WalletRecargaComercioService — ningún movimiento RECARGA_EFECTIVO se
//   crea todavía desde ningún flujo real);
// - cobertura de pruebas CI (scripts/validate-backend.sh);
// - frontend productivo;
// - auditoría persistente adicional (tabla Auditoria genérica), si
//   posteriormente se define su esquema.
//
// Clase concreta, sin interfaz — mismo patrón que el resto de los servicios
// del proyecto (ninguno usa interfaz).
public class WalletCajaComercioService(XpayDbContext db, ComercioScopeService scope, ILogger<WalletCajaComercioService> logger)
{
    // ── Estados (diseño Fase 70.4, sección 5) — única definición centralizada,
    // mismo criterio que las cuentas ledger constantes ya usadas en
    // WalletRecargaComercioService (CodEfectivoRecaudar/CodObligacionWallet). ──
    private const string EstadoAbierta                = "ABIERTA";
    private const string EstadoEnCuadre                = "EN_CUADRE";
    private const string EstadoCerrada                = "CERRADA";
    private const string EstadoConDiferencia           = "CON_DIFERENCIA";
    private const string EstadoCerradaAutomaticamente  = "CERRADA_AUTOMATICAMENTE";
    private const string EstadoRevisada                = "REVISADA";

    // ── Tipos de movimiento (diseño Fase 70.4, sección 3) — solo los 2
    // insertables en 70.4.1. OTRO_INGRESO/RETIRO/DEVOLUCION/ENTREGA_PARCIAL
    // quedan reservados para 70.4.2 — no se declaran constantes para ellos
    // todavía porque ningún código de esta fase los inserta. ──────────────
    private const string TipoMovimientoFondoInicial    = "FONDO_INICIAL";
    private const string TipoMovimientoRecargaEfectivo = "RECARGA_EFECTIVO";

    // Valor por defecto cuando comercio_establecimientos.hora_cierre_automatico_caja
    // es NULL (diseño Fase 70.4, sección 3).
    private static readonly TimeOnly HoraLimiteCierrePorDefecto = new(21, 0);

    // ── Helpers de fecha/hora Colombia (mismo patrón que
    // WalletCierreDiarioComercioService.HoyColombia) ──────────────────────
    private static DateOnly HoyColombia() =>
        DateOnly.FromDateTime(ColombiaTime.DesdeUtc(DateTime.UtcNow));

    private static TimeOnly HoraColombia() =>
        TimeOnly.FromDateTime(ColombiaTime.DesdeUtc(DateTime.UtcNow));

    // ── EstaVencida (diseño Fase 70.4, sección 9 — fórmula fecha+hora, sin >=) ──
    // caja.Estado IN (ABIERTA, EN_CUADRE) AND (
    //     FechaOperativa < HoyColombia()
    //     OR (FechaOperativa == HoyColombia() AND HoraColombia() > HoraLimiteCierre))
    private static bool EstaVencida(WalletCajaComercio caja)
    {
        if (caja.Estado != EstadoAbierta && caja.Estado != EstadoEnCuadre) return false;

        var hoy = HoyColombia();
        if (caja.FechaOperativa < hoy) return true;
        return caja.FechaOperativa == hoy && HoraColombia() > caja.HoraLimiteCierre;
    }

    // El patrón transaccional completo de dos fases (Fase A/Fase B) NO se
    // implementa en esta etapa — pertenece a iniciar-cuadre/cerrar/recargar,
    // fuera de alcance de la Etapa 4C. Aquí solo se auto-sana en lectura
    // (Capa 3 del diseño), nunca se rechaza una escritura con 409.

    // ── Resolución estricta del scope comercial (diseño Fase 70.4, sección 4) ──
    // 0 scopes activos → UnauthorizedAccessException (403); exactamente 1 →
    // se resuelve y se usa; más de 1 → ScopeComercioAmbiguoException (409).
    // No usa FirstOrDefaultAsync — cuenta explícitamente antes de resolver,
    // a diferencia de ComercioScopeService.GetScopeAsync (que sí lo hace y que
    // aquí solo se invoca una vez confirmado que existe una única fila activa).
    private async Task<ComercioScope> ResolverScopeUnicoAsync(long idUsuario)
    {
        var cantidadScopesActivos = await db.ComercioUsuarios
            .CountAsync(u => u.IdUsuario == idUsuario && u.Estado == "ACTIVO");

        if (cantidadScopesActivos == 0)
            throw new UnauthorizedAccessException(
                "El usuario no tiene acceso operativo a ningún comercio aliado activo.");

        if (cantidadScopesActivos > 1)
            throw new ScopeComercioAmbiguoException(
                "Tu cuenta tiene acceso activo a más de un comercio. Esta función todavía no soporta seleccionar el comercio — contacta a XPAY.");

        return await scope.GetScopeAsync(idUsuario)
            ?? throw new UnauthorizedAccessException(
                "El usuario no tiene acceso operativo a ningún comercio aliado activo.");
    }

    // ── Operaciones (diseño Fase 70.4, sección 6/18). Sin lógica de negocio
    // todavía (Etapa 4A es solo contratos y estructura base). ──────────────

    // ── Consultar caja actual (diseño Fase 70.4, sección 6/9) ───────────────
    // Siempre responde CajaDto?/null — nunca 409 por vencimiento (Capa 3, sección
    // 11). Busca cualquier caja ABIERTA/EN_CUADRE del usuario en el comercio del
    // scope, sin filtrar por fecha_operativa (para poder encontrar y auto-sanar
    // una caja de un día anterior que quedó abandonada — caso E2E 48 del
    // diseño). Si hay más de una caja no terminal para el mismo usuario+comercio
    // (anomalía de datos — hoy AbrirAsync no lo impide entre fechas distintas),
    // no se elige ninguna silenciosamente: se lanza una excepción explícita.
    public async Task<CajaDto?> GetCajaActualAsync(long idUsuario)
    {
        var s = await ResolverScopeUnicoAsync(idUsuario);
        var idComercio = s.IdComercioExistente
            ?? throw new InvalidOperationException("Tu comercio operativo no tiene un comercio existente asociado.");

        var candidatas = await db.WalletCajasComercio
            .AsNoTracking()
            .Where(c => c.IdUsuarioCajero == idUsuario && c.IdComercio == idComercio
                     && (c.Estado == EstadoAbierta || c.Estado == EstadoEnCuadre))
            .ToListAsync();

        if (candidatas.Count == 0) return null;

        if (candidatas.Count > 1)
            throw new InvalidOperationException(
                $"Se encontraron {candidatas.Count} cajas no terminales (ABIERTA/EN_CUADRE) para el usuario {idUsuario} en el comercio {idComercio} — estado inconsistente, requiere revisión manual.");

        var caja = candidatas[0];
        var fechaOperativaOriginal = caja.FechaOperativa;

        if (EstaVencida(caja))
        {
            caja = await AutoSanarAsync(caja);
            // La auto-sanación queda persistida siempre. Pero si la caja saneada
            // era de un día anterior, ya no es "la caja actual" del día de hoy.
            if (fechaOperativaOriginal < HoyColombia())
                return null;
        }

        return await ProyectarAsync(caja);
    }

    // ── Auto-sanación (diseño Fase 70.4, sección 9, Capa 3) ─────────────────
    // Transacción corta e independiente: lock WITH (UPDLOCK, ROWLOCK), revalida
    // EstaVencida bajo el lock (por si otro proceso ya la cerró/revisó), aplica
    // el snapshot mínimo y confirma. No implementa sp_getapplock ni el patrón de
    // dos fases completo (eso es de escritura) — esto es solo la Capa 3.
    private async Task<WalletCajaComercio> AutoSanarAsync(WalletCajaComercio cajaDetectada)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var caja = await db.WalletCajasComercio
                .FromSqlInterpolated($"SELECT * FROM wallet_cajas_comercio WITH (UPDLOCK, ROWLOCK) WHERE id_caja = {cajaDetectada.IdCaja}")
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException($"Caja {cajaDetectada.IdCaja} no encontrada.");

            // Revalidar bajo lock: otra lectura/escritura concurrente pudo haberla
            // cerrado ya — no se sobrescribe una transición ya completada.
            if (!EstaVencida(caja))
            {
                await tx.CommitAsync();
                return caja;
            }

            // EfectivoEsperado (diseño, sección 8): ABIERTA lo calcula y congela
            // en este instante; EN_CUADRE conserva el valor ya congelado al
            // iniciar cuadre, no se recalcula. La suma es sobre wallet_caja_
            // movimientos (tabla propia de 70.4) — no se toca wallet_recargas_
            // comercio ni ningún servicio de 70.1/70.2.
            decimal efectivoEsperado;
            if (caja.Estado == EstadoAbierta)
            {
                var sumaRecargas = await db.WalletCajaMovimientos
                    .Where(m => m.IdCaja == caja.IdCaja && m.TipoMovimiento == TipoMovimientoRecargaEfectivo)
                    .SumAsync(m => (decimal?)m.Valor) ?? 0m;
                efectivoEsperado = caja.FondoInicial + sumaRecargas;
            }
            else
            {
                efectivoEsperado = caja.EfectivoEsperado
                    ?? throw new InvalidOperationException($"Caja {caja.IdCaja} está EN_CUADRE sin efectivo_esperado congelado — estado inconsistente.");
            }

            var now = DateTime.UtcNow;
            caja.Estado                 = EstadoCerradaAutomaticamente;
            caja.EfectivoEsperado       = efectivoEsperado;
            caja.EfectivoContado        = null;
            caja.Diferencia             = null;
            caja.FechaCierreUtc         = now;
            caja.CerradaAutomaticamente = true;
            // ObservacionesCajero: el documento no define un texto literal para
            // el cierre automático — se deja sin modificar (no se inventa
            // contenido). No se marca REVISADA ni se asignan RevisadoPorUsuario/
            // FechaRevision. No se crean movimientos ni comprobante.

            await db.SaveChangesAsync();
            await tx.CommitAsync();

            logger.LogInformation(
                "WALLET_CAJA_CIERRE_AUTOMATICO: idCaja={IdCaja} idUsuario={IdUsuario} idComercio={IdComercio} fechaOperativa={FechaOperativa} efectivoEsperado={EfectivoEsperado}",
                caja.IdCaja, caja.IdUsuarioCajero, caja.IdComercio, caja.FechaOperativa, efectivoEsperado);

            return caja;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ── Proyección común WalletCajaComercio → CajaDto (diseño, sección 18) ──
    // Reutilizada por AbrirAsync y GetCajaActualAsync. Asume que el solicitante
    // ES el dueño de la caja — válido en ambos casos actuales (quien abre es el
    // dueño; mi-caja-actual solo busca la propia). Cuando se implemente el
    // acceso de ADMIN_SEDE_COMERCIO/ADMIN_COMERCIO a cajas de otros cajeros
    // (ListarAsync/GET por id), deberá generalizarse para recibir también al
    // solicitante y su rol.
    private async Task<CajaDto> ProyectarAsync(WalletCajaComercio caja)
    {
        var nombreComercio = await db.Comercios.AsNoTracking()
            .Where(c => c.IdComercio == caja.IdComercio)
            .Select(c => c.NombreComercial)
            .FirstOrDefaultAsync();

        var nombreEstablecimiento = await db.ComercioEstablecimientos.AsNoTracking()
            .Where(e => e.IdEstablecimiento == caja.IdEstablecimiento)
            .Select(e => e.NombreEstablecimiento)
            .FirstOrDefaultAsync();

        var nombreUsuarioCajero = await db.Usuarios.AsNoTracking()
            .Where(u => u.IdUsuario == caja.IdUsuarioCajero)
            .Select(u => u.NombreUsuario)
            .FirstOrDefaultAsync();

        string? nombreRevisor = null;
        if (caja.RevisadoPorUsuario.HasValue)
            nombreRevisor = await db.Usuarios.AsNoTracking()
                .Where(u => u.IdUsuario == caja.RevisadoPorUsuario.Value)
                .Select(u => u.NombreUsuario)
                .FirstOrDefaultAsync();

        string? tipoDiferencia = caja.Diferencia switch
        {
            null    => null,
            > 0m    => "SOBRANTE",
            < 0m    => "FALTANTE",
            _       => "EXACTO",
        };

        var acciones = await CalcularAccionesAsync(caja);

        return new CajaDto(
            caja.IdCaja, caja.IdComercio, nombreComercio, caja.IdComercioAliado,
            caja.IdEstablecimiento, nombreEstablecimiento, caja.IdUsuarioCajero, nombreUsuarioCajero,
            caja.FechaOperativa, caja.FechaAperturaUtc, caja.FechaCierreUtc, caja.Estado,
            caja.FondoInicial, caja.EfectivoEsperado, caja.EfectivoContado, caja.Diferencia,
            tipoDiferencia, caja.CerradaAutomaticamente, caja.ObservacionesCajero,
            caja.RevisadoPorUsuario, nombreRevisor, caja.FechaRevision, caja.ObservacionesRevision,
            acciones);
    }

    // ── Acciones disponibles por estado (diseño, secciones 5/7/9) ───────────
    // Asume solicitante == dueño de la caja (ver nota de ProyectarAsync).
    // ABIERTA, EN_CUADRE y CERRADA_AUTOMATICAMENTE: reglas confirmadas para
    // esta etapa. CERRADA/CON_DIFERENCIA/REVISADA: hoy inalcanzables (ningún
    // código implementa todavía CerrarAsync/RevisarAsync) — se dejan con
    // valores conservadores (todo false) únicamente para que el switch
    // compile; se ajustarán en la etapa que implemente cada transición.
    private async Task<CajaAccionesDisponiblesDto> CalcularAccionesAsync(WalletCajaComercio caja)
    {
        switch (caja.Estado)
        {
            case EstadoAbierta:
            {
                var tieneRecargas = await db.WalletCajaMovimientos.AnyAsync(m =>
                    m.IdCaja == caja.IdCaja && m.TipoMovimiento == TipoMovimientoRecargaEfectivo);
                return new CajaAccionesDisponiblesDto(
                    PuedeIniciarCuadre: true,
                    PuedeCerrar: false,
                    PuedeRevisar: false,
                    PuedeVerComprobante: false,
                    PuedeCorregirFondoInicial: !tieneRecargas);
            }
            case EstadoEnCuadre:
                return new CajaAccionesDisponiblesDto(
                    PuedeIniciarCuadre: false,
                    PuedeCerrar: true,
                    PuedeRevisar: false,
                    PuedeVerComprobante: false,
                    PuedeCorregirFondoInicial: false);
            case EstadoCerradaAutomaticamente:
                // Confirmado: sin funcionalidades todavía no implementadas —
                // PuedeVerComprobante queda false hasta que la etapa de
                // comprobante exista y defina explícitamente su disponibilidad.
                return new CajaAccionesDisponiblesDto(
                    PuedeIniciarCuadre: false,
                    PuedeCerrar: false,
                    PuedeRevisar: false,
                    PuedeVerComprobante: false,
                    PuedeCorregirFondoInicial: false);
            case EstadoCerrada:
            case EstadoConDiferencia:
            case EstadoRevisada:
                // Placeholder conservador — estos 3 estados no son alcanzables
                // todavía (sin CerrarAsync/RevisarAsync implementados). Se
                // ajustarán explícitamente en la etapa que los implemente.
                return new CajaAccionesDisponiblesDto(
                    PuedeIniciarCuadre: false,
                    PuedeCerrar: false,
                    PuedeRevisar: false,
                    PuedeVerComprobante: false,
                    PuedeCorregirFondoInicial: false);
            default:
                throw new InvalidOperationException($"Estado de caja no reconocido: {caja.Estado}");
        }
    }

    // ── Abrir caja (diseño Fase 70.4, secciones 3, 6, 9, 10) ────────────────
    // No integra 70.1/70.2, no crea movimientos de caja. Fase 70.4-B: la
    // inserción de la caja ocurre dentro de una transacción que adquiere
    // sp_getapplock (AppLockHelper, clave comercio+fecha) antes de revalidar
    // que no exista ya un cierre diario generado para ese comercio+fecha —
    // sincronización bilateral con
    // WalletCierreDiarioComercioService.GenerarCierreAsync.
    public async Task<CajaDto> AbrirAsync(long idUsuario, AbrirCajaRequest req)
    {
        if (req.FondoInicial < 0)
            throw new ArgumentException("El fondo inicial no puede ser negativo.");

        var s = await ResolverScopeUnicoAsync(idUsuario);

        var idComercio = s.IdComercioExistente
            ?? throw new InvalidOperationException("Tu comercio operativo no tiene un comercio existente asociado.");

        var comercio = await db.Comercios.FindAsync(idComercio)
            ?? throw new InvalidOperationException("Comercio no encontrado.");
        if (comercio.Estado != "ACTIVO")
            throw new TransicionCajaInvalidaException("El comercio no está activo.");

        // Resolución de id_establecimiento — regla exacta del diseño (sección 10):
        // ADMIN_COMERCIO no tiene sede fija en su scope y debe indicarla explícita
        // en el request; ADMIN_SEDE_COMERCIO/CAJERO siempre usan la de su propio
        // scope, ignorando cualquier valor que llegue en el body. Nunca se elige
        // con FirstOrDefaultAsync ante ambigüedad — cada rama es determinística.
        long idEstablecimientoResuelto;
        if (s.RolComercio == "ADMIN_COMERCIO")
        {
            idEstablecimientoResuelto = req.IdEstablecimiento
                ?? throw new ArgumentException("Debes seleccionar la sede donde vas a operar la caja.");
        }
        else
        {
            idEstablecimientoResuelto = s.IdEstablecimiento
                ?? throw new InvalidOperationException("Tu usuario operativo no tiene una sede asignada.");
        }

        var establecimiento = await db.ComercioEstablecimientos.FirstOrDefaultAsync(
                e => e.IdEstablecimiento == idEstablecimientoResuelto && e.IdComercioAliado == s.IdComercioAliado)
            ?? throw new KeyNotFoundException("La sede indicada no pertenece a tu comercio.");
        if (establecimiento.Estado != "ACTIVO")
            throw new TransicionCajaInvalidaException("La sede seleccionada no está activa.");

        var hoy        = HoyColombia();
        var horaActual = HoraColombia();
        var horaLimiteEfectiva = establecimiento.HoraCierreAutomaticoCaja ?? HoraLimiteCierrePorDefecto;

        // fecha_operativa siempre es hoy — no existe apertura retroactiva ni
        // programada a futuro en 70.4.1, así que basta comparar la hora.
        if (horaActual > horaLimiteEfectiva)
            throw new TransicionCajaInvalidaException(
                $"No se puede abrir una caja después de la hora límite de cierre de esta sede ({horaLimiteEfectiva:HH:mm}). Contacta a tu administrador si necesitas operar en un turno posterior.");

        // Chequeo previo amigable — el UNIQUE(id_usuario_cajero, id_comercio,
        // fecha_operativa) es la garantía real contra la carrera de concurrencia
        // (ver catch más abajo); esta consulta solo evita una excepción de SQL
        // en el caso común de un segundo intento no concurrente. No filtra por
        // estado: una caja ya cerrada/revisada/cerrada automáticamente para ese
        // usuario+comercio+día también bloquea una nueva apertura.
        var yaExisteCaja = await db.WalletCajasComercio.AnyAsync(c =>
            c.IdUsuarioCajero == idUsuario && c.IdComercio == idComercio && c.FechaOperativa == hoy);
        if (yaExisteCaja)
            throw new CajaDuplicadaException(
                $"Ya existe una caja para el usuario {idUsuario} en el comercio {idComercio} para la fecha {hoy:yyyy-MM-dd}.");

        var now = DateTime.UtcNow;
        var caja = new WalletCajaComercio
        {
            IdUnidadNegocio        = comercio.IdUnidadNegocio,
            IdComercio             = idComercio,
            IdComercioAliado       = s.IdComercioAliado,
            IdEstablecimiento      = idEstablecimientoResuelto,
            IdUsuarioCajero        = idUsuario,
            FechaOperativa         = hoy,
            FechaAperturaUtc       = now,
            Estado                 = EstadoAbierta,
            HoraLimiteCierre       = horaLimiteEfectiva,
            FondoInicial           = req.FondoInicial,
            CerradaAutomaticamente = false,
            CreatedAt              = now,
        };

        // ── Sincronización bilateral con Fase 70.3 (diseño Fase 70.4, sección
        // 10 — Fase 70.4-B). Sección atómica: adquiere el application lock
        // comercio+fecha antes de revalidar que no exista ya un cierre diario
        // generado para ese comercio+fecha, y antes de reconfirmar la
        // duplicidad de caja bajo el lock. La UNIQUE constraint sigue siendo
        // la garantía final contra la carrera de duplicidad (catch de abajo);
        // el lock es lo que impide la carrera con GenerarCierreAsync, que no
        // tiene ninguna restricción de base de datos equivalente.
        var claveLock = AppLockHelper.ClaveCajaCierre(idComercio, hoy);

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            try
            {
                var resultadoLock = await AppLockHelper.AdquirirAsync(db, claveLock);
                AppLockHelper.ValidarResultado(resultadoLock, claveLock);

                var existeCierre = await db.WalletCierresDiariosComercio
                    .AnyAsync(c => c.IdComercio == idComercio && c.FechaCierre == hoy);
                if (existeCierre)
                    throw new CierreDiarioYaGeneradoException(
                        $"Ya existe un cierre diario generado para el comercio {idComercio} en la fecha {hoy:yyyy-MM-dd} — no puede abrirse una caja nueva para esta fecha.");

                // Revalidación autoritativa de duplicidad bajo el lock — el
                // chequeo de arriba (fuera de la transacción) es solo una
                // optimización amigable.
                var yaExisteCajaBajoLock = await db.WalletCajasComercio.AnyAsync(c =>
                    c.IdUsuarioCajero == idUsuario && c.IdComercio == idComercio && c.FechaOperativa == hoy);
                if (yaExisteCajaBajoLock)
                    throw new CajaDuplicadaException(
                        $"Ya existe una caja para el usuario {idUsuario} en el comercio {idComercio} para la fecha {hoy:yyyy-MM-dd}.");

                db.WalletCajasComercio.Add(caja);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException ex) when (EsViolacionUniqueUsuarioComercioFecha(ex))
            {
                // Carrera real: dos solicitudes de apertura concurrentes para el mismo
                // (id_usuario_cajero, id_comercio, fecha_operativa) — el chequeo previo
                // no la detectó porque ambas lo pasaron antes de que cualquiera
                // confirmara. Solo se traduce a CajaDuplicadaException cuando el motor
                // confirma que fue específicamente uq_wcc_usuario_comercio_fecha — no
                // cualquier DbUpdateException ni cualquier otra violación UNIQUE.
                await tx.RollbackAsync();
                throw new CajaDuplicadaException(
                    $"Ya existe una caja para el usuario {idUsuario} en el comercio {idComercio} para la fecha {hoy:yyyy-MM-dd}.");
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        logger.LogInformation(
            "WALLET_CAJA_ABRIR: idCaja={IdCaja} idUsuario={IdUsuario} idComercio={IdComercio} idEstablecimiento={IdEstablecimiento} fechaOperativa={FechaOperativa} fondoInicial={FondoInicial} horaLimiteCierre={HoraLimiteCierre}",
            caja.IdCaja, idUsuario, idComercio, idEstablecimientoResuelto, hoy, req.FondoInicial, horaLimiteEfectiva);

        // Proyección compartida con GetCajaActualAsync (Etapa 4C) — mismos
        // valores que ya devolvía esta operación en la Etapa 4B aprobada: caja
        // recién creada, ABIERTA, sin recargas vinculadas, dueño=solicitante.
        return await ProyectarAsync(caja);
    }

    // ── Corregir fondo inicial (diseño Fase 70.4, secciones 6, 10, 11) ──────
    // Solo el propio cajero dueño de la caja, solo mientras ABIERTA, solo si no
    // tiene recargas de efectivo vinculadas. Una sola consulta filtra por
    // (idCaja, idComercio del scope, idUsuario solicitante) a la vez — así el
    // 404 no distingue "no existe" de "existe pero fuera de alcance" (Etapa 4D,
    // punto 2: no revelar si existe una caja fuera del alcance).
    public async Task<CajaDto> CorregirFondoInicialAsync(long idUsuario, long idCaja, CorregirFondoInicialRequest req)
    {
        if (req.FondoInicial < 0)
            throw new ArgumentException("El fondo inicial no puede ser negativo.");

        var s = await ResolverScopeUnicoAsync(idUsuario);
        var idComercio = s.IdComercioExistente
            ?? throw new InvalidOperationException("Tu comercio operativo no tiene un comercio existente asociado.");

        var caja = await db.WalletCajasComercio
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.IdCaja == idCaja && c.IdComercio == idComercio && c.IdUsuarioCajero == idUsuario)
            ?? throw new KeyNotFoundException($"Caja {idCaja} no encontrada.");

        // Pre-chequeo fuera de transacción (Etapa 4D, punto 4): si ya está
        // vencida, se auto-sana (transacción propia e independiente de
        // AutoSanarAsync) y se rechaza — nunca se devuelve la caja como
        // corregida. Sin BeginTransactionAsync ni lock propios aquí — no existe
        // transacción de la operación todavía en este punto (Etapa 4I,
        // ampliación: mismo contrato CajaVencidaException que la rama bajo
        // lock, independiente de en qué momento se detectó el vencimiento).
        if (EstaVencida(caja))
        {
            var cajaSaneada = await AutoSanarAsync(caja);
            var cajaSaneadaDto = await ProyectarAsync(cajaSaneada);
            throw new CajaVencidaException(
                $"La caja {idCaja} venció y fue cerrada automáticamente — el fondo inicial ya no puede corregirse.", cajaSaneadaDto);
        }

        if (caja.Estado != EstadoAbierta)
            throw new TransicionCajaInvalidaException(
                $"El fondo inicial solo puede corregirse mientras la caja está {EstadoAbierta} (estado actual: {caja.Estado}).");

        // fondoInicialAnterior ya NO se toma de aquí (lectura sin tracking previa
        // al lock) — se captura más abajo desde cajaLock, bajo el lock, justo
        // antes de sobrescribirlo (corrección Etapa 4D).
        WalletCajaComercio? cajaActualizada = null;
        WalletCajaComercio? cajaVencidaBajoLock = null;
        decimal fondoInicialAnterior = 0m;

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            try
            {
                // Releer y bloquear bajo UPDLOCK/ROWLOCK — mismo patrón que
                // AutoSanarAsync. Revalida alcance, estado, vencimiento y ausencia
                // de recargas otra vez, ahora bajo el lock, antes de escribir.
                var cajaLock = await db.WalletCajasComercio
                    .FromSqlInterpolated($"SELECT * FROM wallet_cajas_comercio WITH (UPDLOCK, ROWLOCK) WHERE id_caja = {idCaja}")
                    .FirstOrDefaultAsync()
                    ?? throw new KeyNotFoundException($"Caja {idCaja} no encontrada.");

                if (cajaLock.IdComercio != idComercio || cajaLock.IdUsuarioCajero != idUsuario)
                    throw new KeyNotFoundException($"Caja {idCaja} no encontrada.");

                if (EstaVencida(cajaLock))
                {
                    // Venció justo entre el pre-chequeo y la adquisición del lock:
                    // no se toca FondoInicial. Se libera esta transacción (nada que
                    // confirmar) para que AutoSanarAsync pueda abrir la suya propia
                    // más abajo — nunca se anida una transacción dentro de otra.
                    await tx.RollbackAsync();
                    cajaVencidaBajoLock = cajaLock;
                }
                else
                {
                    if (cajaLock.Estado != EstadoAbierta)
                        throw new TransicionCajaInvalidaException(
                            $"El fondo inicial solo puede corregirse mientras la caja está {EstadoAbierta} (estado actual: {cajaLock.Estado}).");

                    var tieneRecargas = await db.WalletCajaMovimientos.AnyAsync(m =>
                        m.IdCaja == idCaja && m.TipoMovimiento == TipoMovimientoRecargaEfectivo);
                    if (tieneRecargas)
                        throw new TransicionCajaInvalidaException(
                            $"La caja {idCaja} ya tiene recargas de efectivo vinculadas — el fondo inicial ya no puede corregirse.");

                    fondoInicialAnterior = cajaLock.FondoInicial;
                    cajaLock.FondoInicial = req.FondoInicial;
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                    cajaActualizada = cajaLock;
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
            // La transacción anterior ya terminó (Rollback + Dispose del using de
            // arriba liberó el lock) antes de esta llamada. AutoSanarAsync abre y
            // confirma su propia transacción de forma completamente independiente.
            // Si otro proceso ya la auto-sanó entre medio, AutoSanarAsync lo
            // detecta (EstaVencida bajo su propio lock ya sería false) y devuelve
            // la caja sin sobrescribirla ni fallar — sin cambios necesarios en
            // AutoSanarAsync, ya es seguro ante una segunda llamada. Se captura el
            // valor de retorno (nunca se proyecta el cajaLock desactualizado de
            // antes del rollback) y se proyecta con el mismo ProyectarAsync ya
            // aprobado, para transportar el CajaDto real y persistido en la
            // excepción (Etapa 4I — alineación con la tabla HTTP del diseño).
            var cajaSaneada = await AutoSanarAsync(cajaVencidaBajoLock);
            var cajaSaneadaDto = await ProyectarAsync(cajaSaneada);
            throw new CajaVencidaException(
                $"La caja {idCaja} venció — el fondo inicial ya no puede corregirse.", cajaSaneadaDto);
        }

        logger.LogInformation(
            "WALLET_CAJA_CORREGIR_FONDO_INICIAL: idCaja={IdCaja} idUsuario={IdUsuario} idComercio={IdComercio} fondoInicialAnterior={FondoInicialAnterior} fondoInicialNuevo={FondoInicialNuevo}",
            idCaja, idUsuario, idComercio, fondoInicialAnterior, req.FondoInicial);

        return await ProyectarAsync(cajaActualizada!);
    }

    // ── Iniciar cuadre (diseño Fase 70.4, secciones 6, 8, 9, 11) ────────────
    // ABIERTA → EN_CUADRE, congelando EfectivoEsperado = FondoInicial +
    // SUM(movimientos RECARGA_EFECTIVO). Mismo patrón de alcance/vencimiento/
    // transacción que CorregirFondoInicialAsync (Etapa 4D).
    public async Task<CajaDto> IniciarCuadreAsync(long idUsuario, long idCaja)
    {
        var s = await ResolverScopeUnicoAsync(idUsuario);
        var idComercio = s.IdComercioExistente
            ?? throw new InvalidOperationException("Tu comercio operativo no tiene un comercio existente asociado.");

        var caja = await db.WalletCajasComercio
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.IdCaja == idCaja && c.IdComercio == idComercio && c.IdUsuarioCajero == idUsuario)
            ?? throw new KeyNotFoundException($"Caja {idCaja} no encontrada.");

        // Pre-chequeo fuera de transacción: si ya está vencida, se auto-sana
        // (transacción propia e independiente) y se rechaza — nunca pasa a
        // EN_CUADRE. Sin BeginTransactionAsync ni lock propios aquí (Etapa 4I,
        // ampliación: mismo contrato CajaVencidaException que la rama bajo
        // lock).
        if (EstaVencida(caja))
        {
            var cajaSaneada = await AutoSanarAsync(caja);
            var cajaSaneadaDto = await ProyectarAsync(cajaSaneada);
            throw new CajaVencidaException(
                $"La caja {idCaja} venció y fue cerrada automáticamente — no puede iniciarse el cuadre.", cajaSaneadaDto);
        }

        if (caja.Estado != EstadoAbierta)
            throw new TransicionCajaInvalidaException(
                $"Solo puede iniciarse el cuadre de una caja {EstadoAbierta} (estado actual: {caja.Estado}).");

        WalletCajaComercio? cajaActualizada = null;
        WalletCajaComercio? cajaVencidaBajoLock = null;
        decimal fondoInicialUsado = 0m;
        decimal sumaRecargasEfectivo = 0m;
        decimal efectivoEsperadoCalculado = 0m;

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            try
            {
                // Releer y bloquear bajo UPDLOCK/ROWLOCK — mismo patrón que
                // AutoSanarAsync/CorregirFondoInicialAsync. Revalida alcance,
                // estado y vencimiento otra vez, ahora bajo el lock, antes de
                // calcular y escribir.
                var cajaLock = await db.WalletCajasComercio
                    .FromSqlInterpolated($"SELECT * FROM wallet_cajas_comercio WITH (UPDLOCK, ROWLOCK) WHERE id_caja = {idCaja}")
                    .FirstOrDefaultAsync()
                    ?? throw new KeyNotFoundException($"Caja {idCaja} no encontrada.");

                if (cajaLock.IdComercio != idComercio || cajaLock.IdUsuarioCajero != idUsuario)
                    throw new KeyNotFoundException($"Caja {idCaja} no encontrada.");

                if (EstaVencida(cajaLock))
                {
                    // Venció justo entre el pre-chequeo y la adquisición del lock:
                    // no se toca Estado/EfectivoEsperado. Se libera esta
                    // transacción (nada que confirmar) para que AutoSanarAsync
                    // pueda abrir la suya propia más abajo — nunca se anida una
                    // transacción dentro de otra.
                    await tx.RollbackAsync();
                    cajaVencidaBajoLock = cajaLock;
                }
                else
                {
                    if (cajaLock.Estado != EstadoAbierta)
                        throw new TransicionCajaInvalidaException(
                            $"Solo puede iniciarse el cuadre de una caja {EstadoAbierta} (estado actual: {cajaLock.Estado}).");

                    // EfectivoEsperado (diseño, sección 8) — misma fórmula y misma
                    // consulta que la rama ABIERTA de AutoSanarAsync: SUM sobre
                    // wallet_caja_movimientos, sin tocar wallet_recargas_comercio
                    // ni ningún servicio de 70.1/70.2.
                    sumaRecargasEfectivo = await db.WalletCajaMovimientos
                        .Where(m => m.IdCaja == idCaja && m.TipoMovimiento == TipoMovimientoRecargaEfectivo)
                        .SumAsync(m => (decimal?)m.Valor) ?? 0m;

                    fondoInicialUsado = cajaLock.FondoInicial;
                    efectivoEsperadoCalculado = fondoInicialUsado + sumaRecargasEfectivo;

                    cajaLock.Estado           = EstadoEnCuadre;
                    cajaLock.EfectivoEsperado = efectivoEsperadoCalculado;

                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                    cajaActualizada = cajaLock;
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
            // propia de forma completamente independiente; si otro proceso ya la
            // auto-sanó entre medio, lo detecta bajo su propio lock y no la
            // sobrescribe ni falla. Se captura el valor de retorno y se proyecta
            // (Etapa 4I — alineación con la tabla HTTP del diseño).
            var cajaSaneada = await AutoSanarAsync(cajaVencidaBajoLock);
            var cajaSaneadaDto = await ProyectarAsync(cajaSaneada);
            throw new CajaVencidaException(
                $"La caja {idCaja} venció — no puede iniciarse el cuadre.", cajaSaneadaDto);
        }

        logger.LogInformation(
            "WALLET_CAJA_INICIAR_CUADRE: idCaja={IdCaja} idUsuario={IdUsuario} idComercio={IdComercio} fondoInicial={FondoInicial} sumaRecargasEfectivo={SumaRecargasEfectivo} efectivoEsperado={EfectivoEsperado} fechaOperativa={FechaOperativa}",
            idCaja, idUsuario, idComercio, fondoInicialUsado, sumaRecargasEfectivo, efectivoEsperadoCalculado, cajaActualizada!.FechaOperativa);

        return await ProyectarAsync(cajaActualizada!);
    }

    // ── Cerrar caja (diseño Fase 70.4, secciones 8, 9, 11) ──────────────────
    // EN_CUADRE → CERRADA (Diferencia == 0) o CON_DIFERENCIA (Diferencia != 0).
    // Diferencia = EfectivoContado - EfectivoEsperado (única fórmula consistente
    // con el signo ya fijado por ProyectarAsync: positivo=SOBRANTE). Igualdad
    // decimal exacta, sin tolerancia — exigida por ck_wcc_consistencia_estado.
    // EfectivoEsperado nunca se recalcula: se usa el valor congelado al iniciar
    // el cuadre. Mismo patrón de alcance/vencimiento/transacción que
    // CorregirFondoInicialAsync/IniciarCuadreAsync.
    public async Task<CajaDto> CerrarAsync(long idUsuario, long idCaja, CerrarCajaRequest req)
    {
        if (req.EfectivoContado < 0)
            throw new ArgumentException("El efectivo contado no puede ser negativo.");

        var s = await ResolverScopeUnicoAsync(idUsuario);
        var idComercio = s.IdComercioExistente
            ?? throw new InvalidOperationException("Tu comercio operativo no tiene un comercio existente asociado.");

        var caja = await db.WalletCajasComercio
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.IdCaja == idCaja && c.IdComercio == idComercio && c.IdUsuarioCajero == idUsuario)
            ?? throw new KeyNotFoundException($"Caja {idCaja} no encontrada.");

        // Pre-chequeo fuera de transacción: si ya está vencida, se auto-sana
        // (transacción propia e independiente) y se rechaza — nunca se cierra
        // manualmente una caja vencida. Sin BeginTransactionAsync ni lock
        // propios aquí (Etapa 4I, ampliación: mismo contrato CajaVencidaException
        // que la rama bajo lock).
        if (EstaVencida(caja))
        {
            var cajaSaneada = await AutoSanarAsync(caja);
            var cajaSaneadaDto = await ProyectarAsync(cajaSaneada);
            throw new CajaVencidaException(
                $"La caja {idCaja} venció y fue cerrada automáticamente — no puede cerrarse manualmente.", cajaSaneadaDto);
        }

        if (caja.Estado != EstadoEnCuadre)
            throw new TransicionCajaInvalidaException(
                $"Solo puede cerrarse una caja {EstadoEnCuadre} (estado actual: {caja.Estado}).");

        WalletCajaComercio? cajaActualizada = null;
        WalletCajaComercio? cajaVencidaBajoLock = null;
        decimal efectivoEsperadoUsado = 0m;
        decimal diferenciaCalculada = 0m;
        string estadoFinal = string.Empty;

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            try
            {
                // Releer y bloquear bajo UPDLOCK/ROWLOCK — mismo patrón que
                // AutoSanarAsync/CorregirFondoInicialAsync/IniciarCuadreAsync.
                // Revalida alcance, estado y vencimiento otra vez, ahora bajo el
                // lock, antes de calcular y escribir.
                var cajaLock = await db.WalletCajasComercio
                    .FromSqlInterpolated($"SELECT * FROM wallet_cajas_comercio WITH (UPDLOCK, ROWLOCK) WHERE id_caja = {idCaja}")
                    .FirstOrDefaultAsync()
                    ?? throw new KeyNotFoundException($"Caja {idCaja} no encontrada.");

                if (cajaLock.IdComercio != idComercio || cajaLock.IdUsuarioCajero != idUsuario)
                    throw new KeyNotFoundException($"Caja {idCaja} no encontrada.");

                if (EstaVencida(cajaLock))
                {
                    // Venció justo entre el pre-chequeo y la adquisición del lock:
                    // no se toca Estado/EfectivoContado/Diferencia. Se libera esta
                    // transacción (nada que confirmar) para que AutoSanarAsync
                    // pueda abrir la suya propia más abajo — nunca se anida una
                    // transacción dentro de otra. AutoSanarAsync conserva el
                    // EfectivoEsperado ya congelado (rama no-ABIERTA existente).
                    await tx.RollbackAsync();
                    cajaVencidaBajoLock = cajaLock;
                }
                else
                {
                    if (cajaLock.Estado != EstadoEnCuadre)
                        throw new TransicionCajaInvalidaException(
                            $"Solo puede cerrarse una caja {EstadoEnCuadre} (estado actual: {cajaLock.Estado}).");

                    // EfectivoEsperado nunca se recalcula — se usa el valor
                    // congelado al iniciar el cuadre (Etapa 4E). Si faltara, es una
                    // inconsistencia estructural real (no representable por el
                    // diseño para una caja EN_CUADRE) — no se inventa un valor.
                    efectivoEsperadoUsado = cajaLock.EfectivoEsperado
                        ?? throw new InvalidOperationException(
                            $"Caja {idCaja} está EN_CUADRE sin EfectivoEsperado congelado — estado inconsistente.");

                    diferenciaCalculada = req.EfectivoContado - efectivoEsperadoUsado;
                    estadoFinal = diferenciaCalculada == 0m ? EstadoCerrada : EstadoConDiferencia;

                    // ObservacionesCajero (decisión funcional confirmada): se
                    // normaliza una sola vez con Trim(). Con Diferencia == 0 es
                    // opcional (vacío tras Trim() → null). Con Diferencia != 0 es
                    // obligatoria (rechaza null/vacío/solo espacios). Máximo
                    // VARCHAR(500) — nunca se trunca en silencio.
                    var observacionesNormalizadas = req.Observaciones?.Trim();
                    if (string.IsNullOrEmpty(observacionesNormalizadas))
                        observacionesNormalizadas = null;

                    if (diferenciaCalculada != 0m && observacionesNormalizadas == null)
                        throw new ArgumentException(
                            "Debes indicar observaciones al cerrar la caja con diferencia — no puede quedar vacío ni solo con espacios.");

                    if (observacionesNormalizadas != null && observacionesNormalizadas.Length > 500)
                        throw new ArgumentException(
                            $"Las observaciones no pueden superar 500 caracteres (longitud actual: {observacionesNormalizadas.Length}).");

                    cajaLock.Estado                 = estadoFinal;
                    cajaLock.EfectivoContado         = req.EfectivoContado;
                    cajaLock.Diferencia              = diferenciaCalculada;
                    cajaLock.FechaCierreUtc          = DateTime.UtcNow;
                    cajaLock.CerradaAutomaticamente  = false;
                    cajaLock.ObservacionesCajero     = observacionesNormalizadas;

                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                    cajaActualizada = cajaLock;
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
            // propia de forma completamente independiente; si otro proceso ya la
            // auto-sanó entre medio, lo detecta bajo su propio lock y no la
            // sobrescribe ni falla. Se captura el valor de retorno y se proyecta
            // (Etapa 4I — alineación con la tabla HTTP del diseño).
            var cajaSaneada = await AutoSanarAsync(cajaVencidaBajoLock);
            var cajaSaneadaDto = await ProyectarAsync(cajaSaneada);
            throw new CajaVencidaException(
                $"La caja {idCaja} venció — no puede cerrarse manualmente.", cajaSaneadaDto);
        }

        logger.LogInformation(
            "WALLET_CAJA_CERRAR: idCaja={IdCaja} idUsuario={IdUsuario} idComercio={IdComercio} efectivoEsperado={EfectivoEsperado} efectivoContado={EfectivoContado} diferencia={Diferencia} estadoFinal={EstadoFinal} fechaOperativa={FechaOperativa}",
            idCaja, idUsuario, idComercio, efectivoEsperadoUsado, req.EfectivoContado, diferenciaCalculada, estadoFinal, cajaActualizada!.FechaOperativa);

        return await ProyectarAsync(cajaActualizada!);
    }

    // ── Revisar caja (diseño Fase 70.4, secciones 7, 11, 18) ────────────────
    // CON_DIFERENCIA|CERRADA_AUTOMATICAMENTE → REVISADA. Operación
    // administrativa (ADMIN_SEDE_COMERCIO/ADMIN_COMERCIO) — nunca CAJERO como
    // rol, nunca el propio cajero dueño de la caja. Solo metadata de auditoría:
    // no recalcula el cuadre ni toca ningún campo monetario. EstaVencida no
    // aplica aquí — internamente ya devuelve false para cualquier estado
    // distinto de ABIERTA/EN_CUADRE, y ambos estados de origen son terminales.
    public async Task<CajaDto> RevisarAsync(long idUsuario, long idCaja, RevisarCajaRequest req)
    {
        // ObservacionesRevision es obligatoria siempre (decisión funcional
        // confirmada) — se normaliza una sola vez con Trim(), rechaza
        // null/vacío/solo espacios, respeta el máximo VARCHAR(500).
        var observacionesNormalizadas = req.Observaciones?.Trim();
        if (string.IsNullOrEmpty(observacionesNormalizadas))
            throw new ArgumentException(
                "Debes indicar observaciones al revisar la caja — no puede quedar vacío ni solo con espacios.");
        if (observacionesNormalizadas.Length > 500)
            throw new ArgumentException(
                $"Las observaciones no pueden superar 500 caracteres (longitud actual: {observacionesNormalizadas.Length}).");

        var s = await ResolverScopeUnicoAsync(idUsuario);

        // Allowlist positiva (diseño, sección 7 — tabla de permisos): exactamente
        // ADMIN_SEDE_COMERCIO o ADMIN_COMERCIO. Cualquier otro valor —CAJERO,
        // null, vacío, un rol desconocido o uno futuro no autorizado
        // expresamente— queda rechazado por defecto, no solo CAJERO. El proyecto
        // no define constantes centrales para estos roles (todos los servicios
        // comparan contra el string literal — ver ComercioScopeService,
        // WalletRecargaComercioService, WalletCierreDiarioComercioService y la
        // propia AbrirAsync de esta clase) — se sigue esa misma convención.
        if (s.RolComercio != "ADMIN_COMERCIO" && s.RolComercio != "ADMIN_SEDE_COMERCIO")
            throw new UnauthorizedAccessException("Tu rol no está autorizado para revisar cajas.");

        var idComercio = s.IdComercioExistente
            ?? throw new InvalidOperationException("Tu comercio operativo no tiene un comercio existente asociado.");

        // El alcance de sede depende del rol ya validado exactamente arriba, no
        // de PuedeVerTodoComercio (una propiedad general de visibilidad, no la
        // autorización específica de esta operación). ADMIN_COMERCIO: cualquier
        // sede del comercio. ADMIN_SEDE_COMERCIO: acotado a s.IdEstablecimiento.
        long? idEstablecimientoScope;
        if (s.RolComercio == "ADMIN_COMERCIO")
        {
            idEstablecimientoScope = null;
        }
        else
        {
            idEstablecimientoScope = s.IdEstablecimiento
                ?? throw new InvalidOperationException("Tu usuario operativo no tiene una sede asignada.");
        }

        // Consulta única con todos los filtros de alcance combinados — una caja
        // de otro comercio o de otra sede (para ADMIN_SEDE_COMERCIO) responde
        // igual que una inexistente (404 genérico, sin revelar datos).
        var caja = await db.WalletCajasComercio
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.IdCaja == idCaja && c.IdComercio == idComercio
                    && (idEstablecimientoScope == null || c.IdEstablecimiento == idEstablecimientoScope))
            ?? throw new KeyNotFoundException($"Caja {idCaja} no encontrada.");

        // Segregación de funciones: nunca la propia caja, incluso si hoy el
        // solicitante ya no es CAJERO en este comercio (pudo serlo cuando esta
        // caja se abrió). Es una falta de autorización (403), no una
        // transición de estado inválida (409).
        if (caja.IdUsuarioCajero == idUsuario)
            throw new UnauthorizedAccessException("No puedes revisar tu propia caja.");

        if (caja.Estado != EstadoConDiferencia && caja.Estado != EstadoCerradaAutomaticamente)
            throw new TransicionCajaInvalidaException(
                $"Solo puede revisarse una caja {EstadoConDiferencia} o {EstadoCerradaAutomaticamente} (estado actual: {caja.Estado}).");

        WalletCajaComercio? cajaActualizada = null;
        string estadoAnterior = string.Empty;

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            try
            {
                // Releer y bloquear bajo UPDLOCK/ROWLOCK — mismo patrón que el
                // resto de transiciones de esta clase. Revalida alcance,
                // auto-revisión y estado otra vez, ahora bajo el lock, antes de
                // escribir.
                var cajaLock = await db.WalletCajasComercio
                    .FromSqlInterpolated($"SELECT * FROM wallet_cajas_comercio WITH (UPDLOCK, ROWLOCK) WHERE id_caja = {idCaja}")
                    .FirstOrDefaultAsync()
                    ?? throw new KeyNotFoundException($"Caja {idCaja} no encontrada.");

                if (cajaLock.IdComercio != idComercio
                    || (idEstablecimientoScope != null && cajaLock.IdEstablecimiento != idEstablecimientoScope))
                    throw new KeyNotFoundException($"Caja {idCaja} no encontrada.");

                if (cajaLock.IdUsuarioCajero == idUsuario)
                    throw new UnauthorizedAccessException("No puedes revisar tu propia caja.");

                if (cajaLock.Estado != EstadoConDiferencia && cajaLock.Estado != EstadoCerradaAutomaticamente)
                    throw new TransicionCajaInvalidaException(
                        $"Solo puede revisarse una caja {EstadoConDiferencia} o {EstadoCerradaAutomaticamente} (estado actual: {cajaLock.Estado}).");

                estadoAnterior = cajaLock.Estado;

                cajaLock.Estado               = EstadoRevisada;
                cajaLock.RevisadoPorUsuario    = idUsuario;
                cajaLock.FechaRevision         = DateTime.UtcNow;
                cajaLock.ObservacionesRevision = observacionesNormalizadas;

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                cajaActualizada = cajaLock;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // ObservacionesRevision no se registra en el log — el estándar del
        // proyecto no registra texto libre de negocio en ninguna otra
        // operación de esta clase (Motivo/Observaciones tampoco se loguean en
        // CorregirFondoInicialAsync/CerrarAsync).
        logger.LogInformation(
            "WALLET_CAJA_REVISAR: idCaja={IdCaja} idUsuarioRevisor={IdUsuarioRevisor} idComercio={IdComercio} idUsuarioCajero={IdUsuarioCajero} estadoAnterior={EstadoAnterior} estadoFinal={EstadoFinal} diferencia={Diferencia} fechaOperativa={FechaOperativa}",
            idCaja, idUsuario, idComercio, cajaActualizada!.IdUsuarioCajero, estadoAnterior, EstadoRevisada, cajaActualizada.Diferencia, cajaActualizada.FechaOperativa);

        return await ProyectarAsync(cajaActualizada!);
    }

    // ── Listar cajas (diseño Fase 70.4, secciones 6, 7, 11, 18) ─────────────
    // Consulta pura y paginada, sin efectos secundarios: no llama a
    // AutoSanarAsync, no abre transacciones ni locks, muestra el estado
    // persistido tal cual (decisión funcional confirmada — la auto-sanación
    // masiva de listados queda fuera de esta etapa). Allowlist positiva por
    // rol exacto (mismo criterio que RevisarAsync, Etapa 4G): CAJERO ve solo
    // sus propias cajas, ADMIN_SEDE_COMERCIO solo su sede, ADMIN_COMERCIO
    // cualquier sede de su comercio. page/pageSize fuera de rango →
    // ArgumentException (decisión funcional confirmada — tabla HTTP sección 11,
    // no el clamp silencioso usado en otros listados del proyecto).
    public async Task<PaginadoDto<CajaResumenDto>> ListarAsync(
        long idUsuario, int page, int pageSize,
        long? idEstablecimiento, string? estado, DateOnly? desde, DateOnly? hasta)
    {
        if (page < 1)
            throw new ArgumentException("La página debe ser mayor o igual a 1.");
        if (pageSize < 1 || pageSize > 100)
            throw new ArgumentException("El tamaño de página debe estar entre 1 y 100.");

        if (estado != null && !new[]
            {
                EstadoAbierta, EstadoEnCuadre, EstadoCerrada,
                EstadoConDiferencia, EstadoCerradaAutomaticamente, EstadoRevisada,
            }.Contains(estado))
            throw new ArgumentException($"Estado desconocido: {estado}.");

        if (desde.HasValue && hasta.HasValue && desde.Value > hasta.Value)
            throw new ArgumentException("fechaDesde no puede ser posterior a fechaHasta.");

        var s = await ResolverScopeUnicoAsync(idUsuario);
        var idComercio = s.IdComercioExistente
            ?? throw new InvalidOperationException("Tu comercio operativo no tiene un comercio existente asociado.");

        // El alcance de sede depende del rol exacto, nunca únicamente de
        // PuedeVerTodoComercio. ADMIN_SEDE_COMERCIO: el filtro del cliente se
        // ignora, siempre se fuerza su propia sede (mismo criterio ya aprobado
        // en AbrirAsync). ADMIN_COMERCIO/CAJERO: el filtro, si viene, solo
        // acota — nunca puede ampliar (CAJERO ya está acotado por
        // IdUsuarioCajero más abajo; ADMIN_COMERCIO ya ve todo el comercio).
        long? idEstablecimientoScope;
        bool esCajero;
        if (s.RolComercio == "ADMIN_COMERCIO")
        {
            idEstablecimientoScope = idEstablecimiento;
            esCajero = false;
        }
        else if (s.RolComercio == "ADMIN_SEDE_COMERCIO")
        {
            idEstablecimientoScope = s.IdEstablecimiento
                ?? throw new InvalidOperationException("Tu usuario operativo no tiene una sede asignada.");
            esCajero = false;
        }
        else if (s.RolComercio == "CAJERO")
        {
            idEstablecimientoScope = idEstablecimiento;
            esCajero = true;
        }
        else
        {
            throw new UnauthorizedAccessException("Tu rol no está autorizado para listar cajas.");
        }

        var query = db.WalletCajasComercio.AsNoTracking()
            .Where(c => c.IdComercio == idComercio);

        if (esCajero)
            query = query.Where(c => c.IdUsuarioCajero == idUsuario);
        if (idEstablecimientoScope.HasValue)
            query = query.Where(c => c.IdEstablecimiento == idEstablecimientoScope.Value);
        if (estado != null)
            query = query.Where(c => c.Estado == estado);
        if (desde.HasValue)
            query = query.Where(c => c.FechaOperativa >= desde.Value);
        if (hasta.HasValue)
            query = query.Where(c => c.FechaOperativa <= hasta.Value);

        var totalItems = await query.CountAsync();

        // Desplazamiento en long — evita que (page - 1) * pageSize desborde
        // int cuando page es extremadamente grande (page no tiene tope
        // superior, a diferencia de pageSize). Si el desplazamiento no cabe en
        // int, ni siquiera se ejecuta la consulta paginada (evita un OFFSET
        // gigantesco e innecesariamente costoso en SQL Server) — se devuelve
        // items vacío directamente, conservando totalItems/totalPages reales.
        var skipLong = ((long)page - 1L) * pageSize;

        List<CajaResumenDto> items;
        if (skipLong > int.MaxValue)
        {
            items = [];
        }
        else
        {
            // Proyección con JOIN traducible (sin navegación EF configurada,
            // pero Queryable.Join sobre claves escalares sí se traduce a SQL
            // JOIN). IdEstablecimiento/IdUsuarioCajero son NOT NULL con FK
            // real — INNER JOIN es seguro, sin filas huérfanas posibles.
            // TipoDiferencia usa la misma convención ya aprobada en
            // ProyectarAsync. Una sola consulta para nombres + datos, sin N+1.
            items = await (
                from c in query
                join e in db.ComercioEstablecimientos.AsNoTracking() on c.IdEstablecimiento equals e.IdEstablecimiento
                join u in db.Usuarios.AsNoTracking() on c.IdUsuarioCajero equals u.IdUsuario
                orderby c.FechaOperativa descending, c.IdCaja descending
                select new CajaResumenDto(
                    c.IdCaja,
                    c.IdEstablecimiento,
                    e.NombreEstablecimiento,
                    c.IdUsuarioCajero,
                    u.NombreUsuario,
                    c.FechaOperativa,
                    c.Estado,
                    c.FondoInicial,
                    c.EfectivoEsperado,
                    c.EfectivoContado,
                    c.Diferencia,
                    c.Diferencia == null ? null : (c.Diferencia > 0m ? "SOBRANTE" : (c.Diferencia < 0m ? "FALTANTE" : "EXACTO")))
            )
                .Skip((int)skipLong)
                .Take(pageSize)
                .ToListAsync();
        }

        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        return new PaginadoDto<CajaResumenDto>(items, page, pageSize, totalItems, totalPages);
    }

    // Más estricto que WalletCierreDiarioComercioService.EsViolacionUnica (Fase
    // 70.3): además del número SQL (2627 violación de índice único / 2601
    // violación de índice único al crear la fila), exige que el mensaje de SQL
    // Server identifique literalmente el índice uq_wcc_usuario_comercio_fecha.
    // Cualquier otra violación UNIQUE (de otro índice/constraint) o cualquier
    // otro DbUpdateException se propaga sin traducir a CajaDuplicadaException.
    private static bool EsViolacionUniqueUsuarioComercioFecha(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx
        && (sqlEx.Number == 2627 || sqlEx.Number == 2601)
        && sqlEx.Message.Contains("uq_wcc_usuario_comercio_fecha", StringComparison.OrdinalIgnoreCase);
}
