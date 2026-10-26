using System.ComponentModel.DataAnnotations;

namespace TeamLocum.Web.Infrastructure.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending EF Core migrations when the app starts. On by default in Development and in the Docker image.</summary>
    public bool ApplyMigrationsOnStartup { get; set; }
}

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Insert demo accounts, shifts and holidays. Only permitted in the Development environment.</summary>
    public bool DemoData { get; set; }

    /// <summary>Optional bootstrap administrator, created once if no administrator exists. Email and password go together.</summary>
    public string? AdminEmail { get; set; }

    public string? AdminPassword { get; set; }
}

public sealed class ClinicOptions
{
    public const string SectionName = "Clinic";

    /// <summary>
    /// IANA time zone in which shift times are entered and "now" is judged (a shift "starts" at local wall-clock time).
    /// </summary>
    [Required]
    public string TimeZone { get; set; } = "Europe/London";
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary>Sign-in and registration attempts per client address per minute.</summary>
    [Range(1, 100_000)]
    public int SignInPerMinute { get; set; } = 10;

    [Range(1, 100_000)]
    public int GlobalPerMinute { get; set; } = 300;
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Redirect HTTP to HTTPS and send HSTS. Enable when Kestrel itself terminates TLS (a proxy usually does).</summary>
    public bool HttpsRedirection { get; set; }
}

public sealed class DataProtectionSettings
{
    public const string SectionName = "DataProtection";

    /// <summary>
    /// Directory for the keys that protect cookies and anti-forgery tokens. Set it to a persistent volume in containers,
    /// otherwise every restart signs everyone out.
    /// </summary>
    public string? KeysPath { get; set; }
}

public sealed class MatchingOptions
{
    public const string SectionName = "Matching";

    [Range(1, 50)]
    public int MaxResults { get; set; } = 10;

    public GeminiOptions Gemini { get; set; } = new();
}

public sealed class GeminiOptions
{
    /// <summary>Off by default. When off, only the rule-based matcher runs and nothing leaves the server.</summary>
    public bool Enabled { get; set; }

    /// <summary>Sent in the <c>x-goog-api-key</c> header, never in the URL.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gemini-2.5-flash";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/";

    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 8;

    [Range(0, 60)]
    public int CacheMinutes { get; set; } = 5;

    [Range(2, 1000)]
    public int CircuitBreakerMinimumRequests { get; set; } = 10;

    [Range(1, 600)]
    public int CircuitBreakerSeconds { get; set; } = 30;
}
