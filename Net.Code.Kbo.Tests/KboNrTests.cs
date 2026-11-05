namespace Net.Code.Kbo.Tests;

public class KboNrTests
{
    [Test]
    [Arguments("0403170701")]
    [Arguments("0403.170.701")]
    [Arguments("0403 170 701")]
    [Arguments("BE 0403.170.701")]
    public async Task TryParse_AcceptsAnyFormatWithTenDigits(string input)
    {
        await Assert.That(KboNr.TryParse(input, out var kbo)).IsTrue();
        await Assert.That(kbo.Value).IsEqualTo(403170701L);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("403170701")]
    [Arguments("04031707011")]
    [Arguments("not a number")]
    public async Task TryParse_RejectsInputWithoutExactlyTenDigits(string? input)
    {
        await Assert.That(KboNr.TryParse(input, out _)).IsFalse();
    }

    [Test]
    public async Task Parse_Throws_OnInvalidInput()
    {
        await Assert.That(() => KboNr.Parse("123")).Throws<FormatException>();
    }

    [Test]
    public async Task ToString_IsDotted_AndNIsTenDigits()
    {
        var kbo = KboNr.Parse("0403170701");

        await Assert.That(kbo.ToString()).IsEqualTo("0403.170.701");
        await Assert.That(kbo.ToString("N")).IsEqualTo("0403170701");
    }

    [Test]
    [Arguments("0403.170.701", true)]   // real numbers: 97 - (04031707 mod 97) = 01
    [Arguments("0200.065.765", true)]
    [Arguments("0403.170.702", false)]
    public async Task HasValidCheckDigits(string number, bool expected)
    {
        await Assert.That(KboNr.Parse(number).HasValidCheckDigits).IsEqualTo(expected);
    }

    [Test]
    [Arguments(403170701L, "0403.170.701")]
    [Arguments(2123456789L, "2.123.456.789")]
    [Arguments(9000006626L, "9.000.006.626")]
    public async Task EntityNumber_FormatsEnterprisesAndUnits(long value, string expected)
    {
        await Assert.That(EntityNumber.Format(value)).IsEqualTo(expected);
        await Assert.That(EntityNumber.Parse(expected)).IsEqualTo(value);
    }
}
