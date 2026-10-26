using System.Net;
using Microsoft.EntityFrameworkCore;
using TeamLocum.Tests.TestSupport;
using TeamLocum.Web.Domain;
using static TeamLocum.Tests.TestSupport.Flows;

namespace TeamLocum.Tests;

public sealed class BookingWorkflowTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private DateTime Tomorrow9 => factory.ClinicNow.Date.AddDays(2).AddHours(9);

    // ───────────── posting shifts ─────────────

    [Fact]
    public async Task An_approved_hospital_posts_a_shift_and_sees_it_in_its_list()
    {
        var hospital = await factory.AddHospitalAsync();
        var client = await factory.SignInAsync(hospital);

        var response = await client.PostShiftAsync(Tomorrow9, Tomorrow9.AddHours(8), rate: 92.5m, location: "Poster Hospital, Derby", specialty: "Paediatrics");

        Assert.True(response.IsRedirect());
        var id = BookingIdFrom(response);
        var page = await client.FollowAsync(response);
        Assert.Contains("Shift posted. Doctors can now apply.", page);
        Assert.Contains("Poster Hospital, Derby", page);
        Assert.Contains("Paediatrics", page);
        Assert.Contains("Not assigned yet", page);

        var list = await client.GetHtmlAsync("/Bookings");
        Assert.Contains($"/Bookings/Details/{id}", list);
    }

    [Theory]
    [InlineData("end-before-start", "must end after it starts")]
    [InlineData("too-long", "cannot be longer than 24 hours")]
    [InlineData("in-the-past", "must start in the future")]
    [InlineData("zero-rate", "between £1 and £1,000")]
    public async Task Invalid_shifts_are_refused_with_an_explanation_and_nothing_is_saved(string scenario, string expected)
    {
        var hospital = await factory.AddHospitalAsync();
        var client = await factory.SignInAsync(hospital);
        var start = Tomorrow9;
        var (from, to, rate) = scenario switch
        {
            "end-before-start" => (start, start.AddHours(-2), 85m),
            "too-long" => (start, start.AddHours(25), 85m),
            "in-the-past" => (factory.ClinicNow.AddHours(-3), factory.ClinicNow.AddHours(3), 85m),
            _ => (start, start.AddHours(8), 0m)
        };

        var response = await client.PostShiftAsync(from, to, rate: rate);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(0, await factory.WithDbAsync(db => db.Bookings.CountAsync(b => b.ClientId == hospital.ProfileId)));
    }

    [Fact]
    public async Task A_shift_can_run_overnight()
    {
        var hospital = await factory.AddHospitalAsync();
        var client = await factory.SignInAsync(hospital);

        var response = await client.PostShiftAsync(Tomorrow9.AddHours(13), Tomorrow9.AddHours(21));

        Assert.True(response.IsRedirect());
        var booking = await factory.WithDbAsync(db => db.Bookings.SingleAsync(b => b.Id == BookingIdFrom(response)));
        Assert.True(booking.EndsAt.Date > booking.StartsAt.Date);
        Assert.Equal(TimeSpan.FromHours(8), booking.Duration);
    }

    [Fact]
    public async Task A_shift_on_a_bank_holiday_is_flagged_but_allowed()
    {
        var holidayDate = DateOnly.FromDateTime(factory.ClinicNow.Date.AddDays(40));
        await factory.WithDbAsync(async db =>
        {
            db.Holidays.Add(new Holiday { Date = holidayDate, Details = "Test Bank Holiday" });
            await db.SaveChangesAsync();
            return 0;
        });
        var hospital = await factory.AddHospitalAsync();
        var client = await factory.SignInAsync(hospital);
        var start = holidayDate.ToDateTime(new TimeOnly(9, 0));

        var response = await client.PostShiftAsync(start, start.AddHours(8));

        Assert.True(response.IsRedirect());
        var page = await client.FollowAsync(response);
        Assert.Contains("falls on a bank holiday: Test Bank Holiday", page);
    }

    [Fact]
    public async Task A_hospital_awaiting_review_cannot_post_shifts()
    {
        var pending = await factory.AddHospitalAsync(approved: false);
        var client = await factory.SignInAsync(pending);

        var response = await client.PostShiftAsync(Tomorrow9, Tomorrow9.AddHours(8));

        Assert.True(response.IsRedirect());
        Assert.Contains("AccessDenied", BrowserClient.Location(response));
        Assert.Equal(0, await factory.WithDbAsync(db => db.Bookings.CountAsync(b => b.ClientId == pending.ProfileId)));
    }

    [Fact]
    public async Task Doctors_cannot_post_shifts()
    {
        var doctor = await factory.AddDoctorAsync();
        var client = await factory.SignInAsync(doctor);

        var page = await client.GetAsync("/Bookings/Create");
        Assert.Contains("AccessDenied", BrowserClient.Location(page));
    }

    [Fact]
    public async Task An_administrator_can_post_a_shift_for_an_approved_hospital_only()
    {
        var approved = await factory.AddHospitalAsync();
        var pending = await factory.AddHospitalAsync(approved: false);
        var admin = await factory.AdminAsync();

        var ok = await admin.PostShiftAsync(Tomorrow9, Tomorrow9.AddHours(8), clientId: approved.ProfileId);
        var refused = await admin.PostShiftAsync(Tomorrow9, Tomorrow9.AddHours(8), clientId: pending.ProfileId);
        var missing = await admin.PostShiftAsync(Tomorrow9, Tomorrow9.AddHours(8));

        Assert.True(ok.IsRedirect());
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("has not been approved", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        Assert.Contains("Choose the hospital", await missing.Content.ReadAsStringAsync());
    }

    // ───────────── applying and assigning ─────────────

    [Fact]
    public async Task A_doctor_applies_and_the_hospital_assigns_them()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync(first: "Maya", last: "Applicant");
        var rival = await factory.AddDoctorAsync(first: "Rita", last: "Rival");
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var doctorClient = await factory.SignInAsync(doctor);
        var rivalClient = await factory.SignInAsync(rival);
        var hospitalClient = await factory.SignInAsync(hospital);

        var applied = await doctorClient.ApplyAsync(shiftId, "I can start early.");
        Assert.Contains("Application sent", await doctorClient.FollowAsync(applied));
        await rivalClient.ApplyAsync(shiftId);

        var review = await hospitalClient.GetHtmlAsync($"/Bookings/Details/{shiftId}");
        Assert.Contains("Maya Applicant", review);
        Assert.Contains("Rita Rival", review);
        Assert.Contains("I can start early.", review);

        var assigned = await hospitalClient.AssignAsync(shiftId, doctor.ProfileId);
        Assert.Contains("Doctor assigned. The shift is filled.", await hospitalClient.FollowAsync(assigned));

        var (status, applications) = await factory.WithDbAsync(async db => (
            (await db.Bookings.SingleAsync(b => b.Id == shiftId)).Status,
            await db.ShiftApplications.Where(a => a.BookingId == shiftId).ToDictionaryAsync(a => a.LocumId, a => a.Status)));
        Assert.Equal(BookingStatus.Filled, status);
        Assert.Equal(ApplicationStatus.Accepted, applications[doctor.ProfileId]);
        Assert.Equal(ApplicationStatus.Declined, applications[rival.ProfileId]);

        var mine = await doctorClient.GetHtmlAsync($"/Bookings/Details/{shiftId}");
        Assert.Contains("You", mine);
        Assert.DoesNotContain("Apply for this shift", mine);
    }

    [Fact]
    public async Task A_doctor_cannot_apply_twice_and_can_withdraw_and_reapply()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var client = await factory.SignInAsync(doctor);

        await client.ApplyAsync(shiftId);
        var twice = await client.ApplyAsync(shiftId);
        Assert.Contains("already applied", await client.FollowAsync(twice));

        var withdrawn = await client.WithdrawAsync(shiftId);
        Assert.Contains("Application withdrawn.", await client.FollowAsync(withdrawn));

        var again = await client.ApplyAsync(shiftId, "Changed my mind");
        Assert.Contains("Application sent", await client.FollowAsync(again));
        var count = await factory.WithDbAsync(db => db.ShiftApplications.CountAsync(a => a.BookingId == shiftId && a.LocumId == doctor.ProfileId));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task A_doctor_awaiting_approval_cannot_apply()
    {
        var hospital = await factory.AddHospitalAsync();
        var pending = await factory.AddDoctorAsync(approved: false);
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var client = await factory.SignInAsync(pending);

        var response = await client.PostFormAsync($"/Bookings/Apply/{shiftId}", [F("message", string.Empty)]);

        Assert.True(response.IsRedirect());
        Assert.Contains("AccessDenied", BrowserClient.Location(response));
        Assert.Equal(0, await factory.WithDbAsync(db => db.ShiftApplications.CountAsync(a => a.BookingId == shiftId)));
    }

    [Fact]
    public async Task A_doctor_cannot_apply_to_a_shift_that_overlaps_one_they_already_hold()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        var first = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var second = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(34), TimeSpan.FromHours(8));
        var hospitalClient = await factory.SignInAsync(hospital);
        var doctorClient = await factory.SignInAsync(doctor);
        await doctorClient.ApplyAsync(first);
        await hospitalClient.AssignAsync(first, doctor.ProfileId);

        var response = await doctorClient.ApplyAsync(second);

        Assert.Contains("already have a shift that overlaps", await doctorClient.FollowAsync(response));
        Assert.Equal(0, await factory.WithDbAsync(db => db.ShiftApplications.CountAsync(a => a.BookingId == second)));
    }

    [Fact]
    public async Task Assigning_a_busy_doctor_is_refused_and_the_shift_stays_open()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        var first = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var overlapping = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(33), TimeSpan.FromHours(8));
        var adjacent = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(38), TimeSpan.FromHours(4));
        var client = await factory.SignInAsync(hospital);
        await client.AssignAsync(first, doctor.ProfileId);

        // Flash messages live for one request, so each response is read before the next action.
        var refused = await client.AssignAsync(overlapping, doctor.ProfileId);
        Assert.Contains("already has a shift that overlaps", await client.FollowAsync(refused));
        var allowed = await client.AssignAsync(adjacent, doctor.ProfileId);
        Assert.Contains("Doctor assigned", await client.FollowAsync(allowed));
        var statuses = await factory.WithDbAsync(db => db.Bookings.Where(b => new[] { first, overlapping, adjacent }.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Status));
        Assert.Equal(BookingStatus.Filled, statuses[first]);
        Assert.Equal(BookingStatus.Open, statuses[overlapping]);
        Assert.Equal(BookingStatus.Filled, statuses[adjacent]);
    }

    [Fact]
    public async Task Two_hospitals_racing_to_assign_one_doctor_to_overlapping_shifts_cannot_both_win()
    {
        var hospitalA = await factory.AddHospitalAsync();
        var hospitalB = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        var shiftA = await factory.AddShiftAsync(hospitalA, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var shiftB = await factory.AddShiftAsync(hospitalB, TimeSpan.FromHours(32), TimeSpan.FromHours(8));
        var clientA = await factory.SignInAsync(hospitalA);
        var clientB = await factory.SignInAsync(hospitalB);
        var stampA = await clientA.StampAsync(shiftA);
        var stampB = await clientB.StampAsync(shiftB);
        var tokenA = await clientA.TokenFromAsync($"/Bookings/Details/{shiftA}");
        var tokenB = await clientB.TokenFromAsync($"/Bookings/Details/{shiftB}");

        var results = await Task.WhenAll(
            clientA.PostWithTokenAsync($"/Bookings/Assign/{shiftA}", [F("locumId", doctor.ProfileId.ToString()), F("stamp", stampA)], tokenA),
            clientB.PostWithTokenAsync($"/Bookings/Assign/{shiftB}", [F("locumId", doctor.ProfileId.ToString()), F("stamp", stampB)], tokenB));

        Assert.All(results, r => Assert.True(r.IsRedirect()));
        var filled = await factory.WithDbAsync(db => db.Bookings.CountAsync(b => b.LocumId == doctor.ProfileId && b.Status == BookingStatus.Filled));
        Assert.Equal(1, filled);
    }

    // ───────────── editing, releasing, cancelling, completing ─────────────

    [Fact]
    public async Task Editing_a_shift_with_a_stale_version_is_refused_and_changes_nothing()
    {
        var hospital = await factory.AddHospitalAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var one = await factory.SignInAsync(hospital);
        var two = await factory.SignInAsync(hospital);
        var oneStamp = await one.EditStampAsync(shiftId);
        var twoStamp = await two.EditStampAsync(shiftId);
        var start = factory.ClinicNow.Date.AddDays(3).AddHours(10);

        List<KeyValuePair<string, string>> Edit(string location, string stamp) =>
        [
            F("Input.StartsAt", Iso(start)), F("Input.EndsAt", Iso(start.AddHours(8))), F("Input.RatePerHour", "90"),
            F("Input.Location", location), F("Input.Specialty", "General practice"), F("Input.ConcurrencyStamp", stamp)
        ];

        var first = await one.PostFormAsync($"/Bookings/Edit/{shiftId}", Edit("First edit", oneStamp), $"/Bookings/Edit/{shiftId}");
        var second = await two.PostFormAsync($"/Bookings/Edit/{shiftId}", Edit("Second edit", twoStamp), $"/Bookings/Edit/{shiftId}");

        Assert.True(first.IsRedirect());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("changed by someone else", await second.Content.ReadAsStringAsync());
        var saved = await factory.WithDbAsync(db => db.Bookings.SingleAsync(b => b.Id == shiftId));
        Assert.Equal("First edit", saved.Location);
    }

    [Fact]
    public async Task Moving_a_filled_shift_onto_another_of_the_doctors_shifts_is_refused()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        var first = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(4));
        var second = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(40), TimeSpan.FromHours(4));
        var client = await factory.SignInAsync(hospital);
        await client.AssignAsync(first, doctor.ProfileId);
        await client.AssignAsync(second, doctor.ProfileId);
        var stamp = await client.EditStampAsync(second);
        var clash = await factory.WithDbAsync(async db => (await db.Bookings.SingleAsync(b => b.Id == first)).StartsAt.AddHours(1));

        var response = await client.PostFormAsync($"/Bookings/Edit/{second}",
        [
            F("Input.StartsAt", Iso(clash)), F("Input.EndsAt", Iso(clash.AddHours(2))), F("Input.RatePerHour", "90"),
            F("Input.Location", "Moved"), F("Input.ConcurrencyStamp", stamp)
        ], $"/Bookings/Edit/{second}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("overlap", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_filled_shift_can_release_its_doctor_and_be_filled_again()
    {
        var hospital = await factory.AddHospitalAsync();
        var one = await factory.AddDoctorAsync();
        var two = await factory.AddDoctorAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var client = await factory.SignInAsync(hospital);
        await client.AssignAsync(shiftId, one.ProfileId);

        var released = await client.ActionAsync("Release", shiftId);
        Assert.Contains("open again", await client.FollowAsync(released));
        var again = await client.AssignAsync(shiftId, two.ProfileId);
        Assert.Contains("Doctor assigned", await client.FollowAsync(again));

        var booking = await factory.WithDbAsync(db => db.Bookings.SingleAsync(b => b.Id == shiftId));
        Assert.Equal(two.ProfileId, booking.LocumId);
    }

    [Fact]
    public async Task Cancelling_frees_the_doctor_for_another_shift_at_the_same_time()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        var original = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var replacement = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var client = await factory.SignInAsync(hospital);
        await client.AssignAsync(original, doctor.ProfileId);

        var cancelled = await client.ActionAsync("Cancel", original);
        Assert.Contains("Shift cancelled.", await client.FollowAsync(cancelled));
        var assigned = await client.AssignAsync(replacement, doctor.ProfileId);

        Assert.Contains("Doctor assigned", await client.FollowAsync(assigned));
    }

    [Fact]
    public async Task A_shift_can_only_be_completed_after_it_has_ended()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(2), TimeSpan.FromHours(3));
        var client = await factory.SignInAsync(hospital);
        await client.AssignAsync(shiftId, doctor.ProfileId);

        var tooEarly = await client.ActionAsync("Complete", shiftId);
        Assert.Contains("has not finished yet", await client.FollowAsync(tooEarly));

        factory.Clock.Advance(TimeSpan.FromHours(6));
        var ok = await client.ActionAsync("Complete", shiftId);

        Assert.Contains("marked as completed", await client.FollowAsync(ok));
        Assert.Equal(BookingStatus.Completed, (await factory.WithDbAsync(db => db.Bookings.SingleAsync(b => b.Id == shiftId))).Status);
    }

    [Fact]
    public async Task A_shift_that_has_started_cannot_be_cancelled_or_edited()
    {
        var hospital = await factory.AddHospitalAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(-1), TimeSpan.FromHours(6));
        var client = await factory.SignInAsync(hospital);

        var cancel = await client.ActionAsync("Cancel", shiftId);
        Assert.Contains("has started cannot be cancelled", await client.FollowAsync(cancel));

        var details = await client.GetHtmlAsync($"/Bookings/Details/{shiftId}");
        Assert.DoesNotContain("Edit shift", details);
        Assert.DoesNotContain("Cancel shift", details);
    }
}
