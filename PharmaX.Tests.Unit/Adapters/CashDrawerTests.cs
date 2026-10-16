using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PharmaX.Adapters.Devices;
using PharmaX.Application.Abstractions;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// Which printer the cash drawer kick is addressed to.
/// </summary>
/// <remarks>
/// The kick is five bytes of ESC/POS meant for a device. Sent to the
/// wrong printer it is not a drawer that fails to open quietly: on a PC
/// whose default printer is "Microsoft Print to PDF" it stopped every
/// cash sale with a Save-As dialog and left a zero-byte .pdf behind that
/// no reader would open. These tests pin down what PharmaX is allowed to
/// aim five raw bytes at.
/// </remarks>
public class CashDrawerTests
{
    private readonly IWorkstationSettings _settings =
        Substitute.For<IWorkstationSettings>();

    public CashDrawerTests() =>
        _settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<string?>(null));

    /// <remarks>
    /// The regression. A till with no printer configured has no drawer,
    /// and "no drawer" must never resolve to whatever printer Windows
    /// happens to call the default.
    /// </remarks>
    [Fact]
    public async Task A_till_with_no_printer_configured_has_no_drawer()
    {
        var drawer = Build(portOf: _ => "USB001");

        (await drawer.DrawerPrinterAsync()).Should().BeNull();
        (await drawer.IsAvailableAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Opening_a_drawer_that_is_not_configured_does_nothing()
    {
        var drawer = Build(portOf: _ => "USB001");

        // No printer is touched, and a sale that has already taken the
        // money is not interrupted by the attempt.
        var open = async () => await drawer.OpenAsync();

        await open.Should().NotThrowAsync();
    }

    [Fact]
    public async Task The_drawer_uses_the_printer_it_was_given()
    {
        Setting(WorkstationSettingKey.CashDrawerPrinter, "Till roll");

        (await Build(portOf: _ => "USB001").DrawerPrinterAsync())
            .Should().Be("Till roll");
    }

    /// <remarks>
    /// The drawer is wired through the receipt printer on almost every
    /// till, so that is the fallback when no drawer printer is named.
    /// </remarks>
    [Fact]
    public async Task The_drawer_falls_back_to_the_receipt_printer()
    {
        Setting(WorkstationSettingKey.ReceiptPrinter, "Epson TM-T20");

        (await Build(portOf: _ => "USB001").DrawerPrinterAsync())
            .Should().Be("Epson TM-T20");
    }

    [Fact]
    public async Task A_drawer_printer_of_its_own_beats_the_receipt_printer()
    {
        Setting(WorkstationSettingKey.CashDrawerPrinter, "Till roll");
        Setting(WorkstationSettingKey.ReceiptPrinter, "Office laser");

        (await Build(portOf: _ => "USB001").DrawerPrinterAsync())
            .Should().Be("Till roll");
    }

    /// <remarks>
    /// The second half of the same defect: a receipt printer set to
    /// "Microsoft Print to PDF" - which is the only thing to set it to
    /// on a machine with no till printer - would otherwise inherit the
    /// kick through the fallback above and produce the same dead file.
    /// </remarks>
    [Theory]
    [InlineData("PORTPROMPT:")]
    [InlineData("portprompt:")]
    [InlineData("FILE:")]
    [InlineData("nul:")]
    public async Task No_drawer_hangs_off_a_port_that_writes_to_a_file(string port)
    {
        Setting(WorkstationSettingKey.ReceiptPrinter, "Microsoft Print to PDF");

        var drawer = Build(portOf: _ => port);

        (await drawer.DrawerPrinterAsync()).Should().BeNull();
        (await drawer.IsAvailableAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task No_drawer_hangs_off_a_printer_on_another_computer()
    {
        Setting(WorkstationSettingKey.CashDrawerPrinter, @"\\TILL-2\EPSON");

        (await Build(portOf: _ => "USB001").DrawerPrinterAsync()).Should().BeNull();
    }

    /// <remarks>
    /// A settings store that will not answer is a till that cannot open
    /// its drawer, not a till that cannot sell (POS-014).
    /// </remarks>
    [Fact]
    public async Task A_settings_store_that_throws_leaves_the_sale_alone()
    {
        _settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns<Task<string?>>(_ =>
                     throw new InvalidOperationException("no database"));

        var drawer = Build(portOf: _ => "USB001");

        (await drawer.DrawerPrinterAsync()).Should().BeNull();

        var open = async () => await drawer.OpenAsync();
        await open.Should().NotThrowAsync();
    }

    private void Setting(string key, string value) =>
        _settings.GetAsync(key, Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<string?>(value));

    private EscPosCashDrawer Build(Func<string, string?> portOf) =>
        new(NullLogger<EscPosCashDrawer>.Instance, _settings, portOf);
}
