using Net.Code.Kbo.Data.Service;

namespace Net.Code.Kbo.Tests;

/// <summary>
/// End-to-end: CSV → SQLite import pipeline → ICompanyService queries.
/// </summary>
[ClassDataSource<ImportedDatabase>(Shared = SharedType.PerClass)]
public class ImportAndQueryTests(ImportedDatabase db)
{
    [Test]
    public async Task Import_PopulatesRelationalTables()
    {
        await Assert.That(db.ImportResult).IsEqualTo(0);
        await Assert.That(db.Scalar<int>("SELECT COUNT(*) FROM Enterprises")).IsEqualTo(2);
        await Assert.That(db.Scalar<int>("SELECT COUNT(*) FROM Establishments")).IsEqualTo(1);
        await Assert.That(db.Scalar<int>("SELECT COUNT(*) FROM Addresses")).IsEqualTo(3);
        await Assert.That(db.Scalar<int>("SELECT COUNT(*) FROM Denominations")).IsEqualTo(4);
        await Assert.That(db.Scalar<int>("SELECT COUNT(*) FROM Contacts")).IsEqualTo(1);
        await Assert.That(db.Scalar<int>("SELECT COUNT(*) FROM Activities")).IsEqualTo(2);
    }

    [Test]
    public async Task Import_BuildsCompanyDocumentsAndFtsIndex()
    {
        await Assert.That(db.Scalar<int>("SELECT COUNT(*) FROM CompanyDocuments")).IsEqualTo(2);
        await Assert.That(db.Scalar<int>("SELECT COUNT(*) FROM companies_locations_fts")).IsEqualTo(2);
    }

    [Test]
    public async Task GetCompany_ReturnsCompanyWithDescriptionsInRequestedLanguage()
    {
        var company = await db.WithCompanyService(s => s.GetCompany(KboNr.Parse(ImportedDatabase.Acme), "nl"));

        await Assert.That(company).IsNotNull();
        await Assert.That(company!.EnterpriseNumber).IsEqualTo(ImportedDatabase.Acme);
        await Assert.That(company.JuridicalForm).IsEqualTo("Naamloze vennootschap");
        await Assert.That(company.Names).Contains(new EntityName("name", "Acme Software"));
        await Assert.That(company.Names).Contains(new EntityName("commercialName", "Acme Labs"));
        await Assert.That(company.MainAddress.City).IsEqualTo("Antwerpen");
        await Assert.That(company.ContactInfo.Length).IsEqualTo(1);
        await Assert.That(company.ContactInfo[0].Value).IsEqualTo("info@acme.example");
        await Assert.That(company.Establishments.Length).IsEqualTo(1);
        await Assert.That(company.Establishments[0].Address.City).IsEqualTo("Gent");
        await Assert.That(company.Activities.Length).IsEqualTo(1);
    }

    [Test]
    public async Task GetCompany_UsesFrenchDescriptions_WhenRequested()
    {
        var company = await db.WithCompanyService(s => s.GetCompany(KboNr.Parse(ImportedDatabase.Acme), "fr"));

        await Assert.That(company!.JuridicalForm).IsEqualTo("Société anonyme");
        await Assert.That(company.MainAddress.City).IsEqualTo("Anvers");
    }

    [Test]
    public async Task GetCompany_ReturnsNull_ForUnknownNumber()
    {
        var company = await db.WithCompanyService(s => s.GetCompany(KboNr.Parse("0999.999.999"), "nl"));

        await Assert.That(company).IsNull();
    }

    [Test]
    public async Task StructuredSearch_FindsByNameAndPostalCode()
    {
        var results = await db.WithCompanyService(s =>
            s.SearchCompany(new EntityLookup { Name = "boulangerie", PostalCode = "1000" }, "fr"));

        await Assert.That(results.Length).IsEqualTo(1);
        await Assert.That(results[0].EnterpriseNumber).IsEqualTo(ImportedDatabase.Bakery);
    }

    [Test]
    [Arguments("acme", ImportedDatabase.Acme)]
    [Arguments("Dupont Bruxelles", ImportedDatabase.Bakery)]
    [Arguments("antw", ImportedDatabase.Acme)]           // prefix match on city
    [Arguments("boulangerie 1000", ImportedDatabase.Bakery)]
    public async Task FreeTextSearch_RanksExpectedCompanyFirst(string text, string expected)
    {
        var results = await db.WithCompanyService(s => s.SearchCompany(text, "nl"));

        await Assert.That(results.Length).IsGreaterThan(0);
        await Assert.That(results[0].EnterpriseNumber).IsEqualTo(expected);
    }

    [Test]
    public async Task FreeTextSearch_ReturnsEmpty_WhenNothingMatches()
    {
        var results = await db.WithCompanyService(s => s.SearchCompany("zzzqqq", "nl"));

        await Assert.That(results.Length).IsEqualTo(0);
    }
}
