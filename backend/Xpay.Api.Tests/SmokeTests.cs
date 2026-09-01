using Xunit;

namespace Xpay.Api.Tests;

// M2.0 — smoke test único: sólo demuestra que el proyecto de pruebas
// restaura, compila, y que `dotnet test` descubre y ejecuta xUnit.
// No importa MiDecisor, no usa red, no usa variables de entorno, no toca BD.
public class SmokeTests
{
    [Fact]
    public void TestInfrastructure_IsOperational()
    {
        Assert.True(true);
    }
}
