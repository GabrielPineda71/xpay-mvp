namespace Xpay.PassportSandboxHarness;

// XPAY-471 — customer_id sintético, DETERMINISTA e inválido para M4-T3-C
// (Create QR Static con customer_id incorrecto). Función PURA — no hace
// I/O, no llama a Passport, no lee ningún valor de ~/.passport-sandbox.env
// ni de PASSPORT_TEST_CUSTOMER_ID (nunca deriva de él, ni de su
// fingerprint — SHA-256 no es reversible, y aunque lo fuera, este valor NO
// se calcula a partir de ningún dato real).
//
// DETERMINISTA a propósito (a diferencia de QrCodeReferenceGenerator, que
// es aleatorio): M4-T3-C exige reproducibilidad exacta entre ejecuciones —
// el mismo customer_id sintético e inválido debe poder documentarse y
// reconocerse en cualquier evidencia futura, no una unicidad por ejecución.
//
// Con forma de UUID (36 caracteres) porque es el shape OBSERVADO
// localmente del customer_id real de Sandbox (confirmado en auditorías
// previas, XPAY-459/460/470) — esto NO afirma que Passport exija
// formato UUID; no existe documentación local que lo confirme. La
// justificación correcta, y la única que este código declara, es:
// "synthetic deterministic invalid customer identifier shaped consistently
// with the observed Sandbox fixture" — un valor con la MISMA FORMA que el
// dato real observado, para que la solicitud alcance la validación
// FUNCIONAL de Passport (que sí exige, comprobado en PassportQrClient.
// ValidateDecodeRequest / Validate, únicamente que no esté vacío) en vez de
// ser rechazada antes por un chequeo de formato local inexistente.
//
// El valor en sí NUNCA es secreto ni PII — es deliberadamente reconocible
// como sintético (bloque final "...000000000001", nunca visto en un UUID
// real de producción) — pero de todas formas nunca se imprime como valor
// completo en stdout/evidencia (mismo criterio conservador que el resto
// del harness aplica a cualquier identificador, real o no).
public static class InvalidCustomerIdGenerator
{
    private const string Value = "00000000-0000-0000-0000-000000000001";

    public static string Generate() => Value;
}
