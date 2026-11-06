using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Net.Code.Kbo.Build;

/// <summary>Builds a release database (kbo-&lt;extract&gt;.db) from a KBO Full zip. Schema: specs/azure-architecture.md §5.</summary>
static class ReleaseBuilder
{
    public const string Schema = """
        CREATE TABLE release (
          extract_number INTEGER NOT NULL,
          snapshot_date  TEXT    NOT NULL,
          built_at       TEXT    NOT NULL,
          counts         TEXT    NOT NULL
        );
        CREATE TABLE code (
          category TEXT NOT NULL, code TEXT NOT NULL, lang TEXT NOT NULL, description TEXT NOT NULL,
          PRIMARY KEY (category, code, lang)
        ) WITHOUT ROWID;
        CREATE TABLE company (
          number  INTEGER PRIMARY KEY,   -- enterprise number as integer; also the FTS rowid
          summary TEXT NOT NULL,
          doc     TEXT NOT NULL
        );
        -- contentless; no prefix index (costs 1.8 GB for ~0.1 s on broad prefixes, §11)
        CREATE VIRTUAL TABLE company_fts USING fts5(
          name, street, city, postal_code, activity,
          content = '',
          tokenize = 'unicode61 remove_diacritics 2'
        );
        -- document frequency per word, used to drop very common words from queries
        CREATE VIRTUAL TABLE company_fts_vocab USING fts5vocab(company_fts, 'row');
        """;

    /// <returns>The counts stored in the release table.</returns>
    public static IReadOnlyDictionary<string, long> Build(string zipPath, string dbPath, Action<string> log)
    {
        var model = ExtractModel.Load(zipPath, log);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        if (File.Exists(dbPath)) File.Delete(dbPath);

        using (var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString()))
        {
            con.Open();
            Execute(con, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-262144;");
            Execute(con, Schema);

            using (var tx = con.BeginTransaction())
            {
                using var insertCode = Command(con, tx, "INSERT OR IGNORE INTO code VALUES ($p0, $p1, $p2, $p3)", 4);
                foreach (var (category, code, language, description) in model.CodeRows) Run(insertCode, category, code, language, description);
                tx.Commit();
            }

            using (var tx = con.BeginTransaction())
            {
                using var insertCompany = Command(con, tx, "INSERT INTO company VALUES ($p0, $p1, $p2)", 3);
                using var insertFts = Command(con, tx, "INSERT INTO company_fts (rowid, name, street, city, postal_code, activity) VALUES ($p0, $p1, $p2, $p3, $p4, $p5)", 6);
                long written = 0;
                foreach (var c in model.Companies())
                {
                    Run(insertCompany, c.Number, KboJson.Serialize(c.Summary), KboJson.Serialize(c.Document));
                    Run(insertFts, c.Number, c.Search.Name, c.Search.Street, c.Search.City, c.Search.PostalCode, c.Search.Activity);
                    if (++written % 500_000 == 0) log($"  written {written:N0}");
                }
                model.Counts["companies"] = written;
                tx.Commit();
            }
            log($"companies {model.Counts["companies"]:N0}");

            using (var tx = con.BeginTransaction())
            {
                using var insertRelease = Command(con, tx, "INSERT INTO release VALUES ($p0, $p1, $p2, $p3)", 4);
                Run(insertRelease, model.ExtractNumber, model.SnapshotDate, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    JsonSerializer.Serialize(model.Counts, KboJson.Default.DictionaryStringInt64));
                tx.Commit();
            }

            Execute(con, "INSERT INTO company_fts (company_fts) VALUES ('optimize');");
            Execute(con, "ANALYZE; PRAGMA journal_mode=DELETE;");
        }
        log($"done: {dbPath} ({new FileInfo(dbPath).Length / 1048576.0:N0} MB)");
        return model.Counts;
    }

    private static void Execute(SqliteConnection con, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteConnection con, SqliteTransaction tx, string sql, int parameters)
    {
        var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        for (var i = 0; i < parameters; i++) cmd.Parameters.Add(new SqliteParameter($"$p{i}", null));
        cmd.Prepare();
        return cmd;
    }

    private static void Run(SqliteCommand cmd, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++) cmd.Parameters[i].Value = values[i] ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }
}

/// <summary>Console logging with elapsed time and memory, for long-running builds.</summary>
static class ConsoleLog
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static void Write(string message)
    {
        var p = Process.GetCurrentProcess();
        Console.WriteLine($"[{Clock.Elapsed:hh\\:mm\\:ss}] {message} (memory {p.WorkingSet64 / 1048576:N0} MB, peak {p.PeakWorkingSet64 / 1048576:N0} MB)");
    }
}
