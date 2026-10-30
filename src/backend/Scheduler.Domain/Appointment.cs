namespace Scheduler.Domain;

public sealed record UtcInterval
{
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }
    public UtcInterval(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start) throw new ArgumentException("Service interval must be positive.");
        Start = start.ToUniversalTime();
        End = end.ToUniversalTime();
    }
    public bool Overlaps(UtcInterval other) => BookingRules.Overlaps(Start, End, other.Start, other.End);
}
public sealed record AppointmentConfirmed(Guid EventId, Guid AppointmentId, DateTimeOffset OccurredAtUtc);
public sealed class Appointment
{
    private Appointment() { }
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid DealershipId { get; private set; }
    public Guid ServiceTypeId { get; private set; }
    public Guid TechnicianId { get; private set; }
    public Guid ServiceBayId { get; private set; }
    public DateTimeOffset StartsAtUtc { get; private set; }
    public DateTimeOffset EndsAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public AppointmentConfirmed? Confirmation { get; private set; }
    public UtcInterval Interval => new(StartsAtUtc, EndsAtUtc);
    public static Appointment Confirm(Guid customerId, Guid vehicleId, Guid dealershipId, Guid serviceTypeId,
        Guid technicianId, Guid serviceBayId, UtcInterval interval, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(interval);
        if (new[] { customerId, vehicleId, dealershipId, serviceTypeId, technicianId, serviceBayId }.Any(x => x == Guid.Empty))
            throw new ArgumentException("All appointment associations are required.");
        var result = new Appointment { Id = Guid.NewGuid(), CustomerId = customerId, VehicleId = vehicleId,
            DealershipId = dealershipId, ServiceTypeId = serviceTypeId, TechnicianId = technicianId,
            ServiceBayId = serviceBayId, StartsAtUtc = interval.Start, EndsAtUtc = interval.End,
            CreatedAtUtc = createdAt.ToUniversalTime() };
        result.Confirmation = new(Guid.NewGuid(), result.Id, result.CreatedAtUtc);
        return result;
    }
}
public sealed class CustomerIdentityLink
{
    public Guid Id { get; set; }
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public Guid CustomerId { get; set; }
}
