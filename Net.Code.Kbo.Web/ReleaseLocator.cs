namespace Net.Code.Kbo.Web;

/// <summary>
/// Finds the release database to serve:
/// <list type="bullet">
/// <item><c>Kbo:Database</c>: path to a kbo-&lt;extract&gt;.db (local development, tests), or</item>
/// <item><c>Kbo:Share</c>: the (mounted) share; serves the file named in its current.txt (Azure).</item>
/// </list>
/// Relative paths are resolved against the content root. Read once at startup: a new release is picked up
/// on the next start (the app scales to zero, so that is the next cold start).
/// </summary>
static class ReleaseLocator
{
    public static CompanyStore Open(IConfiguration config, IHostEnvironment env)
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
