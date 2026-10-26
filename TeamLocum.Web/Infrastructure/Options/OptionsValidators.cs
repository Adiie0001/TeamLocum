using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TeamLocum.Web.Data;

namespace TeamLocum.Web.Infrastructure.Options;

/// <summary>Keeps demo data and demo credentials out of every environment except Development.</summary>
public sealed class SeedOptionsValidator(IHostEnvironment environment) : IValidateOptions<SeedOptions>
{
    public ValidateOptionsResult Validate(string? name, SeedOptions options)
    {
        var failures = new List<string>();
        var isDevelopment = environment.IsDevelopment();

        if (options.DemoData && !isDevelopment)
        {
            failures.Add("Seed:DemoData is only allowed in the Development environment.");
        }

        var hasEmail = !string.IsNullOrWhiteSpace(options.AdminEmail);
        var hasPassword = !string.IsNullOrEmpty(options.AdminPassword);
        if (hasEmail != hasPassword)
        {
            failures.Add("Seed:AdminEmail and Seed:AdminPassword must be provided together.");
        }
        else if (hasEmail)
        {
            if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(options.AdminEmail))
            {
                failures.Add("Seed:AdminEmail is not a valid email address.");
            }

            failures.AddRange(PasswordRules.Validate(options.AdminPassword!, options.AdminEmail!)
                .Select(error => $"Seed:AdminPassword {error}"));

            if (!isDevelopment && DemoCredentials.IsDemoPassword(options.AdminPassword!))
            {
                failures.Add("Seed:AdminPassword is a published demo password and cannot be used outside Development.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class ClinicOptionsValidator : IValidateOptions<ClinicOptions>
{
    public ValidateOptionsResult Validate(string? name, ClinicOptions options)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
            return ValidateOptionsResult.Success;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return ValidateOptionsResult.Fail($"Clinic:TimeZone '{options.TimeZone}' is not a time zone on this machine.");
        }
    }
}

public sealed partial class MatchingOptionsValidator : IValidateOptions<MatchingOptions>
{
    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex ModelNamePattern();

    public ValidateOptionsResult Validate(string? name, MatchingOptions options)
    {
        var gemini = options.Gemini;
        if (!gemini.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(gemini.ApiKey))
        {
            failures.Add("Matching:Gemini:ApiKey is required when Gemini is enabled.");
        }

        if (!ModelNamePattern().IsMatch(gemini.Model))
        {
            failures.Add("Matching:Gemini:Model contains unsupported characters.");
        }

        if (!Uri.TryCreate(gemini.BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Matching:Gemini:BaseUrl must be an absolute http(s) URL.");
        }

        // DataAnnotations validation does not reach nested option objects, so range-check them here.
        if (gemini.TimeoutSeconds is < 1 or > 60)
        {
            failures.Add("Matching:Gemini:TimeoutSeconds must be between 1 and 60.");
        }

        if (gemini.CacheMinutes is < 0 or > 60)
        {
            failures.Add("Matching:Gemini:CacheMinutes must be between 0 and 60.");
        }

        if (gemini.CircuitBreakerMinimumRequests is < 2 or > 1000)
        {
            failures.Add("Matching:Gemini:CircuitBreakerMinimumRequests must be between 2 and 1000.");
        }

        if (gemini.CircuitBreakerSeconds is < 1 or > 600)
        {
            failures.Add("Matching:Gemini:CircuitBreakerSeconds must be between 1 and 600.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>The same rules ASP.NET Identity enforces, checked up front for configured bootstrap passwords.</summary>
public static class PasswordRules
{
    public const int MinLength = 10;
    public const int UniqueChars = 4;

    public static IReadOnlyList<string> Validate(string password, string? email = null)
    {
        var errors = new List<string>();

        if (password.Length < MinLength)
        {
            errors.Add($"must be at least {MinLength} characters long");
        }

        if (password.Distinct().Count() < UniqueChars)
        {
            errors.Add($"must contain at least {UniqueChars} different characters");
        }

        if (!password.Any(char.IsLower))
        {
            errors.Add("must contain a lower-case letter");
        }

        if (!password.Any(char.IsUpper))
        {
            errors.Add("must contain an upper-case letter");
        }

        if (!password.Any(char.IsDigit))
        {
            errors.Add("must contain a digit");
        }

        var localPart = email?.Split('@')[0];
        if (localPart is { Length: >= 4 } && password.Contains(localPart, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("must not contain the name part of the email address");
        }

        return errors;
    }
}
