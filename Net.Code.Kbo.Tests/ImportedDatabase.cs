using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Net.Code.Kbo.Data;
using Net.Code.Kbo.Data.Service;

using TUnit.Core.Interfaces;

namespace Net.Code.Kbo.Tests;

/// <summary>
/// Writes a tiny KBO-shaped CSV dataset to a temp folder, runs the full import pipeline
/// (relational tables + CompanyDocuments + FTS) into a temp SQLite file, and exposes
/// an <see cref="ICompanyService"/> on top of it. Shared by all tests in a class.
/// </summary>
public sealed class ImportedDatabase : IAsyncInitializer, IAsyncDisposable
{
    public const string Acme = "0403.170.701";
    public const string Bakery = "0200.065.765";
    public const string AcmeEstablishment = "2.123.456.789";

    private readonly string folder = Path.Combine(Path.GetTempPath(), "kbo-tests-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider? provider;

    public int ImportResult { get; private set; }

    /// <summary>Folder holding the CSV files (and data.db).</summary>
    public string Folder => folder;

    /// <summary>(Over)writes a CSV file in <see cref="Folder"/>.</summary>
    public void WriteCsv(string name, params string[] lines) =>
        File.WriteAllLines(Path.Combine(folder, name), lines);

    public int Import(Func<IImportService, int> action)
    {
        using var scope = provider!.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<IImportService>());
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(folder);
        WriteCsvFiles(folder);

        var connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, "data.db") }.ConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddImportService(connectionString);
        services.AddCompanyService(connectionString);
        provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<KboDataContext>().Database.EnsureCreated();
            ImportResult = scope.ServiceProvider.GetRequiredService<IImportService>().FullImport(folder, incremental: false);
        }

        return Task.CompletedTask;
    }

    public async Task<T> WithCompanyService<T>(Func<ICompanyService, Task<T>> action)
    {
        using var scope = provider!.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ICompanyService>());
    }

    public T Scalar<T>(string sql)
    {
        using var scope = provider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Net.Code.ADONet.IDb>();
        return db.Sql(sql).AsScalar<T>()!;
    }

    public async ValueTask DisposeAsync()
    {
        if (provider is not null) await provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        System.Data.SQLite.SQLiteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { /* best effort: SQLite may still hold a handle on Windows */ }
        catch (UnauthorizedAccessException) { }
    }

    private static void WriteCsvFiles(string folder)
    {
        void Write(string name, params string[] lines) =>
            File.WriteAllLines(Path.Combine(folder, name), lines);

        Write("meta.csv",
            "\"Variable\",\"Value\"",
            "\"SnapshotDate\",\"01-11-2026\"",
            "\"ExtractNumber\",\"999\"");

        Write("code.csv",
            "\"Category\",\"Code\",\"Language\",\"Description\"",
            "\"Language\",\"1\",\"NL\",\"Frans\"",
            "\"Language\",\"1\",\"FR\",\"Français\"",
            "\"Language\",\"2\",\"NL\",\"Nederlands\"",
            "\"Language\",\"2\",\"FR\",\"Néerlandais\"",
            "\"JuridicalSituation\",\"000\",\"NL\",\"Normale toestand\"",
            "\"JuridicalSituation\",\"000\",\"FR\",\"Situation normale\"",
            "\"TypeOfEnterprise\",\"2\",\"NL\",\"Rechtspersoon\"",
            "\"TypeOfEnterprise\",\"2\",\"FR\",\"Personne morale\"",
            "\"JuridicalForm\",\"014\",\"NL\",\"Naamloze vennootschap\"",
            "\"JuridicalForm\",\"014\",\"FR\",\"Société anonyme\"",
            "\"TypeOfAddress\",\"REGO\",\"NL\",\"Adres van de zetel\"",
            "\"TypeOfAddress\",\"REGO\",\"FR\",\"Adresse du siège\"",
            "\"TypeOfAddress\",\"BAET\",\"NL\",\"Adres van de vestigingseenheid\"",
            "\"TypeOfAddress\",\"BAET\",\"FR\",\"Adresse de l'unité d'établissement\"",
            "\"TypeOfDenomination\",\"001\",\"NL\",\"Naam\"",
            "\"TypeOfDenomination\",\"001\",\"FR\",\"Dénomination\"",
            "\"TypeOfDenomination\",\"003\",\"NL\",\"Commerciële naam\"",
            "\"TypeOfDenomination\",\"003\",\"FR\",\"Dénomination commerciale\"",
            "\"EntityContact\",\"ENT\",\"NL\",\"Entiteit\"",
            "\"EntityContact\",\"ENT\",\"FR\",\"Entité\"",
            "\"ContactType\",\"EMAIL\",\"NL\",\"E-mail\"",
            "\"ContactType\",\"EMAIL\",\"FR\",\"E-mail\"",
            "\"ActivityGroup\",\"001\",\"NL\",\"BTW-activiteiten\"",
            "\"ActivityGroup\",\"001\",\"FR\",\"Activités TVA\"",
            "\"Classification\",\"MAIN\",\"NL\",\"Hoofdactiviteit\"",
            "\"Classification\",\"MAIN\",\"FR\",\"Activité principale\"",
            "\"Nace2008\",\"62010\",\"NL\",\"Ontwerpen en programmeren van computerprogramma's\"",
            "\"Nace2008\",\"62010\",\"FR\",\"Programmation informatique\"",
            "\"Nace2008\",\"10711\",\"NL\",\"Industriële vervaardiging van brood\"",
            "\"Nace2008\",\"10711\",\"FR\",\"Boulangerie industrielle\"");

        Write("enterprise.csv",
            "\"EnterpriseNumber\",\"Status\",\"JuridicalSituation\",\"TypeOfEnterprise\",\"JuridicalForm\",\"JuridicalFormCAC\",\"StartDate\"",
            $"\"{Acme}\",\"AC\",\"000\",\"2\",\"014\",\"\",\"01-01-2001\"",
            $"\"{Bakery}\",\"AC\",\"000\",\"2\",\"014\",\"\",\"15-06-1995\"");

        Write("establishment.csv",
            "\"EstablishmentNumber\",\"StartDate\",\"EnterpriseNumber\"",
            $"\"{AcmeEstablishment}\",\"01-01-2005\",\"{Acme}\"");

        Write("denomination.csv",
            "\"EntityNumber\",\"Language\",\"TypeOfDenomination\",\"Denomination\"",
            $"\"{Acme}\",\"2\",\"001\",\"Acme Software\"",
            $"\"{Acme}\",\"2\",\"003\",\"Acme Labs\"",
            $"\"{Bakery}\",\"1\",\"001\",\"Boulangerie Dupont\"",
            $"\"{AcmeEstablishment}\",\"2\",\"003\",\"Acme Gent\"");

        Write("address.csv",
            "\"EntityNumber\",\"TypeOfAddress\",\"CountryNL\",\"CountryFR\",\"Zipcode\",\"MunicipalityNL\",\"MunicipalityFR\",\"StreetNL\",\"StreetFR\",\"HouseNumber\",\"Box\",\"ExtraAddressInfo\",\"DateStrikingOff\"",
            $"\"{Acme}\",\"REGO\",\"\",\"\",\"2000\",\"Antwerpen\",\"Anvers\",\"Meir\",\"Meir\",\"1\",\"\",\"\",\"\"",
            $"\"{Bakery}\",\"REGO\",\"\",\"\",\"1000\",\"Brussel\",\"Bruxelles\",\"Wetstraat\",\"Rue de la Loi\",\"16\",\"\",\"\",\"\"",
            $"\"{AcmeEstablishment}\",\"BAET\",\"\",\"\",\"9000\",\"Gent\",\"Gand\",\"Veldstraat\",\"Veldstraat\",\"10\",\"\",\"\",\"\"");

        Write("contact.csv",
            "\"EntityNumber\",\"EntityContact\",\"ContactType\",\"Value\"",
            $"\"{Acme}\",\"ENT\",\"EMAIL\",\"info@acme.example\"");

        Write("activity.csv",
            "\"EntityNumber\",\"ActivityGroup\",\"NaceVersion\",\"NaceCode\",\"Classification\"",
            $"\"{Acme}\",\"001\",\"2008\",\"62010\",\"MAIN\"",
            $"\"{Bakery}\",\"001\",\"2008\",\"10711\",\"MAIN\"");
    }
}
