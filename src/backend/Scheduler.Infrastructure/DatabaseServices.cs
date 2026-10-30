using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Scheduler.Application;

namespace Scheduler.Infrastructure;

public static class DatabaseConfiguration
{
    public static IServiceCollection AddSchedulerPersistence(this IServiceCollection services)
    {
        services.AddDbContextFactory<SchedulerDbContext>((provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            options.UseNpgsql(ConnectionString(configuration)).EnableSensitiveDataLogging(false).EnableDetailedErrors(false);
        }, ServiceLifetime.Scoped);
        services.AddSingleton<IDependencyFailureClassifier, DependencyFailureClassifier>();
        services.AddSingleton<ISchedulerReadiness, SchedulerReadiness>();
        return services;
    }
    public static string ConnectionString(IConfiguration configuration)
    {
        var value = configuration.GetConnectionString("SchedulerDb");
        if (string.IsNullOrWhiteSpace(value)) throw new DependencyUnavailableException();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(value) { IncludeErrorDetail = false, LogParameters = false };
            if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database))
                throw new DependencyUnavailableException();
            return connection.ConnectionString;
        }
        catch (ArgumentException) { throw new DependencyUnavailableException(); }
    }
    public static bool IsDemoDatabase(IConfiguration configuration)
    {
        try { return new NpgsqlConnectionStringBuilder(ConnectionString(configuration)).Database == "keyloop_scheduler_demo"; }
        catch (DependencyUnavailableException) { return false; }
    }
}
public sealed class DependencyFailureClassifier : IDependencyFailureClassifier
{
    public bool IsUnavailable(Exception exception)
    {
        if (exception is DependencyUnavailableException or TimeoutException) return true;
        if (exception is DbUpdateException { InnerException: { } inner }) return IsUnavailable(inner);
        // The provider's non-retrying execution strategy wraps transient failures in InvalidOperationException.
        if (exception is InvalidOperationException { InnerException: NpgsqlException providerFailure })
            return IsUnavailable(providerFailure);
        // Only connectivity/availability failures are dependencies; integrity and unexpected SQL failures are 500.
        if (exception is PostgresException postgres)
            return postgres.SqlState.StartsWith("08", StringComparison.Ordinal) || postgres.SqlState is "57P01" or "57P02" or "57P03" or "53300";
        return exception is NpgsqlException { IsTransient: true };
    }
}
public sealed class SchedulerReadiness(IServiceScopeFactory scopes) : ISchedulerReadiness
{
private const string RequiredSchemaSql = """
    SELECT CASE WHEN NOT EXISTS (
        SELECT 1
        FROM (VALUES
            ('Customers','Id'), ('Customers','Name'),
            ('CustomerIdentityLinks','Id'), ('CustomerIdentityLinks','Issuer'), ('CustomerIdentityLinks','Subject'), ('CustomerIdentityLinks','CustomerId'),
            ('BookingReceipts','ScopeKey'), ('BookingReceipts','Fingerprint'), ('BookingReceipts','AppointmentId'), ('BookingReceipts','ResponseJson'),
            ('Vehicles','Id'), ('Vehicles','CustomerId'), ('Vehicles','Registration'), ('Vehicles','Make'), ('Vehicles','Model'),
            ('Dealerships','Id'), ('Dealerships','Name'), ('Dealerships','TimeZoneId'),
            ('ServiceTypes','Id'), ('ServiceTypes','Name'), ('ServiceTypes','DurationMinutes'),
            ('ServiceBays','Id'), ('ServiceBays','DealershipId'), ('ServiceBays','Name'),
            ('Technicians','Id'), ('Technicians','DealershipId'), ('Technicians','Name'),
            ('TechnicianServiceTypes','TechnicianId'), ('TechnicianServiceTypes','ServiceTypeId'),
            ('ServiceBayAvailabilities','Id'), ('ServiceBayAvailabilities','ServiceBayId'), ('ServiceBayAvailabilities','StartsAtUtc'), ('ServiceBayAvailabilities','EndsAtUtc'),
            ('TechnicianAvailabilities','Id'), ('TechnicianAvailabilities','TechnicianId'), ('TechnicianAvailabilities','StartsAtUtc'), ('TechnicianAvailabilities','EndsAtUtc'),
            ('Appointments','Id'), ('Appointments','CustomerId'), ('Appointments','VehicleId'), ('Appointments','DealershipId'),
            ('Appointments','ServiceTypeId'), ('Appointments','TechnicianId'), ('Appointments','ServiceBayId'),
            ('Appointments','StartsAtUtc'), ('Appointments','EndsAtUtc'), ('Appointments','CreatedAtUtc'),
            ('DemoFixtures','Id'), ('DemoFixtures','SeededAtUtc'), ('DemoFixtures','Json')
        ) AS expected(table_name, column_name)
        LEFT JOIN information_schema.columns actual
          ON actual.table_schema = current_schema()
         AND actual.table_name = expected.table_name
         AND actual.column_name = expected.column_name
        WHERE actual.column_name IS NULL
    ) THEN 1 ELSE 0 END AS "Value"
    """;

    public async Task<bool> IsReady(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<SchedulerDbContext>>().CreateDbContextAsync(ct);
            if (!await db.Database.CanConnectAsync(ct) || (await db.Database.GetPendingMigrationsAsync(ct)).Any()) return false;
            var schemaPresent = await db.Database.SqlQueryRaw<int>(RequiredSchemaSql).SingleAsync(ct) == 1;
            var constraintsPresent = await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE conrelid = '\"Appointments\"'::regclass AND contype = 'x' AND conname IN ('appointment_technician_no_overlap','appointment_bay_no_overlap')").SingleAsync(ct) == 2;
            return schemaPresent && constraintsPresent;
        }
        catch (Exception) { return false; }
    }
}
