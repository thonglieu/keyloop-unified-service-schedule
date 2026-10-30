using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Scheduler.Application;
using Scheduler.Domain;
using Scheduler.Infrastructure;
using Xunit;
using Npgsql;

namespace Scheduler.IntegrationTests;

public sealed class ProviderTransactionTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [Theory]
    [InlineData("positiveInterval", PostgresErrorCodes.CheckViolation)]
    [InlineData("requiredBay", PostgresErrorCodes.NotNullViolation)]
    [InlineData("customerVehicle", PostgresErrorCodes.ForeignKeyViolation)]
    public async Task Q05_Real_database_enforces_positive_interval_required_resource_and_vehicle_owner(string invalid, string sqlState)
    {
        var s = await database.CreateScenario(); var row = s.Row();
        await using (var setup = database.OpenContext()) { setup.Add(row); await setup.SaveChangesAsync(); }
        await using (var db = database.OpenContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            Task Execute() => invalid switch
            {
                "positiveInterval" => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Appointments\" SET \"EndsAtUtc\" = \"StartsAtUtc\" WHERE \"Id\" = {row.Id}"),
                "requiredBay" => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Appointments\" SET \"ServiceBayId\" = NULL WHERE \"Id\" = {row.Id}"),
                _ => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Appointments\" SET \"VehicleId\" = {s.Vehicles[1]} WHERE \"Id\" = {row.Id}")
            };
            Assert.Equal(sqlState, (await Assert.ThrowsAsync<Npgsql.PostgresException>(Execute)).SqlState);
        }
        await using var independent = database.OpenContext();
        Assert.Equal(AppointmentDto.From(row), AppointmentDto.From(await independent.Appointments.SingleAsync(a => a.Id == row.Id)));
    }
    [Fact]
    public async Task DATA01_Model_matches_migration_snapshot_and_database_has_no_pending_migrations()
    {
        await using var db = database.OpenContext();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task ARCH03_Real_queries_are_untracked_and_never_reserve_capacity_or_change_booking_count()
    {
        var s = await database.CreateScenario();
        await using var db = database.OpenContext();
        var before = await db.Appointments.CountAsync();
        var reads = new SchedulingReadStore(db);
        var policy = new OwnershipPolicy(new ApplicationBehaviorTests.User());
        var references = new ReferenceQueries(reads, policy);
        await references.Dealerships(default); await references.ServiceTypes(default);
        await references.Customers(default); await references.Vehicles(s.Customers[0], default);
        var appointments = new AppointmentQueries(reads, policy);
        Assert.Empty(await appointments.List(s.Customers[0], default));
        Assert.Equal(404, (await Assert.ThrowsAsync<BookingException>(() => appointments.Get(Guid.NewGuid(), default))).Status);
        var query = new CheckAvailabilityHandler(reads, policy, new ApplicationBehaviorTests.Clock());
        Assert.True((await query.Handle(s.DealershipId, s.ServiceTypeId, s.Start.ToString("O"), default)).Available);
        Assert.True((await query.Handle(s.DealershipId, s.ServiceTypeId, s.Start.ToString("O"), default)).Available);
        Assert.Empty(db.ChangeTracker.Entries());
        await using var independent = database.OpenContext();
        Assert.Equal(before, await independent.Appointments.CountAsync());
        var booked = await Handler(new Contexts(database.ConnectionString), new ObservedEvents(database)).Handle(Request(s), default);
        Assert.Equal(s.Customers[0], booked.CustomerId);
    }
    [Fact]
    public async Task DATA01_EVT01_Failure_after_real_save_rolls_back_and_publishes_nothing()
    {
        var s = await database.CreateScenario();
        var interceptor = new AfterSaveFailure(); var events = new ObservedEvents(database);
        var contexts = new Contexts(database.ConnectionString, interceptor);
        await Assert.ThrowsAsync<IOException>(() => Handler(contexts, events).Handle(Request(s), default));
        Assert.Equal(1, interceptor.Executed);
        Assert.Single(contexts.Created); Assert.Empty(events.Seen);
        await using var fresh = database.OpenContext();
        Assert.Empty(await fresh.Appointments.Where(a => a.DealershipId == s.DealershipId).ToListAsync());
    }

    [Fact]
    public async Task DATA01_EVT01_Real_exclusion_loser_retries_fresh_context_and_publishes_only_committed_alternative()
    {
        var s = await database.CreateScenario(technicians: 2, bays: 2);
        // SavingChanges is after the allocation read, before SQL insertion. A separate
        // connection commits the selected pair here, deterministically forcing the loser.
        var contender = new CommitCompetingBooking(database);
        var contexts = new Contexts(database.ConnectionString, contender);
        var events = new ObservedEvents(database);
        var result = await Handler(contexts, events).Handle(Request(s), default);
        Assert.Equal(1, contender.CompetingCommits);
        Assert.Equal(2, contexts.Created.Count);
        Assert.NotEqual(contexts.Created[0], contexts.Created[1]);
        Assert.NotEqual(contender.Selected!.TechnicianId, result.TechnicianId);
        Assert.NotEqual(contender.Selected.ServiceBayId, result.ServiceBayId);
        Assert.Equal(result.Id, Assert.Single(events.Seen).AppointmentId);
        Assert.True(events.ReadCommittedRow);
        await using var fresh = database.OpenContext();
        var rows = await fresh.Appointments.Where(a => a.DealershipId == s.DealershipId).ToArrayAsync();
        Assert.Equal(2, rows.Length);
        Assert.DoesNotContain(rows, a => a.Id == contender.Selected.Id);
    }

    [Fact]
    public async Task DATA01_EVT01_Lost_commit_acknowledgement_does_not_retry_or_publish_even_if_row_exists()
    {
        var s = await database.CreateScenario(); var interceptor = new CommitAcknowledgementFailure();
        var contexts = new Contexts(database.ConnectionString, interceptor); var events = new ObservedEvents(database);
        await Assert.ThrowsAsync<IOException>(() => Handler(contexts, events).Handle(Request(s), default));
        Assert.Equal(1, interceptor.Executed); Assert.Single(contexts.Created); Assert.Empty(events.Seen);
        await using var fresh = database.OpenContext();
        Assert.Single(await fresh.Appointments.Where(a => a.DealershipId == s.DealershipId).ToArrayAsync());
    }

    [Fact]
    public async Task DATA01_EVT01_Cancellation_after_real_save_rolls_back_before_commit()
    {
        var s = await database.CreateScenario(); using var cancellation = new CancellationTokenSource();
        var interceptor = new CancelAfterSave(cancellation);
        var contexts = new Contexts(database.ConnectionString, interceptor); var events = new ObservedEvents(database);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Handler(contexts, events).Handle(Request(s), cancellation.Token));
        Assert.Equal(1, interceptor.Executed); Assert.Empty(events.Seen);
        await using var fresh = database.OpenContext();
        Assert.Empty(await fresh.Appointments.Where(a => a.DealershipId == s.DealershipId).ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DATA01_EVT01_Context_cleanup_failure_preserves_commit_but_suppresses_event(bool beforeDisposal)
    {
        var s = await database.CreateScenario();
        var contexts = new Contexts(database.ConnectionString) { FailDisposal = !beforeDisposal, FailBeforeDisposal = beforeDisposal };
        var events = new ObservedEvents(database);
        try
        {
            var result = await Handler(contexts, events).Handle(Request(s), default);
            Assert.Single(contexts.Created); Assert.Equal(1, contexts.Disposals);
            Assert.Empty(events.Seen);
            await using var fresh = database.OpenContext();
            Assert.True(await fresh.Appointments.AnyAsync(a => a.Id == result.Id));
        }
        finally { await contexts.Cleanup(); }
    }

    [Fact]
    public async Task DATA01_Real_transactions_use_read_committed_with_durability_enabled()
    {
        var factory = new BookingTransactionFactory(new Contexts(database.ConnectionString), NullLogger<BookingTransactionFactory>.Instance);
        await using var transaction = await factory.Begin(default);
        await using var connection = new Npgsql.NpgsqlConnection(database.ConnectionString); await connection.OpenAsync();
        foreach (var setting in new[] { "fsync", "synchronous_commit" })
        {
            await using var command = new Npgsql.NpgsqlCommand("SHOW " + setting, connection);
            Assert.Equal("on", await command.ExecuteScalarAsync());
        }
        var intercept = new IsolationObserver(); var contexts = new Contexts(database.ConnectionString, intercept);
        var s = await database.CreateScenario();
        await Handler(contexts, new ObservedEvents(database)).Handle(Request(s), default);
        Assert.Equal(System.Data.IsolationLevel.ReadCommitted, intercept.Isolation);
    }

    private static BookingRequest Request(Scenario s) => new(s.Customers[0], s.Vehicles[0], s.DealershipId, s.ServiceTypeId, s.Start.ToString("O"));
    private static BookAppointmentHandler Handler(Contexts contexts, IAppointmentEventDispatcher events) => new(
        new BookingTransactionFactory(contexts, NullLogger<BookingTransactionFactory>.Instance),
        new OwnershipPolicy(new ApplicationBehaviorTests.User()), new ApplicationBehaviorTests.Clock(), events);

    private sealed class Contexts(string connection, params IInterceptor[] interceptors) : IDbContextFactory<SchedulerDbContext>
    {
        public readonly List<Guid> Created = [];
        public bool FailDisposal, FailBeforeDisposal; public int Disposals;
        private readonly List<ObservedContext> contexts = [];
        public SchedulerDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<SchedulerDbContext>().UseNpgsql(connection).AddInterceptors(interceptors).Options;
            var context = new ObservedContext(options,
                () => { Disposals++; if (FailBeforeDisposal) throw new IOException("Synthetic failure before context disposal"); },
                () => { if (FailDisposal) throw new IOException("Synthetic failure after context disposal"); });
            contexts.Add(context); Created.Add(context.ContextId.InstanceId); return context;
        }
        public Task<SchedulerDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
        public async Task Cleanup() { foreach (var context in contexts) await context.DisposeSafely(); }
    }
    private sealed class ObservedContext(DbContextOptions<SchedulerDbContext> options, Action disposing, Action disposed) : SchedulerDbContext(options)
    {
        public override async ValueTask DisposeAsync() { disposing(); await base.DisposeAsync(); disposed(); }
        public ValueTask DisposeSafely() => base.DisposeAsync();
    }
    private sealed class ObservedEvents(TestDatabase database) : IAppointmentEventDispatcher
    {
        public List<AppointmentConfirmed> Seen = []; public bool ReadCommittedRow;
        public async Task Dispatch(AppointmentConfirmed message)
        {
            Seen.Add(message);
            await using var independent = database.OpenContext();
            ReadCommittedRow = await independent.Appointments.AnyAsync(a => a.Id == message.AppointmentId);
        }
    }
    private sealed class AfterSaveFailure : SaveChangesInterceptor
    {
        public int Executed;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
        { Executed++; throw new IOException("Synthetic failure after successful SQL insertion"); }
    }
    private sealed class CancelAfterSave(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public int Executed;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
        { Executed++; cancellation.Cancel(); return ValueTask.FromResult(result); }
    }
    private sealed class CommitCompetingBooking(TestDatabase database) : SaveChangesInterceptor
    {
        public int CompetingCommits; public Appointment? Selected;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (CompetingCommits != 0) return result;
            Selected = eventData.Context!.ChangeTracker.Entries<Appointment>().Single(e => e.State == EntityState.Added).Entity;
            await using var competitor = database.OpenContext();
            competitor.Add(Appointment.Confirm(Selected.CustomerId, Selected.VehicleId, Selected.DealershipId,
                Selected.ServiceTypeId, Selected.TechnicianId, Selected.ServiceBayId, Selected.Interval, TestDatabase.Now));
            await competitor.SaveChangesAsync(ct); CompetingCommits++;
            return result;
        }
    }
    private sealed class CommitAcknowledgementFailure : DbTransactionInterceptor
    {
        public int Executed;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
        { Executed++; throw new IOException("Synthetic lost commit acknowledgement"); }
    }
    private sealed class IsolationObserver : DbTransactionInterceptor
    {
        public System.Data.IsolationLevel? Isolation;
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken ct = default)
        { Isolation = result.IsolationLevel; return ValueTask.FromResult(result); }
    }
}
