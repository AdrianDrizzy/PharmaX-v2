using FluentAssertions;
using PharmaX.Adapters.Printing;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// When a computer prints labels on its own label printer, and when on the
/// dispensary's: a printer on another computer is reached over the
/// network, a bare name only where it is installed.
/// </summary>
public class LabelPrinterChoiceTests
{
    [Fact]
    public void A_printer_shared_by_another_computer_is_reachable() =>
        LabelPrinter.Reachable(@"\\DISPENSARY1\SCRIPT").Should().BeTrue();

    [Fact]
    public void A_bare_name_not_installed_here_falls_to_the_dispensary_printer() =>
        LabelPrinter.Reachable("SCRIPT-" + Guid.NewGuid().ToString("N")).Should().BeFalse();

    [Fact]
    public void Nothing_chosen_falls_to_the_dispensary_printer()
    {
        LabelPrinter.Reachable(null).Should().BeFalse();
        LabelPrinter.Reachable("  ").Should().BeFalse();
    }
}
