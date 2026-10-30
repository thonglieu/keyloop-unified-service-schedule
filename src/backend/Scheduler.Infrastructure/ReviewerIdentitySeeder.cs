using Microsoft.EntityFrameworkCore;
using Scheduler.Application;

namespace Scheduler.Infrastructure;

// Maps only customers from an existing synthetic fixture; never accepts a caller-selected identity.
public sealed class ReviewerIdentitySeeder(SchedulerDbContext db, ISchedulingReadStore reads, TimeProvider clock)
{
    public static string Subject(Guid customerId) => $"reviewer-customer-{customerId:N}";

    public async Task<FixtureDto> Prepare(string issuer, CancellationToken ct)
    {
        var fixture = await reads.Fixtures(ct);
        if (fixture is null || fixture.ExpiresAt <= clock.GetUtcNow() || fixture.Customers.Length < 2)
            throw new BookingException(503, "Fresh demo fixtures are required.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var customer in fixture.Customers.Take(2))
        {
            var subject = Subject(customer.CustomerId);
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "CustomerIdentityLinks" ("Id", "Issuer", "Subject", "CustomerId")
                VALUES ({id}, {issuer}, {subject}, {customer.CustomerId})
                ON CONFLICT ("Issuer", "Subject") DO NOTHING
                """, ct);
            var mapped = await db.CustomerIdentityLinks.AsNoTracking()
                .SingleAsync(x => x.Issuer == issuer && x.Subject == subject, ct);
            if (mapped.CustomerId != customer.CustomerId)
                throw new InvalidOperationException("Reviewer identity mapping is inconsistent.");
        }
        await transaction.CommitAsync(ct);
        return fixture;
    }
}
