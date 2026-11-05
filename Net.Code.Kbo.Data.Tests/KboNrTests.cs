using System.ComponentModel;

using Net.Code.Kbo.Data.Service;

namespace Net.Code.Kbo.Data.Tests;

public class KboNrTests
{
    [Test]
    [Arguments("0403170701")]
    [Arguments("0403.170.701")]
    [Arguments("0403 170 701")]
    [Arguments("BE 0403.170.701")]
    public async Task TryParse_AcceptsAnyFormatWithTenDigits(string input)
    {
        var ok = KboNr.TryParse(input, out var kbonr);

        await Assert.That(ok).IsTrue();
        await Assert.That(kbonr.Value).IsEqualTo(403170701L);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("403170701")]      // 9 digits
    [Arguments("04031707011")]    // 11 digits
    [Arguments("not a number")]
    public async Task TryParse_RejectsInvalidInput(string? input)
    {
        await Assert.That(KboNr.TryParse(input, out _)).IsFalse();
    }

    [Test]
    public async Task Parse_ThrowsFormatException_OnInvalidInput()
    {
        await Assert.That(() => KboNr.Parse("123")).Throws<FormatException>();
    }

    [Test]
    public async Task ToString_Default_IsTenDigitsZeroPadded()
    {
        var kbonr = KboNr.Parse("0403.170.701");

        await Assert.That(kbonr.ToString()).IsEqualTo("0403170701");
    }

    [Test]
    public async Task ToString_F_IsDottedFormat()
    {
        var kbonr = KboNr.Parse("0403170701");

        await Assert.That(kbonr.ToString("F")).IsEqualTo("0403.170.701");
    }

    [Test]
    public async Task ToString_OfDefault_IsEmpty()
    {
        await Assert.That(default(KboNr).ToString()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task TypeConverter_RoundTripsFromString()
    {
        // Used by ASP.NET Core minimal API route binding (/companies/{id})
        var converter = TypeDescriptor.GetConverter(typeof(KboNr));

        var value = (KboNr)converter.ConvertFrom("0403 170 701")!;

        await Assert.That(value.Value).IsEqualTo(403170701L);
        await Assert.That(converter.ConvertTo(value, typeof(string))).IsEqualTo("0403.170.701");
    }
}
