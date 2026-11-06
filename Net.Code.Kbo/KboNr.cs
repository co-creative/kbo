using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Net.Code.Kbo;

/// <summary>
/// Belgian enterprise number (ondernemingsnummer / numéro d'entreprise), e.g. <c>0403.170.701</c>.
/// Parsing accepts any input with exactly 10 digits ("0403170701", "BE 0403.170.701", ...).
/// </summary>
public readonly record struct KboNr(long Value) : IParsable<KboNr>, IFormattable
{
    public static bool TryParse([NotNullWhen(true)] string? s, out KboNr result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        long value = 0;
        var digits = 0;
        foreach (var c in s)
        {
            if (!char.IsAsciiDigit(c)) continue;
            if (++digits > 10) return false;
            value = value * 10 + (c - '0');
        }
        if (digits != 10) return false;
        result = new KboNr(value);
        return true;
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out KboNr result) => TryParse(s, out result);

    public static KboNr Parse(string s, IFormatProvider? provider = null) =>
        TryParse(s, out var result) ? result : throw new FormatException($"'{s}' is not an enterprise number (10 digits expected).");

    /// <summary>The last two digits are 97 minus (first eight digits mod 97).</summary>
    public bool HasValidCheckDigits => Value != 0 && 97 - Value / 100 % 97 == Value % 100;

    /// <summary>Dotted form, e.g. <c>0403.170.701</c>.</summary>
    public override string ToString() => ToString("F", null);

    /// <summary><c>F</c> (default): <c>0403.170.701</c>; <c>N</c>: <c>0403170701</c>.</summary>
    public string ToString(string? format, IFormatProvider? formatProvider = null) => format switch
    {
        null or "" or "F" => EntityNumber.Format(Value),
        "N" => Value.ToString("0000000000", CultureInfo.InvariantCulture),
        _ => throw new FormatException($"Unknown KboNr format '{format}'.")
    };
}

/// <summary>
/// Numbers of enterprises (<c>0403.170.701</c>), establishment units (<c>2.123.456.789</c>) and
/// branches (<c>9.000.006.626</c>) are all 10 digits, so they share one <see cref="long"/> key space:
/// enterprise numbers start with 0 or 1, establishments with 2-8 and branch ids with 9.
/// </summary>
public static class EntityNumber
{
    /// <summary>Digits only; separators are ignored.</summary>
    public static long Parse(string s)
    {
        long value = 0;
        foreach (var c in s)
            if (char.IsAsciiDigit(c)) value = value * 10 + (c - '0');
        return value;
    }

    public static bool IsEnterprise(long value) => value < 2_000_000_000;

    public static string Format(long value) => IsEnterprise(value)
        ? $"{value / 1_000_000:0000}.{value / 1000 % 1000:000}.{value % 1000:000}"
        : $"{value / 1_000_000_000}.{value / 1_000_000 % 1000:000}.{value / 1000 % 1000:000}.{value % 1000:000}";
}
