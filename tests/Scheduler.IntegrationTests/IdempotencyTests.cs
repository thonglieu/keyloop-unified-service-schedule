using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application;
using Xunit;

namespace Scheduler.IntegrationTests;

public sealed class IdempotencyTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [Fact]
    public async Task Concurrent_same_key_requests_persist_one_appointment_and_replay_identical_response()
    {
        var scenario = await database.CreateScenario();
        var key = Guid.NewGuid().ToString("N");
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Send(key, scenario.Request())));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var bookings = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<AppointmentDto>()));
        Assert.All(bookings, booking => Assert.Equal(bookings[0], booking));
        Assert.All(responses, response => Assert.Equal(responses[0].Headers.Location, response.Headers.Location));
        await using var db = database.OpenContext();
        Assert.Equal(1, await db.Appointments.CountAsync(a => a.DealershipId == scenario.DealershipId));
        Assert.Equal(1, await db.BookingReceipts.CountAsync(r => r.AppointmentId == bookings[0]!.Id));
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task Reusing_key_for_different_request_is_conflict_without_another_booking()
    {
        var scenario = await database.CreateScenario();
        var key = Guid.NewGuid().ToString("N");
        using var first = await Send(key, scenario.Request());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await Send(key, scenario.Request(start: scenario.Start.AddHours(1)));
        await ProblemAssertions.Read(second, HttpStatusCode.Conflict, "idempotency_conflict");
        await using var db = database.OpenContext();
        Assert.Equal(1, await db.Appointments.CountAsync(a => a.DealershipId == scenario.DealershipId));
    }

    [Fact]
    public async Task Same_client_key_is_scoped_to_verified_identity()
    {
        var scenario = await database.CreateScenario();
        var key = Guid.NewGuid().ToString("N");
        using var first = await Send(key, scenario.Request(), "admin-one");
        using var second = await Send(key, scenario.Request(start: scenario.Start.AddHours(1)), "admin-two");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstBooking = await first.Content.ReadFromJsonAsync<AppointmentDto>();
        var secondBooking = await second.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotEqual(firstBooking!.Id, secondBooking!.Id);
    }

    [Fact]
    public async Task Receipt_and_appointment_roll_back_together_without_commit()
    {
        var scenario = await database.CreateScenario();
        var appointment = scenario.Row();
        var receipt = new BookingIdempotency(Guid.NewGuid().ToString("N"), "synthetic-fingerprint");
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IBookingTransactionFactory>();
        await using (var transaction = await factory.Begin(default))
        {
            Assert.Null(await transaction.LockAndReadIdempotency(receipt.ScopeKey, default));
            await transaction.Save(appointment, default);
            await transaction.RecordIdempotency(receipt, AppointmentDto.From(appointment), default);
        }
        await using var db = database.OpenContext();
        Assert.False(await db.Appointments.AnyAsync(a => a.Id == appointment.Id));
        Assert.False(await db.BookingReceipts.AnyAsync(r => r.ScopeKey == receipt.ScopeKey));
    }

    private async Task<HttpResponseMessage> Send(string key, object body, string subject = "synthetic-admin")
    {
        using var request = SyntheticJwt.Request(HttpMethod.Post, "/api/appointments",
            database.Factory.Tokens.Issue(subject: subject), JsonContent.Create(body));
        request.Headers.Add("Idempotency-Key", key);
        return await database.Client.SendAsync(request);
    }
}
