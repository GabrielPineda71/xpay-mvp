using System.Text.Json;
using Xpay.Api.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

// ══════════════════════════════════════════════════════════════════════════
// M2.4a — extensión de captura P0 (diseño 175 + 176 + 177). Tests PUROS
// (sin SQL, sin red, sin proveedor) de los 3 helpers:
//   - CarteraP0ProviderRawProjector  (semantic raw projection + fail-closed)
//   - CarteraDualPathResolver        (PRESENTE / AUSENTE / CONFLICTO)
//   - CarteraComportamientoVectorProjector (json + count, sin cap, sin truncar)
// ══════════════════════════════════════════════════════════════════════════

public sealed class CarteraP0ProviderRawProjectorTests
{
    private static string Proyectar(
        string? tipoDoc = "CC",
        string? edb = "Vigente", string? eid = "Vigente",
        string? rdb = "46-55", string? rid = "46-55",
        string? anio = "2025", string? mes = "11", string? dia = "5",
        bool bloque = true,
        IReadOnlyList<CarteraComportamientoVectorItemRaw>? vector = null)
        => CarteraP0ProviderRawProjector.Proyectar(
            tipoDoc, edb, eid, rdb, rid, anio, mes, dia, bloque, vector);

    [Fact]
    public void RoundTrip_preservaValoresExactos()
    {
        var vector = new List<CarteraComportamientoVectorItemRaw>
        {
            new("2024-11", "N"),
            new("2024-11", "1"),   // duplicado conflictivo
            new("2025-1", "-"),
            new("2025-2", ""),
            new("2025-3", " "),
            new("XXXX", "Z"),
            new(null, null),
        };
        var json = Proyectar(tipoDoc: "CÉDULA DE CIUDADANÍA", edb: "  Vigente  ", eid: "", vector: vector);
        var proj = CarteraP0ProviderRawProjector.Deserializar(json);

        Assert.Equal("CÉDULA DE CIUDADANÍA", proj.TipoDocumento);
        Assert.Equal("  Vigente  ", proj.EstadoDocumentoDatosBasicos);
        Assert.Equal("", proj.EstadoDocumentoInfoDemografica);
        Assert.Equal("2025", proj.AnioConsulta);
        Assert.True(proj.ComportamientoVectorPresente);
        Assert.NotNull(proj.ComportamientoVector);
        Assert.Equal(7, proj.ComportamientoVector!.Count);
        Assert.Equal("2024-11", proj.ComportamientoVector[0].AnioMes);
        Assert.Equal("1", proj.ComportamientoVector[1].Comportamiento);   // orden + duplicado preservados
        Assert.Equal("-", proj.ComportamientoVector[2].Comportamiento);
        Assert.Equal("", proj.ComportamientoVector[3].Comportamiento);
        Assert.Equal(" ", proj.ComportamientoVector[4].Comportamiento);
        Assert.Equal("Z", proj.ComportamientoVector[5].Comportamiento);
        Assert.Null(proj.ComportamientoVector[6].AnioMes);
        Assert.Null(proj.ComportamientoVector[6].Comportamiento);
    }

    [Fact]
    public void NullTipoDoc_seConserva()
    {
        var proj = CarteraP0ProviderRawProjector.Deserializar(Proyectar(tipoDoc: null));
        Assert.Null(proj.TipoDocumento);
    }

    [Fact]
    public void BloqueAusente_vectorNull()
    {
        var proj = CarteraP0ProviderRawProjector.Deserializar(Proyectar(bloque: false, vector: null));
        Assert.False(proj.ComportamientoVectorPresente);
        Assert.Null(proj.ComportamientoVector);
    }

    [Fact]
    public void BloquePresenteVacio_vectorListaVacia()
    {
        var proj = CarteraP0ProviderRawProjector.Deserializar(
            Proyectar(bloque: true, vector: new List<CarteraComportamientoVectorItemRaw>()));
        Assert.True(proj.ComportamientoVectorPresente);
        Assert.NotNull(proj.ComportamientoVector);
        Assert.Empty(proj.ComportamientoVector!);
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("{ malformado ")]
    [InlineData("null")]
    public void StagingCorrupto_lanzaInvariante(string json)
        => Assert.Throws<CarteraConsumoResultadoInvarianteException>(
            () => CarteraP0ProviderRawProjector.Deserializar(json));

    [Fact]
    public void MasDe120Elementos_sePreservanCompletos_sinCap()
    {
        var vector = new List<CarteraComportamientoVectorItemRaw>();
        for (var i = 0; i < 300; i++)
            vector.Add(new($"2000-{(i % 12) + 1}", "N"));

        var json = Proyectar(vector: vector);
        var proj = CarteraP0ProviderRawProjector.Deserializar(json);
        Assert.Equal(300, proj.ComportamientoVector!.Count);
    }
}

public sealed class CarteraDualPathResolverTests
{
    [Theory]
    [InlineData(null, null, "AUSENTE")]
    [InlineData("", "", "AUSENTE")]
    [InlineData("   ", " ", "AUSENTE")]
    [InlineData("Vigente", null, "PRESENTE")]
    [InlineData(null, "Vigente", "PRESENTE")]
    [InlineData("Vigente", "", "PRESENTE")]
    [InlineData("Vigente", "Vigente", "PRESENTE")]
    [InlineData("Vigente", " Vigente ", "PRESENTE")]   // iguales sólo tras Trim
    [InlineData("Vigente", "Cancelada", "CONFLICTO")]
    [InlineData("46-55", "36-45", "CONFLICTO")]
    public void Resolver_clasificaEstructuralmente(string? a, string? b, string esperado)
        => Assert.Equal(esperado, CarteraDualPathResolver.ResolverTexto(a, b));

    [Fact]
    public void ComparacionEsOrdinal_noDependienteDeCultura()
    {
        // "Vigente" vs "VIGENTE" → distinto en comparación ordinal → CONFLICTO.
        Assert.Equal("CONFLICTO", CarteraDualPathResolver.ResolverTexto("Vigente", "VIGENTE"));
    }
}

public sealed class CarteraComportamientoVectorProjectorTests
{
    [Fact]
    public void BloqueAusente_jsonYcountNull()
    {
        var (json, count) = CarteraComportamientoVectorProjector.Proyectar(false, null);
        Assert.Null(json);
        Assert.Null(count);
    }

    [Fact]
    public void PresenteVacio_jsonArrayVacio_count0()
    {
        var (json, count) = CarteraComportamientoVectorProjector.Proyectar(
            true, new List<CarteraComportamientoVectorItemRaw>());
        Assert.Equal("[]", json);
        Assert.Equal(0, count);
    }

    [Fact]
    public void PresenteVacio_conListaNull_jsonArrayVacio_count0()
    {
        var (json, count) = CarteraComportamientoVectorProjector.Proyectar(true, null);
        Assert.Equal("[]", json);
        Assert.Equal(0, count);
    }

    [Fact]
    public void ConElementos_preservaOrdenDuplicadosYCrudos_countExacto()
    {
        var items = new List<CarteraComportamientoVectorItemRaw>
        {
            new("2024-11", "N"),
            new("2024-11", "N"),   // duplicado idéntico — NO se deduplica
            new("2024-11", "3"),   // duplicado conflictivo — NO se resuelve
            new("2025-1", "-"),
            new("2025-2", ""),
            new("2025-3", " "),
            new("bad-anio", "Z"),
            new(null, null),
        };
        var (json, count) = CarteraComportamientoVectorProjector.Proyectar(true, items);
        Assert.Equal(8, count);

        using var doc = JsonDocument.Parse(json!);
        var arr = doc.RootElement;
        Assert.Equal(8, arr.GetArrayLength());
        Assert.Equal("N", arr[0].GetProperty("Comportamiento").GetString());
        Assert.Equal("3", arr[2].GetProperty("Comportamiento").GetString());
        Assert.Equal("-", arr[3].GetProperty("Comportamiento").GetString());
        Assert.Equal("", arr[4].GetProperty("Comportamiento").GetString());
        Assert.Equal(" ", arr[5].GetProperty("Comportamiento").GetString());
        Assert.Equal(JsonValueKind.Null, arr[7].GetProperty("AnioMes").ValueKind);
    }

    [Fact]
    public void MuchosElementos_noSeTrunca()
    {
        var items = new List<CarteraComportamientoVectorItemRaw>();
        for (var i = 0; i < 500; i++)
            items.Add(new($"2000-{(i % 12) + 1}", "N"));

        var (json, count) = CarteraComportamientoVectorProjector.Proyectar(true, items);
        Assert.Equal(500, count);
        using var doc = JsonDocument.Parse(json!);
        Assert.Equal(500, doc.RootElement.GetArrayLength());
    }
}
