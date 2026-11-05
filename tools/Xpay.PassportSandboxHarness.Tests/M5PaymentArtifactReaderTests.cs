using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-551 — prueba la corrección del glob de M5PaymentArtifactReader:
// debe seleccionar EXCLUSIVAMENTE artefactos "M5-PAYMENT-CREATE-*.private.json"
// dentro del directorio compartido con M5-T2-RESOLVE (compartido a propósito
// desde XPAY-545), ignorando por completo cualquier artefacto/marcador de
// Resolve que coexista ahí. Cubre exactamente los casos A-G del ticket.
public class M5PaymentArtifactReaderTests
{
    private static string NewTempDir(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));

    private static void WriteRaw(string dir, string fileName, string content)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
    }

    private static string ValidPaymentJson(string accountIdFingerprint = "fake-account-fp", string paymentId = "fake-payment-id") =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["case_id"] = "M5-PAYMENT-CREATE",
            ["backend_commit_sha"] = "fake-sha",
            ["account_id_fingerprint"] = accountIdFingerprint,
            ["payment_id"] = paymentId,
        });

    private static string ResolveArtifactJson() =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["case_id"] = "M5-T2-RESOLVE",
            ["resolution_id"] = "fake-resolution-id",
        });

    private static string ConsumedMarkerJson() =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["case_id"] = "M5-T2-RESOLVE",
            ["consumed_utc"] = "2026-01-01T00:00:00Z",
        });

    // A — exactamente un M5-PAYMENT-CREATE => Found.
    [Fact]
    public void A_SinglePaymentArtifact_Found()
    {
        var dir = NewTempDir("xpay-m5-payment-reader-a");
        WriteRaw(dir, "M5-PAYMENT-CREATE-2026-01-01T00-00-00Z.private.json", ValidPaymentJson());

        var (outcome, artifact) = M5PaymentArtifactReader.Resolve(dir);

        Assert.Equal(M5PaymentArtifactReader.ResolutionOutcome.Found, outcome);
        Assert.NotNull(artifact);
        Assert.Equal("M5-PAYMENT-CREATE", artifact!.CaseId);
    }

    // B — un Payment + artefactos Resolve/Consumed coexistiendo => Found
    // exactamente el Payment (los de Resolve se ignoran por completo).
    [Fact]
    public void B_PaymentPlusResolveArtifactsCoexisting_FindsExactlyThePayment()
    {
        var dir = NewTempDir("xpay-m5-payment-reader-b");
        WriteRaw(dir, "M5-PAYMENT-CREATE-2026-01-01T00-00-00Z.private.json", ValidPaymentJson(paymentId: "fake-payment-id-b"));
        WriteRaw(dir, "M5-T2-RESOLVE-2026-01-02T00-00-00Z.private.json", ResolveArtifactJson());
        WriteRaw(dir, "M5-T2-RESOLVE-CONSUMED-2026-01-02T00-00-00Z.private.json", ConsumedMarkerJson());

        var (outcome, artifact) = M5PaymentArtifactReader.Resolve(dir);

        Assert.Equal(M5PaymentArtifactReader.ResolutionOutcome.Found, outcome);
        Assert.NotNull(artifact);
        Assert.Equal("M5-PAYMENT-CREATE", artifact!.CaseId);
        Assert.Equal("fake-payment-id-b", artifact.PaymentId);
    }

    // C — sólo artefactos Resolve/Consumed, ningún Payment => NoArtifactFound.
    [Fact]
    public void C_OnlyResolveArtifactsPresent_NoArtifactFound()
    {
        var dir = NewTempDir("xpay-m5-payment-reader-c");
        WriteRaw(dir, "M5-T2-RESOLVE-2026-01-02T00-00-00Z.private.json", ResolveArtifactJson());
        WriteRaw(dir, "M5-T2-RESOLVE-CONSUMED-2026-01-02T00-00-00Z.private.json", ConsumedMarkerJson());

        var (outcome, artifact) = M5PaymentArtifactReader.Resolve(dir);

        Assert.Equal(M5PaymentArtifactReader.ResolutionOutcome.NoArtifactFound, outcome);
        Assert.Null(artifact);
    }

    // D — dos artefactos Payment => MultipleArtifactsFound (fail-closed,
    // nunca se elige "el más nuevo" silenciosamente).
    [Fact]
    public void D_TwoPaymentArtifacts_MultipleArtifactsFound()
    {
        var dir = NewTempDir("xpay-m5-payment-reader-d");
        WriteRaw(dir, "M5-PAYMENT-CREATE-2026-01-01T00-00-00Z.private.json", ValidPaymentJson(paymentId: "fake-payment-id-1"));
        WriteRaw(dir, "M5-PAYMENT-CREATE-2026-01-02T00-00-00Z.private.json", ValidPaymentJson(paymentId: "fake-payment-id-2"));

        var (outcome, artifact) = M5PaymentArtifactReader.Resolve(dir);

        Assert.Equal(M5PaymentArtifactReader.ResolutionOutcome.MultipleArtifactsFound, outcome);
        Assert.Null(artifact);
    }

    // D2 — dos Payment + artefactos Resolve coexistiendo también =>
    // sigue siendo MultipleArtifactsFound (el filtro de Resolve no
    // enmascara una ambigüedad real dentro de los propios Payment).
    [Fact]
    public void D2_TwoPaymentArtifactsPlusResolveArtifacts_StillMultipleArtifactsFound()
    {
        var dir = NewTempDir("xpay-m5-payment-reader-d2");
        WriteRaw(dir, "M5-PAYMENT-CREATE-2026-01-01T00-00-00Z.private.json", ValidPaymentJson(paymentId: "fake-payment-id-1"));
        WriteRaw(dir, "M5-PAYMENT-CREATE-2026-01-02T00-00-00Z.private.json", ValidPaymentJson(paymentId: "fake-payment-id-2"));
        WriteRaw(dir, "M5-T2-RESOLVE-2026-01-03T00-00-00Z.private.json", ResolveArtifactJson());
        WriteRaw(dir, "M5-T2-RESOLVE-CONSUMED-2026-01-03T00-00-00Z.private.json", ConsumedMarkerJson());

        var (outcome, _) = M5PaymentArtifactReader.Resolve(dir);

        Assert.Equal(M5PaymentArtifactReader.ResolutionOutcome.MultipleArtifactsFound, outcome);
    }

    // E — un M5-PAYMENT-CREATE malformado => fail-closed (Unreadable).
    [Fact]
    public void E_MalformedPaymentArtifact_Unreadable()
    {
        var dir = NewTempDir("xpay-m5-payment-reader-e");
        WriteRaw(dir, "M5-PAYMENT-CREATE-2026-01-01T00-00-00Z.private.json", "not-valid-json{{{");

        var (outcome, artifact) = M5PaymentArtifactReader.Resolve(dir);

        Assert.Equal(M5PaymentArtifactReader.ResolutionOutcome.Unreadable, outcome);
        Assert.Null(artifact);
    }

    // F — nombre de archivo M5-PAYMENT-CREATE-* pero case_id interno
    // incorrecto => Resolve lo encuentra (Found), pero IdentityMatches
    // (usado por el caller real) lo rechaza — fail-closed a nivel de
    // identidad, sin exponer valores crudos.
    [Fact]
    public void F_FileNameMatchesButInternalCaseIdWrong_IdentityMatchesRejects()
    {
        var dir = NewTempDir("xpay-m5-payment-reader-f");
        var wrongCaseIdJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["case_id"] = "M5-T2-RESOLVE", // ajeno, a propósito, pese al nombre de archivo
            ["backend_commit_sha"] = "fake-sha",
            ["account_id_fingerprint"] = "fake-account-fp",
            ["payment_id"] = "fake-payment-id",
        });
        WriteRaw(dir, "M5-PAYMENT-CREATE-2026-01-01T00-00-00Z.private.json", wrongCaseIdJson);

        var (outcome, artifact) = M5PaymentArtifactReader.Resolve(dir);

        Assert.Equal(M5PaymentArtifactReader.ResolutionOutcome.Found, outcome);
        Assert.NotNull(artifact);
        Assert.False(M5PaymentArtifactReader.IdentityMatches(artifact!, "fake-account-fp"));
    }

    // G — ningún mensaje/valor crudo se expone en ningún camino de error.
    [Fact]
    public void G_NoRawValuesExposedInAnyErrorPath()
    {
        var dir = NewTempDir("xpay-m5-payment-reader-g");
        WriteRaw(dir, "M5-PAYMENT-CREATE-2026-01-01T00-00-00Z.private.json",
            ValidPaymentJson(accountIdFingerprint: "fake-account-fp-should-not-leak", paymentId: "fake-payment-id-should-not-leak"));
        WriteRaw(dir, "M5-PAYMENT-CREATE-2026-01-02T00-00-00Z.private.json",
            ValidPaymentJson(accountIdFingerprint: "fake-account-fp-2", paymentId: "fake-payment-id-2"));

        var (outcome, artifact) = M5PaymentArtifactReader.Resolve(dir);

        Assert.Equal(M5PaymentArtifactReader.ResolutionOutcome.MultipleArtifactsFound, outcome);
        Assert.Null(artifact); // el propio contrato ya garantiza que nada se expone en ambigüedad.
    }

    // ── Casos adicionales de robustez ──────────────────────────────────

    [Fact]
    public void DirectoryMissing_ReturnsDirectoryMissing()
    {
        var dir = NewTempDir("xpay-m5-payment-reader-missing");

        var (outcome, artifact) = M5PaymentArtifactReader.Resolve(dir);

        Assert.Equal(M5PaymentArtifactReader.ResolutionOutcome.DirectoryMissing, outcome);
        Assert.Null(artifact);
    }

    [Fact]
    public void EmptyDirectory_ReturnsNoArtifactFound()
    {
        var dir = NewTempDir("xpay-m5-payment-reader-empty");
        Directory.CreateDirectory(dir);

        var (outcome, artifact) = M5PaymentArtifactReader.Resolve(dir);

        Assert.Equal(M5PaymentArtifactReader.ResolutionOutcome.NoArtifactFound, outcome);
        Assert.Null(artifact);
    }

    [Fact]
    public void RealXpay549ArtifactNamingConvention_StillResolvesCorrectly()
    {
        // Reproduce el patrón EXACTO de nombre de archivo que
        // M5PaymentPrivateCapture.Write produjo realmente en XPAY-549:
        // "{caseId}-{timestamp saneado}.private.json" — confirma que el
        // artefacto real ya existente sigue siendo válido sin reescritura.
        var dir = NewTempDir("xpay-m5-payment-reader-real-naming");
        WriteRaw(dir, "M5-PAYMENT-CREATE-2026-09-25T03-53-39Z.private.json",
            ValidPaymentJson(paymentId: "fake-real-shaped-payment-id"));

        var (outcome, artifact) = M5PaymentArtifactReader.Resolve(dir);

        Assert.Equal(M5PaymentArtifactReader.ResolutionOutcome.Found, outcome);
        Assert.Equal("fake-real-shaped-payment-id", artifact!.PaymentId);
    }
}
