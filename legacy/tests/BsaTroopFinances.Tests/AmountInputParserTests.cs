using BsaTroopFinances.Services;

namespace BsaTroopFinances.Tests;

/// <summary>
/// Typed money amounts. Mirrors wwwroot/amount-input.js, a cash-register style box: digits fill in
/// from the cents end ("1250" is $12.50), unless the text already has a decimal point with one or
/// two digits after it.
/// </summary>
public class AmountInputParserTests
{
    [Theory]
    [InlineData("12.5", "12.50")]
    [InlineData("12.50", "12.50")]
    [InlineData("$1,234.56", "1234.56")]
    [InlineData("  7.05 ", "7.05")]
    public void An_amount_with_a_decimal_point_is_read_as_written(string text, string expected) =>
        Assert.Equal(decimal.Parse(expected), AmountInputParser.Parse(text));

    [Theory]
    [InlineData("1250", "12.50")]
    [InlineData("5", "0.05")]
    [InlineData("$12.345", "123.45")]   // a third decimal digit shifts everything up a place
    [InlineData("1.234", "12.34")]
    [InlineData("1,000", "10.00")]
    public void Otherwise_the_digits_fill_in_from_the_cents(string text, string expected) =>
        Assert.Equal(decimal.Parse(expected), AmountInputParser.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("$")]
    public void Nothing_usable_is_zero(string? text) => Assert.Equal(0m, AmountInputParser.Parse(text));

    [Fact]
    public void More_than_ten_digits_are_cut_off() =>
        Assert.Equal(12_345_678.90m, AmountInputParser.Parse("123456789012"));

    [Theory]
    [InlineData("-5.00")]
    [InlineData("($5.00)")]
    public void A_negative_amount_is_zero(string text) => Assert.Equal(0m, AmountInputParser.Parse(text));

    [Theory]
    [InlineData("-5.00", "-5.00")]
    [InlineData(" -1250", "-12.50")]
    [InlineData("5.00", "5.00")]
    public void Signed_amounts_keep_a_leading_minus(string text, string expected) =>
        Assert.Equal(decimal.Parse(expected), AmountInputParser.ParseSigned(text));

    [Fact]
    public void Amounts_are_shown_as_us_dollars() =>
        Assert.Equal("$1,234.50", AmountInputParser.Format(1234.5m));
}
