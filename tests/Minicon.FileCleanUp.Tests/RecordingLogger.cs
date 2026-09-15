using Microsoft.Extensions.Logging;

namespace Minicon.FileCleanUp.Tests;

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<Dictionary<string, object?>> Events { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(x => x.Key, x => x.Value);
        properties["EventId"] = eventId.Id;
        properties["Level"] = logLevel;
        properties["Message"] = formatter(state, exception);
        Events.Add(properties);
    }
}
