using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TeamLocum.Web.Domain;

namespace TeamLocum.Web.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class LoginModel(SignInManager<ApplicationUser> signInManager, ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string ReturnUrl { get; set; } = "/";

    public sealed class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Keep me signed in on this device")]
        public bool RememberMe { get; set; }
    }

    public void OnGet(string? returnUrl = null) => ReturnUrl = LocalOrHome(returnUrl);

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = LocalOrHome(returnUrl);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(Input.Email.Trim(), Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            logger.LogInformation("User signed in");
            return LocalRedirect(ReturnUrl);
        }

        if (result.IsLockedOut)
        {
            logger.LogWarning("Sign-in refused: account is locked out");
            return RedirectToPage("./Lockout");
        }

        // One message for unknown account and wrong password, so the form cannot be used to find out who has an account.
        ModelState.AddModelError(string.Empty, "Invalid email or password.");
        return Page();
    }

    private string LocalOrHome(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/");
}
