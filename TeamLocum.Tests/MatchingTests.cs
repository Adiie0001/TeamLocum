using System.Text.Json;
using TeamLocum.Tests.TestSupport;
using static TeamLocum.Tests.TestSupport.Flows;

namespace TeamLocum.Tests;

/// <summary>The "suggested doctors" part of the shift page with the transparent rules (no AI configured).</summary>
public sealed class RuleBasedSuggestionsTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static string Unique() => Guid.NewGuid().ToString("N")[..6];

    [Fact]
    public async Task The_shift_page_ranks_free_approved_doctors_and_explains_every_point()
    {
        var hospital = await factory.AddHospitalAsync();
        var tag = Unique();
        await factory.AddDoctorAsync("Omega", $"Weak{tag}", "Psychiatry", "Glasgow", 200m, 0);
        await factory.AddDoctorAsync("Alpha", $"Best{tag}", "GP, Paediatrics", "Leicester", 60m, 10);
        await factory.AddDoctorAsync("Pending", $"Person{tag}", "General practice", "Leicester", approved: false);
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8),
            rate: 85m, location: "Leicester Royal Infirmary", specialty: "General practice");
        var client = await factory.SignInAsync(hospital);

        var html = System.Net.WebUtility.HtmlDecode(await client.GetHtmlAsync($"/Bookings/Details/{shiftId}"));

        Assert.Contains("Ranked by transparent rules", html);
        Assert.True(html.IndexOf($"Best{tag}", StringComparison.Ordinal) < html.IndexOf($"Weak{tag}", StringComparison.Ordinal));
        Assert.DoesNotContain($"Person{tag}", html);
        Assert.Contains("Specialty: 40 / 40, works in General practice", html);
        Assert.Contains("Location: 25 / 25, based in Leicester", html);
        Assert.Contains("Rate: 15 / 15, rate meets the £60/h minimum", html);
        Assert.Contains("Experience: 10 / 10, 10 years' experience", html);
        Assert.Contains("No matching factors.", html);
    }

    [Fact]
    public async Task A_doctor_who_has_applied_is_marked_and_earns_interest_points()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync("Keen", $"Applicant{Unique()}", "General practice", "Leicester");
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8), location: "Leicester");
        await (await factory.SignInAsync(doctor)).ApplyAsync(shiftId);

        var html = await (await factory.SignInAsync(hospital)).GetHtmlAsync($"/Bookings/Details/{shiftId}");

        Assert.Contains("Interest: 10 / 10, applied for this shift", html);
        Assert.Contains(">Applied</span>", html);
    }

    [Fact]
    public async Task A_doctor_who_is_already_busy_at_those_times_is_not_suggested()
    {
        var busy = await factory.AddDoctorAsync("Busy", $"Booked{Unique()}", "General practice", "Leicester");
        var elsewhere = await factory.AddHospitalAsync();
        var theirShift = await factory.AddShiftAsync(elsewhere, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        await (await factory.SignInAsync(busy)).ApplyAsync(theirShift);
        await (await factory.SignInAsync(elsewhere)).AssignAsync(theirShift, busy.ProfileId);

        var hospital = await factory.AddHospitalAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(32), TimeSpan.FromHours(4));

        var html = await (await factory.SignInAsync(hospital)).GetHtmlAsync($"/Bookings/Details/{shiftId}");

        Assert.DoesNotContain(busy.Name, html);
    }

    [Fact]
    public async Task Doctors_never_see_suggestions_and_a_filled_shift_stops_offering_them()
    {
        var hospital = await factory.AddHospitalAsync();
        var doctor = await factory.AddDoctorAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var doctorClient = await factory.SignInAsync(doctor);
        var owner = await factory.SignInAsync(hospital);

        Assert.DoesNotContain("Suggested doctors", await doctorClient.GetHtmlAsync($"/Bookings/Details/{shiftId}"));
        Assert.Contains("Suggested doctors", await owner.GetHtmlAsync($"/Bookings/Details/{shiftId}"));

        await doctorClient.ApplyAsync(shiftId);
        await owner.AssignAsync(shiftId, doctor.ProfileId);

        Assert.DoesNotContain("Suggested doctors", await owner.GetHtmlAsync($"/Bookings/Details/{shiftId}"));
    }

    [Fact]
    public async Task Another_hospital_cannot_see_the_suggestions_for_a_shift_that_is_not_theirs()
    {
        var owner = await factory.AddHospitalAsync();
        var other = await factory.AddHospitalAsync();
        var shiftId = await factory.AddShiftAsync(owner, TimeSpan.FromHours(30), TimeSpan.FromHours(8));

        var response = await (await factory.SignInAsync(other)).GetAsync($"/Bookings/Details/{shiftId}");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Only_the_configured_number_of_suggestions_is_shown()
    {
        using var small = AppFactory.With(new() { ["Matching:MaxResults"] = "2" });
        var hospital = await small.AddHospitalAsync();
        for (var i = 0; i < 4; i++)
        {
            await small.AddDoctorAsync();
        }

        var shiftId = await small.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));
        var html = await (await small.SignInAsync(hospital)).GetHtmlAsync($"/Bookings/Details/{shiftId}");

        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(html, "Score breakdown"));
    }

    [Fact]
    public async Task With_no_free_approved_doctor_the_page_says_so()
    {
        using var empty = AppFactory.With([]);
        var hospital = await empty.AddHospitalAsync();
        var shiftId = await empty.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8));

        var html = await (await empty.SignInAsync(hospital)).GetHtmlAsync($"/Bookings/Details/{shiftId}");

        Assert.Contains("No approved doctor is free for this shift.", html);
    }
}

/// <summary>The same page with the optional AI ranking switched on, against a fake Gemini endpoint.</summary>
public sealed class GeminiSuggestionsTests
{
    /// <summary>A "model" that likes doctors based in Leicester, reading only what was actually sent.</summary>
    private static HttpResponseMessage ScoreByLocation(FakeGeminiHandler.RecordedRequest request, string leicesterReason = "Local and experienced.")
    {
        using var payload = InnerPayload(request);
        var scores = payload.RootElement.GetProperty("candidates").EnumerateArray().Select(candidate =>
        {
            var local = candidate.GetProperty("baseLocation").GetString() == "Leicester";
            return new { @ref = candidate.GetProperty("ref").GetString(), score = local ? 95 : 10, reason = local ? leicesterReason : "Far away." };
        });
        return FakeGeminiHandler.Envelope(JsonSerializer.Serialize(scores));
    }

    /// <summary>The JSON document the app put in the user message of the request.</summary>
    private static JsonDocument InnerPayload(FakeGeminiHandler.RecordedRequest request)
    {
        using var body = JsonDocument.Parse(request.Body);
        var text = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
        return JsonDocument.Parse(text);
    }

    private static AppFactory WithModel(Func<FakeGeminiHandler.RecordedRequest, HttpResponseMessage>? responder = null)
    {
        var factory = AppFactory.With(AppFactory.WithGemini());
        if (responder is not null)
        {
            factory.Gemini.Responder = responder;
        }

        return factory;
    }

    private static async Task<(int ShiftId, BrowserClient Owner)> OpenShiftAsync(AppFactory factory, string location = "Leicester")
    {
        var hospital = await factory.AddHospitalAsync();
        var shiftId = await factory.AddShiftAsync(hospital, TimeSpan.FromHours(30), TimeSpan.FromHours(8), location: location);
        return (shiftId, await factory.SignInAsync(hospital));
    }

    [Fact]
    public async Task When_the_model_answers_its_ranking_is_shown_and_labelled_as_AI()
    {
        using var factory = WithModel(request => ScoreByLocation(request));
        await factory.AddDoctorAsync("Aaron", "Distant", baseLocation: "Aberdeen");
        await factory.AddDoctorAsync("Zoe", "Nearby", baseLocation: "Leicester");
        var (shiftId, owner) = await OpenShiftAsync(factory);

        var html = await owner.GetHtmlAsync($"/Bookings/Details/{shiftId}");

        Assert.Contains("Ranked by an AI model from anonymised profiles", html);
        Assert.True(html.IndexOf("Zoe Nearby", StringComparison.Ordinal) < html.IndexOf("Aaron Distant", StringComparison.Ordinal));
        Assert.Contains("Local and experienced.", html);
        Assert.Contains("Far away.", html);
        Assert.Single(factory.Gemini.Requests);
    }

    [Fact]
    public async Task What_is_sent_to_the_model_is_anonymous_and_the_key_stays_in_the_header()
    {
        using var factory = WithModel(request => ScoreByLocation(request));
        var hospital = await factory.AddHospitalAsync("Confidential Infirmary Ltd");
        var doctor = await factory.AddDoctorAsync("Priya", "Secretname", baseLocation: "Leicester", gmc: "7654321");
        var owner = await factory.SignInAsync(hospital);
        var start = factory.ClinicNow.Date.AddDays(3).AddHours(9);
        var posted = await owner.PostShiftAsync(start, start.AddHours(8), location: "Leicester", notes: "Patient Jane Roe in bay 4, call 07700 900123");

        await owner.GetHtmlAsync($"/Bookings/Details/{BookingIdFrom(posted)}");

        var request = Assert.Single(factory.Gemini.Requests);
        Assert.Equal("gemini.test", request.Uri.Host);
        Assert.Equal("/v1beta/models/gemini-2.5-flash:generateContent", request.Uri.AbsolutePath);
        Assert.Equal(string.Empty, request.Uri.Query);
        Assert.DoesNotContain(AppFactory.GeminiKey, request.Uri.ToString());
        Assert.Equal(AppFactory.GeminiKey, request.Headers["x-goog-api-key"]);

        foreach (var secret in new[]
                 {
                     "Priya", "Secretname", "7654321", "Confidential", "Jane Roe", "07700", "bay 4",
                     doctor.Email, doctor.UserId, hospital.Email, hospital.UserId, AppFactory.GeminiKey
                 })
        {
            Assert.DoesNotContain(secret, request.Body, StringComparison.OrdinalIgnoreCase);
        }

        using var payload = InnerPayload(request);
        Assert.Equal("c1", payload.RootElement.GetProperty("candidates")[0].GetProperty("ref").GetString());
        Assert.Equal("Leicester", payload.RootElement.GetProperty("shift").GetProperty("location").GetString());
    }

    [Fact]
    public async Task When_the_model_is_unavailable_the_rules_answer_and_the_page_says_so()
    {
        using var factory = WithModel(); // the fake answers HTTP 500 until told otherwise
        var doctor = await factory.AddDoctorAsync("Still", "Ranked", baseLocation: "Leicester");
        var (shiftId, owner) = await OpenShiftAsync(factory);

        var html = await owner.GetHtmlAsync($"/Bookings/Details/{shiftId}");

        Assert.Contains("The AI ranking was unavailable", html);
        Assert.DoesNotContain("Ranked by an AI model", html);
        Assert.Contains(doctor.Name, html);
        Assert.Contains("Score breakdown", html);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("[]")]
    [InlineData("[{\"ref\":\"nobody\",\"score\":90,\"reason\":\"unknown ref\"}]")]
    public async Task An_unusable_model_answer_falls_back_to_the_rules(string answer)
    {
        using var factory = WithModel(_ => FakeGeminiHandler.Envelope(answer));
        await factory.AddDoctorAsync(baseLocation: "Leicester");
        var (shiftId, owner) = await OpenShiftAsync(factory);

        var html = await owner.GetHtmlAsync($"/Bookings/Details/{shiftId}");

        Assert.Contains("The AI ranking was unavailable", html);
    }

    [Fact]
    public async Task A_reason_containing_markup_is_shown_as_text()
    {
        using var factory = WithModel(request => ScoreByLocation(request, "<script>alert(1)</script><b>bold</b>"));
        await factory.AddDoctorAsync(baseLocation: "Leicester");
        var (shiftId, owner) = await OpenShiftAsync(factory);

        var html = await owner.GetHtmlAsync($"/Bookings/Details/{shiftId}");

        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;&lt;b&gt;bold&lt;/b&gt;", html);
        Assert.DoesNotContain("<script>alert(1)", html);
    }

    [Fact]
    public async Task The_same_question_is_answered_from_the_cache()
    {
        using var factory = WithModel(request => ScoreByLocation(request));
        await factory.AddDoctorAsync(baseLocation: "Leicester");
        var (shiftId, owner) = await OpenShiftAsync(factory);

        await owner.GetHtmlAsync($"/Bookings/Details/{shiftId}");
        await owner.GetHtmlAsync($"/Bookings/Details/{shiftId}");

        Assert.Single(factory.Gemini.Requests);
    }

    [Fact]
    public async Task With_nobody_to_rank_the_model_is_not_called_at_all()
    {
        using var factory = WithModel(request => ScoreByLocation(request));
        var (shiftId, owner) = await OpenShiftAsync(factory);

        var html = await owner.GetHtmlAsync($"/Bookings/Details/{shiftId}");

        Assert.Contains("No approved doctor is free for this shift.", html);
        Assert.Empty(factory.Gemini.Requests);
    }

    [Fact]
    public async Task With_AI_off_nothing_ever_leaves_the_server()
    {
        using var factory = AppFactory.With([]);
        await factory.AddDoctorAsync();
        var (shiftId, owner) = await OpenShiftAsync(factory);

        await owner.GetHtmlAsync($"/Bookings/Details/{shiftId}");

        Assert.Empty(factory.Gemini.Requests);
    }
}
