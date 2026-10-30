using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scheduler.Application;
using Scheduler.Domain;
using Scheduler.Infrastructure;
using Xunit;

namespace Scheduler.IntegrationTests;

public sealed class EventDispatcherTests
{
    [Fact]
    public async Task EVT01_Subscriber_failure_is_logged_and_later_subscriber_runs_in_order()
    {
        var calls = new List<string>(); var logs = new CapturedLogs();
        var services = new ServiceCollection();
        services.AddScoped<IAppointmentConfirmedHandler>(_ => new Handler((_, _) =>
        { calls.Add("first"); throw new InvalidOperationException("SYNTHETIC-SECRET-CANARY"); }));
        services.AddScoped<IAppointmentConfirmedHandler>(_ => new Handler((_, _) =>
        { calls.Add("second"); return Task.CompletedTask; }));
        await using var provider = services.BuildServiceProvider();
        await Dispatcher(provider, logs).Dispatch(Message());
        Assert.Equal(new[] { "first", "second" }, calls);
        logs.AssertExcludes("SYNTHETIC-SECRET-CANARY");
        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task EVT01_Resolution_failure_is_noncritical_and_sanitized()
    {
        var logs = new CapturedLogs(); var services = new ServiceCollection();
        services.AddScoped<IAppointmentConfirmedHandler>(_ => throw new InvalidOperationException("SYNTHETIC-SECRET-CANARY"));
        await using var provider = services.BuildServiceProvider();
        await Dispatcher(provider, logs).Dispatch(Message());
        logs.AssertExcludes("SYNTHETIC-SECRET-CANARY");
        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task EVT01_Total_budget_cancels_active_handler_awaits_cleanup_and_skips_remaining()
    {
        var started = false; var cleaned = false; var later = false; var disposed = false;
        var services = new ServiceCollection();
        services.AddScoped<IAppointmentConfirmedHandler>(_ => new Handler(async (_, ct) =>
        {
            started = true;
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { cleaned = true; }
        }, () => { Assert.True(cleaned); disposed = true; }));
        services.AddScoped<IAppointmentConfirmedHandler>(_ => new Handler((_, _) => { later = true; return Task.CompletedTask; }));
        await using var provider = services.BuildServiceProvider();
        await Dispatcher(provider, new CapturedLogs(), 100).Dispatch(Message()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(started); Assert.True(cleaned); Assert.True(disposed); Assert.False(later);
    }

    [Fact]
    public async Task EVT01_Registered_diagnostic_handler_honors_cancellation_without_logging_confirmation()
    {
        var logs = new CapturedLogs();
        using var logging = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var handler = new BookingDiagnosticHandler(logging.CreateLogger<BookingDiagnosticHandler>());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.Handle(Message(), cancellation.Token));
        Assert.Empty(logs.Entries);
    }

    [Fact]
    public async Task EVT01_New_dispatcher_scope_does_not_replay_previous_events()
    {
        var received = new List<Guid>(); var services = new ServiceCollection();
        services.AddScoped<IAppointmentConfirmedHandler>(_ => new Handler((message, _) =>
        { received.Add(message.EventId); return Task.CompletedTask; }));
        var message = Message();
        await using (var first = services.BuildServiceProvider()) await Dispatcher(first, new CapturedLogs()).Dispatch(message);
        await using var restarted = services.BuildServiceProvider();
        _ = Dispatcher(restarted, new CapturedLogs());
        Assert.Equal(new[] { message.EventId }, received);
    }

    private static AppointmentConfirmed Message() => new(Guid.NewGuid(), Guid.NewGuid(), TestDatabase.Now);
    private static AppointmentEventDispatcher Dispatcher(ServiceProvider provider, CapturedLogs logs, int milliseconds = 1000) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new EventDispatchOptions { DispatchBudgetMilliseconds = milliseconds }),
            new Logger<AppointmentEventDispatcher>(new LoggerFactory([logs])));
    private sealed class Handler(Func<AppointmentConfirmed, CancellationToken, Task> action, Action? dispose = null) : IAppointmentConfirmedHandler, IDisposable
    {
        public Task Handle(AppointmentConfirmed message, CancellationToken ct) => action(message, ct);
        public void Dispose() => dispose?.Invoke();
    }
}
