using System.Text.Json;
using Xpay.PassportSandboxHarness;
using Xunit;

namespace Xpay.PassportSandboxHarness.Tests;

public class EvidenceWriterTests
{
    private const string SecretApiKey    = "SYNTH-SECRET-API-KEY-abc123";
    private const string SecretApiSecret = "SYNTH-SECRET-API-SECRET-xyz789";
    private const string SecretKeyValue  = "SYNTH-SECRET-KEY-VALUE-should-never-appear";
    private const string SecretBearer    = "SYNTH-BEARER-TOKEN-should-never-appear";
    private const string SecretAccountNumber = "SYNTH-ACCOUNT-NUMBER-1234567890";
    private const string SecretIdentificationNumber = "SYNTH-ID-NUMBER-9999999999";

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xpay-evidence-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static EvidenceRecord SyntheticRecord(
        string caseId = "M3-T1",
        string executedAtUtc = "2026-01-01T00:00:00Z",
        string commitSha = "synthetic-commit-sha-0000000000000000000000000000000000000000",
        string result = EvidenceRecord.ResultPass) => new(
        CaseId: caseId,
        ExecutedAtUtc: executedAtUtc,
        Environment: EvidenceRecord.EnvironmentSandbox,
        BackendCommitSha: commitSha,
        Operation: "POST /v1/keys",
        HttpStatus: 200,
        Result: result,
        RequestSanitized: new Dictionary<string, object?>
        {
            ["key_type"] = "BCODE",
            ["account_id_fingerprint"] = Fingerprint.Compute("synthetic-account-id"),
            ["key_value"] = "REDACTED",
        },
        ResponseSanitized: new Dictionary<string, object?>
        {
            ["status"] = "ACTIVE",
            ["id_fingerprint"] = Fingerprint.Compute("synthetic-key-id"),
        },
        AutomatedTestReference: "PassportKeyClientTests.CreateKeyAsync_PostsToExactContractPath",
        Notes: null,
        ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);

    [Fact]
    public void Write_ProducesValidJson_WithExpectedFields()
    {
        var dir = NewTempDir();
        try
        {
            var path = EvidenceWriter.Write(dir, SyntheticRecord());
            Assert.True(File.Exists(path));

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json); // JSON válido
            var root = doc.RootElement;

            Assert.Equal("M3-T1", root.GetProperty("case_id").GetString());
            Assert.Equal("sandbox", root.GetProperty("environment").GetString());
            Assert.Equal("synthetic-commit-sha-0000000000000000000000000000000000000000",
                root.GetProperty("backend_commit_sha").GetString());
            Assert.Equal("POST /v1/keys", root.GetProperty("operation").GetString());
            Assert.Equal(200, root.GetProperty("http_status").GetInt32());
            Assert.Equal("PASS", root.GetProperty("result").GetString());
            Assert.Equal("PENDING_PASSPORT_REVIEW", root.GetProperty("review_status").GetString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Write_RejectsEmptyCommitSha()
    {
        var dir = NewTempDir();
        try
        {
            var record = SyntheticRecord(commitSha: "");
            Assert.Throws<ArgumentException>(() => EvidenceWriter.Write(dir, record));
            // No debe haber quedado ningún archivo.
            Assert.False(Directory.Exists(Path.Combine(dir, "M3-T1"))
                         && Directory.GetFiles(Path.Combine(dir, "M3-T1")).Length > 0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Write_RejectsInvalidResult()
    {
        var dir = NewTempDir();
        try
        {
            var record = SyntheticRecord(result: "MAYBE");
            Assert.Throws<ArgumentException>(() => EvidenceWriter.Write(dir, record));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Write_DoesNotOverwriteExistingEvidence_ForSameCaseAndTimestamp()
    {
        var dir = NewTempDir();
        try
        {
            var record = SyntheticRecord();
            EvidenceWriter.Write(dir, record); // primera escritura, OK

            var ex = Assert.Throws<InvalidOperationException>(() => EvidenceWriter.Write(dir, record));
            Assert.Contains("no se sobrescribe", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Write_DifferentTimestamps_SameCase_DoNotCollide()
    {
        var dir = NewTempDir();
        try
        {
            var first  = SyntheticRecord(executedAtUtc: "2026-01-01T00:00:00Z");
            var second = SyntheticRecord(executedAtUtc: "2026-01-01T00:05:00Z");

            var path1 = EvidenceWriter.Write(dir, first);
            var path2 = EvidenceWriter.Write(dir, second);

            Assert.NotEqual(path1, path2);
            Assert.True(File.Exists(path1));
            Assert.True(File.Exists(path2));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Write_FailedMove_LeavesNoTempFileBehind()
    {
        var dir = NewTempDir();
        try
        {
            var record = SyntheticRecord();
            var caseDir = Path.Combine(dir, "M3-T1");
            Directory.CreateDirectory(caseDir);
            // Forzar el fallo: crear el destino final COMO DIRECTORIO, de modo
            // que File.Move falle (File.Exists(finalPath) es false para un
            // directorio, así que se intenta escribir y falla en el Move).
            var finalPath = Path.Combine(caseDir, $"evidence-{record.ExecutedAtUtc.Replace(':', '-')}.json");
            Directory.CreateDirectory(finalPath);

            Assert.ThrowsAny<Exception>(() => EvidenceWriter.Write(dir, record));

            // No debe quedar ningún archivo temporal .tmp-* colgado.
            var leftoverTempFiles = Directory.GetFiles(caseDir, "*.tmp-*");
            Assert.Empty(leftoverTempFiles);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Write_NeverContainsSecretLookingValues()
    {
        var dir = NewTempDir();
        try
        {
            var record = new EvidenceRecord(
                CaseId: "M3-T1",
                ExecutedAtUtc: "2026-01-01T00:00:00Z",
                Environment: EvidenceRecord.EnvironmentSandbox,
                BackendCommitSha: "synthetic-commit-sha-0000000000000000000000000000000000000000",
                Operation: "POST /v1/keys",
                HttpStatus: 200,
                Result: EvidenceRecord.ResultPass,
                RequestSanitized: new Dictionary<string, object?>
                {
                    ["key_type"] = "BCODE",
                    ["key_value"] = "REDACTED", // NUNCA el valor real
                    ["account_id_fingerprint"] = Fingerprint.Compute(SecretAccountNumber),
                },
                ResponseSanitized: new Dictionary<string, object?>
                {
                    ["status"] = "ACTIVE",
                    ["id_fingerprint"] = Fingerprint.Compute(SecretIdentificationNumber),
                },
                AutomatedTestReference: null,
                Notes: null,
                ReviewStatus: EvidenceRecord.ReviewPendingPassportReview);

            var path = EvidenceWriter.Write(dir, record);
            var json = File.ReadAllText(path);

            // Adversarial: ninguno de estos valores "secretos" sintéticos
            // debe aparecer literalmente en el archivo generado.
            Assert.DoesNotContain(SecretApiKey, json);
            Assert.DoesNotContain(SecretApiSecret, json);
            Assert.DoesNotContain(SecretKeyValue, json);
            Assert.DoesNotContain(SecretBearer, json);
            Assert.DoesNotContain(SecretAccountNumber, json);
            Assert.DoesNotContain(SecretIdentificationNumber, json);
            Assert.DoesNotContain("Authorization", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bearer", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
