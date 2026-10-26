using System.Net;
using System.Text.RegularExpressions;

namespace TeamLocum.Tests.TestSupport;

/// <summary>The clicks of the staffing workflow, expressed as the form posts a browser would make.</summary>
public static partial class Flows
{
    [GeneratedRegex(@"/Bookings/Details/(\d+)")]
    private static partial Regex DetailsPath();

    [GeneratedRegex("name=\"stamp\"[^>]*value=\"([^\"]+)\"|value=\"([^\"]+)\"[^>]*name=\"stamp\"")]
    private static partial Regex StampField();

    [GeneratedRegex("name=\"Input.ConcurrencyStamp\"[^>]*value=\"([^\"]+)\"|value=\"([^\"]+)\"[^>]*name=\"Input.ConcurrencyStamp\"")]
    private static partial Regex EditStampField();

    public static string Iso(DateTime value) => value.ToString("yyyy-MM-ddTHH:mm");

    public static KeyValuePair<string, string> F(string key, string value) => new(key, value);

    public static int BookingIdFrom(HttpResponseMessage response)
    {
        var match = DetailsPath().Match(BrowserClient.Location(response));
        Assert.True(match.Success, $"Expected a redirect to a shift but got {(int)response.StatusCode} {BrowserClient.Location(response)}");
        return int.Parse(match.Groups[1].Value);
    }

    public static Task<HttpResponseMessage> PostShiftAsync(
        this BrowserClient client,
        DateTime start,
        DateTime end,
        decimal rate = 85m,
        string location = "City General Hospital, Leicester",
        string? specialty = "General practice",
        string? notes = null,
        int? clientId = null)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            F("Input.StartsAt", Iso(start)),
            F("Input.EndsAt", Iso(end)),
            F("Input.RatePerHour", rate.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            F("Input.Location", location),
            F("Input.Specialty", specialty ?? string.Empty),
            F("Input.Notes", notes ?? string.Empty),
            F("Input.ConcurrencyStamp", Guid.Empty.ToString())
        };
        if (clientId is { } id)
        {
            fields.Add(F("Input.ClientId", id.ToString()));
        }

        return client.PostFormAsync("/Bookings/Create", fields, tokenPage: "/Bookings/Create");
    }

    public static async Task<string> StampAsync(this BrowserClient client, int bookingId)
    {
        var html = await client.GetHtmlAsync($"/Bookings/Details/{bookingId}");
        var match = StampField().Match(html);
        Assert.True(match.Success, "The shift page offers no assign form (no concurrency stamp found).");
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    public static async Task<string> EditStampAsync(this BrowserClient client, int bookingId)
    {
        var html = await client.GetHtmlAsync($"/Bookings/Edit/{bookingId}");
        var match = EditStampField().Match(html);
        Assert.True(match.Success, "The edit form has no concurrency stamp.");
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    public static Task<HttpResponseMessage> ApplyAsync(this BrowserClient doctor, int bookingId, string? message = null) =>
        doctor.PostFormAsync($"/Bookings/Apply/{bookingId}", [F("message", message ?? string.Empty)], tokenPage: $"/Bookings/Details/{bookingId}");

    public static Task<HttpResponseMessage> WithdrawAsync(this BrowserClient doctor, int bookingId) =>
        doctor.PostFormAsync($"/Bookings/Withdraw/{bookingId}", tokenPage: $"/Bookings/Details/{bookingId}");

    public static async Task<HttpResponseMessage> AssignAsync(this BrowserClient owner, int bookingId, int locumId, string? stamp = null)
    {
        // The edit form always carries the version; the shift page only offers Assign for doctors who are free.
        stamp ??= await owner.EditStampAsync(bookingId);
        return await owner.PostFormAsync($"/Bookings/Assign/{bookingId}",
            [F("locumId", locumId.ToString()), F("stamp", stamp)], tokenPage: $"/Bookings/Details/{bookingId}");
    }

    public static Task<HttpResponseMessage> ActionAsync(this BrowserClient owner, string action, int bookingId) =>
        owner.PostFormAsync($"/Bookings/{action}/{bookingId}", tokenPage: $"/Bookings/Details/{bookingId}");

    public static bool IsRedirect(this HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.RedirectKeepVerb;
}
