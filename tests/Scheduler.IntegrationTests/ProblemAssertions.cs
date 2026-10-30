using System.Net;
using System.Text.Json;
using Xunit;

namespace Scheduler.IntegrationTests;

internal static class ProblemAssertions
{
    public static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode status,
        string code, params string[] forbiddenText)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var text in forbiddenText) Assert.DoesNotContain(text, body);
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        if (status == HttpStatusCode.Unauthorized)
            Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        return problem.Clone();
    }

    public static void Field(JsonElement problem, string field)
    {
        var messages = problem.GetProperty("errors").GetProperty(field);
        Assert.Equal(JsonValueKind.Array, messages.ValueKind);
        Assert.NotEmpty(messages.EnumerateArray());
        Assert.All(messages.EnumerateArray(), message => Assert.False(string.IsNullOrWhiteSpace(message.GetString())));
    }

    public static void SameConcealedResource(JsonElement foreign, JsonElement absent)
    {
        foreach (var name in new[] { "status", "code", "title", "detail" })
        {
            var hasForeign = foreign.TryGetProperty(name, out var foreignValue);
            var hasAbsent = absent.TryGetProperty(name, out var absentValue);
            Assert.Equal(hasForeign, hasAbsent);
            if (hasForeign) Assert.Equal(foreignValue.GetRawText(), absentValue.GetRawText());
        }
    }
}
