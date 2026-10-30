using Scheduler.Domain;
using Xunit;

namespace Scheduler.UnitTests;

public class BookingRulesTests
{
    [Theory]
    [InlineData("2030-01-02T09:00:00", false)]
    [InlineData("2030-01-02T09:00:00Z", true)]
    [InlineData("2030-01-02T09:00:00+07:00", true)]
    [InlineData("invalid", false)]
    public void Timestamp_requires_explicit_offset(string text, bool expected) => Assert.Equal(expected, BookingRules.TryParseStart(text, out _));
    [Fact]
    public void Equivalent_offsets_normalize_to_same_instant()
    {
        BookingRules.TryParseStart("2030-01-02T09:00:00+07:00", out var first);
        BookingRules.TryParseStart("2030-01-02T02:00:00Z", out var second);
        Assert.Equal(first, second);
        Assert.Equal(TimeSpan.Zero, first.Offset);
    }
    [Fact]
    public void Adjacent_intervals_do_not_overlap_but_partial_overlap_does()
    {
        var start = DateTimeOffset.Parse("2030-01-02T02:00:00Z");
        Assert.False(BookingRules.Overlaps(start, start.AddMinutes(30), start.AddMinutes(30), start.AddHours(1)));
        Assert.True(BookingRules.Overlaps(start, start.AddHours(1), start.AddMinutes(30), start.AddHours(2)));
    }
    [Theory]
    [InlineData("2030-01-02T08:00:00+07:00", 540, true)]
    [InlineData("2030-01-02T16:30:00+07:00", 60, false)]
    [InlineData("2030-01-02T07:59:00+07:00", 30, false)]
    [InlineData("2030-01-02T16:30:00+07:00", 30, true)]
    public void Full_interval_must_fit_dealership_hours(string text, int minutes, bool expected)
    {
        var start = DateTimeOffset.Parse(text);
        Assert.Equal(expected, BookingRules.WithinOpeningHours(start, start.AddMinutes(minutes), "Asia/Ho_Chi_Minh"));
    }
}
