using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xpay.Api.Data;
using Xpay.Api.DTOs;

namespace Xpay.Api.Services;

// XPAY-702 - alta ADMINISTRATIVA de cliente de prueba QA: registro (servicio
// existente) + preparacion de identidad QA (store existente) en UNA sola
// transaccion. Deja la doble marca SIMULACION_QA; NO es Veriff.
//
// FAIL-CLOSED: requiere XPAY_QA_IDENTITY_PREP_ENABLED == "true",
// XPAY_QA_CLIENT_CREATE_ENABLED == "true" y XPAY_QA_CLIENT_CREATE_MAX como
// entero positivo (tope de clientes SIMULACION_QA). En produccion estas
// variables no existen: el endpoint responde 404.
public static class QaClientePruebaConfig
{
    public const string EnvCreateEnabled = "XPAY_QA_CLIENT_CREATE_ENABLED";
    public const string EnvCreateMax     = "XPAY_QA_CLIENT_CREATE_MAX";

    public static bool TryHabilitado(IConfiguration configuration, out int maximo)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        maximo = 0;
        if (!QaIdentidadPruebaConfig.EstaHabilitado(configuration))
            return false;
        if (!string.Equals(configuration[EnvCreateEnabled], "true", StringComparison.Ordinal))
            return false;
        var raw = configuration[EnvCreateMax];
        if (string.IsNullOrEmpty(raw))
            return false;
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var max) || max <= 0)
            return false;
        maximo = max;
        return true;
    }
}

public enum ResultadoClientePruebaQa
{
    Creado,
    NoDisponible,
    NoAutorizado,
    LimiteAlcanzado,
    DatosInvalidos,
    Conflicto,
    NoPreparado,
}

public sealed record ClientePruebaQaResultado(
    ResultadoClientePruebaQa Resultado, long? IdUsuario, string? Mensaje);

public sealed class QaClientePruebaService(
    XpayDbContext db,
    RegistroUsuarioFinalService registro,
    IQaIdentidadPruebaStore preparacion,
    IConfiguration configuration,
    TimeProvider timeProvider,
    Xpay.Api.Common.IAmbienteXpay ambiente)
{
    // XPAY-717 - false en cualquier ambiente productivo, aunque haya flags QA.
    public bool Habilitado =>
        ambiente.PermiteMecanismosNoProductivos
        && QaClientePruebaConfig.TryHabilitado(configuration, out _);

    public async Task<ClientePruebaQaResultado> CrearAsync(
        RegistroUsuarioFinalRequest request, long idAdmin, CancellationToken ct = default)
    {
        // XPAY-717 - en ambiente productivo el mecanismo no existe, aunque haya flags QA.
        if (!ambiente.PermiteMecanismosNoProductivos)
            return new(ResultadoClientePruebaQa.NoDisponible, null, null);

        if (!QaClientePruebaConfig.TryHabilitado(configuration, out var maximo))
            return new(ResultadoClientePruebaQa.NoDisponible, null, null);
        if (idAdmin <= 0)
            return new(ResultadoClientePruebaQa.NoAutorizado, null, null);

        // Sin unidad de negocio en el request: se usa la del administrador.
        if (request.IdUnidadNegocio <= 0)
        {
            var unidadAdmin = await db.Usuarios.AsNoTracking()
                .Where(u => u.IdUsuario == idAdmin)
                .Join(db.Personas.AsNoTracking(), u => u.IdPersona, p => p.IdPersona,
                      (u, p) => p.IdUnidadNegocio)
                .FirstOrDefaultAsync(ct);
            if (unidadAdmin <= 0)
                return new(ResultadoClientePruebaQa.DatosInvalidos, null, "No se pudo determinar la unidad de negocio.");
            request.IdUnidadNegocio = unidadAdmin;
        }

        // La preparacion QA solo admite CC (forma exigida por MiDecisor PN).
        var tipo = (request.TipoDocumento ?? string.Empty).Trim().ToUpperInvariant();
        if (tipo != "CC")
            return new(ResultadoClientePruebaQa.DatosInvalidos, null, "Solo se admite tipo de documento CC.");

        var proveedorQa = QaIdentidadPruebaConstantes.Proveedor;
        var existentes = await db.Personas.AsNoTracking()
            .CountAsync(p => p.IdentidadVerificadaProveedor == proveedorQa, ct);
        if (existentes >= maximo)
            return new(ResultadoClientePruebaQa.LimiteAlcanzado, null, null);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            long idUsuario;
            try
            {
                idUsuario = await registro.RegistrarAsync(request);
            }
            catch (InvalidOperationException ex)
            {
                await Revertir(tx);
                return new(ResultadoClientePruebaQa.Conflicto, null, ex.Message);
            }

            var idPersona = await db.Usuarios.AsNoTracking()
                .Where(u => u.IdUsuario == idUsuario)
                .Select(u => u.IdPersona)
                .SingleAsync(ct);

            var fecha = timeProvider.GetUtcNow().UtcDateTime;
            var aplicacion = new AplicacionPreparacionIdentidadQa(idUsuario, idPersona, idAdmin, fecha);
            if (!await preparacion.AplicarPreparacionAsync(aplicacion, ct))
            {
                await Revertir(tx);
                return new(ResultadoClientePruebaQa.NoPreparado, null, null);
            }

            await tx.CommitAsync(ct);
            return new(ResultadoClientePruebaQa.Creado, idUsuario, null);
        }
        catch
        {
            await Revertir(tx);
            throw;
        }
    }

    private async Task Revertir(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx)
    {
        if (db.Database.CurrentTransaction is not null)
            await tx.RollbackAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
    }
}
