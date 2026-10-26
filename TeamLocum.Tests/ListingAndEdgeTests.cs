using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamLocum.Tests.TestSupport;
using TeamLocum.Web.Domain;
using TeamLocum.Web.Services;
using static TeamLocum.Tests.TestSupport.Flows;

namespace TeamLocum.Tests;

/// <summary>Lists, filters and paging, plus the awkward requests a careful user or a careless script can make.</summary>
public sealed partial class ListingTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [GeneratedRegex(@"href=""/Bookings/Details/(\d+)""")]
    private static partial Regex DetailsLink();

    private static List<int> Ids(string html) =>
        DetailsLink().Matches(html).Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToList();

    private sealed record Scenario(TestUser Hospital, BrowserClient Owner, TestUser Doctor, int Soon, int Later, int Cancelled, int Filled, int Past);

    /// <summary>One hospital with an upcoming shift, a later one, a cancelled one, a filled one and one that is over.</summary>
    private async Task<Scenario> SeedAsync()
    {
        var hospital = await factory.AddHospitalAsync();
        var owner = await factory.SignInAsync(hospital);
        var doctor = await factory.AddDoctorAsync();
        var soon = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8), location: "Leicester General", specialty: "Paediatrics");
        var later = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(80), TimeSpan.FromHours(8), location: "Derby Royal", specialty: null);
        var cancelled = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(120), TimeSpan.FromHours(8), location: "Nottingham East");
        var filled = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(160), TimeSpan.FromHours(8), location: "Coventry Ward");
        var past = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(-30), TimeSpan.FromHours(8), location: "Yesterday Clinic");

        await owner.ActionAsync("Cancel", cancelled);
        await (await factory.SignInAsync(doctor)).ApplyAsync(filled);
        await owner.AssignAsync(filled, doctor.ProfileId);
        return new Scenario(hospital, owner, doctor, soon, later, cancelled, filled, past);
    }

    // ───────────── shifts ─────────────

    [Fact]
    public async Task The_default_list_shows_upcoming_shifts_in_the_order_they_start_and_hides_finished_ones()
    {
        var s = await SeedAsync();

        var html = await s.Owner.GetHtmlAsync("/Bookings");

        Assert.Equal([s.Soon, s.Later, s.Cancelled, s.Filled], Ids(html));
        Assert.Contains("4 shifts, page 1 of 1", html);
    }

    [Fact]
    public async Task Finished_shifts_have_their_own_view_with_the_newest_first()
    {
        var s = await SeedAsync();
        var older = await factory.AddShiftAsync(s.Hospital, TimeSpan.FromHours(-80), TimeSpan.FromHours(8));

        var html = await s.Owner.GetHtmlAsync("/Bookings?past=true");

        Assert.Equal([s.Past, older], Ids(html));
        Assert.Contains("checked", html);
    }

    [Theory]
    [InlineData("status=Open", "Soon,Later")]
    [InlineData("status=Cancelled", "Cancelled")]
    [InlineData("status=Filled", "Filled")]
    [InlineData("status=Completed", "")]
    [InlineData("search=leicester", "Soon")]
    [InlineData("search=PAEDIATRICS", "Soon")]
    [InlineData("search=Royal", "Later")]
    [InlineData("search=%25", "")]
    [InlineData("search=_", "")]
    [InlineData("search=%5C", "")]
    [InlineData("search=nothing-like-this", "")]
    [InlineData("status=Open&search=derby", "Later")]
    public async Task Filters_narrow_the_list_and_wildcard_characters_are_taken_literally(string query, string expectedNames)
    {
        var s = await SeedAsync();

        var html = await s.Owner.GetHtmlAsync($"/Bookings?{query}");

        var byName = new Dictionary<string, int> { ["Soon"] = s.Soon, ["Later"] = s.Later, ["Cancelled"] = s.Cancelled, ["Filled"] = s.Filled };
        var expected = expectedNames.Length == 0 ? [] : expectedNames.Split(',').Select(name => byName[name]).ToList();
        Assert.Equal(expected, Ids(html));
        if (expected.Count == 0)
        {
            Assert.Contains("No shifts match.", html);
        }
    }

    [Fact]
    public async Task Pages_are_cut_at_the_requested_size_and_odd_values_are_tamed()
    {
        var s = await SeedAsync();

        var first = await s.Owner.GetHtmlAsync("/Bookings?pageSize=2");
        var second = await s.Owner.GetHtmlAsync("/Bookings?pageSize=2&page=2");

        Assert.Equal([s.Soon, s.Later], Ids(first));
        Assert.Contains("4 shifts, page 1 of 2", first);
        Assert.Contains(">Next</a>", first);
        Assert.DoesNotContain(">Previous</a>", first);
        Assert.Contains("pageSize=2", first);
        Assert.Equal([s.Cancelled, s.Filled], Ids(second));
        Assert.Contains(">Previous</a>", second);
        Assert.DoesNotContain(">Next</a>", second);

        Assert.Equal(Ids(first), Ids(await s.Owner.GetHtmlAsync("/Bookings?pageSize=2&page=0")));
        Assert.Equal(Ids(first), Ids(await s.Owner.GetHtmlAsync("/Bookings?pageSize=2&page=-4")));
        Assert.Equal(4, Ids(await s.Owner.GetHtmlAsync("/Bookings?pageSize=100000")).Count);
        Assert.Single(Ids(await s.Owner.GetHtmlAsync("/Bookings?pageSize=0")));
        Assert.Contains("No shifts match.", await s.Owner.GetHtmlAsync("/Bookings?pageSize=2&page=99"));
    }

    [Fact]
    public async Task A_doctor_can_narrow_the_list_to_open_shifts_or_their_own()
    {
        var s = await SeedAsync();
        var doctor = await factory.SignInAsync(s.Doctor);
        var ours = new[] { s.Soon, s.Later, s.Cancelled, s.Filled, s.Past };

        // Every doctor sees every hospital's open shifts, and other tests in this class post some, so only ours are compared.
        Assert.Equal([s.Soon, s.Later], Ids(await doctor.GetHtmlAsync("/Bookings?scope=Open&pageSize=100")).Where(ours.Contains));
        Assert.Equal([s.Filled], Ids(await doctor.GetHtmlAsync("/Bookings?scope=Mine&pageSize=100")));
        Assert.Equal([s.Soon, s.Later, s.Filled], Ids(await doctor.GetHtmlAsync("/Bookings?pageSize=100")).Where(ours.Contains));
    }

    [Fact]
    public async Task An_administrator_sees_every_hospitals_shifts_with_the_hospital_named()
    {
        var s = await SeedAsync();
        var admin = await factory.AdminAsync();

        var html = await admin.GetHtmlAsync("/Bookings?pageSize=100");

        Assert.All(new[] { s.Soon, s.Later, s.Cancelled, s.Filled }, id => Assert.Contains(id, Ids(html)));
        Assert.Contains(s.Hospital.Name, html);
        Assert.Contains("<th>Hospital</th>", html);
    }

    [Fact]
    public async Task A_shift_on_a_bank_holiday_carries_a_badge_in_the_list()
    {
        var s = await SeedAsync();
        var start = await factory.WithDbAsync(db => db.Bookings.Where(b => b.Id == s.Soon).Select(b => b.StartsAt).SingleAsync());
        await factory.WithDbAsync(async db =>
        {
            db.Holidays.Add(new Holiday { Date = DateOnly.FromDateTime(start), Details = "Founders Day" });
            await db.SaveChangesAsync();
            return 0;
        });

        var html = await s.Owner.GetHtmlAsync("/Bookings");

        Assert.Contains("Founders Day", html);
    }

    // ───────────── doctors ─────────────

    [Fact]
    public async Task Hospitals_and_administrators_search_doctors_by_specialty_location_or_surname()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var near = await factory.AddDoctorAsync("Nora", $"Near{tag}", $"Zebra{tag}", $"Quarry{tag}");
        var far = await factory.AddDoctorAsync("Fred", $"Far{tag}", $"Giraffe{tag}", $"Marsh{tag}");
        var hospital = await factory.SignInAsync(await factory.AddHospitalAsync());
        var admin = await factory.AdminAsync();

        foreach (var client in new[] { hospital, admin })
        {
            var bySpecialty = await client.GetHtmlAsync($"/Locums?search=zebra{tag}");
            Assert.Contains(near.Name, bySpecialty);
            Assert.DoesNotContain(far.Name, bySpecialty);

            Assert.Contains(far.Name, await client.GetHtmlAsync($"/Locums?search=MARSH{tag}"));
            Assert.Contains(near.Name, await client.GetHtmlAsync($"/Locums?search=Near{tag}"));
            Assert.Contains("No doctors match.", await client.GetHtmlAsync("/Locums?search=%25"));
        }
    }

    [Fact]
    public async Task Only_administrators_can_filter_doctors_by_status_and_hospitals_never_see_unapproved_ones()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var pending = await factory.AddDoctorAsync("Pat", $"Pending{tag}", approved: false);
        var active = await factory.AddDoctorAsync("Ann", $"Active{tag}");
        var hospital = await factory.SignInAsync(await factory.AddHospitalAsync());
        var admin = await factory.AdminAsync();

        var adminPending = await admin.GetHtmlAsync("/Locums?status=PendingReview");
        var adminAll = await admin.GetHtmlAsync("/Locums");
        var hospitalPending = await hospital.GetHtmlAsync("/Locums?status=PendingReview");

        Assert.Contains(pending.Name, adminPending);
        Assert.DoesNotContain(active.Name, adminPending);
        Assert.Contains(pending.Name, adminAll);
        Assert.Contains(active.Name, adminAll);
        Assert.DoesNotContain(pending.Name, hospitalPending);
        Assert.Contains(active.Name, hospitalPending);
    }

    [Fact]
    public async Task A_hospital_that_is_not_yet_approved_cannot_browse_doctors()
    {
        var waiting = await factory.SignInAsync(await factory.AddHospitalAsync(approved: false));

        var response = await waiting.GetAsync("/Locums");

        Assert.True(response.IsRedirect());
        Assert.Contains("AccessDenied", BrowserClient.Location(response));
    }

    [Fact]
    public async Task Hospitals_can_be_filtered_by_status_and_reinstated_after_a_suspension()
    {
        var hospital = await factory.AddHospitalAsync();
        var admin = await factory.AdminAsync();
        await admin.PostFormAsync($"/Clients/Suspend/{hospital.ProfileId}", tokenPage: "/Clients");

        var suspended = await admin.GetHtmlAsync("/Clients?status=Suspended");
        Assert.Contains(hospital.Name, suspended);
        Assert.Contains("Reinstate", suspended);
        Assert.DoesNotContain(hospital.Name, await admin.GetHtmlAsync("/Clients?status=Accepted"));

        var reinstate = await admin.PostFormAsync($"/Clients/Reinstate/{hospital.ProfileId}", tokenPage: "/Clients");
        Assert.Contains("Hospital reinstated.", await admin.FollowAsync(reinstate));
        Assert.Equal(ClientStatus.Accepted, (await factory.WithDbAsync(db => db.Clients.SingleAsync(c => c.Id == hospital.ProfileId))).Status);
    }

    [Fact]
    public async Task A_hospital_profile_with_missing_details_is_shown_again_with_the_errors()
    {
        var client = await factory.SignInAsync(await factory.AddHospitalAsync());

        var response = await client.PostFormAsync("/Clients/Profile",
            [F("Input.FirstName", ""), F("Input.LastName", "Reid"), F("Input.CompanyName", "")], "/Clients/Profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Removing_a_bank_holiday_that_is_already_gone_is_a_404()
    {
        var admin = await factory.AdminAsync();

        var response = await admin.PostFormAsync("/Holidays/Delete/987654", tokenPage: "/Holidays");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ───────────── edge cases in the shift workflow ─────────────

    [Fact]
    public async Task Filling_a_shift_that_has_started_or_with_a_doctor_who_cannot_work_is_refused_with_a_reason()
    {
        var hospital = await factory.AddHospitalAsync();
        var owner = await factory.SignInAsync(hospital);
        var pending = await factory.AddDoctorAsync(approved: false);
        var started = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(-2), TimeSpan.FromHours(8));
        var open = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var stamp = Guid.NewGuid().ToString();

        var late = await owner.AssignAsync(started, pending.ProfileId, stamp);
        Assert.Contains("can no longer be filled", await owner.FollowAsync(late));

        var unapproved = await owner.AssignAsync(open, pending.ProfileId);
        Assert.Contains("not approved to take shifts", await owner.FollowAsync(unapproved));

        var unknown = await owner.AssignAsync(open, 987654);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(BookingStatus.Open, (await factory.WithDbAsync(db => db.Bookings.SingleAsync(b => b.Id == open))).Status);
    }

    [Fact]
    public async Task Releasing_a_doctor_declines_their_accepted_application_and_cancelling_declines_the_pending_ones()
    {
        var hospital = await factory.AddHospitalAsync();
        var owner = await factory.SignInAsync(hospital);
        var chosen = await factory.AddDoctorAsync();
        var waiting = await factory.AddDoctorAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        await (await factory.SignInAsync(chosen)).ApplyAsync(shiftId);
        await owner.AssignAsync(shiftId, chosen.ProfileId);

        await owner.ActionAsync("Release", shiftId);
        var afterRelease = await factory.WithDbAsync(db => db.ShiftApplications.Where(a => a.BookingId == shiftId).ToListAsync());
        Assert.Equal(ApplicationStatus.Declined, Assert.Single(afterRelease).Status);

        await (await factory.SignInAsync(waiting)).ApplyAsync(shiftId);
        await owner.ActionAsync("Cancel", shiftId);
        var afterCancel = await factory.WithDbAsync(db => db.ShiftApplications.Where(a => a.BookingId == shiftId && a.LocumId == waiting.ProfileId).SingleAsync());
        Assert.Equal(ApplicationStatus.Declined, afterCancel.Status);
    }

    [Fact]
    public async Task A_doctor_whose_application_lost_cannot_apply_again_and_cannot_withdraw_a_decided_one()
    {
        var hospital = await factory.AddHospitalAsync();
        var owner = await factory.SignInAsync(hospital);
        var winner = await factory.AddDoctorAsync();
        var loser = await factory.AddDoctorAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var loserClient = await factory.SignInAsync(loser);
        var winnerClient = await factory.SignInAsync(winner);
        await loserClient.ApplyAsync(shiftId);
        await winnerClient.ApplyAsync(shiftId);
        await owner.AssignAsync(shiftId, winner.ProfileId);

        var again = await loserClient.ApplyAsync(shiftId);
        Assert.Contains("no longer open for applications", await loserClient.FollowAsync(again));

        var withdrawDeclined = await loserClient.WithdrawAsync(shiftId);
        Assert.Contains("Only a pending application can be withdrawn.", await loserClient.FollowAsync(withdrawDeclined));
        var withdrawAccepted = await winnerClient.WithdrawAsync(shiftId);
        Assert.Contains("Only a pending application can be withdrawn.", await winnerClient.FollowAsync(withdrawAccepted));
    }

    [Fact]
    public async Task Withdrawing_needs_a_doctor_and_an_application_to_withdraw()
    {
        var hospital = await factory.AddHospitalAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var doctor = await factory.SignInAsync(await factory.AddDoctorAsync());

        var byHospital = await (await factory.SignInAsync(hospital)).PostFormAsync($"/Bookings/Withdraw/{shiftId}");
        var noApplication = await doctor.PostFormAsync($"/Bookings/Withdraw/{shiftId}");

        Assert.Contains("AccessDenied", BrowserClient.Location(byHospital));
        Assert.Equal(HttpStatusCode.NotFound, noApplication.StatusCode);
    }

    [Fact]
    public async Task An_administrator_posting_for_a_hospital_that_does_not_exist_gets_a_404()
    {
        var admin = await factory.AdminAsync();
        var start = factory.ClinicNow.Date.AddDays(3).AddHours(9);

        var response = await admin.PostShiftAsync(start, start.AddHours(8), clientId: 987654);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ───────────── service rules that no page can reach ─────────────

    private async Task<T> InServiceAsync<T>(Func<BookingService, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<BookingService>());
    }

    [Fact]
    public async Task Suggestions_for_a_shift_that_is_no_longer_open_are_empty()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        await (await factory.SignInAsync(doctor)).ApplyAsync(shiftId);
        await (await factory.SignInAsync(hospital)).AssignAsync(shiftId, doctor.ProfileId);
        var admin = new Actor("admin", Roles.Admin, null, null, null, null);

        var result = await InServiceAsync(service => service.MatchesAsync(admin, shiftId, CancellationToken.None));

        Assert.Empty(result.Candidates);
    }

    [Theory]
    [InlineData(Roles.Client)]
    [InlineData(Roles.Locum)]
    public async Task An_account_whose_profile_is_missing_sees_no_shifts_at_all(string role)
    {
        var hospital = await factory.AddHospitalAsync();
        await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var orphan = new Actor("orphan", role, null, null, null, null);

        var result = await InServiceAsync(service => service.ListAsync(orphan, new BookingListQuery { PageSize = 100 }, CancellationToken.None));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task An_account_with_no_role_is_signed_in_but_can_do_nothing()
    {
        var email = $"norole-{Guid.NewGuid():N}@clinic.test";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var created = await users.CreateAsync(
                new ApplicationUser { UserName = email, Email = email, FirstName = "No", LastName = "Role", CreatedAt = DateTimeOffset.UtcNow },
                AppFactory.UserPassword);
            Assert.True(created.Succeeded);
        }

        var client = factory.NewClient();
        var login = await client.LoginAsync(email, AppFactory.UserPassword);
        Assert.True(login.IsRedirect());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Contains("AccessDenied", BrowserClient.Location(await client.GetAsync("/Bookings")));
        Assert.Contains("AccessDenied", BrowserClient.Location(await client.PostFormAsync("/Holidays/Create", [F("Date", "2040-01-01"), F("Details", "Nope")])));
    }

    // ───────────── error and lockout pages ─────────────

    [Theory]
    [InlineData(403, 403, "You do not have access to that")]
    [InlineData(404, 404, "We could not find that page")]
    [InlineData(418, 418, "That request could not be completed")]
    [InlineData(429, 429, "Too many requests")]
    [InlineData(500, 500, "Something went wrong on our side")]
    [InlineData(503, 503, "Something went wrong on our side")]
    [InlineData(200, 404, "We could not find that page")]
    [InlineData(99999, 404, "We could not find that page")]
    [InlineData(-5, 404, "We could not find that page")]
    public async Task The_status_page_only_honours_real_error_statuses(int requested, int expectedStatus, string title)
    {
        var response = await factory.NewClient().GetAsync($"/Home/HttpStatus?code={requested}");

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Contains(title, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_locked_out_visitor_is_shown_a_page_that_explains_it()
    {
        var hospital = await factory.AddHospitalAsync();
        var attacker = factory.NewClient();
        HttpResponseMessage last = null!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            last = await attacker.LoginAsync(hospital.Email, "Wrong-Password-1");
        }

        var page = await attacker.GetAsync(BrowserClient.Location(last));

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("locked", await page.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>What the sign-up form says when it is filled in badly.</summary>
public sealed class RegistrationValidationTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static string NewEmail() => $"signup-{Guid.NewGuid():N}@clinic.test";

    private static List<KeyValuePair<string, string>> Form(string type, string email, params (string Key, string Value)[] extra)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.AccountType"] = type,
            ["Input.Email"] = email,
            ["Input.Password"] = AppFactory.UserPassword,
            ["Input.ConfirmPassword"] = AppFactory.UserPassword,
            ["Input.FirstName"] = "Sam",
            ["Input.LastName"] = "Reid"
        };
        foreach (var (key, value) in extra)
        {
            fields[key] = value;
        }

        return [.. fields.Select(f => F(f.Key, f.Value))];
    }

    private async Task<string> RefusedAsync(List<KeyValuePair<string, string>> form, string email)
    {
        var response = await factory.NewClient().PostFormAsync("/Identity/Account/Register", form, "/Identity/Account/Register");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await factory.WithDbAsync(db => db.Users.AnyAsync(u => u.Email == email)), "A refused registration must not create an account.");
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_hospital_must_say_what_it_is_called()
    {
        var email = NewEmail();

        var html = await RefusedAsync(Form("1", email, ("Input.CompanyName", "  ")), email);

        Assert.Contains("Enter the name of your hospital or clinic.", html);
    }

    [Fact]
    public async Task A_doctor_must_list_a_specialty_and_a_base_location()
    {
        var email = NewEmail();

        var html = await RefusedAsync(Form("2", email, ("Input.GmcNumber", "1234567"), ("Input.Specialties", " "), ("Input.BaseLocation", "")), email);

        Assert.Contains("List at least one specialty.", html);
        Assert.Contains("Enter where you are based.", html);
    }

    [Fact]
    public async Task The_account_type_is_required_and_the_passwords_must_match()
    {
        var email = NewEmail();
        var form = Form("", email, ("Input.ConfirmPassword", "Another-Password-9"));

        var html = await RefusedAsync(form, email);

        Assert.Contains("I am registering as", html);
        Assert.Contains("The passwords do not match.", html);
    }

    [Theory]
    [InlineData("/Bookings", "/Bookings")]
    [InlineData("https://evil.test/steal", "/")]
    [InlineData("//evil.test/steal", "/")]
    public async Task After_signing_up_the_user_goes_to_a_page_on_this_site_only(string requested, string expected)
    {
        var email = NewEmail();
        var client = factory.NewClient();

        var response = await client.PostFormAsync(
            $"/Identity/Account/Register?returnUrl={Uri.EscapeDataString(requested)}",
            Form("1", email, ("Input.CompanyName", "Reid Surgery")), "/Identity/Account/Register");

        Assert.True(response.IsRedirect());
        Assert.Equal(expected, BrowserClient.Location(response));
    }
}

/// <summary>What each role sees on a doctor's page, and the forms that are posted empty.</summary>
public sealed class DetailPageTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Administrators_see_a_doctors_email_and_gmc_number_hospitals_only_the_gmc_number_and_doctors_neither()
    {
        var doctor = await factory.AddDoctorAsync("Dana", "Details", gmc: "5550001");
        var other = await factory.AddDoctorAsync();
        var admin = await (await factory.AdminAsync()).GetHtmlAsync($"/Locums/Details/{doctor.ProfileId}");
        var hospital = await (await factory.SignInAsync(await factory.AddHospitalAsync())).GetHtmlAsync($"/Locums/Details/{doctor.ProfileId}");
        var own = await (await factory.SignInAsync(doctor)).GetHtmlAsync($"/Locums/Details/{doctor.ProfileId}");
        var rival = await (await factory.SignInAsync(other)).GetAsync($"/Locums/Details/{doctor.ProfileId}");

        Assert.Contains("5550001", admin);
        Assert.Contains(doctor.Email, admin);
        Assert.Contains("5550001", hospital);
        Assert.DoesNotContain(doctor.Email, hospital);
        Assert.DoesNotContain("5550001", own);
        Assert.Contains("General practice", own);
        Assert.Equal(HttpStatusCode.NotFound, rival.StatusCode);
    }

    [Fact]
    public async Task A_doctor_with_no_specialties_listed_says_so()
    {
        var doctor = await factory.AddDoctorAsync(specialties: " ");
        var admin = await factory.AdminAsync();

        var html = await admin.GetHtmlAsync($"/Locums/Details/{doctor.ProfileId}");

        Assert.Contains("None listed", html);
    }

    [Fact]
    public async Task Submitting_the_sign_in_form_empty_shows_what_is_missing_and_signs_nobody_in()
    {
        var client = factory.NewClient();

        var response = await client.PostFormAsync("/Identity/Account/Login", [F("Input.Email", ""), F("Input.Password", "")], "/Identity/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Bookings")).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Client)]
    [InlineData(Roles.Locum)]
    [InlineData("Stranger")]
    public async Task An_account_without_a_profile_has_an_empty_dashboard(string role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<DirectoryService>();

        var items = await directory.DashboardAsync(new Actor("nobody", role, null, null, null, null), CancellationToken.None);

        Assert.Empty(items);
    }

    [Fact]
    public async Task Administrative_actions_are_refused_to_anyone_else_at_the_service_layer_too()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<DirectoryService>();
        var hospital = new Actor("h", Roles.Client, 1, ClientStatus.Accepted, null, null);

        await Assert.ThrowsAsync<ForbiddenException>(() => directory.ListClientsAsync(hospital, null, CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => directory.ListUsersAsync(hospital, CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => directory.UnlockUserAsync(hospital, "x", CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => directory.ChangeLocumStatusAsync(hospital, 1, LocumStatus.Active, CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => directory.ChangeClientStatusAsync(hospital, 1, ClientStatus.Suspended, CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => directory.GetMyLocumProfileAsync(hospital, CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => directory.GetMyClientProfileAsync(new Actor("d", Roles.Locum, null, null, 1, LocumStatus.Active), CancellationToken.None));
    }
}
