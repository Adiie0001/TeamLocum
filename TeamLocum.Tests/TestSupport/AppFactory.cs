using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TeamLocum.Web.Data;
using TeamLocum.Web.Domain;
using TeamLocum.Web.Services.Matching;

namespace TeamLocum.Tests.TestSupport;

public sealed record TestUser(string Email, string Password, string UserId, int ProfileId, string Name);

/// <summary>
/// Boots the real application in-process against a private temporary SQLite file with the real migrations applied. Only
/// the clock, the Gemini HTTP endpoint and the (deliberately slow) password hash cost are replaced.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "root@clinic.test";
    public const string AdminPassword = "Sup3r-Secret-Pass";
    public const string UserPassword = "Correct-Horse-9";

    public const string GeminiKey = "test-gemini-key-0123456789";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"teamlocum-test-{Guid.NewGuid():N}.db");
    private readonly Dictionary<string, string?> _settings;
    private readonly string _environment;
    private int _sequence;

    public AppFactory() : this(null, "Testing")
    {
    }

    // xUnit class fixtures may expose only one public constructor, so variants are created through With().
    private AppFactory(Dictionary<string, string?>? overrides, string environment)
    {
        _environment = environment;
        _settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Data Source={_databasePath}",
            ["Database:ApplyMigrationsOnStartup"] = "true",
            ["Seed:AdminEmail"] = AdminEmail,
            ["Seed:AdminPassword"] = AdminPassword,
            ["RateLimiting:Enabled"] = "false",
            ["Matching:Gemini:Enabled"] = "false"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                _settings[key] = value;
            }
        }
    }

    public static AppFactory With(Dictionary<string, string?> overrides, string environment = "Testing") =>
        new(overrides, environment);

    /// <summary>Settings that turn the AI matcher on against <see cref="Gemini"/>.</summary>
    public static Dictionary<string, string?> WithGemini() => new()
    {
        ["Matching:Gemini:Enabled"] = "true",
        ["Matching:Gemini:ApiKey"] = GeminiKey,
        ["Matching:Gemini:BaseUrl"] = "https://gemini.test/",
        ["Matching:Gemini:CircuitBreakerMinimumRequests"] = "1000"
    };

    public OffsetTimeProvider Clock { get; } = new();

    /// <summary>The clinic's wall-clock "now", in the same zone the application uses.</summary>
    public DateTime ClinicNow =>
        TimeZoneInfo.ConvertTime(Clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime;

    public FakeGeminiHandler Gemini { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        // UseSetting feeds host configuration, which exists before Program.cs starts reading anything.
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            // Production hashing cost is deliberate; tests create many accounts.
            services.Configure<PasswordHasherOptions>(options => options.IterationCount = 1000);

            services.AddHttpClient<IGeminiMatchClient, GeminiMatchClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Gemini);

            services.AddControllers().AddApplicationPart(typeof(AppFactory).Assembly);
        });
    }

    public BrowserClient NewClient() => new(CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

    public async Task<BrowserClient> AdminAsync()
    {
        var client = NewClient();
        await client.LoginAsync(AdminEmail, AdminPassword);
        return client;
    }

    /// <summary>Signs in as someone created by <see cref="AddHospitalAsync"/> or <see cref="AddDoctorAsync"/>.</summary>
    public async Task<BrowserClient> SignInAsync(TestUser user)
    {
        var client = NewClient();
        await client.LoginAsync(user.Email, user.Password);
        return client;
    }

    // ───────────── data helpers (straight through the services, no UI) ─────────────

    public async Task<TestUser> AddHospitalAsync(string? company = null, bool approved = true, string? city = null)
    {
        var n = Interlocked.Increment(ref _sequence);
        company ??= $"Hospital {n}";
        var email = $"hospital{n}-{Guid.NewGuid():N}@clinic.test";

        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await CreateUserAsync(users, email, "Hospital", $"Contact{n}", Roles.Client);
        var client = new Client { UserId = user.Id, CompanyName = company, CreatedAt = Clock.GetUtcNow() };
        if (approved)
        {
            client.MoveTo(ClientStatus.Accepted);
        }

        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return new TestUser(email, UserPassword, user.Id, client.Id, company);
    }

    public async Task<TestUser> AddDoctorAsync(
        string first = "Test",
        string? last = null,
        string specialties = "General practice",
        string baseLocation = "Leicester",
        decimal minRate = 60m,
        int years = 5,
        bool approved = true,
        string? gmc = null)
    {
        var n = Interlocked.Increment(ref _sequence);
        last ??= $"Doctor{n}";
        var email = $"doctor{n}-{Guid.NewGuid():N}@clinic.test";
        gmc ??= (1_000_000 + n).ToString();

        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await CreateUserAsync(users, email, first, last, Roles.Locum);
        var locum = new Locum
        {
            UserId = user.Id,
            GmcNumber = gmc,
            Specialties = specialties,
            BaseLocation = baseLocation,
            MinRatePerHour = minRate,
            YearsExperience = years,
            CreatedAt = Clock.GetUtcNow()
        };
        if (approved)
        {
            locum.MoveTo(LocumStatus.Active);
        }

        db.Locums.Add(locum);
        await db.SaveChangesAsync();
        return new TestUser(email, UserPassword, user.Id, locum.Id, $"{first} {last}");
    }

    /// <summary>A shift posted straight into the database. Start is relative to the clinic's "now".</summary>
    public async Task<int> AddShiftAsync(
        TestUser hospital,
        TimeSpan startsIn,
        TimeSpan duration,
        decimal rate = 85m,
        string location = "City General Hospital, Leicester",
        string? specialty = "General practice")
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TeamLocum.Web.Services.ClinicClock>();

        var start = clock.Now.Date.AddHours(clock.Now.Hour).Add(startsIn);
        var booking = new Booking
        {
            ClientId = hospital.ProfileId,
            Location = location,
            Specialty = specialty,
            RatePerHour = rate,
            CreatedAt = Clock.GetUtcNow()
        };
        booking.SetWindow(start, start + duration);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    public async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private async Task<ApplicationUser> CreateUserAsync(UserManager<ApplicationUser> users, string email, string first, string last, string role)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = first,
            LastName = last,
            CreatedAt = Clock.GetUtcNow()
        };

        var created = await users.CreateAsync(user, UserPassword);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, role);
        return user;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            // Release pooled connections so the file can be removed on Windows.
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try
                {
                    File.Delete(_databasePath + suffix);
                }
                catch (IOException)
                {
                    // Best effort: a leftover temp file is harmless.
                }
            }
        }
    }
}
