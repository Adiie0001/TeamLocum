namespace TeamLocum.Web.Domain;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Client = "Client";
    public const string Locum = "Locum";

    public static readonly string[] All = [Admin, Client, Locum];
}

/// <summary>Approval state of a hospital or clinic account. Only <see cref="Accepted"/> clients may post shifts.</summary>
public enum ClientStatus
{
    AwaitingReview,
    Accepted,
    Rejected,
    Suspended
}

/// <summary>Vetting state of a doctor. Only <see cref="Active"/> locums may apply for or be assigned to shifts.</summary>
public enum LocumStatus
{
    PendingReview,
    Active,
    Rejected,
    Suspended
}

/// <summary>
/// Lifecycle of a shift: Open (no doctor yet) → Filled (doctor assigned) → Completed (shift finished),
/// or Cancelled from Open/Filled before it starts.
/// </summary>
public enum BookingStatus
{
    Open,
    Filled,
    Completed,
    Cancelled
}

public enum ApplicationStatus
{
    Pending,
    Accepted,
    Declined,
    Withdrawn
}
