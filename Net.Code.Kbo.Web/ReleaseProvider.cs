namespace Net.Code.Kbo.Web;

/// <summary>
/// The release database being served, opened from:
/// <list type="bullet">
/// <item><c>Kbo:Database</c>: path to a kbo-&lt;extract&gt;.db (local development, tests), or</item>
/// <item><c>Kbo:Share</c>: the (mounted) share; serves the file named in its current.txt (Azure).</item>
/// </list>
/// Relative paths are resolved against the content root. The app starts without a release (first deployment,
/// empty share): <see cref="Current"/> is then null and opening is retried on demand, at most every
/// <c>Kbo:RetryInterval</c> (default 10 s). Once open, the release is kept: a newer one is picked up on the next
/// start (the app scales to zero, so that is the next cold start).
/// </summary>
public sealed class ReleaseProvider(IConfiguration config, IHostEnvironment env, ILogger<ReleaseProvider> log)
{
    private readonly Lock gate = new();
    private readonly TimeSpan retryInterval = config.GetValue("Kbo:RetryInterval", TimeSpan.FromSeconds(10));
    private CompanyStore? store;
    private DateTime nextAttempt = DateTime.MinValue;

    /// <summary>The open release, or null when there is none (yet).</summary>
    public CompanyStore? Current
    {
        get
        {
            if (Volatile.Read(ref store) is { } open) return open;
            lock (gate)
            {
                if (store is null && DateTime.UtcNow >= nextAttempt)
                {
                    try
                    {
                        Volatile.Write(ref store, Open());
                        log.LogInformation("Serving KBO extract {Extract} ({Companies:N0} companies)", store!.Release.ExtractNumber, store.CompanyCount);
                    }
                    catch (Exception e)
                    {
                        nextAttempt = DateTime.UtcNow + retryInterval;
                        log.LogWarning("No KBO release available yet: {Reason}", e.Message);
                    }
                }
                return store;
            }
        }
    }

    private CompanyStore Open()
    {
        var commonTermFraction = config.GetValue("Kbo:CommonTermFraction", 0.10);

        var database = config["Kbo:Database"];
        if (string.IsNullOrEmpty(database))
        {
            var share = config["Kbo:Share"]
                ?? throw new InvalidOperationException("Configure Kbo:Database (a release .db file) or Kbo:Share (a directory with current.txt).");
            share = Path.Combine(env.ContentRootPath, share);
            var current = File.ReadAllText(Path.Combine(share, "current.txt")).Trim();
            database = Path.Combine(share, current);
        }

        return CompanyStore.Open(Path.Combine(env.ContentRootPath, database), commonTermFraction);
    }
}

/// <summary>Thrown when a request needs the release and there is none yet; answered with 503.</summary>
public sealed class NoReleaseException() : Exception("No KBO release is available yet.");
