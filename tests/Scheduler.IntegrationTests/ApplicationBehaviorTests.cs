using Scheduler.Application;
using Scheduler.Domain;
using Xunit;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Scheduler.IntegrationTests;

// These are application unit tests, housed here to reuse existing project references; no provider is used.
public sealed class ApplicationBehaviorTests
{
    [Fact]
    public async Task Q04_EVT01_Capacity_consumed_while_waiting_for_allocation_uses_fresh_attempt()
    {
        var f = new Fixture();
        var available = f.Reads.Pairs[1];
        f.Factory.AfterLock = () => f.Reads.Pairs = [available];

        var result = await f.Book();

        Assert.Equal(2, f.Factory.Attempts.Count);
        Assert.Null(f.Factory.Attempts[0].Saved);
        Assert.True(f.Factory.Attempts[0].Disposed);
        Assert.False(f.Factory.Attempts[0].Committed);
        Assert.Equal(available.TechnicianId, result.TechnicianId);
        Assert.Equal(available.ServiceBayId, result.ServiceBayId);
        Assert.Equal(result.Id, Assert.Single(f.Events.Messages).AppointmentId);
    }

    [Fact]
    public async Task Q04_Finite_candidate_retry_does_not_abandon_available_pair_after_128_losses()
    {
        var f = new Fixture();
        f.Reads.Pairs = Enumerable.Range(0, 129).Select(_ => new ResourcePair(Guid.NewGuid(), Guid.NewGuid())).ToArray();
        f.Factory.SaveFailure = attempt => attempt <= 128 ? new CapacityRaceException() : null;
        var result = await f.Book();
        Assert.Equal(129, f.Factory.Attempts.Count);
        Assert.Equal(f.Reads.Pairs[128].TechnicianId, result.TechnicianId);
        Assert.Single(f.Events.Messages);
    }

    [Fact]
    public async Task Q04_EVT01_Exhausted_candidates_return_conflict_without_reusing_pair_or_publishing()
    {
        var f = new Fixture(); f.Factory.SaveFailure = _ => new CapacityRaceException();
        Assert.Equal(409, (await Assert.ThrowsAsync<BookingException>(() => f.Book())).Status);
        var saved = f.Factory.Attempts.Where(a => a.Saved is not null).ToArray();
        Assert.Equal(f.Reads.Pairs.Length, saved.Length);
        Assert.Equal(saved.Length, saved.Select(a => a.Saved!.TechnicianId).Distinct().Count());
        Assert.All(f.Factory.Attempts, a => { Assert.True(a.Disposed); Assert.False(a.Committed); });
        Assert.Empty(f.Events.Messages);
    }

    [Fact]
    public async Task ARCH03_SEC02_Advisory_query_requires_role_but_no_mapping_or_command_side_effects()
    {
        var f = new Fixture();
        var handler = new CheckAvailabilityHandler(f.Reads, new OwnershipPolicy(new User(true, false, true)), new Clock());
        var result = await handler.Handle(f.Request.DealershipId, f.Request.ServiceTypeId, f.Request.StartsAt, default);
        Assert.True(result.Available); Assert.Equal(f.Start.AddHours(1), result.EndsAt);
        Assert.DoesNotContain("identity", f.Reads.Calls); Assert.Empty(f.Trace);
    }

    [Theory]
    [InlineData("get", 404)]
    [InlineData("list", 403)]
    [InlineData("vehicles", 404)]
    [InlineData("customers", 403)]
    public async Task SEC02_Direct_query_handlers_enforce_owner_scope_and_admin_policy(string operation, int expected)
    {
        var f = new Fixture(); f.Reads.MappedCustomer = f.Request.CustomerId;
        var policy = new OwnershipPolicy(new User(true, false, true));
        Task Invoke() => operation switch
        {
            "get" => new AppointmentQueries(f.Reads, policy).Get(Guid.NewGuid(), default),
            "list" => new AppointmentQueries(f.Reads, policy).List(Guid.NewGuid(), default),
            "vehicles" => new ReferenceQueries(f.Reads, policy).Vehicles(Guid.NewGuid(), default),
            "customers" => new ReferenceQueries(f.Reads, policy).Customers(default),
            _ => throw new ArgumentException()
        };
        Assert.Equal(expected, (await Assert.ThrowsAsync<BookingException>(Invoke)).Status);
        if (operation == "get") Assert.Equal(f.Request.CustomerId, f.Reads.AppointmentScope);
        else Assert.DoesNotContain("appointment", f.Reads.Calls);
        Assert.Empty(f.Trace);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("list")]
    [InlineData("vehicles")]
    public async Task API01_Empty_query_ids_do_not_query_identity_or_storage(string operation)
    {
        var f = new Fixture(); var policy = new OwnershipPolicy(new User(true, false, true));
        Task Invoke() => operation switch
        {
            "get" => new AppointmentQueries(f.Reads, policy).Get(Guid.Empty, default),
            "list" => new AppointmentQueries(f.Reads, policy).List(Guid.Empty, default),
            _ => new ReferenceQueries(f.Reads, policy).Vehicles(Guid.Empty, default)
        };
        Assert.Equal(400, (await Assert.ThrowsAsync<BookingException>(Invoke)).Status); Assert.Empty(f.Reads.Calls);
    }
    [Theory]
    [InlineData("scheduler.bookings")]
    [InlineData("scheduler.booking.duration")]
    [InlineData("scheduler.booking.diagnostic_failures")]
    public async Task EVT01_Throwing_metric_observer_cannot_reverse_known_commit(string instrumentName)
    {
        var f = new Fixture();
        f.Events.Failure = new InvalidOperationException("Trigger diagnostic-failure metric");
        var observed = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, subscriber) =>
        {
            if (instrument.Meter.Name == "Scheduler.Booking" && instrument.Name == instrumentName)
                subscriber.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => { observed++; throw new InvalidOperationException("Synthetic observer failure"); });
        listener.SetMeasurementEventCallback<double>((_, _, _, _) => { observed++; throw new InvalidOperationException("Synthetic observer failure"); });
        listener.Start();
        try
        {
            var result = await f.Book();
            Assert.True(observed > 0);
            Assert.True(Assert.Single(f.Factory.Attempts).Committed);
            Assert.Equal(f.Factory.Attempts[0].Saved!.Id, result.Id);
        }
        finally
        {
            // A throwing callback can remain in an instrument's dispatch snapshot.
            // Disarm fault injection before disposal so later hosts can observe the shared meter.
            listener.SetMeasurementEventCallback<long>((_, _, _, _) => { });
            listener.SetMeasurementEventCallback<double>((_, _, _, _) => { });
        }
    }

    [Fact]
    public async Task EVT01_Throwing_activity_stop_observer_cannot_reverse_known_commit()
    {
        var f = new Fixture(); var observed = false;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Scheduler.Booking",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName != "booking") return;
                observed = true; throw new InvalidOperationException("Synthetic activity observer failure");
            }
        };
        ActivitySource.AddActivityListener(listener);
        var result = await f.Book();
        Assert.True(observed); Assert.True(Assert.Single(f.Factory.Attempts).Committed);
        Assert.Equal(f.Factory.Attempts[0].Saved!.Id, result.Id);
    }
    [Fact]
    public async Task EVT01_Success_commits_and_disposes_before_publishing_matching_event()
    {
        var f = new Fixture();
        var result = await f.Book();
        Assert.Equal(new[] { "begin", "save", "commit", "dispose", "dispatch" }, f.Trace);
        Assert.Equal(result.Id, Assert.Single(f.Events.Messages).AppointmentId);
        Assert.Equal(f.Request.CustomerId, result.CustomerId);
        Assert.Equal(f.Request.VehicleId, result.VehicleId);
        Assert.Equal(f.Start.AddMinutes(60), result.EndsAt);
    }

    [Fact]
    public async Task DATA01_EVT01_Exclusion_retry_uses_fresh_transaction_aggregate_and_alternate_pair()
    {
        var f = new Fixture();
        f.Factory.SaveFailure = attempt => attempt == 1 ? new CapacityRaceException() : null;
        var result = await f.Book();
        Assert.Equal(2, f.Factory.Attempts.Count);
        var first = f.Factory.Attempts[0]; var second = f.Factory.Attempts[1];
        Assert.NotSame(first, second);
        Assert.NotEqual(first.Saved!.Id, second.Saved!.Id);
        Assert.NotEqual(first.Saved.Confirmation!.EventId, second.Saved.Confirmation!.EventId);
        Assert.NotEqual(first.Saved.TechnicianId, second.Saved.TechnicianId);
        Assert.False(first.Committed); Assert.True(first.Disposed); Assert.True(second.Committed);
        Assert.Equal(result.Id, Assert.Single(f.Events.Messages).AppointmentId);
        Assert.Equal(new[] { "begin", "save", "dispose", "begin", "save", "commit", "dispose", "dispatch" }, f.Trace);
    }

    [Fact]
    public async Task DATA01_EVT01_Unknown_commit_failure_never_retries_or_dispatches()
    {
        var f = new Fixture();
        var failure = new IOException("Synthetic uncertain commit");
        f.Factory.CommitFailure = failure;
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => f.Book()));
        Assert.Single(f.Factory.Attempts); Assert.Empty(f.Events.Messages);
        Assert.True(f.Factory.Attempts[0].Disposed);
    }

    [Fact]
    public async Task DATA01_EVT01_Save_failure_disposes_without_commit_or_event()
    {
        var f = new Fixture();
        f.Factory.SaveFailure = _ => new IOException("Synthetic storage failure");
        await Assert.ThrowsAsync<IOException>(() => f.Book());
        Assert.Equal(new[] { "begin", "save", "dispose" }, f.Trace);
        Assert.Single(f.Factory.Attempts); Assert.Empty(f.Events.Messages);
    }

    [Fact]
    public async Task DATA01_EVT01_Cancellation_after_save_prevents_commit_and_dispatch()
    {
        var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        f.Factory.AfterSave = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Book(cancellation.Token));
        Assert.False(Assert.Single(f.Factory.Attempts).Committed);
        Assert.True(f.Factory.Attempts[0].Disposed); Assert.Empty(f.Events.Messages);
    }

    [Fact]
    public async Task EVT01_Dispatcher_failure_preserves_committed_confirmation()
    {
        var f = new Fixture(); f.Events.Failure = new InvalidOperationException("Synthetic diagnostic failure");
        var result = await f.Book();
        Assert.True(Assert.Single(f.Factory.Attempts).Committed);
        Assert.Equal(f.Factory.Attempts[0].Saved!.Id, result.Id);
    }

    [Fact]
    public async Task EVT01_Known_commit_cleanup_failure_preserves_success()
    {
        var f = new Fixture(); f.Factory.DisposeFailure = new IOException("Synthetic cleanup failure");
        var result = await f.Book();
        Assert.True(Assert.Single(f.Factory.Attempts).Committed);
        Assert.Equal(f.Factory.Attempts[0].Saved!.Id, result.Id);
        Assert.Single(f.Factory.Attempts);
        Assert.Empty(f.Events.Messages);
    }

    [Theory]
    [InlineData(false, false, false, 401)]
    [InlineData(true, false, false, 403)]
    public async Task SEC02_Direct_booking_rejects_unauthenticated_and_unknown_roles_before_storage(bool authenticated, bool admin, bool customer, int status)
    {
        var f = new Fixture { User = new User(authenticated, admin, customer) };
        Assert.Equal(status, (await Assert.ThrowsAsync<BookingException>(() => f.Book())).Status);
        Assert.Empty(f.Factory.Attempts); Assert.Empty(f.Events.Messages);
    }

    [Fact]
    public async Task SEC02_Customer_cannot_spoof_customer_ID()
    {
        var f = new Fixture { User = new User(true, false, true) };
        f.Reads.MappedCustomer = Guid.NewGuid();
        Assert.Equal(403, (await Assert.ThrowsAsync<BookingException>(() => f.Book())).Status);
        Assert.Equal(new[] { "identity" }, f.Reads.Calls);
        Assert.Null(Assert.Single(f.Factory.Attempts).Saved); Assert.Empty(f.Events.Messages);
    }

    [Fact]
    public async Task SEC02_Unmapped_customer_is_denied_but_admin_needs_no_mapping()
    {
        var customer = new Fixture { User = new User(true, false, true) };
        Assert.Equal(403, (await Assert.ThrowsAsync<BookingException>(() => customer.Book())).Status);
        var admin = new Fixture(); await admin.Book();
        Assert.DoesNotContain("identity", admin.Reads.Calls);
    }

    [Fact]
    public async Task SEC02_Customer_vehicle_lookup_is_scoped_and_missing_is_concealed()
    {
        var f = new Fixture { User = new User(true, false, true) };
        f.Reads.MappedCustomer = f.Request.CustomerId; f.Reads.HideVehicle = true;
        Assert.Equal(404, (await Assert.ThrowsAsync<BookingException>(() => f.Book())).Status);
        Assert.Equal(f.Request.CustomerId, f.Reads.VehicleScope);
        Assert.Null(Assert.Single(f.Factory.Attempts).Saved);
    }

    [Fact]
    public async Task API01_Invalid_shape_is_rejected_before_transaction_or_identity_lookup()
    {
        var f = new Fixture { User = new User(true, false, true) };
        f.Request = f.Request with { VehicleId = Guid.Empty };
        Assert.Equal(400, (await Assert.ThrowsAsync<BookingException>(() => f.Book())).Status);
        Assert.Empty(f.Factory.Attempts); Assert.Empty(f.Reads.Calls);
    }

    internal sealed record User(bool IsAuthenticated = true, bool IsAdmin = true, bool IsCustomer = false,
        string Issuer = "https://identity.scheduler.test", string Subject = "synthetic") : ICurrentUser;

    internal sealed class Fixture
    {
        public readonly DateTimeOffset Start = TestDatabase.Now.AddDays(1).AddHours(2);
        public readonly List<string> Trace = [];
        public BookingRequest Request;
        public User User = new();
        public readonly Reads Reads;
        public readonly Transactions Factory;
        public readonly Events Events;
        public Fixture()
        {
            Request = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Start.ToString("O"));
            Reads = new Reads(Request); Factory = new Transactions(Reads, Trace); Events = new Events(Trace);
        }
        public Task<AppointmentDto> Book(CancellationToken ct = default) =>
            new BookAppointmentHandler(Factory, new OwnershipPolicy(User), new Clock(), Events).Handle(Request, ct);
    }
    internal sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => TestDatabase.Now; }
    internal sealed class Events(List<string> trace) : IAppointmentEventDispatcher
    {
        public List<AppointmentConfirmed> Messages = [];
        public Exception? Failure;
        public Task Dispatch(AppointmentConfirmed message)
        {
            trace.Add("dispatch"); Messages.Add(message);
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }
    internal sealed class Transactions(Reads reads, List<string> trace) : IBookingTransactionFactory
    {
        public List<Transaction> Attempts = [];
        public Dictionary<string, RecordedBooking> Receipts = [];
        public Func<int, Exception?>? SaveFailure;
        public Exception? CommitFailure, DisposeFailure;
        public Action? AfterSave;
        public Action? AfterLock;
        public Task<IBookingTransaction> Begin(CancellationToken ct)
        {
            trace.Add("begin"); var tx = new Transaction(this, reads, trace, Attempts.Count + 1);
            Attempts.Add(tx); return Task.FromResult<IBookingTransaction>(tx);
        }
    }
    internal sealed class Transaction(Transactions owner, Reads reads, List<string> trace, int attempt) : IBookingTransaction
    {
        public ISchedulingReadStore Reads => reads;
        public Appointment? Saved;
        public bool Committed, Disposed;
        private KeyValuePair<string, RecordedBooking>? pendingReceipt;
        public Task<RecordedBooking?> LockAndReadIdempotency(string scopeKey, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(owner.Receipts.GetValueOrDefault(scopeKey));
        }
        public Task RecordIdempotency(BookingIdempotency key, AppointmentDto appointment, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            pendingReceipt = new(key.ScopeKey, new(key.Fingerprint, appointment));
            return Task.CompletedTask;
        }
        public Task LockAllocation(ResourcePair pair, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            owner.AfterLock?.Invoke();
            return Task.CompletedTask;
        }
        public Task Save(Appointment appointment, CancellationToken ct)
        {
            trace.Add("save"); Saved = appointment;
            if (owner.SaveFailure?.Invoke(attempt) is { } error) return Task.FromException(error);
            owner.AfterSave?.Invoke(); return Task.CompletedTask;
        }
        public Task Commit(CancellationToken ct)
        {
            trace.Add("commit"); ct.ThrowIfCancellationRequested();
            if (owner.CommitFailure is { } error) return Task.FromException(error);
            if (pendingReceipt is { } receipt) owner.Receipts.Add(receipt.Key, receipt.Value);
            Committed = true; return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            trace.Add("dispose"); Disposed = true;
            return owner.DisposeFailure is { } error ? ValueTask.FromException(error) : ValueTask.CompletedTask;
        }
    }
    internal sealed class Reads(BookingRequest request) : ISchedulingReadStore
    {
        public List<string> Calls = [];
        public Guid? MappedCustomer, VehicleScope, AppointmentScope;
        public bool HideVehicle;
        public ResourcePair[] Pairs = [new(Guid.NewGuid(), Guid.NewGuid()), new(Guid.NewGuid(), Guid.NewGuid())];
        public Task<Guid?> ResolveCustomer(string issuer, string subject, CancellationToken ct) { Calls.Add("identity"); return Task.FromResult(MappedCustomer); }
        public Task<DealershipDto?> Dealership(Guid id, CancellationToken ct) { Calls.Add("dealer"); return Task.FromResult<DealershipDto?>(new(id, "Synthetic", "Asia/Ho_Chi_Minh")); }
        public Task<ServiceTypeDto?> ServiceType(Guid id, CancellationToken ct) { Calls.Add("service"); return Task.FromResult<ServiceTypeDto?>(new(id, "Synthetic", 60)); }
        public Task<bool> CustomerExists(Guid id, CancellationToken ct) { Calls.Add("customer"); return Task.FromResult(true); }
        public Task<VehicleDto?> Vehicle(Guid id, Guid? customerId, CancellationToken ct) { Calls.Add("vehicle"); VehicleScope = customerId; return Task.FromResult<VehicleDto?>(HideVehicle ? null : new(id, request.CustomerId, "Synthetic", "Test", "Fixture")); }
        public Task<IEnumerable<ResourcePair>> Eligible(Guid dealershipId, Guid serviceTypeId, UtcInterval interval, CancellationToken ct) { Calls.Add("eligible"); return Task.FromResult<IEnumerable<ResourcePair>>(Pairs); }
        public Task<DealershipDto[]> Dealerships(CancellationToken ct) => Task.FromResult<DealershipDto[]>([]);
        public Task<ServiceTypeDto[]> ServiceTypes(CancellationToken ct) => Task.FromResult<ServiceTypeDto[]>([]);
        public Task<CustomerDto[]> Customers(CancellationToken ct) => throw new NotSupportedException();
        public Task<VehicleDto[]> Vehicles(Guid customerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<AppointmentDto?> Appointment(Guid id, Guid? customerId, CancellationToken ct) { Calls.Add("appointment"); AppointmentScope = customerId; return Task.FromResult<AppointmentDto?>(null); }
        public Task<AppointmentDto[]> Appointments(Guid customerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<FixtureDto?> Fixtures(CancellationToken ct) => throw new NotSupportedException();
    }
}
