using Net.Code.Kbo.Data.Service;

namespace Net.Code.Kbo.Tests;

public class FtsMatchExpressionTests
{
    static string Build(string input) => CompanyService.BuildFtsMatchExpression(input);

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("a b c")]          // single letters are dropped
    [Arguments("12 123 12345")]   // only 4-digit numbers are kept
    public async Task ReturnsEmpty_WhenNoUsableTokens(string input)
    {
        await Assert.That(Build(input)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task LowercasesWordsAndAddsPrefixWildcard()
    {
        await Assert.That(Build("Bakkerij JANSSENS")).IsEqualTo("(bakkerij* OR janssens*)");
    }

    [Test]
    public async Task KeepsPostalCodesWithoutWildcard()
    {
        await Assert.That(Build("Gent 9000")).IsEqualTo("(gent* OR 9000)");
    }

    [Test]
    public async Task RemovesDuplicateTokens()
    {
        await Assert.That(Build("Gent gent GENT")).IsEqualTo("(gent*)");
    }

    [Test]
    public async Task StripsPunctuationThatWouldBreakFtsSyntax()
    {
        await Assert.That(Build("\"foo\" AND (bar)*")).IsEqualTo("(foo* OR and* OR bar*)");
    }

    [Test]
    public async Task CapsAtTwelveTokens()
    {
        var input = string.Join(' ', Enumerable.Range(0, 20).Select(i => "w" + (char)('a' + i)));

        var tokens = Build(input).Trim('(', ')').Split(" OR ");

        await Assert.That(tokens.Length).IsEqualTo(12);
    }
}
