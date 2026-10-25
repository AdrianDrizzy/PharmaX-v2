using System.Reflection;
using System.Runtime.InteropServices;
using FluentAssertions;
using PharmaX.Desktop.Features.Admin;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// A new account's username has to be editable, including Backspace.
/// An existing account's username stays fixed.
/// </summary>
public class UserEditFormTests
{
    [Fact]
    public void Backspace_deletes_a_character_from_a_new_username()
    {
        using var form = new frmUserEdit();
        form.LoadUser(null);
        Load(form);

        var box = (TextBox)form.Controls.Find("txtUsername", true).Single();

        box.ReadOnly.Should().BeFalse();
        box.ShortcutsEnabled.Should().BeTrue();
        box.Text = "tmoyo";
        box.Focus();
        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;

        var message = Message.Create(box.Handle, 0x0100, (IntPtr)Keys.Back,
            (IntPtr)0x000E0001);
        var args = new object[] { message };
        var swallowed = (bool)typeof(Control)
            .GetMethod("PreProcessMessage")!
            .Invoke(box, args)!;

        swallowed.Should().BeFalse("Backspace belongs to the username box");

        var key = Message.Create(box.Handle, 0x0100, (IntPtr)Keys.Back, (IntPtr)0x000E0001);
        var keyArgs = new object[] { key };
        var eaten = (bool)typeof(Control).GetMethod("ProcessKeyMessage",
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(box, keyArgs)!;

        eaten.Should().BeFalse("the username box has to receive Backspace");

        SendMessage(box.Handle, 0x0102, (IntPtr)'\b', IntPtr.Zero);
        box.Text.Should().Be("tmoy");
    }

    [Fact]
    public void An_existing_username_stays_fixed()
    {
        using var form = new frmUserEdit();
        form.LoadUser(Guid.NewGuid());

        var box = (TextBox)form.Controls.Find("txtUsername", true).Single();

        box.ReadOnly.Should().BeTrue();
    }

    private static void Load(Form form) =>
        typeof(Form).GetMethod("OnLoad",
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new object[] { EventArgs.Empty });

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
