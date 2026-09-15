namespace Xpay.PassportSandboxHarness;

// XPAY-336 — construye el EvidenceRecord saneado para M3-T7 (intento de
// eliminar una llave que YA fue eliminada por M3-T5). Función PURA de
// transformación — no hace I/O, no llama a Passport, no escribe archivos
// (eso es EvidenceWriter) — 100% testeable offline.
//
// DELIBERADAMENTE UN TIPO SEPARADO de DeleteKeyEvidenceBuilder (M3-T5),
// aunque ambos casos invoquen exactamente el mismo
// IPassportKeyClient.DeleteKeyAsync: M3-T5 y M3-T7 son casos de
// certificación DISTINTOS (`case_id` distinto) con semántica distinta —
// nunca deben conflaturse en el mismo builder ni en la misma evidencia.
//
// ── SEMÁNTICA CRÍTICA (XPAY-336 §9/12) ──────────────────────────────────
// M3-T7 es una PRUEBA NEGATIVA: no existe en este repositorio (ni se
// encontró en la documentación local disponible) un contrato explícito
// que diga qué HTTP status "correcto" debe devolver Passport al intentar
// eliminar una llave ya eliminada — `M3_T7_EXPECTED_HTTP_CONTRACT=UNKNOWN`.
//
// Por tanto, este builder NUNCA interpreta el resultado transporte como un
// juicio de certificación:
//   - `Result=PASS` aquí significa ÚNICAMENTE "la llamada HTTP a Passport
//     completó sin excepción" (nivel transporte) — NO significa "Passport
//     se comportó correctamente al permitir un segundo Delete". Podría
//     ser, de hecho, el escenario B descrito en XPAY-336 (Passport
//     permitió inesperadamente una operación que debía rechazarse).
//   - `Result=FAIL` aquí significa ÚNICAMENTE "la llamada HTTP falló"
//     (nivel transporte) — NO significa "el caso de certificación falló".
//     Podría ser exactamente el escenario A esperado (Passport rechazó
//     correctamente el segundo Delete).
// Ambos casos incluyen una nota explícita en `notes` señalando que la
// interpretación de certificación (¿comportamiento esperado o inesperado
// de Passport?) requiere revisión contractual separada — nunca se codifica
// aquí qué HTTP status "cuenta" como comportamiento correcto.
public static class DeleteAlreadyDeletedKeyEvidenceBuilder
{
    public const string CaseId = "M3-T7";

    // Mismo endpoint que M3-T5 (es la misma operación HTTP) — la
    // diferencia está en el `case_id` y en las notas, nunca en el
    // placeholder de operación. NUNCA interpolar el key_id real aquí.
    public const string Operation = "DELETE /v1/keys/{key_id}";

    private const string ReviewNote =
        "M3-T7 (delete already-deleted key): resultado a nivel transporte únicamente — " +
        "la interpretación de certificación (¿comportamiento esperado o inesperado de Passport " +
        "ante un segundo Delete?) requiere revisión contractual explícita antes de aceptar/rechazar " +
        "este caso. No se asume aquí ningún HTTP status como 'correcto'.";

    public static EvidenceRecord BuildSuccess(
        string keyId,
        int? httpStatus,
        string commitSha,
        DateTime executedAtUtc,
        string? automatedTestReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        var requestSanitized = new Dictionary<string, object?>
        {
            ["key_id_fingerprint"] = Fingerprint.Compute(keyId),
        };

        // Igual que M3-T5: 204 No Content sin body — nunca se fabrica un
        // campo de respuesta que Passport no devolvió.
        var responseSanitized = new Dictionary<string, object?>();

        return new EvidenceRecord(
            CaseId: CaseId,
            ExecutedAtUtc: FormatTimestamp(executedAtUtc),
            Environment: EvidenceRecord.EnvironmentSandbox,
            BackendCommitSha: commitSha,
            Operation: Operation,
            HttpStatus: httpStatus,
            Result: EvidenceRecord.ResultPass,
            RequestSanitized: requestSanitized,
            ResponseSanitized: responseSanitized,
            AutomatedTestReference: automatedTestReference,
            Notes: ReviewNote,
            ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);
    }

    // XPAY-336 (mismo criterio XPAY-326/332/334) — bloqueo LOCAL (target
    // ausente, commit SHA no resoluble, u otra condición ANTES de intentar
    // HTTP): nunca se finge como respuesta de Passport.
    public static EvidenceRecord BuildLocalBlocked(string reason, string commitSha, DateTime executedAtUtc) => new(
        CaseId: CaseId,
        ExecutedAtUtc: FormatTimestamp(executedAtUtc),
        Environment: EvidenceRecord.EnvironmentSandbox,
        BackendCommitSha: commitSha,
        Operation: Operation,
        HttpStatus: null,
        Result: EvidenceRecord.ResultFail,
        RequestSanitized: new Dictionary<string, object?>(),
        ResponseSanitized: new Dictionary<string, object?>(),
        AutomatedTestReference: null,
        Notes: EvidenceRecord.NotePrefixLocalBlocked + reason,
        ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);

    // Fallo real de transporte (HTTP no-2xx, o error de protocolo). Ver
    // nota de clase: Result=FAIL aquí es SÓLO nivel transporte — para
    // M3-T7 podría ser precisamente el rechazo correctamente esperado.
    // Nunca filtra el response body ni el key_id.
    public static EvidenceRecord BuildPassportHttpFailure(
        string reason, int? httpStatus, string commitSha, DateTime executedAtUtc) => new(
        CaseId: CaseId,
        ExecutedAtUtc: FormatTimestamp(executedAtUtc),
        Environment: EvidenceRecord.EnvironmentSandbox,
        BackendCommitSha: commitSha,
        Operation: Operation,
        HttpStatus: httpStatus,
        Result: EvidenceRecord.ResultFail,
        RequestSanitized: new Dictionary<string, object?>(),
        ResponseSanitized: new Dictionary<string, object?>(),
        AutomatedTestReference: null,
        Notes: $"{EvidenceRecord.NotePrefixPassportHttpFailure}{reason} — {ReviewNote}",
        ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);

    private static string FormatTimestamp(DateTime executedAtUtc) =>
        executedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
