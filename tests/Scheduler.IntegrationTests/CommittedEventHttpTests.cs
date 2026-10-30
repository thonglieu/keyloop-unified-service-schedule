using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scheduler.Application;
using Scheduler.Domain;
using Scheduler.Infrastructure;
using Xunit;

namespace Scheduler.IntegrationTests;

public sealed class CommittedEventHttpTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [Fact]
    public async Task EVT01_Real_HTTP_commit_survives_throwing_subscriber_and_continues_next_subscriber()
    {
        var s = await database.CreateScenario(); var order = new List<string>();
        var persistedAtDispatch = false; Guid dispatched = default;
        await using var host = new SchedulerFactory(database.ConnectionString, configure: services =>
        {
            services.RemoveAll<IAppointmentConfirmedHandler>();
            services.AddScoped<IAppointmentConfirmedHandler>(_ => new Subscriber(async (message, _) =>
            {
                order.Add("throwing"); dispatched = message.AppointmentId;
                await using var independent = database.OpenContext();
                persistedAtDispatch = await independent.Appointments.AnyAsync(a => a.Id == message.AppointmentId);
                throw new InvalidOperationException("Synthetic subscriber failure");
            }));
            services.AddScoped<IAppointmentConfirmedHandler>(_ => new Subscriber((_, _) =>
            { order.Add("later"); return Task.CompletedTask; }));
        });
        using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/appointments", s.Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var appointment = (await response.Content.ReadFromJsonAsync<AppointmentDto>())!;
        Assert.Equal(new[] { "throwing", "later" }, order);
        Assert.True(persistedAtDispatch); Assert.Equal(appointment.Id, dispatched);
        await using var fresh = database.OpenContext();
        Assert.Equal(appointment, AppointmentDto.From(await fresh.Appointments.SingleAsync(a => a.Id == appointment.Id)));
    }

    [Fact]
    public async Task EVT01_Real_HTTP_commit_survives_cooperative_dispatch_timeout_and_awaits_cleanup()
    {
        var s = await database.CreateScenario(); var started = false; var cleaned = false; var later = false;
        await using var host = new SchedulerFactory(database.ConnectionString, configure: services =>
        {
            services.Configure<EventDispatchOptions>(options => options.DispatchBudgetMilliseconds = 150);
            services.RemoveAll<IAppointmentConfirmedHandler>();
            services.AddScoped<IAppointmentConfirmedHandler>(_ => new Subscriber(async (_, ct) =>
            {
                started = true;
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                finally { cleaned = true; }
            }));
            services.AddScoped<IAppointmentConfirmedHandler>(_ => new Subscriber((_, _) =>
            { later = true; return Task.CompletedTask; }));
        });
        using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/appointments", s.Request()).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(started); Assert.True(cleaned); Assert.False(later);
        var appointment = (await response.Content.ReadFromJsonAsync<AppointmentDto>())!;
        await using var independent = database.OpenContext();
        Assert.Equal(appointment, AppointmentDto.From(await independent.Appointments.SingleAsync(a => a.Id == appointment.Id)));
    }

    private sealed class Subscriber(Func<AppointmentConfirmed, CancellationToken, Task> action) : IAppointmentConfirmedHandler
    {
        public Task Handle(AppointmentConfirmed message, CancellationToken ct) => action(message, ct);
    }
}
