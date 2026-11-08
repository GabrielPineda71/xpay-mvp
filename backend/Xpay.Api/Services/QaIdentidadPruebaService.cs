using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Xpay.Api.Services;

// XPAY-669 — preparación QA de UNA persona de prueba de proveedor externo
// (primera consulta MiDecisor). Mecanismo administrativo AISLADO del flujo
// KYC/Veriff: no modifica KycService, KycController, simular-estado ni
// KycAprobado. Deja la identidad habilitada con procedencia explícita
// "SIMULACION_QA" — nunca la presenta como verificada por Veriff.
//
// FAIL-CLOSED por configuración (QA corre con ASPNETCORE_ENVIRONMENT=Production,
// así que el nombre de ambiente NO sirve como guarda):
//   1. XPAY_QA_IDENTITY_PREP_ENABLED debe valer exactamente "true" (ordinal);
//   2. XPAY_QA_IDENTITY_PREP_ALLOWED_USUARIO_ID debe ser UN long > 0 igual al
//      usuario destino.
// Sin esa configuración el mecanismo se comporta como no disponible. La
// configuración NUNCA contiene PII: sólo el id interno del usuario.
//
// Este servicio no tiene logger y no devuelve ni registra documento, apellido
// ni nombre: los datos declarados sólo se validan en memoria.
public static class QaIdentidadPruebaConfig
{
    public const string EnvEnabled          = "XPAY_QA_IDENTITY_PREP_ENABLED";
    public const string EnvAllowedUsuarioId = "XPAY_QA_IDENTITY_PREP_ALLOWED_USUARIO_ID";

    // Único valor que habilita el mecanismo. Comparación ordinal exacta.
    public const string ValorHabilitado = "true";

    public static bool EstaHabilitado(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return string.Equals(configuration[EnvEnabled], ValorHabilitado, StringComparison.Ordinal);
    }

    // Exactamente UN id: sólo dígitos ASCII (NumberStyles.None: sin signo,
    // espacios, separadores ni listas) y mayor que cero.
    public static bool TryLeerUsuarioPermitido(IConfiguration configuration, out long idUsuario)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        idUsuario = 0;

        var raw = configuration[EnvAllowedUsuarioId];
        if (string.IsNullOrEmpty(raw))
            return false;

        if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var valor) || valor <= 0)
            return false;

        idUsuario = valor;
        return true;
    }
}

public static class QaIdentidadPruebaConstantes
{
    public const string Proveedor = "SIMULACION_QA";
    public const string Decision  = "SIMULACION_QA";
    public const string EstadoKyc = "APROBADO";
    public const string Reason    = "Preparación QA de persona de prueba de proveedor externo; NO verificada por Veriff";
    public const string Modulo    = "KYC";
    public const string Accion    = "QA_PREPARAR_IDENTIDAD_PRUEBA";
    public const string ProveedorVeriff = "VERIFF";
}

public enum ResultadoPreparacionIdentidadQa
{
    // Mecanismo deshabilitado: se comporta como no disponible.
    NoDisponible,
    // Usuario permitido ausente, inválido o distinto del solicitado; o
    // ejecutor no resoluble.
    NoAutorizado,
    // El administrador intenta prepararse a sí mismo.
    AutoPreparacionProhibida,
    // Usuario o persona destino inexistente.
    NoEncontrado,
    // Identidad real (Veriff) o estado verificado no reconocible como una
    // preparación de este mecanismo: nunca se sobrescribe.
    IdentidadProtegida,
    // Usuario/persona no activos o datos declarados sin la forma requerida.
    PrecondicionesNoCumplidas,
    // El estado cambió entre la lectura y la transacción: no se escribió nada.
    EstadoCambiado,
    // Ya preparada por este mecanismo: idempotente, sin escrituras.
    YaPreparada,
    // Preparación aplicada.
    Preparada,
}

// Estado necesario para decidir. Clase (no record) para que ToString NO
// imprima los datos declarados: nunca debe loguearse.
public sealed class QaIdentidadPruebaSnapshot
{
    public long    IdUsuario { get; init; }
    public long    IdPersona { get; init; }
    public string? UsuarioEstado { get; init; }
    public string? EstadoKycActual { get; init; }
    public string? PersonaEstado { get; init; }
    public string? TipoDocumento { get; init; }
    public string? NumeroDocumento { get; init; }
    public string? PrimerApellido { get; init; }
    public bool    IdentidadVerificada { get; init; }
    public string? IdentidadVerificadaProveedor { get; init; }
    // Algún campo crudo de Veriff poblado (nombre/apellido/tipo/número verificado).
    public bool    TieneDatosVeriff { get; init; }
    // Existe alguna fila kyc_verificaciones del proveedor VERIFF para el usuario.
    public bool    TieneVerificacionVeriff { get; init; }
    // Filas kyc_verificaciones con es_actual = 1.
    public int     KycActuales { get; init; }
    // La fila actual es exactamente la que crea este mecanismo.
    public bool    KycActualEsPreparacionQa { get; init; }
}

public sealed record AplicacionPreparacionIdentidadQa(
    long IdUsuario, long IdPersona, long IdAdmin, DateTime FechaUtc);

public interface IQaIdentidadPruebaStore
{
    Task<QaIdentidadPruebaSnapshot?> CargarAsync(long idUsuario, CancellationToken cancellationToken);

    // Aplica la preparación en UNA transacción, revalidando el estado dentro de
    // ella. Devuelve false (sin escrituras) si el estado ya no es elegible.
    Task<bool> AplicarPreparacionAsync(AplicacionPreparacionIdentidadQa aplicacion, CancellationToken cancellationToken);
}

public sealed class QaIdentidadPruebaService(
    IQaIdentidadPruebaStore store,
    IConfiguration configuration,
    TimeProvider timeProvider,
    Xpay.Api.Common.IAmbienteXpay ambiente)
{
    public async Task<ResultadoPreparacionIdentidadQa> PrepararAsync(
        long idUsuarioDestino, long idAdmin, CancellationToken cancellationToken = default)
    {
        // XPAY-717 - en ambiente productivo el mecanismo no existe, aunque haya flags QA.
        if (!ambiente.PermiteMecanismosNoProductivos)
            return ResultadoPreparacionIdentidadQa.NoDisponible;

        // 1. Mecanismo habilitado (antes de cualquier lectura).
        if (!QaIdentidadPruebaConfig.EstaHabilitado(configuration))
            return ResultadoPreparacionIdentidadQa.NoDisponible;

        // 2–3. Usuario permitido válido e igual al solicitado.
        if (!QaIdentidadPruebaConfig.TryLeerUsuarioPermitido(configuration, out var permitido)
            || idUsuarioDestino <= 0
            || idUsuarioDestino != permitido)
            return ResultadoPreparacionIdentidadQa.NoAutorizado;

        // 4–5. Ejecutor resoluble y distinto del destino.
        if (idAdmin <= 0)
            return ResultadoPreparacionIdentidadQa.NoAutorizado;
        if (idAdmin == idUsuarioDestino)
            return ResultadoPreparacionIdentidadQa.AutoPreparacionProhibida;

        // 6. Carga del destino.
        var snap = await store.CargarAsync(idUsuarioDestino, cancellationToken).ConfigureAwait(false);
        if (snap is null)
            return ResultadoPreparacionIdentidadQa.NoEncontrado;

        // 7a. Protección de identidad real: cualquier rastro de Veriff es rechazo absoluto.
        if (snap.TieneVerificacionVeriff
            || snap.TieneDatosVeriff
            || string.Equals(snap.IdentidadVerificadaProveedor, QaIdentidadPruebaConstantes.ProveedorVeriff, StringComparison.Ordinal))
            return ResultadoPreparacionIdentidadQa.IdentidadProtegida;

        // 7b. Ya verificada: sólo es idempotente si es exactamente una preparación de este mecanismo.
        if (snap.IdentidadVerificada)
            return EsPreparacionQaCompleta(snap)
                ? ResultadoPreparacionIdentidadQa.YaPreparada
                : ResultadoPreparacionIdentidadQa.IdentidadProtegida;

        // 7c. Precondiciones del destino.
        if (!string.Equals(snap.UsuarioEstado, "ACTIVO", StringComparison.Ordinal)
            || !string.Equals(snap.PersonaEstado, "ACTIVA", StringComparison.Ordinal)
            || !DatosDeclaradosValidos(snap))
            return ResultadoPreparacionIdentidadQa.PrecondicionesNoCumplidas;

        var aplicada = await store.AplicarPreparacionAsync(
                new AplicacionPreparacionIdentidadQa(
                    snap.IdUsuario, snap.IdPersona, idAdmin, timeProvider.GetUtcNow().UtcDateTime),
                cancellationToken)
            .ConfigureAwait(false);

        return aplicada
            ? ResultadoPreparacionIdentidadQa.Preparada
            : ResultadoPreparacionIdentidadQa.EstadoCambiado;
    }

    // YaPreparada exige evidencia inequívoca de ESTE mecanismo, no sólo
    // IdentidadVerificada = true.
    internal static bool EsPreparacionQaCompleta(QaIdentidadPruebaSnapshot s) =>
        s.IdentidadVerificada
        && string.Equals(s.IdentidadVerificadaProveedor, QaIdentidadPruebaConstantes.Proveedor, StringComparison.Ordinal)
        && !s.TieneDatosVeriff
        && !s.TieneVerificacionVeriff
        && string.Equals(s.EstadoKycActual, QaIdentidadPruebaConstantes.EstadoKyc, StringComparison.Ordinal)
        && s.KycActuales == 1
        && s.KycActualEsPreparacionQa;

    // Forma requerida por la consulta MiDecisor de Persona Natural con CC:
    // TipoDocumento "CC", número de 3–13 dígitos ASCII, primer apellido no vacío.
    internal static bool DatosDeclaradosValidos(QaIdentidadPruebaSnapshot s)
    {
        var tipo = (s.TipoDocumento ?? string.Empty).Trim();
        if (!string.Equals(tipo, "CC", StringComparison.Ordinal))
            return false;

        var numero = (s.NumeroDocumento ?? string.Empty).Trim();
        if (numero.Length is < 3 or > 13)
            return false;
        foreach (var c in numero)
            if (!char.IsAsciiDigit(c))
                return false;

        return !string.IsNullOrWhiteSpace(s.PrimerApellido);
    }
}
