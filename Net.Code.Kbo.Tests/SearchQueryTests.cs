namespace Net.Code.Kbo.Tests;

public class SearchQueryTests
{
    static string Match(SearchQuery q) => SearchQuery.ToMatch(q.GetTerms());

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("a b c")]          // single letters are dropped
    [Arguments("12 123 12345")]   // only 4-digit numbers are kept
    public async Task NoTerms_WhenNothingUsable(string text)
    {
        await Assert.That(new SearchQuery { Text = text }.GetTerms().Count).IsEqualTo(0);
    }

    [Test]
    public async Task WordsAreLowercasedQuotedAndPrefixed_NumbersAreExact()
    {
        await Assert.That(Match(new SearchQuery { Text = "Bakkerij GENT 9000" })).IsEqualTo("\"bakkerij\"* \"gent\"* \"9000\"");
    }

    [Test]
    public async Task FtsSyntaxInInputIsNeutralized()
    {
        await Assert.That(Match(new SearchQuery { Text = "\"foo\" AND (bar)* NEAR/2 -baz" }))
            .IsEqualTo("\"foo\"* \"and\"* \"bar\"* \"near\"* \"baz\"*");
    }

    [Test]
    public async Task FieldsBecomeColumnFilters()
    {
        var q = new SearchQuery { Name = "Colruyt", Street = "Edingensesteenweg", City = "Halle", PostalCode = "1500" };

        await Assert.That(Match(q)).IsEqualTo("name:\"colruyt\"* street:\"edingensesteenweg\"* city:\"halle\"* postal_code:\"1500\"");
    }

    [Test]
    public async Task DuplicatesAreRemoved_AndTermsAreCapped()
    {
        await Assert.That(Match(new SearchQuery { Text = "Gent gent GENT" })).IsEqualTo("\"gent\"*");

        var many = string.Join(' ', Enumerable.Range(0, 20).Select(i => "w" + (char)('a' + i)));
        await Assert.That(new SearchQuery { Text = many }.GetTerms().Count).IsEqualTo(SearchQuery.MaxTerms);
    }

    [Test]
    public async Task IndexForm_RemovesDiacritics()
    {
        var term = new SearchQuery { Text = "Société Liège" }.GetTerms();

        await Assert.That(term.Select(t => t.IndexForm())).IsEquivalentTo(new[] { "societe", "liege" });
    }
}
