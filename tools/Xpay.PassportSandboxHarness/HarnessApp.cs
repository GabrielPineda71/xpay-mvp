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
        string EvidenceBaseDirectory,
        // XPAY-351 — M4-T1: mismo stack HTTP/OAuth subyacente
        // (IPassportHttpClient), cliente tipado distinto. Nullable NO se usa
        // deliberadamente: todo llamador (Program.cs real, y cada test que
        // construye Dependencies) debe proveerlo explícitamente, igual que
        // los demás clientes.
        IPassportQrClient QrClient);

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
                output.WriteLine("     resolve-key      [--execute --confirm-resolve-key]");
                output.WriteLine("     create-key-missing [--execute --confirm-create-key-missing]");
                output.WriteLine("     create-key-invalid [--execute --confirm-create-key-invalid]");
                output.WriteLine("     create-qr-static [--execute --confirm-create-qr-static]");
                output.WriteLine("     decode-qr-static [--execute --confirm-decode-qr-static]");
                output.WriteLine("     create-qr-static-suspended-key    [--execute --confirm-create-qr-static-suspended-key]");
                output.WriteLine("     create-qr-static-deleted-key      [--execute --confirm-create-qr-static-deleted-key]");
                output.WriteLine("     create-qr-static-invalid-customer [--execute --confirm-create-qr-static-invalid-customer]");
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

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.ResolveKey:
                // XPAY-340 — NUNCA lee los valores reales de
                // PASSPORT_TEST_CUSTOMER_ID/PASSPORT_TEST_BREB_KEY_TYPE/
                // PASSPORT_TEST_BREB_KEY en este modo: sólo ya se confirmó
                // su PRESENCIA (impreso arriba). No se construye ningún
                // PassportResolveKeyRequest, no se obtiene token, no hay
                // HTTP, no hay evidencia.
                output.WriteLine("case=M3-T2 (Resolve Key)");
                output.WriteLine("endpoint=POST /v1/resolve-key");
                output.WriteLine("mutating=NO (read-only, resuelve una llave existente)");
                output.WriteLine("requires=--execute --confirm-resolve-key");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateKeyMissing:
                // XPAY-344 — NUNCA lee PASSPORT_TEST_ACCOUNT_ID/
                // PASSPORT_TEST_NEW_KEY_TYPE en este modo, sólo ya se
                // confirmó su PRESENCIA (impreso arriba). No hay token, no
                // hay HTTP, no hay evidencia.
                output.WriteLine("case=M3-T6-MISSING (Create Key sin key_value)");
                output.WriteLine("endpoint=POST /v1/keys — blocked before transport (esperado)");
                output.WriteLine("mutating=NO (bloqueo local esperado, nunca llega a Passport)");
                output.WriteLine("requires=--execute --confirm-create-key-missing");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateKeyInvalid:
                // XPAY-344 — NUNCA lee PASSPORT_TEST_ACCOUNT_ID/
                // PASSPORT_TEST_NEW_KEY_TYPE en este modo. El key_value
                // inválido se genera internamente sólo en ReadyToExecute —
                // nunca aquí, nunca desde el env.
                output.WriteLine("case=M3-T6-INVALID (Create Key con key_value de formato inválido)");
                output.WriteLine("endpoint=POST /v1/keys");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-344)");
                output.WriteLine("requires=--execute --confirm-create-key-invalid");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateQrStatic:
                // XPAY-351 — NUNCA lee los valores reales de
                // PASSPORT_TEST_NEW_KEY_ID/PASSPORT_TEST_CUSTOMER_ID en este
                // modo: sólo ya se confirmó su PRESENCIA (impreso arriba).
                // No se construye ningún PassportCreateQrCodeRequest, no se
                // obtiene token, no hay HTTP, no hay evidencia.
                output.WriteLine("case=M4-T1 (Create QR Code — STATIC)");
                output.WriteLine("endpoint=POST /v1/qrcodes");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-351)");
                output.WriteLine("requires=--execute --confirm-create-qr-static");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M4-T1: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-351)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.DecodeQrStatic:
                // XPAY-465 — NUNCA lee los valores reales de
                // PASSPORT_TEST_CUSTOMER_ID/PASSPORT_TEST_QR_DECODE_DATA_FILE
                // en este modo (sólo ya se confirmó su PRESENCIA arriba), y
                // mucho menos el CONTENIDO del archivo que la segunda
                // apunta — eso sólo se lee en ReadyToExecute, dentro de
                // DecodeQrStaticExecutor. No se construye ningún
                // PassportDecodeQrCodeRequest, no se obtiene token, no hay
                // HTTP, no hay evidencia.
                output.WriteLine("case=M4-T2 (Decode QR Code — STATIC)");
                output.WriteLine("endpoint=POST /v1/qrcodes/decode");
                output.WriteLine("mutating=NO (read-only, decodifica un QR ya existente)");
                output.WriteLine("requires=--execute --confirm-decode-qr-static");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M4-T2: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-465)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateQrStaticSuspendedKey:
                // XPAY-471 — NUNCA lee los valores reales de
                // PASSPORT_TEST_QR_SUSPENDED_KEY_ID/PASSPORT_TEST_CUSTOMER_ID
                // en este modo. No se construye ningún request, no se
                // obtiene token, no hay HTTP, no hay evidencia. Este comando
                // NO crea ni suspende ninguna llave — sólo intentaría Create
                // QR contra una que YA debería estar suspendida.
                output.WriteLine("case=M4-T3-A (Create QR Code STATIC — llave Bre-B SUSPENDIDA)");
                output.WriteLine("endpoint=POST /v1/qrcodes");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-471)");
                output.WriteLine("requires=--execute --confirm-create-qr-static-suspended-key");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M4-T3-A: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-471)");
                output.WriteLine("note=CASO NEGATIVO: un HTTP 4xx es el resultado ESPERADO, pero este comando NUNCA lo certifica automáticamente");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateQrStaticDeletedKey:
                // XPAY-471 — NUNCA lee los valores reales de
                // PASSPORT_TEST_NEW_KEY_ID/PASSPORT_TEST_CUSTOMER_ID en este
                // modo. Este comando NUNCA muta la llave de M3 (nunca
                // Delete/Suspend/Activate/List Keys) — sólo intentaría
                // Create QR contra ella, usándola como referencia histórica
                // eliminada.
                output.WriteLine("case=M4-T3-B (Create QR Code STATIC — llave Bre-B ELIMINADA)");
                output.WriteLine("endpoint=POST /v1/qrcodes");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-471)");
                output.WriteLine("requires=--execute --confirm-create-qr-static-deleted-key");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M4-T3-B: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-471)");
                output.WriteLine("note=CASO NEGATIVO: un HTTP 4xx es el resultado ESPERADO, pero este comando NUNCA lo certifica automáticamente");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateQrStaticInvalidCustomer:
                // XPAY-471 — NUNCA lee el valor real de
                // PASSPORT_TEST_QR_KEY_ID en este modo. customer_id nunca
                // proviene del entorno para este comando (siempre
                // InvalidCustomerIdGenerator, determinista y sintético) —
                // dry-run tampoco lo genera ni lo imprime.
                output.WriteLine("case=M4-T3-C (Create QR Code STATIC — customer_id INCORRECTO)");
                output.WriteLine("endpoint=POST /v1/qrcodes");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-471)");
                output.WriteLine("requires=--execute --confirm-create-qr-static-invalid-customer");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M4-T3-C: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-471)");
                output.WriteLine("note=CASO NEGATIVO: un HTTP 4xx es el resultado ESPERADO, pero este comando NUNCA lo certifica automáticamente");
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

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.ResolveKey:
            {
                var execution = await ResolveKeyExecutor
                    .ExecuteAsync(configuration, dependencies.KeyClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-340 — un bloqueo LOCAL (target ausente, key_type
                    // inválido, commit SHA no resoluble) NUNCA escribe
                    // evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var path = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={path}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateKeyMissing:
            {
                var execution = await CreateKeyMissingExecutor
                    .ExecuteAsync(configuration, dependencies.KeyClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-344 — bloqueo operativo GENUINO (target
                    // ausente, commit SHA no resoluble, o el guard
                    // productivo no disparó como se esperaba) — NUNCA
                    // escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                // Outcome.Success aquí significa: el guard productivo de
                // CreateKeyAsync SÍ rechazó el request por key_value
                // ausente, ANTES de HTTP — éxito del subcaso M3-T6-MISSING.
                var path = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={path}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                output.WriteLine("note=M3-T6-MISSING: bloqueo LOCAL confirmado antes de cualquier HTTP a Passport");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateKeyInvalid:
            {
                var execution = await CreateKeyInvalidExecutor
                    .ExecuteAsync(configuration, dependencies.KeyClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-344 — bloqueo LOCAL (target ausente, key_type no
                    // soportado para generación de valor inválido, commit
                    // SHA no resoluble) NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var path = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={path}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                output.WriteLine("note=M3-T6-INVALID: transporte PASS/FAIL != juicio de certificación (ver evidencia)");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateQrStatic:
            {
                var execution = await CreateQrStaticExecutor
                    .ExecuteAsync(configuration, dependencies.QrClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-351 — un bloqueo LOCAL (target ausente, commit
                    // SHA no resoluble) NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var path = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={path}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.DecodeQrStatic:
            {
                var execution = await DecodeQrStaticExecutor
                    .ExecuteAsync(configuration, dependencies.QrClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-465 — un bloqueo LOCAL (target ausente, archivo
                    // de qr_code_data ausente/vacío/ilegible, commit SHA no
                    // resoluble) NUNCA escribe evidence.json. `Detail` nunca
                    // incluye el contenido del archivo — ver DecodeQrStaticExecutor.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var decodePath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={decodePath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateQrStaticSuspendedKey:
            {
                var execution = await CreateQrStaticSuspendedKeyExecutor
                    .ExecuteAsync(configuration, dependencies.QrClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-471 — un bloqueo LOCAL (target ausente, commit
                    // SHA no resoluble) NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var suspendedKeyPath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={suspendedKeyPath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                output.WriteLine("note=CASO NEGATIVO: result=FAIL a nivel transporte no implica fallo de certificación — ver evidencia");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateQrStaticDeletedKey:
            {
                var execution = await CreateQrStaticDeletedKeyExecutor
                    .ExecuteAsync(configuration, dependencies.QrClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-471 — un bloqueo LOCAL NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var deletedKeyPath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={deletedKeyPath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                output.WriteLine("note=CASO NEGATIVO: result=FAIL a nivel transporte no implica fallo de certificación — ver evidencia");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateQrStaticInvalidCustomer:
            {
                var execution = await CreateQrStaticInvalidCustomerExecutor
                    .ExecuteAsync(configuration, dependencies.QrClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-471 — un bloqueo LOCAL NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var invalidCustomerPath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={invalidCustomerPath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                output.WriteLine("note=CASO NEGATIVO: result=FAIL a nivel transporte no implica fallo de certificación — ver evidencia");
                return;
            }
        }
    }
}
