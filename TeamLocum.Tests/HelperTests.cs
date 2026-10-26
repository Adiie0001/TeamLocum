using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TeamLocum.Web.Data;
using TeamLocum.Web.Domain;
using TeamLocum.Web.Infrastructure;
using TeamLocum.Web.Infrastructure.Middleware;
using TeamLocum.Web.Infrastructure.Options;
using TeamLocum.Web.Services;
using TeamLocum.Web.ViewModels;

namespace TeamLocum.Tests;

public sealed class UiHelpersTests
{
    [Theory]
    [InlineData(85, "£85.00")]
    [InlineData(0, "£0.00")]
    [InlineData(1234.5, "£1,234.50")]
    [InlineData(0.1, "£0.10")]
    public void Money_is_pounds_with_pence_and_thousands_separators(double amount, string expected)
    {
        Assert.Equal(expected, UiHelpers.Money((decimal)amount));
    }

    [Fact]
    public void A_shift_window_names_the_day_and_crosses_midnight_clearly()
    {
        Assert.Equal("Tue 20 Oct 2026, 09:00–17:00",
            UiHelpers.Window(new DateTime(2026, 10, 20, 9, 0, 0), new DateTime(2026, 10, 20, 17, 0, 0)));
        Assert.Equal("Tue 20 Oct 2026 22:00 – Wed 21 Oct 06:00",
            UiHelpers.Window(new DateTime(2026, 10, 20, 22, 0, 0), new DateTime(2026, 10, 21, 6, 0, 0)));
    }

    [Fact]
    public void Day_and_month_names_do_not_depend_on_the_machines_culture_data()
    {
        Assert.Equal("Tue 15 Sep 2026", UiHelpers.Date(new DateOnly(2026, 9, 15)));
        Assert.Equal("15 Sep 09:05", UiHelpers.Moment(new DateTime(2026, 9, 15, 9, 5, 0)));
        Assert.Equal("1 Sep 2026", UiHelpers.Day(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Dates_hours_and_form_values_use_the_formats_people_and_browsers_expect()
    {
        Assert.Equal("Fri 25 Dec 2026", UiHelpers.Date(new DateOnly(2026, 12, 25)));
        Assert.Equal("8 h", UiHelpers.Hours(TimeSpan.FromHours(8)));
        Assert.Equal("7.5 h", UiHelpers.Hours(TimeSpan.FromHours(7.5)));
        Assert.Equal("1.5 h", UiHelpers.Hours(TimeSpan.FromMinutes(90)));
        Assert.Equal("2026-10-20T09:05", UiHelpers.ForInput(new DateTime(2026, 10, 20, 9, 5, 0)));
        Assert.Equal(string.Empty, UiHelpers.ForInput(null));
    }

    [Fact]
    public void Every_status_has_a_badge_and_the_review_states_have_readable_labels()
    {
        Assert.All(Enum.GetValues<BookingStatus>(), s => Assert.StartsWith("text-bg-", UiHelpers.Badge(s)));
        Assert.All(Enum.GetValues<LocumStatus>(), s => Assert.StartsWith("text-bg-", UiHelpers.Badge(s)));
        Assert.All(Enum.GetValues<ClientStatus>(), s => Assert.StartsWith("text-bg-", UiHelpers.Badge(s)));
        Assert.All(Enum.GetValues<ApplicationStatus>(), s => Assert.StartsWith("text-bg-", UiHelpers.Badge(s)));

        Assert.Equal("text-bg-success", UiHelpers.Badge(BookingStatus.Filled));
        Assert.Equal("text-bg-warning", UiHelpers.Badge(LocumStatus.PendingReview));
        Assert.Equal("text-bg-danger", UiHelpers.Badge(ClientStatus.Suspended));
        Assert.Equal("text-bg-secondary", UiHelpers.Badge(ApplicationStatus.Withdrawn));

        Assert.Equal("Awaiting review", UiHelpers.Label(ClientStatus.AwaitingReview));
        Assert.Equal("Pending review", UiHelpers.Label(LocumStatus.PendingReview));
        Assert.Equal("Suspended", UiHelpers.Label(LocumStatus.Suspended));
    }
}

public sealed class CorrelationIdTests
{
    [Theory]
    [InlineData("abc-123_DEF.9", "abc-123_DEF.9")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123", "0123456789012345678901234567890123456789012345678901234567890123")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234", null)]
    [InlineData("has space", null)]
    [InlineData("semi;colon", null)]
    [InlineData("<script>", null)]
    [InlineData("line\nbreak", null)]
    [InlineData("café", null)]
    public void Only_short_plain_identifiers_are_accepted_from_callers(string? supplied, string? expected)
    {
        Assert.Equal(expected, CorrelationIdMiddleware.Accept(supplied));
    }
}

public sealed class DomainExceptionFilterTests
{
    private sealed class MemoryTempData : ITempDataProvider
    {
        public Dictionary<string, object?> Saved { get; private set; } = [];

        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>(Saved);

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) => Saved = new Dictionary<string, object?>(values);
    }

    private sealed class Factory(MemoryTempData provider) : ITempDataDictionaryFactory
    {
        public ITempDataDictionary GetTempData(HttpContext context) => new TempDataDictionary(context, provider);
    }

    private static (DomainExceptionFilter Filter, MemoryTempData Store) NewFilter()
    {
        var store = new MemoryTempData();
        return (new DomainExceptionFilter(new Factory(store), NullLogger<DomainExceptionFilter>.Instance), store);
    }

    private static ExceptionContext ContextFor(Exception exception, string method = "GET", string? referer = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Host = new HostString("clinic.test");
        if (referer is not null)
        {
            http.Request.Headers.Referer = referer;
        }

        return new ExceptionContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [])
        {
            Exception = exception
        };
    }

    [Fact]
    public void A_record_that_does_not_exist_is_a_404_and_a_missing_permission_is_a_forbid()
    {
        var (filter, _) = NewFilter();
        var missing = ContextFor(new NotFoundException("nope"));
        var forbidden = ContextFor(new ForbiddenException("no"));

        filter.OnException(missing);
        filter.OnException(forbidden);

        Assert.IsType<NotFoundResult>(missing.Result);
        Assert.True(missing.ExceptionHandled);
        Assert.IsType<ForbidResult>(forbidden.Result);
        Assert.True(forbidden.ExceptionHandled);
    }

    [Fact]
    public void A_rule_broken_by_a_form_post_is_flashed_and_the_user_is_sent_back_to_the_page_they_came_from()
    {
        var (filter, store) = NewFilter();
        var context = ContextFor(new ConflictException("That doctor already has a shift."), "POST", "http://clinic.test/Bookings/Details/4?tab=x");

        filter.OnException(context);

        var redirect = Assert.IsType<LocalRedirectResult>(context.Result);
        Assert.Equal("/Bookings/Details/4?tab=x", redirect.Url);
        Assert.Equal("That doctor already has a shift.", store.Saved[DomainExceptionFilter.ErrorKey]);
        Assert.True(context.ExceptionHandled);
    }

    [Theory]
    [InlineData(typeof(ConflictException), 409)]
    [InlineData(typeof(InvalidTransitionException), 400)]
    [InlineData(typeof(DomainException), 400)]
    public void A_rule_broken_on_a_page_that_is_only_viewed_renders_an_explanation(Type exceptionType, int status)
    {
        var (filter, _) = NewFilter();
        var context = ContextFor((Exception)Activator.CreateInstance(exceptionType, "Explained.")!);

        filter.OnException(context);

        var view = Assert.IsType<ViewResult>(context.Result);
        Assert.Equal(status, view.StatusCode);
        Assert.Equal("~/Views/Shared/Problem.cshtml", view.ViewName);
        Assert.Equal("Explained.", view.ViewData.Model);
    }

    [Fact]
    public void Failures_that_are_not_domain_rules_are_left_for_the_error_page()
    {
        var (filter, _) = NewFilter();
        var context = ContextFor(new InvalidOperationException("a bug"));

        filter.OnException(context);

        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
    }

    [Theory]
    [InlineData("http://clinic.test/Locums?search=a", "/Locums?search=a")]
    [InlineData("https://CLINIC.test/Bookings", "/Bookings")]
    [InlineData("http://clinic.test", "/")]
    [InlineData("http://evil.test/Bookings", "/")]
    [InlineData("http://clinic.test:8080/Bookings", "/")]
    [InlineData("//evil.test/Bookings", "/")]
    [InlineData("/Bookings", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData("not a url", "/")]
    [InlineData("", "/")]
    public void The_way_back_only_ever_leads_to_this_site(string referer, string expected)
    {
        var http = new DefaultHttpContext();
        http.Request.Host = new HostString("clinic.test");
        http.Request.Headers.Referer = referer;

        Assert.Equal(expected, DomainExceptionFilter.ReturnTarget(http));
    }
}

public sealed class PasswordRulesTests
{
    [Theory]
    [InlineData("Correct-Horse-9", true)]
    [InlineData("Abcdefghi1", true)]
    [InlineData("Short1a", false)]
    [InlineData("nouppercase123", false)]
    [InlineData("NOLOWERCASE123", false)]
    [InlineData("NoDigitsHere!!", false)]
    [InlineData("Aaaaaaaaa1", false)]
    public void Passwords_must_be_long_mixed_and_varied(string password, bool acceptable)
    {
        Assert.Equal(acceptable, PasswordRules.Validate(password).Count == 0);
    }

    [Fact]
    public void A_password_may_not_contain_the_name_part_of_the_email_address()
    {
        Assert.Contains(PasswordRules.Validate("Surgery-Manager1", "surgery@clinic.test"), e => e.Contains("name part of the email"));
        Assert.Empty(PasswordRules.Validate("Surgery-Manager1", "ab@clinic.test"));
        Assert.Empty(PasswordRules.Validate("Correct-Horse-9", "surgery@clinic.test"));
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var problems = PasswordRules.Validate("abc");

        Assert.Equal(4, problems.Count);
    }

    [Theory]
    [InlineData(DemoCredentials.AdminPassword, true)]
    [InlineData(DemoCredentials.HospitalPassword, true)]
    [InlineData(DemoCredentials.DoctorPassword, true)]
    [InlineData("Sup3r-Secret-Pass", false)]
    public void The_published_demo_passwords_are_recognised(string password, bool demo)
    {
        Assert.Equal(demo, DemoCredentials.IsDemoPassword(password));
    }
}

public sealed class OptionsValidatorTests
{
    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "TeamLocum.Web";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string[] Failures(string environment, SeedOptions options)
    {
        var result = new SeedOptionsValidator(new Environment(environment)).Validate(null, options);
        return result.Failed ? [.. result.Failures] : [];
    }

    [Fact]
    public void Demo_data_is_a_development_only_switch()
    {
        Assert.Empty(Failures(Environments.Development, new SeedOptions { DemoData = true }));
        Assert.Contains("only allowed in the Development environment",
            Assert.Single(Failures(Environments.Production, new SeedOptions { DemoData = true })));
        Assert.Contains("only allowed in the Development environment",
            Assert.Single(Failures(Environments.Staging, new SeedOptions { DemoData = true })));
    }

    [Fact]
    public void The_bootstrap_admin_needs_both_halves_a_real_address_and_a_strong_password()
    {
        Assert.Empty(Failures(Environments.Production, new SeedOptions()));
        Assert.Empty(Failures(Environments.Production, new SeedOptions { AdminEmail = "owner@clinic.test", AdminPassword = "Sup3r-Secret-Pass" }));
        Assert.Contains("provided together", Assert.Single(Failures(Environments.Production, new SeedOptions { AdminEmail = "owner@clinic.test" })));
        Assert.Contains("provided together", Assert.Single(Failures(Environments.Production, new SeedOptions { AdminPassword = "Sup3r-Secret-Pass" })));
        Assert.Contains("not a valid email", Assert.Single(Failures(Environments.Production, new SeedOptions { AdminEmail = "nope", AdminPassword = "Sup3r-Secret-Pass" })));
        Assert.True(Failures(Environments.Production, new SeedOptions { AdminEmail = "owner@clinic.test", AdminPassword = "x" }).Length >= 3);
    }

    [Fact]
    public void A_published_demo_password_is_fine_on_a_developer_machine_and_nowhere_else()
    {
        var options = new SeedOptions { AdminEmail = "owner@clinic.test", AdminPassword = DemoCredentials.AdminPassword };

        Assert.Empty(Failures(Environments.Development, options));
        Assert.Contains("published demo password", Assert.Single(Failures(Environments.Production, options)));
    }

    [Fact]
    public void The_clinic_time_zone_must_exist_on_this_machine()
    {
        var validator = new ClinicOptionsValidator();

        Assert.True(validator.Validate(null, new ClinicOptions { TimeZone = "Europe/London" }).Succeeded);
        Assert.True(validator.Validate(null, new ClinicOptions { TimeZone = "America/New_York" }).Succeeded);
        Assert.Contains("Mars/Base", Assert.Single(validator.Validate(null, new ClinicOptions { TimeZone = "Mars/Base" }).Failures!));
    }

    [Fact]
    public void Gemini_settings_are_only_judged_when_gemini_is_switched_on()
    {
        var validator = new MatchingOptionsValidator();
        var junk = new MatchingOptions { Gemini = new GeminiOptions { Enabled = false, Model = "../../x", BaseUrl = "nonsense", TimeoutSeconds = 999 } };
        var good = new MatchingOptions { Gemini = new GeminiOptions { Enabled = true, ApiKey = "key" } };
        var broken = new MatchingOptions { Gemini = new GeminiOptions { Enabled = true, ApiKey = " ", Model = "../../x", BaseUrl = "nonsense", TimeoutSeconds = 0, CacheMinutes = 99, CircuitBreakerMinimumRequests = 1, CircuitBreakerSeconds = 0 } };

        Assert.True(validator.Validate(null, junk).Succeeded);
        Assert.True(validator.Validate(null, good).Succeeded);
        Assert.Equal(7, validator.Validate(null, broken).Failures!.Count());
    }
}

public sealed class ClinicClockTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static ClinicClock Clock(string utc, string zone = "Europe/London") =>
        new(new FixedTime(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture)),
            Microsoft.Extensions.Options.Options.Create(new ClinicOptions { TimeZone = zone }));

    [Theory]
    [InlineData("2026-01-15T12:00:00Z", "2026-01-15 12:00:00")]
    [InlineData("2026-07-01T12:00:00Z", "2026-07-01 13:00:00")]
    [InlineData("2026-03-29T00:59:59Z", "2026-03-29 00:59:59")]
    [InlineData("2026-03-29T01:00:00Z", "2026-03-29 02:00:00")]
    [InlineData("2026-10-25T00:30:00Z", "2026-10-25 01:30:00")]
    [InlineData("2026-10-25T01:30:00Z", "2026-10-25 01:30:00")]
    [InlineData("2026-12-31T23:30:00Z", "2026-12-31 23:30:00")]
    [InlineData("2026-06-30T23:30:00Z", "2026-07-01 00:30:00")]
    public void Now_is_the_wall_clock_in_the_clinic_zone_including_the_clock_changes(string utc, string expectedLocal)
    {
        Assert.Equal(DateTime.Parse(expectedLocal, System.Globalization.CultureInfo.InvariantCulture), Clock(utc).Now);
    }

    [Fact]
    public void Today_and_the_utc_time_follow_the_same_clock()
    {
        var clock = Clock("2026-06-30T23:30:00Z");

        Assert.Equal(new DateOnly(2026, 7, 1), clock.Today);
        Assert.Equal(DateTimeOffset.Parse("2026-06-30T23:30:00Z", System.Globalization.CultureInfo.InvariantCulture), clock.UtcNow);
    }

    [Fact]
    public void Any_stored_moment_can_be_shown_as_clinic_wall_clock_time()
    {
        var clock = Clock("2026-01-01T00:00:00Z");

        Assert.Equal(new DateTime(2026, 7, 1, 13, 0, 0), clock.ToLocal(DateTimeOffset.Parse("2026-07-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(new DateTime(2026, 1, 1, 12, 0, 0), clock.ToLocal(DateTimeOffset.Parse("2026-01-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(new DateTime(2026, 7, 1, 13, 0, 0), clock.ToLocal(DateTimeOffset.Parse("2026-07-01T20:00:00+08:00", System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void The_zone_is_configurable()
    {
        Assert.Equal(new DateTime(2026, 7, 1, 8, 0, 0), Clock("2026-07-01T12:00:00Z", "America/New_York").Now);
    }
}

public sealed class StorageConverterTests
{
    [Theory]
    [InlineData(85.55, 8555L)]
    [InlineData(0, 0L)]
    [InlineData(0.005, 1L)]
    [InlineData(-0.005, -1L)]
    [InlineData(85.554, 8555L)]
    [InlineData(85.555, 8556L)]
    [InlineData(999999.99, 99999999L)]
    public void Money_is_stored_as_whole_pence_rounded_away_from_zero(double pounds, long pence)
    {
        var converter = new DecimalToPenceConverter();

        Assert.Equal(pence, converter.ConvertToProvider((decimal)pounds));
        Assert.Equal(pence / 100m, converter.ConvertFromProvider(pence));
    }

    [Fact]
    public void Timestamps_round_trip_through_unix_milliseconds_without_losing_precision()
    {
        var converter = new UnixMillisecondsConverter();
        var moment = new DateTimeOffset(2026, 10, 15, 12, 30, 45, 123, TimeSpan.FromHours(1));

        var stored = (long)converter.ConvertToProvider(moment)!;
        var loaded = (DateTimeOffset)converter.ConvertFromProvider(stored)!;

        Assert.Equal(moment.ToUnixTimeMilliseconds(), stored);
        Assert.Equal(moment.ToUniversalTime(), loaded);
    }
}
