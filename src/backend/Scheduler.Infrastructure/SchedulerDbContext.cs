using Microsoft.EntityFrameworkCore;
using Scheduler.Domain;

namespace Scheduler.Infrastructure;

public class SchedulerDbContext(DbContextOptions<SchedulerDbContext> options) : DbContext(options)
{
    public DbSet<BookingReceipt> BookingReceipts => Set<BookingReceipt>();
    public DbSet<CustomerIdentityLink> CustomerIdentityLinks => Set<CustomerIdentityLink>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Dealership> Dealerships => Set<Dealership>();
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();
    public DbSet<Technician> Technicians => Set<Technician>();
    public DbSet<ServiceBay> ServiceBays => Set<ServiceBay>();
    public DbSet<TechnicianServiceType> TechnicianServiceTypes => Set<TechnicianServiceType>();
    public DbSet<TechnicianAvailability> TechnicianAvailabilities => Set<TechnicianAvailability>();
    public DbSet<ServiceBayAvailability> ServiceBayAvailabilities => Set<ServiceBayAvailability>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<DemoFixture> DemoFixtures => Set<DemoFixture>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        var receipt = m.Entity<BookingReceipt>();
        receipt.HasKey(x => x.ScopeKey);
        receipt.Property(x => x.ScopeKey).HasMaxLength(64);
        receipt.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
        receipt.Property(x => x.ResponseJson).IsRequired();
        receipt.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Vehicle>().HasAlternateKey(x => new { x.Id, x.CustomerId });
        m.Entity<Vehicle>().HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ServiceType>().ToTable(t => t.HasCheckConstraint("service_positive_duration", "\"DurationMinutes\" > 0"));
        m.Entity<Technician>().HasAlternateKey(x => new { x.Id, x.DealershipId });
        m.Entity<ServiceBay>().HasAlternateKey(x => new { x.Id, x.DealershipId });
        m.Entity<Technician>().HasOne<Dealership>().WithMany().HasForeignKey(x => x.DealershipId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ServiceBay>().HasOne<Dealership>().WithMany().HasForeignKey(x => x.DealershipId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<TechnicianServiceType>().HasKey(x => new { x.TechnicianId, x.ServiceTypeId });
        m.Entity<TechnicianServiceType>().HasOne<Technician>().WithMany().HasForeignKey(x => x.TechnicianId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<TechnicianServiceType>().HasOne<ServiceType>().WithMany().HasForeignKey(x => x.ServiceTypeId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<TechnicianAvailability>().HasOne<Technician>().WithMany().HasForeignKey(x => x.TechnicianId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ServiceBayAvailability>().HasOne<ServiceBay>().WithMany().HasForeignKey(x => x.ServiceBayId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<TechnicianAvailability>().HasIndex(x => new { x.TechnicianId, x.StartsAtUtc, x.EndsAtUtc });
        m.Entity<ServiceBayAvailability>().HasIndex(x => new { x.ServiceBayId, x.StartsAtUtc, x.EndsAtUtc });
        m.Entity<TechnicianAvailability>().ToTable(t => t.HasCheckConstraint("technician_window_positive", "\"EndsAtUtc\" > \"StartsAtUtc\""));
        m.Entity<ServiceBayAvailability>().ToTable(t => t.HasCheckConstraint("bay_window_positive", "\"EndsAtUtc\" > \"StartsAtUtc\""));
        var link = m.Entity<CustomerIdentityLink>();
        link.Property(x => x.Issuer).IsRequired().UseCollation("C");
        link.Property(x => x.Subject).IsRequired().UseCollation("C");
        link.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
        link.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        var a = m.Entity<Appointment>();
        a.Ignore(x => x.Interval);
        a.Ignore(x => x.Confirmation);
        a.ToTable(t => t.HasCheckConstraint("appointment_positive_duration", "\"EndsAtUtc\" > \"StartsAtUtc\""));
        a.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<Vehicle>().WithMany().HasForeignKey(x => new { x.VehicleId, x.CustomerId }).HasPrincipalKey(x => new { x.Id, x.CustomerId }).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<Dealership>().WithMany().HasForeignKey(x => x.DealershipId).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<ServiceType>().WithMany().HasForeignKey(x => x.ServiceTypeId).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<Technician>().WithMany().HasForeignKey(x => new { x.TechnicianId, x.DealershipId }).HasPrincipalKey(x => new { x.Id, x.DealershipId }).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<ServiceBay>().WithMany().HasForeignKey(x => new { x.ServiceBayId, x.DealershipId }).HasPrincipalKey(x => new { x.Id, x.DealershipId }).OnDelete(DeleteBehavior.Restrict);
        a.HasIndex(x => new { x.CustomerId, x.StartsAtUtc });
        m.Entity<DemoFixture>().HasIndex(x => x.SeededAtUtc);
    }
}
