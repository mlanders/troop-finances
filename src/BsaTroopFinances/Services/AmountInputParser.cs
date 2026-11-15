using System.Globalization;

namespace BsaTroopFinances.Services;

public static class AmountInputParser
{
    private static readonly CultureInfo UsCulture = CultureInfo.GetCultureInfo("en-US");

    public static decimal Parse(string? text)
    {
        text ??= string.Empty;
        var decimalIndex = text.LastIndexOf('.');
        var fractionalDigits = decimalIndex >= 0
            ? text[(decimalIndex + 1)..].Count(char.IsDigit)
            : 0;

        if (decimalIndex >= 0 && fractionalDigits is > 0 and <= 2
            && decimal.TryParse(text, NumberStyles.Currency, UsCulture, out var explicitAmount))
        {
            return Math.Round(Math.Max(0, explicitAmount), 2, MidpointRounding.AwayFromZero);
        }

        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (digits.Length > 10)
        {
            digits = digits[..10];
        }

        return long.TryParse(digits, out var cents) ? cents / 100m : 0;
    }

    public static string Format(decimal amount) => amount.ToString("C2", UsCulture);

    public static decimal ParseSigned(string? text)
    {
        var normalized = text?.Trim() ?? string.Empty;
        var isNegative = normalized.StartsWith("-", StringComparison.Ordinal);
        var value = Parse(normalized.TrimStart('-'));
        return isNegative ? -value : value;
    }
}
