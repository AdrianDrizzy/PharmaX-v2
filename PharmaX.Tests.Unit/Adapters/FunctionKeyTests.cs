using System.Reflection;
using FluentAssertions;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// Every button on every screen has a function key, written on it, and
/// the same key means the same thing from one screen to the next.
/// </summary>
public class FunctionKeyTests
{
    /// <remarks>
    /// Walks every screen in the application rather than a list of them,
    /// so a screen added later is held to the same rule without anyone
    /// adding it here.
    /// </remarks>
    [Fact]
    public void Every_button_on_every_screen_has_a_key()
    {
        var problems = new List<string>();
        var checkedForms = 0;

        foreach (var type in Screens())
        {
            PhxFormBase form;

            try
            {
                form = (PhxFormBase)Activator.CreateInstance(type)!;
            }
            catch (TargetInvocationException)
            {
                continue;   // needs its services even to be drawn
            }

            using (form)
            {
                checkedForms++;

                var map = form.FunctionKeyMap;

                foreach (var button in Buttons(form))
                {
                    if (!map.Values.Contains(button))
                        problems.Add($"{type.Name}: '{button.Text}' has no key");
                    else if (FunctionKeys.KeyWritten(button.Text) is null)
                        problems.Add($"{type.Name}: '{button.Text}' does not say its key");
                }
            }
        }

        checkedForms.Should().BeGreaterThan(30, "the screens have to be found");
        problems.Should().BeEmpty();
    }

    [Theory]
    [InlineData("&Add", Keys.F3)]
    [InlineData("&Edit", Keys.F4)]
    [InlineData("Re&fresh", Keys.F5)]
    [InlineData("&Save", Keys.F6)]
    [InlineData("&Issue", Keys.F6)]
    [InlineData("Take Payment", Keys.F6)]
    [InlineData("Re&move item", Keys.F7)]
    [InlineData("Deac&tivate", Keys.F7)]
    [InlineData("&Hold", Keys.F8)]
    [InlineData("C&hoose", Keys.F9)]
    [InlineData("&What prints", Keys.F10)]
    [InlineData("&Export to Excel", Keys.F11)]
    [InlineData("&Reprint labels", Keys.F12)]
    [InlineData("&Find script", Keys.F2)]
    [InlineData("&Print labels", Keys.F6)]
    [InlineData("&Log in", Keys.F6)]
    [InlineData("Take Out of &Use", Keys.F7)]
    [InlineData("Claim &form", Keys.F12)]
    public void A_button_takes_the_key_its_caption_means(string caption,
                                                          Keys key) =>
        FunctionKeys.Meaning(caption).Should().Be(key);

    [Theory]
    [InlineData("F6 Pay", Keys.F6)]
    [InlineData("&Preview (F12)", Keys.F12)]
    [InlineData("Esc Void line", Keys.Escape)]
    [InlineData("Shift+F3 Test", Keys.Shift | Keys.F3)]
    [InlineData("F&ind...", null)]
    public void A_key_already_on_a_caption_is_kept(string caption, Keys? key) =>
        FunctionKeys.KeyWritten(caption).Should().Be(key);

    [Fact]
    public void Close_takes_Escape_and_the_rest_share_the_free_keys()
    {
        using var form = new Screen("&Add", "&Save", "&Close", "Other one",
                                    "Other two");

        var map = form.FunctionKeyMap;

        map[Keys.F3].Text.Should().Be("F3 &Add");
        map[Keys.F6].Text.Should().Be("F6 &Save");
        map[Keys.Escape].Text.Should().Be("Esc &Close");
        form.CancelButton.Should().Be(map[Keys.Escape]);

        map.Values.Select(b => b.Text).Should().Contain(
            new[] { "F2 Other one", "F4 Other two" },
            "the leftovers take the first keys nobody has");
    }

    /// <remarks>
    /// Escape is the way out of the window. Where the screen already names
    /// its cancel button, that is the one it presses - not a "Cancel" that
    /// cancels a stock count.
    /// </remarks>
    [Fact]
    public void Escape_stays_with_the_screens_own_cancel_button()
    {
        using var form = new Screen("Cance&l", "&Close");
        var close = form.Controls.OfType<Button>().Single(b => b.Text == "&Close");
        form.CancelButton = close;

        var map = form.FunctionKeyMap;

        map[Keys.Escape].Should().BeSameAs(close);
        map[Keys.F7].Text.Should().Be("F7 Cance&l");
    }

    /// <remarks>
    /// A screen that renames a button later - Deactivate becoming
    /// Reactivate, Edit becoming View - keeps the key written on it.
    /// </remarks>
    [Fact]
    public void A_caption_changed_later_keeps_its_key()
    {
        using var form = new Screen("Deac&tivate");

        var button = form.FunctionKeyMap[Keys.F7];
        button.Text = "Reac&tivate";

        button.Text.Should().Be("F7 Reac&tivate");
    }

    [Fact]
    public void A_key_presses_its_button_only_when_it_can_be_pressed()
    {
        using var form = new Screen("&Save", "&Remove");
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-3000, -3000);
        form.ShowInTaskbar = false;
        form.Show();

        var saved = 0;
        var removed = 0;
        form.FunctionKeyMap[Keys.F6].Click += (_, _) => saved++;
        form.FunctionKeyMap[Keys.F7].Click += (_, _) => removed++;
        form.FunctionKeyMap[Keys.F7].Enabled = false;

        form.PressFunctionKey(Keys.F6).Should().BeTrue();
        form.PressFunctionKey(Keys.F7).Should().BeFalse();

        saved.Should().Be(1);
        removed.Should().Be(0, "a disabled button stays unpressed");
    }

    // ---- Steps -----------------------------------------------------------

    private static IEnumerable<Type> Screens() =>
        typeof(PhxFormBase).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(PhxFormBase)) && !t.IsAbstract)
            .Where(t => t.GetConstructor(Type.EmptyTypes) is not null)
            .Where(t => t.Name is not ("frmMain" or "frmPOS"));

    private static IEnumerable<Button> Buttons(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Button button) yield return button;

            foreach (var inner in Buttons(child)) yield return inner;
        }
    }

    private sealed class Screen : PhxFormBase
    {
        public Screen(params string[] captions)
        {
            var x = 10;

            foreach (var caption in captions)
            {
                Controls.Add(new Button
                {
                    Text = caption,
                    Location = new Point(x, 10),
                    Size = new Size(140, 30)
                });

                x += 150;
            }
        }
    }
}
