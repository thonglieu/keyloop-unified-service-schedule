using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using Scheduler.Application;
using Scheduler.Domain;

namespace Scheduler.Infrastructure;

public sealed class SchedulingReadStore(SchedulerDbContext db) : ISchedulingReadStore
{
    public Task<Guid?> ResolveCustomer(string issuer, string subject, CancellationToken ct) => db.CustomerIdentityLinks.AsNoTracking().Where(x => x.Issuer == issuer && x.Subject == subject).Select(x => (Guid?)x.CustomerId).SingleOrDefaultAsync(ct);
    public Task<DealershipDto?> Dealership(Guid id, CancellationToken ct) => db.Dealerships.AsNoTracking().Where(x => x.Id == id).Select(x => new DealershipDto(x.Id, x.Name, x.TimeZoneId)).SingleOrDefaultAsync(ct);
    public Task<ServiceTypeDto?> ServiceType(Guid id, CancellationToken ct) => db.ServiceTypes.AsNoTracking().Where(x => x.Id == id).Select(x => new ServiceTypeDto(x.Id, x.Name, x.DurationMinutes)).SingleOrDefaultAsync(ct);
    public Task<bool> CustomerExists(Guid id, CancellationToken ct) => db.Customers.AnyAsync(x => x.Id == id, ct);
    public Task<VehicleDto?> Vehicle(Guid id, Guid? customerId, CancellationToken ct) => db.Vehicles.AsNoTracking().Where(x => x.Id == id && (!customerId.HasValue || x.CustomerId == customerId)).Select(x => new VehicleDto(x.Id, x.CustomerId, x.Registration, x.Make, x.Model)).SingleOrDefaultAsync(ct);
    public async Task<IEnumerable<ResourcePair>> Eligible(Guid dealershipId, Guid serviceTypeId, UtcInterval interval, CancellationToken ct)
    {
        var start = interval.Start; var end = interval.End;
        var technicians = await db.Technicians.Where(t => t.DealershipId == dealershipId &&
            db.TechnicianServiceTypes.Any(q => q.TechnicianId == t.Id && q.ServiceTypeId == serviceTypeId) &&
            db.TechnicianAvailabilities.Any(w => w.TechnicianId == t.Id && w.StartsAtUtc <= start && w.EndsAtUtc >= end) &&
            !db.Appointments.Any(a => a.TechnicianId == t.Id && a.StartsAtUtc < end && a.EndsAtUtc > start))
            .OrderBy(t => t.Id).Select(t => t.Id).ToArrayAsync(ct);
        var bays = await db.ServiceBays.Where(b => b.DealershipId == dealershipId &&
            db.ServiceBayAvailabilities.Any(w => w.ServiceBayId == b.Id && w.StartsAtUtc <= start && w.EndsAtUtc >= end) &&
            !db.Appointments.Any(a => a.ServiceBayId == b.Id && a.StartsAtUtc < end && a.EndsAtUtc > start))
            .OrderBy(b => b.Id).Select(b => b.Id).ToArrayAsync(ct);
        return from t in technicians from b in bays select new ResourcePair(t, b);
    }
    public Task<DealershipDto[]> Dealerships(CancellationToken ct) => db.Dealerships.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new DealershipDto(x.Id, x.Name, x.TimeZoneId)).ToArrayAsync(ct);
    public Task<ServiceTypeDto[]> ServiceTypes(CancellationToken ct) => db.ServiceTypes.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new ServiceTypeDto(x.Id, x.Name, x.DurationMinutes)).ToArrayAsync(ct);
    public Task<CustomerDto[]> Customers(CancellationToken ct) => db.Customers.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new CustomerDto(x.Id, x.Name)).ToArrayAsync(ct);
    public Task<VehicleDto[]> Vehicles(Guid id, CancellationToken ct) => db.Vehicles.AsNoTracking().Where(x => x.CustomerId == id).OrderBy(x => x.Id).Select(x => new VehicleDto(x.Id, x.CustomerId, x.Registration, x.Make, x.Model)).ToArrayAsync(ct);
    public async Task<AppointmentDto?> Appointment(Guid id, Guid? customerId, CancellationToken ct)
    {
        var row = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && (!customerId.HasValue || x.CustomerId == customerId), ct);
        return row is null ? null : AppointmentDto.From(row);
    }
    public async Task<AppointmentDto[]> Appointments(Guid id, CancellationToken ct) => (await db.Appointments.AsNoTracking().Where(x => x.CustomerId == id).OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id).ToArrayAsync(ct)).Select(AppointmentDto.From).ToArray();
    public async Task<FixtureDto?> Fixtures(CancellationToken ct)
    {
        var json = await db.DemoFixtures.AsNoTracking().OrderByDescending(x => x.SeededAtUtc).ThenByDescending(x => x.Id).Select(x => x.Json).FirstOrDefaultAsync(ct);
        return json is null ? null : JsonSerializer.Deserialize<FixtureDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
public sealed class BookingTransactionFactory : IBookingTransactionFactory
{
    private readonly IDbContextFactory<SchedulerDbContext> factory;
    public BookingTransactionFactory(IDbContextFactory<SchedulerDbContext> factory, ILogger<BookingTransactionFactory> logger)
    {
        this.factory = factory;
    }
    public async Task<IBookingTransaction> Begin(CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        try { return new BookingTransaction(db, await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)); }
        catch { await db.DisposeAsync(); throw; }
    }
    private sealed class BookingTransaction(SchedulerDbContext db, IDbContextTransaction tx) : IBookingTransaction
    {
        public ISchedulingReadStore Reads { get; } = new SchedulingReadStore(db);
        public async Task<RecordedBooking?> LockAndReadIdempotency(string scopeKey, CancellationToken ct)
        {
            // Lock even when no receipt exists. Under Read Committed, the following
            // query sees the preceding holder's commit before allocation can begin.
            var lockKey = "idempotency:" + scopeKey;
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
            var receipt = await db.BookingReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scopeKey, ct);
            return receipt is null ? null : new RecordedBooking(receipt.Fingerprint,
                JsonSerializer.Deserialize<AppointmentDto>(receipt.ResponseJson) ?? throw new InvalidOperationException("Invalid booking receipt."));
        }
        public async Task RecordIdempotency(BookingIdempotency key, AppointmentDto appointment, CancellationToken ct)
        {
            db.BookingReceipts.Add(new BookingReceipt
            {
                ScopeKey = key.ScopeKey, Fingerprint = key.Fingerprint,
                AppointmentId = appointment.Id, ResponseJson = JsonSerializer.Serialize(appointment)
            });
            await db.SaveChangesAsync(ct);
        }
        public async Task LockAllocation(ResourcePair pair, CancellationToken ct)
        {
            // Use transaction-scoped PostgreSQL advisory locks. A stable global ordering
            // prevents lock-order cycles when two candidate pairs share one resource.
            var keys = new[] { $"bay:{pair.ServiceBayId:N}", $"technician:{pair.TechnicianId:N}" };
            Array.Sort(keys, StringComparer.Ordinal);
            foreach (var key in keys)
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }
        public async Task Save(Appointment appointment, CancellationToken ct)
        {
            db.Appointments.Add(appointment);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23P01", ConstraintName: "appointment_technician_no_overlap" or "appointment_bay_no_overlap" }) { throw new CapacityRaceException(); }
        }
        public Task Commit(CancellationToken ct) => tx.CommitAsync(ct);
        public async ValueTask DisposeAsync()
        {
            // Attempt both cleanups; the handler preserves committed success and skips dispatch on failure.
            try { await tx.DisposeAsync(); }
            finally { await db.DisposeAsync(); }
        }
    }
}
