using System.Text.RegularExpressions;

namespace Net.Code.Kbo.Build;

/// <summary>
/// The monthly release flow on the file share (specs/azure-architecture.md §6):
/// <code>
/// share/incoming/KboOpenData_0502_2026_10_04_Full.zip   ← uploaded by hand
/// share/kbo-0502.db, share/kbo-0501.db                   ← current and previous release
/// share/current.txt                                      ← "kbo-0502.db", written last (the switch)
/// </code>
/// </summary>
static partial class Releaser
{
    public const string CurrentFile = "current.txt";

    /// <returns>Process exit code: 0 = published or nothing to do, 1 = build or validation failed.</returns>
    public static int Run(string share, string workDirectory, ValidationOptions validation, Action<string> log)
    {
        using var lease = AcquireLease(share);
        return RunWhileLeased(share, workDirectory, validation, log);
    }

    internal static IDisposable AcquireLease(string share)
    {
        var path = Path.Combine(share, ".release.lock");
        return new ReleaseLease(path, new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None));
    }

    private sealed class ReleaseLease(string path, FileStream stream) : IDisposable
    {
        public void Dispose()
        {
            stream.Dispose();
            File.Delete(path);
        }
    }

    internal static int RunWhileLeased(string share, string workDirectory, ValidationOptions validation, Action<string> log)
    {
        var incoming = Path.Combine(share, "incoming");
        var current = CurrentExtract(share);

        var candidate = Directory.Exists(incoming)
            ? Directory.EnumerateFiles(incoming, "*_Full.zip")
                .Select(path => (path, number: ExtractNumberOf(path)))
                .Where(c => c.number > current)
                .OrderByDescending(c => c.number)
                .FirstOrDefault()
            : default;
        if (candidate.path is null)
        {
            log($"nothing to do: no Full zip in {incoming} newer than the current release ({(current == 0 ? "none" : FileName(current))})");
            return 0;
        }

        log($"building extract {candidate.number} from {Path.GetFileName(candidate.path)}");
        var target = Path.Combine(share, FileName(candidate.number));
        if (File.Exists(target))
        {
            log($"cannot publish {FileName(candidate.number)}: that immutable release already exists");
            return 1;
        }
        Directory.CreateDirectory(workDirectory);
        var localDb = Path.Combine(workDirectory, FileName(candidate.number));
        try
        {
            ReleaseBuilder.Build(candidate.path, localDb, log);
            if (Validator.Validate(localDb, validation, log, candidate.number).Count > 0) return 1;

            var temp = target + ".tmp";
            File.Copy(localDb, temp, overwrite: true);
            File.Move(temp, target);
            WriteAtomically(Path.Combine(share, CurrentFile), FileName(candidate.number));
            log($"published {FileName(candidate.number)}");

            PruneReleases(share, candidate.number, current, log);

            var processed = Path.Combine(incoming, "processed");
            Directory.CreateDirectory(processed);
            File.Move(candidate.path, Path.Combine(processed, Path.GetFileName(candidate.path)), overwrite: true);
            return 0;
        }
        finally
        {
            if (File.Exists(localDb)) File.Delete(localDb);
        }
    }

    public static string FileName(int extractNumber) => $"kbo-{extractNumber:0000}.db";

    /// <summary>Extract number of the release named in current.txt, or 0.</summary>
    public static int CurrentExtract(string share)
    {
        var path = Path.Combine(share, CurrentFile);
        if (!File.Exists(path)) return 0;
        var number = ExtractNumberOfDb(File.ReadAllText(path).Trim());
        return number > 0 ? number : throw new InvalidDataException("The current release pointer is invalid.");
    }

    private static int ExtractNumberOf(string zipPath) =>
        ZipName().Match(Path.GetFileName(zipPath)) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    private static int ExtractNumberOfDb(string dbPath) =>
        DbName().Match(Path.GetFileName(dbPath)) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    internal static void WriteAtomically(string path, string content)
    {
        File.WriteAllText(path + ".tmp", content);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    internal static void PruneReleases(string share, int current, int previous, Action<string> log)
    {
        var releases = Directory.EnumerateFiles(share, "kbo-*.db")
            .Select(path => (path, number: ExtractNumberOfDb(path)))
            .Where(x => x.number > 0).ToArray();
        if (previous == 0)
            previous = releases.Where(x => x.number != current).Select(x => x.number).DefaultIfEmpty().Max();
        foreach (var (path, number) in releases.Where(x => x.number != current && x.number != previous))
        {
            File.Delete(path);
            log($"deleted old release {Path.GetFileName(path)}");
            var source = SourceBinding.PathFor(share, number);
            if (File.Exists(source)) File.Delete(source);
        }
        foreach (var path in Directory.EnumerateFiles(share, "kbo-*.db.tmp"))
            if (DbTempName().IsMatch(Path.GetFileName(path))) File.Delete(path);

        var stateDirectory = Path.Combine(share, ".state");
        if (!Directory.Exists(stateDirectory)) return;
        foreach (var path in Directory.EnumerateFiles(stateDirectory, "source-*.zip"))
        {
            var match = SourceName().Match(Path.GetFileName(path));
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number) &&
                number != current && number != previous)
                File.Delete(path);
        }
        foreach (var path in Directory.EnumerateFiles(stateDirectory, "source-*.zip.tmp"))
            if (SourceTempName().IsMatch(Path.GetFileName(path))) File.Delete(path);
    }

    [GeneratedRegex(@"^KboOpenData_(\d+)_.*_Full\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex ZipName();

    [GeneratedRegex(@"^kbo-(\d+)\.db$")]
    private static partial Regex DbName();

    [GeneratedRegex(@"^source-(\d+)\.zip$")]
    private static partial Regex SourceName();

    [GeneratedRegex(@"^kbo-\d+\.db\.tmp$")]
    private static partial Regex DbTempName();

    [GeneratedRegex(@"^source-\d+\.zip\.tmp$")]
    private static partial Regex SourceTempName();
}
