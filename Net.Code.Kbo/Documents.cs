using System.Text.Json;
using System.Text.Json.Serialization;

namespace Net.Code.Kbo;

// The language-neutral company document stored in company.doc (specs/azure-architecture.md §5).
// Values are KBO codes; descriptions come from the CodeBook. Null/empty values are omitted from the JSON.

public sealed record CompanyDocument
{
    public required string Number { get; init; }
    public string? Status { get; init; }
    public string? StartDate { get; init; }
    public string? TypeOfEnterprise { get; init; }
    public string? JuridicalSituation { get; init; }
    public string? JuridicalForm { get; init; }
    public string? JuridicalFormCac { get; init; }
    public IReadOnlyList<NameEntry>? Names { get; init; }
    public IReadOnlyList<AddressEntry>? Addresses { get; init; }
    public IReadOnlyList<ContactEntry>? Contacts { get; init; }
    public IReadOnlyList<ActivityEntry>? Activities { get; init; }
    public IReadOnlyList<UnitDocument>? Establishments { get; init; }
    public IReadOnlyList<UnitDocument>? Branches { get; init; }
}

/// <summary>An establishment unit or a branch.</summary>
public sealed record UnitDocument
{
    public required string Number { get; init; }
    public string? StartDate { get; init; }
    public IReadOnlyList<NameEntry>? Names { get; init; }
    public IReadOnlyList<AddressEntry>? Addresses { get; init; }
    public IReadOnlyList<ContactEntry>? Contacts { get; init; }
    public IReadOnlyList<ActivityEntry>? Activities { get; init; }
}

/// <param name="Type">TypeOfDenomination code: 001 name, 002 abbreviation, 003 commercial name, 004 branch name.</param>
/// <param name="Lang">Language code: 0 unknown, 1 FR, 2 NL, 3 DE, 4 EN.</param>
public sealed record NameEntry(string Type, string? Lang, string Value);

public sealed record AddressEntry
{
    /// <summary>TypeOfAddress code, e.g. REGO (registered office), BAET (establishment unit).</summary>
    public required string Type { get; init; }
    public string? Zip { get; init; }
    public LocalizedText? City { get; init; }
    public LocalizedText? Street { get; init; }
    public string? Number { get; init; }
    public string? Box { get; init; }
    public string? Extra { get; init; }
    /// <summary>Only for addresses abroad.</summary>
    public LocalizedText? Country { get; init; }
    /// <summary>Set when the address was struck off (ISO date); such addresses are not searchable.</summary>
    public string? StrikingOff { get; init; }
}

public sealed record LocalizedText(string? Nl, string? Fr)
{
    public static LocalizedText? Create(string? nl, string? fr) => nl is null && fr is null ? null : new(nl, fr);
    public string? Get(string lang) => lang.Equals("FR", StringComparison.OrdinalIgnoreCase) ? Fr ?? Nl : Nl ?? Fr;
}

/// <param name="Type">ContactType code: EMAIL, TEL, WEB.</param>
public sealed record ContactEntry(string Type, string Value);

/// <param name="Group">ActivityGroup code, e.g. 001 (VAT), 006 (RSZ/ONSS).</param>
/// <param name="Nace">"version/code", e.g. "2025/35110"; only the newest NACE version per group and classification is kept.</param>
/// <param name="Classification">MAIN, SECO or ANCI.</param>
public sealed record ActivityEntry(string Group, string Nace, string Classification);

/// <summary>Small projection stored in company.summary, used for result lists.</summary>
public sealed record CompanySummary
{
    public required string Number { get; init; }
    public string? Name { get; init; }
    public string? Zip { get; init; }
    public LocalizedText? City { get; init; }
    public string? JuridicalForm { get; init; }
    public string? Status { get; init; }
    public int Establishments { get; init; }
}

/// <summary>Contents of the release table.</summary>
public sealed record ReleaseInfo(int ExtractNumber, string SnapshotDate, string BuiltAt, IReadOnlyDictionary<string, long> Counts);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CompanyDocument))]
[JsonSerializable(typeof(CompanySummary))]
[JsonSerializable(typeof(Dictionary<string, long>))]
public sealed partial class KboJson : JsonSerializerContext
{
    public static string Serialize(CompanyDocument doc) => JsonSerializer.Serialize(doc, Default.CompanyDocument);
    public static string Serialize(CompanySummary summary) => JsonSerializer.Serialize(summary, Default.CompanySummary);
}
