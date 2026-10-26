using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TeamLocum.Web.Domain;
using TeamLocum.Web.Infrastructure.Options;
using TeamLocum.Web.Services;

namespace TeamLocum.Web.Data;

/// <summary>
/// Startup data tasks, each opt-in through configuration: apply migrations, make sure the roles exist, insert demo data
/// (Development only, enforced by <see cref="SeedOptionsValidator"/>) and create a bootstrap administrator.
/// </summary>
public sealed class DatabaseInitializer(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    ClinicClock clock,
    IOptions<DatabaseOptions> database,
    IOptions<SeedOptions> seed,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (database.Value.ApplyMigrationsOnStartup)
        {
            logger.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync(ct);

            if (db.Database.IsSqlite())
            {
                // Write-ahead logging lets readers run while the single writer commits; the mode is stored in the file.
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
            }
        }

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count > 0)
        {
            throw new InvalidOperationException(
                $"The database has {pending.Count} pending migration(s). Run 'dotnet ef database update' or set " +
                "Database__ApplyMigrationsOnStartup=true.");
        }

        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole(role));
            }
        }

        var options = seed.Value;
        if (options.DemoData && !await users.Users.AnyAsync(ct))
        {
            await new DemoDataSeeder(db, users, clock, logger).SeedAsync(ct);
        }

        if (!string.IsNullOrWhiteSpace(options.AdminEmail))
        {
            await EnsureBootstrapAdminAsync(options, ct);
        }
    }

    private async Task EnsureBootstrapAdminAsync(SeedOptions options, CancellationToken ct)
    {
        if ((await users.GetUsersInRoleAsync(Roles.Admin)).Count > 0)
        {
            return;
        }

        if (await users.FindByEmailAsync(options.AdminEmail!) is not null)
        {
            logger.LogWarning("Bootstrap admin email is already registered as a non-admin account; leaving it unchanged");
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = options.AdminEmail,
            Email = options.AdminEmail,
            EmailConfirmed = true,
            FirstName = "Site",
            LastName = "Administrator",
            CreatedAt = clock.UtcNow
        };

        var created = await users.CreateAsync(admin, options.AdminPassword!);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                "The bootstrap administrator could not be created: " + string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        await users.AddToRoleAsync(admin, Roles.Admin);
        logger.LogInformation("Created the bootstrap administrator");
    }
}
