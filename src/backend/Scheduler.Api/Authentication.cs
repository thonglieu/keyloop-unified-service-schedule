using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scheduler.Application;

namespace Scheduler.Api;

public sealed class JwtSettings
{
    public string Authority { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SubjectClaimType { get; set; } = "sub";
    public string RoleClaimType { get; set; } = "role";
}
public sealed class ConfigureBearer(IOptions<JwtSettings> settings, IConfiguration configuration, ReviewerTokenIssuer reviewerTokens) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(JwtBearerOptions options) => Configure(JwtBearerDefaults.AuthenticationScheme, options);
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme) return;
        var jwt = settings.Value;
        var valid = Uri.TryCreate(jwt.Authority, UriKind.Absolute, out var authority) && authority.Scheme == "https" &&
            Uri.TryCreate(jwt.Issuer, UriKind.Absolute, out var issuer) && issuer.Scheme == "https" &&
            !string.IsNullOrWhiteSpace(jwt.Audience) && !string.IsNullOrWhiteSpace(jwt.SubjectClaimType) && !string.IsNullOrWhiteSpace(jwt.RoleClaimType);
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = true;
        options.IncludeErrorDetails = false;
        var reviewer = configuration.GetValue<bool>("ReviewerAuth:Enabled");
        if (valid && !reviewer) options.Authority = jwt.Authority;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = jwt.Issuer,
            ValidateAudience = true, ValidAudience = jwt.Audience,
            ValidateLifetime = true, RequireExpirationTime = true,
            RequireSignedTokens = true, ValidateIssuerSigningKey = true,
            // Invalid configuration is denied by OnMessageReceived; safe construction must not turn it into 500.
            NameClaimType = string.IsNullOrWhiteSpace(jwt.SubjectClaimType) ? "sub" : jwt.SubjectClaimType,
            RoleClaimType = string.IsNullOrWhiteSpace(jwt.RoleClaimType) ? "role" : jwt.RoleClaimType,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        if (reviewer) options.TokenValidationParameters.IssuerSigningKey = reviewerTokens.PublicKey;
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (!valid) context.Fail("Identity configuration unavailable.");
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var subjects = context.Principal!.FindAll(jwt.SubjectClaimType).Select(x => x.Value).ToArray();
                var issuers = context.Principal.FindAll("iss").Select(x => x.Value).ToArray();
                if (subjects.Length != 1 || string.IsNullOrWhiteSpace(subjects[0]) || issuers.Length != 1 || string.IsNullOrWhiteSpace(issuers[0])) context.Fail("Invalid identity.");
                return Task.CompletedTask;
            }
        };
    }
}
public sealed class CurrentUser(IHttpContextAccessor accessor, IOptions<JwtSettings> settings) : ICurrentUser
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;
    public bool IsAdmin => User?.IsInRole("Admin") == true;
    public bool IsCustomer => User?.IsInRole("Customer") == true;
    public string Issuer => User?.FindFirst("iss")?.Value ?? "";
    public string Subject => User?.FindFirst(settings.Value.SubjectClaimType)?.Value ?? "";
}
