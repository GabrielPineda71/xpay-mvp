using System.Text.Json;

namespace Xpay.Api.Common;

// M2.4a (extensión de captura P0, diseño 175/176/177) — proyección durable del
// vector `comportamientoPago.vectorComportamiento[]` de MiDecisor.
//
// Elemento RAW del vector, tal cual lo entrega System.Text.Json: cada campo se
// conserva verbatim (incluye "", " ", whitespace, "-", códigos desconocidos,
// anioMes inválidos) o null cuando STJ entrega null.
public sealed record CarteraComportamientoVectorItemRaw(
    string? AnioMes,
    string? Comportamiento);

// Helper PURO (sin SQL, sin estado, determinista). NO normaliza, NO ordena, NO
// deduplica, NO valida política crediticia, NO trunca. NO existe un máximo de
// elementos propio de XPAY (corrección 177): todos los elementos que la
// infraestructura de deserialización haya producido se preservan.
public static class CarteraComportamientoVectorProjector
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        // Serialización canónica estable. Sin escape agresivo innecesario, pero
        // manteniendo seguridad por defecto de STJ.
        WriteIndented = false,
    };

    // Semántica de count / json:
    //   bloque comportamientoPago ausente  → (json = null, count = null)
    //   bloque presente, vector vacío/null → (json = "[]", count = 0)
    //   bloque presente, N elementos       → (json = proyección de N, count = N)
    public static (string? Json, int? Count) Proyectar(
        bool bloquePresente,
        IReadOnlyList<CarteraComportamientoVectorItemRaw>? items)
    {
        if (!bloquePresente)
            return (null, null);

        var lista = items ?? Array.Empty<CarteraComportamientoVectorItemRaw>();
        var json = JsonSerializer.Serialize(lista, JsonOpts);
        return (json, lista.Count);
    }
}
