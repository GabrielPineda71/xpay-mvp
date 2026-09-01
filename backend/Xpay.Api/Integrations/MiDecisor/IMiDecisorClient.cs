namespace Xpay.Api.Integrations.MiDecisor;

// Contrato desacoplado del cliente MiDecisor. M1: sólo la interfaz — NO hay
// implementación (ni HTTP, ni token, ni auth). No se registra en DI todavía;
// nada la resuelve en runtime hasta M2.
//
// Por diseño la interfaz NO recibe: credenciales, token, HttpClient,
// idUsuario, ni nada de EF/DbContext. No decide crédito. Sólo traduce una
// consulta de Persona Natural en un resultado normalizado.
//
// El endpoint concreto (unificado `/co/cs/midecisor/v1/client` vs. específico
// PN) NO se fija aquí — esa decisión queda para M2 (pendiente en 037).
public interface IMiDecisorClient
{
    // Consulta de riesgo para una Persona Natural. La implementación (M2)
    // obtendrá el token OAuth2, enviará el request y normalizará el envelope
    // a MiDecisorResultado. Lanza una excepción de dominio ante fallo de
    // transporte o respuesta no interpretable (sin exponer secretos/PII).
    Task<MiDecisorResultado> ConsultarPersonaNaturalAsync(
        MiDecisorConsultaRequest request,
        CancellationToken cancellationToken = default);
}
