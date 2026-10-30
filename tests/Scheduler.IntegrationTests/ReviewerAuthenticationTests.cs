using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scheduler.Api;
using Scheduler.Application;
using Scheduler.Infrastructure;
using Xunit;

namespace Scheduler.IntegrationTests;

public sealed class ReviewerAuthenticationTests
{
    [Theory]
    [InlineData("Production", true, "keyloop_scheduler_demo")]
    [InlineData("Development", false, "keyloop_scheduler_demo")]
    [InlineData("Development", true, "keyloop_scheduler_test_forbidden")]
    public void Reviewer_authentication_fails_startup_outside_explicit_demo_guards(string environment, bool demo, string database)
    {
        using var host = new ReviewerFactory(environment: environment, demo: demo, database: database);
        var error = Assert.Throws<InvalidOperationException>(() => host.CreateClient());
        Assert.Contains("Reviewer authentication requires", error.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Disabled_reviewer_endpoint_is_absent(string environment)
    {
        await using var host = new ReviewerFactory(enabled: false, environment: environment);
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/demo/auth", null)).StatusCode);
    }

    [Fact]
    public async Task External_authentication_does_not_accept_reviewer_tokens()
    {
        await using var host = new SchedulerFactory("Host=127.0.0.1;Port=1;Database=keyloop_scheduler_test_unused;Username=synthetic");
        using var client = host.CreateClient();
        using var reviewer = new ReviewerTokenIssuer();
        client.DefaultRequestHeaders.Authorization = new("Bearer", reviewer.Issue("reviewer-admin", "Admin", DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dealerships")).StatusCode);
    }

    [Fact]
    public async Task Reviewer_tokens_use_real_bearer_roles_signature_and_lifetime_validation()
    {
        var reads = new ApplicationBehaviorTests.Fixture().Reads;
        await using var host = new ReviewerFactory(configure: services =>
        {
            services.RemoveAll<ISchedulingReadStore>();
            services.AddSingleton<ISchedulingReadStore>(reads);
        });
        using var client = host.CreateClient();
        var issuer = host.Services.GetRequiredService<ReviewerTokenIssuer>();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dealerships")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", issuer.Issue("reviewer-admin", "Admin", expiry));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dealerships")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", issuer.Issue("reviewer-customer", "Customer", expiry));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dealerships")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/customers")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", issuer.Issue("reviewer-unknown", "Unknown", expiry));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dealerships")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", issuer.Issue("reviewer-admin", "Admin", DateTimeOffset.UtcNow.AddMinutes(-5)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dealerships")).StatusCode);
        using var differentProcess = new ReviewerTokenIssuer();
        client.DefaultRequestHeaders.Authorization = new("Bearer", differentProcess.Issue("reviewer-admin", "Admin", expiry));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dealerships")).StatusCode);
    }

    internal sealed class ReviewerFactory(bool enabled = true, string environment = "Development", bool demo = true,
        string database = "keyloop_scheduler_demo", Action<IServiceCollection>? configure = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            var values = new Dictionary<string, string?>
            {
                ["ReviewerAuth:Enabled"] = enabled.ToString(), ["Demo:Enabled"] = demo.ToString(),
                ["ConnectionStrings:SchedulerDb"] = $"Host=127.0.0.1;Port=1;Database={database};Username=synthetic;Timeout=1",
                ["Jwt:Authority"] = "", ["Jwt:Issuer"] = "", ["Jwt:Audience"] = ""
            };
            foreach (var pair in values) builder.UseSetting(pair.Key, pair.Value);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(values));
            if (configure is not null) builder.ConfigureServices(configure);
        }
        protected override void ConfigureClient(HttpClient client) { base.ConfigureClient(client); client.BaseAddress = new("https://localhost"); }
    }
}

public sealed class ReviewerAuthenticationDatabaseTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [Fact]
    public async Task Issued_customer_tokens_map_to_seeded_records_and_cannot_read_other_customers()
    {
        await using (var db = database.OpenContext()) await DemoSeeder.Seed(db, new FrozenClock(), 14);
        await using var host = new ReviewerAuthenticationTests.ReviewerFactory(configure: services =>
        {
            // Keep the production configuration guard; use only the isolated test database for storage.
            services.RemoveAll<SchedulerDbContext>(); services.AddScoped(_ => database.OpenContext());
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(new FrozenClock());
        });
        using var client = host.CreateClient();
        var response = await client.PostAsync("/api/demo/auth", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var accounts = (await response.Content.ReadFromJsonAsync<ReviewerAccounts>())!;
        Assert.Equal(3, accounts.Accounts.Count);
        var customer = accounts.Accounts[1]; var other = accounts.Accounts[2];
        Assert.NotEqual(customer.CustomerId, other.CustomerId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer.AccessToken);
        var owned = await client.GetAsync($"/api/customers/{customer.CustomerId}/vehicles");
        Assert.Equal(HttpStatusCode.OK, owned.StatusCode);
        Assert.Contains((await owned.Content.ReadFromJsonAsync<VehicleDto[]>())!, vehicle => vehicle.Id == customer.VehicleId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/customers/{other.CustomerId}/vehicles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/customers")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accounts.Accounts[0].AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/customers")).StatusCode);
        // Concurrent requests reuse mappings, never duplicate or remap them.
        var repeats = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => client.PostAsync("/api/demo/auth", null)));
        Assert.All(repeats, result => Assert.Equal(HttpStatusCode.OK, result.StatusCode));
        await using var verify = database.OpenContext();
        foreach (var account in accounts.Accounts.Skip(1))
            Assert.Equal(account.CustomerId, (await verify.CustomerIdentityLinks.SingleAsync(x =>
                x.Issuer == ReviewerTokenIssuer.Issuer && x.Subject == ReviewerIdentitySeeder.Subject(account.CustomerId!.Value))).CustomerId);
    }

    private sealed class FrozenClock : TimeProvider { public override DateTimeOffset GetUtcNow() => TestDatabase.Now; }
}
