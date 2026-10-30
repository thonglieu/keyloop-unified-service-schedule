namespace Scheduler.Domain;

public class Customer { public Guid Id { get; set; } public string Name { get; set; } = ""; }
public class Vehicle { public Guid Id { get; set; } public Guid CustomerId { get; set; } public string Registration { get; set; } = ""; public string Make { get; set; } = ""; public string Model { get; set; } = ""; }
public class Dealership { public Guid Id { get; set; } public string Name { get; set; } = ""; public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh"; }
public class ServiceType { public Guid Id { get; set; } public string Name { get; set; } = ""; public int DurationMinutes { get; set; } }
public class Technician { public Guid Id { get; set; } public Guid DealershipId { get; set; } public string Name { get; set; } = ""; }
public class ServiceBay { public Guid Id { get; set; } public Guid DealershipId { get; set; } public string Name { get; set; } = ""; }
public class TechnicianServiceType { public Guid TechnicianId { get; set; } public Guid ServiceTypeId { get; set; } }
public class TechnicianAvailability { public Guid Id { get; set; } public Guid TechnicianId { get; set; } public DateTimeOffset StartsAtUtc { get; set; } public DateTimeOffset EndsAtUtc { get; set; } }
public class ServiceBayAvailability { public Guid Id { get; set; } public Guid ServiceBayId { get; set; } public DateTimeOffset StartsAtUtc { get; set; } public DateTimeOffset EndsAtUtc { get; set; } }
public class DemoFixture { public Guid Id { get; set; } public DateTimeOffset SeededAtUtc { get; set; } public string Json { get; set; } = ""; }
