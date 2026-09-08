using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xpay.Api.Common;

// M2.4a (extensión de captura P0, diseño 175/176/177) — "durable semantic raw
// projection" de los valores P0 de MiDecisor.
//
// IMPORTANTE: esto NO es el JSON original del proveedor ni "verbatim bytes". Se
// construye DESPUÉS de la deserialización de System.Text.Json y contiene SÓLO
// los valores P0 (nunca el envelope completo, nunca número de documento /
// nombres / fecha de nacimiento / dirección / teléfono / email / género).
//
// Preserva: strings exactos ("", " ", whitespace), null cuando STJ entrega
// null, el orden del vectorComportamiento y sus duplicados, valores
// desconocidos y anioMes inválidos. NO normaliza, NO ordena, NO deduplica, NO
// trunca.
public sealed record CarteraP0ProviderRawProjection(
    [property: JsonPropertyName("tipoDocumento")]                  string? TipoDocumento,
    [property: JsonPropertyName("estadoDocumentoDatosBasicos")]    string? EstadoDocumentoDatosBasicos,
    [property: JsonPropertyName("estadoDocumentoInfoDemografica")] string? EstadoDocumentoInfoDemografica,
    [property: JsonPropertyName("rangoEdadDatosBasicos")]          string? RangoEdadDatosBasicos,
    [property: JsonPropertyName("rangoEdadInfoDemografica")]       string? RangoEdadInfoDemografica,
    [property: JsonPropertyName("anioConsulta")]                   string? AnioConsulta,
    [property: JsonPropertyName("mesConsulta")]                    string? MesConsulta,
    [property: JsonPropertyName("diaConsulta")]                    string? DiaConsulta,
    [property: JsonPropertyName("comportamientoVectorPresente")]   bool    ComportamientoVectorPresente,
    [property: JsonPropertyName("comportamientoVector")]           IReadOnlyList<CarteraComportamientoVectorItemRaw>? ComportamientoVector);

public static class CarteraP0ProviderRawProjector
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    public static string Proyectar(
        string? tipoDocumento,
        string? estadoDocDatosBasicos,
        string? estadoDocInfoDemografica,
        string? rangoEdadDatosBasicos,
        string? rangoEdadInfoDemografica,
        string? anioConsulta,
        string? mesConsulta,
        string? diaConsulta,
        bool comportamientoVectorPresente,
        IReadOnlyList<CarteraComportamientoVectorItemRaw>? comportamientoVector)
    {
        var proj = new CarteraP0ProviderRawProjection(
            TipoDocumento:                  tipoDocumento,
            EstadoDocumentoDatosBasicos:    estadoDocDatosBasicos,
            EstadoDocumentoInfoDemografica: estadoDocInfoDemografica,
            RangoEdadDatosBasicos:          rangoEdadDatosBasicos,
            RangoEdadInfoDemografica:       rangoEdadInfoDemografica,
            AnioConsulta:                   anioConsulta,
            MesConsulta:                    mesConsulta,
            DiaConsulta:                    diaConsulta,
            ComportamientoVectorPresente:   comportamientoVectorPresente,
            ComportamientoVector:           comportamientoVectorPresente
                                                ? (comportamientoVector ?? Array.Empty<CarteraComportamientoVectorItemRaw>())
                                                : null);

        return JsonSerializer.Serialize(proj, JsonOpts);
    }

    // Fail-closed: una proyección de staging estructuralmente corrupta
    // (no parseable, o null) lanza CarteraConsumoResultadoInvarianteException.
    // La AUSENCIA de un dato del proveedor NO es corrupción (esos campos
    // quedan null en la proyección y son un snapshot válido).
    public static CarteraP0ProviderRawProjection Deserializar(string json)
    {
        CarteraP0ProviderRawProjection? proj;
        try
        {
            proj = JsonSerializer.Deserialize<CarteraP0ProviderRawProjection>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new CarteraConsumoResultadoInvarianteException(
                $"p0_provider_raw_json de staging no es interpretable: {ex.Message}");
        }

        return proj
            ?? throw new CarteraConsumoResultadoInvarianteException(
                "p0_provider_raw_json de staging deserializó a null.");
    }
}
