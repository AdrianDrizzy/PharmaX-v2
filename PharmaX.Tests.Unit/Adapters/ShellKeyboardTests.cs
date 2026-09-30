using System.Reflection;
using FluentAssertions;
using PharmaX.Desktop.Shell;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// The shell as the keyboard and a screen reader meet it.
/// </summary>
/// <remarks>
/// The end-to-end run could not press real keys without reaching into
/// the tester's own windows, so Ctrl+T was left unproven (ADM-BLK-H03)
/// and the tile names could only be read, not fixed, there. Both are
/// proven here instead, against the shell itself.
/// </remarks>
public class ShellKeyboardTests
{
    [Fact]
    public void Ctrl_T_opens_the_till_session()
    {
        using var shell = new frmMain();

        string? chosen = null;
        shell.MenuSelected += (_, key) => chosen = key;

        var handled = Press(shell, Keys.Control | Keys.T);

        handled.Should().BeTrue("the shortcut belongs to a menu entry");
        chosen.Should().Be("till");
    }

    [Theory]
    [InlineData(Keys.F2, "dispensing")]
    [InlineData(Keys.F4, "patients")]
    public void The_function_keys_open_their_screens(Keys key, string expected)
    {
        using var shell = new frmMain();

        string? chosen = null;
        shell.MenuSelected += (_, k) => chosen = k;

        Press(shell, key).Should().BeTrue();
        chosen.Should().Be(expected);
    }

    /// <remarks>
    /// Every tile was announced as the one before it: Collect as "Sell",
    /// Dispense as "Collect" (ADM-OBS-19).
    /// </remarks>
    [Fact]
    public void Each_home_tile_is_a_button_named_after_its_own_caption()
    {
        using var shell = new frmMain();

        var tiles = Tiles(shell).ToList();
        tiles.Should().NotBeEmpty();

        foreach (var tile in tiles)
        {
            var caption = tile.Controls.OfType<Label>()
                .OrderBy(l => l.Top).First().Text;

            tile.AccessibleName.Should().Be(caption);
            tile.AccessibilityObject.Role.Should().Be(AccessibleRole.PushButton);
            tile.TabStop.Should().BeTrue(caption);
        }

        tiles.Select(t => t.AccessibleName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void A_tile_pressed_through_its_accessible_object_opens_its_screen()
    {
        using var shell = new frmMain();

        string? chosen = null;
        shell.MenuSelected += (_, key) => chosen = key;

        var tile = Tiles(shell).First(t => t.Enabled);
        tile.AccessibilityObject.DoDefaultAction();

        chosen.Should().Be(tile.MenuKey);
    }

    private static IEnumerable<HomeTile> Tiles(Control root) =>
        root.Controls.Cast<Control>()
            .SelectMany(c => c is HomeTile tile
                ? new[] { tile }
                : Tiles(c));

    /// The key as the form receives it, through its command-key handling.
    private static bool Press(Form form, Keys keys)
    {
        // Addressed to the window, which is how the menu finds the form
        // whose shortcuts apply.
        var message = Message.Create(form.Handle, 0x0100 /* WM_KEYDOWN */,
                                     (IntPtr)(int)(keys & Keys.KeyCode), IntPtr.Zero);

        var method = typeof(Form).GetMethod("ProcessCmdKey",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var arguments = new object[] { message, keys };
        return (bool)method.Invoke(form, arguments)!;
    }
}
