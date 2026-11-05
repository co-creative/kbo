using static Net.Code.Kbo.Tests.KboFixture;

namespace Net.Code.Kbo.Tests;

[ClassDataSource<BuiltRelease>(Shared = SharedType.PerClass)]
public class ViewsTests(BuiltRelease release)
{
    CompanyView View(string number, string lang) =>
        CompanyView.Create(release.Store.Get(KboNr.Parse(number))!, release.Store.Codes, lang);

    [Test]
    public async Task Company_InDutch()
    {
        var v = View(Acme, "nl");

        await Assert.That(v.Name).IsEqualTo("Acme Software BV");
        await Assert.That(v.JuridicalForm).IsEqualTo("Naamloze vennootschap");
        await Assert.That(v.Addresses[0].Type).IsEqualTo("Adres van de zetel");
        await Assert.That(v.Addresses[0].Line).IsEqualTo("Meir 1, 2000 Antwerpen");
        await Assert.That(v.Names[0]).IsEqualTo(new NameView("Naam", "Nederlands", "Acme Software BV"));
        await Assert.That(v.Activities[0]).IsEqualTo(new ActivityView("Hoofdactiviteit", "BTW-activiteiten", "2025", "35110",
            "Productie van elektriciteit uit niet-hernieuwbare bronnen", IsMain: true));
        await Assert.That(v.Establishments[0].Name).IsEqualTo("Acme Gent");
    }

    [Test]
    public async Task UnknownLanguage_FallsBackToDutch()
    {
        await Assert.That(View(Acme, "xx").JuridicalForm).IsEqualTo("Naamloze vennootschap");
    }

    [Test]
    public async Task Name_PrefersRequestedLanguage_ThenAnyLegalName()
    {
        // the bakery's only legal name is French
        await Assert.That(View(Bakery, "nl").Name).IsEqualTo(BakeryName);
    }

    [Test]
    public async Task AddressLine_IncludesBox()
    {
        await Assert.That(View(Gamma, "fr").Addresses[0].Line).IsEqualTo("Grand-Place 5 box B, 8500 Courtrai");
    }

    [Test]
    public async Task StruckOffAddresses_SortLast()
    {
        var delta = View(Delta, "nl");

        await Assert.That(delta.Addresses.Single().StrikingOff).IsEqualTo("2020-01-01");
    }
}
