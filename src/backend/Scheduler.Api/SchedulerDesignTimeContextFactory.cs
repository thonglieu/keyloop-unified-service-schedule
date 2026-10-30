using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Scheduler.Infrastructure;

namespace Scheduler.Api;

// Private configuration precedence: user-secrets < environment < command line.
// Without a configured connection, model-only scaffolding still works offline.
public sealed class SchedulerDesignTimeContextFactory : IDesignTimeDbContextFactory<SchedulerDbContext>
{
    public SchedulerDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<SchedulerDesignTimeContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
        var options = new DbContextOptionsBuilder<SchedulerDbContext>();
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("SchedulerDb")))
            options.UseNpgsql();
        else
            options.UseNpgsql(DatabaseConfiguration.ConnectionString(configuration));
        options.EnableSensitiveDataLogging(false).EnableDetailedErrors(false);
        return new(options.Options);
    }
}
