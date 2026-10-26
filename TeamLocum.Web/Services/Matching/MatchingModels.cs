namespace TeamLocum.Web.Services.Matching;

/// <summary>What a shift needs, with nothing that identifies the hospital, the patients or the staff.</summary>
public sealed record ShiftProfile(string? Specialty, string Location, decimal RatePerHour);

/// <summary>What the matcher knows about a doctor. The display name is only used locally and never sent to an AI provider.</summary>
public sealed record LocumProfile(
    int LocumId,
    string DisplayName,
    IReadOnlyList<string> Specialties,
    string BaseLocation,
    decimal MinRatePerHour,
    int YearsExperience,
    bool Applied);

/// <summary>One scored factor, so a hospital can see exactly why a doctor ranked where they did.</summary>
public sealed record MatchFactor(string Name, int Points, int MaxPoints, string Detail);

public sealed record MatchCandidate(
    int LocumId,
    string DisplayName,
    int Score,
    bool Applied,
    string Summary,
    IReadOnlyList<MatchFactor> Factors);

/// <summary>
/// Ranked doctors. <c>Source</c> is <c>rules</c> when the deterministic matcher answered (AI not configured),
/// <c>gemini</c> when the model ranked anonymised profiles, and <c>rules-fallback</c> when AI was configured but
/// unavailable so the deterministic matcher answered.
/// </summary>
public sealed record MatchResult(string Source, IReadOnlyList<MatchCandidate> Candidates);

public static class MatchSources
{
    public const string Rules = "rules";
    public const string Gemini = "gemini";
    public const string RulesFallback = "rules-fallback";
}

public interface IShiftMatcher
{
    Task<MatchResult> RankAsync(ShiftProfile shift, IReadOnlyList<LocumProfile> candidates, CancellationToken ct);
}

/// <summary>The AI provider could not give a usable answer; callers fall back to the rule-based matcher.</summary>
public sealed class MatchingUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
