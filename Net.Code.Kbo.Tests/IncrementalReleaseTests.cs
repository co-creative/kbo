using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Net.Code.Kbo.Build;

namespace Net.Code.Kbo.Tests;

public class IncrementalReleaseTests
{
    // ExtractModel records these counters only while reading unit rows.
    private const string SupportEnterprise = "0000.000.999";
    private const string SupportEstablishment = "2000.000.999";
    private const string SupportBranch = "9000.000.999";

    private static readonly string[] TableNames =
    [
        "enterprise", "establishment", "branch", "denomination", "address", "contact", "activity"
    ];

    private static readonly IReadOnlyDictionary<string, string[]> Headers =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["enterprise"] = ["EnterpriseNumber", "Status", "JuridicalSituation", "TypeOfEnterprise", "JuridicalForm", "JuridicalFormCAC", "StartDate"],
            ["establishment"] = ["EstablishmentNumber", "StartDate", "EnterpriseNumber"],
            ["branch"] = ["Id", "StartDate", "EnterpriseNumber"],
            ["denomination"] = ["EntityNumber", "Language", "TypeOfDenomination", "Denomination"],
            ["address"] = ["EntityNumber", "TypeOfAddress", "CountryNL", "CountryFR", "Zipcode", "MunicipalityNL", "MunicipalityFR", "StreetNL", "StreetFR", "HouseNumber", "Box", "ExtraAddressInfo", "DateStrikingOff"],
            ["contact"] = ["EntityNumber", "EntityContact", "ContactType", "Value"],
            ["activity"] = ["EntityNumber", "ActivityGroup", "NaceVersion", "NaceCode", "Classification"],
        };

    [Test]
    public async Task Build_RenamesEnterpriseAndRemovesOldFtsTerms()
    {
        using var temp = new TestDirectory();
        var previousSource = CreateSource(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC")]),
            ("denomination", [Denomination("0000.000.101", "OldQuartz")]),
            ("address", [Address("0000.000.101", "REGO", "Oldborough", "Oldstreet")])));
        var nextSource = CreateSource(temp, 101, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC")]),
            ("denomination", [Denomination("0000.000.101", "NewQuartz")]),
            ("address", [Address("0000.000.101", "REGO", "Newborough", "Newstreet")])));
        var current = BuildDatabase(temp, previousSource, "current.db");
        var output = Path.Combine(temp.Path, "next.db");

        Build(previousSource, nextSource, current, output, Changes([101]));

        var store = CompanyStore.Open(output, commonTermFraction: 1.0);
        var document = store.Get(KboNr.Parse("0000.000.101"));
        await Assert.That(document!.Names!.Single().Value).IsEqualTo("NewQuartz");
        await Assert.That(SearchNumbers(store, "oldquartz")).IsEmpty();
        await Assert.That(SearchNumbers(store, "newquartz")).IsEquivalentTo(new[] { "0000.000.101" });
        await Assert.That(SearchNumbers(store, "oldstreet")).IsEmpty();
        await Assert.That(SearchNumbers(store, "newstreet")).IsEquivalentTo(new[] { "0000.000.101" });
        await Assert.That(store.Release.ExtractNumber).IsEqualTo(101);
        await Assert.That(store.Release.SnapshotDate).IsEqualTo("2026-10-04");
    }

    [Test]
    public async Task Build_RemovesDeletedEnterpriseAndItsFtsTerms()
    {
        using var temp = new TestDirectory();
        var previousSource = CreateSource(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC"), Enterprise("0000.000.102", "AC")]),
            ("denomination",
            [
                Denomination("0000.000.101", "GoneGamma"),
                Denomination("0000.000.102", "StayBeta")
            ])));
        var nextSource = CreateSource(temp, 101, Sources(
            ("enterprise", [Enterprise("0000.000.102", "AC")]),
            ("denomination", [Denomination("0000.000.102", "StayBeta")])));
        var current = BuildDatabase(temp, previousSource, "current.db");
        var output = Path.Combine(temp.Path, "next.db");

        Build(previousSource, nextSource, current, output, Changes([101]));

        var store = CompanyStore.Open(output, commonTermFraction: 1.0);
        await Assert.That(store.Get(KboNr.Parse("0000.000.101"))).IsNull();
        await Assert.That(SearchNumbers(store, "gonegamma")).IsEmpty();
        await Assert.That(SearchNumbers(store, "staybeta")).IsEquivalentTo(new[] { "0000.000.102" });
        await Assert.That(store.CompanyCount).IsEqualTo(2);
    }

    [Test]
    public async Task Build_AddsNewEnterpriseFromEntityChanges()
    {
        using var temp = new TestDirectory();
        var previousSource = CreateSource(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC")]),
            ("denomination", [Denomination("0000.000.101", "StayAlpha")])));
        var nextSource = CreateSource(temp, 101, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC"), Enterprise("0000.000.103", "AC")]),
            ("denomination",
            [
                Denomination("0000.000.101", "StayAlpha"),
                Denomination("0000.000.103", "NewDelta")
            ])));
        var current = BuildDatabase(temp, previousSource, "current.db");
        var output = Path.Combine(temp.Path, "next.db");

        // An inserted enterprise is an entity change. There is no previous owner row to add to EnterpriseOwners.
        Build(previousSource, nextSource, current, output, Changes([103]));

        var store = CompanyStore.Open(output, commonTermFraction: 1.0);
        var added = store.Get(KboNr.Parse("0000.000.103"));
        await Assert.That(added!.Names!.Single().Value).IsEqualTo("NewDelta");
        await Assert.That(SearchNumbers(store, "newdelta")).IsEquivalentTo(new[] { "0000.000.103" });
        await Assert.That(store.CompanyCount).IsEqualTo(3);
    }

    [Test]
    public async Task Build_TransfersEstablishmentAndRefreshesBothOwners()
    {
        using var temp = new TestDirectory();
        const string establishment = "2000.000.101";
        var previousSource = CreateSource(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC"), Enterprise("0000.000.102", "AC")]),
            ("establishment", [Establishment(establishment, "0000.000.101")]),
            ("denomination",
            [
                Denomination("0000.000.101", "OwnerAlpha"),
                Denomination("0000.000.102", "OwnerBeta"),
                Denomination(establishment, "OldUnitName")
            ]),
            ("address", [Address(establishment, "BAET", "Oldunitcity", "Oldunitstreet")])));
        var nextSource = CreateSource(temp, 101, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC"), Enterprise("0000.000.102", "AC")]),
            ("establishment", [Establishment(establishment, "0000.000.102")]),
            ("denomination",
            [
                Denomination("0000.000.101", "OwnerAlpha"),
                Denomination("0000.000.102", "OwnerBeta"),
                Denomination(establishment, "NewUnitName")
            ]),
            ("address", [Address(establishment, "BAET", "Newunitcity", "Newunitstreet")])));
        var current = BuildDatabase(temp, previousSource, "current.db");
        var output = Path.Combine(temp.Path, "next.db");
        var changes = Changes([2_000_000_101], [101, 102]);

        Build(previousSource, nextSource, current, output, changes);

        var store = CompanyStore.Open(output, commonTermFraction: 1.0);
        var oldOwner = store.Get(KboNr.Parse("0000.000.101"));
        var newOwner = store.Get(KboNr.Parse("0000.000.102"));
        await Assert.That(oldOwner!.Establishments).IsNull();
        await Assert.That(newOwner!.Establishments!.Single().Number).IsEqualTo("2.000.000.101");
        await Assert.That(store.Search(new SearchQuery { Text = "oldunitname" }).Items).IsEmpty();
        await Assert.That(SearchNumbers(store, "newunitname")).IsEquivalentTo(new[] { "0000.000.102" });
        await Assert.That(SearchNumbers(store, "oldunitstreet")).IsEmpty();
        await Assert.That(SearchNumbers(store, "newunitstreet")).IsEquivalentTo(new[] { "0000.000.102" });
        await Assert.That(store.Search(new SearchQuery { Name = "owneralpha" }).Items.Single().Establishments).IsEqualTo(0);
        await Assert.That(store.Search(new SearchQuery { Name = "ownerbeta" }).Items.Single().Establishments).IsEqualTo(1);
    }

    [Test]
    public async Task Build_ReplacesOldNaceSearchWithNewNaceSearch()
    {
        using var temp = new TestDirectory();
        var previousSource = CreateSource(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC")]),
            ("denomination", [Denomination("0000.000.101", "NaceCompany")]),
            ("activity", [Activity("0000.000.101", "11111")])));
        var nextSource = CreateSource(temp, 101, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC")]),
            ("denomination", [Denomination("0000.000.101", "NaceCompany")]),
            ("activity", [Activity("0000.000.101", "22222")])));
        var current = BuildDatabase(temp, previousSource, "current.db");
        var output = Path.Combine(temp.Path, "next.db");

        Build(previousSource, nextSource, current, output, Changes([101]));

        var store = CompanyStore.Open(output, commonTermFraction: 1.0);
        await Assert.That(SearchNumbers(store, "turbine")).IsEmpty();
        await Assert.That(SearchNumbers(store, "solar")).IsEquivalentTo(new[] { "0000.000.101" });
        await Assert.That(store.Get(KboNr.Parse("0000.000.101"))!.Activities!.Single().Nace).IsEqualTo("2025/22222");
    }

    [Test]
    public async Task Build_LeavesCurrentDatabaseUnchanged()
    {
        using var temp = new TestDirectory();
        var previousSource = CreateSource(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC")]),
            ("denomination", [Denomination("0000.000.101", "BeforeName")])));
        var nextSource = CreateSource(temp, 101, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC")]),
            ("denomination", [Denomination("0000.000.101", "AfterName")])));
        var current = BuildDatabase(temp, previousSource, "current.db");
        var currentHash = HashFile(current);
        var output = Path.Combine(temp.Path, "next.db");

        Build(previousSource, nextSource, current, output, Changes([101]));

        await Assert.That(HashFile(current)).IsEqualTo(currentHash);
        var oldStore = CompanyStore.Open(current, commonTermFraction: 1.0);
        var newStore = CompanyStore.Open(output, commonTermFraction: 1.0);
        await Assert.That(SearchNumbers(oldStore, "beforename")).IsEquivalentTo(new[] { "0000.000.101" });
        await Assert.That(SearchNumbers(oldStore, "aftername")).IsEmpty();
        await Assert.That(SearchNumbers(newStore, "beforename")).IsEmpty();
        await Assert.That(SearchNumbers(newStore, "aftername")).IsEquivalentTo(new[] { "0000.000.101" });
    }

    [Test]
    public async Task Build_CodebookChangeRejectsWithoutOutput()
    {
        using var temp = new TestDirectory();
        var previousSource = CreateSource(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC")])));
        var nextSource = CreateSource(temp, 101, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC")])));
        var current = BuildDatabase(temp, previousSource, "current.db");
        var currentHash = HashFile(current);
        var output = Path.Combine(temp.Path, "next.db");

        var error = Capture(() =>
            Build(previousSource, nextSource, current, output, Changes([101], codesChanged: true)));

        await Assert.That(error is InvalidDataException).IsTrue();
        await Assert.That(File.Exists(output)).IsFalse();
        await Assert.That(HashFile(current)).IsEqualTo(currentHash);
    }

    [Test]
    public async Task Build_MismatchedBaselineRejectsWithoutOutput()
    {
        using var temp = new TestDirectory();
        var previousSource = CreateSource(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC")])));
        var nextSource = CreateSource(temp, 101, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC")])));
        var currentSource = CreateSource(temp, 99, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC")])));
        var current = BuildDatabase(temp, currentSource, "current.db");
        var currentHash = HashFile(current);
        var output = Path.Combine(temp.Path, "next.db");

        var error = Capture(() => Build(previousSource, nextSource, current, output, Changes([101])));

        await Assert.That(error is InvalidDataException).IsTrue();
        await Assert.That(File.Exists(output)).IsFalse();
        await Assert.That(HashFile(current)).IsEqualTo(currentHash);
    }

    private static void Build(
        string previousSource,
        string nextSource,
        string currentDatabase,
        string outputDatabase,
        IncrementalChanges changes) =>
        IncrementalRelease.Build(
            previousSource, nextSource, currentDatabase, outputDatabase, changes, _ => { }, CancellationToken.None);

    private static string BuildDatabase(TestDirectory temp, string source, string fileName)
    {
        var path = Path.Combine(temp.Path, fileName);
        ReleaseBuilder.Build(source, path, _ => { });
        return path;
    }

    private static string CreateSource(
        TestDirectory temp,
        int extractNumber,
        IReadOnlyDictionary<string, string[][]>? source = null)
    {
        var path = Path.Combine(temp.Path, $"source-{extractNumber}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(zip, "meta.csv", Csv(
            ["Variable", "Value"],
            [
                ["ExtractNumber", extractNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                ["SnapshotDate", "04-10-2026"],
                ["ExtractTimestamp", "04-10-2026 09:04:45"],
                ["ExtractType", "full"],
                ["Version", "1.0.0"]
            ]));
        AddEntry(zip, "code.csv", Csv(
            ["Category", "Code", "Language", "Description"],
            [
                ["Language", "2", "NL", "Nederlands"],
                ["Nace2025", "11111", "NL", "Old turbine machinery"],
                ["Nace2025", "22222", "NL", "New solar equipment"],
                ["Nace2025", "33333", "NL", "Support activity"]
            ]));
        foreach (var table in TableNames)
        {
            var rows = source is not null && source.TryGetValue(table, out var supplied)
                ? supplied
                : Array.Empty<string[]>();
            rows = table switch
            {
                "enterprise" => rows.Append(Enterprise(SupportEnterprise, "AC"))
                    .OrderBy(row => row[0], StringComparer.Ordinal).ToArray(),
                "establishment" => rows.Append(Establishment(SupportEstablishment, SupportEnterprise))
                    .OrderBy(row => row[0], StringComparer.Ordinal).ToArray(),
                "branch" => rows.Append([SupportBranch, "01-01-2015", SupportEnterprise])
                    .OrderBy(row => row[0], StringComparer.Ordinal).ToArray(),
                "denomination" => rows.Append(Denomination(SupportEnterprise, "SupportCompany"))
                    .OrderBy(row => row[0], StringComparer.Ordinal).ToArray(),
                "address" => rows.Append(Address(SupportEnterprise, "REGO", "Supportcity", "Supportstreet"))
                    .OrderBy(row => row[0], StringComparer.Ordinal).ToArray(),
                "contact" => rows.Append([SupportEnterprise, "ENT", "EMAIL", "support@example.invalid"])
                    .OrderBy(row => row[0], StringComparer.Ordinal).ToArray(),
                "activity" => rows.Append(Activity(SupportEnterprise, "33333"))
                    .OrderBy(row => row[0], StringComparer.Ordinal).ToArray(),
                _ => rows
            };
            AddEntry(zip, table + ".csv", Csv(Headers[table], rows));
        }
        return path;
    }

    private static Dictionary<string, string[][]> Sources(params (string Table, string[][] Rows)[] rows)
    {
        var result = TableNames.ToDictionary(table => table, _ => Array.Empty<string[]>(), StringComparer.Ordinal);
        foreach (var (table, values) in rows) result[table] = values;
        return result;
    }

    private static IncrementalChanges Changes(
        long[] entities,
        long[]? enterpriseOwners = null,
        bool codesChanged = false) =>
        new(entities.ToHashSet(), (enterpriseOwners ?? []).ToHashSet(), codesChanged);

    private static string[] SearchNumbers(CompanyStore store, string text) =>
        store.Search(new SearchQuery { Text = text }).Items.Select(item => item.Number).ToArray();

    private static string[] Enterprise(string number, string status) =>
        [number, status, "000", "2", "014", "", "01-01-2001"];

    private static string[] Denomination(string entity, string value) =>
        [entity, "2", "001", value];

    private static string[] Establishment(string number, string enterprise) =>
        [number, "01-01-2005", enterprise];

    private static string[] Address(string entity, string type, string city, string street) =>
        [entity, type, "", "", "1234", city, city, street, street, "1", "", "", ""];

    private static string[] Activity(string entity, string code) =>
        [entity, "001", "2025", code, "MAIN"];

    private static string Csv(string[] headers, IEnumerable<string[]> rows) =>
        string.Join('\n', new[] { headers }.Concat(rows).Select(CsvRow)) + "\n";

    private static string CsvRow(IEnumerable<string> values) =>
        string.Join(',', values.Select(value =>
            "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static Exception? Capture(Action action)
    {
        try { action(); }
        catch (Exception error) { return error; }
        return null;
    }

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "kbo-incremental-release-tests-" + Guid.NewGuid().ToString("N"));

        public TestDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
