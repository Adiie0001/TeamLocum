namespace TeamLocum.Tests.TestSupport;

/// <summary>
/// System time plus an adjustable offset. Tests jump forward (a shift ends, a token expires) without freezing the clock,
/// so timers and delays created through the same provider by libraries such as Polly keep working in real time.
/// </summary>
public sealed class OffsetTimeProvider : TimeProvider
{
    private long _offsetTicks;

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow().AddTicks(Interlocked.Read(ref _offsetTicks));

    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);
}
