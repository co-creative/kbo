using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Net.Code.Kbo.Build;

sealed record SourceBinding(int Extract, string Hash, string FullMonth, int PreviousExtract = 0)
{
    public static string PathFor(string share, int extract) =>
        Path.Combine(share, ".state", $"source-{extract:0000}.zip");

    public static SourceBinding? Open(string share, int extract, Action<string> log, CancellationToken cancellationToken)
    {
        if (extract == 0) return null;
        var db = Path.Combine(share, Releaser.FileName(extract));
        var source = PathFor(share, extract);
        if (!File.Exists(db) || !File.Exists(source))
        {
            log("No associated source snapshot; a Full rebuild is required.");
            return null;
        }

        using var con = OpenImmutable(db);
        using var check = con.CreateCommand();
        check.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'incremental_state'";
        if (Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
        {
            log("The current release has no source binding; a Full rebuild is required.");
            return null;
        }
        using (var columns = con.CreateCommand())
        {
            columns.CommandText = "SELECT name FROM pragma_table_info('incremental_state')";
            using var columnReader = columns.ExecuteReader();
            var names = new HashSet<string>(StringComparer.Ordinal);
            while (columnReader.Read()) names.Add(columnReader.GetString(0));
            if (!new[] { "version", "extract_number", "source_sha256", "full_month", "previous_extract" }
                .All(names.Contains))
            {
                log("The current source binding schema is incompatible; a Full rebuild is required.");
                return null;
            }
        }
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT s.version, s.extract_number, s.source_sha256, s.full_month, r.extract_number, s.previous_extract
            FROM incremental_state s CROSS JOIN release r
            """;
        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || reader.GetInt32(0) != 1 || reader.GetInt32(1) != extract || reader.GetInt32(4) != extract)
        {
            log("The current source binding is incompatible; a Full rebuild is required.");
            return null;
        }
        var hash = reader.GetString(2);
        var month = reader.GetString(3);
        var previous = reader.GetInt32(5);
        if (reader.Read() || previous < 0 || previous >= extract || hash.Length != 64 || !hash.All(Uri.IsHexDigit) ||
            !DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            log("The current source binding is invalid; a Full rebuild is required.");
            return null;
        }
        if (!string.Equals(hash, HashFile(source, cancellationToken), StringComparison.Ordinal))
        {
            log("The current source snapshot does not match its release; a Full rebuild is required.");
            return null;
        }
        return new SourceBinding(extract, hash, month, previous);
    }

    public static void Write(string database, SourceBinding binding)
    {
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Pooling = false
        }.ToString());
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS incremental_state (
              version INTEGER NOT NULL, extract_number INTEGER NOT NULL,
              source_sha256 TEXT NOT NULL, full_month TEXT NOT NULL, previous_extract INTEGER NOT NULL
            );
            DELETE FROM incremental_state;
            INSERT INTO incremental_state VALUES (1, $extract, $hash, $month, $previous);
            """;
        cmd.Parameters.AddWithValue("$extract", binding.Extract);
        cmd.Parameters.AddWithValue("$hash", binding.Hash);
        cmd.Parameters.AddWithValue("$month", binding.FullMonth);
        cmd.Parameters.AddWithValue("$previous", binding.PreviousExtract);
        cmd.ExecuteNonQuery();
    }

    public static string HashFile(string path, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[131072];
        int read;
        while ((read = stream.Read(buffer)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static SqliteConnection OpenImmutable(string path)
    {
        var full = Path.GetFullPath(path);
        var uri = "file:" + full.Replace('\\', '/').Replace("%", "%25").Replace("?", "%3f").Replace("#", "%23") + "?immutable=1";
        var con = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = uri,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        con.Open();
        return con;
    }
}
