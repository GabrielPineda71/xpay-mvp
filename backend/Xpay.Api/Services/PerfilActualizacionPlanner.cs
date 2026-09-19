using Xpay.Api.DTOs;

namespace Xpay.Api.Services;

// XPAY-399 (Perfil Fase 2A) — cómputo PURO de qué cambia al aplicar
// ActualizarMiPerfilRequest sobre los valores actuales de Persona/Usuario.
// Extraído de PerfilService deliberadamente para que sea testeable SIN
// XpayDbContext/base de datos — mismo criterio ya usado en este proyecto
// para separar cómputo puro de orquestación EF Core (ver
// BrebPaymentReconciliationSelector, KeySelector). No lee ni escribe nada,
// no conoce EF Core, no lanza excepciones de infraestructura — solo
// InvalidOperationException para violaciones de validación de negocio.
public static class PerfilActualizacionPlanner
{
    public sealed record CamposActuales(
        string Celular,
        string? Email,
        string? Direccion,
        string? Ciudad,
        string? Departamento);

    public sealed record Cambio(object? Antes, object? Despues);

    public sealed record Plan(
        string CelularNuevo,
        string? EmailNuevo,
        string? DireccionNuevo,
        string? CiudadNuevo,
        string? DepartamentoNuevo,
        bool ResetearCelularVerificado,
        bool ResetearEmailVerificado,
        IReadOnlyDictionary<string, Cambio> Cambios);

    // Semántica (ver ActualizarMiPerfilRequest para el detalle completo):
    //   ausente/null  -> no tocar.
    //   ""            -> limpiar (solo campos nullable; Celular lanza).
    //   valor         -> validar longitud/formato y aplicar si difiere.
    public static Plan Calcular(CamposActuales actuales, ActualizarMiPerfilRequest request)
    {
        var cambios = new Dictionary<string, Cambio>(StringComparer.Ordinal);

        var celularNuevo = AplicarTexto(request.Celular, actuales.Celular, "celular", 30, permiteNull: false, cambios)
            ?? actuales.Celular; // Celular nunca puede quedar null (permiteNull:false garantiza esto; el ?? solo satisface el tipo).
        var emailNuevo = AplicarTexto(request.Email, actuales.Email, "email", 200, permiteNull: true, cambios, validar: EsEmailRazonable);
        var direccionNuevo = AplicarTexto(request.Direccion, actuales.Direccion, "direccion", 300, permiteNull: true, cambios);
        var ciudadNuevo = AplicarTexto(request.Ciudad, actuales.Ciudad, "ciudad", 100, permiteNull: true, cambios);
        var departamentoNuevo = AplicarTexto(request.Departamento, actuales.Departamento, "departamento", 100, permiteNull: true, cambios);

        return new Plan(
            CelularNuevo: celularNuevo,
            EmailNuevo: emailNuevo,
            DireccionNuevo: direccionNuevo,
            CiudadNuevo: ciudadNuevo,
            DepartamentoNuevo: departamentoNuevo,
            ResetearCelularVerificado: cambios.ContainsKey("celular"),
            ResetearEmailVerificado: cambios.ContainsKey("email"),
            Cambios: cambios);
    }

    private static string? AplicarTexto(
        string? entrante, string? actual, string nombreCampo, int maxLen, bool permiteNull,
        Dictionary<string, Cambio> cambios, Func<string, bool>? validar = null)
    {
        if (entrante is null) return actual; // ausente o null explícito -> no tocar

        var trimmed = entrante.Trim();
        if (trimmed.Length == 0)
        {
            if (!permiteNull)
                throw new InvalidOperationException($"El campo {nombreCampo} no puede quedar vacío.");
            if (actual is not null)
                cambios[nombreCampo] = new Cambio(actual, null);
            return null;
        }

        if (trimmed.Length > maxLen)
            throw new InvalidOperationException($"El campo {nombreCampo} no puede superar {maxLen} caracteres.");

        if (string.Equals(trimmed, actual, StringComparison.Ordinal))
            return actual; // sin cambio real — no se registra en `cambios`

        if (validar is not null && !validar(trimmed))
            throw new InvalidOperationException($"El formato de {nombreCampo} no es válido.");

        cambios[nombreCampo] = new Cambio(actual, trimmed);
        return trimmed;
    }

    private static bool EsEmailRazonable(string email)
    {
        try { _ = new System.Net.Mail.MailAddress(email); return true; }
        catch (FormatException) { return false; }
    }
}
