using System.Net;
using Microsoft.EntityFrameworkCore;
using TeamLocum.Tests.TestSupport;
using TeamLocum.Web.Domain;

namespace TeamLocum.Tests;

public sealed class AccountTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static KeyValuePair<string, string> F(string key, string value) => new(key, value);

    private static List<KeyValuePair<string, string>> HospitalForm(string email, string? password = null) =>
    [
        F("Input.AccountType", "1"), F("Input.Email", email), F("Input.Password", password ?? AppFactory.UserPassword),
        F("Input.ConfirmPassword", password ?? AppFactory.UserPassword), F("Input.FirstName", "Sam"), F("Input.LastName", "Reid"),
        F("Input.CompanyName", "Reid Surgery")
    ];

    private static List<KeyValuePair<string, string>> DoctorForm(string email, string gmc = "1234567") =>
    [
        F("Input.AccountType", "2"), F("Input.Email", email), F("Input.Password", AppFactory.UserPassword),
        F("Input.ConfirmPassword", AppFactory.UserPassword), F("Input.FirstName", "Ada"), F("Input.LastName", "Lovelace"),
        F("Input.GmcNumber", gmc), F("Input.Specialties", "Paediatrics, General practice"), F("Input.BaseLocation", "Leicester"),
        F("Input.MinRatePerHour", "70"), F("Input.YearsExperience", "9")
    ];

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@clinic.test";

    [Fact]
    public async Task A_hospital_can_register_and_starts_awaiting_review()
    {
        var email = NewEmail("hospital");
        var client = factory.NewClient();

        var response = await client.PostFormAsync("/Identity/Account/Register", HospitalForm(email), "/Identity/Account/Register");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var home = await client.FollowAsync(response);
        Assert.Contains("awaiting review", home);

        var profile = await factory.WithDbAsync(db => db.Clients.Include(c => c.User).SingleAsync(c => c.User!.Email == email));
        Assert.Equal(ClientStatus.AwaitingReview, profile.Status);
        Assert.Equal("Reid Surgery", profile.CompanyName);
    }

    [Fact]
    public async Task A_doctor_can_register_and_starts_pending_review()
    {
        var email = NewEmail("doctor");
        var client = factory.NewClient();

        var response = await client.PostFormAsync("/Identity/Account/Register", DoctorForm(email, gmc: "7000001"), "/Identity/Account/Register");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var locum = await factory.WithDbAsync(db => db.Locums.Include(l => l.User).SingleAsync(l => l.User!.Email == email));
        Assert.Equal(LocumStatus.PendingReview, locum.Status);
        Assert.Equal("7000001", locum.GmcNumber);
        Assert.Equal(["Paediatrics", "General practice"], locum.SpecialtyList);
    }

    [Fact]
    public async Task Nobody_can_register_as_an_administrator()
    {
        var email = NewEmail("sneaky");
        var client = factory.NewClient();
        var form = HospitalForm(email);
        form.RemoveAll(f => f.Key == "Input.AccountType");
        form.Add(F("Input.AccountType", "3"));
        form.Add(F("Input.Role", "Admin"));
        form.Add(F("Role", "Admin"));

        var response = await client.PostFormAsync("/Identity/Account/Register", form, "/Identity/Account/Register");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await factory.WithDbAsync(db => db.Users.AnyAsync(u => u.Email == email)));
    }

    [Fact]
    public async Task A_hospital_that_adds_admin_fields_still_only_becomes_a_hospital()
    {
        var email = NewEmail("extra");
        var client = factory.NewClient();
        var form = HospitalForm(email);
        form.Add(F("Role", "Admin"));
        form.Add(F("Input.Status", "Accepted"));
        form.Add(F("Status", "Accepted"));

        var response = await client.PostFormAsync("/Identity/Account/Register", form, "/Identity/Account/Register");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var (roles, status) = await factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email);
            var userRoles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == user.Id select r.Name).ToListAsync();
            return (userRoles, (await db.Clients.SingleAsync(c => c.UserId == user.Id)).Status);
        });
        Assert.Equal([Roles.Client], roles);
        Assert.Equal(ClientStatus.AwaitingReview, status);
    }

    [Theory]
    [InlineData("short1A", "password")]
    [InlineData("alllowercase123", "password")]
    [InlineData("ALLUPPERCASE123", "password")]
    [InlineData("NoDigitsInThisOne", "password")]
    public async Task Weak_passwords_are_refused(string password, string expectedField)
    {
        var email = NewEmail("weak");
        var client = factory.NewClient();

        var response = await client.PostFormAsync("/Identity/Account/Register", HospitalForm(email, password), "/Identity/Account/Register");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await factory.WithDbAsync(db => db.Users.AnyAsync(u => u.Email == email)), expectedField);
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("12345678")]
    [InlineData("12345AB")]
    public async Task A_gmc_number_must_be_exactly_seven_digits(string gmc)
    {
        var email = NewEmail("gmc");
        var client = factory.NewClient();

        var response = await client.PostFormAsync("/Identity/Account/Register", DoctorForm(email, gmc), "/Identity/Account/Register");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("GMC number is exactly 7 digits", await response.Content.ReadAsStringAsync());
        Assert.False(await factory.WithDbAsync(db => db.Users.AnyAsync(u => u.Email == email)));
    }

    [Fact]
    public async Task Duplicate_emails_and_gmc_numbers_are_refused_without_a_half_created_account()
    {
        var first = NewEmail("first");
        await factory.NewClient().PostFormAsync("/Identity/Account/Register", DoctorForm(first, "7000002"), "/Identity/Account/Register");

        var sameGmc = NewEmail("second");
        var duplicateGmc = await factory.NewClient().PostFormAsync("/Identity/Account/Register", DoctorForm(sameGmc, "7000002"), "/Identity/Account/Register");
        var duplicateEmail = await factory.NewClient().PostFormAsync("/Identity/Account/Register", DoctorForm(first, "7000003"), "/Identity/Account/Register");

        Assert.Equal(HttpStatusCode.OK, duplicateGmc.StatusCode);
        Assert.Contains("already registered", await duplicateGmc.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, duplicateEmail.StatusCode);
        Assert.False(await factory.WithDbAsync(db => db.Users.AnyAsync(u => u.Email == sameGmc)));
        Assert.False(await factory.WithDbAsync(db => db.Locums.AnyAsync(l => l.GmcNumber == "7000003")));
    }

    [Fact]
    public async Task Anonymous_visitors_are_sent_to_sign_in_and_returned_afterwards()
    {
        var response = await factory.NewClient().GetAsync("/Bookings");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Login?ReturnUrl=%2FBookings", BrowserClient.Location(response));
    }

    [Fact]
    public async Task Signing_in_returns_to_the_page_that_was_asked_for_but_never_to_another_site()
    {
        var hospital = await factory.AddHospitalAsync();
        var client = factory.NewClient();

        var local = await client.LoginAsync(hospital.Email, hospital.Password, returnUrl: "/Bookings");
        Assert.Equal(HttpStatusCode.Redirect, local.StatusCode);
        Assert.Equal("/Bookings", BrowserClient.Location(local));

        var other = factory.NewClient();
        var offsite = await other.LoginAsync(hospital.Email, hospital.Password, returnUrl: "https://evil.test/phish");
        Assert.Equal(HttpStatusCode.Redirect, offsite.StatusCode);
        Assert.Equal("/", BrowserClient.Location(offsite));
    }

    [Fact]
    public async Task Failed_sign_ins_do_not_reveal_whether_the_account_exists()
    {
        var hospital = await factory.AddHospitalAsync();

        var wrongPassword = await factory.NewClient().LoginAsync(hospital.Email, "Wrong-Password-1");
        var unknownAccount = await factory.NewClient().LoginAsync($"nobody-{Guid.NewGuid():N}@clinic.test", "Wrong-Password-1");

        Assert.Equal(HttpStatusCode.OK, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknownAccount.StatusCode);
        Assert.Contains("Invalid email or password.", await wrongPassword.Content.ReadAsStringAsync());
        Assert.Contains("Invalid email or password.", await unknownAccount.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_until_an_administrator_unlocks_it()
    {
        var hospital = await factory.AddHospitalAsync();
        var attacker = factory.NewClient();

        HttpResponseMessage last = null!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            last = await attacker.LoginAsync(hospital.Email, "Wrong-Password-1");
        }

        Assert.Equal(HttpStatusCode.Redirect, last.StatusCode);
        Assert.Contains("Lockout", BrowserClient.Location(last));

        var correctButLocked = await factory.NewClient().LoginAsync(hospital.Email, hospital.Password);
        Assert.Contains("Lockout", BrowserClient.Location(correctButLocked));

        var admin = await factory.AdminAsync();
        var users = await admin.GetHtmlAsync("/Admin/Users");
        Assert.Contains("Locked out", users);
        var unlock = await admin.PostFormAsync($"/Admin/Unlock/{hospital.UserId}", tokenPage: "/Admin/Users");
        Assert.Equal(HttpStatusCode.Redirect, unlock.StatusCode);

        var afterUnlock = await factory.NewClient().LoginAsync(hospital.Email, hospital.Password);
        Assert.Equal("/", BrowserClient.Location(afterUnlock));
    }

    [Theory]
    [InlineData("/Identity/Account/ForgotPassword")]
    [InlineData("/Identity/Account/ResetPassword")]
    [InlineData("/Identity/Account/ConfirmEmail")]
    [InlineData("/Identity/Account/Manage/Email")]
    [InlineData("/Identity/Account/Manage/TwoFactorAuthentication")]
    [InlineData("/Identity/Account/Manage/DeletePersonalData")]
    public async Task Identity_pages_that_promise_features_the_app_does_not_have_are_switched_off(string path)
    {
        var hospital = await factory.AddHospitalAsync();
        var client = await factory.SignInAsync(hospital);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var hospital = await factory.AddHospitalAsync();
        var client = await factory.SignInAsync(hospital);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Bookings")).StatusCode);

        var logout = await client.PostFormAsync("/Identity/Account/Logout?returnUrl=%2F");
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);

        var after = await client.GetAsync("/Bookings");
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.Contains("/Identity/Account/Login", BrowserClient.Location(after));
    }
}
