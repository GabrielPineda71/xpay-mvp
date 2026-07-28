using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Xpay.Api.Common;

// Fase 71.2-E-G: construye el request_hash normalizado documentado en
// docs/security/FASE_71.2_E_B_AUTORIZACION_IDOR.md §16.8 — SortedDictionary
// con StringComparer.Ordinal garantiza orden alfabético fijo e
// independiente de la cultura del servidor (nunca se depende del orden de
// inserción ni de reflection sobre un tipo anónimo). Los valores resueltos
// por el servidor (idUsuario, idWalletOrigen/idWalletPagadora) nunca
// provienen de un campo que el cliente pudiera enviar — esos campos ya no
// existen en TransferenciaWalletRequest/PagoQrRequest desde 71.2-E-C/D.
public static class IdempotencyHashHelper
{
    public static byte[] ComputeTransferenciaHash(long idUsuario, long idWalletOrigen, long idWalletDestino, decimal valor, string? descripcion) =>
        ComputeHash(new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["op"] = "wallets.transferencia.v1",
            ["idUsuario"] = idUsuario,
            ["idWalletOrigen"] = idWalletOrigen,
            ["idWalletDestino"] = idWalletDestino,
            ["valor"] = NormalizarValor(valor),
            ["descripcion"] = NormalizarTexto(descripcion),
        });

    public static byte[] ComputePagoQrHash(long idUsuario, long idWalletPagadora, string codigoQr, decimal valor, string? descripcion) =>
        ComputeHash(new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["op"] = "qr.pagar.v1",
            ["idUsuario"] = idUsuario,
            ["idWalletPagadora"] = idWalletPagadora,
            ["codigoQr"] = codigoQr,
            ["valor"] = NormalizarValor(valor),
            ["descripcion"] = NormalizarTexto(descripcion),
        });

    // Formato invariante, dos decimales fijos — evita que variaciones de
    // cultura/formato produzcan hashes distintos para el mismo valor.
    private static string NormalizarValor(decimal valor) => valor.ToString("F2", CultureInfo.InvariantCulture);

    // Trim explícito; null se preserva como null (nunca colapsa a cadena
    // vacía) para que "sin descripción" y "descripción vacía enviada
    // explícitamente" produzcan hashes distintos.
    private static string? NormalizarTexto(string? valor) => valor?.Trim();

    private static byte[] ComputeHash(SortedDictionary<string, object?> payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        return SHA256.HashData(bytes);
    }
}
