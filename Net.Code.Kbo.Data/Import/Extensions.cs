using Net.Code.ADONet;
using Net.Code.Csv;

namespace Net.Code.Kbo;

static class Extensions
{
    class TableInfo
    {
        public string name { get; set; } = null!;
    }

    extension(IDb db)
    {

        // the database contains no tables, or all tables are empty
        public bool IsEmpty
        {
            get 
            {
                const string sql = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
                var tables = db.Sql(sql).AsEnumerable<TableInfo>().ToList();
                foreach (var table in tables)
                {
                    var count = db.Sql($"SELECT COUNT(1) FROM {table.name};").AsScalar<int>();
                    if (count > 0) return false; // found a table with rows
                }
                return true; 
            }
        }

        public int? GetTableCount(string tableName)
        {
            const string sql = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name = @name;";
            var tables = db.Sql(sql).WithParameter("name", tableName).AsEnumerable<TableInfo>().ToList();
            if (tables.Count == 0) return null; // table does not exist
            return db.Sql($"SELECT COUNT(1) FROM {tableName};").AsScalar<int>();
        }

        /// <summary>
        /// Drops and recreates <paramref name="tableName"/> (empty, without indexes).
        /// Returns the table's explicit CREATE INDEX statements; run them after bulk-loading
        /// (building an index once is much faster than maintaining it during the insert).
        /// </summary>
        public IReadOnlyList<string> DropAndRecreate(string tableName)
        {
            var create = db.Sql($"SELECT sql FROM sqlite_master WHERE type = 'table' AND name = '{tableName}';").AsScalar<string>();
            if (string.IsNullOrEmpty(create))
                throw new InvalidOperationException($"Table {tableName} not found in database");
            var indexes = db.Sql($"SELECT sql FROM sqlite_master WHERE type = 'index' AND tbl_name = '{tableName}' AND sql IS NOT NULL;")
                .AsEnumerable<IndexInfo>()
                .Select(i => i.sql)
                .ToList();
            db.Sql($"DROP TABLE IF EXISTS {tableName}").AsNonQuery();
            db.Sql(create).AsNonQuery();
            return indexes;
        }
    }

    class IndexInfo
    {
        public string sql { get; set; } = null!;
    }
}