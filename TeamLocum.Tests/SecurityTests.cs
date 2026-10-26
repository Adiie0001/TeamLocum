using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TeamLocum.Tests.TestSupport;
using TeamLocum.Web.Domain;
using static TeamLocum.Tests.TestSupport.Flows;

namespace TeamLocum.Tests;

public sealed partial class SecurityTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [GeneratedRegex(@"<script(?![^>]*\bsrc=)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScript();

    [GeneratedRegex(@"\sstyle\s*=|\son[a-z]+\s*=|javascript:", RegexOptions.IgnoreCase)]
    private static partial Regex InlineStyleOrHandler();

    // ───────────── cross-site request forgery ─────────────

    [Theory]
    [InlineData("/Locums/Approve/1")]
    [InlineData("/Clients/Approve/1")]
    [InlineData("/Holidays/Create")]
    [InlineData("/Bookings/Create")]
    [InlineData("/Bookings/Cancel/1")]
    [InlineData("/Admin/Unlock/someone")]
    public async Task State_changing_posts_without_an_anti_forgery_token_are_rejected(string path)
    {
        var admin = await factory.AdminAsync();

        var response = await admin.PostNoTokenAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/Identity/Account/Login")]
    [InlineData("/Identity/Account/Register")]
    public async Task Sign_in_and_registration_forms_also_require_a_token(string path)
    {
        var response = await factory.NewClient().PostNoTokenAsync(path, [F("Input.Email", "a@b.test"), F("Input.Password", "Whatever-123")]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_token_from_another_session_is_not_accepted()
    {
        var admin = await factory.AdminAsync();
        var attacker = factory.NewClient();
        var attackerToken = await attacker.TokenFromAsync("/Identity/Account/Login");

        var response = await admin.PostWithTokenAsync("/Holidays/Create",
            [F("Date", "2031-01-01"), F("Details", "Forged")], attackerToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await factory.WithDbAsync(db => db.Holidays.AnyAsync(h => h.Details == "Forged")));
    }

    [Fact]
    public async Task Approval_links_cannot_be_triggered_by_simply_visiting_them()
    {
        var doctor = await factory.AddDoctorAsync(approved: false);
        var hospital = await factory.AddHospitalAsync(approved: false);
        var admin = await factory.AdminAsync();

        foreach (var url in new[]
                 {
                     $"/Locums/Approve/{doctor.ProfileId}", $"/Locums/Reject/{doctor.ProfileId}",
                     $"/Clients/Approve/{hospital.ProfileId}", $"/Clients/Reject/{hospital.ProfileId}"
                 })
        {
            var response = await admin.GetAsync(url);
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{url} answered {(int)response.StatusCode}");
        }

        Assert.Equal(LocumStatus.PendingReview, (await factory.WithDbAsync(db => db.Locums.SingleAsync(l => l.Id == doctor.ProfileId))).Status);
        Assert.Equal(ClientStatus.AwaitingReview, (await factory.WithDbAsync(db => db.Clients.SingleAsync(c => c.Id == hospital.ProfileId))).Status);
    }

    // ───────────── headers and cookies ─────────────

    [Fact]
    public async Task Every_response_carries_the_security_headers()
    {
        var response = await factory.NewClient().GetAsync("/");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
    }

    [Fact]
    public async Task Signed_in_pages_are_marked_not_to_be_cached()
    {
        var hospital = await factory.AddHospitalAsync();
        var client = await factory.SignInAsync(hospital);

        var response = await client.GetAsync("/Bookings");

        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Pages_that_render_a_form_still_end_up_with_a_single_deny_framing_header()
    {
        // The anti-forgery service adds its own X-Frame-Options (SAMEORIGIN) when it renders a token; ours must win.
        var response = await factory.NewClient().GetAsync("/Identity/Account/Login");

        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task The_session_and_anti_forgery_cookies_are_not_readable_by_scripts_and_not_sent_cross_site()
    {
        var hospital = await factory.AddHospitalAsync();
        var client = factory.NewClient();
        var loginPage = await client.GetAsync("/Identity/Account/Login");
        var antiforgery = loginPage.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("teamlocum.antiforgery"));
        Assert.Contains("httponly", antiforgery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", antiforgery, StringComparison.OrdinalIgnoreCase);

        var login = await client.LoginAsync(hospital.Email, hospital.Password);
        var session = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("teamlocum.auth"));

        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", session, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_flash_message_cookie_is_script_proof_and_named_like_the_others()
    {
        var admin = await factory.AdminAsync();

        var added = await admin.PostFormAsync("/Holidays/Create", [F("Date", "2034-02-02"), F("Details", "Cookie check")], "/Holidays/Create");

        var flash = added.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("teamlocum.flash"));
        Assert.Contains("httponly", flash, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", flash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_supplied_correlation_id_is_echoed_and_an_unsafe_one_is_replaced()
    {
        var client = factory.NewClient();
        using var good = new HttpRequestMessage(HttpMethod.Get, "/");
        good.Headers.Add("X-Correlation-ID", "support-ticket_4711.a");
        using var bad = new HttpRequestMessage(HttpMethod.Get, "/");
        bad.Headers.TryAddWithoutValidation("X-Correlation-ID", "has spaces <b>");

        var goodResponse = await client.Http.SendAsync(good);
        var badResponse = await client.Http.SendAsync(bad);

        Assert.Equal("support-ticket_4711.a", goodResponse.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Matches("^[0-9a-f]{32}$", badResponse.Headers.GetValues("X-Correlation-ID").Single());
    }

    // ───────────── pages that must work under a strict content security policy ─────────────

    [Fact]
    public async Task No_page_relies_on_inline_script_inline_style_or_event_handler_attributes()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var admin = await factory.AdminAsync();
        var hospitalClient = await factory.SignInAsync(hospital);
        var doctorClient = await factory.SignInAsync(doctor);
        await doctorClient.ApplyAsync(shiftId);

        var pages = new List<(BrowserClient Client, string Path)>
        {
            (factory.NewClient(), "/"), (factory.NewClient(), "/Home/Privacy"),
            (factory.NewClient(), "/Identity/Account/Login"), (factory.NewClient(), "/Identity/Account/Register"),
            (admin, "/"), (admin, "/Admin"), (admin, "/Admin/Users"), (admin, "/Clients"), (admin, "/Locums"),
            (admin, "/Holidays"), (admin, "/Holidays/Create"), (admin, "/Bookings"), (admin, "/Bookings/Create"),
            (hospitalClient, "/"), (hospitalClient, "/Bookings"), (hospitalClient, $"/Bookings/Details/{shiftId}"),
            (hospitalClient, $"/Bookings/Edit/{shiftId}"), (hospitalClient, "/Locums"), (hospitalClient, "/Clients/Profile"),
            (doctorClient, "/"), (doctorClient, "/Bookings"), (doctorClient, $"/Bookings/Details/{shiftId}"),
            (doctorClient, "/Locums/Profile"), (doctorClient, "/Identity/Account/Manage/Index"),
            (doctorClient, "/Identity/Account/Manage/ChangePassword")
        };

        foreach (var (client, path) in pages)
        {
            var response = await client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)response.StatusCode}");
            var html = await response.Content.ReadAsStringAsync();
            Assert.False(InlineScript().IsMatch(html), $"{path} contains an inline <script>");
            Assert.False(InlineStyleOrHandler().IsMatch(html), $"{path} contains an inline style, event handler or javascript: URL");
        }
    }

    [Fact]
    public async Task Unknown_pages_get_a_friendly_404_with_a_reference_and_no_internals()
    {
        var client = factory.NewClient();

        var response = await client.GetAsync("/definitely/not/here");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("We could not find that page", html);
        Assert.Contains("Reference:", html);
    }

    [Fact]
    public async Task Unhandled_errors_show_a_generic_page_and_never_the_exception()
    {
        var client = factory.NewClient();

        var response = await client.GetAsync("/test/boom");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Something went wrong on our side", html);
        Assert.DoesNotContain("internal detail", html);
        Assert.DoesNotContain("InvalidOperationException", html);
        Assert.Contains(response.Headers.GetValues("X-Correlation-ID").Single(), html);
    }
}

public sealed class RateLimitTests
{
    [Fact]
    public async Task Sign_in_attempts_are_limited_per_client_and_the_answer_says_when_to_retry()
    {
        using var factory = AppFactory.With(new() { ["RateLimiting:Enabled"] = "true", ["RateLimiting:SignInPerMinute"] = "3" });
        var client = factory.NewClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await client.LoginAsync("nobody@clinic.test", "Wrong-Password-1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var limited = await client.LoginAsync("nobody@clinic.test", "Wrong-Password-1");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(int.Parse(limited.Headers.GetValues("Retry-After").Single()) > 0);
        Assert.Contains("Too many requests", await limited.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Static_files_do_not_use_up_a_visitors_page_allowance()
    {
        using var factory = AppFactory.With(new() { ["RateLimiting:Enabled"] = "true", ["RateLimiting:GlobalPerMinute"] = "5" });
        var client = factory.NewClient();

        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/css/site.css")).StatusCode);
        }

        for (var page = 1; page <= 5; page++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Home/Privacy")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/Home/Privacy")).StatusCode);
    }

    [Fact]
    public async Task Health_probes_and_ordinary_pages_are_not_throttled_by_the_sign_in_limit()
    {
        using var factory = AppFactory.With(new() { ["RateLimiting:Enabled"] = "true", ["RateLimiting:SignInPerMinute"] = "2" });
        var client = factory.NewClient();

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Home/Privacy")).StatusCode);
        }
    }
}
