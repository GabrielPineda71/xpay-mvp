namespace Xpay.PassportSandboxHarness;

// XPAY-325 — abstracción para obtener el commit SHA del backend que ejecuta
// la operación, requerida por el pipeline de evidencia de certificación
// (docs/certificacion/passport-breb/). Se inyecta como interfaz para que
// los tests NUNCA dependan de un proceso Git real ni de un repositorio real
// en disco — usan FixedCommitShaProvider con un valor sintético.
public interface ICommitShaProvider
{
    string GetCommitSha();
}

// Implementación real: SIEMPRE resuelve a un valor no vacío o lanza — nunca
// se permite evidencia con backend_commit_sha vacío (XPAY-325 FASE 8).
//
// Orden de resolución:
//   1) variable de entorno XPAY_BUILD_COMMIT_SHA (útil para metadata de
//      build/CI, donde invocar `git` puede no ser apropiado ni posible);
//   2) `git rev-parse HEAD` como fallback, ejecutado como subproceso.
public sealed class GitCommitShaProvider : ICommitShaProvider
{
    private const string EnvOverride = "XPAY_BUILD_COMMIT_SHA";

    public string GetCommitSha()
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvOverride);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();

        string? output = null;
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse HEAD",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode != 0)
                output = null;
        }
        catch
        {
            output = null;
        }

        if (string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException(
                "No se pudo determinar el commit SHA del backend (ni por " +
                $"{EnvOverride} ni por `git rev-parse HEAD`) — la evidencia de " +
                "certificación nunca debe generarse con backend_commit_sha vacío.");

        return output;
    }
}
