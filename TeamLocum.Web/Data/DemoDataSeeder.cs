using Microsoft.AspNetCore.Identity;
using TeamLocum.Web.Domain;
using TeamLocum.Web.Services;

namespace TeamLocum.Web.Data;

/// <summary>Development-only demo content. Everything goes through the domain methods, so the seed obeys the same rules as users.</summary>
internal sealed class DemoDataSeeder(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    ClinicClock clock,
    ILogger logger)
{
    private sealed record HospitalSeed(string Email, string Company, string First, string Last);

    private sealed record DoctorSeed(
        string Email, string First, string Last, string Gmc, string Specialties, string Base, decimal MinRate, int Years);

    private static readonly HospitalSeed[] Hospitals =
    [
        new(DemoCredentials.HospitalEmail, "City General Hospital", "Sarah", "Thompson"),
        new("queens.medical@example.com", "Queen's Medical Centre", "James", "Patel"),
        new("north.clinic@example.com", "North Community Clinic", "Emma", "Williams")
    ];

    private static readonly DoctorSeed[] Doctors =
    [
        new(DemoCredentials.DoctorEmail, "Rajesh", "Sharma", "0000001", "General practice, Urgent care", "Leicester", 60m, 12),
        new("dr.jones@example.com", "Emily", "Jones", "0000002", "Paediatrics", "Nottingham", 70m, 8),
        new("dr.khan@example.com", "Omar", "Khan", "0000003", "Emergency medicine", "Leicester", 75m, 10),
        new("dr.patel@example.com", "Priya", "Patel", "0000004", "General practice", "Derby", 55m, 5),
        new("dr.chen@example.com", "Wei", "Chen", "0000005", "Anaesthetics, Intensive care", "Birmingham", 80m, 15)
    ];

    public async Task SeedAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;

        await CreateUserAsync(DemoCredentials.AdminEmail, DemoCredentials.AdminPassword, "Demo", "Admin", Roles.Admin);

        var clients = new List<Client>();
        foreach (var h in Hospitals)
        {
            var user = await CreateUserAsync(h.Email, DemoCredentials.HospitalPassword, h.First, h.Last, Roles.Client);
            var client = new Client { UserId = user.Id, CompanyName = h.Company, CreatedAt = now };
            client.MoveTo(ClientStatus.Accepted);
            clients.Add(client);
        }

        var locums = new List<Locum>();
        foreach (var d in Doctors)
        {
            var user = await CreateUserAsync(d.Email, DemoCredentials.DoctorPassword, d.First, d.Last, Roles.Locum);
            var locum = new Locum
            {
                UserId = user.Id,
                GmcNumber = d.Gmc,
                Specialties = d.Specialties,
                BaseLocation = d.Base,
                MinRatePerHour = d.MinRate,
                YearsExperience = d.Years,
                CreatedAt = now
            };
            locum.MoveTo(LocumStatus.Active);
            locums.Add(locum);
        }

        db.Clients.AddRange(clients);
        db.Locums.AddRange(locums);
        await db.SaveChangesAsync(ct);

        var city = clients[0];
        var queens = clients[1];
        var north = clients[2];
        var today = clock.Now.Date;

        // A finished shift, a filled shift, and three open shifts (one overnight) that doctors can apply for.
        var past = NewShift(city, today.AddDays(-3).AddHours(9), today.AddDays(-3).AddHours(17), 85m,
            "City General Hospital, Leicester", "General practice", "Surgery cover", now);
        past.Fill(locums[0].Id);
        past.Complete(clock.Now);

        var filled = NewShift(city, today.AddDays(1).AddHours(9), today.AddDays(1).AddHours(17), 85m,
            "City General Hospital, Leicester", "Paediatrics", "Paediatrics ward cover", now);
        filled.Fill(locums[1].Id);

        var emergency = NewShift(city, today.AddDays(3).AddHours(8), today.AddDays(3).AddHours(16), 90m,
            "City General Hospital, Leicester", "Emergency medicine", "Emergency department cover", now);
        var gp = NewShift(north, today.AddDays(5).AddHours(18), today.AddDays(5).AddHours(22), 70m,
            "North Community Clinic, Derby", "General practice", "Evening out-of-hours session", now);
        var night = NewShift(queens, today.AddDays(7).AddHours(22), today.AddDays(8).AddHours(6), 95m,
            "Queen's Medical Centre, Nottingham", "Anaesthetics", "Overnight theatre list", now);

        db.Bookings.AddRange(past, filled, emergency, gp, night);
        await db.SaveChangesAsync(ct);

        db.ShiftApplications.AddRange(
            new ShiftApplication { BookingId = emergency.Id, LocumId = locums[2].Id, Message = "Available all day.", CreatedAt = now },
            new ShiftApplication { BookingId = gp.Id, LocumId = locums[3].Id, CreatedAt = now },
            new ShiftApplication { BookingId = gp.Id, LocumId = locums[0].Id, CreatedAt = now });

        db.Holidays.AddRange(BankHolidays.Select(h => new Holiday { Date = h.Date, Details = h.Details }));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Inserted demo data (Development only)");
    }

    private static Booking NewShift(
        Client client, DateTime start, DateTime end, decimal rate, string location, string specialty, string notes, DateTimeOffset now)
    {
        var booking = new Booking
        {
            ClientId = client.Id,
            Location = location,
            Specialty = specialty,
            Notes = notes,
            RatePerHour = rate,
            CreatedAt = now
        };
        booking.SetWindow(start, end);
        return booking;
    }

    private async Task<ApplicationUser> CreateUserAsync(string email, string password, string first, string last, string role)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = first,
            LastName = last,
            CreatedAt = clock.UtcNow
        };

        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException($"Demo user {email} could not be created: " +
                string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        await users.AddToRoleAsync(user, role);
        return user;
    }

    /// <summary>England and Wales bank holidays, including substitute days.</summary>
    internal static readonly (DateOnly Date, string Details)[] BankHolidays =
    [
        (new DateOnly(2026, 12, 25), "Christmas Day"),
        (new DateOnly(2026, 12, 28), "Boxing Day (substitute day)"),
        (new DateOnly(2027, 1, 1), "New Year's Day"),
        (new DateOnly(2027, 3, 26), "Good Friday"),
        (new DateOnly(2027, 3, 29), "Easter Monday"),
        (new DateOnly(2027, 5, 3), "Early May bank holiday"),
        (new DateOnly(2027, 5, 31), "Spring bank holiday"),
        (new DateOnly(2027, 8, 30), "Summer bank holiday"),
        (new DateOnly(2027, 12, 27), "Christmas Day (substitute day)"),
        (new DateOnly(2027, 12, 28), "Boxing Day (substitute day)")
    ];
}
