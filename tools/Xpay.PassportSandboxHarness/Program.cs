using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xpay.Api.Integrations.Passport;
using Xpay.PassportSandboxHarness;

// XPAY-312/325 — punto de entrada manual único. NO se invoca desde ningún
// controller/endpoint/CI. Uso esperado (fase futura, NO en XPAY-312/325):
//   dotnet run --project tools/Xpay.PassportSandboxHarness -- create-customer
//   dotnet run --project tools/Xpay.PassportSandboxHarness -- create-customer --execute --confirm-create-customer
//   dotnet run --project tools/Xpay.PassportSandboxHarness -- create-key
//   dotnet run --project tools/Xpay.PassportSandboxHarness -- create-key --execute --confirm-create-key
//
// Config: SOLO desde variables de entorno del proceso (AddEnvironmentVariables).
// Este código NUNCA abre ni parsea ~/.passport-sandbox.env — cargar esas
// variables al entorno del proceso (p. ej. mediante `source` en shell) es
// responsabilidad de una fase POSTERIOR y explícitamente controlada, fuera
// de este programa.
//
// Debe ejecutarse con el directorio de trabajo en la raíz del repositorio —
// el expediente de evidencia se escribe en ./docs/certificacion/passport-breb/
// relativo al directorio de trabajo actual.
//
// XPAY-325 — este archivo es ahora un wrapper DELGADO: toda la lógica de
// decisión/ejecución/evidencia vive en HarnessApp/CreateKeyExecutor
// (testeables con dependencias fake). Program.cs sólo construye el stack
// REAL de Passport (DI local al harness, sin tocar Xpay.Api.Program.cs) y
// delega. Esto es lo único que hace este archivo — no hay lógica propia que
// probar aquí más allá de la construcción de dependencias.

IConfiguration configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddHttpClient();
// XPAY-330 — nivel mínimo Warning GLOBAL: previene que los logging
// handlers automáticos de Microsoft.Extensions.Http (adjuntados por
// AddHttpClient() a nivel Information) impriman la URI completa de
// cualquier request (que puede contener un key_id/id sensible en el path
// o en el query string) — ver HarnessLogging.cs para el detalle del
// incidente y la corrección. Preserva los _logger.LogWarning(...)
// explícitos y ya saneados del stack Passport.
services.AddLogging(HarnessLogging.Configure);
services.AddSingleton(configuration);
services.AddSingleton(TimeProvider.System);
services.AddSingleton<IPassportTokenProvider, PassportTokenProvider>();
services.AddSingleton<IPassportHttpClient, PassportHttpClient>();
services.AddSingleton<IPassportCustomerAccountClient, PassportCustomerAccountClient>();
services.AddSingleton<IPassportKeyClient, PassportKeyClient>();
services.AddSingleton<ICommitShaProvider, GitCommitShaProvider>();

using var provider = services.BuildServiceProvider();

var dependencies = new HarnessApp.Dependencies(
    CustomerAccountClient: provider.GetRequiredService<IPassportCustomerAccountClient>(),
    KeyClient: provider.GetRequiredService<IPassportKeyClient>(),
    CommitShaProvider: provider.GetRequiredService<ICommitShaProvider>(),
    EvidenceBaseDirectory: Path.Combine("docs", "certificacion", "passport-breb"));

await HarnessApp.RunAsync(args, configuration, dependencies, Console.Out);
