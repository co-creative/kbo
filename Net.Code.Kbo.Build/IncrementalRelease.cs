using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Net.Code.Kbo.Build;

/// <summary>Updates an unpublished copy of a release from its associated source snapshots.</summary>
static class IncrementalRelease
{
    public static void Build(
        string previousSource, string nextSource, string currentDatabase, string outputDatabase,
        IncrementalChanges changes, Action<string> log, CancellationToken cancellationToken)
    {
        if (changes.CodesChanged)
            throw new InvalidDataException("A changed codebook requires a Full rebuild.");

        var (previousNumber, affected, previous) = PreviousCompanies(previousSource, changes, log);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var next = ExtractModel.Load(nextSource, log);
        var nextAffected = next.AffectedCompanies(changes);
        if (!nextAffected.IsSubsetOf(affected))
            throw new InvalidDataException("The update affected an unexpected enterprise.");

        var current = next.Companies(affected).ToDictionary(c => c.Number);
        next.Counts["companies"] = next.Counts["enterprises"];
        cancellationToken.ThrowIfCancellationRequested();

        var copied = false;
        try
        {
            using (var input = new FileStream(currentDatabase, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var output = new FileStream(outputDatabase, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                copied = true;
                var buffer = new byte[131072];
                int read;
                while ((read = input.Read(buffer)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using var con = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = outputDatabase,
                Pooling = false
            }.ToString());
            con.Open();
            using (var check = con.CreateCommand())
            {
                check.CommandText = "SELECT extract_number FROM release";
                if (Convert.ToInt32(check.ExecuteScalar()) != previousNumber)
                    throw new InvalidDataException("The source snapshot does not match the current release.");
            }
            Execute(con, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY;");
            using (var tx = con.BeginTransaction())
            {
                using var deleteFts = Command(con, tx, """
                    INSERT INTO company_fts (company_fts, rowid, name, street, city, postal_code, activity)
                    VALUES ('delete', $p0, $p1, $p2, $p3, $p4, $p5)
                    """, 6);
                using var deleteCompany = Command(con, tx, "DELETE FROM company WHERE number = $p0", 1);
                using var insertCompany = Command(con, tx, "INSERT INTO company VALUES ($p0, $p1, $p2)", 3);
                using var insertFts = Command(con, tx, """
                    INSERT INTO company_fts (rowid, name, street, city, postal_code, activity)
                    VALUES ($p0, $p1, $p2, $p3, $p4, $p5)
                    """, 6);
                foreach (var number in affected.Order())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (previous.TryGetValue(number, out var old))
                    {
                        Run(deleteFts, number, old.Search.Name, old.Search.Street, old.Search.City,
                            old.Search.PostalCode, old.Search.Activity);
                        Run(deleteCompany, number);
                    }
                    if (current.TryGetValue(number, out var updated))
                    {
                        Run(insertCompany, number, KboJson.Serialize(updated.Summary), KboJson.Serialize(updated.Document));
                        Run(insertFts, number, updated.Search.Name, updated.Search.Street, updated.Search.City,
                            updated.Search.PostalCode, updated.Search.Activity);
                    }
                }

                using var release = Command(con, tx, """
                    UPDATE release SET extract_number = $p0, snapshot_date = $p1, built_at = $p2, counts = $p3
                    """, 4);
                Run(release, next.ExtractNumber, next.SnapshotDate, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    JsonSerializer.Serialize(next.Counts, KboJson.Default.DictionaryStringInt64));
                tx.Commit();
            }
            Execute(con, "ANALYZE; PRAGMA journal_mode=DELETE;");
            log($"updated {affected.Count:N0} enterprises for extract {next.ExtractNumber}");
        }
        catch
        {
            if (copied) File.Delete(outputDatabase);
            throw;
        }
    }

    private static (int Number, HashSet<long> Affected, Dictionary<long, BuiltCompany> Companies) PreviousCompanies(
        string path, IncrementalChanges changes, Action<string> log)
    {
        var model = ExtractModel.Load(path, log);
        var affected = model.AffectedCompanies(changes);
        return (model.ExtractNumber, affected, model.Companies(affected).ToDictionary(c => c.Number));
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
