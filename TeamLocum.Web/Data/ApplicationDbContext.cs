using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TeamLocum.Web.Domain;

namespace TeamLocum.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Locum> Locums => Set<Locum>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<ShiftApplication> ShiftApplications => Set<ShiftApplication>();
    public DbSet<Holiday> Holidays => Set<Holiday>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // SQLite stores decimals as text, which cannot be ordered or compared reliably. Money is kept as whole pence.
        configurationBuilder.Properties<decimal>().HaveConversion<DecimalToPenceConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.FirstName).HasMaxLength(60).IsRequired();
            user.Property(u => u.LastName).HasMaxLength(60).IsRequired();
            user.Property(u => u.CreatedAt).HasConversion<UnixMillisecondsConverter>();
            user.Ignore(u => u.FullName);
        });

        builder.Entity<Client>(client =>
        {
            client.ToTable("Clients");
            client.Property(c => c.CompanyName).HasMaxLength(120).IsRequired();
            client.Property(c => c.CarasId).HasMaxLength(40);
            client.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            client.Property(c => c.CreatedAt).HasConversion<UnixMillisecondsConverter>();
            client.Ignore(c => c.CanPostShifts);
            client.HasIndex(c => c.UserId).IsUnique();
            client.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Locum>(locum =>
        {
            locum.ToTable("Locums", table =>
            {
                table.HasCheckConstraint("CK_Locums_YearsExperience", "\"YearsExperience\" BETWEEN 0 AND 60");
                table.HasCheckConstraint("CK_Locums_MinRatePerHour", "\"MinRatePerHour\" >= 0");
            });
            locum.Property(l => l.GmcNumber).HasMaxLength(Locum.GmcNumberLength).IsRequired();
            locum.Property(l => l.Specialties).HasMaxLength(200).IsRequired();
            locum.Property(l => l.BaseLocation).HasMaxLength(80).IsRequired();
            locum.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
            locum.Property(l => l.CreatedAt).HasConversion<UnixMillisecondsConverter>();
            locum.Ignore(l => l.CanWork);
            locum.Ignore(l => l.SpecialtyList);
            locum.HasIndex(l => l.UserId).IsUnique();
            locum.HasIndex(l => l.GmcNumber).IsUnique();
            locum.HasOne(l => l.User).WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Booking>(booking =>
        {
            booking.ToTable("Bookings", table =>
            {
                table.HasCheckConstraint("CK_Bookings_Window", "datetime(\"EndsAt\") > datetime(\"StartsAt\")");
                table.HasCheckConstraint("CK_Bookings_Rate", "\"RatePerHour\" > 0");
                // A filled or completed shift always has a doctor; open and cancelled-before-fill shifts never need one.
                table.HasCheckConstraint("CK_Bookings_FilledHasLocum", "\"Status\" NOT IN ('Filled', 'Completed') OR \"LocumId\" IS NOT NULL");
                table.HasCheckConstraint("CK_Bookings_OpenHasNoLocum", "\"Status\" <> 'Open' OR \"LocumId\" IS NULL");
            });
            booking.Property(b => b.Location).HasMaxLength(120).IsRequired();
            booking.Property(b => b.Specialty).HasMaxLength(80);
            booking.Property(b => b.Notes).HasMaxLength(1000);
            booking.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
            booking.Property(b => b.CreatedAt).HasConversion<UnixMillisecondsConverter>();
            booking.Property(b => b.ConcurrencyStamp).IsConcurrencyToken();
            booking.Ignore(b => b.Duration);
            booking.Ignore(b => b.TotalPay);
            booking.Ignore(b => b.IsLive);
            booking.HasIndex(b => new { b.ClientId, b.StartsAt });
            booking.HasIndex(b => new { b.LocumId, b.StartsAt });
            booking.HasIndex(b => new { b.Status, b.StartsAt });
            booking.HasOne(b => b.Client).WithMany().HasForeignKey(b => b.ClientId).OnDelete(DeleteBehavior.Restrict);
            booking.HasOne(b => b.Locum).WithMany().HasForeignKey(b => b.LocumId).OnDelete(DeleteBehavior.Restrict);
            booking.HasMany(b => b.Applications).WithOne(a => a.Booking).HasForeignKey(a => a.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ShiftApplication>(application =>
        {
            application.ToTable("ShiftApplications");
            application.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            application.Property(a => a.Message).HasMaxLength(500);
            application.Property(a => a.CreatedAt).HasConversion<UnixMillisecondsConverter>();
            application.HasIndex(a => new { a.BookingId, a.LocumId }).IsUnique();
            application.HasOne(a => a.Locum).WithMany().HasForeignKey(a => a.LocumId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Holiday>(holiday =>
        {
            holiday.ToTable("Holidays");
            holiday.Property(h => h.Details).HasMaxLength(80).IsRequired();
            holiday.HasIndex(h => h.Date).IsUnique();
        });
    }
}

internal sealed class DecimalToPenceConverter() : ValueConverter<decimal, long>(
    value => (long)decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero),
    value => value / 100m);

internal sealed class UnixMillisecondsConverter() : ValueConverter<DateTimeOffset, long>(
    value => value.ToUnixTimeMilliseconds(),
    value => DateTimeOffset.FromUnixTimeMilliseconds(value));
