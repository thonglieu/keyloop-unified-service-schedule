using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scheduler.Application;
using Scheduler.Domain;

namespace Scheduler.Infrastructure;

public sealed class EventDispatchOptions { public int DispatchBudgetMilliseconds { get; set; } = 1000; }
public sealed class AppointmentEventDispatcher(IServiceScopeFactory scopes, IOptions<EventDispatchOptions> options, ILogger<AppointmentEventDispatcher> logger) : IAppointmentEventDispatcher
{
    public async Task Dispatch(AppointmentConfirmed message)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Clamp(options.Value.DispatchBudgetMilliseconds, 1, 60000)));
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handlers = scope.ServiceProvider.GetServices<IAppointmentConfirmedHandler>();
            foreach (var handler in handlers)
            {
                if (budget.IsCancellationRequested) { logger.LogWarning("Booking diagnostic dispatch budget exhausted"); break; }
                try { await handler.Handle(message, budget.Token); }
                catch (Exception) { logger.LogWarning("Booking diagnostic subscriber failed or was cancelled"); }
            }
        }
        catch (Exception) { logger.LogWarning("Booking diagnostic dispatch failed"); }
    }
}
public sealed class BookingDiagnosticHandler(ILogger<BookingDiagnosticHandler> logger) : IAppointmentConfirmedHandler
{
    public Task Handle(AppointmentConfirmed message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        logger.LogInformation("Booking confirmed; event {EventId}; appointment {AppointmentId}", message.EventId, message.AppointmentId);
        return Task.CompletedTask;
    }
}
