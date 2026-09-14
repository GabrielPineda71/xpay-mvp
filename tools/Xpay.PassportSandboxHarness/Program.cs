using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;

// XPAY-312 — punto de entrada manual único. NO se invoca desde ningún
// controller/endpoint/CI. Uso esperado (fase futura, NO en XPAY-312):
//   dotnet run --project tools/Xpay.PassportSandboxHarness -- create-customer
//   dotnet run --project tools/Xpay.PassportSandboxHarness -- create-customer --execute --confirm-create-customer
//
// Config: SOLO desde variables de entorno del proceso (AddEnvironmentVariables).
// Este código NUNCA abre ni parsea ~/.passport-sandbox.env — cargar esas
// variables al entorno del proceso (p. ej. mediante `source` en shell) es
// responsabilidad de una fase POSTERIOR y explícitamente controlada, fuera
// de este programa.

IConfiguration configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

var decision = HarnessOrchestrator.Prepare(args, configuration);

Console.WriteLine("== Xpay Passport Sandbox Harness (XPAY-312) ==");
Console.WriteLine("case=M2-T1 (Create Customer) — único soportado en esta fase.");
foreach (var line in decision.ConfigStatus.ToRedactedLines())
    Console.WriteLine(line);

switch (decision.Outcome)
{
    case HarnessOrchestrator.Outcome.ShowHelp:
        Console.WriteLine();
        Console.WriteLine("Uso: create-customer [--execute --confirm-create-customer]");
        Console.WriteLine("Sin argumentos o sin ambas banderas: modo dry-run (sin HTTP).");
        return;

    case HarnessOrchestrator.Outcome.AbortedMissingConfirmation:
    case HarnessOrchestrator.Outcome.AbortedConfigMissing:
    case HarnessOrchestrator.Outcome.AbortedNonSandboxHost:
        Console.WriteLine($"result=ABORTED detail={decision.Detail}");
        return;

    case HarnessOrchestrator.Outcome.DryRun:
    {
        // FASE 6 — construir el request sintético y confirmar que serializa,
        // SIN ningún HttpClient, SIN ningún token, SIN ninguna llamada HTTP.
        var request = SyntheticCustomerRequestFactory.BuildSynthetic();
        var json = System.Text.Json.JsonSerializer.Serialize(request);
        Console.WriteLine($"dry_run=YES http_call=NO request_bytes={json.Length}");
        Console.WriteLine("result=DRY_RUN");
        return;
    }

    case HarnessOrchestrator.Outcome.ReadyToExecute:
    {
        // XPAY-312 NUNCA alcanza esta rama (nunca se invoca con
        // --execute --confirm-create-customer en esta fase). Se deja el
        // cableado del stack REAL (sin segundo HTTP/OAuth) preparado para
        // una fase futura explícitamente autorizada, pero NO se ejecuta aquí.
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddLogging(b => b.AddConsole());
        services.AddSingleton(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPassportTokenProvider, PassportTokenProvider>();
        services.AddSingleton<IPassportHttpClient, PassportHttpClient>();
        services.AddSingleton<IPassportCustomerAccountClient, PassportCustomerAccountClient>();

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IPassportCustomerAccountClient>();

        // NO se llama a client.LinkMerchantAsync aquí de forma automática:
        // eso sería una llamada real a Passport Sandbox. Queda como
        // comentario intencional — la fase futura que autorice la ejecución
        // real decidirá explícitamente cuándo invocarlo:
        //
        //   var request = SyntheticCustomerRequestFactory.BuildSynthetic(); // o datos reales aprobados
        //   var response = await client.LinkMerchantAsync(request);
        //
        _ = client; // evita warning de variable no usada sin invocar nada real.
        Console.WriteLine("result=READY_TO_EXECUTE detail=stack_construido_pero_no_invocado");
        return;
    }
}
