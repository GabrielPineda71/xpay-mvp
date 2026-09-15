using Microsoft.Extensions.Configuration;
using Xpay.Api.Integrations.Passport;

namespace Xpay.PassportSandboxHarness;

// XPAY-325 — orquestación COMPLETA del comando (parseo → decisión → acción)
// extraída de Program.cs para que sea invocable/testeable con dependencias
// fake/inyectadas, sin lanzar el proceso ni depender de top-level
// statements. Program.cs se reduce a construir las dependencias REALES
// (DI real de Passport) y llamar a este método una sola vez.
public static class HarnessApp
{
    // XPAY-325 — dependencias que Program.cs resuelve con el stack real
    // (PassportCustomerAccountClient/PassportKeyClient/GitCommitShaProvider)
    // y que los tests reemplazan con fakes en memoria. EvidenceBaseDirectory
    // es explícito (no un valor fijo hardcodeado) para que los tests usen un
    // directorio temporal — nunca escriben en docs/certificacion/passport-breb/.
    public sealed record Dependencies(
        IPassportCustomerAccountClient CustomerAccountClient,
        IPassportKeyClient KeyClient,
        ICommitShaProvider CommitShaProvider,
        string EvidenceBaseDirectory);

    public static async Task RunAsync(
        string[] args, IConfiguration configuration, Dependencies dependencies, TextWriter output)
    {
        var decision = HarnessOrchestrator.Prepare(args, configuration);

        output.WriteLine("== Xpay Passport Sandbox Harness (XPAY-312/325) ==");
        output.WriteLine($"command={decision.Command}");
        foreach (var line in decision.ConfigStatus.ToRedactedLines())
            output.WriteLine(line);
        if (decision.TargetConfig is not null)
            foreach (var line in decision.TargetConfig.ToRedactedLines())
                output.WriteLine(line);

        switch (decision.Outcome)
        {
            case HarnessOrchestrator.Outcome.ShowHelp:
                output.WriteLine();
                output.WriteLine("Uso: create-customer [--execute --confirm-create-customer]");
                output.WriteLine("     create-key       [--execute --confirm-create-key]");
                output.WriteLine("     suspend-key      [--execute --confirm-suspend-key]");
                output.WriteLine("     activate-key     [--execute --confirm-activate-key]");
                output.WriteLine("     delete-key       [--execute --confirm-delete-key]");
                output.WriteLine("     delete-already-deleted-key [--execute --confirm-delete-already-deleted-key]");
                output.WriteLine("Sin argumentos o sin ambas banderas: modo dry-run (sin HTTP).");
                return;

            case HarnessOrchestrator.Outcome.AbortedMissingConfirmation:
            case HarnessOrchestrator.Outcome.AbortedConfigMissing:
            case HarnessOrchestrator.Outcome.AbortedTargetMissing:
            case HarnessOrchestrator.Outcome.AbortedNonSandboxHost:
                // XPAY-325 FASE 5/7 — un aborto ANTES de HTTP nunca genera
                // evidencia de certificación (DRY-RUN/ABORTED ≠ CERTIFICATION
                // EVIDENCE). Sólo se informa por consola.
                output.WriteLine($"result=ABORTED detail={decision.Detail}");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateCustomer:
            {
                var request = SyntheticCustomerRequestFactory.BuildSynthetic();
                var json = System.Text.Json.JsonSerializer.Serialize(request);
                output.WriteLine($"dry_run=YES http_call=NO request_bytes={json.Length}");
                output.WriteLine("result=DRY_RUN");
                return;
            }

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateKey:
                // XPAY-325 FASE 5 — NUNCA lee los valores reales de
                // PASSPORT_TEST_ACCOUNT_ID/PASSPORT_TEST_NEW_KEY_TYPE/
                // PASSPORT_TEST_NEW_KEY_VALUE en este modo: sólo ya se
                // confirmó su PRESENCIA (impreso arriba). No se construye
                // ningún PassportCreateKeyRequest, no se obtiene token, no
                // hay HTTP, no hay evidencia.
                output.WriteLine("case=M3-T1 (Create Key)");
                output.WriteLine("endpoint=POST /v1/keys");
                output.WriteLine("mutating=YES");
                output.WriteLine("requires=--execute --confirm-create-key");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.SuspendKey:
                // XPAY-326 — igual criterio que create-key: NUNCA lee el
                // valor real de PASSPORT_TEST_NEW_KEY_ID en este modo, sólo
                // ya se confirmó su PRESENCIA (impreso arriba). No hay
                // token, no hay HTTP, no hay evidencia.
                output.WriteLine("case=M3-T3 (Suspend Key)");
                output.WriteLine("endpoint=PATCH /v1/keys/{key_id}/suspend");
                output.WriteLine("mutating=YES");
                output.WriteLine("requires=--execute --confirm-suspend-key");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.ActivateKey:
                // XPAY-332 — mismo criterio que suspend-key: NUNCA lee el
                // valor real de PASSPORT_TEST_NEW_KEY_ID en este modo, sólo
                // ya se confirmó su PRESENCIA (impreso arriba). No hay
                // token, no hay HTTP, no hay evidencia.
                output.WriteLine("case=M3-T4 (Activate Key)");
                output.WriteLine("endpoint=PATCH /v1/keys/{key_id}/activate");
                output.WriteLine("mutating=YES");
                output.WriteLine("requires=--execute --confirm-activate-key");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.DeleteKey:
                // XPAY-334 — mismo criterio que suspend-key/activate-key:
                // NUNCA lee el valor real de PASSPORT_TEST_NEW_KEY_ID en
                // este modo, sólo ya se confirmó su PRESENCIA (impreso
                // arriba). No hay token, no hay HTTP, no hay evidencia.
                output.WriteLine("case=M3-T5 (Delete Key)");
                output.WriteLine("endpoint=DELETE /v1/keys/{key_id}");
                output.WriteLine("mutating=YES");
                output.WriteLine("requires=--execute --confirm-delete-key");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.DeleteAlreadyDeletedKey:
                // XPAY-336 — mismo criterio que delete-key: NUNCA lee el
                // valor real de PASSPORT_TEST_NEW_KEY_ID en este modo,
                // sólo ya se confirmó su PRESENCIA (impreso arriba). No
                // hay token, no hay HTTP, no hay evidencia.
                output.WriteLine("case=M3-T7 (Delete Already-Deleted Key)");
                output.WriteLine("endpoint=DELETE /v1/keys/{key_id}");
                output.WriteLine("mutating=YES");
                output.WriteLine("requires=--execute --confirm-delete-already-deleted-key");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M3-T7 es prueba negativa: transporte PASS/FAIL != juicio de certificación");
                return;

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateCustomer:
                // XPAY-312 — wiring confirmado (RealStackWiringTests), pero
                // la invocación real de LinkMerchantAsync queda diferida a
                // una fase futura explícitamente autorizada.
                _ = dependencies.CustomerAccountClient;
                output.WriteLine("result=READY_TO_EXECUTE detail=stack_construido_pero_no_invocado");
                return;

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateKey:
            {
                var execution = await CreateKeyExecutor
                    .ExecuteAsync(configuration, dependencies.KeyClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == CreateKeyExecutionOutcome.LocalBlocked)
                {
                    // XPAY-325 FASE 7 — un bloqueo LOCAL (key_type inválido,
                    // commit SHA no resoluble) NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var path = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={path}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.SuspendKey:
            {
                var execution = await SuspendKeyExecutor
                    .ExecuteAsync(configuration, dependencies.KeyClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-326 — un bloqueo LOCAL (key_id ausente, commit SHA
                    // no resoluble) NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var path = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={path}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.ActivateKey:
            {
                var execution = await ActivateKeyExecutor
                    .ExecuteAsync(configuration, dependencies.KeyClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-332 — un bloqueo LOCAL (key_id ausente, commit SHA
                    // no resoluble) NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var path = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={path}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.DeleteKey:
            {
                var execution = await DeleteKeyExecutor
                    .ExecuteAsync(configuration, dependencies.KeyClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-334 — un bloqueo LOCAL (key_id ausente, commit SHA
                    // no resoluble) NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var path = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={path}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.DeleteAlreadyDeletedKey:
            {
                var execution = await DeleteAlreadyDeletedKeyExecutor
                    .ExecuteAsync(configuration, dependencies.KeyClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-336 — un bloqueo LOCAL (key_id ausente, commit
                    // SHA no resoluble) NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var path = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={path}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                output.WriteLine("note=M3-T7 es prueba negativa: transporte PASS/FAIL != juicio de certificación (ver evidencia)");
                return;
            }
        }
    }
}
