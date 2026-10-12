using FluentAssertions;
using PharmaX.Application.Reference;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// Shorthand is written out in full on the label, and a code nobody has
/// defined is left exactly as it was typed.
/// </summary>
public class DosageShorthandTests
{
    private static readonly IReadOnlyDictionary<string, string> Codes =
        DosageShorthand.Standard.ToDictionary(c => c.Code, c => c.Meaning,
                                              StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("T1 TDS PC", "Take 1 tablet three times a day after meals")]
    [InlineData("t2 bd prn", "Take 2 tablets twice a day when required")]
    [InlineData("C1 NOCTE PO", "Take 1 capsule at night by mouth")]
    public void Shorthand_is_written_out(string typed, string label) =>
        DosageShorthand.Expand(typed, Codes).Should().Be(label);

    [Theory]
    [InlineData("T1 TDS PC", "Take 1 tablet three times a day after meals")]
    [InlineData("1 T TDS AF", "1 tablet three times a day after food")]
    [InlineData("2 CAP BD BF", "2 capsule twice a day before food")]
    public void Rsi_shorthand_is_written_out(string typed, string label) =>
        DosageShorthand.Expand(typed, Codes).Should().Be(label);

    [Theory]
    [InlineData("Apply to the affected area")]
    [InlineData("1 UF")]
    public void Words_and_undefined_codes_are_left_as_typed(string typed) =>
        DosageShorthand.Expand(typed, Codes).Should().Be(typed);

    [Fact]
    public void A_defined_code_mixed_with_an_undefined_one_expands_only_its_own()
    {
        DosageShorthand.Expand("T1 OD UF", Codes).Should().Be("Take 1 tablet once a day UF");
    }
}
