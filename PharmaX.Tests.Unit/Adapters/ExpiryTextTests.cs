using System.Globalization;
using FluentAssertions;
using PharmaX.Desktop.Features.Inventory;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// The expiry a person types into a batch cell or a delivery note, read
/// the same on every till whatever culture its currency gave it.
/// </summary>
public class ExpiryTextTests
{
    [Theory]
    [InlineData("31/12/2029", 2029, 12, 31)]
    [InlineData("1/2/2030", 2030, 2, 1)]
    [InlineData("2029-12-31", 2029, 12, 31)]
    [InlineData("12/2029", 2029, 12, 31)]
    [InlineData("02/2028", 2028, 2, 29)]
    [InlineData("06/07/2029", 2029, 7, 6)]
    public void Reads_day_first_or_a_month_on_a_dollar_till(string text, int year, int month, int day)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

        try
        {
            ExpiryText.TryParse(text, out var expiry).Should().BeTrue();
            expiry.Should().Be(new DateOnly(year, month, day));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Nonsense_is_refused()
    {
        ExpiryText.TryParse("13/13/2029", out _).Should().BeFalse();
        ExpiryText.TryParse("soon", out _).Should().BeFalse();
    }
}
