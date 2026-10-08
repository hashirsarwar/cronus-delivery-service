using Microsoft.Extensions.Logging;

namespace Cronus.DeliveryService.Tests.Infrastructure;

/// <summary>Captures named log properties for assertions.</summary>
/// <remarks>Rendered messages alone cannot prove identifiers remain queryable as structured fields.</remarks>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

        // The logger state is the templated value list, so the named holes are readable here. The
        // final entry is always {OriginalFormat}, which is the template itself.
        if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
        {
            foreach (var value in values)
            {
                properties[value.Key] = value.Value;
            }
        }

        _entries.Add(new LogEntry(logLevel, formatter(state, exception), properties));
    }
}

/// <summary>One captured log entry: its level, its rendered message and its named properties.</summary>
internal sealed record LogEntry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Properties);
