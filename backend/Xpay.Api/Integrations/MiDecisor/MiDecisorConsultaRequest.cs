using System.Text.Json.Serialization;

namespace Xpay.Api.Integrations.MiDecisor;

// Body de consulta de MiDecisor — contrato oficial (Swagger MiDecisor.yaml,
// schema `ConsultaRequest`). Los TRES campos son obligatorios en el contrato.
//
// Los nombres JSON del proveedor se fijan EXPLÍCITAMENTE con
// [JsonPropertyName]: el cliente de M2 hará `JsonSerializer.Serialize` con
// sus propias opciones y NO debe depender de ninguna naming policy implícita
// (la de MVC sólo rige los formatters de los endpoints propios de XPAY).
//
// Deliberadamente NO incluye monto solicitado, producto, convenio ni
// consentimiento: no pertenecen al body documentado de MiDecisor.
//
//   tipoIdentificacion  — código string "1".."9". Para Persona Natural:
//                         1=CC, 4=CE, 5=PAS, 6=CD, 7=TI, 8=DNI, 9=PEP.
//                         (Ver TipoDocumentoMiDecisorMapper.)
//   numeroIdentificacion — sólo dígitos, sin puntos ni espacios,
//                          longitud 3–13.
//   apellidoRazonSocial  — para PN: primer apellido. Sin caracteres
//                          especiales, sin números.
//
// La validación/normalización de estos valores NO ocurre aquí (es M2/M3);
// este record sólo fija la forma del contrato.
public sealed record MiDecisorConsultaRequest(
    [property: JsonPropertyName("tipoIdentificacion")]  string TipoIdentificacion,
    [property: JsonPropertyName("numeroIdentificacion")] string NumeroIdentificacion,
    [property: JsonPropertyName("apellidoRazonSocial")]  string ApellidoRazonSocial);
