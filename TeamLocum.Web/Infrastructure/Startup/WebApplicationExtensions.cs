using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using TeamLocum.Web.Data;
using TeamLocum.Web.Infrastructure.Middleware;
using TeamLocum.Web.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace TeamLocum.Web.Infrastructure.Startup;

public static class WebApplicationExtensions
{
    public static WebApplication UseTeamLocum(this WebApplication app)
    {
        // Outermost first: ids and headers exist even when something below throws.
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();

        // Dates, decimals and currency are entered and shown the UK way regardless of the server's own culture.
        app.UseRequestLocalization(new RequestLocalizationOptions()
            .SetDefaultCulture("en-GB")
            .AddSupportedCultures("en-GB")
            .AddSupportedUICultures("en-GB"));

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Home/Error");
        }

        app.UseStatusCodePagesWithReExecute("/Home/HttpStatus", "?code={0}");

        // TLS normally ends at a reverse proxy or container platform; opt in when Kestrel itself terminates it.
        if (app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value.HttpsRedirection)
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseMiddleware<DisabledIdentityPagesMiddleware>();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapStaticAssets();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
        app.MapRazorPages().WithStaticAssets();
        return app;
    }

    /// <summary>
    /// Validates configuration first, so a bad setup fails before any database work, then runs the opt-in startup data
    /// tasks (migrations, roles, demo data, bootstrap admin).
    /// </summary>
    public static async Task InitializeAsync(this WebApplication app)
    {
        app.Services.GetRequiredService<IStartupValidator>().Validate();

        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);
    }
}
