using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Exceptions;

namespace Xpay.Api.Services;

internal static class DocumentoCuenta
{
    internal static string NormalizarNumero(string numero) => numero.Trim()
        .Replace(".", "").Replace("-", "").Replace(" ", "").Replace("\t", "")
        .ToUpperInvariant();

    internal static string? NormalizarTipo(string? tipo) => tipo?.Trim().ToUpperInvariant() switch
    {
        null or "" => null,
        "ID_CARD" or "CEDULA DE CIUDADANIA" or "CÉDULA DE CIUDADANÍA" => "CC",
        "CEDULA DE EXTRANJERIA" or "CÉDULA DE EXTRANJERÍA" => "CE",
        "PASSPORT" => "PASAPORTE",
        var normalizado => normalizado,
    };

    // Alta declarada y decisión Veriff compiten bajo la misma clave. El número
    // serializa incluso si Veriff aún no permite mapear el tipo de documento.
    internal static async Task BloquearAsync(XpayDbContext db, string numero, CancellationToken ct = default)
    {
        var resultado = await AppLockHelper.AdquirirAsync(db, $"XPAY:IDENTIDAD_DOCUMENTO:{numero}", ct);
        if (resultado is 0 or 1) return;
        if (resultado is -1 or -2 or -3)
            throw new IdentidadDocumentoConcurrenteException("Otra operación está comprobando este documento. Intenta de nuevo.");
        throw new InvalidOperationException("No se pudo obtener el bloqueo de identidad.");
    }

    internal static async Task<bool> ExisteOtraCuentaAsync(
        XpayDbContext db, long? idPersona, string? tipo, string numero, CancellationToken ct = default)
    {
        var candidatos = await db.Personas.AsNoTracking()
            .Where(p => p.IdPersona != idPersona && db.Usuarios.Any(u => u.IdPersona == p.IdPersona))
            .Where(p =>
                p.NumeroDocumento != null &&
                p.NumeroDocumento.Replace(".", "").Replace("-", "").Replace(" ", "").Replace("\t", "").ToUpper() == numero
                || p.IdentidadVerificada && p.NumeroDocumentoVerificado != null &&
                p.NumeroDocumentoVerificado.Replace(".", "").Replace("-", "").Replace(" ", "").Replace("\t", "").ToUpper() == numero)
            .Select(p => new { p.TipoDocumento, p.NumeroDocumento, p.IdentidadVerificada, p.TipoDocumentoVeriffRaw, p.NumeroDocumentoVerificado })
            .ToListAsync(ct);

        foreach (var candidato in candidatos)
        {
            var coincideDeclarado = candidato.NumeroDocumento is { } declarado && NormalizarNumero(declarado) == numero;
            var tipoDeclarado = coincideDeclarado ? NormalizarTipo(candidato.TipoDocumento) : null;
            if (coincideDeclarado && CoincideTipo(tipo, tipoDeclarado))
                return true;

            if (candidato.IdentidadVerificada && candidato.NumeroDocumentoVerificado is { } verificado
                && NormalizarNumero(verificado) == numero
                && CoincideTipo(tipo, TipoVerificado(candidato.TipoDocumentoVeriffRaw, tipoDeclarado)))
                return true;
        }

        return false;
    }

    // Veriff usa ID_CARD para varios países. Un registro antiguo sin país
    // documental solo confirma un tipo cuando su declaración coincide.
    private static string? TipoVerificado(string? tipo, string? tipoDeclarado) => tipo?.Trim().ToUpperInvariant() switch
    {
        "ID_CARD" => tipoDeclarado is "CC" or "CE" or "TI" ? tipoDeclarado : null,
        "CC" => "CC",
        "PASSPORT" or "PASAPORTE" => "PASAPORTE",
        _ => null,
    };

    private static bool CoincideTipo(string? solicitado, string? existente) =>
        solicitado is null || existente is null || solicitado == existente;
}
