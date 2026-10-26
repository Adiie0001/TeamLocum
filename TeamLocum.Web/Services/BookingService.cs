using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TeamLocum.Web.Data;
using TeamLocum.Web.Domain;
using TeamLocum.Web.Infrastructure.Options;
using TeamLocum.Web.Services.Matching;

namespace TeamLocum.Web.Services;

/// <summary>
/// Everything that happens to a shift. Each method receives the <see cref="Actor"/> and decides what that actor may see
/// and do, so the rules hold no matter which controller, test or future API calls them. A doctor can never hold two
/// overlapping filled shifts: the service checks first for a friendly message, and a database trigger is the backstop
/// for races.
/// </summary>
public sealed class BookingService(
    ApplicationDbContext db,
    ClinicClock clock,
    IShiftMatcher matcher,
    IOptions<MatchingOptions> matching)
{
    // ───────────── reading ─────────────

    public async Task<PagedResult<BookingRow>> ListAsync(Actor actor, BookingListQuery query, CancellationToken ct)
    {
        var now = clock.Now;
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var bookings = Visible(actor, now);

        if (query.Status is { } status)
        {
            bookings = bookings.Where(b => b.Status == status);
        }

        if (query.Scope == BookingScope.Open)
        {
            bookings = bookings.Where(b => b.Status == BookingStatus.Open && b.StartsAt > now);
        }
        else if (query.Scope == BookingScope.Mine && actor.LocumId is { } mine)
        {
            bookings = bookings.Where(b => b.LocumId == mine);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{EscapeLike(query.Search.Trim())}%";
            bookings = bookings.Where(b =>
                EF.Functions.Like(b.Location, pattern, "\\") ||
                (b.Specialty != null && EF.Functions.Like(b.Specialty, pattern, "\\")));
        }

        bookings = query.Past ? bookings.Where(b => b.EndsAt < now) : bookings.Where(b => b.EndsAt >= now);

        var total = await bookings.CountAsync(ct);
        var ordered = query.Past
            ? bookings.OrderByDescending(b => b.StartsAt).ThenBy(b => b.Id)
            : bookings.OrderBy(b => b.StartsAt).ThenBy(b => b.Id);

        var myLocumId = actor.LocumId ?? -1;
        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new
            {
                b.Id,
                b.StartsAt,
                b.EndsAt,
                b.Location,
                b.Specialty,
                b.LocumId,
                ClientName = b.Client!.CompanyName,
                LocumFirst = b.Locum != null ? b.Locum.User!.FirstName : null,
                LocumLast = b.Locum != null ? b.Locum.User!.LastName : null,
                b.RatePerHour,
                b.Status,
                Pending = b.Applications.Count(x => x.Status == ApplicationStatus.Pending),
                Mine = b.Applications.Where(x => x.LocumId == myLocumId)
                    .Select(x => (ApplicationStatus?)x.Status).FirstOrDefault()
            })
            .ToListAsync(ct);

        var holidays = await HolidaysForAsync(rows.Select(r => (r.StartsAt, r.EndsAt)), ct);

        var items = rows.Select(r => new BookingRow(
            r.Id,
            r.StartsAt,
            r.EndsAt,
            r.Location,
            r.Specialty,
            r.ClientName,
            DoctorLabel(actor, r.LocumId, r.LocumFirst, r.LocumLast),
            r.RatePerHour,
            r.Status,
            actor.IsLocum ? 0 : r.Pending,
            actor.IsLocum ? r.Mine : null,
            holidays.FirstOrDefault(h => h.Date >= DateOnly.FromDateTime(r.StartsAt) && h.Date <= DateOnly.FromDateTime(r.EndsAt))?.Details))
            .ToList();

        return new PagedResult<BookingRow>(items, page, pageSize, total);
    }

    public async Task<BookingDetails> GetAsync(Actor actor, int id, CancellationToken ct)
    {
        var now = clock.Now;
        var booking = await Visible(actor, now)
            .Include(b => b.Client)
            .Include(b => b.Locum!).ThenInclude(l => l.User)
            .Include(b => b.Applications).ThenInclude(a => a.Locum!).ThenInclude(l => l.User)
            .SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw NotFound();

        var mine = actor.LocumId is { } locumId
            ? booking.Applications.FirstOrDefault(a => a.LocumId == locumId)
            : null;

        var owner = IsOwner(actor, booking);
        var applicants = owner
            ? booking.Applications
                .OrderBy(a => a.CreatedAt)
                .Select(a => new ApplicantRow(a.LocumId, a.Locum?.User?.FullName ?? $"Doctor {a.LocumId}", a.Status, a.Message, clock.ToLocal(a.CreatedAt)))
                .ToList()
            : [];

        var warnings = (await HolidaysForAsync([(booking.StartsAt, booking.EndsAt)], ct))
            .Select(h => $"This shift falls on a bank holiday: {h.Details} ({h.Date:d MMM yyyy}).")
            .ToList();

        return new BookingDetails(booking, applicants, warnings, PermissionsFor(actor, booking, mine, now), mine?.Status);
    }

    /// <summary>Ranked doctors for an open shift, for the hospital that owns it (or an administrator).</summary>
    public async Task<MatchResult> MatchesAsync(Actor actor, int bookingId, CancellationToken ct)
    {
        var now = clock.Now;
        var booking = await Visible(actor, now).Include(b => b.Applications)
            .SingleOrDefaultAsync(b => b.Id == bookingId, ct) ?? throw NotFound();
        EnsureOwner(actor, booking);

        if (booking.Status != BookingStatus.Open)
        {
            return new MatchResult(MatchSources.Rules, []);
        }

        var busy = db.Bookings.Where(b =>
            b.LocumId != null && (b.Status == BookingStatus.Filled || b.Status == BookingStatus.Completed) &&
            b.StartsAt < booking.EndsAt && b.EndsAt > booking.StartsAt)
            .Select(b => b.LocumId!.Value);

        var applied = booking.Applications
            .Where(a => a.Status is ApplicationStatus.Pending or ApplicationStatus.Accepted)
            .Select(a => a.LocumId)
            .ToHashSet();

        var locums = await db.Locums.AsNoTracking()
            .Include(l => l.User)
            .Where(l => l.Status == LocumStatus.Active && !busy.Contains(l.Id))
            .ToListAsync(ct);

        var profiles = locums
            .Select(l => new LocumProfile(l.Id, l.User?.FullName ?? $"Doctor {l.Id}", l.SpecialtyList, l.BaseLocation,
                l.MinRatePerHour, l.YearsExperience, applied.Contains(l.Id)))
            .ToList();

        var result = await matcher.RankAsync(new ShiftProfile(booking.Specialty, booking.Location, booking.RatePerHour), profiles, ct);
        return result with { Candidates = result.Candidates.Take(matching.Value.MaxResults).ToList() };
    }

    // ───────────── hospital / admin actions ─────────────

    public async Task<Booking> CreateAsync(Actor actor, BookingInput input, CancellationToken ct)
    {
        if (!actor.CanPostShifts)
        {
            throw new ForbiddenException(actor.IsClient
                ? "Your hospital account has to be approved before you can post shifts."
                : "Only hospitals and administrators can post shifts.");
        }

        var clientId = await ResolveClientIdAsync(actor, input.ClientId, ct);
        var (start, end) = Window(input);
        EnsureFuture(start);

        var booking = new Booking
        {
            ClientId = clientId,
            Location = input.Location.Trim(),
            Specialty = Blank(input.Specialty),
            Notes = Blank(input.Notes),
            RatePerHour = input.RatePerHour,
            CreatedAt = clock.UtcNow
        };
        booking.SetWindow(start, end);

        db.Bookings.Add(booking);
        await SaveAsync(ct);

        return booking;
    }

    public async Task<BookingInput> GetForEditAsync(Actor actor, int id, CancellationToken ct)
    {
        EnsureCanManage(actor);
        var booking = await LoadOwnedAsync(actor, id, ct);
        EnsureEditable(booking, clock.Now);
        return new BookingInput
        {
            ClientId = booking.ClientId,
            StartsAt = booking.StartsAt,
            EndsAt = booking.EndsAt,
            RatePerHour = booking.RatePerHour,
            Location = booking.Location,
            Specialty = booking.Specialty,
            Notes = booking.Notes,
            ConcurrencyStamp = booking.ConcurrencyStamp
        };
    }

    public async Task<Booking> UpdateAsync(Actor actor, int id, BookingInput input, CancellationToken ct)
    {
        EnsureCanManage(actor);
        var booking = await LoadOwnedAsync(actor, id, ct);
        EnsureEditable(booking, clock.Now);

        var (start, end) = Window(input);
        EnsureFuture(start);

        if (booking.LocumId is { } assigned && (start != booking.StartsAt || end != booking.EndsAt) &&
            await HasOverlapAsync(assigned, start, end, booking.Id, ct))
        {
            throw new ConflictException("The assigned doctor already has another shift that overlaps the new times.");
        }

        db.Entry(booking).Property(b => b.ConcurrencyStamp).OriginalValue = input.ConcurrencyStamp;
        booking.SetWindow(start, end);
        booking.Location = input.Location.Trim();
        booking.Specialty = Blank(input.Specialty);
        booking.Notes = Blank(input.Notes);
        booking.RatePerHour = input.RatePerHour;
        booking.ConcurrencyStamp = Guid.NewGuid();

        await SaveAsync(ct);
        return booking;
    }

    /// <summary>Open → Filled with the chosen doctor; their application is accepted and the others are declined.</summary>
    public async Task AssignAsync(Actor actor, int bookingId, int locumId, Guid concurrencyStamp, CancellationToken ct)
    {
        EnsureCanManage(actor);
        var booking = await LoadOwnedAsync(actor, bookingId, ct, includeApplications: true);
        var now = clock.Now;

        if (booking.StartsAt <= now)
        {
            throw new InvalidTransitionException("A shift that has started can no longer be filled.");
        }

        var locum = await db.Locums.SingleOrDefaultAsync(l => l.Id == locumId, ct)
            ?? throw new NotFoundException("That doctor was not found.");
        if (!locum.CanWork)
        {
            throw new DomainException("That doctor is not approved to take shifts.");
        }

        if (await HasOverlapAsync(locumId, booking.StartsAt, booking.EndsAt, booking.Id, ct))
        {
            throw new ConflictException("That doctor already has a shift that overlaps this one.");
        }

        db.Entry(booking).Property(b => b.ConcurrencyStamp).OriginalValue = concurrencyStamp;
        booking.Fill(locumId);
        booking.ConcurrencyStamp = Guid.NewGuid();

        foreach (var application in booking.Applications.Where(a => a.Status == ApplicationStatus.Pending))
        {
            application.Status = application.LocumId == locumId ? ApplicationStatus.Accepted : ApplicationStatus.Declined;
        }

        await SaveAsync(ct);
    }

    /// <summary>Filled → Open so another doctor can be chosen. The released doctor's application is declined.</summary>
    public async Task ReleaseAsync(Actor actor, int bookingId, CancellationToken ct)
    {
        EnsureCanManage(actor);
        var booking = await LoadOwnedAsync(actor, bookingId, ct, includeApplications: true);

        foreach (var application in booking.Applications.Where(a => a.Status == ApplicationStatus.Accepted))
        {
            application.Status = ApplicationStatus.Declined;
        }

        booking.ReleaseLocum(clock.Now);
        booking.ConcurrencyStamp = Guid.NewGuid();
        await SaveAsync(ct);
    }

    public async Task CancelAsync(Actor actor, int bookingId, CancellationToken ct)
    {
        var booking = await LoadOwnedAsync(actor, bookingId, ct, includeApplications: true);

        booking.Cancel(clock.Now);
        booking.ConcurrencyStamp = Guid.NewGuid();
        foreach (var application in booking.Applications.Where(a => a.Status == ApplicationStatus.Pending))
        {
            application.Status = ApplicationStatus.Declined;
        }

        await SaveAsync(ct);
    }

    public async Task CompleteAsync(Actor actor, int bookingId, CancellationToken ct)
    {
        var booking = await LoadOwnedAsync(actor, bookingId, ct);

        booking.Complete(clock.Now);
        booking.ConcurrencyStamp = Guid.NewGuid();
        await SaveAsync(ct);
    }

    // ───────────── doctor actions ─────────────

    public async Task ApplyAsync(Actor actor, int bookingId, string? message, CancellationToken ct)
    {
        if (!actor.CanWork || actor.LocumId is not { } locumId)
        {
            throw new ForbiddenException("Only approved doctors can apply for shifts.");
        }

        var now = clock.Now;
        var booking = await Visible(actor, now).Include(b => b.Applications)
            .SingleOrDefaultAsync(b => b.Id == bookingId, ct) ?? throw NotFound();

        if (booking.Status != BookingStatus.Open || booking.StartsAt <= now)
        {
            throw new InvalidTransitionException("This shift is no longer open for applications.");
        }

        var existing = booking.Applications.FirstOrDefault(a => a.LocumId == locumId);
        if (existing is not null && existing.Status != ApplicationStatus.Withdrawn)
        {
            throw new ConflictException(existing.Status == ApplicationStatus.Declined
                ? "Your application for this shift was declined."
                : "You have already applied for this shift.");
        }

        if (await HasOverlapAsync(locumId, booking.StartsAt, booking.EndsAt, booking.Id, ct))
        {
            throw new ConflictException("You already have a shift that overlaps this one.");
        }

        var text = Blank(message);
        if (existing is null)
        {
            db.ShiftApplications.Add(new ShiftApplication
            {
                BookingId = booking.Id,
                LocumId = locumId,
                Message = text,
                CreatedAt = clock.UtcNow
            });
        }
        else
        {
            // Re-applying after a withdrawal reuses the row because (booking, doctor) is unique.
            var tracked = await db.ShiftApplications.SingleAsync(a => a.Id == existing.Id, ct);
            tracked.Status = ApplicationStatus.Pending;
            tracked.Message = text;
            tracked.CreatedAt = clock.UtcNow;
        }

        await SaveAsync(ct);
    }

    public async Task WithdrawAsync(Actor actor, int bookingId, CancellationToken ct)
    {
        if (actor.LocumId is not { } locumId)
        {
            throw new ForbiddenException("Only doctors can withdraw an application.");
        }

        var application = await db.ShiftApplications
            .SingleOrDefaultAsync(a => a.BookingId == bookingId && a.LocumId == locumId, ct)
            ?? throw NotFound();

        if (application.Status != ApplicationStatus.Pending)
        {
            throw new InvalidTransitionException("Only a pending application can be withdrawn.");
        }

        application.Status = ApplicationStatus.Withdrawn;
        await SaveAsync(ct);
    }

    // ───────────── helpers ─────────────

    private IQueryable<Booking> Visible(Actor actor, DateTime now)
    {
        var bookings = db.Bookings.AsNoTracking();

        if (actor.IsAdmin)
        {
            return bookings;
        }

        if (actor.IsClient && actor.ClientId is { } clientId)
        {
            return bookings.Where(b => b.ClientId == clientId);
        }

        if (actor.IsLocum && actor.LocumId is { } locumId)
        {
            var active = actor.CanWork;
            return bookings.Where(b =>
                b.LocumId == locumId ||
                b.Applications.Any(a => a.LocumId == locumId) ||
                (active && b.Status == BookingStatus.Open && b.StartsAt > now));
        }

        return bookings.Where(_ => false);
    }

    /// <summary>Doctors never see each other's names: they get "You" for their own shifts and a neutral label otherwise.</summary>
    private static string? DoctorLabel(Actor actor, int? locumId, string? first, string? last)
    {
        if (locumId is null)
        {
            return null;
        }

        if (actor.IsLocum)
        {
            return actor.LocumId == locumId ? "You" : "Another doctor";
        }

        return $"{first} {last}".Trim();
    }

    private static bool IsOwner(Actor actor, Booking booking) =>
        actor.IsAdmin || (actor.IsClient && actor.ClientId == booking.ClientId);

    private static void EnsureOwner(Actor actor, Booking booking)
    {
        if (!IsOwner(actor, booking))
        {
            throw NotFound();
        }
    }

    private static void EnsureCanManage(Actor actor)
    {
        if (!actor.CanPostShifts)
        {
            throw new ForbiddenException("Your hospital account has to be approved before you can manage shifts.");
        }
    }

    private static void EnsureEditable(Booking booking, DateTime now)
    {
        if (!booking.IsLive)
        {
            throw new InvalidTransitionException($"A {booking.Status} shift can no longer be changed.");
        }

        if (booking.StartsAt <= now)
        {
            throw new InvalidTransitionException("A shift that has started can no longer be edited.");
        }
    }

    /// <summary>A tracked booking the actor owns. Anything else looks like it does not exist.</summary>
    private async Task<Booking> LoadOwnedAsync(Actor actor, int id, CancellationToken ct, bool includeApplications = false)
    {
        var query = db.Bookings.AsQueryable();
        if (includeApplications)
        {
            query = query.Include(b => b.Applications);
        }

        var booking = await query.SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw NotFound();
        EnsureOwner(actor, booking);
        return booking;
    }

    private BookingPermissions PermissionsFor(Actor actor, Booking booking, ShiftApplication? mine, DateTime now)
    {
        var owner = IsOwner(actor, booking);
        var future = booking.StartsAt > now;
        var manage = owner && actor.CanPostShifts;

        return new BookingPermissions(
            CanEdit: manage && booking.IsLive && future,
            CanCancel: owner && booking.IsLive && future,
            CanAssign: manage && booking.Status == BookingStatus.Open && future,
            CanRelease: manage && booking.Status == BookingStatus.Filled && future,
            CanComplete: owner && booking.Status == BookingStatus.Filled && booking.EndsAt <= now,
            CanApply: actor.CanWork && booking.Status == BookingStatus.Open && future &&
                      (mine is null || mine.Status == ApplicationStatus.Withdrawn),
            CanWithdraw: actor.IsLocum && mine?.Status == ApplicationStatus.Pending);
    }

    private async Task<int> ResolveClientIdAsync(Actor actor, int? requested, CancellationToken ct)
    {
        if (actor.IsClient)
        {
            return actor.ClientId ?? throw new ForbiddenException("Your account has no hospital profile.");
        }

        var clientId = requested ?? throw new DomainException("Choose the hospital this shift is for.");
        var status = await db.Clients.AsNoTracking().Where(c => c.Id == clientId)
            .Select(c => (ClientStatus?)c.Status).SingleOrDefaultAsync(ct);

        return status switch
        {
            null => throw new NotFoundException("That hospital was not found."),
            ClientStatus.Accepted => clientId,
            _ => throw new DomainException("That hospital has not been approved.")
        };
    }

    private Task<bool> HasOverlapAsync(int locumId, DateTime start, DateTime end, int? excludeBookingId, CancellationToken ct) =>
        db.Bookings.AnyAsync(b =>
            b.LocumId == locumId &&
            (excludeBookingId == null || b.Id != excludeBookingId) &&
            (b.Status == BookingStatus.Filled || b.Status == BookingStatus.Completed) &&
            b.StartsAt < end && b.EndsAt > start, ct);

    private async Task<List<Holiday>> HolidaysForAsync(IEnumerable<(DateTime Start, DateTime End)> windows, CancellationToken ct)
    {
        var list = windows.ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var first = DateOnly.FromDateTime(list.Min(w => w.Start));
        var last = DateOnly.FromDateTime(list.Max(w => w.End));
        return await db.Holidays.AsNoTracking().Where(h => h.Date >= first && h.Date <= last).OrderBy(h => h.Date).ToListAsync(ct);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This shift was changed by someone else. Reload it and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { Message: var message } &&
                                           message.Contains("booking_overlap", StringComparison.Ordinal))
        {
            // The database trigger caught a race the pre-check could not see.
            throw new ConflictException("That doctor already has a shift that overlaps this one.");
        }
    }

    private static (DateTime Start, DateTime End) Window(BookingInput input)
    {
        var start = input.StartsAt ?? throw new DomainException("Enter when the shift starts.");
        var end = input.EndsAt ?? throw new DomainException("Enter when the shift ends.");
        Booking.ValidateWindow(start, end);
        return (start, end);
    }

    private void EnsureFuture(DateTime start)
    {
        if (start <= clock.Now)
        {
            throw new DomainException("A shift must start in the future.");
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static NotFoundException NotFound() => new("That shift was not found.");
}
