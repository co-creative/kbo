namespace Net.Code.Kbo.Web;

/// <summary>
/// The release database being served, opened from:
/// <list type="bullet">
/// <item><c>Kbo:Database</c>: path to a kbo-&lt;extract&gt;.db (local development, tests), opened once, or</item>
/// <item><c>Kbo:Share</c>: the (mounted) share; serves the file named in its current.txt (Azure).</item>
/// </list>
/// Relative paths are resolved against the content root. The app starts without a release (first deployment,
/// empty share): <see cref="Current"/> is then null and opening is retried on demand, at most every
/// <c>Kbo:RetryInterval</c> (default 10 s). Once a release is open, current.txt is checked again on demand, at most
/// every <c>Kbo:CheckInterval</c> (default 1 min): when it names another file (a new release, or a rollback), that
/// file is opened and swapped in. Requests that already hold the previous store finish on it; the releaser keeps
/// the previous file on the share, so it stays readable.
/// </summary>
public sealed class ReleaseProvider(IConfiguration config, IHostEnvironment env, ILogger<ReleaseProvider> log)
{
    private readonly Lock gate = new();
    private readonly TimeSpan retryInterval = config.GetValue("Kbo:RetryInterval", TimeSpan.FromSeconds(10));
    private readonly TimeSpan checkInterval = config.GetValue("Kbo:CheckInterval", TimeSpan.FromMinutes(1));
    private CompanyStore? store;
    private string? storePath;
    private long nextCheck = DateTime.MinValue.Ticks; // UTC ticks; MaxValue once a fixed Kbo:Database is open

    /// <summary>The open release, or null when there is none (yet).</summary>
    public CompanyStore? Current
    {
        get
        {
            var open = Volatile.Read(ref store);
            if (open is not null && DateTime.UtcNow.Ticks < Volatile.Read(ref nextCheck)) return open;

            // a check is due; with a release open, other requests keep using it instead of waiting for the check
            if (open is not null) { if (!gate.TryEnter()) return open; }
            else gate.Enter();
            try
            {
                if (DateTime.UtcNow.Ticks >= nextCheck) Refresh();
                return store;
            }
            finally { gate.Exit(); }
        }
    }

    private void Refresh()
    {
        try
        {
            var (path, isFixed) = Resolve();
            if (path != storePath)
            {
                var previous = store;
                var opened = CompanyStore.Open(path, config.GetValue("Kbo:CommonTermFraction", 0.10));
                Volatile.Write(ref store, opened);
                storePath = path;
                log.LogInformation("Serving KBO extract {Extract} ({Companies:N0} companies)", opened.Release.ExtractNumber, opened.CompanyCount);
                previous?.ClearPool(); // close its idle connections, so old files on the share can be deleted later
            }
            Volatile.Write(ref nextCheck, isFixed ? DateTime.MaxValue.Ticks : (DateTime.UtcNow + checkInterval).Ticks);
        }
        catch (Exception e)
        {
            Volatile.Write(ref nextCheck, (DateTime.UtcNow + (store is null ? retryInterval : checkInterval)).Ticks);
            if (store is null) log.LogWarning("No KBO release available yet: {Reason}", e.Message);
            else log.LogWarning("Keeping KBO extract {Extract}; could not open the current release: {Reason}", store.Release.ExtractNumber, e.Message);
        }
    }

    /// <summary>Full path of the release to serve, and whether it is fixed (Kbo:Database) rather than named in current.txt.</summary>
    private (string Path, bool IsFixed) Resolve()
    {
        var database = config["Kbo:Database"];
        if (!string.IsNullOrEmpty(database)) return (Path.GetFullPath(Path.Combine(env.ContentRootPath, database)), true);

        var share = config["Kbo:Share"]
            ?? throw new InvalidOperationException("Configure Kbo:Database (a release .db file) or Kbo:Share (a directory with current.txt).");
        share = Path.Combine(env.ContentRootPath, share);
        var current = File.ReadAllText(Path.Combine(share, "current.txt")).Trim();
        return (Path.GetFullPath(Path.Combine(share, current)), false);
    }
}

/// <summary>Thrown when a request needs the release and there is none yet; answered with 503.</summary>
public sealed class NoReleaseException() : Exception("No KBO release is available yet.");
