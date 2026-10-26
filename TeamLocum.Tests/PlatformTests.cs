using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamLocum.Tests.TestSupport;
using TeamLocum.Web.ViewModels;
using static TeamLocum.Tests.TestSupport.Flows;

namespace TeamLocum.Tests;

/// <summary>Everything around the features: probes, static files, the framework's own pages, formats and the runtime setup.</summary>
public sealed partial class PlatformTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [GeneratedRegex(@"<(?:script|link)\b[^>]*?\b(?:src|href)=""([^""]+)""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex AssetReference();

    // ───────────── probes ─────────────

    [Fact]
    public async Task Liveness_and_readiness_answer_healthy_without_signing_in()
    {
        var client = factory.NewClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
        Assert.NotEmpty(live.Headers.GetValues("X-Correlation-ID"));
    }

    [Fact]
    public async Task Readiness_fails_when_the_database_cannot_be_reached_but_liveness_stays_up()
    {
        using var broken = AppFactory.With([]);
        var client = broken.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);

        // The connection string is read whenever a database context is created, so the next probe sees the change.
        broken.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:DefaultConnection"] =
            "Data Source=/no/such/directory/teamlocum.db;Mode=ReadOnly";

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    // ───────────── the pages and assets a browser needs ─────────────

    [Fact]
    public async Task Every_script_and_stylesheet_a_page_loads_comes_from_this_site_and_exists()
    {
        var doctor = await factory.SignInAsync(await factory.AddDoctorAsync());
        var pages = new List<(BrowserClient Client, string Path)>
        {
            (factory.NewClient(), "/"), (factory.NewClient(), "/Identity/Account/Login"), (factory.NewClient(), "/Identity/Account/Register"),
            (doctor, "/Bookings"), (doctor, "/Locums/Profile"), (doctor, "/Identity/Account/Manage/Index"),
            (doctor, "/Identity/Account/Manage/ChangePassword")
        };

        foreach (var (client, path) in pages)
        {
            var html = await client.GetHtmlAsync(path);
            var references = AssetReference().Matches(html).Select(m => m.Groups[1].Value).ToList();

            Assert.NotEmpty(references);
            foreach (var url in references)
            {
                Assert.True(url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal), $"{path} loads {url} from another site");
                var asset = await client.GetAsync(url);
                Assert.True(asset.StatusCode == HttpStatusCode.OK, $"{path} references {url}, which answered {(int)asset.StatusCode}");
            }
        }
    }

    [Fact]
    public async Task The_account_settings_and_access_denied_pages_render_inside_the_application_layout()
    {
        var client = await factory.SignInAsync(await factory.AddDoctorAsync());

        foreach (var path in new[] { "/Identity/Account/Manage/Index", "/Identity/Account/Manage/ChangePassword", "/Identity/Account/AccessDenied" })
        {
            var html = await client.GetHtmlAsync(path);

            Assert.Contains("navbar-brand", html);
            Assert.DoesNotContain("/Identity/css/site.css", html);
        }
    }

    [Fact]
    public async Task A_doctor_who_is_sent_to_access_denied_sees_a_page_and_not_an_error()
    {
        var pending = await factory.SignInAsync(await factory.AddDoctorAsync(approved: false));
        var hospital = await factory.AddHospitalAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));

        var apply = await pending.PostFormAsync($"/Bookings/Apply/{shiftId}");
        var denied = await pending.GetAsync(BrowserClient.Location(apply));

        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
        Assert.Contains("Access denied", await denied.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_rule_broken_on_a_page_that_is_only_viewed_is_explained_with_a_400_page()
    {
        var hospital = await factory.AddHospitalAsync();
        var started = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(-2), TimeSpan.FromHours(8));
        var owner = await factory.SignInAsync(hospital);

        var response = await owner.GetAsync($"/Bookings/Edit/{started}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("A shift that has started can no longer be edited.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_cancelled_shift_cannot_be_opened_for_editing()
    {
        var hospital = await factory.AddHospitalAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var owner = await factory.SignInAsync(hospital);
        await owner.ActionAsync("Cancel", shiftId);

        var response = await owner.GetAsync($"/Bookings/Edit/{shiftId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("A Cancelled shift can no longer be changed.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_hospital_that_is_no_longer_approved_cannot_open_the_edit_form_either()
    {
        var hospital = await factory.AddHospitalAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var owner = await factory.SignInAsync(hospital);
        var admin = await factory.AdminAsync();
        await admin.PostFormAsync($"/Clients/Suspend/{hospital.ProfileId}", tokenPage: "/Clients");

        var response = await owner.GetAsync($"/Bookings/Edit/{shiftId}");

        Assert.True(response.IsRedirect());
        Assert.Contains("AccessDenied", BrowserClient.Location(response));
    }

    [Fact]
    public async Task The_privacy_page_says_plainly_that_this_is_a_demonstration()
    {
        var html = await factory.NewClient().GetHtmlAsync("/Home/Privacy");

        Assert.Contains("portfolio demonstration", html);
    }

    [Fact]
    public async Task Every_role_lands_on_its_own_dashboard()
    {
        var admin = await (await factory.AdminAsync()).GetHtmlAsync("/");
        var hospital = await (await factory.SignInAsync(await factory.AddHospitalAsync())).GetHtmlAsync("/");
        var doctor = await (await factory.SignInAsync(await factory.AddDoctorAsync())).GetHtmlAsync("/");
        var visitor = await factory.NewClient().GetHtmlAsync("/");

        Assert.Contains("Hospitals awaiting approval", admin);
        Assert.Contains("Applications to review", hospital);
        Assert.Contains("Open shifts to apply for", doctor);
        Assert.DoesNotContain("awaiting approval", visitor);
        Assert.Contains("Register", visitor);
    }

    [Fact]
    public async Task Money_and_dates_are_shown_the_UK_way()
    {
        var hospital = await factory.AddHospitalAsync();
        var owner = await factory.SignInAsync(hospital);
        var start = new DateTime(2031, 3, 4, 9, 0, 0);
        var posted = await owner.PostShiftAsync(start, start.AddHours(8), rate: 85.5m);

        var html = await owner.GetHtmlAsync($"/Bookings/Details/{BookingIdFrom(posted)}");

        Assert.Contains("&#xA3;85.50", html);
        Assert.Contains("Tue 4 Mar 2031, 09:00", html);
    }

    [Fact]
    public async Task The_time_an_application_was_sent_is_shown_in_clinic_time_not_in_utc()
    {
        using var summer = AppFactory.With([]);
        summer.Clock.Advance(new DateTimeOffset(2031, 7, 1, 12, 0, 0, TimeSpan.Zero) - DateTimeOffset.UtcNow);
        var hospital = await summer.AddHospitalAsync();
        var doctor = await summer.AddDoctorAsync();
        var shiftId = await summer.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        await (await summer.SignInAsync(doctor)).ApplyAsync(shiftId);

        var html = await (await summer.SignInAsync(hospital)).GetHtmlAsync($"/Bookings/Details/{shiftId}");

        // 12:00 UTC on 1 July is 13:00 in London (British Summer Time).
        Assert.Contains("1 Jul 13:", html);
        Assert.DoesNotContain("1 Jul 12:", html);
    }

    // ───────────── runtime set-up ─────────────

    [Fact]
    public void The_web_app_is_published_with_culture_data_because_it_formats_for_en_GB()
    {
        // The test host runs under the test project's own runtime settings, so a real start-up crash caused by
        // invariant globalization would go unnoticed unless the web project's settings are read directly.
        var settingsFile = Path.Combine(AppContext.BaseDirectory, "TeamLocum.Web.runtimeconfig.json");
        using var settings = JsonDocument.Parse(File.ReadAllText(settingsFile));
        var properties = settings.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");

        var invariant = properties.TryGetProperty("System.Globalization.Invariant", out var value) && value.GetBoolean();

        Assert.False(invariant, "Invariant globalization makes the en-GB request culture fail at start-up.");
        Assert.Equal("£1,234.50", UiHelpers.Money(1234.5m));
    }
}
