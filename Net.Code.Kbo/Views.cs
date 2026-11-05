namespace Net.Code.Kbo;

// Language-resolved projections of the stored documents, shared by the JSON API and the UI.
// Codes become descriptions in the requested language (fallback NL, FR, any); unknown codes are shown as-is.

public static class Lang
{
    /// <summary>code.csv only has Dutch and French descriptions.</summary>
    public static readonly string[] All = ["nl", "fr"];

    /// <summary>One of <see cref="All"/>; anything else becomes "nl".</summary>
    public static string Normalize(string? lang) =>
        lang?.ToLowerInvariant() is { } l && All.Contains(l) ? l : "nl";

    /// <summary>KBO language code used in denominations: 1 FR, 2 NL, 3 DE, 4 EN.</summary>
    public static string KboCode(string lang) => lang switch { "fr" => "1", "de" => "3", "en" => "4", _ => "2" };
}

public sealed record CompanyView(
    string Number,
    string? Name,
    string? Status,
    string? StartDate,
    string? TypeOfEnterprise,
    string? JuridicalSituation,
    string? JuridicalForm,
    string? JuridicalFormCac,
    IReadOnlyList<NameView> Names,
    IReadOnlyList<AddressView> Addresses,
    IReadOnlyList<ContactView> Contacts,
    IReadOnlyList<ActivityView> Activities,
    IReadOnlyList<UnitView> Establishments,
    IReadOnlyList<UnitView> Branches)
{
    public static CompanyView Create(CompanyDocument d, CodeBook codes, string lang)
    {
        lang = Lang.Normalize(lang);
        return new CompanyView(
            d.Number,
            BestName(d.Names, lang),
            codes.Describe("Status", d.Status, lang) ?? d.Status,
            d.StartDate,
            codes.Describe("TypeOfEnterprise", d.TypeOfEnterprise, lang) ?? d.TypeOfEnterprise,
            codes.Describe("JuridicalSituation", d.JuridicalSituation, lang) ?? d.JuridicalSituation,
            codes.Describe("JuridicalForm", d.JuridicalForm, lang) ?? d.JuridicalForm,
            codes.Describe("JuridicalForm", d.JuridicalFormCac, lang) ?? d.JuridicalFormCac,
            NameView.List(d.Names, codes, lang),
            AddressView.List(d.Addresses, codes, lang),
            ContactView.List(d.Contacts, codes, lang),
            ActivityView.List(d.Activities, codes, lang),
            (d.Establishments ?? []).Select(u => UnitView.Create(u, codes, lang)).ToList(),
            (d.Branches ?? []).Select(u => UnitView.Create(u, codes, lang)).ToList());
    }

    /// <summary>Legal name (001) in the requested language, then Dutch, French, any; otherwise the first name.</summary>
    internal static string? BestName(IReadOnlyList<NameEntry>? names, string lang)
    {
        if (names is null || names.Count == 0) return null;
        var legal = names.Where(n => n.Type == "001").ToList();
        return (legal.FirstOrDefault(n => n.Lang == Lang.KboCode(lang))
                ?? legal.FirstOrDefault(n => n.Lang == "2")
                ?? legal.FirstOrDefault(n => n.Lang == "1")
                ?? legal.FirstOrDefault()
                ?? names[0]).Value;
    }
}

public sealed record UnitView(
    string Number,
    string? Name,
    string? StartDate,
    IReadOnlyList<NameView> Names,
    IReadOnlyList<AddressView> Addresses,
    IReadOnlyList<ContactView> Contacts,
    IReadOnlyList<ActivityView> Activities)
{
    public static UnitView Create(UnitDocument u, CodeBook codes, string lang) => new(
        u.Number,
        CompanyView.BestName(u.Names, lang),
        u.StartDate,
        NameView.List(u.Names, codes, lang),
        AddressView.List(u.Addresses, codes, lang),
        ContactView.List(u.Contacts, codes, lang),
        ActivityView.List(u.Activities, codes, lang));
}

public sealed record NameView(string Type, string? Language, string Value)
{
    internal static List<NameView> List(IReadOnlyList<NameEntry>? names, CodeBook codes, string lang) =>
        (names ?? [])
        .OrderBy(n => n.Type)
        .ThenBy(n => n.Lang == Lang.KboCode(lang) ? 0 : 1)
        .Select(n => new NameView(
            codes.Describe("TypeOfDenomination", n.Type, lang) ?? n.Type,
            codes.Describe("Language", n.Lang, lang) ?? n.Lang,
            n.Value))
        .ToList();
}

public sealed record AddressView(
    string Type,
    string? Street,
    string? Number,
    string? Box,
    string? Zip,
    string? City,
    string? Country,
    string? Extra,
    string? StrikingOff)
{
    /// <summary>"Meir 1 box B, 2000 Antwerpen" (country appended for addresses abroad).</summary>
    public string Line =>
        string.Join(", ", new[]
        {
            string.Join(' ', new[] { Street, Number, Box is null ? null : "box " + Box }.Where(s => s is not null)),
            string.Join(' ', new[] { Zip, City }.Where(s => s is not null)),
            Country,
        }.Where(s => !string.IsNullOrEmpty(s)));

    internal static List<AddressView> List(IReadOnlyList<AddressEntry>? addresses, CodeBook codes, string lang) =>
        (addresses ?? [])
        .OrderBy(a => a.StrikingOff is null ? 0 : 1)
        .Select(a => new AddressView(
            codes.Describe("TypeOfAddress", a.Type, lang) ?? a.Type,
            a.Street?.Get(lang),
            a.Number,
            a.Box,
            a.Zip,
            a.City?.Get(lang),
            a.Country?.Get(lang),
            a.Extra,
            a.StrikingOff))
        .ToList();
}

public sealed record ContactView(string Type, string Kind, string Value)
{
    internal static List<ContactView> List(IReadOnlyList<ContactEntry>? contacts, CodeBook codes, string lang) =>
        (contacts ?? []).Select(c => new ContactView(codes.Describe("ContactType", c.Type, lang) ?? c.Type, c.Type, c.Value)).ToList();
}

/// <summary>
/// One activity (NACE code + classification). KBO registers the same activity per activity group
/// (e.g. VAT and RSZ/ONSS); those are merged into <see cref="RegisteredFor"/>.
/// </summary>
public sealed record ActivityView(string Classification, string NaceVersion, string NaceCode, string? Description, bool IsMain, IReadOnlyList<string> RegisteredFor)
{
    internal static List<ActivityView> List(IReadOnlyList<ActivityEntry>? activities, CodeBook codes, string lang) =>
        (activities ?? [])
        .GroupBy(a => (a.Nace, a.Classification))
        .OrderBy(g => g.Key.Classification switch { "MAIN" => 0, "SECO" => 1, _ => 2 })
        .ThenBy(g => g.Min(a => a.Group))
        .Select(g =>
        {
            var (nace, classification) = g.Key;
            var slash = nace.IndexOf('/');
            return new ActivityView(
                codes.Describe("Classification", classification, lang) ?? classification,
                nace[..Math.Max(0, slash)],
                nace[(slash + 1)..],
                codes.DescribeNace(nace, lang),
                classification == "MAIN",
                g.Select(a => a.Group).Distinct().Order().Select(group => codes.Describe("ActivityGroup", group, lang) ?? group).ToList());
        })
        .ToList();
}

public sealed record SummaryView(string Number, string? Name, string? Zip, string? City, string? JuridicalForm, string? Status, int Establishments)
{
    public static SummaryView Create(CompanySummary s, CodeBook codes, string lang)
    {
        lang = Lang.Normalize(lang);
        return new SummaryView(
            s.Number,
            s.Name,
            s.Zip,
            s.City?.Get(lang),
            codes.Describe("JuridicalForm", s.JuridicalForm, lang) ?? s.JuridicalForm,
            codes.Describe("Status", s.Status, lang) ?? s.Status,
            s.Establishments);
    }
}
