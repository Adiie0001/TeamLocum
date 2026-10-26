using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TeamLocum.Tests.TestSupport;
using TeamLocum.Web.Domain;
using static TeamLocum.Tests.TestSupport.Flows;

namespace TeamLocum.Tests;

public sealed class AdminTests(AppFactory factory) : IClassFixture<AppFactory>
{
    // ───────────── approvals ─────────────

    [Fact]
    public async Task Approving_a_doctor_lets_them_apply_and_suspending_takes_it_away()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync(approved: false);
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var admin = await factory.AdminAsync();
        var client = await factory.SignInAsync(doctor);

        var beforeApproval = await client.PostFormAsync($"/Bookings/Apply/{shiftId}");
        Assert.Contains("AccessDenied", BrowserClient.Location(beforeApproval));

        var approve = await admin.PostFormAsync($"/Locums/Approve/{doctor.ProfileId}", tokenPage: "/Locums");
        Assert.Contains("Doctor approved.", await admin.FollowAsync(approve));
        var afterApproval = await client.ApplyAsync(shiftId);
        Assert.Contains("Application sent", await client.FollowAsync(afterApproval));

        var suspend = await admin.PostFormAsync($"/Locums/Suspend/{doctor.ProfileId}", tokenPage: "/Locums");
        Assert.Contains("Doctor suspended.", await admin.FollowAsync(suspend));
        var shiftAgain = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(60), TimeSpan.FromHours(8));
        var afterSuspension = await client.PostFormAsync($"/Bookings/Apply/{shiftAgain}");
        Assert.Contains("AccessDenied", BrowserClient.Location(afterSuspension));

        var reinstate = await admin.PostFormAsync($"/Locums/Reinstate/{doctor.ProfileId}", tokenPage: "/Locums");
        Assert.Contains("Doctor reinstated.", await admin.FollowAsync(reinstate));
        Assert.Equal(LocumStatus.Active, (await factory.WithDbAsync(db => db.Locums.SingleAsync(l => l.Id == doctor.ProfileId))).Status);
    }

    [Fact]
    public async Task Approving_a_hospital_lets_it_post_shifts_and_rejecting_keeps_it_out()
    {
        var pending = await factory.AddHospitalAsync(approved: false);
        var admin = await factory.AdminAsync();
        var client = await factory.SignInAsync(pending);
        var start = factory.ClinicNow.Date.AddDays(3).AddHours(9);

        var before = await client.PostShiftAsync(start, start.AddHours(8));
        Assert.Contains("AccessDenied", BrowserClient.Location(before));

        await admin.PostFormAsync($"/Clients/Approve/{pending.ProfileId}", tokenPage: "/Clients");
        var after = await client.PostShiftAsync(start, start.AddHours(8));
        Assert.True(after.IsRedirect());
        Assert.Contains("/Bookings/Details/", BrowserClient.Location(after));

        var other = await factory.AddHospitalAsync(approved: false);
        var reject = await admin.PostFormAsync($"/Clients/Reject/{other.ProfileId}", tokenPage: "/Clients");
        Assert.Contains("Hospital rejected.", await admin.FollowAsync(reject));
        var rejectedClient = await factory.SignInAsync(other);
        var denied = await rejectedClient.PostShiftAsync(start, start.AddHours(8));
        Assert.Contains("AccessDenied", BrowserClient.Location(denied));
    }

    [Fact]
    public async Task An_illegal_status_change_is_explained_and_changes_nothing()
    {
        var doctor = await factory.AddDoctorAsync(approved: true);
        var admin = await factory.AdminAsync();

        // An active doctor cannot be "rejected"; they have to be suspended.
        var response = await admin.PostFormAsync($"/Locums/Reject/{doctor.ProfileId}", tokenPage: "/Locums");

        Assert.Contains("cannot move to Rejected", await admin.FollowAsync(response));
        Assert.Equal(LocumStatus.Active, (await factory.WithDbAsync(db => db.Locums.SingleAsync(l => l.Id == doctor.ProfileId))).Status);
    }

    [Fact]
    public async Task Approving_something_that_does_not_exist_is_a_404()
    {
        var admin = await factory.AdminAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostFormAsync("/Locums/Approve/999999", tokenPage: "/Locums")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostFormAsync("/Clients/Approve/999999", tokenPage: "/Clients")).StatusCode);
    }

    [Fact]
    public async Task Only_administrators_can_approve_anyone()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync(approved: false);
        var hospitalClient = await factory.SignInAsync(hospital);
        var doctorClient = await factory.SignInAsync(doctor);

        var byHospital = await hospitalClient.PostFormAsync($"/Locums/Approve/{doctor.ProfileId}");
        var byDoctor = await doctorClient.PostFormAsync($"/Locums/Approve/{doctor.ProfileId}");
        var clientByDoctor = await doctorClient.PostFormAsync($"/Clients/Approve/{hospital.ProfileId}");

        Assert.All(new[] { byHospital, byDoctor, clientByDoctor }, r => Assert.Contains("AccessDenied", BrowserClient.Location(r)));
        Assert.Equal(LocumStatus.PendingReview, (await factory.WithDbAsync(db => db.Locums.SingleAsync(l => l.Id == doctor.ProfileId))).Status);
    }

    [Fact]
    public async Task The_dashboard_counts_what_is_waiting_for_review()
    {
        var before = await factory.WithDbAsync(async db => (await db.Locums.CountAsync(l => l.Status == LocumStatus.PendingReview),
                                                              await db.Clients.CountAsync(c => c.Status == ClientStatus.AwaitingReview)));
        await factory.AddDoctorAsync(approved: false);
        await factory.AddHospitalAsync(approved: false);
        var admin = await factory.AdminAsync();

        var html = await admin.GetHtmlAsync("/Admin");

        Assert.Contains("Doctors awaiting approval", html);
        Assert.Matches($@"{before.Item1 + 1}</div>\s*<div class=""text-muted"">Doctors awaiting approval", html);
        Assert.Matches($@"{before.Item2 + 1}</div>\s*<div class=""text-muted"">Hospitals awaiting approval", html);
    }

    [Fact]
    public async Task The_accounts_page_lists_everyone_with_their_roles()
    {
        var doctor = await factory.AddDoctorAsync();
        var hospital = await factory.AddHospitalAsync();
        var admin = await factory.AdminAsync();

        var html = await admin.GetHtmlAsync("/Admin/Users");

        Assert.Matches($@"{Regex.Escape(doctor.Email)}</td>\s*<td>Locum</td>", html);
        Assert.Matches($@"{Regex.Escape(hospital.Email)}</td>\s*<td>Client</td>", html);
        Assert.Matches($@"{Regex.Escape(AppFactory.AdminEmail)}</td>\s*<td>Admin</td>", html);
    }

    // ───────────── bank holidays ─────────────

    [Fact]
    public async Task Administrators_can_add_and_remove_bank_holidays_and_everyone_can_read_them()
    {
        var admin = await factory.AdminAsync();
        var name = $"Test Day {Guid.NewGuid():N}";

        var add = await admin.PostFormAsync("/Holidays/Create", [F("Date", "2031-05-05"), F("Details", name)], "/Holidays/Create");
        Assert.Contains("Bank holiday added.", await admin.FollowAsync(add));

        var doctor = await factory.SignInAsync(await factory.AddDoctorAsync());
        Assert.Contains(name, await doctor.GetHtmlAsync("/Holidays"));

        var id = (await factory.WithDbAsync(db => db.Holidays.SingleAsync(h => h.Details == name))).Id;
        var remove = await admin.PostFormAsync($"/Holidays/Delete/{id}", tokenPage: "/Holidays");
        Assert.Contains("Bank holiday removed.", await admin.FollowAsync(remove));
        Assert.False(await factory.WithDbAsync(db => db.Holidays.AnyAsync(h => h.Id == id)));
    }

    [Fact]
    public async Task A_date_cannot_be_listed_twice_and_non_administrators_cannot_edit_the_list()
    {
        var admin = await factory.AdminAsync();
        await admin.PostFormAsync("/Holidays/Create", [F("Date", "2032-01-01"), F("Details", "First")], "/Holidays/Create");

        var duplicate = await admin.PostFormAsync("/Holidays/Create", [F("Date", "2032-01-01"), F("Details", "Second")], "/Holidays/Create");
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("already on the list", await duplicate.Content.ReadAsStringAsync());

        var hospital = await factory.SignInAsync(await factory.AddHospitalAsync());
        var byHospital = await hospital.PostFormAsync("/Holidays/Create", [F("Date", "2032-02-02"), F("Details", "Sneaky")]);
        Assert.Contains("AccessDenied", BrowserClient.Location(byHospital));
        Assert.False(await factory.WithDbAsync(db => db.Holidays.AnyAsync(h => h.Details == "Sneaky")));
    }

    // ───────────── own profiles ─────────────

    [Fact]
    public async Task A_doctor_can_update_their_profile_but_not_their_status_or_gmc_number()
    {
        var doctor = await factory.AddDoctorAsync(approved: false, gmc: "6660001");
        var client = await factory.SignInAsync(doctor);

        var response = await client.PostFormAsync("/Locums/Profile",
        [
            F("Input.FirstName", "Renamed"), F("Input.LastName", "Person"), F("Input.Specialties", "Anaesthetics,  Intensive care"),
            F("Input.BaseLocation", "Derby"), F("Input.MinRatePerHour", "88"), F("Input.YearsExperience", "11"),
            F("Status", "Active"), F("Input.Status", "Active"), F("Input.GmcNumber", "9999999"), F("GmcNumber", "9999999")
        ], "/Locums/Profile");

        Assert.True(response.IsRedirect());
        var locum = await factory.WithDbAsync(db => db.Locums.Include(l => l.User).SingleAsync(l => l.Id == doctor.ProfileId));
        Assert.Equal("Anaesthetics, Intensive care", locum.Specialties);
        Assert.Equal("Derby", locum.BaseLocation);
        Assert.Equal(88m, locum.MinRatePerHour);
        Assert.Equal("Renamed Person", locum.User!.FullName);
        Assert.Equal(LocumStatus.PendingReview, locum.Status);
        Assert.Equal("6660001", locum.GmcNumber);
    }

    [Fact]
    public async Task A_hospital_can_update_its_profile_but_not_its_status()
    {
        var hospital = await factory.AddHospitalAsync(approved: false);
        var client = await factory.SignInAsync(hospital);

        var response = await client.PostFormAsync("/Clients/Profile",
        [
            F("Input.FirstName", "New"), F("Input.LastName", "Contact"), F("Input.CompanyName", "Renamed Clinic"),
            F("Input.CarasId", "REF-42"), F("Status", "Accepted"), F("Input.Status", "Accepted")
        ], "/Clients/Profile");

        Assert.True(response.IsRedirect());
        var client2 = await factory.WithDbAsync(db => db.Clients.SingleAsync(c => c.Id == hospital.ProfileId));
        Assert.Equal("Renamed Clinic", client2.CompanyName);
        Assert.Equal("REF-42", client2.CarasId);
        Assert.Equal(ClientStatus.AwaitingReview, client2.Status);
    }

    [Fact]
    public async Task Invalid_profile_input_is_redisplayed_with_errors()
    {
        var doctor = await factory.AddDoctorAsync();
        var client = await factory.SignInAsync(doctor);

        var response = await client.PostFormAsync("/Locums/Profile",
        [
            F("Input.FirstName", ""), F("Input.LastName", "X"), F("Input.Specialties", "Y"), F("Input.BaseLocation", "Z"),
            F("Input.MinRatePerHour", "5000"), F("Input.YearsExperience", "99")
        ], "/Locums/Profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("field-validation-error", html);
    }
}
