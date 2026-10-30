using Microsoft.EntityFrameworkCore;
using Scheduler.Infrastructure;
using Xunit;

namespace Scheduler.IntegrationTests;

public sealed class ReviewerSetupTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [Fact]
    public async Task D02_Reviewer_seed_reuses_current_fixtures_and_refreshes_expired_fixtures_without_deletion()
    {
        await using var db = database.OpenContext();
        var latestSeed = await db.DemoFixtures.Select(x => (DateTimeOffset?)x.SeededAtUtc).MaxAsync();
        var now = (latestSeed > TestDatabase.Now ? latestSeed.Value : TestDatabase.Now).AddDays(2);
        var clock = new SetupClock(now);
        var fixtureCount = await db.DemoFixtures.CountAsync();
        var appointmentCount = await db.Appointments.CountAsync();

        var first = await DemoSeeder.SeedIfNeeded(db, clock, 14);
        Assert.Equal(now, first.SeededAt);
        Assert.Equal(fixtureCount + 1, await db.DemoFixtures.CountAsync());
        Assert.Equal(appointmentCount + 2, await db.Appointments.CountAsync());
        var firstDealerIds = first.Scenarios.Values.Select(x => x.DealershipId).ToArray();
        var persistedAppointmentIds = await db.Appointments.Where(x => firstDealerIds.Contains(x.DealershipId))
            .Select(x => x.Id).ToArrayAsync();
        Assert.Equal(2, persistedAppointmentIds.Length);

        // A second startup on a fresh context must leave fixture IDs and stored bookings unchanged.
        await using var secondContext = database.OpenContext();
        var repeated = await DemoSeeder.SeedIfNeeded(secondContext, clock, 14);
        Assert.Equal(first.Namespace, repeated.Namespace);
        Assert.Equal(first.Customers, repeated.Customers);
        Assert.Equal(fixtureCount + 1, await secondContext.DemoFixtures.CountAsync());
        Assert.Equal(appointmentCount + 2, await secondContext.Appointments.CountAsync());

        // At the expiry boundary the next startup adds a usable fixture, preserving old data.
        clock.Now = first.ExpiresAt;
        var refreshed = await DemoSeeder.SeedIfNeeded(secondContext, clock, 14);
        Assert.NotEqual(first.Namespace, refreshed.Namespace);
        Assert.True(refreshed.ExpiresAt > clock.Now);
        Assert.All(refreshed.Scenarios.Values, scenario => Assert.True(scenario.StartsAt > clock.Now));
        Assert.Equal(fixtureCount + 2, await secondContext.DemoFixtures.CountAsync());
        Assert.Equal(appointmentCount + 4, await secondContext.Appointments.CountAsync());
        Assert.True(await secondContext.DemoFixtures.AnyAsync(x => x.Id == Guid.Parse(first.Namespace)));
        Assert.Equal(2, await secondContext.Appointments.CountAsync(x => persistedAppointmentIds.Contains(x.Id)));
    }

    private sealed class SetupClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
