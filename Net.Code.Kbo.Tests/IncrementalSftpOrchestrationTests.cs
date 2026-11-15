using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Net.Code.Kbo.Build;

namespace Net.Code.Kbo.Tests;

public class IncrementalSftpOrchestrationTests
{
    private static readonly ValidationOptions Lenient = new() { MaxOrphanFraction = 0.5 };
    private static readonly DateTimeOffset October = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset November = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private const int CurrentExtract = 500;
    private const int TargetExtract = 501;

    [Test]
    public async Task RunAsync_EnabledSameMonthPublishesDeltaWithoutDownloadingFull()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, CurrentExtract, "2026-10");
        var (files, contents) = FullAndUpdate(share, TargetExtract, Deltas(
            ("denomination",
                ["0403.170.701"],
                [["0403.170.701", "2", "001", "Incremental Quartz"]])));
        var source = new FakeSftpSource(files, contents);

        var exitCode = await RunAsync(share, source, October, incrementalEnabled: true);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.DownloadedNames).IsEquivalentTo(new[] { UpdateName(TargetExtract) });
        await Assert.That(ReadCurrent(share)).IsEqualTo(Releaser.FileName(TargetExtract));
        var store = CompanyStore.Open(Path.Combine(share.Path, Releaser.FileName(TargetExtract)), commonTermFraction: 1.0);
        await Assert.That(store.Get(KboNr.Parse("0403.170.701"))!.Names!.Single().Value).IsEqualTo("Incremental Quartz");
        await Assert.That(SearchNumbers(store, "incremental")).IsEquivalentTo(new[] { "0403.170.701" });
        await Assert.That(SourceBinding.Open(share.Path, TargetExtract, _ => { }, CancellationToken.None)!.FullMonth)
            .IsEqualTo("2026-10");
    }

    [Test]
    public async Task RunAsync_EmptyUpdatePublishesNewExtractWithoutChangingExistingCompany()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, CurrentExtract, "2026-10");
        var (files, contents) = FullAndUpdate(share, TargetExtract, Deltas());
        var source = new FakeSftpSource(files, contents);

        var exitCode = await RunAsync(share, source, October, incrementalEnabled: true);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.DownloadedNames).IsEquivalentTo(new[] { UpdateName(TargetExtract) });
        await Assert.That(ReadCurrent(share)).IsEqualTo(Releaser.FileName(TargetExtract));
        var store = CompanyStore.Open(Path.Combine(share.Path, Releaser.FileName(TargetExtract)), commonTermFraction: 1.0);
        await Assert.That(store.Get(KboNr.Parse("0403.170.701"))!.Names!.Select(name => name.Value))
            .IsEquivalentTo(new[] { "Acme Software BV", "Acme Labs" });
        await Assert.That(store.Release.ExtractNumber).IsEqualTo(TargetExtract);
    }

    [Test]
    public async Task RunAsync_DueMonthlyFullUsesFullInsteadOfAvailableUpdateChain()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, CurrentExtract, "2026-10");
        var (files, contents) = FullAndUpdate(share, TargetExtract, Deltas());
        var source = new FakeSftpSource(files, contents);

        var exitCode = await RunAsync(share, source, November, incrementalEnabled: true);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.DownloadedNames).IsEquivalentTo(new[] { FullName(TargetExtract) });
        await Assert.That(ReadCurrent(share)).IsEqualTo(Releaser.FileName(TargetExtract));
        await Assert.That(SourceBinding.Open(share.Path, TargetExtract, _ => { }, CancellationToken.None)!.FullMonth)
            .IsEqualTo("2026-11");
    }

    [Test]
    public async Task RunAsync_DisabledIncrementalModePublishesFullOnly()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, CurrentExtract, "2026-10");
        var (files, contents) = FullAndUpdate(share, TargetExtract, Deltas(
            ("denomination",
                ["0403.170.701"],
                [["0403.170.701", "2", "001", "Should Not Be Published"]])));
        var source = new FakeSftpSource(files, contents);

        var exitCode = await RunAsync(share, source, October, incrementalEnabled: false);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.DownloadedNames).IsEquivalentTo(new[] { FullName(TargetExtract) });
        var store = CompanyStore.Open(Path.Combine(share.Path, Releaser.FileName(TargetExtract)), commonTermFraction: 1.0);
        await Assert.That(store.Get(KboNr.Parse("0403.170.701"))!.Names!.Select(name => name.Value))
            .IsEquivalentTo(new[] { "Acme Software BV", "Acme Labs" });
    }

    [Test]
    public async Task RunAsync_IncompleteUpdateChainFallsBackToFull()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, CurrentExtract, "2026-10");
        const int target = 502;
        var baselineSource = SourceBinding.PathFor(share.Path, CurrentExtract);
        var update = CreateUpdateBytes(baselineSource, target, Deltas());
        var full = CreateFullBytes(share, target);
        var files = new[]
        {
            Remote(FullName(target), full),
            Remote(UpdateName(target), update)
        };
        var source = new FakeSftpSource(files, Contents((FullName(target), full), (UpdateName(target), update)));

        var exitCode = await RunAsync(share, source, October, incrementalEnabled: true);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.DownloadedNames).IsEquivalentTo(new[] { FullName(target) });
        await Assert.That(ReadCurrent(share)).IsEqualTo(Releaser.FileName(target));
        await Assert.That(CompanyStore.Open(Path.Combine(share.Path, Releaser.FileName(target))).Release.ExtractNumber)
            .IsEqualTo(target);
    }

    [Test]
    public async Task RunAsync_MissingSourceBindingFallsBackToFull()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, CurrentExtract, "2026-10", writeBinding: false);
        var (files, contents) = FullAndUpdate(share, TargetExtract, Deltas());
        var source = new FakeSftpSource(files, contents);

        var exitCode = await RunAsync(share, source, October, incrementalEnabled: true);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.DownloadedNames).IsEquivalentTo(new[] { FullName(TargetExtract) });
        await Assert.That(ReadCurrent(share)).IsEqualTo(Releaser.FileName(TargetExtract));
    }

    [Test]
    public async Task RunAsync_MismatchedSourceBindingFallsBackToFull()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, CurrentExtract, "2026-10");
        File.AppendAllText(SourceBinding.PathFor(share.Path, CurrentExtract), "changed");
        var (files, contents) = FullAndUpdate(share, TargetExtract, Deltas());
        var source = new FakeSftpSource(files, contents);

        var exitCode = await RunAsync(share, source, October, incrementalEnabled: true);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.DownloadedNames).IsEquivalentTo(new[] { FullName(TargetExtract) });
        await Assert.That(ReadCurrent(share)).IsEqualTo(Releaser.FileName(TargetExtract));
    }

    [Test]
    public async Task RunAsync_OlderSourceBindingSchemaFallsBackToFull()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, CurrentExtract, "2026-10");
        RewriteBindingWithOlderSchema(share, CurrentExtract);
        await Assert.That(SourceBinding.Open(share.Path, CurrentExtract, _ => { }, CancellationToken.None)).IsNull();
        var (files, contents) = FullAndUpdate(share, TargetExtract, Deltas());
        var source = new FakeSftpSource(files, contents);

        var exitCode = await RunAsync(share, source, October, incrementalEnabled: true);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.DownloadedNames).IsEquivalentTo(new[] { FullName(TargetExtract) });
        await Assert.That(ReadCurrent(share)).IsEqualTo(Releaser.FileName(TargetExtract));
    }

    [Test]
    public async Task RunAsync_ChangedCodebookFallsBackToFull()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, CurrentExtract, "2026-10");
        var baselineSource = SourceBinding.PathFor(share.Path, CurrentExtract);
        var update = CreateUpdateBytes(baselineSource, TargetExtract, Deltas(), changeCodebook: true);
        var full = CreateFullBytes(share, TargetExtract);
        var source = new FakeSftpSource(
            [Remote(FullName(TargetExtract), full), Remote(UpdateName(TargetExtract), update)],
            Contents((FullName(TargetExtract), full), (UpdateName(TargetExtract), update)));

        var exitCode = await RunAsync(share, source, October, incrementalEnabled: true);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.DownloadedNames).IsEquivalentTo(new[] { UpdateName(TargetExtract), FullName(TargetExtract) });
        await Assert.That(ReadCurrent(share)).IsEqualTo(Releaser.FileName(TargetExtract));
    }

    [Test]
    public async Task RunAsync_UnsupportedUpdateFormatFallsBackToFull()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, CurrentExtract, "2026-10");
        var baselineSource = SourceBinding.PathFor(share.Path, CurrentExtract);
        var update = CreateUpdateBytes(baselineSource, TargetExtract, Deltas(), version: "2.0.0");
        var full = CreateFullBytes(share, TargetExtract);
        var source = new FakeSftpSource(
            [Remote(FullName(TargetExtract), full), Remote(UpdateName(TargetExtract), update)],
            Contents((FullName(TargetExtract), full), (UpdateName(TargetExtract), update)));

        var exitCode = await RunAsync(share, source, October, incrementalEnabled: true);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.DownloadedNames).IsEquivalentTo(new[] { UpdateName(TargetExtract), FullName(TargetExtract) });
        await Assert.That(ReadCurrent(share)).IsEqualTo(Releaser.FileName(TargetExtract));
    }

    [Test]
    public async Task RunAsync_DuplicateUpdateNumbersAreRejectedWithoutChangingCurrent()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, CurrentExtract, "2026-10");
        var baselineSource = SourceBinding.PathFor(share.Path, CurrentExtract);
        var update = CreateUpdateBytes(baselineSource, TargetExtract, Deltas());
        var full = CreateFullBytes(share, TargetExtract);
        var duplicateName = "KboOpenData_0501_2026_10_05_Update.zip";
        var source = new FakeSftpSource(
            [
                Remote(FullName(TargetExtract), full),
                Remote(UpdateName(TargetExtract), update),
                Remote(duplicateName, update)
            ],
            Contents((FullName(TargetExtract), full), (UpdateName(TargetExtract), update), (duplicateName, update)));
        var before = HashFile(Path.Combine(share.Path, Releaser.FileName(CurrentExtract)));

        var error = await CaptureAsync(() => RunAsync(share, source, October, incrementalEnabled: true));

        await Assert.That(error?.GetType()).IsEqualTo(typeof(InvalidDataException));
        await Assert.That(source.DownloadedNames).IsEmpty();
        await Assert.That(ReadCurrent(share)).IsEqualTo(Releaser.FileName(CurrentExtract));
        await Assert.That(HashFile(Path.Combine(share.Path, Releaser.FileName(CurrentExtract)))).IsEqualTo(before);
    }

    [Test]
    public async Task RunAsync_NoNewerFullExtractReturnsWithoutDownloading()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, CurrentExtract, "2026-10");
        var source = new FakeSftpSource(
            [new RemoteExtract(FullName(CurrentExtract), 1)],
            Contents((FullName(CurrentExtract), [1])));
        var before = HashFile(Path.Combine(share.Path, Releaser.FileName(CurrentExtract)));

        var exitCode = await RunAsync(share, source, October, incrementalEnabled: true);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.DownloadedNames).IsEmpty();
        await Assert.That(ReadCurrent(share)).IsEqualTo(Releaser.FileName(CurrentExtract));
        await Assert.That(HashFile(Path.Combine(share.Path, Releaser.FileName(CurrentExtract)))).IsEqualTo(before);
    }

    [Test]
    public async Task RunAsync_NoNewerFullCleansReleasesBeyondCurrentAndBoundPrevious()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, 400, "2026-10");
        SeedCurrentRelease(share, 500, "2026-10", previousExtract: 400);
        SeedCurrentRelease(share, 501, "2026-10", previousExtract: 500);
        var source = new FakeSftpSource(
            [Remote(FullName(501), [1])],
            Contents((FullName(501), [1])));

        var exitCode = await RunAsync(share, source, October, incrementalEnabled: true);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(source.DownloadedNames).IsEmpty();
        await Assert.That(File.Exists(Path.Combine(share.Path, Releaser.FileName(501)))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(share.Path, Releaser.FileName(500)))).IsTrue();
        await Assert.That(File.Exists(SourceBinding.PathFor(share.Path, 501))).IsTrue();
        await Assert.That(File.Exists(SourceBinding.PathFor(share.Path, 500))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(share.Path, Releaser.FileName(400)))).IsFalse();
        await Assert.That(File.Exists(SourceBinding.PathFor(share.Path, 400))).IsFalse();
    }

    [Test]
    public async Task RunAsync_UpdateTransferFailurePreservesCurrentRelease()
    {
        using var share = new TempDirectory();
        SeedCurrentRelease(share, CurrentExtract, "2026-10");
        var (files, contents) = FullAndUpdate(share, TargetExtract, Deltas());
        var source = new FakeSftpSource(files, contents) { DownloadFailureFor = UpdateName(TargetExtract) };
        var before = HashFile(Path.Combine(share.Path, Releaser.FileName(CurrentExtract)));
        var sourceBefore = HashFile(SourceBinding.PathFor(share.Path, CurrentExtract));

        var error = await CaptureAsync(() => RunAsync(share, source, October, incrementalEnabled: true));

        await Assert.That(error?.GetType()).IsEqualTo(typeof(IOException));
        await Assert.That(source.DownloadedNames).IsEquivalentTo(new[] { UpdateName(TargetExtract) });
        await Assert.That(ReadCurrent(share)).IsEqualTo(Releaser.FileName(CurrentExtract));
        await Assert.That(HashFile(Path.Combine(share.Path, Releaser.FileName(CurrentExtract)))).IsEqualTo(before);
        await Assert.That(HashFile(SourceBinding.PathFor(share.Path, CurrentExtract))).IsEqualTo(sourceBefore);
        await Assert.That(File.Exists(Path.Combine(share.Path, Releaser.FileName(TargetExtract)))).IsFalse();
        await Assert.That(Directory.GetFiles(Path.Combine(share.Path, "work"), ".sftp-*.part", SearchOption.AllDirectories))
            .IsEmpty();
    }

    [Test]
    public async Task RunAsync_FullValidationFailurePreservesCurrentRelease()
    {
        using var share = new TempDirectory();
        using var scratch = new TempDirectory();
        SeedCurrentRelease(share, CurrentExtract, "2026-10");
        var full = CreateFullBytes(share, TargetExtract);
        var source = new FakeSftpSource(
            [Remote(FullName(TargetExtract), full)],
            Contents((FullName(TargetExtract), full)));
        var before = HashFile(Path.Combine(share.Path, Releaser.FileName(CurrentExtract)));
        var sourceBefore = HashFile(SourceBinding.PathFor(share.Path, CurrentExtract));

        var exitCode = await RunAsync(
            share, source, November, incrementalEnabled: true,
            validation: Lenient with { MaxBytes = 1 },
            workDirectory: scratch.Path);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(source.DownloadedNames).IsEquivalentTo(new[] { FullName(TargetExtract) });
        await Assert.That(ReadCurrent(share)).IsEqualTo(Releaser.FileName(CurrentExtract));
        await Assert.That(HashFile(Path.Combine(share.Path, Releaser.FileName(CurrentExtract)))).IsEqualTo(before);
        await Assert.That(HashFile(SourceBinding.PathFor(share.Path, CurrentExtract))).IsEqualTo(sourceBefore);
        await Assert.That(File.Exists(Path.Combine(share.Path, Releaser.FileName(TargetExtract)))).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(share.Path, "incoming"))).IsFalse();
        await Assert.That(Directory.Exists(scratch.Path)
            ? Directory.GetFiles(scratch.Path, "*.zip", SearchOption.AllDirectories).Length
            : 0).IsEqualTo(0);
    }

    private static async Task<int> RunAsync(
        TempDirectory share,
        FakeSftpSource source,
        DateTimeOffset now,
        bool incrementalEnabled,
        ValidationOptions? validation = null,
        string? workDirectory = null)
    {
        return await SftpIngestion.RunAsync(
            share.Path,
            workDirectory ?? Path.Combine(share.Path, "work"),
            validation ?? Lenient,
            _ => Task.FromResult(new SftpCredential("unit-test-user", "unit-test-password-only")),
            _ => source,
            _ => { },
            CancellationToken.None,
            new FixedTimeProvider(now),
            incrementalEnabled);
    }

    private static void SeedCurrentRelease(
        TempDirectory share, int extract, string fullMonth,
        int previousExtract = 0, bool writeBinding = true)
    {
        var original = KboFixture.CreateZip(Path.Combine(share.Path, "baseline-original"), extract);
        var source = CreateSortedSource(share, original, extract);
        var work = Path.Combine(share.Path, "baseline-work");
        Directory.CreateDirectory(work);
        var database = Path.Combine(work, Releaser.FileName(extract));
        ReleaseBuilder.Build(source, database, _ => { });
        if (writeBinding)
        {
            var binding = new SourceBinding(
                extract, SourceBinding.HashFile(source, CancellationToken.None), fullMonth, previousExtract);
            SourceBinding.Write(database, binding);
        }

        File.Copy(database, Path.Combine(share.Path, Releaser.FileName(extract)));
        File.WriteAllText(Path.Combine(share.Path, Releaser.CurrentFile), Releaser.FileName(extract));
        var sourceTarget = SourceBinding.PathFor(share.Path, extract);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceTarget)!);
        File.Copy(source, sourceTarget);
    }

    private static void RewriteBindingWithOlderSchema(TempDirectory share, int extract)
    {
        var database = Path.Combine(share.Path, Releaser.FileName(extract));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DROP TABLE incremental_state;
            CREATE TABLE incremental_state (
              version INTEGER NOT NULL, extract_number INTEGER NOT NULL,
              source_sha256 TEXT NOT NULL, full_month TEXT NOT NULL
            );
            INSERT INTO incremental_state
              VALUES (1, $extract, $hash, '2026-10');
            """;
        command.Parameters.AddWithValue("$extract", extract);
        command.Parameters.AddWithValue("$hash",
            SourceBinding.HashFile(SourceBinding.PathFor(share.Path, extract), CancellationToken.None));
        command.ExecuteNonQuery();
    }

    private static (IReadOnlyList<RemoteExtract> Files, IReadOnlyDictionary<string, byte[]> Contents)
        FullAndUpdate(TempDirectory share, int target, IReadOnlyDictionary<string, Delta> deltas)
    {
        var baselineSource = SourceBinding.PathFor(share.Path, CurrentExtract);
        var full = CreateFullBytes(share, target);
        var update = CreateUpdateBytes(baselineSource, target, deltas);
        var fullName = FullName(target);
        var updateName = UpdateName(target);
        return (
            [Remote(fullName, full), Remote(updateName, update)],
            Contents((fullName, full), (updateName, update)));
    }

    private static byte[] CreateFullBytes(TempDirectory share, int extract)
    {
        var path = KboFixture.CreateZip(Path.Combine(share.Path, $"remote-full-{extract}"), extract);
        return File.ReadAllBytes(path);
    }

    private static string CreateSortedSource(TempDirectory temp, string originalPath, int extract)
    {
        var destination = Path.Combine(temp.Path, "baseline", $"sorted-{extract:0000}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var original = ZipFile.OpenRead(originalPath);
        using var output = ZipFile.Open(destination, ZipArchiveMode.Create);
        foreach (var entry in original.Entries)
        {
            if (!TableNames.Any(table => entry.FullName == table + ".csv"))
            {
                CopyEntry(entry, output);
                continue;
            }

            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r')).ToArray();
            using var writer = new StreamWriter(output.CreateEntry(entry.FullName).Open(), new UTF8Encoding(false));
            writer.WriteLine(lines[0]);
            foreach (var line in lines.Skip(1).OrderBy(FirstCsvField, StringComparer.Ordinal))
                writer.WriteLine(line);
        }
        return destination;
    }

    private static string FirstCsvField(string line)
    {
        var delimiter = line.IndexOf("\",", StringComparison.Ordinal);
        return delimiter < 0 ? line : line[..(delimiter + 1)];
    }

    private static byte[] CreateUpdateBytes(
        string baselinePath, int extract, IReadOnlyDictionary<string, Delta> deltas,
        string version = "1.0.0", bool changeCodebook = false)
    {
        using var baseline = ZipFile.OpenRead(baselinePath);
        using var outputStream = new MemoryStream();
        using (var output = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(output, "meta.csv", string.Join('\n',
                "\"Variable\",\"Value\"",
                "\"SnapshotDate\",\"03-10-2026\"",
                "\"ExtractTimestamp\",\"04-10-2026 09:04:45\"",
                "\"ExtractType\",\"update\"",
                $"\"ExtractNumber\",\"{extract.ToString(CultureInfo.InvariantCulture)}\"",
                $"\"Version\",\"{version}\"") + "\n");
            if (changeCodebook)
            {
                using var codeReader = new StreamReader(baseline.GetEntry("code.csv")!.Open(), Encoding.UTF8);
                var code = codeReader.ReadToEnd() + "\"TestCategory\",\"TEST\",\"NL\",\"Different codebook\"\n";
                WriteTextEntry(output, "code.csv", code);
            }
            else
                CopyEntry(baseline.GetEntry("code.csv")!, output);
            foreach (var table in TableNames)
            {
                var header = ReadFirstLine(baseline.GetEntry(table + ".csv")!);
                var delta = deltas.GetValueOrDefault(table) ?? Delta.Empty;
                var keyHeader = header[..(header.IndexOf(',') is var comma && comma >= 0 ? comma : header.Length)];
                var deletes = new StringBuilder().AppendLine(keyHeader);
                foreach (var key in delta.Deletes) deletes.AppendLine(Quote(key));
                WriteTextEntry(output, table + "_delete.csv", deletes.ToString());

                var inserts = new StringBuilder().AppendLine(header);
                foreach (var row in delta.Inserts)
                    inserts.AppendLine(string.Join(',', row.Select(Quote)));
                WriteTextEntry(output, table + "_insert.csv", inserts.ToString());
            }
        }
        return outputStream.ToArray();
    }

    private static string ReadFirstLine(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadLine() ?? throw new InvalidDataException($"Missing CSV header in {entry.FullName}.");
    }

    private static void CopyEntry(ZipArchiveEntry source, ZipArchive destination)
    {
        using var input = source.Open();
        using var output = destination.CreateEntry(source.FullName).Open();
        input.CopyTo(output);
    }

    private static void WriteTextEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static IReadOnlyDictionary<string, Delta> Deltas(
        params (string Table, string[] Deletes, string[][] Inserts)[] values) =>
        values.ToDictionary(value => value.Table, value => new Delta(value.Deletes, value.Inserts), StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, byte[]> Contents(params (string Name, byte[] Bytes)[] files) =>
        files.ToDictionary(file => file.Name, file => file.Bytes, StringComparer.Ordinal);

    private static RemoteExtract Remote(string name, byte[] bytes) => new(name, bytes.Length);

    private static string FullName(int extract) => $"KboOpenData_{extract:0000}_2026_10_04_Full.zip";

    private static string UpdateName(int extract) => $"KboOpenData_{extract:0000}_2026_10_04_Update.zip";

    private static string ReadCurrent(TempDirectory share) =>
        File.ReadAllText(Path.Combine(share.Path, Releaser.CurrentFile));

    private static string[] SearchNumbers(CompanyStore store, string text) =>
        store.Search(new SearchQuery { Text = text }).Items.Select(item => item.Number).ToArray();

    private static string HashFile(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task<Exception?> CaptureAsync(Func<Task<int>> action)
    {
        try { await action(); }
        catch (Exception error) { return error; }
        return null;
    }

    private static readonly string[] TableNames =
        ["enterprise", "establishment", "branch", "denomination", "address", "contact", "activity"];

    private sealed record Delta(string[] Deletes, string[][] Inserts)
    {
        public static Delta Empty { get; } = new([], []);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeSftpSource(
        IReadOnlyList<RemoteExtract> files,
        IReadOnlyDictionary<string, byte[]> contents) : ISftpSource
    {
        public string? DownloadFailureFor { get; init; }
        public bool Disposed { get; private set; }
        public List<string> DownloadedNames { get; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RemoteExtract>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(files);
        }

        public async Task DownloadAsync(string name, Stream destination, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DownloadedNames.Add(name);
            if (name == DownloadFailureFor) throw new IOException("Synthetic transfer failure.");
            await destination.WriteAsync(contents[name], cancellationToken);
        }

        public void Dispose() => Disposed = true;
    }
}
