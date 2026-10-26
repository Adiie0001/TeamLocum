using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TeamLocum.Tests.TestSupport;
using TeamLocum.Web.Data;
using TeamLocum.Web.Domain;
using TeamLocum.Web.Infrastructure.Options;
using TeamLocum.Web.Services;
using static TeamLocum.Tests.TestSupport.Flows;

namespace TeamLocum.Tests;

/// <summary>The application must refuse to start with unsafe or contradictory configuration, and must say why.</summary>
public sealed class StartupConfigurationTests
{
    private static async Task<string> StartupFailureAsync(Dictionary<string, string?> overrides, string environment = "Production")
    {
        using var factory = AppFactory.With(overrides, environment);
        var failure = await Record.ExceptionAsync(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/health/live");
        });

        Assert.NotNull(failure);
        var messages = new List<string>();
        for (var current = failure; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    [Fact]
    public async Task Demo_data_is_refused_outside_development()
    {
        var message = await StartupFailureAsync(new() { ["Seed:DemoData"] = "true" });

        Assert.Contains("Seed:DemoData is only allowed in the Development environment", message);
    }

    [Fact]
    public async Task A_published_demo_password_cannot_be_used_for_the_bootstrap_admin_in_production()
    {
        var message = await StartupFailureAsync(new()
        {
            ["Seed:AdminEmail"] = "owner@clinic.test",
            ["Seed:AdminPassword"] = DemoCredentials.AdminPassword
        });

        Assert.Contains("published demo password", message);
    }

    [Theory]
    [InlineData("weak", "at least 10 characters")]
    [InlineData("alllowercase123", "upper-case letter")]
    [InlineData("ALLUPPERCASE123", "lower-case letter")]
    [InlineData("NoDigitsAtAllHere", "digit")]
    [InlineData("Aaaaaaaaa1", "4 different characters")]
    [InlineData("OwnerPassword9", "name part of the email")]
    public async Task A_weak_bootstrap_admin_password_is_rejected_with_the_reason(string password, string reason)
    {
        var message = await StartupFailureAsync(new() { ["Seed:AdminEmail"] = "ownerpassword@clinic.test", ["Seed:AdminPassword"] = password });

        Assert.Contains("Seed:AdminPassword", message);
        Assert.Contains(reason, message);
    }

    [Fact]
    public async Task The_bootstrap_admin_email_must_be_an_email_address()
    {
        var message = await StartupFailureAsync(new() { ["Seed:AdminEmail"] = "not-an-email" });

        Assert.Contains("not a valid email", message);
    }

    [Theory]
    [InlineData("Seed:AdminPassword")]
    [InlineData("Seed:AdminEmail")]
    public async Task The_bootstrap_admin_email_and_password_must_come_together(string missing)
    {
        var message = await StartupFailureAsync(new() { [missing] = null });

        Assert.Contains("must be provided together", message);
    }

    [Fact]
    public async Task Enabling_gemini_without_a_key_stops_startup()
    {
        var message = await StartupFailureAsync(new() { ["Matching:Gemini:Enabled"] = "true" });

        Assert.Contains("Matching:Gemini:ApiKey is required", message);
    }

    [Theory]
    [InlineData("Matching:Gemini:Model", "gemini/../../admin", "Model contains unsupported characters")]
    [InlineData("Matching:Gemini:BaseUrl", "ftp://example.test/", "BaseUrl must be an absolute http(s) URL")]
    [InlineData("Matching:Gemini:BaseUrl", "not a url", "BaseUrl must be an absolute http(s) URL")]
    [InlineData("Matching:Gemini:TimeoutSeconds", "0", "TimeoutSeconds")]
    [InlineData("Matching:Gemini:TimeoutSeconds", "61", "TimeoutSeconds")]
    [InlineData("Matching:Gemini:CacheMinutes", "-1", "CacheMinutes")]
    [InlineData("Matching:Gemini:CacheMinutes", "61", "CacheMinutes")]
    [InlineData("Matching:Gemini:CircuitBreakerMinimumRequests", "1", "CircuitBreakerMinimumRequests")]
    [InlineData("Matching:Gemini:CircuitBreakerSeconds", "0", "CircuitBreakerSeconds")]
    public async Task Unsafe_or_out_of_range_gemini_settings_are_rejected(string key, string value, string expected)
    {
        var settings = AppFactory.WithGemini();
        settings[key] = value;

        var message = await StartupFailureAsync(settings);

        Assert.Contains(expected, message);
    }

    [Fact]
    public async Task Gemini_settings_are_not_judged_while_gemini_is_switched_off()
    {
        using var factory = AppFactory.With(new() { ["Matching:Gemini:Model"] = "anything goes here ../", ["Matching:Gemini:BaseUrl"] = "nonsense" });

        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Theory]
    [InlineData("RateLimiting:SignInPerMinute", "0", "SignInPerMinute")]
    [InlineData("RateLimiting:GlobalPerMinute", "0", "GlobalPerMinute")]
    [InlineData("Matching:MaxResults", "0", "MaxResults")]
    [InlineData("Matching:MaxResults", "51", "MaxResults")]
    public async Task Out_of_range_numbers_are_rejected(string key, string value, string expected)
    {
        var message = await StartupFailureAsync(new() { [key] = value });

        Assert.Contains(expected, message);
    }

    [Fact]
    public async Task A_time_zone_that_does_not_exist_stops_startup()
    {
        var message = await StartupFailureAsync(new() { ["Clinic:TimeZone"] = "Mars/Olympus_Mons" });

        Assert.Contains("Clinic:TimeZone 'Mars/Olympus_Mons' is not a time zone on this machine", message);
    }

    [Fact]
    public async Task A_database_that_was_never_migrated_stops_startup_with_instructions()
    {
        var message = await StartupFailureAsync(new() { ["Database:ApplyMigrationsOnStartup"] = "false" });

        Assert.Contains("pending migration", message);
        Assert.Contains("Database__ApplyMigrationsOnStartup=true", message);
    }

    [Fact]
    public async Task Development_needs_no_admin_settings_seeds_demo_data_and_the_demo_logins_work()
    {
        using var factory = AppFactory.With(new()
        {
            ["Seed:DemoData"] = "true",
            ["Seed:AdminEmail"] = null,
            ["Seed:AdminPassword"] = null
        }, "Development");

        foreach (var (email, password) in new[]
                 {
                     (DemoCredentials.AdminEmail, DemoCredentials.AdminPassword),
                     (DemoCredentials.HospitalEmail, DemoCredentials.HospitalPassword),
                     (DemoCredentials.DoctorEmail, DemoCredentials.DoctorPassword)
                 })
        {
            var client = factory.NewClient();
            var login = await client.LoginAsync(email, password);
            Assert.True(login.IsRedirect(), $"{email} could not sign in");
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Bookings")).StatusCode);
        }

        await factory.WithDbAsync(async db =>
        {
            Assert.Equal(5, await db.Bookings.CountAsync());
            Assert.Equal(5, await db.Locums.CountAsync(l => l.Status == LocumStatus.Active));
            Assert.Equal(3, await db.Clients.CountAsync(c => c.Status == ClientStatus.Accepted));
            Assert.Equal(DemoDataSeeder.BankHolidays.Length, await db.Holidays.CountAsync());
            Assert.Equal(1, await db.Bookings.CountAsync(b => b.Status == BookingStatus.Completed));
            Assert.Equal(1, await db.Bookings.CountAsync(b => b.Status == BookingStatus.Filled));
            Assert.Equal(3, await db.ShiftApplications.CountAsync());
            return 0;
        });
    }

    [Fact]
    public async Task The_published_demo_doctors_use_placeholder_numbers_that_cannot_be_real_registrations()
    {
        using var factory = AppFactory.With(new() { ["Seed:DemoData"] = "true" }, "Development");
        using var client = factory.CreateClient();

        var numbers = await factory.WithDbAsync(db => db.Locums.Select(l => l.GmcNumber).ToListAsync());

        Assert.NotEmpty(numbers);
        Assert.All(numbers, number => Assert.StartsWith("00000", number));
        var emails = await factory.WithDbAsync(db => db.Users.Select(u => u.Email!).ToListAsync());
        Assert.All(emails, email => Assert.EndsWith("@example.com", email));
    }

    [Fact]
    public async Task Production_with_real_settings_starts_empty_with_only_the_bootstrap_admin()
    {
        using var factory = AppFactory.With([], "Production");
        var client = factory.NewClient();

        var demoLogin = await client.LoginAsync(DemoCredentials.AdminEmail, DemoCredentials.AdminPassword);
        Assert.Equal(HttpStatusCode.OK, demoLogin.StatusCode);

        var admin = await factory.AdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Admin")).StatusCode);

        await factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.Users.CountAsync());
            Assert.Equal(0, await db.Bookings.CountAsync());
            Assert.Equal(0, await db.Holidays.CountAsync());
            return 0;
        });
    }

    [Fact]
    public async Task The_error_page_in_production_never_shows_exception_details()
    {
        using var factory = AppFactory.With([], "Production");

        var response = await factory.NewClient().GetAsync("/test/boom");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("InvalidOperationException", html);
        Assert.DoesNotContain("internal detail", html);
        Assert.DoesNotContain("   at ", html);
    }

    [Fact]
    public async Task The_bootstrap_admin_is_created_once_and_never_duplicated()
    {
        using var factory = new AppFactory();
        using var client = factory.CreateClient();

        for (var run = 0; run < 2; run++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        }

        var admins = await factory.WithDbAsync(async db =>
            await (from user in db.Users
                   join userRole in db.UserRoles on user.Id equals userRole.UserId
                   join role in db.Roles on userRole.RoleId equals role.Id
                   where role.Name == Roles.Admin
                   select user.Id).CountAsync());
        Assert.Equal(1, admins);
    }

    [Fact]
    public async Task A_bootstrap_email_that_belongs_to_an_ordinary_account_is_never_promoted()
    {
        using var factory = AppFactory.With(new() { ["Seed:AdminEmail"] = null, ["Seed:AdminPassword"] = null });
        using var client = factory.CreateClient();
        var doctor = await factory.AddDoctorAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var initializer = ActivatorUtilities.CreateInstance<DatabaseInitializer>(
            scope.ServiceProvider,
            Microsoft.Extensions.Options.Options.Create(new SeedOptions { AdminEmail = doctor.Email, AdminPassword = "Chosen-Passw0rd" }));
        await initializer.InitializeAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var account = await users.FindByEmailAsync(doctor.Email);
        Assert.Equal([Roles.Locum], await users.GetRolesAsync(account!));
        Assert.Empty(await users.GetUsersInRoleAsync(Roles.Admin));
    }

    [Fact]
    public async Task Cookie_and_token_keys_are_written_to_the_configured_folder()
    {
        var keys = Path.Combine(Path.GetTempPath(), $"teamlocum-keys-{Guid.NewGuid():N}");
        try
        {
            using var factory = AppFactory.With(new() { ["DataProtection:KeysPath"] = keys });
            var admin = await factory.AdminAsync();

            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Admin")).StatusCode);
            Assert.NotEmpty(Directory.GetFiles(keys, "key-*.xml"));
        }
        finally
        {
            if (Directory.Exists(keys))
            {
                Directory.Delete(keys, recursive: true);
            }
        }
    }

    [Fact]
    public async Task When_https_redirection_is_switched_on_plain_http_is_sent_to_https()
    {
        using var factory = AppFactory.With(new() { ["Security:HttpsRedirection"] = "true", ["HTTPS_PORT"] = "443" });
        var client = factory.NewClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.RedirectKeepVerb, response.StatusCode);
        Assert.StartsWith("https://", response.Headers.Location!.ToString());
    }

}
