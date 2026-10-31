using FluentAssertions;
using PharmaX.Application.Assistant.Guided;

namespace PharmaX.Tests.Unit.Assistant;

/// <summary>
/// Dosage shorthand written out for the label and counted, so the quantity
/// can be checked against the course.
/// </summary>
public class DirectionsTests
{
    [Theory]
    [InlineData("1 TDS 5/7", "capsule", "Take ONE capsule THREE times a day for 5 days", 15)]
    [InlineData("T2 BD x 7 days", "tablet", "Take TWO tablets TWICE a day for 7 days", 28)]
    [InlineData("C1 QID 2/52", "tablet", "Take ONE capsule FOUR times a day for 14 days", 56)]
    [InlineData("5ml TDS 5/7", "ml", "Take 5 ml THREE times a day for 5 days", 75)]
    [InlineData("1 OD 1/12", "tablet", "Take ONE tablet ONCE a day for 30 days", 30)]
    public void Shorthand_is_written_out_and_counted(string typed, string unit, string said, int needed)
    {
        var written = Directions.Read(typed, unit);

        written.Text.Should().Be(said);
        written.Needed.Should().Be(needed);
    }

    [Fact]
    public void When_required_has_no_course_to_count()
    {
        var written = Directions.Read("2 PRN", "tablet");

        written.Text.Should().Be("Take TWO tablets when required");
        written.Needed.Should().BeNull();
    }

    [Fact]
    public void Plain_words_are_kept_as_typed() =>
        Directions.Read("Apply thinly to the rash", "tablet").Should()
            .Be(new WrittenDirections("Apply thinly to the rash", null));

    [Theory]
    [InlineData("CAP 500", "AMOXYCILLIN 500MG", "capsule")]
    [InlineData("SYR 100", "PARACETAMOL SYRUP", "ml")]
    [InlineData("TAB 30", "IBUPROFEN 400MG", "tablet")]
    [InlineData(null, "Amoxicillin 500mg capsules 21s", "capsule")]
    public void The_unit_comes_from_the_pack(string? pack, string description, string unit) =>
        Directions.UnitOf(pack, description).Should().Be(unit);
}
