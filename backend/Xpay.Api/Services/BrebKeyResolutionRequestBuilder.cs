using System.Security.Cryptography;
using System.Text;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// XPAY-371 — construye el PassportResolveKeyRequest para "resolver mi
// llave" a partir de la llave YA REGISTRADA (PassportBrebLlave) del usuario
// autenticado. Función PURA de transformación/validación — no hace I/O, no
// llama a Passport, no toca la base de datos — 100% testeable offline
// (mismo criterio que CreateQrStaticEvidenceBuilder/HarnessOrchestrator en
// tools/Xpay.PassportSandboxHarness).
//
// POR QUÉ EXISTE ESTE GUARD DE CONFIRMACIÓN (y no un endpoint sin body):
// PassportBrebLlave NUNCA persiste el key_value en claro — sólo
// KeyValueMasked (visual) y KeyValueHash (SHA-256, irreversible). Por
// diseño (ver BrebService.ComputeKeyHash/UpsertLlave), el valor en claro
// sólo existe en memoria durante el request de registro y nunca se
// recupera después. Por lo tanto, "resolver mi llave" REQUIERE que el
// usuario reenvíe el valor — pero eso NO es "recibir una llave arbitraria
// desde el frontend" (prohibido en XPAY-371 FASE 2.2): este builder exige
// que el hash del valor reenviado coincida EXACTAMENTE con
// llave.KeyValueHash de la llave activa ya resuelta server-side desde la
// Wallet del usuario autenticado, ANTES de construir cualquier request a
// Passport. Un valor que no coincide con el hash registrado se rechaza
// aquí mismo — nunca llega a HttpClient ni a Passport. Esto es
// estructuralmente distinto de un "enviar a cualquier destino": el usuario
// sólo puede reconfirmar la llave que él mismo ya registró.
public static class BrebKeyResolutionRequestBuilder
{
    // XPAY-371 — se lanza cuando no hay llave activa, o cuando el valor de
    // confirmación no coincide con el hash de la llave activa registrada.
    // Mensaje corto y genérico a propósito: nunca revela si "no hay llave"
    // o "el valor no coincide" son casos distintos (evita dar pistas sobre
    // el valor real de la llave de otro usuario, aunque este método sólo
    // opera sobre la wallet del propio usuario autenticado).
    public sealed class LlaveNoConfirmadaException(string message) : InvalidOperationException(message);

    public static PassportResolveKeyRequest Build(
        PassportBrebLlave? llaveActiva,
        string? keyValueConfirmacion,
        string operationalCustomerId)
    {
        if (llaveActiva is null)
            throw new LlaveNoConfirmadaException("No tienes una llave Bre-B activa registrada.");

        if (string.IsNullOrWhiteSpace(keyValueConfirmacion))
            throw new LlaveNoConfirmadaException("Debes confirmar el valor de tu llave Bre-B registrada.");

        if (string.IsNullOrWhiteSpace(operationalCustomerId))
            throw new PassportConfigurationException(
                $"{PassportOptions.EnvOperationalCustomerId} no está configurado.");

        if (!Enum.TryParse<PassportKeyType>(llaveActiva.KeyType, ignoreCase: true, out var keyType)
            || !Enum.IsDefined(keyType))
            throw new InvalidOperationException(
                $"La llave registrada tiene un keyType local no soportado por Passport: '{llaveActiva.KeyType}'.");

        var hashConfirmacion = ComputeKeyHash(keyValueConfirmacion);
        if (!FixedTimeEquals(hashConfirmacion, llaveActiva.KeyValueHash))
            throw new LlaveNoConfirmadaException(
                "El valor ingresado no coincide con tu llave Bre-B registrada.");

        return new PassportResolveKeyRequest(
            CustomerId: operationalCustomerId,
            Key: new PassportKeyRequest(KeyType: keyType, KeyValue: keyValueConfirmacion.Trim()));
    }

    // Mismo algoritmo exacto que BrebService.ComputeKeyHash — SHA-256 sobre
    // el valor trim+lowercase, hex lowercase. Duplicado deliberadamente
    // como método local (no se referencia BrebService desde aquí) para que
    // esta clase permanezca sin dependencia del DbContext/servicio — si en
    // el futuro se decide extraer un solo lugar canónico para el hash, debe
    // hacerse en un ticket propio que actualice ambos sitios a la vez.
    private static string ComputeKeyHash(string keyValue)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(keyValue.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // Comparación en tiempo constante — un hash de llave Bre-B no debe
    // filtrarse ni siquiera indirectamente vía un side-channel de timing de
    // comparación de strings.
    private static bool FixedTimeEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);
        if (bytesA.Length != bytesB.Length) return false;
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
