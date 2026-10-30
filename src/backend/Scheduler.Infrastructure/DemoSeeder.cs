using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scheduler.Application;
using Scheduler.Domain;

namespace Scheduler.Infrastructure;

public static class DemoSeeder
{
    public static async Task<FixtureDto> SeedIfNeeded(SchedulerDbContext db, TimeProvider clock, int days, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var latest = await db.DemoFixtures.AsNoTracking()
            .OrderByDescending(x => x.SeededAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => x.Json)
            .FirstOrDefaultAsync(ct);
        if (latest is not null)
        {
            var fixture = JsonSerializer.Deserialize<FixtureDto>(latest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (fixture is not null && fixture.ExpiresAt > now) return fixture;
        }
        return await Seed(db, clock, days, ct);
    }

    public static async Task<FixtureDto> Seed(SchedulerDbContext db, TimeProvider clock, int days, CancellationToken ct = default)
    {
        if (days < 2 || days > 366) throw new ArgumentOutOfRangeException(nameof(days), "SeedDays must be between 2 and 366.");
        var now = clock.GetUtcNow();
        var id = Guid.NewGuid();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        var localTomorrow = TimeZoneInfo.ConvertTime(now, zone).Date.AddDays(1);
        DateTimeOffset Utc(DateTime local) => new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone));
        var start = Utc(localTomorrow.AddHours(9));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var customers = Enumerable.Range(0, 20).Select(i => new FixtureCustomer(Guid.NewGuid(), Guid.NewGuid())).ToArray();
        foreach (var (c, i) in customers.Select((c, i) => (c, i)))
        {
            db.Customers.Add(new() { Id = c.CustomerId, Name = $"Demo {id:N} customer {i + 1}" });
            db.Vehicles.Add(new() { Id = c.VehicleId, CustomerId = c.CustomerId, Registration = $"SYN-{id:N}-{i + 1}", Make = "Demo", Model = "Simulator" });
        }
        var shortService = new ServiceType { Id = Guid.NewGuid(), Name = $"30-minute service {id:N}", DurationMinutes = 30 };
        var longService = new ServiceType { Id = Guid.NewGuid(), Name = $"60-minute service {id:N}", DurationMinutes = 60 };
        db.ServiceTypes.AddRange(shortService, longService);
        var scenarios = new Dictionary<string, FixtureScenario>();
        foreach (var key in new[] { "valid", "noQualifiedTechnician", "occupiedBay", "occupiedTechnician", "shortWindow", "adjacent", "singlePairRace", "twoPairRace" })
        {
            var dealer = new Dealership { Id = Guid.NewGuid(), Name = $"{key} {id:N}" };
            db.Dealerships.Add(dealer);
            var count = key is "valid" or "twoPairRace" ? 2 : 1;
            // Occupancy fixtures have an extra opposite resource to prove each dimension independently.
            var technicians = Enumerable.Range(0, key == "occupiedBay" ? 2 : count).Select(_ => new Technician { Id = Guid.NewGuid(), DealershipId = dealer.Id, Name = "Demo technician" }).ToArray();
            var bays = Enumerable.Range(0, key == "occupiedTechnician" ? 2 : count).Select(_ => new ServiceBay { Id = Guid.NewGuid(), DealershipId = dealer.Id, Name = "Demo bay" }).ToArray();
            db.Technicians.AddRange(technicians);
            db.ServiceBays.AddRange(bays);
            foreach (var technician in technicians)
            {
                if (key != "noQualifiedTechnician")
                    foreach (var service in new[] { shortService, longService }) db.TechnicianServiceTypes.Add(new() { TechnicianId = technician.Id, ServiceTypeId = service.Id });
                for (var day = 0; day < days; day++) db.TechnicianAvailabilities.Add(new() { Id = Guid.NewGuid(), TechnicianId = technician.Id, StartsAtUtc = Utc(localTomorrow.AddDays(day).AddHours(8)), EndsAtUtc = key == "shortWindow" && day == 0 ? start.AddMinutes(15) : Utc(localTomorrow.AddDays(day).AddHours(17)) });
            }
            foreach (var bay in bays)
                for (var day = 0; day < days; day++) db.ServiceBayAvailabilities.Add(new() { Id = Guid.NewGuid(), ServiceBayId = bay.Id, StartsAtUtc = Utc(localTomorrow.AddDays(day).AddHours(8)), EndsAtUtc = Utc(localTomorrow.AddDays(day).AddHours(17)) });
            var selectedService = key is "occupiedBay" or "occupiedTechnician" or "shortWindow" ? longService : shortService;
            scenarios.Add(key, new(dealer.Id, selectedService.Id, start, selectedService.DurationMinutes, technicians.Select(t => t.Id).ToArray(), bays.Select(b => b.Id).ToArray(), key == "adjacent" ? start.AddMinutes(30) : null));
            if (key is "occupiedBay" or "occupiedTechnician") db.Appointments.Add(Appointment.Confirm(customers[0].CustomerId, customers[0].VehicleId, dealer.Id, shortService.Id, technicians[0].Id, bays[0].Id, new UtcInterval(start.AddMinutes(30), start.AddMinutes(60)), now));
        }
        var fixture = new FixtureDto(id.ToString(), now, start, customers, scenarios);
        db.DemoFixtures.Add(new() { Id = id, SeededAtUtc = now, Json = JsonSerializer.Serialize(fixture, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return fixture;
    }
}
