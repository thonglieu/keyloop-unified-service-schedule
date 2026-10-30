using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Scheduler.Application;

namespace Scheduler.Api;

public static class ApiErrors
{
    public static string Code(int status) => status switch { 400 => "invalid_request", 401 => "unauthenticated", 403 => "forbidden", 404 => "not_found", 409 => "no_capacity", 503 => "dependency_unavailable", _ => "internal_error" };
    public static string Title(int status) => status switch { 400 => "Invalid request.", 401 => "Authentication required.", 403 => "Access denied.", 404 => "Resource not found.", 409 => "No capacity available.", 503 => "Dependency unavailable.", _ => "An unexpected error occurred." };
    public static Dictionary<string, object?> Extensions(HttpContext context, int status) => new() { ["code"] = Code(status), ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier };
    public static Task Write(HttpContext context, int status, Dictionary<string, string[]>? errors = null, string? code = null)
    {
        var extensions = Extensions(context, status);
        if (code is not null) extensions["code"] = code;
        if (errors is not null) extensions["errors"] = errors;
        return Results.Problem(statusCode: status, title: code == "idempotency_conflict" ? "Idempotency key conflict." : Title(status), extensions: extensions).ExecuteAsync(context);
    }
    public static int Status(Exception exception, IDependencyFailureClassifier dependencies)
    {
        if (exception is BookingException booking) return booking.Status;
        if (dependencies.IsUnavailable(exception)) return 503;
        return 500;
    }
    public static IActionResult Validation(ActionContext context)
    {
        var fields = context.ModelState.Where(x => x.Value?.Errors.Count > 0).Select(x => x.Key).ToArray();
        var errors = fields.Select(x => string.IsNullOrEmpty(x) ? "request" : JsonField(x)).Distinct()
            .ToDictionary(x => x, _ => new[] { "Invalid value." });
        var problem = new ValidationProblemDetails(errors) { Status = 400, Title = Title(400) };
        foreach (var pair in Extensions(context.HttpContext, 400)) problem.Extensions[pair.Key] = pair.Value;
        return new BadRequestObjectResult(problem);
    }
    private static string JsonField(string field)
    {
        var name = field.StartsWith("$.", StringComparison.Ordinal) ? field[2..] : field;
        return name.StartsWith('$') ? "request" : char.ToLowerInvariant(name[0]) + name[1..];
    }
}
