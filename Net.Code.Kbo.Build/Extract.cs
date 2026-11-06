using System.IO.Compression;
using System.Text;
using Net.Code.Csv;

namespace Net.Code.Kbo.Build;

/// <summary>A KBO Open Data "Full" zip; CSV entries are streamed without extracting them.</summary>
sealed class Extract(string zipPath) : IDisposable
{
    private readonly ZipArchive zip = ZipFile.OpenRead(zipPath);

    public IEnumerable<T> Read<T>(string entryName)
    {
        var entry = zip.GetEntry(entryName) ?? throw new InvalidDataException($"{zipPath} has no {entryName}.");
        using var stream = entry.Open();
        foreach (var row in ReadCsv.FromStream<T>(stream, encoding: Encoding.UTF8, hasHeaders: true))
            yield return row;
    }

    public void Dispose() => zip.Dispose();
}

// Row types: property names and order match the CSV headers (KBO cookbook, chapter 2).
// All values are read as text; empty fields become null or "".
#pragma warning disable CS8618
sealed class MetaRow { public string Variable { get; set; } public string Value { get; set; } }
sealed class CodeRow { public string Category { get; set; } public string Code { get; set; } public string Language { get; set; } public string Description { get; set; } }
sealed class EnterpriseRow
{
    public string EnterpriseNumber { get; set; }
    public string? Status { get; set; }
    public string? JuridicalSituation { get; set; }
    public string? TypeOfEnterprise { get; set; }
    public string? JuridicalForm { get; set; }
    public string? JuridicalFormCAC { get; set; }
    public string? StartDate { get; set; }
}
sealed class EstablishmentRow { public string EstablishmentNumber { get; set; } public string? StartDate { get; set; } public string EnterpriseNumber { get; set; } }
sealed class BranchRow { public string Id { get; set; } public string? StartDate { get; set; } public string EnterpriseNumber { get; set; } }
sealed class DenominationRow { public string EntityNumber { get; set; } public string? Language { get; set; } public string TypeOfDenomination { get; set; } public string Denomination { get; set; } }
sealed class AddressRow
{
    public string EntityNumber { get; set; }
    public string TypeOfAddress { get; set; }
    public string? CountryNL { get; set; }
    public string? CountryFR { get; set; }
    public string? Zipcode { get; set; }
    public string? MunicipalityNL { get; set; }
    public string? MunicipalityFR { get; set; }
    public string? StreetNL { get; set; }
    public string? StreetFR { get; set; }
    public string? HouseNumber { get; set; }
    public string? Box { get; set; }
    public string? ExtraAddressInfo { get; set; }
    public string? DateStrikingOff { get; set; }
}
sealed class ContactRow { public string EntityNumber { get; set; } public string EntityContact { get; set; } public string ContactType { get; set; } public string Value { get; set; } }
sealed class ActivityRow { public string EntityNumber { get; set; } public string ActivityGroup { get; set; } public string NaceVersion { get; set; } public string NaceCode { get; set; } public string Classification { get; set; } }
#pragma warning restore CS8618
