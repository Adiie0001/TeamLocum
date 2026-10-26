namespace TeamLocum.Web.Infrastructure.Middleware;

/// <summary>
/// Propagates an <c>X-Correlation-ID</c> from the caller (or creates one), echoes it on the response, and puts it in the
/// logging scope so every log line of a request can be found from the id a user reports.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Accept(context.Request.Headers[HeaderName].FirstOrDefault()) ?? Guid.NewGuid().ToString("N");
        context.Items[ItemKey] = correlationId;

        // OnStarting survives the exception handler clearing the response, so error pages carry the id too.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { [ItemKey] = correlationId }))
        {
            await next(context);
        }
    }

    /// <summary>Accepts short ids made of letters, digits and "-_." only; anything else could forge log lines.</summary>
    internal static string? Accept(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxLength)
        {
            return null;
        }

        return candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') ? candidate : null;
    }
}
