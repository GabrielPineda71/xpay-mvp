namespace Xpay.PassportSandboxHarness;

// XPAY-572 — resolution_id sintético, DETERMINISTA e inválido para
// M5-T5-INVALID-RESOLUTION (Create Bre-B Payment con resolution_id
// incorrecto/inexistente). Función PURA — no hace I/O, no llama a
// Passport, no lee ningún valor de ~/.passport-sandbox.env ni de ningún
// artifact privado — nunca deriva de ningún resolution_id real ni de su
// fingerprint (SHA-256 no es reversible, y aunque lo fuera, este valor NO
// se calcula a partir de ningún dato real). Mismo criterio exacto que
// InvalidCustomerIdGenerator (XPAY-471, M4-T3-C).
//
// DETERMINISTA a propósito: M5-T5-F exige reproducibilidad exacta entre
// ejecuciones/auditorías — el mismo resolution_id sintético e inválido
// debe poder documentarse y reconocerse en cualquier evidencia futura.
//
// Con forma de UUID (36 caracteres) porque es el shape OBSERVADO
// localmente del resolution_id real de Sandbox (confirmado offline,
// XPAY-572 — comparación de formato contra artifacts privados archivados,
// sin exponer ningún valor real) — esto NO afirma que Passport exija
// formato UUID; sólo que un valor con esa forma alcanza la validación
// FUNCIONAL de Passport en vez de ser rechazado antes por un chequeo de
// formato local inexistente (PassportPaymentClient.CreateBrebPaymentAsync
// sólo valida que resolution_id no esté vacío).
//
// Terminación deliberadamente DISTINTA de InvalidCustomerIdGenerator
// ("...000000000001") para nunca confundir ambos sintéticos entre sí en
// ninguna evidencia — este usa "...000000000002".
//
// Verificado offline (XPAY-572, sin llamada real) contra los
// resolution_id reales conocidos localmente (artifacts archivados +
// valor configurado en ~/.passport-sandbox.env): SIN colisión.
//
// El valor en sí NUNCA es secreto ni PII — es deliberadamente reconocible
// como sintético — pero de todas formas nunca se imprime como valor
// completo en stdout/evidencia pública (mismo criterio conservador que el
// resto del harness aplica a cualquier identificador, real o no); sólo su
// fingerprint aparece en evidence.json.
public static class InvalidResolutionIdGenerator
{
    private const string Value = "00000000-0000-0000-0000-000000000002";

    public static string Generate() => Value;
}
