using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Scheduler.Domain;
using Scheduler.Infrastructure;
using Xunit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Net.Http.Headers;

namespace Scheduler.IntegrationTests;

// Tests never fall back to the demo database and deliberately fail when a prerequisite is absent.
public sealed class TestDatabase : IAsyncLifetime
{
    public static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public string ConnectionString { get; private set; } = "";
    public SchedulerFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__SchedulerTestDb");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("Real PostgreSQL prerequisite unavailable: set ConnectionStrings__SchedulerTestDb to an isolated keyloop_scheduler_test_<run-id> database.");
        var cs = new NpgsqlConnectionStringBuilder(configured);
        var expectedPort = int.Parse(Environment.GetEnvironmentVariable("SchedulerTests__PostgresPort") ?? "5432");
        if (expectedPort is < 1 or > 65535) throw new InvalidOperationException("Invalid isolated test PostgreSQL port.");
        if (cs.Host is not ("localhost" or "127.0.0.1") || cs.Port != expectedPort ||
            !Regex.IsMatch(cs.Database ?? "", "^keyloop_scheduler_test_[a-zA-Z0-9_]+$"))
            throw new InvalidOperationException("Integration tests require localhost on the explicitly configured test port and a dedicated keyloop_scheduler_test_<run-id> database.");
        cs.IncludeErrorDetail = false;
        ConnectionString = cs.ConnectionString;
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SHOW server_version", connection);
        var version = (string)(await command.ExecuteScalarAsync())!;
        // An explicit override records a user-approved deviation; it never silently substitutes a version.
        var expected = Environment.GetEnvironmentVariable("SchedulerTests__ExpectedPostgresVersion") ?? "17.2";
        if (version.Split(' ')[0] != expected)
            throw new InvalidOperationException($"PostgreSQL prerequisite mismatch: expected {expected}, actual {version.Split(' ')[0]}. No migration or fixture writes performed.");
        await using var db = OpenContext();
        await db.Database.MigrateAsync();
        Factory = new SchedulerFactory(ConnectionString);
        Client = Factory.CreateClient();
    }

    public SchedulerDbContext OpenContext() => new(new DbContextOptionsBuilder<SchedulerDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task<Scenario> CreateScenario(int technicians = 1, int bays = 1, bool qualified = true,
        int duration = 60, int technicianWindowMinutes = 540, int bayWindowMinutes = 540)
    {
        await using var db = OpenContext();
        var dealership = new Dealership { Id = Guid.NewGuid(), Name = "Integration dealership" };
        var service = new ServiceType { Id = Guid.NewGuid(), Name = "Integration service", DurationMinutes = duration };
        var start = Now.AddDays(1).AddHours(2); // 09:00 dealership-local, opening starts 01:00 UTC.
        var opening = start.AddHours(-1);
        db.AddRange(dealership, service);
        var people = Enumerable.Range(0, 20).Select(i => new Customer { Id = Guid.NewGuid(), Name = $"Synthetic {i}" }).ToArray();
        var vehicles = people.Select(c => new Vehicle { Id = Guid.NewGuid(), CustomerId = c.Id, Registration = Guid.NewGuid().ToString("N"), Make = "Test", Model = "Fixture" }).ToArray();
        db.AddRange(people); db.AddRange(vehicles);
        var techs = Enumerable.Range(0, technicians).Select(i => new Technician { Id = Guid.NewGuid(), DealershipId = dealership.Id, Name = $"Tech {i}" }).ToArray();
        var serviceBays = Enumerable.Range(0, bays).Select(i => new ServiceBay { Id = Guid.NewGuid(), DealershipId = dealership.Id, Name = $"Bay {i}" }).ToArray();
        db.AddRange(techs); db.AddRange(serviceBays);
        foreach (var tech in techs)
        {
            if (qualified) db.Add(new TechnicianServiceType { TechnicianId = tech.Id, ServiceTypeId = service.Id });
            db.Add(new TechnicianAvailability { Id = Guid.NewGuid(), TechnicianId = tech.Id, StartsAtUtc = opening, EndsAtUtc = opening.AddMinutes(technicianWindowMinutes) });
        }
        foreach (var bay in serviceBays)
            db.Add(new ServiceBayAvailability { Id = Guid.NewGuid(), ServiceBayId = bay.Id, StartsAtUtc = opening, EndsAtUtc = opening.AddMinutes(bayWindowMinutes) });
        await db.SaveChangesAsync();
        return new Scenario(dealership.Id, service.Id, start, duration, people.Select(c => c.Id).ToArray(), vehicles.Select(v => v.Id).ToArray(), techs.Select(t => t.Id).ToArray(), serviceBays.Select(b => b.Id).ToArray());
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null) await Factory.DisposeAsync();
        // Preserve the isolated DB for inspection; no destructive cleanup of any database.
    }
}

public sealed record Scenario(Guid DealershipId, Guid ServiceTypeId, DateTimeOffset Start, int Duration,
    Guid[] Customers, Guid[] Vehicles, Guid[] Technicians, Guid[] Bays)
{
    public object Request(int customer = 0, DateTimeOffset? start = null) => new
    {
        customerId = Customers[customer], vehicleId = Vehicles[customer], dealershipId = DealershipId,
        serviceTypeId = ServiceTypeId, startsAt = (start ?? Start).ToString("O")
    };
    public string Availability(DateTimeOffset? start = null) =>
        $"/api/availability?dealershipId={DealershipId}&serviceTypeId={ServiceTypeId}&startsAt={Uri.EscapeDataString((start ?? Start).ToString("O"))}";
    public Appointment Row(int technician = 0, int bay = 0, DateTimeOffset? start = null) =>
        Appointment.Confirm(Customers[0], Vehicles[0], DealershipId, ServiceTypeId, Technicians[technician], Bays[bay],
            new UtcInterval(start ?? Start, (start ?? Start).AddMinutes(Duration)), TestDatabase.Now);
}

public sealed class SchedulerFactory(string connection, string environment = "Development", bool demo = false,
    Action<IServiceCollection>? configure = null, Dictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    internal SyntheticJwt Tokens { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:SchedulerDb"] = connection,
            ["Demo:Enabled"] = demo.ToString(),
            ["Jwt:Authority"] = SyntheticJwt.Issuer,
            ["Jwt:Issuer"] = SyntheticJwt.Issuer,
            ["Jwt:Audience"] = SyntheticJwt.Audience,
            ["Jwt:SubjectClaimType"] = "sub",
            ["Jwt:RoleClaimType"] = "role"
        };
        if (settings is not null) foreach (var pair in settings) values[pair.Key] = pair.Value;
        foreach (var pair in values) builder.UseSetting(pair.Key, pair.Value);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(values));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FrozenClock());
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // Replace metadata discovery only. Keep production signature, issuer, audience,
                // lifetime, subject validation and role policies intact.
                var metadata = new OpenIdConnectConfiguration { Issuer = SyntheticJwt.Issuer };
                metadata.SigningKeys.Add(Tokens.PublicKey);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            });
            configure?.Invoke(services);
        });
    }
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.BaseAddress = new Uri("https://localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Tokens.Issue());
    }
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Tokens.Dispose();
    }
    private sealed class FrozenClock : TimeProvider { public override DateTimeOffset GetUtcNow() => TestDatabase.Now; }
}
