using System.Reflection;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

// XPAY-613 — pruebas ESTRUCTURALES del flujo M5-T5-E, independientes de
// cualquier ejecución:
//
//  (a) SEPARACIÓN DE PAGO: ninguno de los tres ejecutores de M5-T5-E puede
//      iniciar un Payment. No es una garantía condicional ("no lo llama si
//      X"), es estructural: estos tipos no reciben, no declaran y no
//      referencian IPassportPaymentClient en ningún miembro. Si alguien
//      añadiera esa dependencia en el futuro, estos tests fallan.
//      Reportado como PAYMENT_CAPABILITY_PRESENT=NO.
//
//  (b) WIRING CLI: cada fase exige su PROPIO flag de confirmación; el flag
//      de otra fase (o de M5-T5-D, o de M4) nunca autoriza esta.
public class M5T5DeletedKeySeparationAndWiringTests
{
    private static readonly Type[] M5T5EExecutors =
    [
        typeof(CreateM5T5DeletedKeyFixtureExecutor),
        typeof(DeleteM5T5FixtureKeyExecutor),
        typeof(ResolveM5T5DeletedKeyExecutor),
    ];

    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    // ══════════════════════════════════════════════════════════════════
    // (a) PAYMENT_CAPABILITY_PRESENT=NO
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void NoM5T5EExecutor_AcceptsThePaymentClientAsAParameter()
    {
        foreach (var executor in M5T5EExecutors)
        {
            foreach (var method in executor.GetMethods(AllMembers))
            {
                foreach (var parameter in method.GetParameters())
                {
                    Assert.False(
                        typeof(IPassportPaymentClient).IsAssignableFrom(parameter.ParameterType),
                        $"{executor.Name}.{method.Name} recibe IPassportPaymentClient — M5-T5-E nunca debe poder iniciar un Payment.");
                }
            }
        }
    }

    [Fact]
    public void NoM5T5EExecutor_DeclaresThePaymentClientAsAFieldOrReturnsIt()
    {
        foreach (var executor in M5T5EExecutors)
        {
            foreach (var field in executor.GetFields(AllMembers))
            {
                Assert.False(
                    typeof(IPassportPaymentClient).IsAssignableFrom(field.FieldType),
                    $"{executor.Name} declara un campo IPassportPaymentClient.");
            }

            foreach (var method in executor.GetMethods(AllMembers))
            {
                Assert.False(
                    typeof(IPassportPaymentClient).IsAssignableFrom(method.ReturnType),
                    $"{executor.Name}.{method.Name} devuelve IPassportPaymentClient.");
            }
        }
    }

    [Fact]
    public void EveryM5T5EExecutor_DependsOnlyOnTheKeyClient()
    {
        // La única dependencia remota declarada por las tres fases es
        // IPassportKeyClient — ni Payment, ni QR, ni ningún otro cliente.
        foreach (var executor in M5T5EExecutors)
        {
            var execute = executor.GetMethod("ExecuteAsync", AllMembers);
            Assert.NotNull(execute);

            var remoteClients = execute!.GetParameters()
                .Select(p => p.ParameterType)
                .Where(t => t.Namespace == typeof(IPassportKeyClient).Namespace && t.IsInterface)
                .ToArray();

            Assert.Equal(new[] { typeof(IPassportKeyClient) }, remoteClients);
        }
    }

    [Fact]
    public void TheFinalEvidenceBuilder_NeverReportsAnyPaymentOrRetrieveCall()
    {
        // Defensa documental: el JSON final de M5-T5-E siempre declara
        // explícitamente cero llamadas de Payment/Retrieve.
        var rejection = ResolveM5T5DeletedKeyEvidenceBuilder.BuildExpectedRejection(
            keyIdFingerprint: "fp-key-id", keyValueFingerprint: "fp-key-value",
            httpStatus: 404, safeErrorCode: "resource_not_found", safeErrorMessage: "key not found.",
            deletionSignalConfirmed: true, commitSha: "synthetic-commit-sha",
            executedAtUtc: new DateTime(2026, 1, 1, 0, 30, 0, DateTimeKind.Utc), automatedTestReference: null);

        Assert.Equal(0, rejection.ResponseSanitized["payment_real_calls"]);
        Assert.Equal(0, rejection.ResponseSanitized["retrieve_real_calls"]);
        Assert.Equal(0, rejection.ResponseSanitized["delete_key_real_calls"]);
        Assert.Equal(1, rejection.ResponseSanitized["resolve_real_calls"]);
        Assert.Equal("M5-T5-DELETED-KEY", rejection.CaseId);
    }

    // ══════════════════════════════════════════════════════════════════
    // (b) WIRING CLI — UN FLAG PROPIO POR FASE
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(HarnessDecision.CommandCreateM5T5DeletedKeyFixture)]
    [InlineData(HarnessDecision.CommandDeleteM5T5FixtureKey)]
    [InlineData(HarnessDecision.CommandResolveM5T5DeletedKey)]
    public void WithoutExecuteFlag_EveryPhaseStaysInDryRun(string command)
    {
        var action = HarnessDecision.Decide(new[] { command }, out _);
        Assert.Equal(HarnessAction.DryRun, action);
    }

    [Theory]
    [InlineData(HarnessDecision.CommandCreateM5T5DeletedKeyFixture)]
    [InlineData(HarnessDecision.CommandDeleteM5T5FixtureKey)]
    [InlineData(HarnessDecision.CommandResolveM5T5DeletedKey)]
    public void WithExecuteButNoConfirmation_EveryPhaseAborts(string command)
    {
        var action = HarnessDecision.Decide(new[] { command, HarnessDecision.FlagExecute }, out var abortReason);
        Assert.Equal(HarnessAction.Aborted, action);
        Assert.NotNull(abortReason);
    }

    [Theory]
    // El flag de M5-T5-D nunca autoriza M5-T5-E.
    [InlineData(HarnessDecision.CommandCreateM5T5DeletedKeyFixture, HarnessDecision.FlagConfirmCreateM5T5SuspendedKeyFixture)]
    [InlineData(HarnessDecision.CommandDeleteM5T5FixtureKey, HarnessDecision.FlagConfirmSuspendM5T5FixtureKey)]
    [InlineData(HarnessDecision.CommandResolveM5T5DeletedKey, HarnessDecision.FlagConfirmResolveM5T5SuspendedKey)]
    // El flag de M3-T5 (delete-key) nunca autoriza la Fase B de M5-T5-E.
    [InlineData(HarnessDecision.CommandDeleteM5T5FixtureKey, HarnessDecision.FlagConfirmDeleteKey)]
    // Los flags de M5-T5-E no son intercambiables entre sus propias fases.
    [InlineData(HarnessDecision.CommandDeleteM5T5FixtureKey, HarnessDecision.FlagConfirmCreateM5T5DeletedKeyFixture)]
    [InlineData(HarnessDecision.CommandResolveM5T5DeletedKey, HarnessDecision.FlagConfirmDeleteM5T5FixtureKey)]
    [InlineData(HarnessDecision.CommandCreateM5T5DeletedKeyFixture, HarnessDecision.FlagConfirmResolveM5T5DeletedKey)]
    public void AForeignConfirmationFlag_NeverAuthorizesAPhase(string command, string foreignFlag)
    {
        var action = HarnessDecision.Decide(
            new[] { command, HarnessDecision.FlagExecute, foreignFlag }, out var abortReason);
        Assert.Equal(HarnessAction.Aborted, action);
        Assert.NotNull(abortReason);
    }

    [Theory]
    [InlineData(HarnessDecision.CommandCreateM5T5DeletedKeyFixture, HarnessDecision.FlagConfirmCreateM5T5DeletedKeyFixture)]
    [InlineData(HarnessDecision.CommandDeleteM5T5FixtureKey, HarnessDecision.FlagConfirmDeleteM5T5FixtureKey)]
    [InlineData(HarnessDecision.CommandResolveM5T5DeletedKey, HarnessDecision.FlagConfirmResolveM5T5DeletedKey)]
    public void OnlyItsOwnConfirmationFlag_ReachesReadyToExecute(string command, string ownFlag)
    {
        var action = HarnessDecision.Decide(
            new[] { command, HarnessDecision.FlagExecute, ownFlag }, out var abortReason);
        Assert.Equal(HarnessAction.Execute, action);
        Assert.Null(abortReason);
    }

    [Fact]
    public void EveryM5T5EPhase_ParsesToItsOwnCommand_DistinctFromM5T5D()
    {
        Assert.Equal(HarnessCommand.CreateM5T5DeletedKeyFixture,
            HarnessDecision.ParseCommand([HarnessDecision.CommandCreateM5T5DeletedKeyFixture]));
        Assert.Equal(HarnessCommand.DeleteM5T5FixtureKey,
            HarnessDecision.ParseCommand([HarnessDecision.CommandDeleteM5T5FixtureKey]));
        Assert.Equal(HarnessCommand.ResolveM5T5DeletedKey,
            HarnessDecision.ParseCommand([HarnessDecision.CommandResolveM5T5DeletedKey]));

        // Case ids y prefijos disjuntos de M5-T5-D y de M4.
        Assert.NotEqual(CreateM5T5SuspendedKeyFixtureExecutor.FixtureCaseIdPrefix,
                        CreateM5T5DeletedKeyFixtureExecutor.FixtureCaseIdPrefix);
        Assert.NotEqual(SuspendM5T5FixtureKeyExecutor.SuspendGuardCaseId,
                        DeleteM5T5FixtureKeyExecutor.DeleteGuardCaseId);
        Assert.NotEqual(ResolveM5T5SuspendedKeyExecutor.ResolveGuardCaseId,
                        ResolveM5T5DeletedKeyExecutor.ResolveGuardCaseId);
        Assert.NotEqual(ResolveM5T5SuspendedKeyEvidenceBuilder.CaseId,
                        ResolveM5T5DeletedKeyEvidenceBuilder.CaseId);
        Assert.StartsWith("M5-T5-", ResolveM5T5DeletedKeyEvidenceBuilder.CaseId);
        Assert.DoesNotContain("M4", ResolveM5T5DeletedKeyEvidenceBuilder.CaseId);
    }

    // ══════════════════════════════════════════════════════════════════
    // READER — VALIDACIÓN DE CONTENIDO AISLADA
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("CREATED", "BCODE", "key-id", "0091234567", true)]
    [InlineData("PREPARED_NOT_CREATED", "BCODE", "key-id", "0091234567", false)]
    [InlineData("CREATE_FAILED", "BCODE", "key-id", "0091234567", false)]
    [InlineData("CREATED", "PHONE", "key-id", "0091234567", false)]
    [InlineData("CREATED", "BCODE", "", "0091234567", false)]
    [InlineData("CREATED", "BCODE", "key-id", "", false)]
    public void ReaderIsValid_RequiresCreatedBcodeWithBothIdentifiers(
        string state, string keyType, string keyId, string keyValue, bool expected)
    {
        var artifact = new M5T5DeletedFixtureArtifactReader.Artifact(
            Purpose: CreateM5T5DeletedKeyFixtureExecutor.FixturePurpose,
            State: state, KeyType: keyType, KeyValue: keyValue, KeyId: keyId,
            KeyValueFingerprint: null, KeyIdFingerprint: null);

        Assert.Equal(expected, M5T5DeletedFixtureArtifactReader.IsValid(artifact));
    }

    [Fact]
    public void ReaderIsValid_RejectsAnArtifactBelongingToM5T5D()
    {
        var artifact = new M5T5DeletedFixtureArtifactReader.Artifact(
            Purpose: "M5-T5-D-SUSPENDED-KEY-FIXTURE",
            State: "CREATED", KeyType: "BCODE", KeyValue: "0091234567", KeyId: "key-id",
            KeyValueFingerprint: null, KeyIdFingerprint: null);

        Assert.False(M5T5DeletedFixtureArtifactReader.IsValid(artifact));
    }

    [Theory]
    [InlineData("EXPECTED_DELETE_SUCCESS", true, "fp-id", "fp-value", true)]
    [InlineData("EXPECTED_DELETE_SUCCESS", false, "fp-id", "fp-value", false)]
    [InlineData("DELETE_REJECTED", true, "fp-id", "fp-value", false)]
    [InlineData("EXPECTED_DELETE_SUCCESS", true, "otro-fp", "fp-value", false)]
    [InlineData("EXPECTED_DELETE_SUCCESS", true, "fp-id", "otro-fp", false)]
    public void ReaderDeleteCorrelation_IsFailClosed(
        string outcome, bool acknowledged, string markerIdFp, string markerValueFp, bool expected)
    {
        var artifact = new M5T5DeletedFixtureArtifactReader.Artifact(
            Purpose: CreateM5T5DeletedKeyFixtureExecutor.FixturePurpose,
            State: "CREATED", KeyType: "BCODE", KeyValue: "0091234567", KeyId: "key-id",
            KeyValueFingerprint: "fp-value", KeyIdFingerprint: "fp-id");

        var marker = new M5T5DeletedFixtureArtifactReader.DeleteMarker(
            KeyIdFingerprint: markerIdFp, KeyValueFingerprint: markerValueFp,
            Outcome: outcome, DeleteAcknowledged: acknowledged);

        Assert.Equal(expected, M5T5DeletedFixtureArtifactReader.IsDeleteCorrelationValid(artifact, marker));
    }

    [Fact]
    public void ReaderDeleteCorrelation_RejectsAMissingMarker()
    {
        var artifact = new M5T5DeletedFixtureArtifactReader.Artifact(
            Purpose: CreateM5T5DeletedKeyFixtureExecutor.FixturePurpose,
            State: "CREATED", KeyType: "BCODE", KeyValue: "0091234567", KeyId: "key-id",
            KeyValueFingerprint: "fp-value", KeyIdFingerprint: "fp-id");

        Assert.False(M5T5DeletedFixtureArtifactReader.IsDeleteCorrelationValid(artifact, null));
    }
}
