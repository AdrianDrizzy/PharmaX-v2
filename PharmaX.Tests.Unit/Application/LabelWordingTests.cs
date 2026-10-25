using FluentAssertions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Reference;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// What a label says in words: the directions as they were typed, and the
/// warnings that go with the medicine.
/// </summary>
public class LabelWordingTests
{
    /// <remarks>
    /// As the pharmacy's own labels print them: the figures as typed, the
    /// whole line in capitals.
    /// </remarks>
    [Theory]
    [InlineData("Take 2 tablets after meals three times daily for pain",
                "TAKE 2 TABLETS AFTER MEALS THREE TIMES DAILY FOR PAIN")]
    [InlineData("  take 1 tablet\r\n twice a day ", "TAKE 1 TABLET TWICE A DAY")]
    [InlineData(null, "")]
    public void Directions_print_as_typed_in_capitals(string? typed, string printed) =>
        LabelWording.Directions(typed).Should().Be(printed);

    [Fact]
    public void Shorthand_prints_written_out_on_the_label()
    {
        var codes = DosageShorthand.Standard.ToDictionary(c => c.Code, c => c.Meaning,
            StringComparer.OrdinalIgnoreCase);

        LabelWording.Directions(DosageShorthand.Expand("1 T TDS AF", codes))
            .Should().Be("1 TABLET THREE TIMES A DAY AFTER FOOD");

        LabelWording.Directions(DosageShorthand.Expand("T2 BD PC", codes))
            .Should().Be("TAKE 2 TABLETS TWICE A DAY AFTER MEALS");
    }

    [Theory]
    [InlineData("AMOXYCILLIN CAPSULES 500MG", "Finish the whole course, even if you feel better.")]
    [InlineData("CHLORPHENIRAMINE (VARICHEM) 4MG", "May cause drowsiness. Do not drive or use machines. Avoid alcohol.")]
    [InlineData("MEFENAMIC ACID 100ML", "Take with or after food.")]
    [InlineData("PANADO SYRUP", "Contains paracetamol. Do not take other medicines that contain paracetamol.")]
    [InlineData("GENTLE MAGIC CREAM", "For external use only. Do not swallow.")]
    [InlineData("MMT SUSP 200ML", "Shake the bottle well before use.")]
    [InlineData("METRONIDAZOLE 400MG TABLETS", "Do not drink alcohol while taking this, or for 2 days after.")]
    [InlineData("CHLORAMPHENICOL EYE DROPS 0.5%", "For the eyes only.")]
    public void A_medicine_carries_its_warning(string medicine, string warning) =>
        LabelWording.Warnings(medicine).Should().Contain(warning);

    [Fact]
    public void A_medicine_no_rule_speaks_to_still_carries_a_warning()
    {
        LabelWording.Warnings("COTTON WOOL 125G").Should().Equal(LabelWording.General);
    }

    [Fact]
    public void The_share_sentence_is_left_off()
    {
        LabelWording.Directions(
                "Take at once. Do not share this medicine with anyone.")
            .Should().Be("TAKE AT ONCE.");

        LabelWording.WithoutShareNotice(
                "Use only as directed. Do not share this medicine.")
            .Should().Be("Use only as directed.");

        string.Join(" ", LabelWording.Warnings("COTTON WOOL 125G"))
            .Should().NotContain("share");
    }

    [Fact]
    public void A_cream_is_not_told_to_be_taken_with_food()
    {
        var warnings = LabelWording.Warnings("DICLOFENAC EMULGEL 1%");

        warnings.Should().Contain("For external use only. Do not swallow.");
        warnings.Should().NotContain("Take with or after food.");
    }

    [Fact]
    public void A_teething_gel_is_not_called_external()
    {
        LabelWording.Warnings("TEEJEL GEL 10G")
            .Should().NotContain("For external use only. Do not swallow.");
    }

    [Fact]
    public void An_eye_ointment_says_eyes_only_not_external_use_as_well()
    {
        var warnings = LabelWording.Warnings("CHLORAMPHENICOL EYE OINTMENT 1%");

        warnings.Should().Contain("For the eyes only.");
        warnings.Should().NotContain("For external use only. Do not swallow.");
    }

    [Fact]
    public void No_more_warnings_than_a_label_holds()
    {
        LabelWording.Warnings("METRONIDAZOLE SUSPENSION 200MG/5ML")
            .Should().HaveCountLessThanOrEqualTo(LabelWording.MostWarnings)
            .And.StartWith("Do not drink alcohol while taking this, or for 2 days after.");
    }
}
