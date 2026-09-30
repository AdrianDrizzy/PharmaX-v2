using FluentAssertions;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Desktop.Controls;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// The suggestions offered under a search box, and the word-by-word
/// matching every search in PharmaX now shares.
/// </summary>
public class SearchSuggestionsTests
{
    [Fact]
    public void Matches_are_offered_once_two_letters_are_typed()
    {
        using var form = new Form();
        var box = Box(form);
        using var suggestions = new SearchSuggestions(box) { AssumeFocused = true };

        box.Text = "p";
        suggestions.Offer(Patients("Pillay", "Reshma"));
        suggestions.IsOpen.Should().BeFalse("one letter means nothing yet");

        box.Text = "pil";
        suggestions.Offer(Patients("Pillay", "Reshma"));
        suggestions.IsOpen.Should().BeTrue();
        suggestions.Showing.Should().ContainSingle();
    }

    [Fact]
    public void Nothing_is_offered_once_the_operator_has_left_the_box()
    {
        using var form = new Form();
        var box = Box(form);
        using var suggestions = new SearchSuggestions(box);

        box.Text = "pil";
        suggestions.Offer(Patients("Pillay", "Reshma"));

        suggestions.IsOpen.Should().BeFalse("the box does not have the focus");
    }

    [Fact]
    public void Down_highlights_and_Enter_takes_the_highlighted_match()
    {
        using var form = new Form();
        var box = Box(form);
        using var suggestions = new SearchSuggestions(box) { AssumeFocused = true };

        Suggestion? taken = null;
        suggestions.Chosen += (_, s) => taken = s;

        box.Text = "moy";
        suggestions.Offer(Patients("Moyo", "Tendai", "Moyana", "Chipo"));

        // Enter with nothing highlighted is left to the box and the form,
        // so a scanned barcode is never taken over.
        suggestions.Press(Keys.Enter).Should().BeFalse();

        suggestions.Press(Keys.Down).Should().BeTrue();
        suggestions.Press(Keys.Down).Should().BeTrue();
        suggestions.Press(Keys.Enter).Should().BeTrue();

        taken!.Text.Should().Be("Moyana, Chipo");
        suggestions.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Escape_closes_the_list_and_a_refresh_does_not_reopen_it()
    {
        using var form = new Form();
        var box = Box(form);
        using var suggestions = new SearchSuggestions(box) { AssumeFocused = true };

        box.Text = "moy";
        suggestions.Offer(Patients("Moyo", "Tendai"));
        suggestions.Press(Keys.Escape).Should().BeTrue();

        suggestions.Offer(Patients("Moyo", "Tendai"));
        suggestions.IsOpen.Should().BeFalse("closed for these words");

        box.Text = "moyo";
        suggestions.Offer(Patients("Moyo", "Tendai"));
        suggestions.IsOpen.Should().BeTrue("new words open it again");
    }

    [Fact]
    public void No_more_than_the_maximum_are_offered()
    {
        using var form = new Form();
        var box = Box(form);
        using var suggestions = new SearchSuggestions(box)
        {
            AssumeFocused = true,
            Maximum = 3
        };

        box.Text = "mo";
        suggestions.Offer(Patients("Moyo", "A", "Moyo", "B", "Moyo", "C",
                                   "Moyo", "D"));

        suggestions.Showing.Should().HaveCount(3);
    }

    [Fact]
    public void A_patient_reads_surname_first_with_what_tells_them_apart()
    {
        var suggestion = Suggest.Patient(new PatientListItemDto(Guid.NewGuid(),
            "P-001-000003", "Pillay", "Reshma", "Reshma Pillay", 35,
            "084 555 0103", false, true, "28 Woodstock Road\nHarare"));

        suggestion.Text.Should().Be("Pillay, Reshma");
        suggestion.Detail.Should().Be(
            "P-001-000003   ·   084 555 0103   ·   28 Woodstock Road, Harare");
    }

    [Theory]
    [InlineData("Moyo Tendai", new[] { "Moyo", "Tendai" })]
    [InlineData("Moyo, Tendai", new[] { "Moyo", "Tendai" })]
    [InlineData("  amox   500 ", new[] { "amox", "500" })]
    [InlineData("", new string[0])]
    public void A_search_is_read_word_by_word(string term, string[] words) =>
        SearchTerms.Words(term).Should().Equal(words);

    [Theory]
    [InlineData("0771 234 567", "771234567")]
    [InlineData("+263 77 123 4567", "771234567")]
    [InlineData("123", null)]
    public void A_telephone_number_is_compared_on_its_last_nine_digits(
        string typed, string? compared) =>
        SearchTerms.Telephone(typed).Should().Be(compared);

    [Fact]
    public void Every_word_has_to_appear_somewhere()
    {
        SearchTerms.Matches("cimas amox", "AMOX50", "Amoxicillin 500mg",
            "Cimas Medical Aid Society").Should().BeTrue();

        SearchTerms.Matches("cimas insulin", "AMOX50", "Amoxicillin 500mg",
            "Cimas Medical Aid Society").Should().BeFalse();
    }

    // ---- Steps -----------------------------------------------------------

    private static TextBox Box(Form form)
    {
        var box = new TextBox { Location = new Point(10, 10), Width = 300 };
        form.ClientSize = new Size(800, 600);
        form.Controls.Add(box);
        return box;
    }

    /// Surname and first names, in pairs.
    private static IEnumerable<Suggestion> Patients(params string[] names)
    {
        for (var i = 0; i + 1 < names.Length; i += 2)
        {
            yield return Suggest.Patient(new PatientListItemDto(Guid.NewGuid(),
                $"P{i:000}", names[i], names[i + 1],
                $"{names[i + 1]} {names[i]}", 30, null, false, true));
        }
    }
}
