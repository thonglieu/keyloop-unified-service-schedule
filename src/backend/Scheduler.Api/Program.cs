using Scheduler.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

using Scheduler.Application;
using Scheduler.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://localhost:5080");
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
builder.Logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.None);
builder.Services.AddSchedulerPersistence();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ISchedulingReadStore, SchedulingReadStore>();
builder.Services.AddScoped<IBookingTransactionFactory, BookingTransactionFactory>();
builder.Services.AddScoped<OwnershipPolicy>();
builder.Services.AddScoped<BookAppointmentHandler>();
builder.Services.AddScoped<CheckAvailabilityHandler>();
builder.Services.AddScoped<AppointmentQueries>();
builder.Services.AddScoped<ReferenceQueries>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddOptions<JwtSettings>().BindConfiguration("Jwt");
builder.Services.PostConfigure<JwtSettings>(settings =>
{
    if (builder.Configuration.GetValue<bool>("ReviewerAuth:Enabled"))
    {
        settings.Authority = ReviewerTokenIssuer.Issuer;
        settings.Issuer = ReviewerTokenIssuer.Issuer;
        settings.Audience = ReviewerTokenIssuer.Audience;
        settings.SubjectClaimType = "sub";
        settings.RoleClaimType = "role";
    }
});
builder.Services.AddSingleton<ReviewerTokenIssuer>();
builder.Services.AddScoped<ReviewerIdentitySeeder>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureBearer>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Customer", policy => policy.RequireAuthenticatedUser().RequireRole("Customer", "Admin"));
    options.AddPolicy("Admin", policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));
});
builder.Services.AddOptions<EventDispatchOptions>().BindConfiguration("Events");
builder.Services.AddScoped<IAppointmentEventDispatcher, AppointmentEventDispatcher>();
builder.Services.AddScoped<IAppointmentConfirmedHandler, BookingDiagnosticHandler>();
builder.Services.AddControllers().ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiErrors.Validation).AddJsonOptions(o => o.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context => context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.OperationFilter<ReviewerAuthenticationOperationFilter>();
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer", BearerFormat = "JWT",
        Description = "Access token issued by the configured external identity provider."
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        [new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Reference = new Microsoft.OpenApi.Models.OpenApiReference
            { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
        }] = Array.Empty<string>()
    });
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"]).AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
ReviewerAuthentication.ValidateConfiguration(app.Configuration, app.Environment);
if (args.Contains("--seed-demo") || args.Contains("--seed-demo-if-needed") || args.Contains("--migrate"))
{
    if (!app.Environment.IsDevelopment() || !app.Configuration.GetValue<bool>("Demo:Enabled") || !DatabaseConfiguration.IsDemoDatabase(app.Configuration))
        throw new InvalidOperationException("Local setup requires Development, Demo.Enabled and the allowlisted demo database.");
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
    if (args.Contains("--migrate")) await db.Database.MigrateAsync();
    else
    {
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var days = app.Configuration.GetValue("Demo:SeedDays", 14);
        var fixture = args.Contains("--seed-demo-if-needed")
            ? await DemoSeeder.SeedIfNeeded(db, clock, days)
            : await DemoSeeder.Seed(db, clock, days);
        Console.WriteLine($"Seeded fixture namespace {fixture.Namespace}; expires {fixture.ExpiresAt:O}");
    }
    return;
}
app.Use(async (context, next) =>
{
    var watch = Stopwatch.StartNew();
    try { await next(context); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Response.StatusCode = 499; }
    catch (Exception ex)
    {
        if (context.Response.HasStarted) throw;
        var status = ApiErrors.Status(ex, context.RequestServices.GetRequiredService<IDependencyFailureClassifier>());
        if (status >= 500) app.Logger.LogWarning("Request failed; code {Code}; trace {TraceId}", ApiErrors.Code(status), context.TraceIdentifier);
        await ApiErrors.Write(context, status, (ex as BookingException)?.Errors, (ex as BookingException)?.Code);
    }
    finally
    {
        app.Logger.LogInformation("HTTP {Method}; route {Route}; status {Status}; elapsed {ElapsedMs}; trace {TraceId}",
            context.Request.Method, (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched",
            context.Response.StatusCode, watch.Elapsed.TotalMilliseconds, Activity.Current?.Id ?? context.TraceIdentifier);
    }
});
app.UseStatusCodePages(async context =>
{
    if (!context.HttpContext.Request.Path.StartsWithSegments("/health"))
        await ApiErrors.Write(context.HttpContext, context.HttpContext.Response.StatusCode);
});
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (ISchedulerReadiness readiness, CancellationToken ct) =>
    await readiness.IsReady(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapControllers();
app.MapReviewerAuthentication();
app.Run();
public partial class Program { }
