using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TeamLocum.Web.Data;
using TeamLocum.Web.Domain;
using TeamLocum.Web.Services;

namespace TeamLocum.Web.Areas.Identity.Pages.Account;

public enum AccountType
{
    Hospital = 1,
    Doctor = 2
}

/// <summary>
/// Self-service sign-up for hospitals and doctors. Nobody can register as an administrator, and every new profile starts
/// in a "pending review" state: an administrator has to approve it before it can post or work shifts.
/// </summary>
[AllowAnonymous]
public class RegisterModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext db,
    ClinicClock clock,
    ILogger<RegisterModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string ReturnUrl { get; set; } = "/";

    public sealed class InputModel : IValidatableObject
    {
        [Required]
        [Display(Name = "I am registering as")]
        public AccountType? AccountType { get; set; }

        [Required, EmailAddress, StringLength(254)]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 10, ErrorMessage = "The password must be at least {2} characters long.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required, StringLength(60)]
        [Display(Name = "First name")]
        public string FirstName { get; set; } = string.Empty;

        [Required, StringLength(60)]
        [Display(Name = "Last name")]
        public string LastName { get; set; } = string.Empty;

        [StringLength(120)]
        [Display(Name = "Hospital or clinic name")]
        public string? CompanyName { get; set; }

        [StringLength(40)]
        [Display(Name = "Accreditation reference (optional)")]
        public string? CarasId { get; set; }

        // Length is enforced in Validate() so the message is the same however the value is wrong.
        [StringLength(20)]
        [Display(Name = "GMC number (7 digits)")]
        public string? GmcNumber { get; set; }

        [StringLength(200)]
        [Display(Name = "Specialties (comma separated)")]
        public string? Specialties { get; set; }

        [StringLength(80)]
        [Display(Name = "Base location")]
        public string? BaseLocation { get; set; }

        [Range(typeof(decimal), "0", "1000", ErrorMessage = "Enter a rate between £0 and £1,000 per hour.")]
        [Display(Name = "Lowest hourly rate you accept (£)")]
        public decimal MinRatePerHour { get; set; }

        [Range(0, 60)]
        [Display(Name = "Years of experience")]
        public int YearsExperience { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (AccountType == Account.AccountType.Hospital && string.IsNullOrWhiteSpace(CompanyName))
            {
                yield return new ValidationResult("Enter the name of your hospital or clinic.", [nameof(CompanyName)]);
            }

            if (AccountType == Account.AccountType.Doctor)
            {
                if (GmcNumber is null || GmcNumber.Length != Locum.GmcNumberLength || !GmcNumber.All(char.IsAsciiDigit))
                {
                    yield return new ValidationResult("A GMC number is exactly 7 digits.", [nameof(GmcNumber)]);
                }

                if (string.IsNullOrWhiteSpace(Specialties))
                {
                    yield return new ValidationResult("List at least one specialty.", [nameof(Specialties)]);
                }

                if (string.IsNullOrWhiteSpace(BaseLocation))
                {
                    yield return new ValidationResult("Enter where you are based.", [nameof(BaseLocation)]);
                }
            }
        }
    }

    public void OnGet(string? returnUrl = null) => ReturnUrl = LocalOrHome(returnUrl);

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = LocalOrHome(returnUrl);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var isDoctor = Input.AccountType == AccountType.Doctor;
        if (isDoctor && await db.Locums.AnyAsync(l => l.GmcNumber == Input.GmcNumber))
        {
            ModelState.AddModelError("Input.GmcNumber", "A doctor with that GMC number is already registered.");
            return Page();
        }

        var email = Input.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = Input.FirstName.Trim(),
            LastName = Input.LastName.Trim(),
            CreatedAt = clock.UtcNow
        };

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var created = await userManager.CreateAsync(user, Input.Password);
            if (!created.Succeeded)
            {
                foreach (var error in created.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return Page();
            }

            await userManager.AddToRoleAsync(user, isDoctor ? Roles.Locum : Roles.Client);

            if (isDoctor)
            {
                db.Locums.Add(new Locum
                {
                    UserId = user.Id,
                    GmcNumber = Input.GmcNumber!,
                    Specialties = string.Join(", ", Input.Specialties!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
                    BaseLocation = Input.BaseLocation!.Trim(),
                    MinRatePerHour = Input.MinRatePerHour,
                    YearsExperience = Input.YearsExperience,
                    CreatedAt = clock.UtcNow
                });
            }
            else
            {
                db.Clients.Add(new Client
                {
                    UserId = user.Id,
                    CompanyName = Input.CompanyName!.Trim(),
                    CarasId = string.IsNullOrWhiteSpace(Input.CarasId) ? null : Input.CarasId.Trim(),
                    CreatedAt = clock.UtcNow
                });
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex)
        {
            // A concurrent registration with the same GMC number or email won the race.
            logger.LogWarning(ex, "Registration could not be saved");
            ModelState.AddModelError(string.Empty, "That email address or GMC number is already registered.");
            return Page();
        }

        logger.LogInformation("New {Role} account registered", isDoctor ? Roles.Locum : Roles.Client);
        await signInManager.SignInAsync(user, isPersistent: false);
        TempData["Success"] = "Welcome! Your account is created. An administrator has to approve it before you can " +
                              (isDoctor ? "apply for shifts." : "post shifts.");
        return LocalRedirect(ReturnUrl);
    }

    private string LocalOrHome(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/");
}
