using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TeamLocum.Web.Data;
using TeamLocum.Web.Domain;

namespace TeamLocum.Web.Services;

/// <summary>Doctors, hospitals and user accounts: who may see whom, approvals, and each party's own profile.</summary>
public sealed class DirectoryService(ApplicationDbContext db, UserManager<ApplicationUser> users, ClinicClock clock, ILogger<DirectoryService> logger)
{
    // ───────────── doctors ─────────────

    public async Task<IReadOnlyList<LocumRow>> ListLocumsAsync(Actor actor, string? search, LocumStatus? status, CancellationToken ct)
    {
        if (!actor.IsAdmin && !(actor.IsClient && actor.ClientStatus == ClientStatus.Accepted))
        {
            throw new ForbiddenException("Only administrators and approved hospitals can browse doctors.");
        }

        var locums = db.Locums.AsNoTracking().Include(l => l.User).AsQueryable();

        // Hospitals only ever see doctors who are approved to work.
        locums = actor.IsAdmin
            ? status is { } wanted ? locums.Where(l => l.Status == wanted) : locums
            : locums.Where(l => l.Status == LocumStatus.Active);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            locums = locums.Where(l =>
                EF.Functions.Like(l.Specialties, pattern, "\\") ||
                EF.Functions.Like(l.BaseLocation, pattern, "\\") ||
                EF.Functions.Like(l.User!.LastName, pattern, "\\"));
        }

        var rows = await locums.OrderBy(l => l.User!.LastName).ThenBy(l => l.User!.FirstName).ThenBy(l => l.Id).ToListAsync(ct);
        return rows.Select(l => ToRow(actor, l)).ToList();
    }

    public async Task<LocumRow> GetLocumAsync(Actor actor, int id, CancellationToken ct)
    {
        var locum = await db.Locums.AsNoTracking().Include(l => l.User).SingleOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("That doctor was not found.");

        var allowed = actor.IsAdmin
            || (actor.IsLocum && actor.LocumId == locum.Id)
            || (actor.IsClient && actor.ClientStatus == ClientStatus.Accepted && locum.Status == LocumStatus.Active);

        return allowed ? ToRow(actor, locum) : throw new NotFoundException("That doctor was not found.");
    }

    public async Task ChangeLocumStatusAsync(Actor actor, int id, LocumStatus next, CancellationToken ct)
    {
        RequireAdmin(actor);
        var locum = await db.Locums.SingleOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("That doctor was not found.");
        locum.MoveTo(next);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Doctor {LocumId} moved to {Status} by administrator {AdminId}", id, next, actor.UserId);
    }

    public async Task<LocumProfileInput> GetMyLocumProfileAsync(Actor actor, CancellationToken ct)
    {
        var locum = await MyLocumAsync(actor, track: false, ct);
        return new LocumProfileInput
        {
            FirstName = locum.User!.FirstName,
            LastName = locum.User.LastName,
            Specialties = locum.Specialties,
            BaseLocation = locum.BaseLocation,
            MinRatePerHour = locum.MinRatePerHour,
            YearsExperience = locum.YearsExperience
        };
    }

    public async Task UpdateMyLocumProfileAsync(Actor actor, LocumProfileInput input, CancellationToken ct)
    {
        var locum = await MyLocumAsync(actor, track: true, ct);
        locum.User!.FirstName = input.FirstName.Trim();
        locum.User.LastName = input.LastName.Trim();
        locum.Specialties = string.Join(", ", input.Specialties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        locum.BaseLocation = input.BaseLocation.Trim();
        locum.MinRatePerHour = input.MinRatePerHour;
        locum.YearsExperience = input.YearsExperience;
        await db.SaveChangesAsync(ct);
    }

    // ───────────── hospitals ─────────────

    public async Task<IReadOnlyList<ClientRow>> ListClientsAsync(Actor actor, ClientStatus? status, CancellationToken ct)
    {
        RequireAdmin(actor);
        var clients = db.Clients.AsNoTracking().Include(c => c.User).AsQueryable();
        if (status is { } wanted)
        {
            clients = clients.Where(c => c.Status == wanted);
        }

        var rows = await clients.OrderBy(c => c.CompanyName).ThenBy(c => c.Id).ToListAsync(ct);
        return rows.Select(c => new ClientRow(
            c.Id, c.CompanyName, c.User?.FullName ?? string.Empty, c.User?.Email ?? string.Empty, c.CarasId, c.Status, c.CreatedAt)).ToList();
    }

    public async Task ChangeClientStatusAsync(Actor actor, int id, ClientStatus next, CancellationToken ct)
    {
        RequireAdmin(actor);
        var client = await db.Clients.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("That hospital was not found.");
        client.MoveTo(next);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Hospital {ClientId} moved to {Status} by administrator {AdminId}", id, next, actor.UserId);
    }

    public async Task<ClientProfileInput> GetMyClientProfileAsync(Actor actor, CancellationToken ct)
    {
        var client = await MyClientAsync(actor, track: false, ct);
        return new ClientProfileInput
        {
            FirstName = client.User!.FirstName,
            LastName = client.User.LastName,
            CompanyName = client.CompanyName,
            CarasId = client.CarasId
        };
    }

    public async Task UpdateMyClientProfileAsync(Actor actor, ClientProfileInput input, CancellationToken ct)
    {
        var client = await MyClientAsync(actor, track: true, ct);
        client.User!.FirstName = input.FirstName.Trim();
        client.User.LastName = input.LastName.Trim();
        client.CompanyName = input.CompanyName.Trim();
        client.CarasId = string.IsNullOrWhiteSpace(input.CarasId) ? null : input.CarasId.Trim();
        await db.SaveChangesAsync(ct);
    }

    // ───────────── accounts and dashboard ─────────────

    public async Task<IReadOnlyList<UserRow>> ListUsersAsync(Actor actor, CancellationToken ct)
    {
        RequireAdmin(actor);
        var now = clock.UtcNow;

        var roleNames = await (from userRole in db.UserRoles
                               join role in db.Roles on userRole.RoleId equals role.Id
                               select new { userRole.UserId, role.Name }).ToListAsync(ct);
        var rolesByUser = roleNames.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(r => r.Name).Order()));

        var accounts = await db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync(ct);
        return accounts.Select(u => new UserRow(
            u.Id,
            u.Email ?? string.Empty,
            u.FullName,
            rolesByUser.GetValueOrDefault(u.Id, "none"),
            u.LockoutEnd is { } end && end > now,
            u.LockoutEnd)).ToList();
    }

    public async Task UnlockUserAsync(Actor actor, string userId, CancellationToken ct)
    {
        RequireAdmin(actor);
        var user = await users.FindByIdAsync(userId) ?? throw new NotFoundException("That account was not found.");
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        logger.LogInformation("Account {UserId} unlocked by administrator {AdminId}", userId, actor.UserId);
    }

    public async Task<IReadOnlyList<DashboardItem>> DashboardAsync(Actor actor, CancellationToken ct)
    {
        var now = clock.Now;

        if (actor.IsAdmin)
        {
            return
            [
                new("Hospitals awaiting approval", await db.Clients.CountAsync(c => c.Status == ClientStatus.AwaitingReview, ct), "Clients", "Index"),
                new("Doctors awaiting approval", await db.Locums.CountAsync(l => l.Status == LocumStatus.PendingReview, ct), "Locums", "Index"),
                new("Open shifts", await db.Bookings.CountAsync(b => b.Status == BookingStatus.Open && b.StartsAt > now, ct), "Bookings", "Index"),
                new("Filled upcoming shifts", await db.Bookings.CountAsync(b => b.Status == BookingStatus.Filled && b.StartsAt > now, ct), "Bookings", "Index")
            ];
        }

        if (actor.IsClient && actor.ClientId is { } clientId)
        {
            return
            [
                new("Open shifts", await db.Bookings.CountAsync(b => b.ClientId == clientId && b.Status == BookingStatus.Open && b.StartsAt > now, ct), "Bookings", "Index"),
                new("Filled upcoming shifts", await db.Bookings.CountAsync(b => b.ClientId == clientId && b.Status == BookingStatus.Filled && b.StartsAt > now, ct), "Bookings", "Index"),
                new("Applications to review", await db.ShiftApplications.CountAsync(a => a.Booking!.ClientId == clientId && a.Status == ApplicationStatus.Pending && a.Booking.Status == BookingStatus.Open, ct), "Bookings", "Index")
            ];
        }

        if (actor.IsLocum && actor.LocumId is { } locumId)
        {
            return
            [
                new("Open shifts to apply for", actor.CanWork
                    ? await db.Bookings.CountAsync(b => b.Status == BookingStatus.Open && b.StartsAt > now, ct)
                    : 0, "Bookings", "Index"),
                new("My upcoming shifts", await db.Bookings.CountAsync(b => b.LocumId == locumId && b.Status == BookingStatus.Filled && b.StartsAt > now, ct), "Bookings", "Index"),
                new("Applications waiting", await db.ShiftApplications.CountAsync(a => a.LocumId == locumId && a.Status == ApplicationStatus.Pending, ct), "Bookings", "Index")
            ];
        }

        return [];
    }

    // ───────────── helpers ─────────────

    private static LocumRow ToRow(Actor actor, Locum locum) => new(
        locum.Id,
        locum.User?.FullName ?? $"Doctor {locum.Id}",
        actor.IsAdmin ? locum.User?.Email : null,
        actor.IsAdmin || actor.IsClient ? locum.GmcNumber : null,
        locum.SpecialtyList,
        locum.BaseLocation,
        locum.MinRatePerHour,
        locum.YearsExperience,
        locum.Status);

    private static void RequireAdmin(Actor actor)
    {
        if (!actor.IsAdmin)
        {
            throw new ForbiddenException("Only administrators can do that.");
        }
    }

    private async Task<Locum> MyLocumAsync(Actor actor, bool track, CancellationToken ct)
    {
        var query = track ? db.Locums.AsQueryable() : db.Locums.AsNoTracking();
        return actor.LocumId is { } id
            ? await query.Include(l => l.User).SingleOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Profile not found.")
            : throw new ForbiddenException("Only doctors have a doctor profile.");
    }

    private async Task<Client> MyClientAsync(Actor actor, bool track, CancellationToken ct)
    {
        var query = track ? db.Clients.AsQueryable() : db.Clients.AsNoTracking();
        return actor.ClientId is { } id
            ? await query.Include(c => c.User).SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Profile not found.")
            : throw new ForbiddenException("Only hospitals have a hospital profile.");
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
