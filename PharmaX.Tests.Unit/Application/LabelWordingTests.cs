using FluentAssertions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Reference;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// What a label says in words: directions a patient can follow, and the
/// warnings that go with the medicine.
/// </summary>
public class LabelWordingTests
{
    [Theory]
    [InlineData("Take 2 tablets three times a day after meals", "Take TWO tablets THREE times a day after meals")]
    [InlineData("take 1 tablet twice a day", "Take ONE tablet TWICE a day")]
    [InlineData("One capsule three times a day", "Take ONE capsule THREE times a day")]
    [InlineData("1 tablet 3 times a day after food", "Take ONE tablet THREE times a day after food")]
    [InlineData("Take 1-2 tablets every 6 hours when required", "Take ONE to TWO tablets every SIX hours when required")]
    [InlineData("Take ½ tablet once a day", "Take HALF a tablet ONCE a day")]
    [InlineData("10 ml three times a day", "Take 10 ml THREE times a day")]
    [InlineData("2 drops in each eye four times a day", "Use TWO drops in each eye FOUR times a day")]
    [InlineData("Apply twice a day", "Apply TWICE a day")]
    public void Directions_are_in_plain_words_with_the_dose_and_how_often_in_capitals(
        string typed, string printed) =>
        LabelWording.Directions(typed).Should().Be(printed);

    [Fact]
    public void Directions_already_in_plain_words_are_left_as_they_are()
    {
        const string plain = "Take TWO tablets THREE times a day after meals";

        LabelWording.Directions(plain).Should().Be(plain);
    }

    [Fact]
    public void Rsi_shorthand_reads_in_plain_words_on_the_label()
    {
        var codes = DosageShorthand.Standard.ToDictionary(c => c.Code, c => c.Meaning,
            StringComparer.OrdinalIgnoreCase);

        LabelWording.Directions(DosageShorthand.Expand("1 T TDS AF", codes))
            .Should().Be("Take ONE tablet THREE times a day after food");

        LabelWording.Directions(DosageShorthand.Expand("T2 BD PC", codes))
            .Should().Be("Take TWO tablets TWICE a day after meals");
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
