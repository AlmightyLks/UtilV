using UtilV.Views;
using Xunit;

namespace UtilV.Tests;

public class NumberInputTests
{
    [Theory]
    [InlineData("25", "25")]
    [InlineData("dfsdf", "")]
    [InlineData("1,000", "1000")]
    [InlineData("1.5", "15")]
    [InlineData("-5", "5")]
    [InlineData("12abc34", "1234")]
    [InlineData(" 42 ", "42")]
    [InlineData("١٢٣", "")]           // Arabic-Indic digits are digits, but not parseable here
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Only_ascii_digits_survive(string? input, string expected)
    {
        Assert.Equal(expected, Clipboard.DigitsOnly(input));
    }
}
