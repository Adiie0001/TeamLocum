using Microsoft.Extensions.Options;
using TeamLocum.Web.Infrastructure.Options;

namespace TeamLocum.Web.Services;

/// <summary>
/// "Now" as the clinic experiences it. Shift times are wall-clock times in the clinic's time zone, so deciding whether a
/// shift has started must use that zone, not the server's (containers run in UTC).
/// </summary>
public sealed class ClinicClock(TimeProvider time, IOptions<ClinicOptions> options)
{
    private readonly Lazy<TimeZoneInfo> _zone = new(() => TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone));

    public DateTime Now => ToLocal(time.GetUtcNow());

    /// <summary>A stored moment as wall-clock time in the clinic's zone.</summary>
    public DateTime ToLocal(DateTimeOffset moment) => TimeZoneInfo.ConvertTime(moment, _zone.Value).DateTime;

    public DateOnly Today => DateOnly.FromDateTime(Now);

    public DateTimeOffset UtcNow => time.GetUtcNow();
}
