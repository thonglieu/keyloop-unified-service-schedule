using System.Globalization;
using System.Text.RegularExpressions;

namespace Scheduler.Domain;

public static partial class BookingRules
{
    [GeneratedRegex(@"(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex OffsetSuffix();
    public static bool TryParseStart(string? text, out DateTimeOffset utc)
    {
        utc = default;
        if (text is null || !text.Contains('T') || !OffsetSuffix().IsMatch(text) ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
        utc = parsed.ToUniversalTime();
        return true;
    }
    public static bool Overlaps(DateTimeOffset start, DateTimeOffset end, DateTimeOffset otherStart, DateTimeOffset otherEnd) => start < otherEnd && otherStart < end;
    public static bool WithinOpeningHours(DateTimeOffset start, DateTimeOffset end, string zone)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(zone);
        var localStart = TimeZoneInfo.ConvertTime(start, tz);
        var localEnd = TimeZoneInfo.ConvertTime(end, tz);
        return end > start && localStart.Date == localEnd.Date && localStart.TimeOfDay >= TimeSpan.FromHours(8) && localEnd.TimeOfDay <= TimeSpan.FromHours(17);
    }
}
