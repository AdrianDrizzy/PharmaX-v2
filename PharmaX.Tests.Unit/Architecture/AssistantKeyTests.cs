using System.Reflection;
using FluentAssertions;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Tests.Unit.Architecture;

/// <summary>
/// Ctrl+Shift+A opens Ask PharmaX from every screen - every screen is a
/// PhxFormBase - except where nobody has signed in, and Ask PharmaX itself.
/// </summary>
public class AssistantKeyTests
{
    [Fact]
    public void The_key_opens_Ask_PharmaX_over_an_ordinary_screen()
    {
        using var screen = new Desktop.Features.Reports.frmReports();
        IWin32Window? openedOver = null;
        screen.UseAssistant(owner => openedOver = owner);

        Press(screen, PhxFormBase.AssistantKey).Should().BeTrue();

        openedOver.Should().BeSameAs(screen);
    }

    [Theory]
    [InlineData(typeof(Desktop.Features.Login.frmLogin))]
    [InlineData(typeof(Desktop.Features.Assistant.frmAssistant))]
    public void Not_before_signing_in_nor_inside_Ask_PharmaX_itself(Type form)
    {
        using var screen = (PhxFormBase)Activator.CreateInstance(form)!;
        var opened = false;
        screen.UseAssistant(_ => opened = true);

        Press(screen, PhxFormBase.AssistantKey);

        opened.Should().BeFalse();
    }

    private static bool Press(Form form, Keys key)
    {
        var msg = new Message();
        var method = typeof(PhxFormBase).GetMethod("ProcessCmdKey",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(form, new object[] { msg, key })!;
    }
}
