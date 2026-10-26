using System.Net;
using TeamLocum.Tests.TestSupport;
using static TeamLocum.Tests.TestSupport.Flows;

namespace TeamLocum.Tests;

/// <summary>
/// Who may see and do what. Each test names a way a real attacker or a curious user would try to reach someone else's
/// data, and proves the answer is "that does not exist" or "access denied", never the data.
/// </summary>
public sealed class AccessControlTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Theory]
    [InlineData("/Admin", false, false, true)]
    [InlineData("/Admin/Users", false, false, true)]
    [InlineData("/Clients", false, false, true)]
    [InlineData("/Locums", true, false, true)]
    [InlineData("/Holidays", true, true, true)]
    [InlineData("/Holidays/Create", false, false, true)]
    [InlineData("/Bookings", true, true, true)]
    [InlineData("/Bookings/Create", true, false, true)]
    [InlineData("/Locums/Profile", false, true, false)]
    [InlineData("/Clients/Profile", true, false, false)]
    public async Task Each_page_is_open_only_to_the_roles_it_is_meant_for(string path, bool hospital, bool doctor, bool admin)
    {
        var anonymous = await factory.NewClient().GetAsync(path);
        Assert.True(anonymous.IsRedirect(), $"{path} should send anonymous visitors to sign in");
        Assert.Contains("/Identity/Account/Login", BrowserClient.Location(anonymous));

        await AssertAccessAsync(path, await factory.SignInAsync(await factory.AddHospitalAsync()), hospital, "hospital");
        await AssertAccessAsync(path, await factory.SignInAsync(await factory.AddDoctorAsync()), doctor, "doctor");
        await AssertAccessAsync(path, await factory.AdminAsync(), admin, "admin");
    }

    private static async Task AssertAccessAsync(string path, BrowserClient client, bool allowed, string role)
    {
        var response = await client.GetAsync(path);
        if (allowed)
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{role} should open {path} but got {(int)response.StatusCode}");
        }
        else
        {
            Assert.True(response.IsRedirect() && BrowserClient.Location(response).Contains("AccessDenied"),
                $"{role} should be denied {path} but got {(int)response.StatusCode} {BrowserClient.Location(response)}");
        }
    }

    // ───────────── other people's shifts ─────────────

    [Fact]
    public async Task A_hospital_cannot_see_or_change_another_hospitals_shift()
    {
        var owner = await factory.AddHospitalAsync();
        var intruder = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        var shiftId = await factory.AddShiftAsync(owner, TimeSpan.FromHours(30), TimeSpan.FromHours(8), location: "Owner Secret Ward");
        var client = await factory.SignInAsync(intruder);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Bookings/Details/{shiftId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Bookings/Edit/{shiftId}")).StatusCode);
        Assert.DoesNotContain("Owner Secret Ward", await client.GetHtmlAsync("/Bookings"));

        // Every write is refused as "not found" too, so the shift's existence is not confirmed.
        foreach (var action in new[] { "Cancel", "Complete", "Release" })
        {
            var response = await client.PostFormAsync($"/Bookings/{action}/{shiftId}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var assign = await client.PostFormAsync($"/Bookings/Assign/{shiftId}", [F("locumId", doctor.ProfileId.ToString()), F("stamp", Guid.NewGuid().ToString())]);
        Assert.Equal(HttpStatusCode.NotFound, assign.StatusCode);

        var edit = await client.PostFormAsync($"/Bookings/Edit/{shiftId}",
        [
            F("Input.StartsAt", Iso(factory.ClinicNow.Date.AddDays(4).AddHours(9))), F("Input.EndsAt", Iso(factory.ClinicNow.Date.AddDays(4).AddHours(17))),
            F("Input.RatePerHour", "1"), F("Input.Location", "Hijacked"), F("Input.ConcurrencyStamp", Guid.NewGuid().ToString())
        ]);
        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);

        var untouched = await factory.WithDbAsync(async db => await db.Bookings.FindAsync(shiftId));
        Assert.Equal(TeamLocum.Web.Domain.BookingStatus.Open, untouched!.Status);
        Assert.Equal("Owner Secret Ward", untouched.Location);
    }

    [Fact]
    public async Task A_doctor_sees_open_shifts_but_not_other_peoples_filled_ones()
    {
        var hospital = await factory.AddHospitalAsync();
        var assigned = await factory.AddDoctorAsync(first: "Assigned", last: "Person");
        var outsider = await factory.AddDoctorAsync(first: "Outsider", last: "Person");
        var openShift = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8), location: "Visible Open Ward");
        var filledShift = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(60), TimeSpan.FromHours(8), location: "Private Filled Ward");
        var hospitalClient = await factory.SignInAsync(hospital);
        await hospitalClient.AssignAsync(filledShift, assigned.ProfileId);
        var client = await factory.SignInAsync(outsider);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Bookings/Details/{openShift}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Bookings/Details/{filledShift}")).StatusCode);
        var list = await client.GetHtmlAsync("/Bookings");
        Assert.Contains("Visible Open Ward", list);
        Assert.DoesNotContain("Private Filled Ward", list);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostFormAsync($"/Bookings/Cancel/{filledShift}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostFormAsync($"/Bookings/Cancel/{openShift}")).StatusCode);
    }

    [Fact]
    public async Task A_doctor_can_never_cancel_complete_release_or_assign()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var client = await factory.SignInAsync(doctor);

        // Cancel and Complete check ownership (the shift looks like someone else's); Release and Assign check the role first.
        foreach (var action in new[] { "Cancel", "Complete" })
        {
            var response = await client.PostFormAsync($"/Bookings/{action}/{shiftId}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var release = await client.PostFormAsync($"/Bookings/Release/{shiftId}");
        Assert.Contains("AccessDenied", BrowserClient.Location(release));
        var assign = await client.PostFormAsync($"/Bookings/Assign/{shiftId}", [F("locumId", doctor.ProfileId.ToString()), F("stamp", Guid.NewGuid().ToString())]);
        Assert.Contains("AccessDenied", BrowserClient.Location(assign));
        var status = (await factory.WithDbAsync(async db => await db.Bookings.FindAsync(shiftId)))!.Status;
        Assert.Equal(TeamLocum.Web.Domain.BookingStatus.Open, status);
    }

    [Fact]
    public async Task The_shift_list_of_a_hospital_contains_only_its_own_shifts()
    {
        var one = await factory.AddHospitalAsync(company: "Alpha Clinic");
        var two = await factory.AddHospitalAsync(company: "Beta Clinic");
        var mine = await factory.AddShiftAsync(one, TimeSpan.FromHours(30), TimeSpan.FromHours(4), location: "Alpha Ward 1");
        await factory.AddShiftAsync(two, TimeSpan.FromHours(30), TimeSpan.FromHours(4), location: "Beta Ward 9");
        var client = await factory.SignInAsync(one);

        var html = await client.GetHtmlAsync("/Bookings");

        Assert.Contains($"/Bookings/Details/{mine}", html);
        Assert.Contains("Alpha Ward 1", html);
        Assert.DoesNotContain("Beta Ward 9", html);
    }

    // ───────────── doctors' privacy ─────────────

    [Fact]
    public async Task Doctors_never_see_each_others_names_or_profiles()
    {
        var hospital = await factory.AddHospitalAsync();
        var first = await factory.AddDoctorAsync(first: "Zelda", last: "Firstdoc");
        var second = await factory.AddDoctorAsync(first: "Yuri", last: "Seconddoc");
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var hospitalClient = await factory.SignInAsync(hospital);
        var firstClient = await factory.SignInAsync(first);
        var secondClient = await factory.SignInAsync(second);
        await firstClient.ApplyAsync(shiftId);
        await secondClient.ApplyAsync(shiftId);
        await hospitalClient.AssignAsync(shiftId, second.ProfileId);

        var list = await secondClient.GetHtmlAsync("/Bookings");
        Assert.DoesNotContain("Zelda", list);
        var details = await secondClient.GetHtmlAsync($"/Bookings/Details/{shiftId}");
        Assert.DoesNotContain("Zelda", details);

        Assert.Equal(HttpStatusCode.NotFound, (await firstClient.GetAsync($"/Locums/Details/{second.ProfileId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await firstClient.GetAsync($"/Locums/Details/{first.ProfileId}")).StatusCode);
    }

    [Fact]
    public async Task A_doctor_whose_application_lost_cannot_learn_who_got_the_shift()
    {
        var hospital = await factory.AddHospitalAsync();
        var winner = await factory.AddDoctorAsync(first: "Winnie", last: "Winner");
        var loser = await factory.AddDoctorAsync(first: "Larry", last: "Loser");
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var hospitalClient = await factory.SignInAsync(hospital);
        var loserClient = await factory.SignInAsync(loser);
        await loserClient.ApplyAsync(shiftId);
        await (await factory.SignInAsync(winner)).ApplyAsync(shiftId);
        await hospitalClient.AssignAsync(shiftId, winner.ProfileId);

        var list = await loserClient.GetHtmlAsync("/Bookings?status=Filled");
        var details = await loserClient.GetHtmlAsync($"/Bookings/Details/{shiftId}");

        Assert.DoesNotContain("Winnie", list);
        Assert.DoesNotContain("Winner", list);
        Assert.DoesNotContain("Winnie", details);
        Assert.Contains("Another doctor", list);
    }

    [Fact]
    public async Task Hospitals_see_gmc_numbers_of_approved_doctors_but_not_pending_ones_and_never_emails()
    {
        var hospital = await factory.AddHospitalAsync();
        var approved = await factory.AddDoctorAsync(first: "Approved", last: "Doc", gmc: "5550001");
        var pending = await factory.AddDoctorAsync(first: "Pending", last: "Doc", gmc: "5550002", approved: false);
        var client = await factory.SignInAsync(hospital);

        var list = await client.GetHtmlAsync("/Locums");
        Assert.Contains("Approved Doc", list);
        Assert.Contains("5550001", list);
        Assert.DoesNotContain("Pending Doc", list);
        Assert.DoesNotContain("5550002", list);
        Assert.DoesNotContain(approved.Email, list);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Locums/Details/{approved.ProfileId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Locums/Details/{pending.ProfileId}")).StatusCode);
    }

    [Fact]
    public async Task Only_administrators_see_doctor_email_addresses_and_pending_profiles()
    {
        var doctor = await factory.AddDoctorAsync(approved: false);
        var admin = await factory.AdminAsync();

        var list = await admin.GetHtmlAsync("/Locums");

        Assert.Contains(doctor.Email, list);
        Assert.Contains("Pending review", list);
    }

    // ───────────── pages that used to be reachable ─────────────

    [Theory]
    [InlineData("/Clients/Masquerade/1")]
    [InlineData("/Bookings/Delete/1")]
    [InlineData("/Bookings/GetAvailableLocums")]
    [InlineData("/Locums/Create")]
    [InlineData("/Locums/Edit/1")]
    [InlineData("/Locums/Delete/1")]
    [InlineData("/Clients/Create")]
    [InlineData("/Clients/Edit/1")]
    [InlineData("/Clients/Delete/1")]
    public async Task Routes_from_the_first_release_that_allowed_impersonation_or_hijacking_no_longer_exist(string path)
    {
        var admin = await factory.AdminAsync();

        var response = await admin.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
