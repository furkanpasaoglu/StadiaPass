using Microsoft.Extensions.Logging;

namespace StadiaPass.Application.UnitTests;

/// <summary>
/// An <see cref="ILogger{TCategoryName}"/> that keeps what it was told.
/// </summary>
/// <remarks>
/// Substituting a logger does not work for these: the source generator behind <c>[LoggerMessage]</c> guards
/// every call with <c>IsEnabled</c> and then calls a generic <c>Log&lt;TState&gt;</c>, which is awkward to
/// match on. Recording is both simpler to read and closer to what is actually being asserted - that a
/// particular thing was said, at a particular level, and not that some mock was called.
/// </remarks>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, EventId EventId, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, EventId EventId, string Message)> Entries => _entries;

    public bool Logged(LogLevel level) => _entries.Any(entry => entry.Level == level);

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        _entries.Add((logLevel, eventId, formatter(state, exception)));

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
