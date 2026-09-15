using Microsoft.Extensions.Logging;

namespace Xpay.PassportSandboxHarness;

// XPAY-330 — configuración de logging ÚNICA y COMPARTIDA para TODO el
// harness (OAuth, Create/List/Suspend/Activate/Delete/Resolve Key, y
// cualquier operación HttpClient futura). Extraída a un método estático
// puro porque Program.cs (top-level statements) no es testeable
// directamente (mismo criterio ya aplicado a HarnessApp/CreateKeyExecutor/
// SuspendKeyExecutor en XPAY-325/326) — HarnessLoggingTests ejercita esta
// configuración end-to-end contra un IHttpClientFactory real con handler
// fake, exactamente el mismo mecanismo que produjo el incidente.
//
// ── INCIDENTE (ejecución real de M3-T3, Suspend Key) ──────────────────
// Program.cs registraba `services.AddHttpClient()` + `services.AddLogging
// (b => b.AddConsole())` SIN fijar un nivel mínimo. `AddHttpClient()`
// adjunta automáticamente, a TODO HttpClient que construya (nombrado o
// default), dos DelegatingHandlers de Microsoft.Extensions.Http
// ("LoggingScopeHttpMessageHandler"/"LoggingHttpMessageHandler",
// categorías "System.Net.Http.HttpClient.{Name}.LogicalHandler"/
// ".ClientHandler") que registran a nivel Information la línea completa
// "Start/Sending/Received/End processing HTTP request {Method} {Uri}" —
// la URI COMPLETA, incluyendo cualquier segmento de path o query string
// sensible (p. ej. el key_id remoto real en
// "PATCH .../v1/keys/{key_id}/suspend"). Esta capa es COMPLETAMENTE
// DISTINTA de los `_logger.LogWarning(...)` explícitos y ya saneados de
// PassportHttpClient/PassportTokenProvider (que nunca incluyen URI/body/
// token) — nunca había sido auditada como parte de "no logging sensible".
// Con Create Key (M3-T1) el mismo riesgo no se manifestó porque
// account_id/key_value viajan en el BODY JSON (que estos handlers no
// registran), nunca en la URL — Suspend Key fue el primer caso con un
// identificador sensible EN LA RUTA.
//
// ── CORRECCIÓN ──────────────────────────────────────────────────────────
// Nivel mínimo GLOBAL = Warning. Esto:
//   - PRESERVA todos los `_logger.LogWarning(...)` explícitos ya saneados
//     (rechazo de auth, error de transporte, timeout, protocolo
//     ilegible) — la señal de diagnóstico realmente útil.
//   - SUPRIME los logs Information/Debug/Trace del pipeline HttpClient
//     (que son los que contienen la URI completa), y también la única
//     línea LogInformation no sensible que emite PassportTokenProvider al
//     refrescar el token con éxito (prescindible; no es una señal de
//     error, no se pierde nada crítico).
// Deliberadamente GLOBAL — no un filtro puntual sólo para la categoría
// "System.Net.Http.*" ni sólo para Suspend: cualquier operación HttpClient
// futura del harness (List Keys ya implementado, Activate/Delete/Resolve
// pendientes, o lo que sea) queda cubierta automáticamente, sin depender
// de que alguien recuerde añadir un filtro nuevo cada vez que se agregue
// un endpoint. Prevención en el ORIGEN (nivel de logging), no un
// post-procesado de string-replace sobre la consola.
public static class HarnessLogging
{
    public static void Configure(ILoggingBuilder builder)
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Warning);
    }
}
