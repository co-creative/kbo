namespace Net.Code.Kbo;

/// <summary>
/// KBO code descriptions (code.csv), e.g. ("JuridicalForm", "014", "NL") → "Naamloze vennootschap".
/// Lookups fall back from the requested language to NL, FR, then any language.
/// </summary>
public sealed class CodeBook
{
    private readonly Dictionary<(string Category, string Code), Dictionary<string, string>> descriptions = new();

    public int Count => descriptions.Count;

    public void Add(string category, string code, string language, string description)
    {
        if (!descriptions.TryGetValue((category, code), out var byLanguage))
            descriptions[(category, code)] = byLanguage = new(StringComparer.OrdinalIgnoreCase);
        byLanguage[language] = description;
    }

    public string? Describe(string category, string? code, string language = "NL")
    {
        if (code is null || !descriptions.TryGetValue((category, code), out var byLanguage)) return null;
        return byLanguage.GetValueOrDefault(language)
            ?? byLanguage.GetValueOrDefault("NL")
            ?? byLanguage.GetValueOrDefault("FR")
            ?? byLanguage.Values.FirstOrDefault();
    }

    /// <summary>Describes an <see cref="ActivityEntry.Nace"/> value such as "2025/35110".</summary>
    public string? DescribeNace(string nace, string language = "NL")
    {
        var slash = nace.IndexOf('/');
        return slash < 0 ? null : Describe("Nace" + nace[..slash], nace[(slash + 1)..], language);
    }

    /// <summary>All descriptions in the given languages (used to build the search index).</summary>
    public IEnumerable<string> AllDescriptions(string category, string code, params string[] languages) =>
        descriptions.TryGetValue((category, code), out var byLanguage)
            ? languages.Select(byLanguage.GetValueOrDefault).OfType<string>()
            : [];
}
