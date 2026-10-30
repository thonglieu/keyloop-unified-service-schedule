using System.Net;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Scheduler.Domain;
using Scheduler.Infrastructure;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application;

namespace Scheduler.IntegrationTests;

public sealed class EnvironmentTests
{
    private const string Unreachable = "Host=127.0.0.1;Port=1;Database=keyloop_scheduler_test_unreachable;Username=synthetic;Timeout=1;Command Timeout=1";

    [Fact]
    public async Task API01_Real_connection_failure_is_classified_as_unavailable_through_provider_wrappers()
    {
        await using var host = new SchedulerFactory(Unreachable);
        await using var scope = host.Services.CreateAsyncScope();
        var transactions = scope.ServiceProvider.GetRequiredService<IBookingTransactionFactory>();
        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var transaction = await transactions.Begin(default);
        });
        var types = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
            types.Add(current.GetType().FullName!);
        Assert.True(scope.ServiceProvider.GetRequiredService<IDependencyFailureClassifier>().IsUnavailable(exception),
            "Observed provider exception chain: " + string.Join(" -> ", types));
    }

    [Fact]
    public async Task API01_Known_database_unavailability_is_503_and_emits_failed_metric_duration_and_booking_span()
    {
        var activities = new ConcurrentBag<string>();
        var outcomes = new ConcurrentBag<string>();
        var durations = new ConcurrentBag<double>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Scheduler.Booking",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity.OperationName)
        };
        ActivitySource.AddActivityListener(activityListener);
        using var meter = new MeterListener();
        meter.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Scheduler.Booking") listener.EnableMeasurementEvents(instrument);
        };
        meter.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name != "scheduler.bookings") return;
            foreach (var tag in tags)
                if (tag.Key == "outcome" && measurement == 1) outcomes.Add(tag.Value?.ToString() ?? "");
        });
        meter.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "scheduler.booking.duration") durations.Add(measurement);
        });
        meter.Start();
        await using var host = new SchedulerFactory(Unreachable, "Production");
        using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/appointments", new
        {
            customerId = Guid.NewGuid(), vehicleId = Guid.NewGuid(), dealershipId = Guid.NewGuid(),
            serviceTypeId = Guid.NewGuid(), startsAt = TestDatabase.Now.AddDays(1).ToString("O")
        });
        await ProblemAssertions.Read(response, HttpStatusCode.ServiceUnavailable, "dependency_unavailable");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("failed", outcomes);
        Assert.Contains(durations, value => value >= 0);
        Assert.Contains("booking", activities);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Npgsql", body);
        Assert.DoesNotContain("synthetic", body);
    }

    [Fact]
    public async Task AC27_Database_unavailable_returns_503_but_process_remains_live()
    {
        await using var host = new SchedulerFactory(Unreachable);
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var response = await client.GetAsync("/health/ready").WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public async Task AC17_Fixture_endpoint_is_disabled_outside_permitted_demo_environment(string environment, bool enabled)
    {
        await using var host = new SchedulerFactory(Unreachable, environment, enabled);
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/demo/fixtures")).StatusCode);
    }

    [Theory]
    [InlineData("Production", "true", "keyloop_scheduler_demo", "--seed-demo")]
    [InlineData("Development", "false", "keyloop_scheduler_demo", "--seed-demo")]
    [InlineData("Development", "true", "keyloop_scheduler_test_forbiddenseed", "--seed-demo")]
    [InlineData("Production", "true", "keyloop_scheduler_demo", "--seed-demo-if-needed")]
    [InlineData("Development", "false", "keyloop_scheduler_demo", "--seed-demo-if-needed")]
    [InlineData("Development", "true", "keyloop_scheduler_test_forbiddenseed", "--seed-demo-if-needed")]
    [InlineData("Production", "true", "keyloop_scheduler_demo", "--migrate")]
    [InlineData("Development", "false", "keyloop_scheduler_demo", "--migrate")]
    [InlineData("Development", "true", "keyloop_scheduler_test_forbiddenseed", "--migrate")]
    public async Task AC17_Seed_command_rejects_each_guard_before_connecting(string environment, string enabled, string database, string command)
    {
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "Keyloop.ServiceScheduler.sln")))
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Repository solution not found.");
        var apiAssembly = Path.Combine(root, "src", "backend", "Scheduler.Api", "bin",
#if DEBUG
            "Debug",
#else
            "Release",
#endif
            "net8.0", "Scheduler.Api.dll");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
        start.ArgumentList.Add(apiAssembly);
        start.ArgumentList.Add(command);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
        start.Environment["DOTNET_ENVIRONMENT"] = environment;
        start.Environment["Demo__Enabled"] = enabled;
        start.Environment["ConnectionStrings__SchedulerDb"] = $"Host=127.0.0.1;Port=1;Database={database};Username=synthetic;Timeout=1";
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        catch { process.Kill(entireProcessTree: true); throw; }
        var output = await outputTask + await errorTask;
        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("Local setup requires Development, Demo.Enabled and the allowlisted demo database.", output);
        Assert.DoesNotContain("Failed to connect", output);
    }
}

public sealed class FixturePersistenceTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [Fact]
    public async Task AC16_Seed_core_creates_isolated_namespaces_without_removing_existing_data()
    {
        // Exercise seed persistence in an isolated test DB; executable allowlist is checked separately.
        await using var db = database.OpenContext();
        var before = await db.Appointments.CountAsync();
        var first = await DemoSeeder.Seed(db, new FixedClock(), 14);
        var second = await DemoSeeder.Seed(db, new FixedClock(), 14);
        Assert.NotEqual(first.Namespace, second.Namespace);
        Assert.Empty(first.Customers.Select(x => x.CustomerId).Intersect(second.Customers.Select(x => x.CustomerId)));
        Assert.True(first.Customers.Length >= 20);
        Assert.Equal(8, first.Scenarios.Count);
        Assert.All(first.Scenarios.Values, s => Assert.True(s.StartsAt > TestDatabase.Now));
        Assert.Equal(2, first.Scenarios["twoPairRace"].TechnicianIds.Length);
        Assert.Single(first.Scenarios["singlePairRace"].ServiceBayIds);
        Assert.Equal(before + 4, await db.Appointments.CountAsync());
        await using var fresh = database.OpenContext();
        Assert.True(await fresh.Set<DemoFixture>().AnyAsync(x => x.Id == Guid.Parse(first.Namespace)));
        Assert.True(await fresh.Set<DemoFixture>().AnyAsync(x => x.Id == Guid.Parse(second.Namespace)));
    }

    [Fact]
    public async Task AC27_Missing_schema_is_not_ready_even_with_a_valid_connection()
    {
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(database.ConnectionString) { SearchPath = "nonexistent_scheduler_test_schema" };
        await using var host = new SchedulerFactory(connection.ConnectionString);
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }

    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => TestDatabase.Now; }
}
