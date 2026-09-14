using Microsoft.Extensions.Logging;

namespace Xpay.Api.Tests.Integrations.Passport;

// ILogger<T> falso que captura cada mensaje YA FORMATEADO (el mismo texto
// que un sink real vería). Se usa exclusivamente para probar que ningún
// mensaje de log emitido por la implementación contiene ClientSecret ni
// AccessToken — nunca para inspeccionar comportamiento funcional.
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = new();
    public IReadOnlyList<string> Messages => _messages;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _messages.Add(message);
        if (exception is not null)
            _messages.Add(exception.ToString());
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
