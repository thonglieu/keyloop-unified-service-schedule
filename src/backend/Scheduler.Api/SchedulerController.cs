using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scheduler.Application;

namespace Scheduler.Api;

[ApiController]
[Authorize(Policy = "Customer")]
[Route("api")]
public class SchedulerController(BookAppointmentHandler book, CheckAvailabilityHandler availability, AppointmentQueries appointments, ReferenceQueries references, IWebHostEnvironment environment, IConfiguration configuration) : ControllerBase
{
    [HttpGet("dealerships")]
    public async Task<IActionResult> Dealerships(CancellationToken ct) => Ok(await references.Dealerships(ct));
    [HttpGet("service-types")]
    public async Task<IActionResult> ServiceTypes(CancellationToken ct) => Ok(await references.ServiceTypes(ct));
    [Authorize(Policy = "Admin")]
    [HttpGet("customers")]
    public async Task<IActionResult> Customers(CancellationToken ct) => Ok(await references.Customers(ct));
    [HttpGet("customers/{id:guid}/vehicles")]
    public async Task<IActionResult> Vehicles(Guid id, CancellationToken ct) => Ok(await references.Vehicles(id, ct));
    [HttpGet("availability")]
    public Task<AvailabilityDto> Availability([FromQuery] Guid dealershipId, [FromQuery] Guid serviceTypeId, [FromQuery] string startsAt, CancellationToken ct) => availability.Handle(dealershipId, serviceTypeId, startsAt, ct);
    [HttpPost("appointments")]
    public async Task<IActionResult> Book([FromBody] BookingRequest request, CancellationToken ct, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null)
    {
        if (Request.Headers.TryGetValue("Idempotency-Key", out var keys))
        {
            if (keys.Count != 1) throw new BookingException(400, "Invalid request.", new() { ["idempotencyKey"] = ["Supply exactly one Idempotency-Key header value."] });
            // Read the raw value: model binding can turn an empty header into null.
            idempotencyKey = keys[0] ?? "";
            BookingIdempotency.ValidateKey(idempotencyKey);
        }
        var result = await book.Handle(request, ct, idempotencyKey);
        return CreatedAtAction(nameof(Appointment), new { id = result.Id }, result);
    }
    [HttpGet("appointments/{id:guid}")]
    public Task<AppointmentDto> Appointment(Guid id, CancellationToken ct) => appointments.Get(id, ct);
    [HttpGet("appointments")]
    public async Task<IActionResult> Appointments([FromQuery] Guid customerId, CancellationToken ct) => Ok(await appointments.List(customerId, ct));
    [Authorize(Policy = "Admin")]
    [HttpGet("demo/fixtures")]
    public async Task<IActionResult> Fixtures(CancellationToken ct)
    {
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>("Demo:Enabled")) throw new BookingException(404, "Resource not found.");
        return Ok(await references.Fixtures(ct) ?? throw new BookingException(404, "Resource not found."));
    }
}
