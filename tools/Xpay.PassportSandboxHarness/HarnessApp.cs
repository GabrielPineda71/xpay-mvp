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
        IPassportQrClient QrClient,
        // XPAY-529 — M5 (Payment): mismo stack HTTP/OAuth subyacente,
        // cliente tipado ya existente (PassportPaymentClient, XPAY-373,
        // usado en producción por BrebPaymentService) — este harness NUNCA
        // pasa por BrebPaymentService; construye requests directamente y
        // llama a este cliente, igual que QrClient/KeyClient. Nullable NO
        // se usa deliberadamente, mismo criterio que los demás.
        IPassportPaymentClient PaymentClient);

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
                output.WriteLine("     create-m4-t3-suspended-fixture-key [--execute --confirm-create-m4-t3-suspended-fixture-key]");
                output.WriteLine("     suspend-m4-t3-fixture-key          [--execute --confirm-suspend-m4-t3-fixture-key]");
                output.WriteLine("     recover-deleted-key-id             [--execute --confirm-recover-deleted-key]");
                output.WriteLine("     create-m4-t3-deleted-fixture-key   [--execute --confirm-create-m4-t3-deleted-fixture-key]");
                output.WriteLine("     delete-m4-t3-fixture-key           [--execute --confirm-delete-m4-t3-fixture-key]");
                output.WriteLine("     create-qr-dynamic                  [--execute --confirm-create-qr-dynamic]");
                output.WriteLine("     create-qr-dynamic-suspended        [--execute --confirm-create-qr-dynamic-suspended]");
                output.WriteLine("     create-qr-dynamic-deleted          [--execute --confirm-create-qr-dynamic-deleted]");
                output.WriteLine("     create-qr-dynamic-invalid-customer [--execute --confirm-create-qr-dynamic-invalid-customer]");
                output.WriteLine("     create-breb-payment-cert           [--execute --confirm-create-breb-payment-cert]");
                output.WriteLine("     retrieve-breb-payment-cert         [--execute --confirm-retrieve-breb-payment-cert]");
                output.WriteLine("     create-m5-customer-b                [--execute --confirm-create-m5-customer-b]");
                output.WriteLine("     create-m5-account-b                 [--execute --confirm-create-m5-account-b]");
                output.WriteLine("     resolve-key-m5-t2                   [--execute --confirm-resolve-key-m5-t2]");
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

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateQrDynamic:
                // XPAY-506 — NUNCA lee los valores reales de
                // PASSPORT_TEST_QR_KEY_ID/PASSPORT_TEST_CUSTOMER_ID en este
                // modo: sólo ya se confirmó su PRESENCIA (impreso arriba).
                // No se construye ningún PassportCreateQrCodeRequest, no se
                // obtiene token, no hay HTTP, no hay evidencia.
                output.WriteLine("case=M4-T4 (Create QR Code — DINÁMICO)");
                output.WriteLine("endpoint=POST /v1/qrcodes");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-506)");
                output.WriteLine("requires=--execute --confirm-create-qr-dynamic");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M4-T4: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-506)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.DecodeQrDynamic:
                // XPAY-517 — NUNCA lee los valores reales de
                // PASSPORT_TEST_QR_KEY_ID/PASSPORT_TEST_CUSTOMER_ID en este
                // modo, y mucho menos resuelve/abre el artefacto privado de
                // M4-T4 — eso sólo ocurre en ReadyToExecute, dentro de
                // DecodeQrDynamicExecutor. No se construye ningún
                // PassportDecodeQrCodeRequest, no se obtiene token, no hay
                // HTTP, no hay evidencia.
                output.WriteLine("case=M4-T5 (Decode QR Code — DINÁMICO, reutiliza el QR de M4-T4)");
                output.WriteLine("endpoint=POST /v1/qrcodes/decode");
                output.WriteLine("mutating=NO (read-only, decodifica un QR ya existente)");
                output.WriteLine("requires=--execute --confirm-decode-qr-dynamic");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M4-T5: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-517)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateQrDynamicSuspendedKey:
                // XPAY-522 — NUNCA lee los valores reales de
                // PASSPORT_TEST_QR_SUSPENDED_KEY_ID/PASSPORT_TEST_CUSTOMER_ID
                // en este modo, y mucho menos calcula/compara ningún
                // fingerprint — eso sólo ocurre en ReadyToExecute, dentro
                // de CreateQrDynamicSuspendedKeyExecutor. No se construye
                // ningún request, no se obtiene token, no hay HTTP.
                output.WriteLine("case=M4-T6-A (Create QR Code DYNAMIC — llave Bre-B suspendida)");
                output.WriteLine("endpoint=POST /v1/qrcodes");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-522)");
                output.WriteLine("requires=--execute --confirm-create-qr-dynamic-suspended");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M4-T6-A: DYNAMIC_EXPECTED_RESULT=UNKNOWN_UNTIL_REAL_EXECUTION (histórico STATIC produjo QR ACTIVE — Gustavo confirmó que el rechazo ocurre en Payment, no en Create QR; esto NO se extrapola automáticamente a DYNAMIC)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateQrDynamicDeletedKey:
                output.WriteLine("case=M4-T6-B (Create QR Code DYNAMIC — llave Bre-B eliminada)");
                output.WriteLine("endpoint=POST /v1/qrcodes");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-522)");
                output.WriteLine("requires=--execute --confirm-create-qr-dynamic-deleted");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M4-T6-B: DYNAMIC_EXPECTED_RESULT=UNKNOWN_UNTIL_REAL_EXECUTION (histórico STATIC produjo HTTP 404 resource_not_found — contexto histórico, no expectativa contractual codificada para DYNAMIC)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateQrDynamicInvalidCustomer:
                output.WriteLine("case=M4-T6-C (Create QR Code DYNAMIC — customer_id incorrecto)");
                output.WriteLine("endpoint=POST /v1/qrcodes");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-522)");
                output.WriteLine("requires=--execute --confirm-create-qr-dynamic-invalid-customer");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M4-T6-C: DYNAMIC_EXPECTED_RESULT=UNKNOWN_UNTIL_REAL_EXECUTION (histórico STATIC produjo HTTP 400 invalid_parameter_value \"UUID version 4 expected\" — contexto histórico, no expectativa contractual codificada para DYNAMIC)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateBrebPaymentCert:
                // XPAY-529 — NUNCA lee PASSPORT_ACCOUNT_ID/
                // PASSPORT_TEST_M5_PAYMENT_RESOLUTION_ID/
                // PASSPORT_TEST_M5_PAYMENT_AMOUNT_VALUE en este modo — esas
                // sólo se leen dentro de CreateBrebPaymentCertificationExecutor,
                // en ReadyToExecute. No se construye ningún
                // PassportCreatePaymentRequest, no se obtiene token, no hay
                // HTTP. Aislado de BrebPaymentService/Wallet/ledger por diseño
                // — ver CreateBrebPaymentCertificationExecutor.
                output.WriteLine("case=M5-PAYMENT-CREATE (Create Bre-B Payment — infraestructura de certificación)");
                output.WriteLine("endpoint=POST /v1/payments/breb");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-529)");
                output.WriteLine("requires=--execute --confirm-create-breb-payment-cert");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=Aislado de BrebPaymentService/PassportBrebRetiro/Wallet/ledger — llamada directa harness -> IPassportPaymentClient -> Passport");
                output.WriteLine("note=M5-PAYMENT-CREATE: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-529)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.RetrieveBrebPaymentCert:
                // XPAY-529 — NUNCA resuelve/abre el artefacto privado de
                // M5-PAYMENT-CREATE en este modo — eso sólo ocurre en
                // ReadyToExecute, dentro de RetrieveBrebPaymentCertificationExecutor.
                output.WriteLine("case=M5-PAYMENT-RETRIEVE (Retrieve Bre-B Payment — infraestructura de certificación)");
                output.WriteLine("endpoint=GET /v1/payments/{payment_id}");
                output.WriteLine("mutating=NO (read-only, consulta un payment ya existente)");
                output.WriteLine("requires=--execute --confirm-retrieve-breb-payment-cert");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=payment_id proviene EXCLUSIVAMENTE del artefacto privado M5-PAYMENT-CREATE — nunca hardcoded, nunca histórico");
                output.WriteLine("note=M5-PAYMENT-RETRIEVE: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-529)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateM5CustomerB:
                // XPAY-531 — NUNCA lee PASSPORT_TEST_M5_CUSTOMER_B_FIXTURE_FILE
                // en este modo, y mucho menos su CONTENIDO — eso sólo ocurre
                // en ReadyToExecute, dentro de CreateM5CustomerBExecutor. No
                // se construye ningún PassportLinkMerchantRequest, no se
                // obtiene token, no hay HTTP. Aislado de todo servicio de
                // producto XPAY — llamada directa harness ->
                // IPassportCustomerAccountClient -> Passport.
                output.WriteLine("case=M5-T1-A (Create/Link Customer B — infraestructura de certificación)");
                output.WriteLine("endpoint=POST /v1/customers/business/link");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-531)");
                output.WriteLine("requires=--execute --confirm-create-m5-customer-b");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=Customer B es un cliente SINTÉTICO dedicado a certificación — nunca el Customer A ni datos de identificación reales");
                output.WriteLine("note=M5-T1-A: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-531)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateM5AccountB:
                // XPAY-531 — NUNCA lee PASSPORT_TEST_M5_ACCOUNT_B_ACCOUNT_NUMBER
                // ni resuelve/abre el artefacto privado de Customer B (M5-T1-A)
                // en este modo — eso sólo ocurre en ReadyToExecute, dentro de
                // CreateM5AccountBExecutor.
                output.WriteLine("case=M5-T1-B (Create/Link Account B — infraestructura de certificación)");
                output.WriteLine("endpoint=POST /v1/accounts/link");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-531)");
                output.WriteLine("requires=--execute --confirm-create-m5-account-b");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=customer_id proviene EXCLUSIVAMENTE del artefacto privado M5-T1-A — nunca hardcoded, nunca Customer A, nunca un argumento CLI");
                output.WriteLine("note=account_number NUNCA es PASSPORT_ACCOUNT_ID (la cuenta operativa de XPAY) — Account B no es la cuenta operativa");
                output.WriteLine("note=M5-T1-B: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-531)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.ResolveKeyM5T2:
                // XPAY-545 — NUNCA lee PASSPORT_TEST_CUSTOMER_ID/
                // PASSPORT_TEST_BREB_KEY_TYPE/PASSPORT_TEST_BREB_KEY ni
                // M5PaymentPrivateCapture.EnvPrivateCaptureDir en este modo,
                // y mucho menos resuelve/abre ningún artefacto privado —
                // eso sólo ocurre en ReadyToExecute, dentro de
                // M5ResolveExecutor. No se construye ningún request, no se
                // obtiene token, no hay HTTP.
                output.WriteLine("case=M5-T2-RESOLVE (Resolve Key — dedicado a certificación Payment B->A)");
                output.WriteLine("endpoint=POST /v1/resolve-key");
                output.WriteLine("mutating=NO (read-only, resuelve la misma llave KEY_A ya usada desde M3-T2)");
                output.WriteLine("requires=--execute --confirm-resolve-key-m5-t2");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=Independiente de M3-T2 (congelado/ACCEPTED) — evidencia propia bajo case_id=M5-T2-RESOLVE, nunca escrita en docs/certificacion/passport-breb/M3-T2/");
                output.WriteLine("note=M5-T2-RESOLVE: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-545)");
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

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateM4T3SuspendedFixtureKey:
                // XPAY-474 — NUNCA lee el valor real de
                // PASSPORT_TEST_ACCOUNT_ID en este modo. CRÍTICO: el
                // key_value (DisposableBcodeGenerator) NUNCA se genera aquí
                // — sólo dentro de CreateM4T3SuspendedFixtureKeyExecutor,
                // exclusivamente en ReadyToExecute. Dry-run nunca consume
                // aleatoriedad ni produce un valor que luego no se usaría.
                output.WriteLine("case=M4-T3-A PREP (Create Key — llave desechable dedicada)");
                output.WriteLine("endpoint=POST /v1/keys");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-474)");
                output.WriteLine("requires=--execute --confirm-create-m4-t3-suspended-fixture-key");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M4-T3-A-KEY-PREP-CREATE: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-474)");
                output.WriteLine("note=Esta llave NO se suspende automáticamente — ver suspend-m4-t3-fixture-key (paso separado)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.SuspendM4T3FixtureKey:
                // XPAY-474 — NUNCA lee el valor real de
                // PASSPORT_TEST_QR_SUSPENDED_KEY_ID en este modo.
                output.WriteLine("case=M4-T3-A PREP (Suspend Key — llave desechable dedicada)");
                output.WriteLine("endpoint=PATCH /v1/keys/{key_id}/suspend");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-474)");
                output.WriteLine("requires=--execute --confirm-suspend-m4-t3-fixture-key");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M4-T3-A-KEY-PREP-SUSPEND: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-474)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.RecoverDeletedKeyId:
                // XPAY-489 — NUNCA lee los valores reales de
                // PASSPORT_TEST_ACCOUNT_ID/PASSPORT_TEST_NEW_KEY_TYPE/
                // PASSPORT_TEST_NEW_KEY_VALUE en este modo. Read-only por
                // diseño — ni siquiera en ReadyToExecute muta nada.
                output.WriteLine("case=RECOVERY (List Keys read-only — localizar key_id DELETED del ciclo M3)");
                output.WriteLine("endpoint=GET /v1/keys");
                output.WriteLine("mutating=NO (read-only — ninguna mutación de llave es alcanzable desde este comando)");
                output.WriteLine("requires=--execute --confirm-recover-deleted-key");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M4-T3-B-KEY-RECOVERY: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-489)");
                output.WriteLine("note=Fail-closed en 0 o >1 coincidencias — nunca selecciona automáticamente ni cae en fallback");
                output.WriteLine("note=Este comando NUNCA escribe ~/.passport-sandbox.env — la reparación de PASSPORT_TEST_NEW_KEY_ID es un paso manual separado");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.CreateM4T3DeletedFixtureKey:
                // XPAY-492 — NUNCA lee el valor real de
                // PASSPORT_TEST_ACCOUNT_ID en este modo. CRÍTICO: el
                // key_value (DisposableBcodeGenerator) NUNCA se genera aquí
                // — sólo dentro de CreateM4T3DeletedFixtureKeyExecutor,
                // exclusivamente en ReadyToExecute.
                output.WriteLine("case=M4-T3-B PREP (Create Key — llave desechable dedicada)");
                output.WriteLine("endpoint=POST /v1/keys");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-492)");
                output.WriteLine("requires=--execute --confirm-create-m4-t3-deleted-fixture-key");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M4-T3-B-KEY-PREP-CREATE: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-492)");
                output.WriteLine("note=Esta llave NO se elimina automáticamente — ver delete-m4-t3-fixture-key (paso separado)");
                return;

            case HarnessOrchestrator.Outcome.DryRun when decision.Command == HarnessCommand.DeleteM4T3FixtureKey:
                // XPAY-492 — NUNCA lee el valor real de
                // PASSPORT_TEST_QR_DELETED_KEY_ID en este modo.
                output.WriteLine("case=M4-T3-B PREP (Delete Key — llave desechable dedicada)");
                output.WriteLine("endpoint=DELETE /v1/keys/{key_id}");
                output.WriteLine("mutating=YES (llamada real futura — NO ejecutada en XPAY-492)");
                output.WriteLine("requires=--execute --confirm-delete-m4-t3-fixture-key");
                output.WriteLine("dry_run=YES http_call=NO oauth_token_requested=NO");
                output.WriteLine("result=DRY_RUN");
                output.WriteLine("note=DRY-RUN != CERTIFICATION EVIDENCE (no se genera evidencia en este modo)");
                output.WriteLine("note=M4-T3-B-KEY-PREP-DELETE: IMPLEMENTED_OFFLINE / NOT_EXECUTED_IN_SANDBOX (XPAY-492)");
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

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateQrDynamic:
            {
                var execution = await CreateQrDynamicExecutor
                    .ExecuteAsync(configuration, dependencies.QrClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-506 — un bloqueo LOCAL (target ausente, commit
                    // SHA no resoluble) NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var dynamicPath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={dynamicPath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.DecodeQrDynamic:
            {
                var execution = await DecodeQrDynamicExecutor
                    .ExecuteAsync(configuration, dependencies.QrClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-517 — un bloqueo LOCAL (target ausente, artefacto
                    // privado M4-T4 ausente/no coincidente, destino privado
                    // M4-T5 inválido, commit SHA no resoluble) NUNCA escribe
                    // evidence.json. `Detail` nunca incluye rutas privadas
                    // ni material crudo.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var m4T5Path = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={m4T5Path}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateQrDynamicSuspendedKey:
            {
                var execution = await CreateQrDynamicSuspendedKeyExecutor
                    .ExecuteAsync(configuration, dependencies.QrClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-522 — un bloqueo LOCAL (target ausente, fingerprint
                    // no coincidente con la fixture suspendida histórica,
                    // commit SHA no resoluble) NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var m4T6APath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={m4T6APath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateQrDynamicDeletedKey:
            {
                var execution = await CreateQrDynamicDeletedKeyExecutor
                    .ExecuteAsync(configuration, dependencies.QrClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var m4T6BPath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={m4T6BPath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateQrDynamicInvalidCustomer:
            {
                var execution = await CreateQrDynamicInvalidCustomerExecutor
                    .ExecuteAsync(configuration, dependencies.QrClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var m4T6CPath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={m4T6CPath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateBrebPaymentCert:
            {
                var execution = await CreateBrebPaymentCertificationExecutor
                    .ExecuteAsync(configuration, dependencies.PaymentClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-529 — un bloqueo LOCAL (PASSPORT_ACCOUNT_ID/
                    // resolution_id/amount ausente, resolución vencida,
                    // destino privado inválido, commit SHA no resoluble)
                    // NUNCA escribe evidence.json. `Detail` nunca incluye
                    // valores crudos ni rutas privadas.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var m5CreatePath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={m5CreatePath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.RetrieveBrebPaymentCert:
            {
                var execution = await RetrieveBrebPaymentCertificationExecutor
                    .ExecuteAsync(configuration, dependencies.PaymentClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var m5RetrievePath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={m5RetrievePath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateM5CustomerB:
            {
                var execution = await CreateM5CustomerBExecutor
                    .ExecuteAsync(configuration, dependencies.CustomerAccountClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-531 — un bloqueo LOCAL (fixture ausente/incompleta,
                    // destino privado inválido, commit SHA no resoluble)
                    // NUNCA escribe evidence.json. `Detail` nunca incluye
                    // datos de identificación ni rutas privadas.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var m5CustomerBPath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={m5CustomerBPath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateM5AccountB:
            {
                var execution = await CreateM5AccountBExecutor
                    .ExecuteAsync(configuration, dependencies.CustomerAccountClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-531 — un bloqueo LOCAL (account_number ausente,
                    // artefacto privado Customer B ausente/no válido, destino
                    // privado Account B inválido, commit SHA no resoluble)
                    // NUNCA escribe evidence.json. `Detail` nunca incluye
                    // valores crudos ni rutas privadas.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var m5AccountBPath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={m5AccountBPath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                output.WriteLine("note=Account B NO se asume equivalente a la cuenta operativa de XPAY, ni account_id_B se usará automáticamente como Payment.account_id");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.ResolveKeyM5T2:
            {
                var execution = await M5ResolveExecutor
                    .ExecuteAsync(configuration, dependencies.KeyClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-545 — un bloqueo LOCAL (destino privado inválido,
                    // lock ya sostenido, resolución previa vigente/no
                    // consumida, target de KEY_A ausente, commit SHA no
                    // resoluble) NUNCA escribe evidence.json. `Detail` nunca
                    // incluye resolution_id/customer_id/key_value ni rutas
                    // privadas.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var m5ResolvePath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={m5ResolvePath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                output.WriteLine("note=resolution_id crudo preservado EXCLUSIVAMENTE en el artefacto privado — nunca impreso, nunca en evidence.json");
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

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateM4T3SuspendedFixtureKey:
            {
                var execution = await CreateM4T3SuspendedFixtureKeyExecutor
                    .ExecuteAsync(configuration, dependencies.KeyClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-474 — un bloqueo LOCAL NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var createFixturePath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={createFixturePath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                output.WriteLine("note=Recupere el key_id real manualmente desde Passport Sandbox Dashboard y configure PASSPORT_TEST_QR_SUSPENDED_KEY_ID usted mismo — este harness nunca lo persiste");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.SuspendM4T3FixtureKey:
            {
                var execution = await SuspendM4T3FixtureKeyExecutor
                    .ExecuteAsync(configuration, dependencies.KeyClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-474 — un bloqueo LOCAL NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var suspendFixturePath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={suspendFixturePath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.RecoverDeletedKeyId:
            {
                var execution = await RecoverDeletedKeyIdExecutor
                    .ExecuteAsync(configuration, dependencies.KeyClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-489 — un bloqueo LOCAL NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var recoveryPath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={recoveryPath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                output.WriteLine("note=Este comando NUNCA escribió ~/.passport-sandbox.env — revise la evidencia sanitizada para el resultado (match_found/matches_count/status/id_fingerprint) y, si corresponde, configure PASSPORT_TEST_NEW_KEY_ID usted mismo, manualmente");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.CreateM4T3DeletedFixtureKey:
            {
                var execution = await CreateM4T3DeletedFixtureKeyExecutor
                    .ExecuteAsync(configuration, dependencies.KeyClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-492 — un bloqueo LOCAL NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var createFixturePath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={createFixturePath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                output.WriteLine("note=Recupere el key_id real manualmente desde Passport Sandbox Dashboard y configure PASSPORT_TEST_QR_DELETED_KEY_ID usted mismo — este harness nunca lo persiste");
                return;
            }

            case HarnessOrchestrator.Outcome.ReadyToExecute when decision.Command == HarnessCommand.DeleteM4T3FixtureKey:
            {
                var execution = await DeleteM4T3FixtureKeyExecutor
                    .ExecuteAsync(configuration, dependencies.KeyClient, dependencies.CommitShaProvider, DateTime.UtcNow)
                    .ConfigureAwait(false);

                if (execution.Outcome == KeyOperationOutcome.LocalBlocked)
                {
                    // XPAY-492 — un bloqueo LOCAL NUNCA escribe evidence.json.
                    output.WriteLine($"result=LOCAL_BLOCKED detail={execution.Detail}");
                    return;
                }

                var deleteFixturePath = EvidenceWriter.Write(dependencies.EvidenceBaseDirectory, execution.Evidence!);
                output.WriteLine($"result={execution.Evidence!.Result}");
                output.WriteLine($"evidence_path={deleteFixturePath}");
                output.WriteLine($"review_status={execution.Evidence.ReviewStatus}");
                return;
            }
        }
    }
}
