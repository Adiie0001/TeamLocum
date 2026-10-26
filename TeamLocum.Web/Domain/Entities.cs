using Microsoft.AspNetCore.Identity;

namespace TeamLocum.Web.Domain;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}

/// <summary>A hospital or clinic that posts shifts.</summary>
public class Client
{
    private static readonly Dictionary<ClientStatus, ClientStatus[]> Transitions = new()
    {
        [ClientStatus.AwaitingReview] = [ClientStatus.Accepted, ClientStatus.Rejected],
        [ClientStatus.Accepted] = [ClientStatus.Suspended],
        [ClientStatus.Rejected] = [ClientStatus.Accepted],
        [ClientStatus.Suspended] = [ClientStatus.Accepted]
    };

    public int Id { get; set; }
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public required string CompanyName { get; set; }

    /// <summary>Optional accreditation reference supplied at registration.</summary>
    public string? CarasId { get; set; }

    public ClientStatus Status { get; private set; } = ClientStatus.AwaitingReview;
    public DateTimeOffset CreatedAt { get; set; }

    public bool CanPostShifts => Status == ClientStatus.Accepted;

    public void MoveTo(ClientStatus next)
    {
        if (!Transitions[Status].Contains(next))
        {
            throw new InvalidTransitionException($"A hospital in status {Status} cannot move to {next}.");
        }

        Status = next;
    }

    /// <summary>Used by the persistence layer and the seeder only; application code must call <see cref="MoveTo"/>.</summary>
    internal void RestoreStatus(ClientStatus status) => Status = status;
}

/// <summary>A doctor offering locum shifts.</summary>
public class Locum
{
    public const int GmcNumberLength = 7;

    private static readonly Dictionary<LocumStatus, LocumStatus[]> Transitions = new()
    {
        [LocumStatus.PendingReview] = [LocumStatus.Active, LocumStatus.Rejected],
        [LocumStatus.Active] = [LocumStatus.Suspended],
        [LocumStatus.Rejected] = [LocumStatus.Active],
        [LocumStatus.Suspended] = [LocumStatus.Active]
    };

    public int Id { get; set; }
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }

    /// <summary>General Medical Council reference number: exactly seven digits.</summary>
    public required string GmcNumber { get; set; }

    /// <summary>Comma-separated specialties, for example "General practice, Paediatrics".</summary>
    public string Specialties { get; set; } = string.Empty;

    /// <summary>Where the doctor is based, for example "Leicester".</summary>
    public string BaseLocation { get; set; } = string.Empty;

    /// <summary>The lowest hourly rate the doctor will accept.</summary>
    public decimal MinRatePerHour { get; set; }

    public int YearsExperience { get; set; }

    public LocumStatus Status { get; private set; } = LocumStatus.PendingReview;
    public DateTimeOffset CreatedAt { get; set; }

    public bool CanWork => Status == LocumStatus.Active;

    public IReadOnlyList<string> SpecialtyList =>
        Specialties.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public void MoveTo(LocumStatus next)
    {
        if (!Transitions[Status].Contains(next))
        {
            throw new InvalidTransitionException($"A doctor in status {Status} cannot move to {next}.");
        }

        Status = next;
    }

    internal void RestoreStatus(LocumStatus status) => Status = status;
}

/// <summary>
/// A shift. Times are the clinic's local wall-clock time. Overnight shifts are ordinary: the end is simply on the
/// next day. Status changes go through the methods below so illegal transitions cannot be written by accident.
/// </summary>
public class Booking
{
    public const int MaxShiftHours = 24;

    public int Id { get; set; }

    public int ClientId { get; set; }
    public Client? Client { get; set; }

    public int? LocumId { get; private set; }
    public Locum? Locum { get; set; }

    public DateTime StartsAt { get; private set; }
    public DateTime EndsAt { get; private set; }

    public decimal RatePerHour { get; set; }
    public required string Location { get; set; }

    /// <summary>The specialty the shift needs, if any, for example "Paediatrics".</summary>
    public string? Specialty { get; set; }

    public string? Notes { get; set; }

    public BookingStatus Status { get; private set; } = BookingStatus.Open;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Optimistic-concurrency token; replaced on every change.</summary>
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<ShiftApplication> Applications { get; set; } = [];

    public TimeSpan Duration => EndsAt - StartsAt;

    public decimal TotalPay => decimal.Round(RatePerHour * (decimal)Duration.TotalHours, 2);

    public bool IsLive => Status is BookingStatus.Open or BookingStatus.Filled;

    public static void ValidateWindow(DateTime startsAt, DateTime endsAt)
    {
        if (endsAt <= startsAt)
        {
            throw new DomainException("The shift must end after it starts.");
        }

        if (endsAt - startsAt > TimeSpan.FromHours(MaxShiftHours))
        {
            throw new DomainException($"A shift cannot be longer than {MaxShiftHours} hours.");
        }
    }

    public void SetWindow(DateTime startsAt, DateTime endsAt)
    {
        if (!IsLive)
        {
            throw new InvalidTransitionException($"A {Status} shift can no longer be changed.");
        }

        ValidateWindow(startsAt, endsAt);
        StartsAt = startsAt;
        EndsAt = endsAt;
    }

    /// <summary>Open → Filled.</summary>
    public void Fill(int locumId)
    {
        if (Status != BookingStatus.Open)
        {
            throw new InvalidTransitionException($"Only an open shift can be filled; this one is {Status}.");
        }

        LocumId = locumId;
        Status = BookingStatus.Filled;
    }

    /// <summary>Filled → Open, so another doctor can be chosen.</summary>
    public void ReleaseLocum(DateTime nowLocal)
    {
        if (Status != BookingStatus.Filled)
        {
            throw new InvalidTransitionException($"Only a filled shift can release its doctor; this one is {Status}.");
        }

        if (StartsAt <= nowLocal)
        {
            throw new InvalidTransitionException("A shift that has started can no longer be reassigned.");
        }

        LocumId = null;
        Locum = null;
        Status = BookingStatus.Open;
    }

    /// <summary>Open or Filled → Cancelled, only before the shift starts.</summary>
    public void Cancel(DateTime nowLocal)
    {
        if (!IsLive)
        {
            throw new InvalidTransitionException($"A {Status} shift cannot be cancelled.");
        }

        if (StartsAt <= nowLocal)
        {
            throw new InvalidTransitionException("A shift that has started cannot be cancelled.");
        }

        Status = BookingStatus.Cancelled;
    }

    /// <summary>Filled → Completed, only after the shift has ended.</summary>
    public void Complete(DateTime nowLocal)
    {
        if (Status != BookingStatus.Filled)
        {
            throw new InvalidTransitionException($"Only a filled shift can be completed; this one is {Status}.");
        }

        if (EndsAt > nowLocal)
        {
            throw new InvalidTransitionException("The shift has not finished yet.");
        }

        Status = BookingStatus.Completed;
    }

    /// <summary>Used by the persistence layer and the seeder only.</summary>
    internal void Restore(DateTime startsAt, DateTime endsAt, BookingStatus status, int? locumId)
    {
        StartsAt = startsAt;
        EndsAt = endsAt;
        Status = status;
        LocumId = locumId;
    }
}

/// <summary>A doctor's interest in an open shift.</summary>
public class ShiftApplication
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public Booking? Booking { get; set; }
    public int LocumId { get; set; }
    public Locum? Locum { get; set; }

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;
    public string? Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A bank holiday. Shifts on these days are flagged, never blocked.</summary>
public class Holiday
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public required string Details { get; set; }
}
