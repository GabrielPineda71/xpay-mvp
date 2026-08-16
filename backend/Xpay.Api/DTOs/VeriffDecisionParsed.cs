namespace Xpay.Api.DTOs;

// Commit 4 — resultado puro de extraer el objeto "verification" de Veriff
// (Decision webhook o, en el futuro, GET /v1/sessions/{sessionId}/decision —
// misma forma documentada para ambos). Sin interpretación de negocio: ningún
// campo aquí decide IdentidadVerificada, mapea TipoDocumento, ni aplica
// reglas de Datacrédito — eso vive exclusivamente en la capa de negocio que
// consuma este record.
public sealed record VeriffDecisionParsed(
    string?   SessionId,
    string?   AttemptId,
    string?   Decision,
    string?   Reason,
    DateTime? DecisionTimeUtc,
    string?   VendorData,
    string?   FirstName,
    string?   LastName,
    string?   DocumentType,
    string?   DocumentNumber,
    DateOnly? DateOfBirth
);
