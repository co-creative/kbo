using Microsoft.Data.Sqlite;

namespace Net.Code.Kbo.Build;

sealed record ValidationOptions
{
    /// <summary>The build job has 8 GiB of ephemeral disk; 0502 is 4.46 GB.</summary>
    public long MaxBytes { get; init; } = 6L * 1024 * 1024 * 1024;
    public double MaxOrphanFraction { get; init; } = 0.001;
    public int Samples { get; init; } = 200;
    /// <summary>Fraction of sampled companies that must be found by searching their own name.</summary>
    public double MinNameHitRate { get; init; } = 0.9;
}

/// <summary>Checks a freshly built release before it is published (specs/azure-architecture.md §6 step 4).</summary>
static class Validator
{
    public static IReadOnlyList<string> Validate(string dbPath, ValidationOptions options, Action<string> log)
    {
        try { return ValidateCore(dbPath, options, log); }
        finally { SqliteConnection.ClearAllPools(); } // release file handles so the database can be copied/deleted
    }

    private static IReadOnlyList<string> ValidateCore(string dbPath, ValidationOptions options, Action<string> log)
    {
        var errors = new List<string>();
        var size = new FileInfo(dbPath).Length;
        if (size > options.MaxBytes) errors.Add($"database is {size:N0} bytes, limit {options.MaxBytes:N0}");

        var store = CompanyStore.Open(dbPath);
        var counts = store.Release.Counts;
        long Count(string key) => counts.GetValueOrDefault(key);

        if (Count("companies") == 0) errors.Add("no companies");
        if (Count("companies") != Count("enterprises")) errors.Add($"{Count("companies"):N0} companies written for {Count("enterprises"):N0} enterprises");
        var entityCount = Count("enterprises") + Count("establishments") + Count("branches");
        if (entityCount > 0 && Count("orphanEntities") > options.MaxOrphanFraction * entityCount)
            errors.Add($"{Count("orphanEntities"):N0} entities without an enterprise (limit {options.MaxOrphanFraction:P1})");
        if (store.Codes.Count == 0) errors.Add("no codes");

        // Sample companies spread over the number range: each must load, and searching its name must find it.
        var samples = SampleNumbers(dbPath, options.Samples);
        int named = 0, found = 0;
        foreach (var number in samples)
        {
            var doc = store.Get(new KboNr(number));
            if (doc is null) { errors.Add($"{EntityNumber.Format(number)} listed but not loadable"); continue; }
            if (doc.Names is not { Count: > 0 } names) continue;
            named++;
            var hits = store.Search(new SearchQuery { Name = names[0].Value }, take: CompanyStore.MaxTake);
            if (hits.Items.Any(i => i.Number == doc.Number)) found++;
        }
        if (named > 0 && found < options.MinNameHitRate * named)
            errors.Add($"only {found} of {named} sampled companies found by name (minimum {options.MinNameHitRate:P0})");

        log(errors.Count == 0
            ? $"validation passed: {size / 1048576.0:N0} MB, {Count("companies"):N0} companies, {found}/{named} sampled names found"
            : $"validation FAILED: {string.Join("; ", errors)}");
        return errors;
    }

    private static List<long> SampleNumbers(string dbPath, int samples)
    {
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT number FROM company WHERE rowid % max(1, (SELECT count(*) FROM company) / $n) = 0 LIMIT $n";
        cmd.Parameters.AddWithValue("$n", samples);
        var numbers = new List<long>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) numbers.Add(r.GetInt64(0));
        return numbers;
    }
}
