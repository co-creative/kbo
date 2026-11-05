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
        // Without a prefix index, expanding and scoring a broad term costs up to seconds ("de*", "ge*", "co*"; §11).
        // Each term is therefore matched as a prefix if that is narrow enough, otherwise as an exact word if *that* is
        // narrow enough ("co" is a normal word: 10k companies, but "co*" covers 6M index entries), otherwise it is common.
        // Common free-text terms are dropped when other terms remain; if only common terms remain, they are matched
        // exactly and the results are not ranked.
        var limit = (long)(CompanyCount * CommonTermFraction);
        var modes = terms.ToDictionary(t => t, t => Classify(con, t, limit));
        var ranked = modes.Values.Any(m => m != TermMode.Common);
        var dropped = ranked ? terms.Where(t => t.Column is null && modes[t] == TermMode.Common).ToList() : [];
        var match = terms
            .Where(t => !dropped.Contains(t))
            .Select(t => modes[t] == TermMode.Prefix ? t : t with { Prefix = false })
            .ToList();

        if (!ranked)
        {
            var unranked = Query(con, """
                SELECT c.number, c.summary FROM
                  (SELECT rowid FROM company_fts WHERE company_fts MATCH $q LIMIT $take OFFSET $skip) f
                JOIN company c ON c.number = f.rowid
                """, SearchQuery.ToMatch(match), take, skip);
            return new SearchResult(unranked.Select(r => r.Summary).ToList(), false, []);
        }

        // 1. Names containing the typed words as a phrase, in order, common words included ("co-creative" → name:"co creative"*).
        //    Someone typing (part of) a literal name expects those first. Plain BM25 gets this wrong: it favours names that
        //    repeat a word, and penalizes big companies whose name column also holds all their establishments' names.
        //    So the phrase hits are ordered by how well the company's own (legal) name matches, then by BM25.
        // 2. Then the normal BM25 ranking over all columns. Both are cut at skip + take, merged, then paged.
        var window = skip + take;
        var rows = new List<(long Number, CompanySummary Summary)>(window);
        if (NamePhrase(terms, modes, match) is { } phrase)
        {
            var typed = string.Join(' ', terms.Where(t => t.Column is null).Select(t => t.IndexForm()));
            rows.AddRange(Ranked(con, phrase, Math.Max(window, PhraseCandidates))
                .OrderBy(r => NameCloseness(SearchQuery.Words(r.Summary.Name), typed)) // stable: keeps BM25 order within a group
                .Take(window));
        }
        if (rows.Count < window)
        {
            var seen = rows.Select(r => r.Number).ToHashSet();
            rows.AddRange(Ranked(con, SearchQuery.ToMatch(match), window).Where(r => !seen.Contains(r.Number)));
        }

        return new SearchResult(rows.Skip(skip).Take(take).Select(r => r.Summary).ToList(), true, dropped.Select(t => t.Token).ToList());
    }

    /// <summary>Phrase hits considered for name ordering; literal-name phrases rarely have more (bpost: 6, colruyt: 20).</summary>
    private const int PhraseCandidates = 200;

    /// <summary>
    /// How well a legal name (as index words) matches the typed words; lower is better. Whole words beat a partially
    /// typed last word ("gent": "Gent helpt Gent" before "Gentaur"):
    /// 0 equal · 1 starts with the words · 2 starts with them, last word partial · 3 contains the words ·
    /// 4 contains them, last word partial · 5 none (matched another name, e.g. an establishment's).
    /// </summary>
    internal static int NameCloseness(string legalName, string typed)
    {
        var name = " " + legalName + " ";
        return legalName == typed ? 0
            : name.StartsWith(" " + typed + " ", StringComparison.Ordinal) ? 1
            : name.StartsWith(" " + typed, StringComparison.Ordinal) ? 2
            : name.Contains(" " + typed + " ", StringComparison.Ordinal) ? 3
            : name.Contains(" " + typed, StringComparison.Ordinal) ? 4
            : 5;
    }

    /// <summary>
    /// <c>name : "w1 w2 … wn"*</c> for the free-text words, plus the field filters. Null when every free-text word is
    /// common (such a phrase says little and is expensive). The last word keeps its wildcard (someone still typing:
    /// "co-creat") when that prefix is narrow, or when other words anchor the phrase and it has at least 3 letters:
    /// <c>name:"co creat"*</c> takes 6 ms although "creat*" alone is broad.
    /// </summary>
    private static string? NamePhrase(IReadOnlyList<SearchTerm> terms, Dictionary<SearchTerm, TermMode> modes, IEnumerable<SearchTerm> match)
    {
        var words = terms.Where(t => t.Column is null).ToList();
        if (words.All(w => modes[w] == TermMode.Common)) return null;
        var last = words[^1];
        var wildcard = last.Prefix && (modes[last] == TermMode.Prefix || (words.Count > 1 && last.Token.Length >= 3));
        var phrase = $"name : \"{string.Join(' ', words.Select(w => w.Token))}\"{(wildcard ? "*" : "")}";
        var filters = match.Where(t => t.Column is not null).Select(t => t.ToFts());
        return string.Join(' ', filters.Prepend(phrase));
    }

    private enum TermMode { Prefix, Exact, Common }

    private static TermMode Classify(SqliteConnection con, SearchTerm term, long limit)
    {
        if (term.Prefix && !Exceeds(con, term, limit)) return TermMode.Prefix;
        return !Exceeds(con, term with { Prefix = false }, limit) ? TermMode.Exact : TermMode.Common;
    }

    // Rank and limit inside the FTS subquery, then fetch only those summaries (3-4x faster than join-then-sort).
    private static List<(long Number, CompanySummary Summary)> Ranked(SqliteConnection con, string match, int limit) =>
        Query(con, """
            SELECT c.number, c.summary FROM
              (SELECT rowid, bm25(company_fts, 5.0, 1.0, 4.0, 2.5, 0.5) AS rank
               FROM company_fts WHERE company_fts MATCH $q ORDER BY rank LIMIT $take OFFSET $skip) f
            JOIN company c ON c.number = f.rowid
            ORDER BY f.rank
            """, match, limit, 0);

    private static List<(long Number, CompanySummary Summary)> Query(SqliteConnection con, string sql, string match, int take, int skip)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$q", match);
        cmd.Parameters.AddWithValue("$take", take);
        cmd.Parameters.AddWithValue("$skip", skip);
        var rows = new List<(long, CompanySummary)>(take);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add((r.GetInt64(0), JsonSerializer.Deserialize(r.GetString(1), KboJson.Default.CompanySummary)!));
        return rows;
    }

    /// <summary>
    /// Whether the term's index entries (companies per matching word, summed over all words for a prefix) exceed
    /// <paramref name="limit"/>. Stops reading as soon as they do: "de*" is decided by its first word.
    /// </summary>
    private static bool Exceeds(SqliteConnection con, SearchTerm term, long limit)
    {
        using var cmd = con.CreateCommand();
        var word = term.IndexForm();
        if (term.Prefix)
        {
            cmd.CommandText = "SELECT doc FROM company_fts_vocab WHERE term >= $lo AND term < $hi";
            cmd.Parameters.AddWithValue("$lo", word);
            cmd.Parameters.AddWithValue("$hi", word[..^1] + (char)(word[^1] + 1));
        }
        else
        {
            cmd.CommandText = "SELECT doc FROM company_fts_vocab WHERE term = $lo";
            cmd.Parameters.AddWithValue("$lo", word);
        }
        long total = 0;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if ((total += r.GetInt64(0)) > limit) return true;
        return false;
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
