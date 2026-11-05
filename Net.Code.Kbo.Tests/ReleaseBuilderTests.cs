using Microsoft.Data.Sqlite;
using static Net.Code.Kbo.Tests.KboFixture;

namespace Net.Code.Kbo.Tests;

/// <summary>The fixture zip built into a release database: counts, documents and search.</summary>
[ClassDataSource<BuiltRelease>(Shared = SharedType.PerClass)]
public class ReleaseBuilderTests(BuiltRelease release)
{
    CompanyStore Store => release.Store;
    CompanyDocument Doc(string number) => Store.Get(KboNr.Parse(number)) ?? throw new InvalidOperationException($"{number} not found");

    [Test]
    public async Task Counts_AndValidation()
    {
        var c = release.Counts;
        await Assert.That(c["enterprises"]).IsEqualTo(6);
        await Assert.That(c["companies"]).IsEqualTo(6);
        await Assert.That(c["establishments"]).IsEqualTo(1);
        await Assert.That(c["branches"]).IsEqualTo(1);
        await Assert.That(c["denominations"]).IsEqualTo(10);
        await Assert.That(c["contactsFaxSkipped"]).IsEqualTo(1);
        await Assert.That(c["orphanEntities"]).IsEqualTo(1);
        // 1 orphan of 8 entities exceeds the 0.1% limit; that is the only validation error
        await Assert.That(release.ValidationErrors.Count).IsEqualTo(1);
        await Assert.That(release.ValidationErrors[0]).Contains("without an enterprise");
    }

    [Test]
    public async Task Release_IsRecorded()
    {
        await Assert.That(Store.Release.ExtractNumber).IsEqualTo(502);
        await Assert.That(Store.Release.SnapshotDate).IsEqualTo("2026-10-03");
        await Assert.That(Store.CompanyCount).IsEqualTo(6);
    }

    [Test]
    public async Task Document_HasEnterpriseData_InCodes()
    {
        var acme = Doc(Acme);

        await Assert.That(acme.Number).IsEqualTo(Acme);
        await Assert.That(acme.JuridicalForm).IsEqualTo("014");
        await Assert.That(acme.StartDate).IsEqualTo("2001-01-01");
        await Assert.That(acme.Names!.Select(n => n.Value)).IsEquivalentTo(new[] { "Acme Software BV", "Acme Labs" });
        await Assert.That(acme.Addresses![0].City).IsEqualTo(new LocalizedText("Antwerpen", "Anvers"));
        await Assert.That(acme.Contacts!.Single()).IsEqualTo(new ContactEntry("EMAIL", "info@acme.example")); // FAX skipped
    }

    [Test]
    public async Task Document_NestsEstablishmentsAndBranches()
    {
        var acme = Doc(Acme);
        var gent = acme.Establishments!.Single();
        await Assert.That(gent.Number).IsEqualTo(AcmeGent);
        await Assert.That(gent.Names!.Single().Value).IsEqualTo("Acme Gent");
        await Assert.That(gent.Addresses!.Single().Street!.Nl).IsEqualTo("Veldstraat");
        await Assert.That(acme.Branches).IsNull();

        var bakery = Doc(Bakery);
        await Assert.That(bakery.Branches!.Single().Number).IsEqualTo(BakeryBranch);
        await Assert.That(bakery.Establishments).IsNull();
    }

    [Test]
    public async Task Document_KeepsOnlyNewestNaceVersion()
    {
        await Assert.That(Doc(Acme).Activities!).IsEquivalentTo(new[] { new ActivityEntry("001", "2025/35110", "MAIN"), new ActivityEntry("006", "2025/35110", "MAIN") });
        await Assert.That(Doc(Bakery).Activities!.Single().Nace).IsEqualTo("2008/10711"); // no 2025 code: falls back
    }

    [Test]
    public async Task Csv_QuotedValuesWithCommasAndDoubledQuotes()
    {
        await Assert.That(Doc(Bakery).Names!.Single().Value).IsEqualTo(BakeryName);
    }

    [Test]
    public async Task StruckOffAddress_IsKept_WithItsDate()
    {
        var address = Doc(Delta).Addresses!.Single();

        await Assert.That(address.StrikingOff).IsEqualTo("2020-01-01");
    }

    [Test]
    public async Task EmptyValues_AreOmittedFromJson()
    {
        using var con = new SqliteConnection($"Data Source={release.DbPath};Mode=ReadOnly;Pooling=false");
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT doc FROM company WHERE number = {KboNr.Parse(Acme).Value}";
        var json = (string)cmd.ExecuteScalar()!;

        await Assert.That(json).DoesNotContain("\"box\"");
        await Assert.That(json).DoesNotContain("null");
        await Assert.That(json).DoesNotContain("\"branches\"");
    }

    [Test]
    public async Task Summary_UsesRegisteredOffice()
    {
        var hit = Store.Search(new SearchQuery { Text = "acme" }).Items[0];

        await Assert.That(hit).IsEqualTo(new CompanySummary
        {
            Number = Acme, Name = "Acme Software BV", Zip = "2000", City = new LocalizedText("Antwerpen", "Anvers"),
            JuridicalForm = "014", Status = "AC", Establishments = 1,
        });
    }

    [Test]
    public async Task Get_ReturnsNull_ForUnknownNumber()
    {
        await Assert.That(Store.Get(KboNr.Parse("0999.999.999"))).IsNull();
    }

    [Test]
    public async Task CodeBook_DescribesCodes_WithLanguageFallback()
    {
        await Assert.That(Store.Codes.Describe("JuridicalForm", "014", "FR")).IsEqualTo("Société anonyme");
        await Assert.That(Store.Codes.Describe("JuridicalForm", "014", "DE")).IsEqualTo("Naamloze vennootschap");
        await Assert.That(Store.Codes.DescribeNace("2025/35110", "NL")).IsEqualTo("Productie van elektriciteit uit niet-hernieuwbare bronnen");
    }

    [Test]
    [Arguments("acme", Acme)]
    [Arguments("dupont", Bakery)]
    [Arguments("veldstraat", Acme)]              // establishment street
    [Arguments("leuven", Bakery)]                // branch city
    [Arguments("anvers", Acme)]                  // French city name
    [Arguments("9000", Acme)]                    // establishment postal code
    [Arguments("vervaardiging brood", Bakery)]   // 2003 NACE wording, not in the document
    [Arguments("elektriciteit", Acme)]
    [Arguments("boulang", Bakery)]               // prefix
    [Arguments("électricité", Acme)]             // diacritics in the query (FR activity description)
    public async Task Search_FindsCompany(string text, string expected)
    {
        var result = Store.Search(new SearchQuery { Text = text });

        await Assert.That(result.Items.Select(i => i.Number)).Contains(expected);
    }

    [Test]
    [Arguments("co-creative")]
    [Arguments("CO CREATIVE")]
    [Arguments("co-creat")]
    public async Task Search_LiteralName_RanksFirst(string text)
    {
        // without the name-phrase pass, "co" is dropped as common and plain BM25 prefers "Creative Creative Creative"
        var result = Store.Search(new SearchQuery { Text = text });

        await Assert.That(result.Items[0].Number).IsEqualTo(CoCreative);
    }

    [Test]
    public async Task Search_NamePhrase_RespectsFieldFilters()
    {
        var result = Store.Search(new SearchQuery { Text = "creative", PostalCode = "9000" });

        await Assert.That(result.Items.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Search_FieldFilters()
    {
        var result = Store.Search(new SearchQuery { Name = "acme", City = "gent" });

        await Assert.That(result.Items.Single().Number).IsEqualTo(Acme);
    }

    [Test]
    public async Task Search_IgnoresStruckOffAddresses()
    {
        await Assert.That(Store.Search(new SearchQuery { City = "brugge" }).Items.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Search_DropsVeryCommonWords_WhenOtherWordsRemain()
    {
        var result = Store.Search(new SearchQuery { Text = "bv gamma" });

        await Assert.That(result.Ranked).IsTrue();
        await Assert.That(result.IgnoredTerms).IsEquivalentTo(new[] { "bv" });
        await Assert.That(result.Items.Single().Number).IsEqualTo(Gamma);
    }

    [Test]
    public async Task Search_OnlyVeryCommonWords_ReturnsUnrankedResults()
    {
        var result = Store.Search(new SearchQuery { Text = "bv" });

        await Assert.That(result.Ranked).IsFalse();
        await Assert.That(result.Items.Select(i => i.Number)).IsEquivalentTo(new[] { Acme, Gamma, Delta });
    }

    [Test]
    public async Task Search_PagesAndCapsTake()
    {
        var all = Store.Search(new SearchQuery { Text = "bv" }, take: 100);
        var second = Store.Search(new SearchQuery { Text = "bv" }, skip: 1, take: 1);

        await Assert.That(all.Items.Count).IsEqualTo(3);
        await Assert.That(second.Items.Single().Number).IsEqualTo(all.Items[1].Number);
    }
}
