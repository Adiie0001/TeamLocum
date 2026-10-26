using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using TeamLocum.Web.Data;
using TeamLocum.Web.Domain;
using TeamLocum.Web.Infrastructure.Options;
using TeamLocum.Web.Services;
using TeamLocum.Web.Services.Matching;

namespace TeamLocum.Web.Infrastructure.Startup;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything the app needs. Configuration is read through options at resolve time, never eagerly, so every
    /// source (files, environment, user-secrets, test overrides) is honoured the same way.
    /// </summary>
    public static WebApplicationBuilder AddTeamLocum(this WebApplicationBuilder builder)
    {
        builder.ConfigureLogging();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 2_097_152);

        var services = builder.Services;
        services.AddAppOptions();
        services.AddPersistenceAndIdentity();
        services.AddDomainServices();
        services.AddMatching();
        services.AddWebPlumbing();
        return builder;
    }

    private static void ConfigureLogging(this WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment())
        {
            builder.Logging.AddSimpleConsole(o =>
            {
                o.IncludeScopes = true;
                o.SingleLine = true;
            });
        }
        else
        {
            builder.Logging.AddJsonConsole(o =>
            {
                o.IncludeScopes = true;
                o.UseUtcTimestamp = true;
                o.TimestampFormat = "O";
            });
        }
    }

    private static void AddAppOptions(this IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName).ValidateOnStart();

        services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<SeedOptions>, SeedOptionsValidator>();

        services.AddOptions<ClinicOptions>().BindConfiguration(ClinicOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IValidateOptions<ClinicOptions>, ClinicOptionsValidator>();

        services.AddOptions<RateLimitOptions>().BindConfiguration(RateLimitOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SecurityOptions>().BindConfiguration(SecurityOptions.SectionName).ValidateOnStart();
        services.AddOptions<DataProtectionSettings>().BindConfiguration(DataProtectionSettings.SectionName).ValidateOnStart();

        services.AddOptions<MatchingOptions>().BindConfiguration(MatchingOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IValidateOptions<MatchingOptions>, MatchingOptionsValidator>();
    }

    private static void AddPersistenceAndIdentity(this IServiceCollection services)
    {
        services.AddDbContext<ApplicationDbContext>((provider, options) =>
        {
            var connection = provider.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
                             ?? "Data Source=teamlocum.db";
            options.UseSqlite(connection);
        });
        services.AddScoped<DatabaseInitializer>();

        services.AddDefaultIdentity<ApplicationUser>(options =>
            {
                // No mail provider is configured, so accounts are usable immediately and approval is the human gate.
                options.SignIn.RequireConfirmedAccount = false;
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = PasswordRules.MinLength;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = PasswordRules.UniqueChars;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.Cookie.Name = "teamlocum.auth";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            // Secure whenever the request arrived over HTTPS (behind a proxy, via forwarded headers).
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
            cookie.SlidingExpiration = true;
            cookie.LoginPath = "/Identity/Account/Login";
            cookie.AccessDeniedPath = "/Identity/Account/AccessDenied";
        });

        // Persist the keys that protect cookies and anti-forgery tokens when a directory is configured.
        services.AddDataProtection().SetApplicationName("TeamLocum");
        services.AddOptions<KeyManagementOptions>().Configure<IOptions<DataProtectionSettings>>((keys, settings) =>
        {
            if (!string.IsNullOrWhiteSpace(settings.Value.KeysPath))
            {
                Directory.CreateDirectory(settings.Value.KeysPath);
                keys.XmlRepository = new Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository(
                    new DirectoryInfo(settings.Value.KeysPath), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
            }
        });
    }

    private static void AddDomainServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ClinicClock>();
        services.AddHttpContextAccessor();
        services.AddScoped<IActorAccessor, ActorAccessor>();
        services.AddScoped<BookingService>();
        services.AddScoped<DirectoryService>();
    }

    private static void AddMatching(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<RuleBasedShiftMatcher>();
        services.AddScoped<GeminiShiftMatcher>();

        services.AddHttpClient<IGeminiMatchClient, GeminiMatchClient>((provider, client) =>
            {
                var gemini = provider.GetRequiredService<IOptions<MatchingOptions>>().Value.Gemini;
                if (Uri.TryCreate(gemini.BaseUrl, UriKind.Absolute, out var baseAddress))
                {
                    client.BaseAddress = baseAddress;
                }

                // The resilience pipeline owns the real deadline; this only stops a hung connection from living forever.
                client.Timeout = TimeSpan.FromSeconds(gemini.TimeoutSeconds + 10);
            })
            .AddStandardResilienceHandler()
            .Configure((resilience, provider) =>
            {
                var gemini = provider.GetRequiredService<IOptions<MatchingOptions>>().Value.Gemini;
                var seconds = gemini.TimeoutSeconds;
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(seconds);
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(Math.Max(1, seconds / 2.0));
                resilience.Retry.MaxRetryAttempts = 1;
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(300);
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, seconds * 2 + 1));
                resilience.CircuitBreaker.MinimumThroughput = gemini.CircuitBreakerMinimumRequests;
                resilience.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(gemini.CircuitBreakerSeconds);
            });

        services.AddScoped<IShiftMatcher>(provider =>
        {
            var aiEnabled = provider.GetRequiredService<IOptions<MatchingOptions>>().Value.Gemini.Enabled;
            return new ResilientShiftMatcher(
                provider.GetRequiredService<RuleBasedShiftMatcher>(),
                aiEnabled ? provider.GetRequiredService<GeminiShiftMatcher>() : null,
                provider.GetRequiredService<ILogger<ResilientShiftMatcher>>());
        });
    }

    private static void AddWebPlumbing(this IServiceCollection services)
    {
        services.AddControllersWithViews(options =>
        {
            // Every non-GET request must carry an anti-forgery token, so no state-changing action can be added without one.
            options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
            options.Filters.Add<DomainExceptionFilter>();
        });
        services.AddRazorPages();

        // The "flash" messages shown after an action travel in a short-lived cookie; give it the same flags as the others.
        services.Configure<CookieTempDataProviderOptions>(options =>
        {
            options.Cookie.Name = "teamlocum.flash";
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "teamlocum.antiforgery";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                context.HttpContext.Response.ContentType = "text/plain";
                return new ValueTask(context.HttpContext.Response.WriteAsync("Too many requests. Please wait a minute and try again."));
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var settings = http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                // Probes and static files (stylesheets, scripts, images) are cheap and every page view fetches several of
                // them, so counting them would throttle ordinary browsing. No page route ends in a file extension.
                if (!settings.Enabled || http.Request.Path.StartsWithSegments("/health") || Path.HasExtension(http.Request.Path.Value))
                {
                    return RateLimitPartition.GetNoLimiter("unlimited");
                }

                var client = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var isCredentialPost = HttpMethods.IsPost(http.Request.Method) &&
                    (http.Request.Path.StartsWithSegments("/Identity/Account/Login", StringComparison.OrdinalIgnoreCase) ||
                     http.Request.Path.StartsWithSegments("/Identity/Account/Register", StringComparison.OrdinalIgnoreCase));

                return isCredentialPost
                    ? RateLimitPartition.GetFixedWindowLimiter($"credentials:{client}", _ => Window(settings.SignInPerMinute))
                    : RateLimitPartition.GetFixedWindowLimiter($"global:{client}", _ => Window(settings.GlobalPerMinute));
            });
        });

        services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("database", tags: ["ready"]);
    }

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    };
}
