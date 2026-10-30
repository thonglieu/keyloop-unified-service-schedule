using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Scheduler.Application;
using Scheduler.Domain;
using Xunit;

namespace Scheduler.IntegrationTests;

public sealed class BookingTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private HttpClient Client => database.Client;

    [Fact]
    public async Task AC01_03_08_09_Booking_persists_all_associations_across_a_new_host()
    {
        var s = await database.CreateScenario();
        var available = await Client.GetFromJsonAsync<AvailabilityDto>(s.Availability());
        Assert.True(available!.Available);
        Assert.Equal(s.Start.AddMinutes(60), available.EndsAt);
        var response = await Client.PostAsJsonAsync("/api/appointments", s.Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = (await response.Content.ReadFromJsonAsync<AppointmentDto>())!;
        Assert.Equal($"/api/appointments/{booking.Id}", new Uri(Client.BaseAddress!, response.Headers.Location!).AbsolutePath);
        Assert.Equal(s.Customers[0], booking.CustomerId);
        Assert.Equal(s.Vehicles[0], booking.VehicleId);
        Assert.Equal(s.DealershipId, booking.DealershipId);
        Assert.Equal(s.ServiceTypeId, booking.ServiceTypeId);
        Assert.Equal(s.Technicians.Single(), booking.TechnicianId);
        Assert.Equal(s.Bays.Single(), booking.ServiceBayId);
        Assert.Equal(s.Start, booking.StartsAt);
        Assert.Equal(s.Start.AddMinutes(60), booking.EndsAt);
        Assert.Equal(TestDatabase.Now, booking.CreatedAt);
        await using var newHost = new SchedulerFactory(database.ConnectionString);
        using var client = newHost.CreateClient();
        var read = await client.GetFromJsonAsync<AppointmentDto>($"/api/appointments/{booking.Id}");
        Assert.Equal(booking, read);
        await using var db = database.OpenContext();
        var stored = await db.Set<Appointment>().SingleAsync(a => a.Id == booking.Id);
        Assert.Equal(booking, AppointmentDto.From(stored));
    }

    [Theory]
    [InlineData("customerId", 404)]
    [InlineData("vehicleId", 404)]
    [InlineData("dealershipId", 404)]
    [InlineData("serviceTypeId", 404)]
    [InlineData("ownership", 400)]
    [InlineData("past", 400)]
    [InlineData("offsetless", 400)]
    [InlineData("invalidDate", 400)]
    [InlineData("emptyId", 400)]
    [InlineData("missingVehicle", 400)]
    [InlineData("overrideEnd", 400)]
    [InlineData("overrideTechnician", 400)]
    [InlineData("overrideBay", 400)]
    [InlineData("overrideDuration", 400)]
    [InlineData("overflow", 400)]
    public async Task AC01_02_14_Invalid_requests_return_problem_details_and_do_not_write(string problem, int status)
    {
        var s = await database.CreateScenario();
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(s.Request()))!;
        void Set(string name, object value) => body[name] = JsonSerializer.SerializeToElement(value);
        switch (problem)
        {
            case "ownership": Set("vehicleId", s.Vehicles[1]); break;
            case "past": Set("startsAt", TestDatabase.Now.AddMinutes(-1).ToString("O")); break;
            case "offsetless": Set("startsAt", "2030-01-02T09:00:00"); break;
            case "invalidDate": Set("startsAt", "not-a-date+07:00"); break;
            case "emptyId": Set("customerId", Guid.Empty); break;
            case "missingVehicle": body.Remove("vehicleId"); break;
            case "overrideEnd": Set("endsAt", s.Start.AddMinutes(1).ToString("O")); break;
            case "overrideTechnician": Set("technicianId", s.Technicians[0]); break;
            case "overrideBay": Set("serviceBayId", s.Bays[0]); break;
            case "overrideDuration": Set("durationMinutes", 1); break;
            case "overflow": Set("startsAt", "9999-12-31T23:59:00Z"); break;
            default: Set(problem, Guid.NewGuid()); break;
        }
        var response = await Client.PostAsJsonAsync("/api/appointments", body);
        await AssertProblem(response, (HttpStatusCode)status);
        Assert.Equal(0, await Count(s));
    }

    [Fact]
    public async Task AC02_Malformed_json_is_a_400_problem()
    {
        var response = await Client.PostAsync("/api/appointments", new StringContent("{", Encoding.UTF8, "application/json"));
        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("unqualified")]
    [InlineData("noBay")]
    [InlineData("noTechnician")]
    [InlineData("shortTechnicianWindow")]
    [InlineData("shortBayWindow")]
    [InlineData("missingTechnicianWindow")]
    [InlineData("missingBayWindow")]
    [InlineData("closing")]
    [InlineData("beforeOpening")]
    public async Task AC04_05_06_Entire_interval_requires_both_resources_and_opening_hours(string reason)
    {
        var s = await database.CreateScenario(qualified: reason != "unqualified", bays: reason == "noBay" ? 0 : 1,
            technicians: reason == "noTechnician" ? 0 : 1,
            technicianWindowMinutes: reason == "shortTechnicianWindow" ? 90 : 1440,
            bayWindowMinutes: reason == "shortBayWindow" ? 90 : 1440);
        var start = reason switch
        {
            "closing" => s.Start.AddHours(7.5), // 16:30 +07, service ends after 17:00.
            "beforeOpening" => s.Start.AddMinutes(-90),
            _ => s.Start
        };
        if (reason is "missingTechnicianWindow" or "missingBayWindow")
        {
            await using var db = database.OpenContext();
            if (reason == "missingTechnicianWindow")
                db.RemoveRange(await db.TechnicianAvailabilities.Where(a => s.Technicians.Contains(a.TechnicianId)).ToListAsync());
            else db.RemoveRange(await db.ServiceBayAvailabilities.Where(a => s.Bays.Contains(a.ServiceBayId)).ToListAsync());
            await db.SaveChangesAsync();
        }
        var advisory = await Client.GetFromJsonAsync<AvailabilityDto>(s.Availability(start));
        Assert.False(advisory!.Available);
        await AssertProblem(await Client.PostAsJsonAsync("/api/appointments", s.Request(start: start)), HttpStatusCode.Conflict);
        Assert.Equal(0, await Count(s));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AC06_Only_one_resource_overlapping_later_in_the_interval_blocks_booking(bool occupiedBay)
    {
        var s = await database.CreateScenario(technicians: 2, bays: 2);
        await using (var db = database.OpenContext())
        {
            // Remove eligibility for the spare resource, but retain it for the pre-existing booking.
            if (occupiedBay)
                db.RemoveRange(await db.Set<ServiceBayAvailability>().Where(a => a.ServiceBayId == s.Bays[1]).ToListAsync());
            else
                db.RemoveRange(await db.Set<TechnicianAvailability>().Where(a => a.TechnicianId == s.Technicians[1]).ToListAsync());
            db.Add(s.Row(technician: occupiedBay ? 1 : 0, bay: occupiedBay ? 0 : 1, start: s.Start.AddMinutes(30)));
            await db.SaveChangesAsync();
        }
        Assert.False((await Client.GetFromJsonAsync<AvailabilityDto>(s.Availability()))!.Available);
        await AssertProblem(await Client.PostAsJsonAsync("/api/appointments", s.Request()), HttpStatusCode.Conflict);
        Assert.Equal(1, await Count(s));
    }

    [Fact]
    public async Task AC07_Touching_boundaries_allow_the_same_resources()
    {
        var s = await database.CreateScenario(duration: 30);
        var first = await Client.PostAsJsonAsync("/api/appointments", s.Request());
        var second = await Client.PostAsJsonAsync("/api/appointments", s.Request(1, s.Start.AddMinutes(30)));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var a = (await first.Content.ReadFromJsonAsync<AppointmentDto>())!;
        var b = (await second.Content.ReadFromJsonAsync<AppointmentDto>())!;
        Assert.Equal(a.EndsAt, b.StartsAt);
        Assert.Equal(a.TechnicianId, b.TechnicianId);
        Assert.Equal(a.ServiceBayId, b.ServiceBayId);
        Assert.Equal(2, await Count(s));
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 20)]
    public async Task AC10_11_Concurrent_clients_use_all_and_only_available_capacity(int capacity, int requests)
    {
        var s = await database.CreateScenario(technicians: capacity, bays: capacity);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, requests).Select(async i =>
        {
            await gate.Task;
            return await Client.PostAsJsonAsync("/api/appointments", s.Request(i));
        }).ToArray();
        gate.SetResult();
        var responses = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        var unexpectedResponses = await Task.WhenAll(responses
            .Where(r => r.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.Conflict))
            .Select(async r => $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}"));
        Assert.True(unexpectedResponses.Length == 0, string.Join(Environment.NewLine, unexpectedResponses));
        Assert.Equal(capacity, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(requests - capacity, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(capacity, await Count(s));
        await using var db = database.OpenContext();
        var rows = await db.Set<Appointment>().Where(a => a.DealershipId == s.DealershipId).ToListAsync();
        Assert.Equal(capacity, rows.Select(a => a.TechnicianId).Distinct().Count());
        Assert.Equal(capacity, rows.Select(a => a.ServiceBayId).Distinct().Count());
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task AC12_Advisory_availability_does_not_reserve_capacity()
    {
        var s = await database.CreateScenario();
        Assert.True((await Client.GetFromJsonAsync<AvailabilityDto>(s.Availability()))!.Available);
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("/api/appointments", s.Request(1))).StatusCode);
        await AssertProblem(await Client.PostAsJsonAsync("/api/appointments", s.Request()), HttpStatusCode.Conflict);
        Assert.Equal(1, await Count(s));
    }

    [Theory]
    [InlineData(true, "appointment_technician_no_overlap")]
    [InlineData(false, "appointment_bay_no_overlap")]
    public async Task AC13_Postgres_independently_rejects_each_resource_overlap(bool sameTechnician, string constraint)
    {
        var s = await database.CreateScenario(technicians: 2, bays: 2);
        await using (var first = database.OpenContext())
        {
            first.Add(s.Row());
            await first.SaveChangesAsync();
        }
        await using var independentContext = database.OpenContext();
        independentContext.Add(s.Row(technician: sameTechnician ? 0 : 1, bay: sameTechnician ? 1 : 0, start: s.Start.AddMinutes(15)));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => independentContext.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.ExclusionViolation, postgres.SqlState);
        Assert.Equal(constraint, postgres.ConstraintName);
        Assert.Equal(1, await Count(s));
    }

    [Fact]
    public async Task AC14_Equivalent_explicit_offsets_share_the_same_UTC_interval()
    {
        var s = await database.CreateScenario();
        var first = await Client.PostAsJsonAsync("/api/appointments", s.Request(start: s.Start.ToOffset(TimeSpan.FromHours(7))));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var booking = (await first.Content.ReadFromJsonAsync<AppointmentDto>())!;
        Assert.Equal(TimeSpan.Zero, booking.StartsAt.Offset);
        Assert.Equal(s.Start, booking.StartsAt);
        Assert.False((await Client.GetFromJsonAsync<AvailabilityDto>(s.Availability()))!.Available);
        await AssertProblem(await Client.PostAsJsonAsync("/api/appointments", s.Request(1)), HttpStatusCode.Conflict);
        Assert.Equal(1, await Count(s));
    }

    [Fact]
    public async Task AC15_Stored_interval_survives_service_duration_changes()
    {
        var s = await database.CreateScenario(duration: 30);
        var response = await Client.PostAsJsonAsync("/api/appointments", s.Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var original = (await response.Content.ReadFromJsonAsync<AppointmentDto>())!;
        await using (var db = database.OpenContext())
        {
            var service = await db.Set<ServiceType>().SingleAsync(t => t.Id == s.ServiceTypeId);
            service.DurationMinutes = 120;
            await db.SaveChangesAsync();
        }
        var persisted = await Client.GetFromJsonAsync<AppointmentDto>($"/api/appointments/{original.Id}");
        Assert.Equal(original, persisted);
        Assert.Equal(s.Start.AddMinutes(30), persisted!.EndsAt);
    }

    private async Task<int> Count(Scenario s)
    {
        await using var db = database.OpenContext();
        return await db.Set<Appointment>().CountAsync(a => a.DealershipId == s.DealershipId);
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("title").GetString()));
    }
}
