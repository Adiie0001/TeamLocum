using System.Net;
using System.Text.RegularExpressions;

namespace TeamLocum.Tests.TestSupport;

/// <summary>
/// A browser-like client: it keeps cookies, never follows redirects on its own (so tests can assert them) and fetches
/// the anti-forgery token the way a real page would before it posts a form.
/// </summary>
public sealed partial class BrowserClient(HttpClient http)
{
    public HttpClient Http => http;

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    public Task<HttpResponseMessage> GetAsync(string url) => http.GetAsync(url);

    public async Task<string> GetHtmlAsync(string url, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await http.GetAsync(url);
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Posts a form the way a browser would: GET a page for its token and cookie, then POST.</summary>
    public async Task<HttpResponseMessage> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>>? fields = null, string tokenPage = "/")
    {
        var token = await TokenFromAsync(tokenPage);
        return await PostWithTokenAsync(url, fields, token, referrerPage: tokenPage);
    }

    public Task<HttpResponseMessage> PostWithTokenAsync(
        string url, IEnumerable<KeyValuePair<string, string>>? fields, string? token, string? referrerPage = null)
    {
        var data = (fields ?? []).ToList();
        if (token is not null)
        {
            data.Add(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(data) };
        if (referrerPage is not null)
        {
            // A browser sends the page the form was on; the app uses it to return the user there after an error.
            request.Headers.Referrer = new Uri(http.BaseAddress!, referrerPage);
        }

        return http.SendAsync(request);
    }

    public Task<HttpResponseMessage> PostNoTokenAsync(string url, IEnumerable<KeyValuePair<string, string>>? fields = null) =>
        PostWithTokenAsync(url, fields, token: null);

    public async Task<string> TokenFromAsync(string page)
    {
        var html = await http.GetStringAsync(page);
        var match = TokenPattern().Match(html);
        Assert.True(match.Success, $"No anti-forgery token on {page}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public async Task<HttpResponseMessage> LoginAsync(string email, string password, string? returnUrl = null)
    {
        var page = "/Identity/Account/Login" + (returnUrl is null ? string.Empty : $"?returnUrl={Uri.EscapeDataString(returnUrl)}");
        var response = await PostFormAsync(page,
            [new("Input.Email", email), new("Input.Password", password)], tokenPage: "/Identity/Account/Login");
        return response;
    }

    /// <summary>The redirect target as a path (and query), whether the server sent an absolute or a relative URL.</summary>
    public static string Location(HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        return location is null ? string.Empty : location.IsAbsoluteUri ? location.PathAndQuery : location.ToString();
    }

    /// <summary>Follows one redirect (and any flash message it carries) and returns the page HTML.</summary>
    public async Task<string> FollowAsync(HttpResponseMessage redirect)
    {
        Assert.True(redirect.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.RedirectKeepVerb or HttpStatusCode.SeeOther,
            $"Expected a redirect but got {(int)redirect.StatusCode}.");
        var response = await http.GetAsync(redirect.Headers.Location);
        return await response.Content.ReadAsStringAsync();
    }
}
