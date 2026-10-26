namespace TeamLocum.Web.Infrastructure.Middleware;

/// <summary>
/// Identity pages that depend on features this app does not provide: e-mail (password reset, confirmation, changing the
/// address), external logins, two-factor set-up (its QR code needs inline script, which the content security policy forbids)
/// and self-service account deletion (hospitals with shift history cannot be deleted without an administrator). Answering 404 is more honest than a page that promises something and does not deliver it.
/// </summary>
public sealed class DisabledIdentityPagesMiddleware(RequestDelegate next)
{
    private static readonly string[] Pages =
    [
        "/Identity/Account/ForgotPassword",
        "/Identity/Account/ForgotPasswordConfirmation",
        "/Identity/Account/ResetPassword",
        "/Identity/Account/ResetPasswordConfirmation",
        "/Identity/Account/ConfirmEmail",
        "/Identity/Account/ConfirmEmailChange",
        "/Identity/Account/ResendEmailConfirmation",
        "/Identity/Account/RegisterConfirmation",
        "/Identity/Account/ExternalLogin",
        "/Identity/Account/LoginWith2fa",
        "/Identity/Account/LoginWithRecoveryCode",
        "/Identity/Account/Manage/Email",
        "/Identity/Account/Manage/ExternalLogins",
        "/Identity/Account/Manage/TwoFactorAuthentication",
        "/Identity/Account/Manage/EnableAuthenticator",
        "/Identity/Account/Manage/ResetAuthenticator",
        "/Identity/Account/Manage/GenerateRecoveryCodes",
        "/Identity/Account/Manage/ShowRecoveryCodes",
        "/Identity/Account/Manage/Disable2fa",
        "/Identity/Account/Manage/PersonalData",
        "/Identity/Account/Manage/DownloadPersonalData",
        "/Identity/Account/Manage/DeletePersonalData"
    ];

    public Task InvokeAsync(HttpContext context)
    {
        if (Pages.Any(page => context.Request.Path.StartsWithSegments(page, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return next(context);
    }
}
