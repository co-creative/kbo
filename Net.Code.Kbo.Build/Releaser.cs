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
        Directory.CreateDirectory(workDirectory);
        var localDb = Path.Combine(workDirectory, FileName(candidate.number));
        try
        {
            ReleaseBuilder.Build(candidate.path, localDb, log);
            if (Validator.Validate(localDb, validation, log).Count > 0) return 1;

            var target = Path.Combine(share, FileName(candidate.number));
            var temp = target + ".tmp";
            File.Copy(localDb, temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
            WriteAtomically(Path.Combine(share, CurrentFile), FileName(candidate.number));
            log($"published {FileName(candidate.number)}");

            // keep the new release and the one before it (for rollback)
            foreach (var old in Directory.EnumerateFiles(share, "kbo-*.db").Select(p => (p, n: ExtractNumberOfDb(p))).Where(x => x.n > 0).OrderByDescending(x => x.n).Skip(2))
            {
                File.Delete(old.p);
                log($"deleted old release {Path.GetFileName(old.p)}");
            }

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
        return File.Exists(path) ? ExtractNumberOfDb(File.ReadAllText(path).Trim()) : 0;
    }

    private static int ExtractNumberOf(string zipPath) =>
        ZipName().Match(Path.GetFileName(zipPath)) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    private static int ExtractNumberOfDb(string dbPath) =>
        DbName().Match(Path.GetFileName(dbPath)) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    private static void WriteAtomically(string path, string content)
    {
        File.WriteAllText(path + ".tmp", content);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    [GeneratedRegex(@"^KboOpenData_(\d+)_.*_Full\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex ZipName();

    [GeneratedRegex(@"^kbo-(\d+)\.db$")]
    private static partial Regex DbName();
}
