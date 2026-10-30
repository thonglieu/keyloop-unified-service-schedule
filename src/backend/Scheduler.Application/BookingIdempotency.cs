using System.Security.Cryptography;
using System.Text.Json;

namespace Scheduler.Application;

public sealed record BookingIdempotency(string ScopeKey, string Fingerprint)
{
    public static void ValidateKey(string key)
    {
        if (key.Length is < 1 or > 128 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or ':' or '-')))
            throw new BookingException(400, "Invalid request.", new() { ["idempotencyKey"] = ["Use one key of 1-128 ASCII letters, digits, '.', '_', ':' or '-'."] });
    }

    public static BookingIdempotency Create(string issuer, string subject, string key, BookingRequest request, DateTimeOffset start)
    {
        ValidateKey(key);
        return new(Hash(new[] { "POST /api/appointments", issuer, subject, key }),
            Hash(new { request.CustomerId, request.VehicleId, request.DealershipId, request.ServiceTypeId, StartsAt = start.UtcTicks }));
    }

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}

public sealed record RecordedBooking(string Fingerprint, AppointmentDto Appointment);
