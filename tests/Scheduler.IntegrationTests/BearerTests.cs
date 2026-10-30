using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Scheduler.Application;
using Xunit;

namespace Scheduler.IntegrationTests;

public sealed class BearerTests
{
    private const string Unreachable = "Host=127.0.0.1;Port=1;Database=keyloop_scheduler_test_unreachable;Username=synthetic;Timeout=1";

    [Theory]
    [InlineData("customerId")]
    [InlineData("vehicleId")]
    [InlineData("dealershipId")]
    [InlineData("serviceTypeId")]
    [InlineData("startsAt")]
    public async Task API01_Valid_customer_invalid_shape_returns_field_errors_before_mapping_or_database(string field)
    {
        await using var host = new SchedulerFactory(Unreachable);
        using var client = host.CreateClient();
        var body = new Dictionary<string, object>
        {
            ["customerId"] = Guid.NewGuid(), ["vehicleId"] = Guid.NewGuid(),
            ["dealershipId"] = Guid.NewGuid(), ["serviceTypeId"] = Guid.NewGuid(),
            ["startsAt"] = TestDatabase.Now.AddDays(1).ToString("O")
        };
        body[field] = field == "startsAt" ? "2030-01-02T09:00:00" : Guid.Empty;
        using var request = SyntheticJwt.Request(HttpMethod.Post, "/api/appointments", host.Tokens.Issue(roles: ["Customer"]),
            System.Net.Http.Json.JsonContent.Create(body));
        var problem = await ProblemAssertions.Read(await client.SendAsync(request), HttpStatusCode.BadRequest, "invalid_request");
        ProblemAssertions.Field(problem, field);
    }

    [Fact]
    public async Task SEC01_Role_denial_precedes_malformed_body_validation()
    {
        await using var host = new SchedulerFactory(Unreachable);
        using var client = host.CreateClient();
        using var request = SyntheticJwt.Request(HttpMethod.Post, "/api/appointments", host.Tokens.Issue(roles: ["Unknown"]),
            new StringContent("{", Encoding.UTF8, "application/json"));
        await ProblemAssertions.Read(await client.SendAsync(request), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task D04_OpenAPI_exposes_bearer_security_and_booking_readback_contract()
    {
        await using var host = new SchedulerFactory(Unreachable);
        using var client = host.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var schemes = root.GetProperty("components").GetProperty("securitySchemes");
        var bearer = Assert.Single(schemes.EnumerateObject(), p =>
            p.Value.TryGetProperty("type", out var type) && type.GetString() == "http" &&
            p.Value.TryGetProperty("scheme", out var scheme) && string.Equals(scheme.GetString(), "bearer", StringComparison.OrdinalIgnoreCase));
        var post = root.GetProperty("paths").GetProperty("/api/appointments").GetProperty("post");
        Assert.True(post.TryGetProperty("requestBody", out _));
        var security = post.TryGetProperty("security", out var local) ? local : root.GetProperty("security");
        Assert.Contains(security.EnumerateArray(), requirement => requirement.TryGetProperty(bearer.Name, out _));
        Assert.True(root.GetProperty("paths").GetProperty("/api/appointments/{id}").TryGetProperty("get", out _));
    }

    [Theory]
    [InlineData(false, 500, "internal_error")]
    [InlineData(true, 409, "no_capacity")]
    public async Task API01_Unexpected_failure_and_capacity_rejection_are_sanitized_distinct_errors(bool conflict, int status, string code)
    {
        var fixture = new ApplicationBehaviorTests.Fixture(); var logs = new CapturedLogs();
        if (conflict) fixture.Reads.Pairs = [];
        else fixture.Factory.SaveFailure = _ => new InvalidOperationException("SYNTHETIC-PRIVATE-FAILURE-CANARY");
        await using var host = new SchedulerFactory(Unreachable, configure: services =>
        {
            services.RemoveAll<IBookingTransactionFactory>(); services.AddSingleton<IBookingTransactionFactory>(fixture.Factory);
            services.AddSingleton<ILoggerProvider>(logs);
        });
        using var client = host.CreateClient();
        using var request = SyntheticJwt.Request(HttpMethod.Post, "/api/appointments", host.Tokens.Issue(),
            System.Net.Http.Json.JsonContent.Create(fixture.Request));
        var response = await client.SendAsync(request);
        await ProblemAssertions.Read(response, (HttpStatusCode)status, code, "SYNTHETIC-PRIVATE-FAILURE-CANARY");
        logs.AssertExcludes("SYNTHETIC-PRIVATE-FAILURE-CANARY");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("key")]
    [InlineData("unsigned")]
    [InlineData("subjectMissing")]
    [InlineData("subjectEmpty")]
    [InlineData("subjectAmbiguous")]
    public async Task SEC01_Invalid_tokens_are_rejected_by_real_bearer_before_body_validation(string kind)
    {
        await using var host = new SchedulerFactory(Unreachable);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        using var otherKey = new SyntheticJwt();
        var token = kind switch
        {
            "missing" => null,
            "malformed" => "not.a.valid.jwt",
            "expired" => host.Tokens.Issue(expires: DateTime.UtcNow.AddMinutes(-5)),
            "future" => host.Tokens.Issue(notBefore: DateTime.UtcNow.AddMinutes(5)),
            "issuer" => host.Tokens.Issue(issuer: "https://untrusted.scheduler.test"),
            "audience" => host.Tokens.Issue(audience: "different-api"),
            "key" => otherKey.Issue(),
            "unsigned" => host.Tokens.Issue(unsigned: true),
            "subjectMissing" => host.Tokens.Issue(subject: null),
            "subjectEmpty" => host.Tokens.Issue(subject: " "),
            "subjectAmbiguous" => host.Tokens.Issue(additionalClaims: [new Claim("sub", "different-subject")]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        using var request = SyntheticJwt.Request(HttpMethod.Post, "/api/appointments", token,
            new StringContent("{", Encoding.UTF8, "application/json"));
        using var response = await client.SendAsync(request);
        await ProblemAssertions.Read(response, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Theory]
    [InlineData("Customer", "/api/customers")]
    [InlineData("Customer", "/api/demo/fixtures")]
    [InlineData("Unknown", "/api/dealerships")]
    [InlineData("", "/api/service-types")]
    public async Task SEC01_Authenticated_disallowed_roles_receive_403(string role, string path)
    {
        await using var host = new SchedulerFactory(Unreachable);
        using var client = host.CreateClient();
        using var request = SyntheticJwt.Request(HttpMethod.Get, path, host.Tokens.Issue(roles: role == "" ? [] : [role]));
        await ProblemAssertions.Read(await client.SendAsync(request), HttpStatusCode.Forbidden, "forbidden");
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Admin")]
    public async Task SEC01_Valid_role_can_read_shared_reference_data_without_customer_mapping(string role)
    {
        var fixture = new ApplicationBehaviorTests.Fixture();
        await using var host = new SchedulerFactory(Unreachable, configure: services =>
        {
            services.RemoveAll<ISchedulingReadStore>(); services.AddSingleton<ISchedulingReadStore>(fixture.Reads);
        });
        using var client = host.CreateClient();
        using var request = SyntheticJwt.Request(HttpMethod.Get, "/api/dealerships", host.Tokens.Issue(roles: [role]));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("identity", fixture.Reads.Calls);
    }

    [Theory]
    [InlineData("Jwt:Authority", "http://insecure.scheduler.test")]
    [InlineData("Jwt:Issuer", "")]
    [InlineData("Jwt:Audience", "")]
    [InlineData("Jwt:SubjectClaimType", "")]
    [InlineData("Jwt:RoleClaimType", "")]
    public async Task SEC01_Invalid_identity_configuration_fails_closed(string setting, string value)
    {
        await using var host = new SchedulerFactory(Unreachable, settings: new() { [setting] = value });
        using var client = host.CreateClient();
        await ProblemAssertions.Read(await client.GetAsync("/api/dealerships"), HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task SEC01_Configured_subject_and_role_claim_names_are_honored()
    {
        var fixture = new ApplicationBehaviorTests.Fixture();
        await using var host = new SchedulerFactory(Unreachable, configure: services =>
        {
            services.RemoveAll<ISchedulingReadStore>(); services.AddSingleton<ISchedulingReadStore>(fixture.Reads);
        }, settings: new() { ["Jwt:SubjectClaimType"] = "subject_id", ["Jwt:RoleClaimType"] = "access_role" });
        using var client = host.CreateClient();
        using var request = SyntheticJwt.Request(HttpMethod.Get, "/api/dealerships",
            host.Tokens.Issue(subjectClaim: "subject_id", roleClaim: "access_role", roles: ["Customer"]));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task API01_Validation_and_logs_are_correlated_and_do_not_include_input_or_token()
    {
        var logs = new CapturedLogs();
        await using var host = new SchedulerFactory(Unreachable, configure: services => services.AddSingleton<ILoggerProvider>(logs));
        using var client = host.CreateClient();
        const string canary = "SYNTHETIC-PRIVATE-INPUT-CANARY";
        var token = host.Tokens.Issue();
        using var request = SyntheticJwt.Request(HttpMethod.Post, "/api/appointments", token,
            new StringContent("{\"customerId\":\"" + canary + "\"}", Encoding.UTF8, "application/json"));
        var response = await client.SendAsync(request);
        var problem = await ProblemAssertions.Read(response, HttpStatusCode.BadRequest, "invalid_request", canary, token);
        logs.AssertExcludes(canary, token);
        Assert.Contains(logs.Entries, entry => entry.Properties.Any(p => p.Key == "TraceId" && Equals(p.Value?.ToString(), problem.GetProperty("traceId").GetString())));
        Assert.Contains(logs.Entries, entry => entry.Properties.Any(p => p.Key == "ElapsedMs"));
    }

    [Fact]
    public async Task SEC01_Anonymous_liveness_remains_minimal()
    {
        await using var host = new SchedulerFactory(Unreachable);
        using var client = host.CreateClient(); client.DefaultRequestHeaders.Authorization = null;
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"live\"}", await response.Content.ReadAsStringAsync());
    }
}
