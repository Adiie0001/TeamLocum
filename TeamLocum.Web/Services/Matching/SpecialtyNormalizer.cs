using System.Text.RegularExpressions;

namespace TeamLocum.Web.Services.Matching;

/// <summary>
/// Maps the many ways clinicians write a specialty ("GP", "A&amp;E", "Paeds") to one canonical name, so matching does not
/// depend on spelling. This is the deterministic counterpart of what a language model would be asked to do.
/// </summary>
public static partial class SpecialtyNormalizer
{
    private static readonly Dictionary<string, string> Canonical = new(StringComparer.Ordinal)
    {
        ["gp"] = "general practice",
        ["general practitioner"] = "general practice",
        ["family medicine"] = "general practice",
        ["general practice"] = "general practice",
        ["a&e"] = "emergency medicine",
        ["a and e"] = "emergency medicine",
        ["ae"] = "emergency medicine",
        ["ed"] = "emergency medicine",
        ["emergency"] = "emergency medicine",
        ["accident and emergency"] = "emergency medicine",
        ["emergency medicine"] = "emergency medicine",
        ["paeds"] = "paediatrics",
        ["pediatrics"] = "paediatrics",
        ["paediatric"] = "paediatrics",
        ["paediatrics"] = "paediatrics",
        ["anaesthetics"] = "anaesthetics",
        ["anaesthesia"] = "anaesthetics",
        ["anesthesia"] = "anaesthetics",
        ["anesthetics"] = "anaesthetics",
        ["icu"] = "intensive care",
        ["itu"] = "intensive care",
        ["critical care"] = "intensive care",
        ["intensive care"] = "intensive care",
        ["o&g"] = "obstetrics and gynaecology",
        ["obs and gynae"] = "obstetrics and gynaecology",
        ["obgyn"] = "obstetrics and gynaecology",
        ["obstetrics and gynaecology"] = "obstetrics and gynaecology",
        ["psych"] = "psychiatry",
        ["psychiatry"] = "psychiatry",
        ["urgent care"] = "urgent care",
        ["walk-in"] = "urgent care",
        ["minor injuries"] = "urgent care"
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string Normalize(string? specialty)
    {
        var cleaned = Whitespace().Replace((specialty ?? string.Empty).Trim().ToLowerInvariant(), " ");
        return Canonical.GetValueOrDefault(cleaned, cleaned);
    }

    /// <summary>The words of a specialty, used to give partial credit ("general medicine" vs "general practice").</summary>
    public static IReadOnlySet<string> Words(string normalized) =>
        normalized.Split([' ', '-', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 3 && word is not "and")
            .ToHashSet(StringComparer.Ordinal);
}
