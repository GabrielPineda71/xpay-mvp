using System.Text.Json.Serialization;

namespace Xpay.PassportSandboxHarness;

// XPAY-325 — schema del expediente de evidencia de certificación Passport,
// tal como se documenta en docs/certificacion/passport-breb/README.md.
// Nombres de campo EXACTOS al diseño autorizado (snake_case en el JSON
// final): case_id, executed_at_utc, environment, backend_commit_sha,
// operation, http_status, result, request_sanitized, response_sanitized,
// automated_test_reference, notes, review_status.
//
// request_sanitized/response_sanitized son diccionarios de valores ya
// saneados (fingerprints, strings seguros, o el marcador REDACTED) — NUNCA
// contienen el valor original de un campo sensible. La responsabilidad de
// sanear vive en el llamador (Program.cs), no en este DTO — este DTO sólo
// serializa lo que ya le entregan saneado, y valida invariantes mínimas
// (backend_commit_sha/case_id no vacíos) antes de permitir su escritura vía
// EvidenceWriter.
public sealed record EvidenceRecord(
    [property: JsonPropertyName("case_id")]                 string CaseId,
    [property: JsonPropertyName("executed_at_utc")]          string ExecutedAtUtc,
    [property: JsonPropertyName("environment")]              string Environment,
    [property: JsonPropertyName("backend_commit_sha")]       string BackendCommitSha,
    [property: JsonPropertyName("operation")]                string Operation,
    [property: JsonPropertyName("http_status")]              int? HttpStatus,
    [property: JsonPropertyName("result")]                   string Result,
    [property: JsonPropertyName("request_sanitized")]        IReadOnlyDictionary<string, object?> RequestSanitized,
    [property: JsonPropertyName("response_sanitized")]       IReadOnlyDictionary<string, object?> ResponseSanitized,
    [property: JsonPropertyName("automated_test_reference")] string? AutomatedTestReference,
    [property: JsonPropertyName("notes")]                    string? Notes,
    [property: JsonPropertyName("review_status")]            string ReviewStatus)
{
    // XPAY-325 — únicamente los estados explícitamente autorizados; no se
    // agregan estados adicionales no solicitados.
    public const string EnvironmentSandbox = "sandbox";

    public const string ResultPass = "PASS";
    public const string ResultFail = "FAIL";

    public const string ReviewPendingPassportReview = "PENDING_PASSPORT_REVIEW";

    // XPAY-325 FASE 12 — distinción interna (NO forma parte del schema
    // público) entre un bloqueo LOCAL (config/host/guard, antes de HTTP) y
    // un fallo real de Passport. Se expresa mediante `notes`, no mediante un
    // campo nuevo — evita complicar el schema público sin necesidad.
    public const string NotePrefixLocalBlocked      = "LOCAL_BLOCKED: ";
    public const string NotePrefixPassportHttpFailure = "PASSPORT_HTTP_FAILURE: ";
}
