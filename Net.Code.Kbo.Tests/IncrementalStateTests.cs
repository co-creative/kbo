using System.IO.Compression;
using System.Text;
using Net.Code.Kbo.Build;

namespace Net.Code.Kbo.Tests;

public class IncrementalStateTests
{
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
    public async Task Apply_AddsAndRemovesRowsAndUsesLatestMetadataAndCode()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC"), Enterprise("0000.000.102", "AC")])));
        var update = CreateUpdate(
            temp,
            101,
            Deltas(("enterprise", Delta(
                inserts: [Enterprise("0000.000.103", "AC")],
                deletes: [DeleteKey("0000.000.101")]))),
            codeDescription: "Updated language description");
        var destination = Path.Combine(temp.Path, "result.zip");

        var changes = IncrementalState.Apply(baseline, [update], destination, CancellationToken.None);

        var rows = ReadDataRows(destination, "enterprise.csv");
        await Assert.That(rows).IsEquivalentTo(new[]
        {
            CsvRow(Enterprise("0000.000.102", "AC")),
            CsvRow(Enterprise("0000.000.103", "AC"))
        });
        await Assert.That(changes.Entities).IsEquivalentTo(new long[] { 101, 103 });
        await Assert.That(changes.EnterpriseOwners.Count).IsEqualTo(0);
        await Assert.That(changes.CodesChanged).IsTrue();
        await Assert.That(ReadEntry(destination, "meta.csv")).Contains(CsvRow(["ExtractNumber", "101"]));
        await Assert.That(ReadEntry(destination, "meta.csv")).Contains(CsvRow(["ExtractType", "update"]));
        await Assert.That(ReadEntry(destination, "code.csv")).Contains(CsvRow(["Language", "2", "NL", "Updated language description"]));
        await Assert.That(EntryNames(destination)).IsEquivalentTo(
            new[] { "meta.csv", "code.csv" }.Concat(TableNames.Select(table => table + ".csv")));
    }

    [Test]
    public async Task Apply_RepeatedKeyAcrossTwoUpdates_UsesLastUpdateAndPreservesMultiRows()
    {
        using var temp = new TestDirectory();
        const string entity = "0000.000.101";
        const string otherEntity = "0000.000.102";
        var baseline = CreateFull(temp, 100, Sources(
            ("denomination",
            [
                Denomination(entity, "2", "001", "Old Dutch"),
                Denomination(entity, "1", "001", "Old French"),
                Denomination(otherEntity, "2", "001", "Other old name")
            ])));
        var firstUpdate = CreateUpdate(temp, 101, Deltas(
            ("denomination", Delta(
                inserts:
                [
                    Denomination(entity, "2", "001", "Intermediate Dutch"),
                    Denomination(entity, "1", "001", "Intermediate French"),
                    Denomination(otherEntity, "2", "001", "Other intermediate name")
                ],
                deletes: [DeleteKey(entity), DeleteKey(otherEntity)]))),
            codeDescription: "Intermediate language description");
        var secondUpdate = CreateUpdate(temp, 102, Deltas(
            ("denomination", Delta(
                inserts:
                [
                    Denomination(entity, "2", "001", "Final Dutch"),
                    Denomination(entity, "1", "001", "Final French")
                ],
                deletes: [DeleteKey(entity)]))),
            codeDescription: "Final language description");
        var destination = Path.Combine(temp.Path, "result.zip");

        var changes = IncrementalState.Apply(
            baseline, [firstUpdate, secondUpdate], destination, CancellationToken.None);

        await Assert.That(ReadDataRows(destination, "denomination.csv")).IsEquivalentTo(new[]
        {
            CsvRow(Denomination(entity, "2", "001", "Final Dutch")),
            CsvRow(Denomination(entity, "1", "001", "Final French")),
            CsvRow(Denomination(otherEntity, "2", "001", "Other intermediate name"))
        });
        await Assert.That(changes.Entities).IsEquivalentTo(new long[] { 101, 102 });
        await Assert.That(changes.CodesChanged).IsTrue();
        await Assert.That(ReadEntry(destination, "meta.csv")).Contains(CsvRow(["ExtractNumber", "102"]));
        await Assert.That(ReadEntry(destination, "code.csv"))
            .Contains(CsvRow(["Language", "2", "NL", "Final language description"]));
    }

    [Test]
    public async Task Apply_EmptyUpdatePreservesCompleteSourceAndReportsNoChanges()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC")]),
            ("denomination", [Denomination("0000.000.101", "2", "001", "Unchanged")])));
        var update = CreateUpdate(temp, 101);
        var destination = Path.Combine(temp.Path, "result.zip");

        var changes = IncrementalState.Apply(baseline, [update], destination, CancellationToken.None);

        await Assert.That(ReadDataRows(destination, "enterprise.csv"))
            .IsEquivalentTo(new[] { CsvRow(Enterprise("0000.000.101", "AC")) });
        await Assert.That(ReadDataRows(destination, "denomination.csv"))
            .IsEquivalentTo(new[] { CsvRow(Denomination("0000.000.101", "2", "001", "Unchanged")) });
        await Assert.That(changes.Entities.Count).IsEqualTo(0);
        await Assert.That(changes.EnterpriseOwners.Count).IsEqualTo(0);
        await Assert.That(changes.CodesChanged).IsFalse();
        await Assert.That(ReadEntry(destination, "meta.csv")).Contains(CsvRow(["ExtractNumber", "101"]));
    }

    [Test]
    public async Task Apply_EstablishmentOwnershipMoveReportsOldAndNewOwners()
    {
        using var temp = new TestDirectory();
        const string establishment = "2.000.000.101";
        var baseline = CreateFull(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC"), Enterprise("0000.000.102", "AC")]),
            ("establishment", [Establishment(establishment, "0000.000.101")])));
        var update = CreateUpdate(temp, 101, Deltas(
            ("establishment", Delta(
                inserts: [Establishment(establishment, "0000.000.102")],
                deletes: [DeleteKey(establishment)]))));
        var destination = Path.Combine(temp.Path, "result.zip");

        var changes = IncrementalState.Apply(baseline, [update], destination, CancellationToken.None);

        await Assert.That(ReadDataRows(destination, "establishment.csv"))
            .IsEquivalentTo(new[] { CsvRow(Establishment(establishment, "0000.000.102")) });
        await Assert.That(changes.Entities).IsEquivalentTo(new long[] { 2_000_000_101 });
        await Assert.That(changes.EnterpriseOwners).IsEquivalentTo(new long[] { 101, 102 });
    }

    [Test]
    public async Task Apply_GapBetweenUpdatesRejectsWithoutCreatingOutput()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100);
        var update = CreateUpdate(temp, 102);
        var destination = Path.Combine(temp.Path, "result.zip");

        await AssertRejectedWithoutOutput(
            () => IncrementalState.Apply(baseline, [update], destination, CancellationToken.None),
            destination);
    }

    [Test]
    public async Task Apply_FormatVersionMismatchRejectsWithoutCreatingOutput()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100, version: "1.0.0");
        var update = CreateUpdate(temp, 101, version: "2.0.0");
        var destination = Path.Combine(temp.Path, "result.zip");

        await AssertRejectedWithoutOutput(
            () => IncrementalState.Apply(baseline, [update], destination, CancellationToken.None),
            destination,
            typeof(SourceIncompatibleException));
    }

    [Test]
    public async Task Apply_MissingPairedEntryRejectsWithoutCreatingOutput()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100);
        var update = CreateUpdate(temp, 101, omitEntry: "enterprise_insert.csv");
        var destination = Path.Combine(temp.Path, "result.zip");

        await AssertRejectedWithoutOutput(
            () => IncrementalState.Apply(baseline, [update], destination, CancellationToken.None),
            destination);
    }

    [Test]
    public async Task Apply_MismatchedInsertHeaderRejectsWithoutCreatingOutput()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100);
        var update = CreateUpdate(temp, 101, mismatchedInsertHeaderFor: "enterprise");
        var destination = Path.Combine(temp.Path, "result.zip");

        await AssertRejectedWithoutOutput(
            () => IncrementalState.Apply(baseline, [update], destination, CancellationToken.None),
            destination);
    }

    [Test]
    public async Task Apply_MismatchedDeleteHeaderRejectsWithoutCreatingOutput()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100);
        var update = CreateUpdate(temp, 101, mismatchedDeleteHeaderFor: "enterprise");
        var destination = Path.Combine(temp.Path, "result.zip");

        await AssertRejectedWithoutOutput(
            () => IncrementalState.Apply(baseline, [update], destination, CancellationToken.None),
            destination);
    }

    [Test]
    public async Task Apply_UnsortedBaselineKeysRejectWithoutCreatingOutput()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.000.102", "AC"), Enterprise("0000.000.101", "AC")])));
        var update = CreateUpdate(temp, 101);
        var destination = Path.Combine(temp.Path, "result.zip");

        await AssertRejectedWithoutOutput(
            () => IncrementalState.Apply(baseline, [update], destination, CancellationToken.None),
            destination,
            typeof(SourceIncompatibleException));
    }

    [Test]
    public async Task Apply_UnsortedInsertKeysRejectWithoutCreatingOutput()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100);
        var update = CreateUpdate(temp, 101, Deltas(
            ("enterprise", Delta(inserts:
            [
                Enterprise("0000.000.103", "AC"),
                Enterprise("0000.000.102", "AC")
            ]))));
        var destination = Path.Combine(temp.Path, "result.zip");

        await AssertRejectedWithoutOutput(
            () => IncrementalState.Apply(baseline, [update], destination, CancellationToken.None),
            destination,
            typeof(SourceIncompatibleException));
    }

    [Test]
    public async Task Apply_InsertOnlyCollisionRejectsWithoutCreatingOutput()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC")])));
        var update = CreateUpdate(temp, 101, Deltas(
            ("enterprise", Delta(inserts: [Enterprise("0000.000.101", "CE")]))));
        var destination = Path.Combine(temp.Path, "result.zip");

        await AssertRejectedWithoutOutput(
            () => IncrementalState.Apply(baseline, [update], destination, CancellationToken.None),
            destination);
    }

    [Test]
    public async Task Apply_CancellationLeavesNoOutput()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100);
        var update = CreateUpdate(temp, 101);
        var destination = Path.Combine(temp.Path, "result.zip");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = Capture(() =>
            IncrementalState.Apply(baseline, [update], destination, cancellation.Token));

        await Assert.That(error is OperationCanceledException).IsTrue();
        await Assert.That(((OperationCanceledException)error!).CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(File.Exists(destination)).IsFalse();
    }

    [Test]
    public async Task Apply_PreservesQuotedMultilineFieldValues()
    {
        using var temp = new TestDirectory();
        const string value = "Boulangerie \"Dupont\", Fils\r\nDeuxième ligne";
        var baseline = CreateFull(temp, 100, Sources(
            ("denomination", [Denomination("0000.000.101", "1", "001", value)])));
        var update = CreateUpdate(temp, 101);
        var destination = Path.Combine(temp.Path, "result.zip");

        IncrementalState.Apply(baseline, [update], destination, CancellationToken.None);

        await Assert.That(ReadEntry(destination, "denomination.csv")).Contains(CsvValue(value));
    }

    [Test]
    public async Task Apply_AcceptsTenDigitKeysWithAndWithoutDots()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.000.101", "AC")])));
        var update = CreateUpdate(temp, 101, Deltas(
            ("enterprise", Delta(inserts: [Enterprise("0000000102", "AC")]))));
        var destination = Path.Combine(temp.Path, "result.zip");

        var changes = IncrementalState.Apply(baseline, [update], destination, CancellationToken.None);

        await Assert.That(ReadDataRows(destination, "enterprise.csv").Last())
            .IsEqualTo(CsvRow(Enterprise("0000000102", "AC")));
        await Assert.That(changes.Entities).IsEquivalentTo(new long[] { 102 });
    }

    [Test]
    public async Task Apply_NormalizesDottedAndUndottedKeysAcrossBaselineDeleteAndInsertWithoutDuplicates()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100, Sources(
            ("enterprise",
            [
                Enterprise("0000.000.101", "AC"),
                Enterprise("0000.000.102", "AC")
            ])));
        var update = CreateUpdate(temp, 101, Deltas(
            ("enterprise", Delta(
                inserts: [Enterprise("0000000101", "LI")],
                deletes: [DeleteKey("0000000101")]))));
        var destination = Path.Combine(temp.Path, "result.zip");

        var changes = IncrementalState.Apply(baseline, [update], destination, CancellationToken.None);

        var rows = ReadDataRows(destination, "enterprise.csv");
        await Assert.That(rows.Length).IsEqualTo(2);
        await Assert.That(rows[0]).IsEqualTo(CsvRow(Enterprise("0000000101", "LI")));
        await Assert.That(rows[1]).IsEqualTo(CsvRow(Enterprise("0000.000.102", "AC")));
        await Assert.That(rows.Count(row => row.StartsWith("\"0000000101\"", StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(changes.Entities).IsEquivalentTo(new long[] { 101 });
    }

    [Test]
    public async Task Apply_RejectsKeyWithNineDigitsWithoutCreatingOutput()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.001.01", "AC")])));
        var update = CreateUpdate(temp, 101);
        var destination = Path.Combine(temp.Path, "result.zip");

        await AssertRejectedWithoutOutput(
            () => IncrementalState.Apply(baseline, [update], destination, CancellationToken.None),
            destination);
    }

    [Test]
    public async Task Apply_RejectsKeyWithElevenDigitsWithoutCreatingOutput()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100, Sources(
            ("enterprise", [Enterprise("00000.000.101", "AC")])));
        var update = CreateUpdate(temp, 101);
        var destination = Path.Combine(temp.Path, "result.zip");

        await AssertRejectedWithoutOutput(
            () => IncrementalState.Apply(baseline, [update], destination, CancellationToken.None),
            destination);
    }

    [Test]
    public async Task Apply_RejectsNondigitKeyCharacterWithoutCreatingOutput()
    {
        using var temp = new TestDirectory();
        var baseline = CreateFull(temp, 100, Sources(
            ("enterprise", [Enterprise("0000.00A.101", "AC")])));
        var update = CreateUpdate(temp, 101);
        var destination = Path.Combine(temp.Path, "result.zip");

        await AssertRejectedWithoutOutput(
            () => IncrementalState.Apply(baseline, [update], destination, CancellationToken.None),
            destination);
    }

    private static string CreateFull(
        TestDirectory temp,
        int extractNumber,
        IReadOnlyDictionary<string, string[][]>? source = null,
        string version = "1.0.0",
        string codeDescription = "Base language description")
    {
        var path = Path.Combine(temp.Path, $"full-{extractNumber}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(zip, "meta.csv", Csv(
            ["Variable", "Value"],
            [
                ["ExtractNumber", extractNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                ["Version", version],
                ["ExtractType", "full"]
            ]));
        AddEntry(zip, "code.csv", CodeCsv(codeDescription));
        foreach (var table in TableNames)
        {
            var rows = source is not null && source.TryGetValue(table, out var supplied)
                ? supplied
                : Array.Empty<string[]>();
            AddEntry(zip, table + ".csv", Csv(Headers[table], rows));
        }
        return path;
    }

    private static string CreateUpdate(
        TestDirectory temp,
        int extractNumber,
        IReadOnlyDictionary<string, TableDelta>? changes = null,
        string version = "1.0.0",
        string codeDescription = "Base language description",
        string? omitEntry = null,
        string? mismatchedInsertHeaderFor = null,
        string? mismatchedDeleteHeaderFor = null)
    {
        var path = Path.Combine(temp.Path, $"update-{extractNumber}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        AddEntry(zip, "meta.csv", Csv(
            ["Variable", "Value"],
            [
                ["ExtractNumber", extractNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                ["Version", version],
                ["ExtractType", "update"]
            ]));
        AddEntry(zip, "code.csv", CodeCsv(codeDescription));

        foreach (var table in TableNames)
        {
            var delta = changes is not null && changes.TryGetValue(table, out var supplied)
                ? supplied
                : TableDelta.Empty;

            var deleteEntry = table + "_delete.csv";
            if (deleteEntry != omitEntry)
            {
                var deleteHeader = table == mismatchedDeleteHeaderFor
                    ? new[] { "WrongKey" }
                    : new[] { Headers[table][0] };
                AddEntry(zip, deleteEntry, Csv(deleteHeader, delta.Deletes));
            }

            var insertEntry = table + "_insert.csv";
            if (insertEntry != omitEntry)
            {
                var insertHeader = Headers[table].ToArray();
                if (table == mismatchedInsertHeaderFor) insertHeader[0] = "WrongKey";
                AddEntry(zip, insertEntry, Csv(insertHeader, delta.Inserts));
            }
        }
        return path;
    }

    private static string CodeCsv(string description) =>
        Csv(["Category", "Code", "Language", "Description"], [["Language", "2", "NL", description]]);

    private static string Csv(string[] headers, IEnumerable<string[]> rows) =>
        string.Join('\n', new[] { headers }.Concat(rows).Select(CsvRow)) + "\n";

    private static string CsvRow(IEnumerable<string> values) =>
        string.Join(',', values.Select(CsvValue));

    private static string CsvValue(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string[] ReadDataRows(string zipPath, string name) =>
        NormalizeNewlines(ReadEntry(zipPath, name))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .ToArray();

    private static string ReadEntry(string zipPath, string name)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        using var stream = zip.GetEntry(name)!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string[] EntryNames(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.Select(entry => entry.FullName).ToArray();
    }

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static Dictionary<string, string[][]> Sources(params (string Table, string[][] Rows)[] rows)
    {
        var result = TableNames.ToDictionary(table => table, _ => Array.Empty<string[]>(), StringComparer.Ordinal);
        foreach (var (table, values) in rows) result[table] = values;
        return result;
    }

    private static Dictionary<string, TableDelta> Deltas(params (string Table, TableDelta Delta)[] deltas)
    {
        var result = new Dictionary<string, TableDelta>(StringComparer.Ordinal);
        foreach (var (table, delta) in deltas) result[table] = delta;
        return result;
    }

    private static TableDelta Delta(string[][]? inserts = null, string[][]? deletes = null) =>
        new(inserts ?? Array.Empty<string[]>(), deletes ?? Array.Empty<string[]>());

    private static string[] Enterprise(string number, string status) =>
        [number, status, "000", "2", "014", "", "01-01-2001"];

    private static string[] Establishment(string number, string enterprise) =>
        [number, "01-01-2005", enterprise];

    private static string[] Denomination(string entity, string language, string type, string value) =>
        [entity, language, type, value];

    private static string[] DeleteKey(string key) => [key];

    private static async Task AssertRejectedWithoutOutput(
        Action action,
        string destination,
        Type? expectedException = null)
    {
        var error = Capture(action);
        await Assert.That(error?.GetType()).IsEqualTo(expectedException ?? typeof(InvalidDataException));
        await Assert.That(File.Exists(destination)).IsFalse();
    }

    private static Exception? Capture(Action action)
    {
        try { action(); }
        catch (Exception error) { return error; }
        return null;
    }

    private sealed record TableDelta(IReadOnlyList<string[]> Inserts, IReadOnlyList<string[]> Deletes)
    {
        public static TableDelta Empty { get; } = new(Array.Empty<string[]>(), Array.Empty<string[]>());
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "kbo-incremental-state-tests-" + Guid.NewGuid().ToString("N"));

        public TestDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
