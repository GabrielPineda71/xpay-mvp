using Xpay.Api.DTOs;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// XPAY-371 — interpreta la PassportResolveKeyResponse ya deserializada
// (IPassportKeyClient.ResolveKeyAsync ya garantiza `Id` no nulo vía su
// propio guard interno RequireResolutionId — ver PassportKeyClient.cs) y
// decide si es "usable" para marcar la llave como VALIDADA, qué columnas
// YA EXISTENTES de PassportBrebLlave puede poblar legítimamente, y qué
// forma sanitizada se expone hacia la UI. Función PURA — no hace I/O, no
// toca la base de datos, no llama a Passport — 100% testeable offline.
//
// QUÉ NO SE POBLA Y POR QUÉ (XPAY-371 FASE 3 — "no asumir equivalencias
// que el DTO no demuestre"):
//   - PassportKeyId: Resolve Key NO devuelve un campo `key_id` en ningún
//     nivel de PassportResolveKeyResponse (sólo `id` = resolution_id, un
//     concepto histórica y explícitamente distinto — ver comentario de
//     CreateQrStaticEvidenceBuilder sobre no conflar qr_id/key_id/
//     resolution_id). Poblar PassportKeyId desde esta respuesta sería
//     inventar una equivalencia que el contrato no sostiene.
//   - PassportAccountId: el objeto `account` de la respuesta sólo trae
//     account_number/account_type — ningún account_id. Mismo criterio.
//   - PassportCustomerId: el `customer_id` de la respuesta es el MISMO
//     customer_id que se envió en el request (la cuenta operativa de XPAY
//     como solicitante) — Passport lo repite, no descubre un customer_id
//     nuevo asociado a la llave/titular resuelto. Persistirlo en la fila
//     de ESTA llave individual sugeriría falsamente que es "el
//     customer_id de este usuario/llave", lo cual el DTO no demuestra.
// Las tres columnas quedan tal como estaban (null en Fase 64) — no es un
// olvido, es la aplicación directa de esa regla.
public static class BrebKeyResolutionResponseMapper
{
    // XPAY-371 FASE 2.6 — "no considerar una llave validada simplemente
    // porque el HTTP respondió". IPassportKeyClient.ResolveKeyAsync ya
    // exige `Id` (RequireResolutionId, ver PassportKeyClient.cs) — pero eso
    // sólo prueba que Passport devolvió UNA resolución, no que traiga
    // información suficiente para mostrarle al usuario un destinatario
    // confirmable. El shape "completo" usado aquí como mínimo aceptable es
    // exactamente el fixture ya versionado en
    // PassportKeyClientTests.FullResolveKeyResponseBody (Owner + Key +
    // Participant + Account, los cuatro presentes) — no un mínimo inventado
    // para este ticket.
    // XPAY-371 — único lugar canónico para decidir si una PassportBrebLlave
    // ya materializada (fuera de una query EF traducida a SQL) fue
    // VALIDADA por una resolución Passport real vs. por el botón admin QA
    // simular-validacion-llave (que nunca puebla OwnerNameMasked). Usado
    // por BrebService.ToLlaveResponse. BrebService.GetAdminLlavesAsync usa
    // la MISMA condición en línea dentro de un .Select() traducido a SQL
    // por EF Core — no puede invocar este método ahí (EF Core no traduce
    // llamadas a métodos arbitrarios), pero la condición es idéntica
    // (`OwnerNameMasked != null`) — mantenerlas sincronizadas si alguna
    // cambia.
    public static bool WasResolvedByPassport(PassportBrebLlave llave)
    {
        ArgumentNullException.ThrowIfNull(llave);
        return llave.OwnerNameMasked is not null;
    }

    public static bool IsComplete(PassportResolveKeyResponse response) =>
        !string.IsNullOrWhiteSpace(response.Id)
        && response.Owner       is not null
        && response.Participant is not null
        && response.Account     is not null;

    // Aplica los campos verificados a la entidad YA CARGADA por el llamador
    // (BrebService es responsable de SaveChangesAsync — este método no
    // hace I/O). Sólo se invoca cuando IsComplete(response) es true.
    public static void ApplyToLlave(
        PassportBrebLlave llave, PassportResolveKeyResponse response, DateTime nowUtc, long updatedByUsuario)
    {
        ArgumentNullException.ThrowIfNull(llave);
        ArgumentNullException.ThrowIfNull(response);
        if (!IsComplete(response))
            throw new InvalidOperationException(
                "No se puede aplicar una resolución incompleta a la llave (llamar IsComplete primero).");

        var owner       = response.Owner!;
        var participant = response.Participant!;
        var account     = response.Account!;

        // XPAY-373 — caché de la resolución más reciente (ver comentario de
        // clase en PassportBrebLlave.cs). expires_at se parsea de forma
        // fail-closed: si Passport devuelve un formato no parseable, se
        // deja null en vez de asumir una fecha — un vencimiento ausente ya
        // es tratado como "vencido" por BrebPaymentRequestBuilder (XPAY-373),
        // nunca como "sin límite".
        llave.PassportResolutionId          = response.Id;
        llave.PassportResolutionExpiresAtUtc = ParseExpiresAt(response.ExpiresAt);

        llave.OwnerIdentificationType         = owner.IdentificationType;
        llave.OwnerIdentificationNumberMasked = MaskTail(owner.IdentificationNumber);
        llave.OwnerNameMasked                 = MaskOwnerName(owner);
        llave.ParticipantName                 = participant.Name;
        llave.ParticipantIdentificationNumber = MaskTail(participant.IdentificationNumber);
        llave.AccountType                     = account.AccountType;
        llave.AccountNumberMasked             = MaskTail(account.AccountNumber);

        llave.Estado             = "VALIDADA";
        llave.FechaValidacion    = nowUtc;
        llave.FechaActualizacion = nowUtc;
        llave.UpdatedByUsuario   = updatedByUsuario;
    }

    // XPAY-371 FASE 4 — DTO sanitizado para que la UI pueda mostrar
    // "Esta es la cuenta asociada a tu llave Bre-B" antes de confirmar un
    // retiro. NUNCA expone: identificación completa, número de cuenta
    // completo, resolution_id (correlación interna, no necesaria para la
    // UI), customer_id/account_id de Passport, tokens/secretos.
    public static MiLlaveResolveResponse ToSanitizedResponse(
        PassportBrebLlave llaveActualizada, PassportResolveKeyResponse response)
    {
        ArgumentNullException.ThrowIfNull(llaveActualizada);
        ArgumentNullException.ThrowIfNull(response);

        return new MiLlaveResolveResponse
        {
            IdBrebLlave              = llaveActualizada.IdBrebLlave,
            KeyType                  = llaveActualizada.KeyType,
            KeyValueMasked           = llaveActualizada.KeyValueMasked,
            Estado                   = llaveActualizada.Estado,
            ResolucionVerificadaPassport = true,
            TitularNombreMasked      = llaveActualizada.OwnerNameMasked ?? string.Empty,
            TitularIdentificacionTipo = llaveActualizada.OwnerIdentificationType,
            TitularIdentificacionMasked = llaveActualizada.OwnerIdentificationNumberMasked,
            EntidadFinanciera        = llaveActualizada.ParticipantName,
            TipoCuenta               = llaveActualizada.AccountType,
            CuentaMasked             = llaveActualizada.AccountNumberMasked,
            VigenteHasta             = response.ExpiresAt,
        };
    }

    // Nombre: se muestra el primer nombre completo y se enmascara el resto
    // (patrón "Juan P***"), igual de conservador que MaskGeneric en
    // BrebService — nunca el nombre completo del titular.
    private static string MaskOwnerName(PassportResolveKeyOwnerResponse owner)
    {
        if (!string.IsNullOrWhiteSpace(owner.BusinessName))
            return MaskTrailingWords(owner.BusinessName);

        var primerNombre = owner.FirstName?.Trim();
        if (string.IsNullOrWhiteSpace(primerNombre))
            return "***";

        return $"{primerNombre} ***";
    }

    private static string MaskTrailingWords(string value)
    {
        var trimmed = value.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace <= 0) return $"{trimmed} ***";
        return $"{trimmed[..firstSpace]} ***";
    }

    // XPAY-373 — parseo fail-closed de expires_at (ISO 8601 UTC, formato
    // confirmado en el fixture ya versionado de PassportKeyClientTests:
    // "2026-01-01T00:30:00.000000Z"). Cualquier valor no parseable produce
    // null (nunca lanza, nunca asume "sin vencimiento").
    private static DateTime? ParseExpiresAt(string? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(expiresAt)) return null;
        return DateTime.TryParse(
            expiresAt, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    // Mismo criterio que MaskGeneric (BrebService): últimos 4 caracteres
    // visibles, nunca el valor completo. null/"" -> null (nunca "***" para
    // un dato que Passport ni siquiera envió).
    private static string? MaskTail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return v.Length <= 4 ? "***" : $"***{v[^4..]}";
    }
}
