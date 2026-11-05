using System.Text.Json;
using Net.Code.Kbo;
using Net.Code.Kbo.Build;

// kbo build   -i <Full zip> -o <db>          build and validate a release database
// kbo release --share <dir> [--work <dir>]   the Container Apps job: build the newest incoming zip and publish it
// kbo get     -d <db> <number>               print a company document
// kbo search  -d <db> <text>                 print search results

return args switch
{
    ["build", .. var rest] when Option(rest, "-i") is { } zip && Option(rest, "-o") is { } db => Build(zip, db),
    ["release", .. var rest] when Option(rest, "--share") is { } share =>
        Releaser.Run(share, Option(rest, "--work") ?? Path.Combine(Path.GetTempPath(), "kbo-build"), new ValidationOptions(), ConsoleLog.Write),
    ["get", "-d", var db, var number] => Get(db, number),
    ["search", "-d", var db, .. var words] when words.Length > 0 => Search(db, string.Join(' ', words)),
    _ => Usage(),
};

static int Build(string zip, string db)
{
    ReleaseBuilder.Build(zip, db, ConsoleLog.Write);
    return Validator.Validate(db, new ValidationOptions(), ConsoleLog.Write).Count == 0 ? 0 : 1;
}

static int Get(string db, string number)
{
    if (!KboNr.TryParse(number, out var kbo)) { Console.Error.WriteLine($"'{number}' is not an enterprise number"); return 2; }
    var doc = CompanyStore.Open(db).Get(kbo);
    Console.WriteLine(doc is null ? "not found" : JsonSerializer.Serialize(doc, new JsonSerializerOptions(KboJson.Default.Options) { WriteIndented = true }));
    return doc is null ? 1 : 0;
}

static int Search(string db, string text)
{
    var store = CompanyStore.Open(db);
    var result = store.Search(new SearchQuery { Text = text });
    if (!result.Ranked) Console.WriteLine("(only very common words: results are not ranked)");
    if (result.IgnoredTerms.Count > 0) Console.WriteLine($"(ignored very common words: {string.Join(", ", result.IgnoredTerms)})");
    foreach (var c in result.Items) Console.WriteLine($"{c.Number}  {c.Name}  {c.Zip} {c.City?.Get("NL")}");
    return 0;
}

static string? Option(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static int Usage()
{
    Console.Error.WriteLine("""
        usage:
          kbo build   -i <KboOpenData_..._Full.zip> -o <kbo-NNNN.db>
          kbo release --share <dir> [--work <dir>]
          kbo get     -d <db> <enterprise number>
          kbo search  -d <db> <text>
        """);
    return 2;
}
