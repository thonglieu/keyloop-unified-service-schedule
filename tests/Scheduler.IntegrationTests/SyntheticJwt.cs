using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Scheduler.IntegrationTests;

// One ephemeral RSA key per host fixture. Never exports or persists private key material.
internal sealed class SyntheticJwt : IDisposable
{
    public const string Issuer = "https://identity.scheduler.test";
    public const string Audience = "scheduler-tests";
    private readonly RSA rsa = RSA.Create(2048);
    private readonly string keyId = Guid.NewGuid().ToString("N");

    public SecurityKey PublicKey => new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = keyId };

    public string Issue(string? subject = "synthetic-admin", string[]? roles = null,
        string issuer = Issuer, string audience = Audience, DateTime? expires = null,
        DateTime? notBefore = null, bool unsigned = false, string subjectClaim = "sub",
        string roleClaim = "role", IEnumerable<Claim>? additionalClaims = null)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>();
        if (subject is not null) claims.Add(new Claim(subjectClaim, subject));
        claims.AddRange((roles ?? ["Admin"]).Select(role => new Claim(roleClaim, role)));
        if (additionalClaims is not null) claims.AddRange(additionalClaims);
        var credentials = unsigned ? null : new SigningCredentials(
            new RsaSecurityKey(rsa) { KeyId = keyId }, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(issuer, audience, claims,
            notBefore ?? now.AddHours(-1), expires ?? now.AddMinutes(10), credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static HttpRequestMessage Request(HttpMethod method, string path, string? token,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public void Dispose() => rsa.Dispose();
}
