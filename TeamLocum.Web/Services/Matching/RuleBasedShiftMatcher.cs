using System.Globalization;
using System.Text.RegularExpressions;

namespace TeamLocum.Web.Services.Matching;

/// <summary>
/// Transparent, deterministic matching. Five factors add up to 100: specialty (40), location (25), rate (15),
/// experience (10) and the doctor's own interest in the shift (10). The same inputs always give the same ranking, and
/// every point is explained, so a hospital can see why a doctor ranked where they did.
/// </summary>
public sealed partial class RuleBasedShiftMatcher : IShiftMatcher
{
    private const int SpecialtyMax = 40;
    private const int LocationMax = 25;
    private const int RateMax = 15;
    private const int ExperienceMax = 10;
    private const int InterestMax = 10;

    /// <summary>Words that appear in many place names and say nothing about where a doctor lives.</summary>
    private static readonly HashSet<string> GenericPlaceWords = new(StringComparer.Ordinal)
    {
        "hospital", "centre", "center", "clinic", "community", "general", "city", "medical", "health", "nhs", "trust",
        "ward", "royal", "university", "infirmary", "department", "north", "south", "east", "west", "the", "and"
    };

    [GeneratedRegex(@"[^\p{L}]+")]
    private static partial Regex NonLetters();

    public Task<MatchResult> RankAsync(ShiftProfile shift, IReadOnlyList<LocumProfile> candidates, CancellationToken ct)
    {
        var ranked = candidates
            .Select(candidate => Score(shift, candidate))
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.Applied)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.LocumId)
            .ToList();

        return Task.FromResult(new MatchResult(MatchSources.Rules, ranked));
    }

    internal static MatchCandidate Score(ShiftProfile shift, LocumProfile locum)
    {
        var factors = new List<MatchFactor>
        {
            ScoreSpecialty(shift, locum),
            ScoreLocation(shift, locum),
            ScoreRate(shift, locum),
            ScoreExperience(locum),
            ScoreInterest(locum)
        };

        var total = factors.Sum(f => f.Points);
        var summary = string.Join("; ", factors.Where(f => f.Points > 0).Select(f => f.Detail));

        return new MatchCandidate(locum.LocumId, locum.DisplayName, total, locum.Applied,
            summary.Length == 0 ? "No matching factors." : summary + ".", factors);
    }

    private static MatchFactor ScoreSpecialty(ShiftProfile shift, LocumProfile locum)
    {
        if (string.IsNullOrWhiteSpace(shift.Specialty))
        {
            return new MatchFactor("Specialty", SpecialtyMax / 2, SpecialtyMax, "no specific specialty required");
        }

        var wanted = SpecialtyNormalizer.Normalize(shift.Specialty);
        var offered = locum.Specialties.Select(SpecialtyNormalizer.Normalize).ToList();

        if (offered.Contains(wanted))
        {
            return new MatchFactor("Specialty", SpecialtyMax, SpecialtyMax, $"works in {shift.Specialty.Trim()}");
        }

        var wantedWords = SpecialtyNormalizer.Words(wanted);
        if (offered.Any(o => SpecialtyNormalizer.Words(o).Overlaps(wantedWords)))
        {
            return new MatchFactor("Specialty", SpecialtyMax * 3 / 8, SpecialtyMax, $"related to {shift.Specialty.Trim()}");
        }

        return new MatchFactor("Specialty", 0, SpecialtyMax, $"does not list {shift.Specialty.Trim()}");
    }

    private static MatchFactor ScoreLocation(ShiftProfile shift, LocumProfile locum)
    {
        if (string.IsNullOrWhiteSpace(locum.BaseLocation))
        {
            return new MatchFactor("Location", LocationMax * 2 / 5, LocationMax, "no base location given");
        }

        var shiftWords = PlaceWords(shift.Location);
        var baseWords = PlaceWords(locum.BaseLocation);

        return shiftWords.Overlaps(baseWords)
            ? new MatchFactor("Location", LocationMax, LocationMax, $"based in {locum.BaseLocation.Trim()}")
            : new MatchFactor("Location", 0, LocationMax, $"based in {locum.BaseLocation.Trim()}, away from the shift");
    }

    private static MatchFactor ScoreRate(ShiftProfile shift, LocumProfile locum)
    {
        if (locum.MinRatePerHour <= 0 || shift.RatePerHour >= locum.MinRatePerHour)
        {
            return new MatchFactor("Rate", RateMax, RateMax,
                locum.MinRatePerHour <= 0 ? "no minimum rate" : $"rate meets the {Money(locum.MinRatePerHour)}/h minimum");
        }

        // Within ten per cent of the minimum is a negotiable gap; further away is not.
        return shift.RatePerHour >= locum.MinRatePerHour * 0.9m
            ? new MatchFactor("Rate", RateMax / 2, RateMax, $"slightly below the {Money(locum.MinRatePerHour)}/h minimum")
            : new MatchFactor("Rate", 0, RateMax, $"below the {Money(locum.MinRatePerHour)}/h minimum");
    }

    private static MatchFactor ScoreExperience(LocumProfile locum)
    {
        var points = Math.Clamp(locum.YearsExperience, 0, ExperienceMax);
        return new MatchFactor("Experience", points, ExperienceMax, $"{locum.YearsExperience} years' experience");
    }

    private static MatchFactor ScoreInterest(LocumProfile locum) =>
        locum.Applied
            ? new MatchFactor("Interest", InterestMax, InterestMax, "applied for this shift")
            : new MatchFactor("Interest", 0, InterestMax, "has not applied");

    private static HashSet<string> PlaceWords(string text) =>
        NonLetters().Split(text.ToLowerInvariant())
            .Where(word => word.Length >= 3 && !GenericPlaceWords.Contains(word))
            .ToHashSet(StringComparer.Ordinal);

    private static string Money(decimal amount) => "£" + amount.ToString("0.##", CultureInfo.InvariantCulture);
}
