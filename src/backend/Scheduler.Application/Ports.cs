using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Scheduler.Application;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }
    bool IsCustomer { get; }
    string Issuer { get; }
    string Subject { get; }
}
public record DealershipDto(Guid Id, string Name, string TimeZoneId);
public record ServiceTypeDto(Guid Id, string Name, int DurationMinutes);
public record CustomerDto(Guid Id, string Name);
public record VehicleDto(Guid Id, Guid CustomerId, string Registration, string Make, string Model);
public record ResourcePair(Guid TechnicianId, Guid ServiceBayId);
public interface ISchedulingReadStore
{
    Task<Guid?> ResolveCustomer(string issuer, string subject, CancellationToken ct);
    Task<DealershipDto?> Dealership(Guid id, CancellationToken ct);
    Task<ServiceTypeDto?> ServiceType(Guid id, CancellationToken ct);
    Task<bool> CustomerExists(Guid id, CancellationToken ct);
    Task<VehicleDto?> Vehicle(Guid id, Guid? customerId, CancellationToken ct);
    Task<IEnumerable<ResourcePair>> Eligible(Guid dealershipId, Guid serviceTypeId, Scheduler.Domain.UtcInterval interval, CancellationToken ct);
    Task<DealershipDto[]> Dealerships(CancellationToken ct);
    Task<ServiceTypeDto[]> ServiceTypes(CancellationToken ct);
    Task<CustomerDto[]> Customers(CancellationToken ct);
    Task<VehicleDto[]> Vehicles(Guid customerId, CancellationToken ct);
    Task<AppointmentDto?> Appointment(Guid id, Guid? customerId, CancellationToken ct);
    Task<AppointmentDto[]> Appointments(Guid customerId, CancellationToken ct);
    Task<FixtureDto?> Fixtures(CancellationToken ct);
}
public interface IBookingTransactionFactory
{
    Task<IBookingTransaction> Begin(CancellationToken ct);
}
public interface IBookingTransaction : IAsyncDisposable
{
    ISchedulingReadStore Reads { get; }
    Task<RecordedBooking?> LockAndReadIdempotency(string scopeKey, CancellationToken ct);
    Task RecordIdempotency(BookingIdempotency key, AppointmentDto appointment, CancellationToken ct);
    Task LockAllocation(ResourcePair pair, CancellationToken ct);
    Task Save(Scheduler.Domain.Appointment appointment, CancellationToken ct);
    Task Commit(CancellationToken ct);
}
public sealed class CapacityRaceException : Exception { }
public interface IAppointmentEventDispatcher
{
    Task Dispatch(Scheduler.Domain.AppointmentConfirmed message);
}
public interface IAppointmentConfirmedHandler
{
    Task Handle(Scheduler.Domain.AppointmentConfirmed message, CancellationToken ct);
}
public interface ISchedulerReadiness
{
    Task<bool> IsReady(CancellationToken ct);
}
public interface IDependencyFailureClassifier
{
    bool IsUnavailable(Exception exception);
}
public sealed class DependencyUnavailableException : Exception
{
    public DependencyUnavailableException() : base("Dependency unavailable.") { }
}
public static class BookingTelemetry
{
    public static readonly ActivitySource Activities = new("Scheduler.Booking");
    public static readonly Meter Meter = new("Scheduler.Booking");
    public static readonly Counter<long> DiagnosticFailures = Meter.CreateCounter<long>("scheduler.booking.diagnostic_failures");
    public static readonly Counter<long> Outcomes = Meter.CreateCounter<long>("scheduler.bookings");
    public static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("scheduler.booking.duration", "ms");
    public static void RecordDiagnosticFailure()
    {
        try { DiagnosticFailures.Add(1); } catch { /* Observers cannot change business outcomes. */ }
    }
    public static void RecordOutcome(string outcome, double milliseconds)
    {
        try { Outcomes.Add(1, new KeyValuePair<string, object?>("outcome", outcome)); } catch { }
        try { Duration.Record(milliseconds); } catch { }
    }
    public static SafeActivity StartActivity(string name)
    {
        try { return new(Activities.StartActivity(name)); } catch { return new(null); }
    }
    public sealed class SafeActivity(Activity? activity) : IDisposable
    {
        public void SetOutcome(string outcome) { try { activity?.SetTag("booking.outcome", outcome); } catch { } }
        public void Dispose() { try { activity?.Dispose(); } catch { } }
    }
}
