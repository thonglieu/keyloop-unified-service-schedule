using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Scheduler.IntegrationTests;

internal sealed class CapturedLogs : ILoggerProvider
{
    public ConcurrentQueue<Entry> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, Entries);
    public void Dispose() { }

    public void AssertExcludes(params string[] canaries)
    {
        Assert.NotEmpty(Entries);
        foreach (var entry in Entries)
        {
            var text = entry.Message + " " + entry.Exception + " " +
                string.Join(" ", entry.Properties.Select(p => $"{p.Key}={p.Value}"));
            foreach (var canary in canaries) Assert.DoesNotContain(canary, text);
        }
    }

    internal sealed record Entry(string Category, LogLevel Level, EventId EventId, string Message,
        string? Exception, KeyValuePair<string, object?>[] Properties);

    private sealed class CaptureLogger(string category, ConcurrentQueue<Entry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToArray() : [];
            entries.Enqueue(new Entry(category, logLevel, eventId, formatter(state, exception),
                exception?.ToString(), properties));
        }
    }
}
