using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Net.Code.Kbo;

/// <summary>
/// Read-only access to a release database (kbo-&lt;extract&gt;.db). Thread-safe: every call opens its
/// own (pooled) connection. The file is opened as immutable, so it must never change while in use.
/// </summary>
public sealed class CompanyStore
{
    public const int MaxTake = 25;

    private readonly string connectionString;

    public ReleaseInfo Release { get; }
    public CodeBook Codes { get; }
    public long CompanyCount { get; }

    /// <summary>
    /// Free-text terms matching more than this fraction of all companies (e.g. "de", "bv") are dropped from
    /// ranked queries: scoring a million matches takes seconds (specs/azure-architecture.md §11).
    /// </summary>
    public double CommonTermFraction { get; }

    private CompanyStore(string connectionString, ReleaseInfo release, CodeBook codes, long companyCount, double commonTermFraction)
    {
        this.connectionString = connectionString;
        Release = release;
        Codes = codes;
        CompanyCount = companyCount;
        CommonTermFraction = commonTermFraction;
    }

    public static CompanyStore Open(string path, double commonTermFraction = 0.10)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("Release database not found.", full);
        var uri = "file:" + full.Replace('\\', '/').Replace("%", "%25").Replace("?", "%3f").Replace("#", "%23") + "?immutable=1";
        var connectionString = new SqliteConnectionStringBuilder { DataSource = uri, Mode = SqliteOpenMode.ReadOnly }.ToString();

        using var con = new SqliteConnection(connectionString);
        con.Open();

        var codes = new CodeBook();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT category, code, lang, description FROM code";
            using var r = cmd.ExecuteReader();
            while (r.Read()) codes.Add(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3));
        }

        ReleaseInfo release;
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT extract_number, snapshot_date, built_at, counts FROM release";
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new InvalidDataException($"{full} has no release row.");
            var counts = JsonSerializer.Deserialize(r.GetString(3), KboJson.Default.DictionaryStringInt64) ?? [];
            release = new ReleaseInfo(r.GetInt32(0), r.GetString(1), r.GetString(2), counts);
        }

        var companyCount = release.Counts.GetValueOrDefault("companies");
        return new CompanyStore(connectionString, release, codes, companyCount, commonTermFraction);
    }

    public CompanyDocument? Get(KboNr number)
    {
        using var con = OpenConnection();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT doc FROM company WHERE number = $n";
        cmd.Parameters.AddWithValue("$n", number.Value);
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize(json, KboJson.Default.CompanyDocument) : null;
    }

    public SearchResult Search(SearchQuery query, int skip = 0, int take = MaxTake)
    {
        take = Math.Clamp(take, 1, MaxTake);
        skip = Math.Max(0, skip);
        var terms = query.GetTerms();
        if (terms.Count == 0) return SearchResult.Empty;

        using var con = OpenConnection();
        // A term whose index entries cover more than CommonTermFraction of all companies is "common": expanding and
        // scoring it costs up to seconds without a prefix index ("de*", "ge*", "ver*"; §11). Common free-text terms are
        // dropped when other terms remain; otherwise common terms are matched exactly (no wildcard), which is cheap.
        var limit = (long)(CompanyCount * CommonTermFraction);
        var common = terms.Where(t => DocumentFrequency(con, t) > limit).ToHashSet();
        var specific = terms.Where(t => !common.Contains(t)).ToList();
        var ranked = specific.Count > 0;
        var dropped = ranked ? common.Where(t => t.Column is null).ToList() : [];
        var match = terms
            .Where(t => !dropped.Contains(t))
            .Select(t => common.Contains(t) ? t with { Prefix = false } : t)
            .ToList();

        using var cmd = con.CreateCommand();
        // Rank and limit inside the FTS subquery, then fetch only those summaries (3-4x faster than join-then-sort).
        cmd.CommandText = ranked
            ? """
              SELECT c.summary FROM
                (SELECT rowid, bm25(company_fts, 5.0, 1.0, 4.0, 2.5, 0.5) AS rank
                 FROM company_fts WHERE company_fts MATCH $q ORDER BY rank LIMIT $take OFFSET $skip) f
              JOIN company c ON c.number = f.rowid
              ORDER BY f.rank
              """
            : """
              SELECT c.summary FROM
                (SELECT rowid FROM company_fts WHERE company_fts MATCH $q LIMIT $take OFFSET $skip) f
              JOIN company c ON c.number = f.rowid
              """;
        cmd.Parameters.AddWithValue("$q", SearchQuery.ToMatch(match));
        cmd.Parameters.AddWithValue("$take", take);
        cmd.Parameters.AddWithValue("$skip", skip);

        var items = new List<CompanySummary>(take);
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                items.Add(JsonSerializer.Deserialize(r.GetString(0), KboJson.Default.CompanySummary)!);

        return new SearchResult(items, ranked, dropped.Select(t => t.Token).ToList());
    }

    /// <summary>Number of companies containing the term (prefix terms: summed over all matching words; an upper bound).</summary>
    private static long DocumentFrequency(SqliteConnection con, SearchTerm term)
    {
        using var cmd = con.CreateCommand();
        var word = term.IndexForm();
        if (term.Prefix)
        {
            cmd.CommandText = "SELECT coalesce(sum(doc), 0) FROM company_fts_vocab WHERE term >= $lo AND term < $hi";
            cmd.Parameters.AddWithValue("$lo", word);
            cmd.Parameters.AddWithValue("$hi", word[..^1] + (char)(word[^1] + 1));
        }
        else
        {
            cmd.CommandText = "SELECT coalesce(sum(doc), 0) FROM company_fts_vocab WHERE term = $t";
            cmd.Parameters.AddWithValue("$t", word);
        }
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private SqliteConnection OpenConnection()
    {
        var con = new SqliteConnection(connectionString);
        con.Open();
        return con;
    }
}

/// <param name="Ranked">False when the query only had very common words: they are matched exactly and results are in number order.</param>
/// <param name="IgnoredTerms">Very common words that were left out of a ranked query.</param>
public sealed record SearchResult(IReadOnlyList<CompanySummary> Items, bool Ranked, IReadOnlyList<string> IgnoredTerms)
{
    public static SearchResult Empty { get; } = new([], true, []);
}
