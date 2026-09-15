using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-328 — tests OFFLINE de KeySelector.SelectExactMatch. Ningún dato real:
// todos los account_id/key_value son sintéticos, construidos sólo para
// ejercitar el criterio de match (account_id+key_type+key_value exactos,
// simultáneamente).
public class KeySelectorTests
{
    private const string ExpectedAccountId = "synthetic-account-id-M3T1";
    private const PassportKeyType ExpectedKeyType = PassportKeyType.BCODE;
    private const string ExpectedKeyValue = "0099999999";

    private static PassportKeyResponse SyntheticKey(
        string id, string accountId, string keyType, string keyValue) => new()
    {
        Id = id,
        Status = "ACTIVE",
        AccountId = accountId,
        Key = new PassportKeyResponseDetail { KeyType = keyType, KeyValue = keyValue },
    };

    private static PassportListKeysResponse ResponseWith(params PassportKeyResponse[] keys) =>
        new() { Keys = keys.ToList() };

    // 1 — 1 resultado exacto → ExactlyOneMatch.
    [Fact]
    public void SelectExactMatch_OneExactCandidate_ReturnsExactlyOneMatch()
    {
        var response = ResponseWith(
            SyntheticKey("synthetic-key-id-target", ExpectedAccountId, "BCODE", ExpectedKeyValue));

        var result = KeySelector.SelectExactMatch(response, ExpectedAccountId, ExpectedKeyType, ExpectedKeyValue);

        Assert.Equal(KeySelectionOutcome.ExactlyOneMatch, result.Outcome);
        Assert.NotNull(result.SelectedKey);
        Assert.Equal("synthetic-key-id-target", result.SelectedKey!.Id);
    }

    // 2 — 0 resultados → NoMatch.
    [Fact]
    public void SelectExactMatch_NoCandidates_ReturnsNoMatch()
    {
        var response = ResponseWith();

        var result = KeySelector.SelectExactMatch(response, ExpectedAccountId, ExpectedKeyType, ExpectedKeyValue);

        Assert.Equal(KeySelectionOutcome.NoMatch, result.Outcome);
        Assert.Null(result.SelectedKey);
    }

    // 3 — 2 resultados exactos → Ambiguous (nunca se elige "el primero").
    [Fact]
    public void SelectExactMatch_TwoExactCandidates_ReturnsAmbiguous()
    {
        var response = ResponseWith(
            SyntheticKey("synthetic-key-id-1", ExpectedAccountId, "BCODE", ExpectedKeyValue),
            SyntheticKey("synthetic-key-id-2", ExpectedAccountId, "BCODE", ExpectedKeyValue));

        var result = KeySelector.SelectExactMatch(response, ExpectedAccountId, ExpectedKeyType, ExpectedKeyValue);

        Assert.Equal(KeySelectionOutcome.Ambiguous, result.Outcome);
        Assert.Null(result.SelectedKey);
    }

    // 4 — mismo BCODE, account distinto → no match.
    [Fact]
    public void SelectExactMatch_SameKeyValueDifferentAccount_ReturnsNoMatch()
    {
        var response = ResponseWith(
            SyntheticKey("synthetic-key-id-other-account", "synthetic-account-id-OTHER", "BCODE", ExpectedKeyValue));

        var result = KeySelector.SelectExactMatch(response, ExpectedAccountId, ExpectedKeyType, ExpectedKeyValue);

        Assert.Equal(KeySelectionOutcome.NoMatch, result.Outcome);
    }

    // 5 — mismo account, BCODE distinto → no match.
    [Fact]
    public void SelectExactMatch_SameAccountDifferentKeyValue_ReturnsNoMatch()
    {
        var response = ResponseWith(
            SyntheticKey("synthetic-key-id-other-value", ExpectedAccountId, "BCODE", "0011111111"));

        var result = KeySelector.SelectExactMatch(response, ExpectedAccountId, ExpectedKeyType, ExpectedKeyValue);

        Assert.Equal(KeySelectionOutcome.NoMatch, result.Outcome);
    }

    // 6 — mismo account/value, key_type distinto → no match.
    [Fact]
    public void SelectExactMatch_SameAccountAndValueDifferentKeyType_ReturnsNoMatch()
    {
        var response = ResponseWith(
            SyntheticKey("synthetic-key-id-other-type", ExpectedAccountId, "ALPHA", ExpectedKeyValue));

        var result = KeySelector.SelectExactMatch(response, ExpectedAccountId, ExpectedKeyType, ExpectedKeyValue);

        Assert.Equal(KeySelectionOutcome.NoMatch, result.Outcome);
    }

    // 7 — lista con varios elementos, sólo uno satisface los tres criterios
    // simultáneamente → ExactlyOneMatch correcto (no confundido por los
    // demás candidatos parcialmente coincidentes).
    [Fact]
    public void SelectExactMatch_MultipleCandidatesOnlyOneFullMatch_ReturnsThatOne()
    {
        var response = ResponseWith(
            SyntheticKey("synthetic-key-id-wrong-account", "synthetic-account-id-OTHER", "BCODE", ExpectedKeyValue),
            SyntheticKey("synthetic-key-id-wrong-value", ExpectedAccountId, "BCODE", "0022222222"),
            SyntheticKey("synthetic-key-id-wrong-type", ExpectedAccountId, "ALPHA", ExpectedKeyValue),
            SyntheticKey("synthetic-key-id-TARGET", ExpectedAccountId, "BCODE", ExpectedKeyValue));

        var result = KeySelector.SelectExactMatch(response, ExpectedAccountId, ExpectedKeyType, ExpectedKeyValue);

        Assert.Equal(KeySelectionOutcome.ExactlyOneMatch, result.Outcome);
        Assert.Equal("synthetic-key-id-TARGET", result.SelectedKey!.Id);
    }

    // ── Guards de input ─────────────────────────────────────────────────

    [Fact]
    public void SelectExactMatch_NullResponse_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => KeySelector.SelectExactMatch(null!, ExpectedAccountId, ExpectedKeyType, ExpectedKeyValue));
    }

    [Fact]
    public void SelectExactMatch_BlankExpectedAccountId_Throws()
    {
        var response = ResponseWith();
        Assert.Throws<ArgumentException>(
            () => KeySelector.SelectExactMatch(response, "   ", ExpectedKeyType, ExpectedKeyValue));
    }

    [Fact]
    public void SelectExactMatch_BlankExpectedKeyValue_Throws()
    {
        var response = ResponseWith();
        Assert.Throws<ArgumentException>(
            () => KeySelector.SelectExactMatch(response, ExpectedAccountId, ExpectedKeyType, "   "));
    }
}
