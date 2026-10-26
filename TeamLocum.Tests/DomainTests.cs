using TeamLocum.Web.Domain;
using TeamLocum.Web.Services;

namespace TeamLocum.Tests;

/// <summary>The rules of the domain on their own, with no database and no web host.</summary>
public sealed class DomainTests
{
    private static readonly DateTime Start = new(2032, 5, 10, 9, 0, 0);
    private static readonly DateTime End = new(2032, 5, 10, 17, 0, 0);

    private static Booking OpenShift(decimal rate = 80m)
    {
        var booking = new Booking { Location = "Leicester", RatePerHour = rate };
        booking.SetWindow(Start, End);
        return booking;
    }

    private static Booking FilledShift()
    {
        var booking = OpenShift();
        booking.Fill(7);
        return booking;
    }

    // ───────────── approval states ─────────────

    public static TheoryData<ClientStatus, ClientStatus, bool> ClientMoves()
    {
        var allowed = new HashSet<(ClientStatus, ClientStatus)>
        {
            (ClientStatus.AwaitingReview, ClientStatus.Accepted), (ClientStatus.AwaitingReview, ClientStatus.Rejected),
            (ClientStatus.Accepted, ClientStatus.Suspended),
            (ClientStatus.Rejected, ClientStatus.Accepted),
            (ClientStatus.Suspended, ClientStatus.Accepted)
        };

        var data = new TheoryData<ClientStatus, ClientStatus, bool>();
        foreach (var from in Enum.GetValues<ClientStatus>())
        {
            foreach (var to in Enum.GetValues<ClientStatus>())
            {
                data.Add(from, to, allowed.Contains((from, to)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ClientMoves))]
    public void A_hospital_only_moves_along_the_approval_paths(ClientStatus from, ClientStatus to, bool allowed)
    {
        var client = new Client { UserId = "u", CompanyName = "c" };
        client.RestoreStatus(from);

        if (allowed)
        {
            client.MoveTo(to);
            Assert.Equal(to, client.Status);
        }
        else
        {
            var failure = Assert.Throws<InvalidTransitionException>(() => client.MoveTo(to));
            Assert.Contains($"{from} cannot move to {to}", failure.Message);
            Assert.Equal(from, client.Status);
        }
    }

    public static TheoryData<LocumStatus, LocumStatus, bool> LocumMoves()
    {
        var allowed = new HashSet<(LocumStatus, LocumStatus)>
        {
            (LocumStatus.PendingReview, LocumStatus.Active), (LocumStatus.PendingReview, LocumStatus.Rejected),
            (LocumStatus.Active, LocumStatus.Suspended),
            (LocumStatus.Rejected, LocumStatus.Active),
            (LocumStatus.Suspended, LocumStatus.Active)
        };

        var data = new TheoryData<LocumStatus, LocumStatus, bool>();
        foreach (var from in Enum.GetValues<LocumStatus>())
        {
            foreach (var to in Enum.GetValues<LocumStatus>())
            {
                data.Add(from, to, allowed.Contains((from, to)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LocumMoves))]
    public void A_doctor_only_moves_along_the_approval_paths(LocumStatus from, LocumStatus to, bool allowed)
    {
        var locum = new Locum { UserId = "u", GmcNumber = "1234567" };
        locum.RestoreStatus(from);

        if (allowed)
        {
            locum.MoveTo(to);
            Assert.Equal(to, locum.Status);
        }
        else
        {
            Assert.Throws<InvalidTransitionException>(() => locum.MoveTo(to));
            Assert.Equal(from, locum.Status);
        }
    }

    [Fact]
    public void New_accounts_start_unapproved_and_only_approved_ones_may_act()
    {
        var client = new Client { UserId = "u", CompanyName = "c" };
        var locum = new Locum { UserId = "u", GmcNumber = "1234567" };

        Assert.Equal(ClientStatus.AwaitingReview, client.Status);
        Assert.False(client.CanPostShifts);
        Assert.Equal(LocumStatus.PendingReview, locum.Status);
        Assert.False(locum.CanWork);

        client.MoveTo(ClientStatus.Accepted);
        locum.MoveTo(LocumStatus.Active);

        Assert.True(client.CanPostShifts);
        Assert.True(locum.CanWork);
    }

    [Theory]
    [InlineData("General practice, Paediatrics", new[] { "General practice", "Paediatrics" })]
    [InlineData("  Anaesthetics ;Intensive care ; ", new[] { "Anaesthetics", "Intensive care" })]
    [InlineData("", new string[0])]
    [InlineData(" , ; ", new string[0])]
    public void Specialties_are_split_on_commas_and_semicolons_and_trimmed(string stored, string[] expected)
    {
        var locum = new Locum { UserId = "u", GmcNumber = "1234567", Specialties = stored };

        Assert.Equal(expected, locum.SpecialtyList);
    }

    [Fact]
    public void A_name_is_the_first_and_last_name_without_stray_spaces()
    {
        Assert.Equal("Ada Lovelace", new ApplicationUser { FirstName = "Ada", LastName = "Lovelace" }.FullName);
        Assert.Equal("Ada", new ApplicationUser { FirstName = "Ada", LastName = "" }.FullName);
    }

    // ───────────── shift windows ─────────────

    [Fact]
    public void A_shift_must_end_after_it_starts_and_last_at_most_a_day()
    {
        Assert.Throws<DomainException>(() => Booking.ValidateWindow(Start, Start));
        Assert.Throws<DomainException>(() => Booking.ValidateWindow(End, Start));
        Assert.Throws<DomainException>(() => Booking.ValidateWindow(Start, Start.AddHours(24).AddMinutes(1)));

        Booking.ValidateWindow(Start, Start.AddMinutes(1));
        Booking.ValidateWindow(Start, Start.AddHours(24));
    }

    [Fact]
    public void An_overnight_shift_is_an_ordinary_shift_that_ends_the_next_day()
    {
        var shift = new Booking { Location = "Leicester", RatePerHour = 90m };

        shift.SetWindow(new DateTime(2032, 5, 10, 22, 0, 0), new DateTime(2032, 5, 11, 6, 0, 0));

        Assert.Equal(TimeSpan.FromHours(8), shift.Duration);
        Assert.Equal(720m, shift.TotalPay);
    }

    [Fact]
    public void Total_pay_is_the_rate_times_the_hours_rounded_to_pence()
    {
        var shift = new Booking { Location = "Leicester", RatePerHour = 85.5m };
        shift.SetWindow(Start, Start.AddHours(7).AddMinutes(30));

        Assert.Equal(641.25m, shift.TotalPay);

        shift.SetWindow(Start, Start.AddMinutes(20));
        shift.RatePerHour = 33.33m;
        Assert.Equal(11.11m, shift.TotalPay);
    }

    // ───────────── the life of a shift ─────────────

    [Fact]
    public void A_new_shift_is_open_with_no_doctor_and_filling_it_records_the_doctor()
    {
        var shift = OpenShift();

        Assert.Equal(BookingStatus.Open, shift.Status);
        Assert.Null(shift.LocumId);
        Assert.True(shift.IsLive);

        shift.Fill(7);

        Assert.Equal(BookingStatus.Filled, shift.Status);
        Assert.Equal(7, shift.LocumId);
        Assert.Throws<InvalidTransitionException>(() => shift.Fill(8));
    }

    [Fact]
    public void Releasing_reopens_a_filled_shift_only_before_it_starts()
    {
        var shift = FilledShift();

        shift.ReleaseLocum(Start.AddHours(-1));

        Assert.Equal(BookingStatus.Open, shift.Status);
        Assert.Null(shift.LocumId);
        Assert.Throws<InvalidTransitionException>(() => shift.ReleaseLocum(Start.AddHours(-1)));

        var started = FilledShift();
        var failure = Assert.Throws<InvalidTransitionException>(() => started.ReleaseLocum(Start));
        Assert.Contains("can no longer be reassigned", failure.Message);
        Assert.Equal(BookingStatus.Filled, started.Status);
    }

    [Fact]
    public void An_open_or_filled_shift_can_be_cancelled_only_before_it_starts()
    {
        var open = OpenShift();
        var filled = FilledShift();
        var started = OpenShift();

        open.Cancel(Start.AddMinutes(-1));
        filled.Cancel(Start.AddMinutes(-1));

        Assert.Equal(BookingStatus.Cancelled, open.Status);
        Assert.Equal(BookingStatus.Cancelled, filled.Status);
        Assert.Contains("has started", Assert.Throws<InvalidTransitionException>(() => started.Cancel(Start)).Message);
        Assert.Contains("Cancelled shift cannot be cancelled", Assert.Throws<InvalidTransitionException>(() => open.Cancel(Start.AddHours(-9))).Message);
    }

    [Fact]
    public void Only_a_filled_shift_that_has_finished_can_be_completed()
    {
        var open = OpenShift();
        var filled = FilledShift();

        Assert.Contains("Only a filled shift", Assert.Throws<InvalidTransitionException>(() => open.Complete(End)).Message);
        Assert.Contains("has not finished", Assert.Throws<InvalidTransitionException>(() => filled.Complete(End.AddMinutes(-1))).Message);

        filled.Complete(End);

        Assert.Equal(BookingStatus.Completed, filled.Status);
        Assert.False(filled.IsLive);
        Assert.Throws<InvalidTransitionException>(() => filled.Complete(End));
        Assert.Throws<InvalidTransitionException>(() => filled.Cancel(Start.AddDays(-1)));
    }

    [Fact]
    public void A_shift_that_is_no_longer_live_cannot_have_its_times_changed()
    {
        var shift = OpenShift();
        shift.Cancel(Start.AddDays(-1));

        Assert.Contains("Cancelled shift can no longer be changed", Assert.Throws<InvalidTransitionException>(() => shift.SetWindow(Start, End)).Message);
    }

    // ───────────── who is asking ─────────────

    [Theory]
    [InlineData(Roles.Admin, null, true)]
    [InlineData(Roles.Client, ClientStatus.Accepted, true)]
    [InlineData(Roles.Client, ClientStatus.AwaitingReview, false)]
    [InlineData(Roles.Client, ClientStatus.Suspended, false)]
    [InlineData(Roles.Locum, null, false)]
    public void Only_administrators_and_approved_hospitals_may_post_shifts(string role, ClientStatus? status, bool expected)
    {
        var actor = new Actor("u", role, role == Roles.Client ? 1 : null, status, role == Roles.Locum ? 1 : null, LocumStatus.Active);

        Assert.Equal(expected, actor.CanPostShifts);
    }

    [Theory]
    [InlineData(LocumStatus.Active, true)]
    [InlineData(LocumStatus.PendingReview, false)]
    [InlineData(LocumStatus.Suspended, false)]
    [InlineData(LocumStatus.Rejected, false)]
    public void Only_active_doctors_can_work(LocumStatus status, bool expected)
    {
        var actor = new Actor("u", Roles.Locum, null, null, 3, status);

        Assert.True(actor.IsLocum);
        Assert.False(actor.IsAdmin || actor.IsClient);
        Assert.Equal(expected, actor.CanWork);
        Assert.False(new Actor("u", Roles.Client, 1, ClientStatus.Accepted, null, null).CanWork);
    }

    [Fact]
    public async Task Anonymous_visitors_are_told_to_sign_in_when_an_actor_is_required()
    {
        var accessor = new AnonymousAccessor();

        var failure = await Assert.ThrowsAsync<ForbiddenException>(() => accessor.RequireAsync());

        Assert.Contains("sign in", failure.Message);
    }

    private sealed class AnonymousAccessor : IActorAccessor
    {
        public Task<Actor?> GetAsync(CancellationToken ct = default) => Task.FromResult<Actor?>(null);
    }

    // ───────────── input rules ─────────────

    [Fact]
    public void Shift_input_is_checked_for_rate_precision_and_window()
    {
        static List<string> Problems(BookingInput input) =>
            input.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(input)).Select(r => r.ErrorMessage!).ToList();

        Assert.Empty(Problems(new BookingInput { StartsAt = Start, EndsAt = End, RatePerHour = 85.25m }));
        Assert.Contains("at most two decimal places", Assert.Single(Problems(new BookingInput { StartsAt = Start, EndsAt = End, RatePerHour = 85.255m })));
        Assert.Contains("must end after it starts", Assert.Single(Problems(new BookingInput { StartsAt = End, EndsAt = Start, RatePerHour = 80m })));
        Assert.Contains("longer than 24 hours", Assert.Single(Problems(new BookingInput { StartsAt = Start, EndsAt = Start.AddHours(25), RatePerHour = 80m })));
        Assert.Empty(Problems(new BookingInput { StartsAt = null, EndsAt = null, RatePerHour = 80m }));
    }

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(41, 20, 3)]
    [InlineData(10, 0, 0)]
    public void The_number_of_pages_rounds_up(int total, int size, int expectedPages)
    {
        Assert.Equal(expectedPages, new PagedResult<int>([], 1, size, total).TotalPages);
    }
}
