using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Scheduler.Application;
using Scheduler.Domain;
using Xunit;

namespace Scheduler.IntegrationTests;

public sealed class OwnershipHttpTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    [Fact]
    public async Task SEC02_A01_A03_Customer_books_reads_and_lists_only_own_persisted_appointments()
    {
        var s = await database.CreateScenario(); var token = await CustomerToken(s.Customers[0]);
        var response = await Send(HttpMethod.Post, "/api/appointments", token, s.Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var appointment = (await response.Content.ReadFromJsonAsync<AppointmentDto>())!;
        var read = await Send(HttpMethod.Get, $"/api/appointments/{appointment.Id}", token);
        Assert.Equal(appointment, await read.Content.ReadFromJsonAsync<AppointmentDto>());
        var list = await Send(HttpMethod.Get, $"/api/appointments?customerId={s.Customers[0]}", token);
        Assert.Equal(appointment, Assert.Single((await list.Content.ReadFromJsonAsync<AppointmentDto[]>())!));
        await using var db = database.OpenContext();
        Assert.True(await db.Appointments.AnyAsync(a => a.Id == appointment.Id && a.CustomerId == s.Customers[0]));
    }

    [Fact]
    public async Task SEC02_Foreign_appointment_and_vehicle_collection_are_indistinguishable_from_missing()
    {
        var s = await database.CreateScenario(); var token = await CustomerToken(s.Customers[0]);
        var booked = await database.Client.PostAsJsonAsync("/api/appointments", s.Request(1));
        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        var appointment = (await booked.Content.ReadFromJsonAsync<AppointmentDto>())!;
        foreach (var paths in new[]
        {
            new[] { $"/api/appointments/{appointment.Id}", $"/api/appointments/{Guid.NewGuid()}" },
            new[] { $"/api/customers/{s.Customers[1]}/vehicles", $"/api/customers/{Guid.NewGuid()}/vehicles" }
        })
        {
            var foreign = await ProblemAssertions.Read(await Send(HttpMethod.Get, paths[0], token), HttpStatusCode.NotFound, "not_found");
            var missing = await ProblemAssertions.Read(await Send(HttpMethod.Get, paths[1], token), HttpStatusCode.NotFound, "not_found");
            ProblemAssertions.SameConcealedResource(foreign, missing);
        }
    }

    [Fact]
    public async Task SEC02_Foreign_customer_booking_and_list_filter_are_forbidden_without_writes()
    {
        var s = await database.CreateScenario(); var token = await CustomerToken(s.Customers[0]);
        await ProblemAssertions.Read(await Send(HttpMethod.Post, "/api/appointments", token, s.Request(1)), HttpStatusCode.Forbidden, "forbidden");
        await ProblemAssertions.Read(await Send(HttpMethod.Get, $"/api/appointments?customerId={s.Customers[1]}", token), HttpStatusCode.Forbidden, "forbidden");
        await using var db = database.OpenContext(); Assert.False(await db.Appointments.AnyAsync(a => a.DealershipId == s.DealershipId));
    }

    [Fact]
    public async Task SEC02_Foreign_and_unknown_vehicle_booking_are_equivalent_404_without_writes()
    {
        var s = await database.CreateScenario(); var token = await CustomerToken(s.Customers[0]);
        object Body(Guid vehicle) => new { customerId = s.Customers[0], vehicleId = vehicle, dealershipId = s.DealershipId, serviceTypeId = s.ServiceTypeId, startsAt = s.Start.ToString("O") };
        var foreign = await ProblemAssertions.Read(await Send(HttpMethod.Post, "/api/appointments", token, Body(s.Vehicles[1])), HttpStatusCode.NotFound, "not_found");
        var absent = await ProblemAssertions.Read(await Send(HttpMethod.Post, "/api/appointments", token, Body(Guid.NewGuid())), HttpStatusCode.NotFound, "not_found");
        ProblemAssertions.SameConcealedResource(foreign, absent);
        await using var db = database.OpenContext(); Assert.False(await db.Appointments.AnyAsync(a => a.DealershipId == s.DealershipId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SEC02_Admin_and_dual_role_require_no_customer_mapping_and_can_choose_customer(bool dual)
    {
        var s = await database.CreateScenario();
        var token = database.Factory.Tokens.Issue(subject: Guid.NewGuid().ToString(), roles: dual ? ["Customer", "Admin"] : ["Admin"]);
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/appointments", token, s.Request(1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/customers", token)).StatusCode);
    }

    [Fact]
    public async Task SEC02_Unmapped_customer_can_read_reference_data_but_not_customer_records()
    {
        var s = await database.CreateScenario();
        var token = database.Factory.Tokens.Issue(subject: Guid.NewGuid().ToString(), roles: ["Customer"]);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/dealerships", token)).StatusCode);
        await ProblemAssertions.Read(await Send(HttpMethod.Post, "/api/appointments", token, s.Request()), HttpStatusCode.Forbidden, "forbidden");
        await ProblemAssertions.Read(await Send(HttpMethod.Get, $"/api/appointments?customerId={s.Customers[0]}", token), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task SEC02_Identity_subject_is_case_sensitive_and_duplicate_issuer_subject_is_rejected()
    {
        var s = await database.CreateScenario(); var subject = "CaseSensitive-" + Guid.NewGuid();
        await AddLink(s.Customers[0], subject);
        var token = database.Factory.Tokens.Issue(subject: subject.ToLowerInvariant(), roles: ["Customer"]);
        await ProblemAssertions.Read(await Send(HttpMethod.Post, "/api/appointments", token, s.Request()), HttpStatusCode.Forbidden, "forbidden");
        await using var db = database.OpenContext();
        db.Add(new CustomerIdentityLink { Id = Guid.NewGuid(), Issuer = SyntheticJwt.Issuer, Subject = subject, CustomerId = s.Customers[1] });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }

    private async Task<string> CustomerToken(Guid customer)
    {
        var subject = "customer-" + Guid.NewGuid(); await AddLink(customer, subject);
        return database.Factory.Tokens.Issue(subject: subject, roles: ["Customer"]);
    }
    private async Task AddLink(Guid customer, string subject)
    {
        await using var db = database.OpenContext();
        db.Add(new CustomerIdentityLink { Id = Guid.NewGuid(), Issuer = SyntheticJwt.Issuer, Subject = subject, CustomerId = customer });
        await db.SaveChangesAsync();
    }
    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string token, object? body = null)
    {
        using var request = SyntheticJwt.Request(method, path, token, body is null ? null : JsonContent.Create(body));
        return await database.Client.SendAsync(request);
    }
}
