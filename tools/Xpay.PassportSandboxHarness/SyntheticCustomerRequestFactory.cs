using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-312 FASE 5 — construye un PassportLinkMerchantRequest 100% sintético
// para validar (en dry-run) que el request es construible/serializable
// contra el DTO productivo real. NUNCA usa PII real, NUNCA usa
// PASSPORT_TEST_IDENTIFICATION_NUMBER del archivo privado (esa variable
// existe para otro propósito y no se lee automáticamente aquí), NUNCA se
// hardcodea un dato real del usuario.
//
// PREREQUISITO ABIERTO, NO RESUELTO EN ESTA FASE: no está confirmado si
// Passport exige un NIT/identification_number de un set de datos aprobado
// específicamente para certificación Sandbox (a diferencia de un valor
// sintético cualquiera). Antes de la ejecución real (fase futura, fuera de
// XPAY-312) esto debe confirmarse explícitamente — no se inventa una
// respuesta aquí.
public static class SyntheticCustomerRequestFactory
{
    public static PassportLinkMerchantRequest BuildSynthetic() => new(
        BusinessName: "XPAY SANDBOX HARNESS TEST SAS",
        Email: "sandbox-harness-test@example.invalid",
        MobilePhoneNumber: "3000000000",
        IdentificationNumber: "000000000", // SYNTHETIC — no es un NIT real, no proviene de datos del proyecto
        MerchantCategoryCode: "5411",
        Address: new PassportMerchantAddressRequest(
            Line1: "Calle Sintetica 0-00",
            City: "Bogota",
            State: "Bogota D.C.",
            PostCode: "000000",
            Country: "CO"));
}
