using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Net.Code.Kbo;

/// <summary>
/// A company search: free text and/or field filters, translated to an FTS5 MATCH expression.
/// Every term is emitted as a quoted string (so user input can't inject FTS syntax); words get a
/// prefix wildcard, numbers don't. Terms are combined with AND.
/// </summary>
public sealed partial record SearchQuery
{
    public const int MaxTerms = 12;

    public string? Text { get; init; }
    public string? Name { get; init; }
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? PostalCode { get; init; }

    public IReadOnlyList<SearchTerm> GetTerms()
    {
        var terms = new List<SearchTerm>();
        var seen = new HashSet<SearchTerm>();
        void AddTerms(string? input, string? column)
        {
            if (string.IsNullOrWhiteSpace(input)) return;
            foreach (Match m in Token().Matches(input))
            {
                var token = m.Value;
                SearchTerm term;
                if (char.IsAsciiDigit(token[0]))
                {
                    if (token.Length != 4) continue; // only 4-digit numbers (postal codes) are useful
                    term = new SearchTerm(column, token, Prefix: false);
                }
                else
                {
                    if (token.Length < 2) continue;
                    term = new SearchTerm(column, token.ToLowerInvariant(), Prefix: true);
                }
                if (terms.Count < MaxTerms && seen.Add(term)) terms.Add(term);
            }
        }
        AddTerms(Text, null);
        AddTerms(Name, "name");
        AddTerms(Street, "street");
        AddTerms(City, "city");
        AddTerms(PostalCode, "postal_code");
        return terms;
    }

    public static string ToMatch(IEnumerable<SearchTerm> terms) => string.Join(' ', terms.Select(t => t.ToFts()));

    [GeneratedRegex(@"\p{L}+|\d+")]
    private static partial Regex Token();
}

/// <param name="Column">FTS column, or null for all columns (free text).</param>
public readonly record struct SearchTerm(string? Column, string Token, bool Prefix)
{
    public string ToFts() => $"{(Column is null ? "" : Column + ":")}\"{Token}\"{(Prefix ? "*" : "")}";

    /// <summary>The token as stored in the index (lower case, diacritics removed), for fts5vocab lookups.</summary>
    public string IndexForm()
    {
        var decomposed = Token.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().ToLowerInvariant();
    }
}
