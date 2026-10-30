using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Scheduler.Infrastructure;

namespace Scheduler.Api;

// Local reviewer tooling only. Business endpoints still use the normal JWT handler and policies.
public static class ReviewerAuthentication
{
    public static void ValidateConfiguration(IConfiguration configuration, IWebHostEnvironment environment)
    {
        if (configuration.GetValue<bool>("ReviewerAuth:Enabled") &&
            (!environment.IsDevelopment() || !configuration.GetValue<bool>("Demo:Enabled") ||
             !DatabaseConfiguration.IsDemoDatabase(configuration)))
            throw new InvalidOperationException("Reviewer authentication requires Development, Demo.Enabled and the allowlisted demo database.");
    }

    public static void MapReviewerAuthentication(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("ReviewerAuth:Enabled")) return;
        app.MapPost("/api/demo/auth", async (ReviewerIdentitySeeder identities, ReviewerTokenIssuer tokens,
            HttpContext context, CancellationToken ct) =>
        {
            var fixture = await identities.Prepare(ReviewerTokenIssuer.Issuer, ct);
            var expires = DateTimeOffset.UtcNow.AddHours(1);
            var accounts = new List<ReviewerAccount>
            {
                new("Admin", "Admin", null, null, tokens.Issue("reviewer-admin", "Admin", expires))
            };
            accounts.AddRange(fixture.Customers.Take(2).Select((customer, index) =>
                new ReviewerAccount($"Customer {index + 1}", "Customer", customer.CustomerId, customer.VehicleId,
                    tokens.Issue(ReviewerIdentitySeeder.Subject(customer.CustomerId), "Customer", expires))));
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            return Results.Ok(new ReviewerAccounts(fixture.Namespace, expires, accounts));
        }).AllowAnonymous().WithName("ReviewerAuthentication")
          .WithSummary("Get local reviewer Admin and Customer tokens")
          .WithDescription("Development reviewer mode only. No credentials or request body required. Tokens expire after one hour and are invalidated when the API restarts.");
    }
}

public sealed record ReviewerAccount(string Name, string Role, Guid? CustomerId, Guid? VehicleId, string AccessToken);
public sealed record ReviewerAccounts(string FixtureNamespace, DateTimeOffset ExpiresAt, IReadOnlyList<ReviewerAccount> Accounts);

public sealed class ReviewerAuthenticationOperationFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
{
    public void Apply(Microsoft.OpenApi.Models.OpenApiOperation operation, Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
    {
        if (context.ApiDescription.RelativePath == "api/demo/auth") operation.Security = [];
    }
}

public sealed class ReviewerTokenIssuer : IDisposable
{
    public const string Issuer = "https://reviewer.scheduler.local";
    public const string Audience = "scheduler-local-reviewer";
    private readonly Lazy<RSA> rsa = new(() => RSA.Create(2048));
    private readonly string keyId = Guid.NewGuid().ToString("N");
    public SecurityKey PublicKey => new RsaSecurityKey(rsa.Value.ExportParameters(false)) { KeyId = keyId };

    public string Issue(string subject, string role, DateTimeOffset expires)
    {
        var token = new JwtSecurityToken(Issuer, Audience,
            [new Claim("sub", subject), new Claim("role", role)],
            DateTime.UtcNow.AddHours(-1), expires.UtcDateTime,
            new SigningCredentials(new RsaSecurityKey(rsa.Value) { KeyId = keyId }, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose() { if (rsa.IsValueCreated) rsa.Value.Dispose(); }
}
