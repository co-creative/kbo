using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Net.Code.Csv;

namespace Net.Code.Kbo.Build;

sealed record IncrementalChanges(
    IReadOnlySet<long> Entities,
    IReadOnlySet<long> EnterpriseOwners,
    bool CodesChanged);

sealed class SourceIncompatibleException(string message) : IOException(message);

/// <summary>Creates a new, lossless source snapshot from a Full snapshot and consecutive Updates.</summary>
static class IncrementalState
{
    private static readonly string[] Tables =
        ["enterprise", "establishment", "branch", "denomination", "address", "contact", "activity"];

    public static IncrementalChanges Apply(
        string baselineZip, IReadOnlyList<string> updateZips, string destinationZip,
        CancellationToken cancellationToken)
    {
        if (updateZips.Count == 0) throw new ArgumentException("At least one Update is required.", nameof(updateZips));

        using var baseline = ZipFile.OpenRead(baselineZip);
        var updates = new List<ZipArchive>();
        try
        {
            foreach (var path in updateZips) updates.Add(ZipFile.OpenRead(path));
            CheckEntries(baseline, isUpdate: false);
            foreach (var update in updates) CheckEntries(update, isUpdate: true);

            var previous = Metadata(baseline);
            var version = previous.Version;
            foreach (var update in updates)
            {
                var meta = Metadata(update);
                if (meta.Version != version)
                    throw new SourceIncompatibleException("The Update source format changed.");
                if (meta.Type != "update" || meta.Number != previous.Number + 1)
                    throw new InvalidDataException("The Update chain metadata is invalid.");
                previous = meta;
            }

            var entities = new HashSet<long>();
            var owners = new HashSet<long>();
            var codesChanged = !SameContent(baseline.GetEntry("code.csv")!, updates[^1].GetEntry("code.csv")!);
            var created = false;
            try
            {
                using var outputStream = new FileStream(destinationZip, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                created = true;
                using (var output = new ZipArchive(outputStream, ZipArchiveMode.Create))
                {
                    Copy(updates[^1].GetEntry("meta.csv")!, output);
                    Copy(updates[^1].GetEntry("code.csv")!, output);
                    foreach (var table in Tables)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        MergeTable(baseline, updates, output, table, entities, owners, cancellationToken);
                    }
                }
                return new IncrementalChanges(entities, owners, codesChanged);
            }
            catch
            {
                if (created) File.Delete(destinationZip);
                throw;
            }
        }
        finally
        {
            foreach (var update in updates) update.Dispose();
        }
    }

    private static void MergeTable(
        ZipArchive baseline, IReadOnlyList<ZipArchive> updates, ZipArchive output, string table,
        HashSet<long> entities, HashSet<long> owners, CancellationToken cancellationToken)
    {
        using var baselineStream = baseline.GetEntry(table + ".csv")!.Open();
        using var source = Reader(baselineStream);
        var headers = Headers(source);
        var deleted = new HashSet<string>(StringComparer.Ordinal);
        var pending = new SortedDictionary<string, List<string[]>>(StringComparer.Ordinal);

        foreach (var update in updates)
        {
            var deletedInUpdate = new HashSet<string>(StringComparer.Ordinal);
            using (var stream = update.GetEntry(table + "_delete.csv")!.Open())
            using (var reader = Reader(stream))
            {
                if (!Headers(reader).SequenceEqual([headers[0]], StringComparer.Ordinal))
                    throw new InvalidDataException($"Invalid {table} delete header.");
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var key = Key(reader.GetString(0));
                    entities.Add(EntityNumber.Parse(key));
                    deleted.Add(key);
                    deletedInUpdate.Add(key);
                    pending.Remove(key);
                }
            }

            using (var stream = update.GetEntry(table + "_insert.csv")!.Open())
            using (var reader = Reader(stream))
            {
                if (!Headers(reader).SequenceEqual(headers, StringComparer.Ordinal))
                    throw new InvalidDataException($"Invalid {table} insert header.");
                string? previous = null;
                var insertedInUpdate = new HashSet<string>(StringComparer.Ordinal);
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = Values(reader);
                    var key = Key(row[0]);
                    if (previous is not null && string.CompareOrdinal(previous, key) > 0)
                        throw new SourceIncompatibleException($"Unsorted {table} insert rows.");
                    previous = key;
                    entities.Add(EntityNumber.Parse(key));
                    AddOwner(table, row, owners);
                    if (insertedInUpdate.Add(key) && pending.ContainsKey(key) && !deletedInUpdate.Contains(key))
                        throw new InvalidDataException($"An inserted {table} key already exists without a deletion.");
                    if (!pending.TryGetValue(key, out var group)) pending[key] = group = [];
                    group.Add(row);
                }
            }
        }

        using var streamOut = CreateEntry(output, table + ".csv").Open();
        using var writer = new StreamWriter(streamOut, new UTF8Encoding(false));
        Write(writer, headers);
        using var additions = pending.GetEnumerator();
        var hasAddition = additions.MoveNext();
        string? last = null;
        var count = 0;
        while (source.Read())
        {
            if ((++count & 0x7fff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var row = Values(source);
            var key = Key(row[0]);
            if (last is not null && string.CompareOrdinal(last, key) > 0)
                throw new SourceIncompatibleException($"Unsorted {table} baseline rows.");
            if (key != last)
            {
                while (hasAddition && string.CompareOrdinal(additions.Current.Key, key) < 0)
                {
                    foreach (var addition in additions.Current.Value) Write(writer, addition);
                    hasAddition = additions.MoveNext();
                }
                if (hasAddition && additions.Current.Key == key)
                {
                    if (!deleted.Contains(key))
                        throw new InvalidDataException($"An inserted {table} key already exists without a deletion.");
                    foreach (var addition in additions.Current.Value) Write(writer, addition);
                    hasAddition = additions.MoveNext();
                }
                last = key;
            }
            if (deleted.Contains(key)) AddOwner(table, row, owners);
            else Write(writer, row);
        }
        while (hasAddition)
        {
            foreach (var addition in additions.Current.Value) Write(writer, addition);
            hasAddition = additions.MoveNext();
        }
    }

    private static IDataReader Reader(Stream stream) =>
        ReadCsv.FromStream(stream, encoding: Encoding.UTF8, comment: null, hasHeaders: true);

    private static string[] Headers(IDataReader reader) =>
        Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();

    private static string[] Values(IDataReader reader) =>
        Enumerable.Range(0, reader.FieldCount)
            .Select(i => reader.IsDBNull(i) ? "" : reader.GetString(i)).ToArray();

    private static void Write(TextWriter writer, IReadOnlyList<string> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0) writer.Write(',');
            writer.Write('"');
            writer.Write(values[i].Replace("\"", "\"\"", StringComparison.Ordinal));
            writer.Write('"');
        }
        writer.WriteLine();
    }

    private static string Key(string value)
    {
        if (value.Length == 10 && value.All(char.IsAsciiDigit))
            return EntityNumber.Format(EntityNumber.Parse(value));
        var enterprise = value.Length == 12 && value[0] is '0' or '1';
        var unit = value.Length == 13 && value[0] is >= '2' and <= '9';
        if (!enterprise && !unit)
            throw new InvalidDataException("Invalid entity number in source data.");
        for (var i = 0; i < value.Length; i++)
        {
            var dot = enterprise ? i is 4 or 8 : i is 1 or 5 or 9;
            if (dot ? value[i] != '.' : !char.IsAsciiDigit(value[i]))
                throw new InvalidDataException("Invalid entity number in source data.");
        }
        return value;
    }

    private static void AddOwner(string table, IReadOnlyList<string> row, HashSet<long> owners)
    {
        if (table is "establishment" or "branch")
            owners.Add(EntityNumber.Parse(Key(row[2])));
    }

    private static void CheckEntries(ZipArchive zip, bool isUpdate)
    {
        var names = new HashSet<string>(StringComparer.Ordinal) { "meta.csv", "code.csv" };
        foreach (var table in Tables)
        {
            if (isUpdate) { names.Add(table + "_delete.csv"); names.Add(table + "_insert.csv"); }
            else names.Add(table + ".csv");
        }
        if (zip.Entries.Count != names.Count || !zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal).SetEquals(names))
        {
            if (!isUpdate) throw new SourceIncompatibleException("The baseline has an unsupported set of CSV files.");
            throw new InvalidDataException("The Update does not contain the expected CSV files.");
        }
    }

    private static (int Number, string Version, string Type) Metadata(ZipArchive zip)
    {
        using var stream = zip.GetEntry("meta.csv")!.Open();
        using var reader = Reader(stream);
        if (!Headers(reader).SequenceEqual(["Variable", "Value"], StringComparer.Ordinal))
            throw new InvalidDataException("Invalid extract metadata header.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read()) values.Add(reader.GetString(0), reader.GetString(1));
        if (!values.TryGetValue("ExtractNumber", out var number) ||
            !int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var extract) || extract < 1 ||
            !values.TryGetValue("Version", out var version) || string.IsNullOrEmpty(version) ||
            !values.TryGetValue("ExtractType", out var type) || type is not ("full" or "update"))
            throw new InvalidDataException("Invalid extract metadata.");
        return (extract, version, type);
    }

    private static bool SameContent(ZipArchiveEntry first, ZipArchiveEntry second)
    {
        using var a = first.Open();
        using var b = second.Open();
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(a), SHA256.HashData(b));
    }

    private static void Copy(ZipArchiveEntry entry, ZipArchive output)
    {
        using var source = entry.Open();
        using var destination = CreateEntry(output, entry.FullName).Open();
        source.CopyTo(destination);
    }

    private static ZipArchiveEntry CreateEntry(ZipArchive output, string name)
    {
        var entry = output.CreateEntry(name, CompressionLevel.Fastest);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return entry;
    }
}
