namespace Xpay.Api.DTOs;

// XPAY-451 — respuesta del preview de solo lectura GET /api/qr/resolver.
// Deliberadamente mínimo: solo lo necesario para que el pagador confirme a
// quién le está pagando ANTES del POST financiero. Nunca expone datos
// operativos internos (IdQr, estados de comercio/tienda distintos de
// ACTIVO ya se filtran en QrResolutionService — si no está ACTIVO, este
// DTO simplemente no se construye).
public record QrResolverResponse(
    string CodigoQr,
    long   IdComercio,
    string NombreComercio,
    long   IdTienda,
    string NombreTienda
);
