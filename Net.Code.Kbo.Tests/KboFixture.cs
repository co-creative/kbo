using System.IO.Compression;
using System.Text;

namespace Net.Code.Kbo.Tests;

/// <summary>
/// A tiny KBO "Full" extract (same files, headers and quoting as the real one) with the edge cases that matter:
/// establishments and branches, quoted names with commas and doubled quotes, a struck-off address,
/// a FAX contact (no code in code.csv), activities in several NACE versions, and an orphan row.
/// </summary>
static class KboFixture
{
    public const string Acme = "0403.170.701";           // NV, Antwerpen; establishment in Gent; NACE 2003/2008/2025
    public const string Bakery = "0200.065.765";         // quoted name; branch in Leuven; NACE 2003/2008 only
    public const string Gamma = "0417.497.106";          // "Gamma BV"
    public const string Delta = "0400.378.485";          // "Delta BV"; only a struck-off address
    public const string AcmeGent = "2.123.456.789";      // establishment of Acme
    public const string BakeryBranch = "9.000.000.001";  // branch of Bakery
    public const string BakeryName = "Boulangerie \"Dupont\", Fils";

    /// <summary>Writes KboOpenData_&lt;extract&gt;_2026_10_04_Full.zip into <paramref name="directory"/>.</summary>
    public static string CreateZip(string directory, int extractNumber = 502)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"KboOpenData_{extractNumber:0000}_2026_10_04_Full.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, lines) in Files(extractNumber))
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            foreach (var line in lines) writer.WriteLine(line);
        }
        return path;
    }

    private static IEnumerable<(string, string[])> Files(int extractNumber)
    {
        yield return ("meta.csv", [
            "\"Variable\",\"Value\"",
            "\"SnapshotDate\",\"03-10-2026\"",
            "\"ExtractTimestamp\",\"04-10-2026 09:04:45\"",
            "\"ExtractType\",\"full\"",
            $"\"ExtractNumber\",\"{extractNumber}\"",
            "\"Version\",\"1.0.0\""]);

        yield return ("code.csv", [
            "\"Category\",\"Code\",\"Language\",\"Description\"",
            .. Code("Language", "1", "Frans", "Français"),
            .. Code("Language", "2", "Nederlands", "Néerlandais"),
            .. Code("JuridicalSituation", "000", "Normale toestand", "Situation normale"),
            .. Code("TypeOfEnterprise", "2", "Rechtspersoon", "Personne morale"),
            .. Code("JuridicalForm", "014", "Naamloze vennootschap", "Société anonyme"),
            .. Code("JuridicalForm", "610", "Besloten vennootschap", "Société à responsabilité limitée"),
            .. Code("TypeOfAddress", "REGO", "Adres van de zetel", "Adresse du siège"),
            .. Code("TypeOfAddress", "BAET", "Adres van de vestigingseenheid", "Adresse de l'unité d'établissement"),
            .. Code("TypeOfDenomination", "001", "Naam", "Dénomination"),
            .. Code("TypeOfDenomination", "003", "Commerciële naam", "Dénomination commerciale"),
            .. Code("ContactType", "EMAIL", "E-mail", "E-mail"),
            .. Code("EntityContact", "ENT", "Entiteit", "Entité"),
            .. Code("ActivityGroup", "001", "BTW-activiteiten", "Activités TVA"),
            .. Code("Classification", "MAIN", "Hoofdactiviteit", "Activité principale"),
            .. Code("Nace2003", "40110", "Productie van elektriciteit", "Production d'électricité"),
            .. Code("Nace2008", "35110", "Productie van elektriciteit", "Production d'électricité"),
            .. Code("Nace2025", "35110", "Productie van elektriciteit uit niet-hernieuwbare bronnen", "Production d'électricité à partir de sources non renouvelables"),
            .. Code("Nace2003", "15811", "Vervaardiging van brood", "Fabrication de pain"),
            .. Code("Nace2008", "10711", "Ambachtelijke broodbakkerijen", "Boulangerie artisanale")]);

        yield return ("enterprise.csv", [
            "\"EnterpriseNumber\",\"Status\",\"JuridicalSituation\",\"TypeOfEnterprise\",\"JuridicalForm\",\"JuridicalFormCAC\",\"StartDate\"",
            $"\"{Bakery}\",\"AC\",\"000\",\"2\",\"014\",,15-06-1995",
            $"\"{Acme}\",\"AC\",\"000\",\"2\",\"014\",,01-01-2001",
            $"\"{Delta}\",\"AC\",\"000\",\"2\",\"610\",,01-03-2010",
            $"\"{Gamma}\",\"AC\",\"000\",\"2\",\"610\",,01-02-2020"]);

        yield return ("establishment.csv", [
            "\"EstablishmentNumber\",\"StartDate\",\"EnterpriseNumber\"",
            $"\"{AcmeGent}\",01-01-2005,\"{Acme}\""]);

        yield return ("branch.csv", [
            "\"Id\",\"StartDate\",\"EnterpriseNumber\"",
            $"\"{BakeryBranch}\",01-09-2015,\"{Bakery}\""]);

        yield return ("denomination.csv", [
            "\"EntityNumber\",\"Language\",\"TypeOfDenomination\",\"Denomination\"",
            $"\"{Acme}\",\"2\",\"001\",\"Acme Software BV\"",
            $"\"{Acme}\",\"2\",\"003\",\"Acme Labs\"",
            $"\"{Bakery}\",\"1\",\"001\",\"Boulangerie \"\"Dupont\"\", Fils\"",
            $"\"{Gamma}\",\"2\",\"001\",\"Gamma BV\"",
            $"\"{Delta}\",\"2\",\"001\",\"Delta BV\"",
            $"\"{AcmeGent}\",\"2\",\"003\",\"Acme Gent\"",
            $"\"{BakeryBranch}\",\"1\",\"001\",\"Dupont Leuven\"",
            "\"0999.999.999\",\"2\",\"001\",\"Orphan without enterprise\""]);

        yield return ("address.csv", [
            "\"EntityNumber\",\"TypeOfAddress\",\"CountryNL\",\"CountryFR\",\"Zipcode\",\"MunicipalityNL\",\"MunicipalityFR\",\"StreetNL\",\"StreetFR\",\"HouseNumber\",\"Box\",\"ExtraAddressInfo\",\"DateStrikingOff\"",
            $"\"{Acme}\",\"REGO\",,,\"2000\",\"Antwerpen\",\"Anvers\",\"Meir\",\"Meir\",\"1\",\"\",\"\",",
            $"\"{Bakery}\",\"REGO\",,,\"1000\",\"Brussel\",\"Bruxelles\",\"Wetstraat\",\"Rue de la Loi\",\"16\",\"\",\"\",",
            $"\"{Gamma}\",\"REGO\",,,\"8500\",\"Kortrijk\",\"Courtrai\",\"Grote Markt\",\"Grand-Place\",\"5\",\"B\",\"\",",
            $"\"{Delta}\",\"REGO\",,,\"8000\",\"Brugge\",\"Bruges\",\"Steenstraat\",\"Steenstraat\",\"3\",\"\",\"\",01-01-2020",
            $"\"{AcmeGent}\",\"BAET\",,,\"9000\",\"Gent\",\"Gand\",\"Veldstraat\",\"Veldstraat\",\"10\",\"\",\"\",",
            $"\"{BakeryBranch}\",\"BAET\",,,\"3000\",\"Leuven\",\"Louvain\",\"Bondgenotenlaan\",\"Bondgenotenlaan\",\"1\",\"\",\"\","]);

        yield return ("contact.csv", [
            "\"EntityNumber\",\"EntityContact\",\"ContactType\",\"Value\"",
            $"\"{Acme}\",\"ENT\",\"EMAIL\",\"info@acme.example\"",
            $"\"{Acme}\",\"ENT\",\"FAX\",\"03 123 45 67\""]);

        yield return ("activity.csv", [
            "\"EntityNumber\",\"ActivityGroup\",\"NaceVersion\",\"NaceCode\",\"Classification\"",
            $"\"{Acme}\",\"001\",\"2003\",\"40110\",\"MAIN\"",
            $"\"{Acme}\",\"001\",\"2008\",\"35110\",\"MAIN\"",
            $"\"{Acme}\",\"001\",\"2025\",\"35110\",\"MAIN\"",
            $"\"{AcmeGent}\",\"001\",\"2025\",\"35110\",\"MAIN\"",
            $"\"{Bakery}\",\"001\",\"2003\",\"15811\",\"MAIN\"",
            $"\"{Bakery}\",\"001\",\"2008\",\"10711\",\"MAIN\""]);
    }

    private static string[] Code(string category, string code, string nl, string fr) =>
        [$"\"{category}\",\"{code}\",\"NL\",\"{nl}\"", $"\"{category}\",\"{code}\",\"FR\",\"{fr}\""];
}

/// <summary>Temporary directory, deleted on dispose.</summary>
sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kbo-tests-" + Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { /* best effort on Windows */ }
        catch (UnauthorizedAccessException) { }
    }
}
