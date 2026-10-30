using Scheduler.Domain;
using Xunit;

namespace Scheduler.UnitTests;

public sealed class AppointmentTests
{
    private static readonly DateTimeOffset Start = new(2030, 1, 2, 9, 0, 0, TimeSpan.FromHours(7));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ARCH01_Interval_rejects_nonpositive_duration(int minutes) =>
        Assert.Throws<ArgumentException>(() => new UtcInterval(Start, Start.AddMinutes(minutes)));

    [Fact]
    public void ARCH01_Interval_normalizes_offsets_and_compares_by_value()
    {
        var interval = new UtcInterval(Start, Start.AddHours(1));
        Assert.Equal(TimeSpan.Zero, interval.Start.Offset);
        Assert.Equal(TimeSpan.Zero, interval.End.Offset);
        Assert.Equal(new UtcInterval(Start.ToUniversalTime(), Start.AddHours(1).ToUniversalTime()), interval);
    }

    [Theory]
    [InlineData(-60, 0, false)]
    [InlineData(60, 120, false)]
    [InlineData(-30, 30, true)]
    [InlineData(30, 90, true)]
    [InlineData(0, 60, true)]
    [InlineData(10, 20, true)]
    [InlineData(-10, 70, true)]
    public void Q03_Half_open_overlap_is_symmetric(int from, int to, bool expected)
    {
        var a = new UtcInterval(Start, Start.AddHours(1));
        var b = new UtcInterval(Start.AddMinutes(from), Start.AddMinutes(to));
        Assert.Equal(expected, a.Overlaps(b));
        Assert.Equal(expected, b.Overlaps(a));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ARCH01_Each_association_is_required(int emptyIndex)
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        ids[emptyIndex] = Guid.Empty;
        Assert.Throws<ArgumentException>(() => Appointment.Confirm(ids[0], ids[1], ids[2], ids[3], ids[4], ids[5],
            new UtcInterval(Start, Start.AddHours(1)), Start.AddDays(-1)));
    }

    [Fact]
    public void ARCH01_Aggregate_preserves_associations_interval_and_confirmation()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var interval = new UtcInterval(Start, Start.AddHours(1));
        var appointment = Appointment.Confirm(ids[0], ids[1], ids[2], ids[3], ids[4], ids[5], interval, Start.AddDays(-1));
        Assert.Equal(ids, new[] { appointment.CustomerId, appointment.VehicleId, appointment.DealershipId,
            appointment.ServiceTypeId, appointment.TechnicianId, appointment.ServiceBayId });
        Assert.NotEqual(Guid.Empty, appointment.Id);
        Assert.Equal(interval, appointment.Interval);
        Assert.Equal(interval.Start, appointment.StartsAtUtc); Assert.Equal(interval.End, appointment.EndsAtUtc);
        Assert.Equal(TimeSpan.Zero, appointment.CreatedAtUtc.Offset);
        Assert.NotNull(appointment.Confirmation);
        Assert.NotEqual(Guid.Empty, appointment.Confirmation!.EventId);
        Assert.Equal(appointment.Id, appointment.Confirmation.AppointmentId);
        Assert.Equal(appointment.CreatedAtUtc, appointment.Confirmation.OccurredAtUtc);
        Assert.Empty(typeof(Appointment).GetConstructors());
        Assert.All(typeof(Appointment).GetProperties(), property => Assert.False(property.SetMethod?.IsPublic ?? false));
    }

    [Fact]
    public void ARCH01_Aggregate_rejects_missing_interval() =>
        Assert.Throws<ArgumentNullException>(() => Appointment.Confirm(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null!, Start));
}
