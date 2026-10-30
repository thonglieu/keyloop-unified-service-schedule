namespace Scheduler.Infrastructure;

// Persistence-only receipt; raw client keys and identity claims are never stored.
public sealed class BookingReceipt
{
    public string ScopeKey { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public Guid AppointmentId { get; set; }
    public string ResponseJson { get; set; } = "";
}
