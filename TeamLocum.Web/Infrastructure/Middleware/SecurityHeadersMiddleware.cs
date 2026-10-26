namespace TeamLocum.Web.Infrastructure.Middleware;

/// <summary>
/// Response headers for a server-rendered app that loads everything from its own origin: no inline script or style, no
/// framing, no third-party content. Pages that need more must say so by changing this policy, not by working around it.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; script-src 'self'; style-src 'self'; font-src 'self'; " +
        "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Content-Security-Policy"] = ContentSecurityPolicy;

            // Pages show personal and staffing data: keep them out of shared caches and the back-forward cache.
            if (context.User.Identity?.IsAuthenticated == true && !headers.ContainsKey("Cache-Control"))
            {
                headers["Cache-Control"] = "no-store";
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
