using Scheduler.Domain;

namespace Scheduler.Application;

public static class RequestValidator
{
    public static void Id(Guid id, string field)
    {
        if (id == Guid.Empty) throw new BookingException(400, "Invalid request.", new() { [field] = ["A nonempty ID is required."] });
    }
    public static DateTimeOffset Start(Guid dealershipId, Guid serviceTypeId, string text, TimeProvider clock)
    {
        var start = StartSyntax(dealershipId, serviceTypeId, text);
        if (start < clock.GetUtcNow()) throw new BookingException(400, "Invalid request.", new() { ["startsAt"] = ["Start time must not be in the past."] });
        return start;
    }
    public static DateTimeOffset StartSyntax(Guid dealershipId, Guid serviceTypeId, string text)
    {
        Id(dealershipId, "dealershipId"); Id(serviceTypeId, "serviceTypeId");
        if (!BookingRules.TryParseStart(text, out var start)) throw new BookingException(400, "Invalid request.", new() { ["startsAt"] = ["An ISO timestamp with an explicit offset is required."] });
        return start;
    }
}
public sealed class OwnershipPolicy(ICurrentUser user)
{
    public BookingIdempotency Idempotency(string key, BookingRequest request, DateTimeOffset start)
    {
        RequireAccess();
        return BookingIdempotency.Create(user.Issuer, user.Subject, key, request, start);
    }
    public void RequireAccess()
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.Subject) || string.IsNullOrWhiteSpace(user.Issuer)) throw new BookingException(401, "Authentication required.");
        if (!user.IsAdmin && !user.IsCustomer) throw new BookingException(403, "Access denied.");
    }
    public void RequireAdmin() { RequireAccess(); if (!user.IsAdmin) throw new BookingException(403, "Access denied."); }
    public async Task<Guid?> Scope(ISchedulingReadStore reads, CancellationToken ct)
    {
        RequireAccess();
        if (user.IsAdmin) return null;
        return await reads.ResolveCustomer(user.Issuer, user.Subject, ct) ?? throw new BookingException(403, "Access denied.");
    }
    public async Task<Guid?> Match(ISchedulingReadStore reads, Guid customerId, int mismatchStatus, CancellationToken ct)
    {
        var scope = await Scope(reads, ct);
        if (scope.HasValue && scope != customerId) throw new BookingException(mismatchStatus, mismatchStatus == 404 ? "Resource not found." : "Access denied.");
        return scope;
    }
}
public static class SchedulingValidation
{
    public static async Task<(UtcInterval Interval, DealershipDto Dealer)> Interval(ISchedulingReadStore reads, Guid dealershipId, Guid serviceTypeId, DateTimeOffset start, CancellationToken ct)
    {
        var dealer = await reads.Dealership(dealershipId, ct) ?? throw new BookingException(404, "Resource not found.");
        var service = await reads.ServiceType(serviceTypeId, ct) ?? throw new BookingException(404, "Resource not found.");
        try { return (new UtcInterval(start, start.AddMinutes(service.DurationMinutes)), dealer); }
        catch (ArgumentOutOfRangeException) { throw new BookingException(400, "Invalid request.", new() { ["startsAt"] = ["Service interval exceeds supported dates."] }); }
    }
}
public sealed class CheckAvailabilityHandler(ISchedulingReadStore reads, OwnershipPolicy ownership, TimeProvider clock)
{
    public async Task<AvailabilityDto> Handle(Guid dealershipId, Guid serviceTypeId, string startsAt, CancellationToken ct)
    {
        ownership.RequireAccess();
        var start = RequestValidator.Start(dealershipId, serviceTypeId, startsAt, clock);
        var (interval, dealer) = await SchedulingValidation.Interval(reads, dealershipId, serviceTypeId, start, ct);
        if (!BookingRules.WithinOpeningHours(interval.Start, interval.End, dealer.TimeZoneId)) return new(false, interval.Start, interval.End, "outsideOpeningHours");
        var available = (await reads.Eligible(dealershipId, serviceTypeId, interval, ct)).Any();
        return new(available, interval.Start, interval.End, available ? null : "noEligibleResourcePair");
    }
}
public sealed class BookAppointmentHandler(IBookingTransactionFactory transactions, OwnershipPolicy ownership, TimeProvider clock, IAppointmentEventDispatcher events)
{
    public async Task<AppointmentDto> Handle(BookingRequest request, CancellationToken ct, string? idempotencyKey = null)
    {
        using var activity = BookingTelemetry.StartActivity("booking");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var outcome = "failed";
        try
        {
            ownership.RequireAccess();
            RequestValidator.Id(request.CustomerId, "customerId"); RequestValidator.Id(request.VehicleId, "vehicleId");
            var parsedStart = RequestValidator.StartSyntax(request.DealershipId, request.ServiceTypeId, request.StartsAt);
            var idempotency = idempotencyKey is null ? null : ownership.Idempotency(idempotencyKey, request, parsedStart);
            if (idempotency is null) RequestValidator.Start(request.DealershipId, request.ServiceTypeId, request.StartsAt, clock);
            var attempted = new HashSet<ResourcePair>();
            Appointment? committed = null;
            var disposed = true;
            // Each iteration owns a fresh context and explicit transaction. Only definite exclusion failures retry.
            while (true)
            {
                var tx = await transactions.Begin(ct);
                try
                {
                    var scope = await ownership.Match(tx.Reads, request.CustomerId, 403, ct);
                    if (idempotency is not null)
                    {
                        var recorded = await tx.LockAndReadIdempotency(idempotency.ScopeKey, ct);
                        if (recorded is not null)
                        {
                            if (recorded.Fingerprint != idempotency.Fingerprint)
                                throw new BookingException(409, "Idempotency key was already used for a different request.", code: "idempotency_conflict");
                            var ownedVehicle = await tx.Reads.Vehicle(request.VehicleId, scope, ct) ?? throw new BookingException(404, "Resource not found.");
                            if (ownedVehicle.CustomerId != request.CustomerId) throw new BookingException(400, "Vehicle does not belong to selected customer.");
                            outcome = "replayed";
                            return recorded.Appointment;
                        }
                    }
                    var start = RequestValidator.Start(request.DealershipId, request.ServiceTypeId, request.StartsAt, clock);
                    var (interval, dealer) = await SchedulingValidation.Interval(tx.Reads, request.DealershipId, request.ServiceTypeId, start, ct);
                    if (!await tx.Reads.CustomerExists(request.CustomerId, ct)) throw new BookingException(404, "Resource not found.");
                    var vehicle = await tx.Reads.Vehicle(request.VehicleId, scope, ct) ?? throw new BookingException(404, "Resource not found.");
                    if (vehicle.CustomerId != request.CustomerId) throw new BookingException(400, "Vehicle does not belong to selected customer.");
                    if (!BookingRules.WithinOpeningHours(interval.Start, interval.End, dealer.TimeZoneId)) throw new BookingException(409, "The full service must fit opening hours.");
                    var pair = (await tx.Reads.Eligible(request.DealershipId, request.ServiceTypeId, interval, ct)).FirstOrDefault(p => !attempted.Contains(p));
                    if (pair is null) throw new BookingException(409, "No qualified technician and bay are available for the entire service.");
                    attempted.Add(pair);
                    // Serialize cooperating allocators on both resources, then re-read eligibility
                    // under Read Committed. The exclusion constraints still protect non-cooperating writers.
                    await tx.LockAllocation(pair, ct);
                    if (!(await tx.Reads.Eligible(request.DealershipId, request.ServiceTypeId, interval, ct)).Contains(pair)) continue;
                    var appointment = Appointment.Confirm(request.CustomerId, request.VehicleId, request.DealershipId, request.ServiceTypeId, pair.TechnicianId, pair.ServiceBayId, interval, clock.GetUtcNow());
                    try
                    {
                        using var persistence = BookingTelemetry.StartActivity("booking.persist");
                        await tx.Save(appointment, ct);
                    }
                    catch (CapacityRaceException) { continue; }
                    if (idempotency is not null)
                        await tx.RecordIdempotency(idempotency, AppointmentDto.From(appointment), ct);
                    // Commit failures have an uncertain outcome and MUST NOT be retried.
                    await tx.Commit(ct);
                    committed = appointment;
                }
                finally
                {
                    try { await tx.DisposeAsync(); }
                    catch when (committed is not null)
                    {
                        // A diagnostic event must not run with a possibly undisposed attempt.
                        disposed = false;
                        BookingTelemetry.RecordDiagnosticFailure();
                    }
                }
                break;
            }
            if (committed is null) throw new BookingException(409, "No qualified technician and bay are available for the entire service.");
            outcome = "confirmed";
            // Includes dispatcher resolution/implementation failure; diagnostics cannot undo a known commit.
            if (disposed)
                try { await events.Dispatch(committed.Confirmation!); } catch { BookingTelemetry.RecordDiagnosticFailure(); }
            return AppointmentDto.From(committed);
        }
        catch (BookingException ex) { outcome = ex.Status == 409 ? "conflict" : "rejected"; throw; }
        finally
        {
            BookingTelemetry.RecordOutcome(outcome, watch.Elapsed.TotalMilliseconds);
            activity.SetOutcome(outcome);
        }
    }
}
public sealed class AppointmentQueries(ISchedulingReadStore reads, OwnershipPolicy ownership)
{
    public async Task<AppointmentDto> Get(Guid id, CancellationToken ct)
    {
        ownership.RequireAccess(); RequestValidator.Id(id, "id");
        var scope = await ownership.Scope(reads, ct);
        return await reads.Appointment(id, scope, ct) ?? throw new BookingException(404, "Resource not found.");
    }
    public async Task<AppointmentDto[]> List(Guid customerId, CancellationToken ct)
    {
        ownership.RequireAccess(); RequestValidator.Id(customerId, "customerId");
        await ownership.Match(reads, customerId, 403, ct);
        if (!await reads.CustomerExists(customerId, ct)) throw new BookingException(404, "Resource not found.");
        return await reads.Appointments(customerId, ct);
    }
}
public sealed class ReferenceQueries(ISchedulingReadStore reads, OwnershipPolicy ownership)
{
    public Task<DealershipDto[]> Dealerships(CancellationToken ct) { ownership.RequireAccess(); return reads.Dealerships(ct); }
    public Task<ServiceTypeDto[]> ServiceTypes(CancellationToken ct) { ownership.RequireAccess(); return reads.ServiceTypes(ct); }
    public Task<CustomerDto[]> Customers(CancellationToken ct) { ownership.RequireAdmin(); return reads.Customers(ct); }
    public Task<FixtureDto?> Fixtures(CancellationToken ct) { ownership.RequireAdmin(); return reads.Fixtures(ct); }
    public async Task<VehicleDto[]> Vehicles(Guid id, CancellationToken ct)
    {
        ownership.RequireAccess(); RequestValidator.Id(id, "id");
        await ownership.Match(reads, id, 404, ct);
        if (!await reads.CustomerExists(id, ct)) throw new BookingException(404, "Resource not found.");
        return await reads.Vehicles(id, ct);
    }
}
