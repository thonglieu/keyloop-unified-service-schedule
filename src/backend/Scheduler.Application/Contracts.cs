using Scheduler.Domain;

namespace Scheduler.Application;

public record BookingRequest(Guid CustomerId, Guid VehicleId, Guid DealershipId, Guid ServiceTypeId, string StartsAt);
public record AppointmentDto(Guid Id, Guid CustomerId, Guid VehicleId, Guid DealershipId, Guid ServiceTypeId, Guid TechnicianId, Guid ServiceBayId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset CreatedAt)
{
    public static AppointmentDto From(Appointment a) => new(a.Id, a.CustomerId, a.VehicleId, a.DealershipId, a.ServiceTypeId, a.TechnicianId, a.ServiceBayId, a.StartsAtUtc, a.EndsAtUtc, a.CreatedAtUtc);
}
public record AvailabilityDto(bool Available, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Reason);
public sealed class BookingException(int status, string message, Dictionary<string, string[]>? errors = null, string? code = null) : Exception(message) { public int Status { get; } = status; public Dictionary<string, string[]>? Errors { get; } = errors; public string? Code { get; } = code; }
public interface IScheduler
{
    Task<AvailabilityDto> Availability(Guid dealershipId, Guid serviceTypeId, string startsAt, CancellationToken ct);
    Task<AppointmentDto> Book(BookingRequest request, CancellationToken ct);
}
public record FixtureCustomer(Guid CustomerId, Guid VehicleId);
public record FixtureScenario(Guid DealershipId, Guid ServiceTypeId, DateTimeOffset StartsAt, int DurationMinutes, Guid[] TechnicianIds, Guid[] ServiceBayIds, DateTimeOffset? SecondStartsAt = null);
public record FixtureDto(string Namespace, DateTimeOffset SeededAt, DateTimeOffset ExpiresAt, FixtureCustomer[] Customers, Dictionary<string, FixtureScenario> Scenarios);
