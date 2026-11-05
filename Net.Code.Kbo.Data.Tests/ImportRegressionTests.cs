using Net.Code.Kbo.Data.Service;

namespace Net.Code.Kbo.Data.Tests;

/// <summary>
/// Regression tests for import/search bugs found during the .NET 11 upgrade. Each test gets its own freshly imported database.
/// </summary>
[ClassDataSource<ImportedDatabase>(Shared = SharedType.None)]
public class ImportRegressionTests(ImportedDatabase db)
{
    // Was: ImportCodes built the "UPDATE CodeDescription ..." command but never executed it.
    [Test]
    public async Task ReimportingCodes_UpdatesChangedDescriptions()
    {
        var codeCsv = File.ReadAllLines(Path.Combine(db.Folder, "code.csv"))
            .Select(l => l.Replace("\"014\",\"NL\",\"Naamloze vennootschap\"", "\"014\",\"NL\",\"Naamloze vennootschap (gewijzigd)\""))
            .ToArray();
        db.WriteCsv("code.csv", codeCsv);

        // incremental: re-imports code.csv; other steps skip because no *_insert/_delete files exist
        db.Import(s => s.FullImport(db.Folder, incremental: true));

        var description = db.Scalar<string>("""
            SELECT cd.Description FROM CodeDescription cd JOIN Codes c ON c.Id = cd.CodeId
            WHERE c.Category = 'JuridicalForm' AND c.Code = '014' AND cd.Language = 'NL'
            """);
        await Assert.That(description).IsEqualTo("Naamloze vennootschap (gewijzigd)");
    }

    // Was: ImportFiles built paths as Path.Combine(folder, "denomination") (no ".csv") for every file except enterprise.
    [Test]
    public async Task ImportFiles_FullMode_ReimportsTheSelectedFile()
    {
        db.WriteCsv("denomination.csv",
            "\"EntityNumber\",\"Language\",\"TypeOfDenomination\",\"Denomination\"",
            $"\"{ImportedDatabase.Acme}\",\"2\",\"001\",\"Acme Software\"");

        db.Import(s => s.ImportFiles(db.Folder, ["denomination"], incremental: false));

        await Assert.That(db.Scalar<int>("SELECT COUNT(*) FROM Denominations")).IsEqualTo(1);
    }

    // Was: ImportBranches passed TypeOfEnterpriseId as key: "DELETE FROM Branches WHERE TypeOfEnterpriseId IN (...)".
    [Test]
    public async Task IncrementalBranchImport_Works()
    {
        db.WriteCsv("branch_delete.csv", "\"Id\"");
        db.WriteCsv("branch_insert.csv",
            "\"Id\",\"StartDate\",\"EnterpriseNumber\"",
            $"\"9.000.000.001\",01-01-2020,\"{ImportedDatabase.Bakery}\"");

        db.Import(s => s.ImportFiles(db.Folder, ["branch"], incremental: true));

        await Assert.That(db.Scalar<int>("SELECT COUNT(*) FROM Branches")).IsEqualTo(1);
    }

    // Was: delete keys were read as raw lines, so KBO's quoted values ("0200.065.765") kept their quotes and never matched.
    [Test]
    public async Task IncrementalDelete_HandlesQuotedKeys()
    {
        db.WriteCsv("enterprise_delete.csv", "\"EnterpriseNumber\"", $"\"{ImportedDatabase.Bakery}\"");
        db.WriteCsv("enterprise_insert.csv",
            "\"EnterpriseNumber\",\"Status\",\"JuridicalSituation\",\"TypeOfEnterprise\",\"JuridicalForm\",\"JuridicalFormCAC\",\"StartDate\"");

        db.Import(s => s.ImportFiles(db.Folder, ["enterprise"], incremental: true));

        await Assert.That(db.Scalar<int>("SELECT COUNT(*) FROM Enterprises")).IsEqualTo(1);
    }

    // Was: DropAndRecreate copied only the CREATE TABLE statement, so a full import silently dropped every
    // EF-defined index (e.g. Establishments.EnterpriseNumber), turning each company lookup into full table scans.
    [Test]
    [Arguments("Establishments", "IX_Establishments_EnterpriseNumber")]
    [Arguments("Branches", "IX_Branches_EnterpriseNumber")]
    [Arguments("Denominations", "IX_Denominations_EntityNumber")]
    [Arguments("Contacts", "IX_Contacts_EntityNumber")]
    [Arguments("Activities", "IX_Activities_EntityNumber")]
    [Arguments("Enterprises", "IX_Enterprises_JuridicalFormId")]
    public async Task FullImport_KeepsModelIndexes(string table, string index)
    {
        var count = db.Scalar<int>($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND tbl_name = '{table}' AND name = '{index}'");

        await Assert.That(count).IsEqualTo(1);
    }

    // Was: the addr CTE joined on m.EnterpriseNumber = a.EntityNumber, so establishment/branch addresses never reached the FTS index.
    [Test]
    public async Task FreeTextSearch_FindsCompanyByEstablishmentStreet()
    {
        var results = await db.WithCompanyService(s => s.SearchCompany("Veldstraat", "nl"));

        await Assert.That(results.Select(r => r.EnterpriseNumber)).Contains(ImportedDatabase.Acme);
    }
}
