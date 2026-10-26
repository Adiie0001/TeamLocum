using System.ComponentModel.DataAnnotations;
using TeamLocum.Web.Domain;

namespace TeamLocum.Web.Services;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public enum BookingScope
{
    /// <summary>Everything the viewer may see.</summary>
    Default,

    /// <summary>Open shifts that can still be applied for or filled.</summary>
    Open,

    /// <summary>Shifts assigned to the viewing doctor.</summary>
    Mine
}

public sealed class BookingListQuery
{
    public BookingScope Scope { get; set; }
    public BookingStatus? Status { get; set; }
    public string? Search { get; set; }
    public bool Past { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed record BookingRow(
    int Id,
    DateTime StartsAt,
    DateTime EndsAt,
    string Location,
    string? Specialty,
    string ClientName,
    string? LocumName,
    decimal RatePerHour,
    BookingStatus Status,
    int PendingApplications,
    ApplicationStatus? MyApplication,
    string? Holiday);

public sealed record ApplicantRow(
    int LocumId,
    string Name,
    ApplicationStatus Status,
    string? Message,
    DateTime AppliedAt);

public sealed record BookingPermissions(
    bool CanEdit,
    bool CanCancel,
    bool CanAssign,
    bool CanRelease,
    bool CanComplete,
    bool CanApply,
    bool CanWithdraw);

public sealed record BookingDetails(
    Booking Booking,
    IReadOnlyList<ApplicantRow> Applicants,
    IReadOnlyList<string> Warnings,
    BookingPermissions Permissions,
    ApplicationStatus? MyApplication);

/// <summary>The fields a hospital fills in to post or change a shift.</summary>
public sealed class BookingInput : IValidatableObject
{
    /// <summary>Only used by administrators posting on behalf of a hospital.</summary>
    public int? ClientId { get; set; }

    [Display(Name = "Starts")]
    [Required]
    public DateTime? StartsAt { get; set; }

    [Display(Name = "Ends")]
    [Required]
    public DateTime? EndsAt { get; set; }

    [Display(Name = "Rate per hour (£)")]
    [Range(typeof(decimal), "1", "1000", ErrorMessage = "The rate must be between £1 and £1,000 per hour.")]
    public decimal RatePerHour { get; set; }

    [Required, StringLength(120)]
    public string Location { get; set; } = string.Empty;

    [StringLength(80)]
    [Display(Name = "Specialty needed")]
    public string? Specialty { get; set; }

    [StringLength(1000)]
    [Display(Name = "Notes for applicants")]
    public string? Notes { get; set; }

    /// <summary>Round-trips from the edit form; a stale value means someone else changed the shift.</summary>
    public Guid ConcurrencyStamp { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RatePerHour != decimal.Round(RatePerHour, 2))
        {
            yield return new ValidationResult("The rate can have at most two decimal places.", [nameof(RatePerHour)]);
        }

        if (StartsAt is { } start && EndsAt is { } end)
        {
            if (end <= start)
            {
                yield return new ValidationResult("The shift must end after it starts.", [nameof(EndsAt)]);
            }
            else if (end - start > TimeSpan.FromHours(Booking.MaxShiftHours))
            {
                yield return new ValidationResult($"A shift cannot be longer than {Booking.MaxShiftHours} hours.", [nameof(EndsAt)]);
            }
        }
    }
}

/// <summary>One number on the landing dashboard, with the page it links to.</summary>
public sealed record DashboardItem(string Label, int Value, string Controller, string Action);
