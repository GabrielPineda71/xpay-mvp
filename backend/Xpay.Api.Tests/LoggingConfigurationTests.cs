using Microsoft.Extensions.Configuration;
using Xunit;

namespace Xpay.Api.Tests;

// XPAY-382 PARTE 7 — valida el CONTENIDO REAL de appsettings.json (el
// archivo que efectivamente se publica/despliega, no una copia en memoria)
// para evitar una regresión silenciosa de la corrección de logging:
// Microsoft.Extensions.Http adjunta automáticamente handlers que registran
// a nivel Information la URI COMPLETA de cada request HttpClient — para
// las llamadas a Passport, eso incluye el payment_id/resolution_id/key_id
// real en el path (mismo incidente ya documentado y corregido para el
// harness offline en tools/Xpay.PassportSandboxHarness/HarnessLogging.cs,
// pero nunca aplicado a la app productiva hasta ahora).
//
// A diferencia del harness (que no tiene logs propios que conservar), aquí
// NO se puede bajar el nivel mínimo GLOBAL a Warning — silenciaría todos
// los _logger.LogInformation(...) ya sanitizados de la aplicación
// (BREB_RETIRO_REAL_*, BREB_RECONCILIACION_*, auditoría, etc.). La
// corrección correcta es un filtro DIRIGIDO sólo a la categoría
// "System.Net.Http" (que cubre "System.Net.Http.HttpClient.*.LogicalHandler"
///".ClientHandler", donde vive la URI completa), dejando "Default" en
// Information.
public class LoggingConfigurationTests
{
    private static IConfigurationRoot CargarAppSettings()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Xpay.Api", "appsettings.json");
        path = Path.GetFullPath(path);

        Assert.True(File.Exists(path),
            $"No se encontró appsettings.json en la ruta esperada: {path} — " +
            "¿cambió la estructura de carpetas del repo?");

        return new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
    }

    [Fact]
    public void AppSettings_SystemNetHttp_EstaEnWarning_ParaNoLoguearUrlsConIdsSensibles()
    {
        var config = CargarAppSettings();
        Assert.Equal("Warning", config["Logging:LogLevel:System.Net.Http"]);
    }

    // Guard contra el error opuesto: que alguien "arregle" el leak bajando
    // el nivel Default global (lo que silenciaría BREB_RETIRO_REAL_*/
    // BREB_RECONCILIACION_*/auditoría) en vez de filtrar sólo HttpClient.
    [Fact]
    public void AppSettings_DefaultLogLevel_SigueEnInformation_LogsPropiosNoSeApagan()
    {
        var config = CargarAppSettings();
        Assert.Equal("Information", config["Logging:LogLevel:Default"]);
    }
}
