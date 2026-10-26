using System.ComponentModel.DataAnnotations;
using TeamLocum.Web.Domain;

namespace TeamLocum.Web.Services;

/// <summary>
/// A doctor as one viewer may see them. <c>Email</c> is only filled in for administrators; <c>GmcNumber</c> only for
/// administrators and hospitals, who have to verify it on the public GMC register.
/// </summary>
public sealed record LocumRow(
    int Id,
    string Name,
    string? Email,
    string? GmcNumber,
    IReadOnlyList<string> Specialties,
    string BaseLocation,
    decimal MinRatePerHour,
    int YearsExperience,
    LocumStatus Status);

public sealed record ClientRow(int Id, string CompanyName, string Contact, string Email, string? CarasId, ClientStatus Status, DateTimeOffset CreatedAt);

public sealed record UserRow(string Id, string Email, string Name, string Roles, bool LockedOut, DateTimeOffset? LockoutEnd);

public sealed class LocumProfileInput
{
    [Required, StringLength(60)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(60)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, StringLength(200)]
    [Display(Name = "Specialties (comma separated)")]
    public string Specialties { get; set; } = string.Empty;

    [Required, StringLength(80)]
    [Display(Name = "Base location")]
    public string BaseLocation { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", "1000", ErrorMessage = "Enter a rate between £0 and £1,000 per hour.")]
    [Display(Name = "Lowest hourly rate you accept (£)")]
    public decimal MinRatePerHour { get; set; }

    [Range(0, 60)]
    [Display(Name = "Years of experience")]
    public int YearsExperience { get; set; }
}

public sealed class ClientProfileInput
{
    [Required, StringLength(60)]
    [Display(Name = "Contact first name")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(60)]
    [Display(Name = "Contact last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, StringLength(120)]
    [Display(Name = "Hospital or clinic name")]
    public string CompanyName { get; set; } = string.Empty;

    [StringLength(40)]
    [Display(Name = "Accreditation reference (optional)")]
    public string? CarasId { get; set; }
}
