using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using PharmaX.Desktop.Controls;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// A filing list typed into: any word of an entry finds it, and what is
/// typed and not picked never leaves the product filed against nothing.
/// </summary>
public class WordFilterTests
{
    [Fact]
    public void Any_word_of_an_entry_narrows_the_list_and_one_match_is_taken()
    {
        using var form = new Form();
        var combo = Filing(form);

        Type(combo, "pain");
        combo.Items.Cast<object>().Should().Equal("Analgesics and pain");

        Settle(combo);
        combo.Items.Count.Should().Be(4, "the whole list comes back once settled");
        combo.SelectedItem.Should().Be("Analgesics and pain");
    }

    [Fact]
    public void Words_in_any_order_match_and_several_matches_keep_the_earlier_choice()
    {
        using var form = new Form();
        var combo = Filing(form);

        Type(combo, "pain anal");
        combo.Items.Cast<object>().Should().Equal("Analgesics and pain");

        Type(combo, "an");
        combo.Items.Cast<object>().Should().Equal("Analgesics and pain", "Antibiotics");

        Settle(combo);
        combo.SelectedItem.Should().Be("Antibiotics",
            "two entries fit, so the one chosen before the typing stands");
    }

    private static ComboBox Filing(Form form)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(new object[]
            { "(none)", "Analgesics and pain", "Antibiotics", "Vitamins" });
        form.Controls.Add(combo);
        form.CreateControl();

        combo.SelectedIndex = 2;
        WordFilter.Attach(combo);
        return combo;
    }

    /// As the keyboard does it: the text changes, then the box says so.
    private static void Type(ComboBox combo, string text)
    {
        combo.Text = text;
        Raise(combo, "OnTextUpdate", EventArgs.Empty);
    }

    /// As leaving the box, or pressing Save, does.
    private static void Settle(ComboBox combo) =>
        Raise(combo, "OnValidating", new CancelEventArgs());

    private static void Raise(Control control, string method, EventArgs e) =>
        typeof(ComboBox).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { e.GetType() }, null)!
            .Invoke(control, new object[] { e });
}
