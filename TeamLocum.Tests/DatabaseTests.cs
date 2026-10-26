using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamLocum.Tests.TestSupport;
using TeamLocum.Web.Data;
using TeamLocum.Web.Domain;

namespace TeamLocum.Tests;

/// <summary>
/// What the database guarantees on its own, whatever the application does: the rules that must hold even when two
/// requests race or someone writes SQL by hand.
/// </summary>
public sealed class DatabaseTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static readonly DateTime Day = new(2032, 5, 10);

    private static Booking NewBooking(
        int clientId, DateTime start, DateTime end, BookingStatus status = BookingStatus.Open, int? locumId = null, decimal rate = 80m)
    {
        var booking = new Booking { ClientId = clientId, Location = "Leicester", RatePerHour = rate, CreatedAt = DateTimeOffset.UtcNow };
        booking.Restore(start, end, status, locumId);
        return booking;
    }

    /// <summary>Saves the changes made by <paramref name="change"/> and returns the database's complaint, or null.</summary>
    private async Task<string?> FailureAsync(Func<ApplicationDbContext, Task> change)
    {
        var failure = await factory.WithDbAsync(async db =>
        {
            await change(db);
            return await Record.ExceptionAsync(() => db.SaveChangesAsync());
        });

        return failure is null ? null : (failure.InnerException ?? failure).Message;
    }

    // ───────────── the schema matches the model ─────────────

    [Fact]
    public async Task The_migration_creates_exactly_what_the_model_describes()
    {
        var pending = await factory.WithDbAsync(db => Task.FromResult(db.Database.HasPendingModelChanges()));

        Assert.False(pending, "The model changed without a new migration.");
    }

    [Fact]
    public async Task The_database_runs_in_write_ahead_logging_mode_with_foreign_keys_on()
    {
        var connectionString = await factory.WithDbAsync(db => Task.FromResult(db.Database.GetConnectionString()!));
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        Assert.Equal("wal", await ScalarAsync(connection, "PRAGMA journal_mode;"));
        Assert.Equal(1L, await ScalarAsync(connection, "PRAGMA foreign_keys;"));
    }

    [Fact]
    public async Task Both_overlap_triggers_exist()
    {
        var names = await factory.WithDbAsync(db =>
            db.Database.SqlQuery<string>($"SELECT name AS Value FROM sqlite_master WHERE type = 'trigger'").ToListAsync());

        Assert.Contains("TR_Bookings_NoLocumOverlap_Insert", names);
        Assert.Contains("TR_Bookings_NoLocumOverlap_Update", names);
    }

    // ───────────── a doctor can never hold two overlapping shifts ─────────────

    [Fact]
    public async Task The_database_refuses_a_second_overlapping_filled_shift_for_the_same_doctor()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();

        var first = await FailureAsync(db =>
        {
            db.Bookings.Add(NewBooking(hospital.ProfileId, Day.AddHours(9), Day.AddHours(17), BookingStatus.Filled, doctor.ProfileId));
            return Task.CompletedTask;
        });
        var second = await FailureAsync(db =>
        {
            db.Bookings.Add(NewBooking(hospital.ProfileId, Day.AddHours(16), Day.AddHours(20), BookingStatus.Filled, doctor.ProfileId));
            return Task.CompletedTask;
        });

        Assert.Null(first);
        Assert.Contains("booking_overlap", second);
    }

    [Fact]
    public async Task A_completed_shift_still_counts_towards_the_overlap_rule()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        await FailureAsync(db =>
        {
            db.Bookings.Add(NewBooking(hospital.ProfileId, Day.AddHours(9), Day.AddHours(17), BookingStatus.Completed, doctor.ProfileId));
            return Task.CompletedTask;
        });

        var failure = await FailureAsync(db =>
        {
            db.Bookings.Add(NewBooking(hospital.ProfileId, Day.AddHours(12), Day.AddHours(14), BookingStatus.Filled, doctor.ProfileId));
            return Task.CompletedTask;
        });

        Assert.Contains("booking_overlap", failure);
    }

    [Fact]
    public async Task Back_to_back_shifts_and_shifts_for_different_doctors_are_fine()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        var colleague = await factory.AddDoctorAsync();

        var failure = await FailureAsync(db =>
        {
            db.Bookings.AddRange(
                NewBooking(hospital.ProfileId, Day.AddHours(9), Day.AddHours(17), BookingStatus.Filled, doctor.ProfileId),
                NewBooking(hospital.ProfileId, Day.AddHours(17), Day.AddHours(21), BookingStatus.Filled, doctor.ProfileId),
                NewBooking(hospital.ProfileId, Day.AddHours(10), Day.AddHours(18), BookingStatus.Filled, colleague.ProfileId));
            return Task.CompletedTask;
        });

        Assert.Null(failure);
    }

    [Fact]
    public async Task Open_and_cancelled_shifts_never_conflict_with_anything()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();

        var failure = await FailureAsync(db =>
        {
            db.Bookings.AddRange(
                NewBooking(hospital.ProfileId, Day.AddHours(9), Day.AddHours(17), BookingStatus.Filled, doctor.ProfileId),
                NewBooking(hospital.ProfileId, Day.AddHours(10), Day.AddHours(16), BookingStatus.Open),
                NewBooking(hospital.ProfileId, Day.AddHours(10), Day.AddHours(16), BookingStatus.Cancelled, doctor.ProfileId));
            return Task.CompletedTask;
        });

        Assert.Null(failure);
    }

    [Fact]
    public async Task Filling_a_shift_late_is_caught_by_the_update_trigger_when_two_requests_raced()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        int openId = 0;
        await FailureAsync(db =>
        {
            var held = NewBooking(hospital.ProfileId, Day.AddHours(9), Day.AddHours(17), BookingStatus.Filled, doctor.ProfileId);
            var open = NewBooking(hospital.ProfileId, Day.AddHours(10), Day.AddHours(18));
            db.Bookings.AddRange(held, open);
            return Task.CompletedTask;
        });
        openId = await factory.WithDbAsync(db => db.Bookings.Where(b => b.Status == BookingStatus.Open && b.ClientId == hospital.ProfileId)
            .Select(b => b.Id).SingleAsync());

        var failure = await FailureAsync(async db => (await db.Bookings.SingleAsync(b => b.Id == openId)).Fill(doctor.ProfileId));

        Assert.Contains("booking_overlap", failure);
    }

    [Fact]
    public async Task Moving_a_filled_shift_onto_another_of_the_doctors_shifts_is_refused_but_editing_its_text_is_not()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        await FailureAsync(db =>
        {
            db.Bookings.AddRange(
                NewBooking(hospital.ProfileId, Day.AddHours(9), Day.AddHours(13), BookingStatus.Filled, doctor.ProfileId),
                NewBooking(hospital.ProfileId, Day.AddHours(15), Day.AddHours(19), BookingStatus.Filled, doctor.ProfileId));
            return Task.CompletedTask;
        });
        var afternoon = await factory.WithDbAsync(db => db.Bookings
            .Where(b => b.LocumId == doctor.ProfileId && b.StartsAt == Day.AddHours(15)).Select(b => b.Id).SingleAsync());

        var rename = await FailureAsync(async db => (await db.Bookings.SingleAsync(b => b.Id == afternoon)).Location = "Somewhere else");
        var move = await FailureAsync(async db =>
        {
            var booking = await db.Bookings.SingleAsync(b => b.Id == afternoon);
            booking.SetWindow(Day.AddHours(12), Day.AddHours(16));
        });

        Assert.Null(rename);
        Assert.Contains("booking_overlap", move);
    }

    // ───────────── CHECK constraints ─────────────

    public static TheoryData<string, int, int, BookingStatus, bool, decimal, string> InvalidBookings => new()
    {
        { "window ends when it starts", 9, 9, BookingStatus.Open, false, 80m, "CK_Bookings_Window" },
        { "window ends before it starts", 17, 9, BookingStatus.Open, false, 80m, "CK_Bookings_Window" },
        { "rate of zero", 9, 17, BookingStatus.Open, false, 0m, "CK_Bookings_Rate" },
        { "negative rate", 9, 17, BookingStatus.Open, false, -5m, "CK_Bookings_Rate" },
        { "filled shift without a doctor", 9, 17, BookingStatus.Filled, false, 80m, "CK_Bookings_FilledHasLocum" },
        { "completed shift without a doctor", 9, 17, BookingStatus.Completed, false, 80m, "CK_Bookings_FilledHasLocum" },
        { "open shift that already has a doctor", 9, 17, BookingStatus.Open, true, 80m, "CK_Bookings_OpenHasNoLocum" }
    };

    [Theory]
    [MemberData(nameof(InvalidBookings))]
    public async Task Impossible_shifts_are_refused_by_the_database(
        string scenario, int fromHour, int toHour, BookingStatus status, bool withDoctor, decimal rate, string constraint)
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();

        var failure = await FailureAsync(db =>
        {
            db.Bookings.Add(NewBooking(hospital.ProfileId, Day.AddHours(fromHour), Day.AddHours(toHour), status,
                withDoctor ? doctor.ProfileId : null, rate));
            return Task.CompletedTask;
        });

        Assert.True(failure is not null && failure.Contains(constraint), $"{scenario}: expected {constraint}, got {failure ?? "success"}");
    }

    [Fact]
    public async Task A_doctor_cannot_be_saved_with_impossible_experience_or_a_negative_minimum_rate()
    {
        var doctor = await factory.AddDoctorAsync();

        var years = await FailureAsync(async db => (await db.Locums.SingleAsync(l => l.Id == doctor.ProfileId)).YearsExperience = 61);
        var rate = await FailureAsync(async db => (await db.Locums.SingleAsync(l => l.Id == doctor.ProfileId)).MinRatePerHour = -1m);

        Assert.Contains("CK_Locums_YearsExperience", years);
        Assert.Contains("CK_Locums_MinRatePerHour", rate);
    }

    // ───────────── uniqueness and references ─────────────

    [Fact]
    public async Task Two_doctors_cannot_share_a_gmc_number()
    {
        await factory.AddDoctorAsync(gmc: "9990001");

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => factory.AddDoctorAsync(gmc: "9990001"));

        Assert.Contains("Locums.GmcNumber", failure.InnerException!.Message);
    }

    [Fact]
    public async Task An_account_has_at_most_one_hospital_profile()
    {
        var hospital = await factory.AddHospitalAsync();

        var failure = await FailureAsync(db =>
        {
            db.Clients.Add(new Client { UserId = hospital.UserId, CompanyName = "Second profile", CreatedAt = DateTimeOffset.UtcNow });
            return Task.CompletedTask;
        });

        Assert.Contains("Clients.UserId", failure);
    }

    [Fact]
    public async Task A_date_can_be_a_bank_holiday_only_once()
    {
        var date = new DateOnly(2033, 1, 3);
        await FailureAsync(db =>
        {
            db.Holidays.Add(new Holiday { Date = date, Details = "First" });
            return Task.CompletedTask;
        });

        var failure = await FailureAsync(db =>
        {
            db.Holidays.Add(new Holiday { Date = date, Details = "Second" });
            return Task.CompletedTask;
        });

        Assert.Contains("Holidays.Date", failure);
    }

    [Fact]
    public async Task A_doctor_can_have_only_one_application_per_shift()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        await FailureAsync(db =>
        {
            db.ShiftApplications.Add(new ShiftApplication { BookingId = shiftId, LocumId = doctor.ProfileId, CreatedAt = DateTimeOffset.UtcNow });
            return Task.CompletedTask;
        });

        var failure = await FailureAsync(db =>
        {
            db.ShiftApplications.Add(new ShiftApplication { BookingId = shiftId, LocumId = doctor.ProfileId, CreatedAt = DateTimeOffset.UtcNow });
            return Task.CompletedTask;
        });

        Assert.Contains("ShiftApplications", failure);
    }

    [Fact]
    public async Task A_hospital_or_doctor_with_shifts_cannot_be_deleted_from_under_them()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        await FailureAsync(db =>
        {
            db.Bookings.Add(NewBooking(hospital.ProfileId, Day.AddHours(9), Day.AddHours(17), BookingStatus.Filled, doctor.ProfileId));
            return Task.CompletedTask;
        });

        var hospitalDelete = await FailureAsync(async db => db.Clients.Remove(await db.Clients.SingleAsync(c => c.Id == hospital.ProfileId)));
        var doctorDelete = await FailureAsync(async db => db.Locums.Remove(await db.Locums.SingleAsync(l => l.Id == doctor.ProfileId)));

        Assert.Contains("FOREIGN KEY", hospitalDelete);
        Assert.Contains("FOREIGN KEY", doctorDelete);
    }

    [Fact]
    public async Task Deleting_an_account_without_history_removes_its_profile_and_deleting_a_shift_removes_its_applications()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        await FailureAsync(db =>
        {
            db.ShiftApplications.Add(new ShiftApplication { BookingId = shiftId, LocumId = doctor.ProfileId, CreatedAt = DateTimeOffset.UtcNow });
            return Task.CompletedTask;
        });

        await FailureAsync(async db => db.Bookings.Remove(await db.Bookings.SingleAsync(b => b.Id == shiftId)));
        Assert.Equal(0, await factory.WithDbAsync(db => db.ShiftApplications.CountAsync(a => a.BookingId == shiftId)));

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.DeleteAsync((await users.FindByIdAsync(doctor.UserId))!)).Succeeded);
        Assert.False(await factory.WithDbAsync(db => db.Locums.AnyAsync(l => l.Id == doctor.ProfileId)));
    }

    // ───────────── how values are stored ─────────────

    [Fact]
    public async Task Money_is_stored_as_whole_pence_and_read_back_exactly()
    {
        var hospital = await factory.AddHospitalAsync();
        await FailureAsync(db =>
        {
            db.Bookings.Add(NewBooking(hospital.ProfileId, Day.AddHours(9), Day.AddHours(17), rate: 85.55m));
            return Task.CompletedTask;
        });

        var stored = await factory.WithDbAsync(db => db.Database
            .SqlQuery<long>($"SELECT RatePerHour AS Value FROM Bookings WHERE ClientId = {hospital.ProfileId}").SingleAsync());
        var loaded = await factory.WithDbAsync(db => db.Bookings.Where(b => b.ClientId == hospital.ProfileId).Select(b => b.RatePerHour).SingleAsync());

        Assert.Equal(8555L, stored);
        Assert.Equal(85.55m, loaded);
    }

    [Fact]
    public async Task Timestamps_are_stored_as_unix_milliseconds_and_statuses_as_readable_text()
    {
        var doctor = await factory.AddDoctorAsync();

        var created = await factory.WithDbAsync(db => db.Database.SqlQuery<long>($"SELECT CreatedAt AS Value FROM Locums WHERE Id = {doctor.ProfileId}").SingleAsync());
        var status = await factory.WithDbAsync(db => db.Database.SqlQuery<string>($"SELECT Status AS Value FROM Locums WHERE Id = {doctor.ProfileId}").SingleAsync());

        Assert.InRange(created, DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds(), DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds());
        Assert.Equal("Active", status);
    }

    // ───────────── optimistic concurrency ─────────────

    [Fact]
    public async Task A_stale_copy_of_a_shift_cannot_overwrite_a_newer_one()
    {
        var hospital = await factory.AddHospitalAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));

        await using var first = factory.Services.CreateAsyncScope();
        await using var second = factory.Services.CreateAsyncScope();
        var dbA = first.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dbB = second.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var a = await dbA.Bookings.SingleAsync(b => b.Id == shiftId);
        var b = await dbB.Bookings.SingleAsync(x => x.Id == shiftId);

        b.Location = "Changed by B";
        b.ConcurrencyStamp = Guid.NewGuid();
        await dbB.SaveChangesAsync();

        a.Location = "Changed by A";
        a.ConcurrencyStamp = Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbA.SaveChangesAsync());
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
