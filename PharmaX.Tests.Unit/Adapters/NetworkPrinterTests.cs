using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PharmaX.Adapters.Printing;
using PharmaX.Application.Abstractions;
using PharmaX.Persistence;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// Printers on the pharmacy's other computers: found through the shared
/// database, chosen like local ones, and never a cash drawer.
/// </summary>
public sealed class NetworkPrinterTests : IDisposable
{
    private static string? Healthy(string printer) => null;

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly IClock _clock = Substitute.For<IClock>();

    public NetworkPrinterTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    [Theory]
    [InlineData(@"\\TILL-2\EPSON", true, "TILL-2", "EPSON on TILL-2")]
    [InlineData(@"\\OFFICE-PC\HP LaserJet", true, "OFFICE-PC", "HP LaserJet on OFFICE-PC")]
    [InlineData("Zebra GK420d", false, null, "Zebra GK420d")]
    [InlineData(@"\\", false, null, @"\\")]
    public void A_printer_on_another_computer_is_named_by_it(string printer,
        bool network, string? computer, string described)
    {
        NetworkPrinters.IsNetworkPath(printer).Should().Be(network);
        NetworkPrinters.ComputerOf(printer).Should().Be(computer);
        NetworkPrinters.Describe(printer).Should().Be(described);
    }

    [Theory]
    [InlineData("EPSON TM-T20II Receipt", "EPSON TM-T20II Receipt")]
    [InlineData(@"Zebra: ""GK420d"" / labels", "Zebra- -GK420d- - labels")]
    public void A_share_name_holds_only_what_windows_allows(string printer,
                                                           string share) =>
        NetworkPrinters.ShareNameFor(printer).Should().Be(share);

    /// <remarks>
    /// Printer names go into the script that shares them, run as an
    /// administrator: a quote in a name must stay part of the name.
    /// </remarks>
    [Theory]
    [InlineData("EPSON", "'EPSON'")]
    [InlineData("Jo's printer", "'Jo''s printer'")]
    [InlineData("x\u2019; Remove-Item C:\\", "'x\u2019\u2019; Remove-Item C:\\'")]
    public void A_printer_name_is_only_ever_text_to_powershell(string name,
                                                               string quoted) =>
        NetworkPrinters.Quote(name).Should().Be(quoted);

    [Theory]
    [InlineData("PORTPROMPT:", "Microsoft Print To PDF", true)]
    [InlineData("SHRFAX:", "Microsoft Shared Fax Driver", true)]
    [InlineData("nul:", "Send to Microsoft OneNote 16 Driver", true)]
    [InlineData("Microsoft.Office.OneNote_16001_x64__8wekyb3d8bbwe",
                "Microsoft Software Printer Driver", true)]
    [InlineData("USB001", "EPSON TM-T20II Receipt5", false)]
    [InlineData("IP_192.168.1.20", "ZDesigner GK420d", false)]
    public void Pdf_writers_fax_and_onenote_are_not_offered_to_the_others(
        string port, string driver, bool skipped) =>
        NetworkPrinters.IsVirtual(port, driver).Should().Be(skipped);

    [Fact]
    public void Only_a_shared_printer_has_a_path_to_print_to()
    {
        new NetworkPrinter("TILL-2", "EPSON", "EPSON").Path
            .Should().Be(@"\\TILL-2\EPSON");

        new NetworkPrinter("TILL-2", "EPSON", null).Path.Should().BeNull();
    }

    [Fact]
    public void A_printer_on_another_computer_is_printed_to_like_a_local_one()
    {
        var choice = PrinterResolver.Resolve(@"\\TILL-2\EPSON", "receipt",
            new[] { "Office Laser", @"\\TILL-2\EPSON" }, "Office Laser", Healthy);

        choice.CanPrint.Should().BeTrue();
        choice.Printer.Should().Be(@"\\TILL-2\EPSON");
    }

    [Fact]
    public void A_computer_that_is_off_is_named_in_the_message()
    {
        var choice = PrinterResolver.Resolve(@"\\TILL-2\EPSON", "label",
            new[] { @"\\TILL-2\EPSON" }, null, _ => "cannot be reached");

        choice.CanPrint.Should().BeFalse();
        choice.Problem.Should().Contain("EPSON on TILL-2")
            .And.Contain("Check TILL-2 is switched on");
    }

    /// <remarks>
    /// A main computer with no printer of its own, set to print on a till's:
    /// when that till cannot be reached it is the till that is named, not
    /// "no printer is installed".
    /// </remarks>
    [Fact]
    public void A_network_printer_that_cannot_be_connected_says_which_computer()
    {
        var choice = PrinterResolver.Resolve(@"\\TILL-2\EPSON", "claim form",
            Array.Empty<string>(), null, Healthy);

        choice.CanPrint.Should().BeFalse();
        choice.Problem.Should().Contain("check TILL-2 is switched on")
            .And.NotContain("No printer is installed");
    }

    [Fact]
    public async Task Every_computer_finds_the_printers_the_others_reported()
    {
        await using var db = Database();
        var directory = new PrinterDirectoryStore(db, _clock);

        await directory.PublishAsync("till-2", new[]
        {
            new NetworkPrinter("till-2", "EPSON TM-T20", "EPSON TM-T20"),
            new NetworkPrinter("till-2", "Zebra", null)
        });

        await directory.PublishAsync("MAIN-PC", new[]
        {
            new NetworkPrinter("MAIN-PC", "HP LaserJet", "HP LaserJet")
        });

        var all = await directory.ListAsync();

        all.Select(p => (p.Computer, p.Name, p.Path)).Should().Equal(
            ("MAIN-PC", "HP LaserJet", @"\\MAIN-PC\HP LaserJet"),
            ("TILL-2", "EPSON TM-T20", @"\\TILL-2\EPSON TM-T20"),
            ("TILL-2", "Zebra", null));
    }

    /// <remarks>
    /// A computer reports what it has now, so a printer unplugged since
    /// drops out rather than being offered for ever.
    /// </remarks>
    [Fact]
    public async Task A_computer_that_reports_again_replaces_what_it_said()
    {
        await using var db = Database();
        var directory = new PrinterDirectoryStore(db, _clock);

        await directory.PublishAsync("TILL-2", new[]
            { new NetworkPrinter("TILL-2", "Old printer", "Old printer") });
        await directory.PublishAsync("TILL-2", new[]
            { new NetworkPrinter("TILL-2", "EPSON", "EPSON") });

        (await directory.ListAsync()).Select(p => p.Name).Should().Equal("EPSON");
    }

    /// <remarks>
    /// Kept beside the tills' own settings, under a scope of its own: a
    /// till's printer choice is not mistaken for a computer, nor the other
    /// way round.
    /// </remarks>
    [Fact]
    public async Task The_directory_and_a_tills_own_settings_stay_apart()
    {
        await using var db = Database();
        var options = new PharmaXOptions();
        var till = new WorkstationSettingStore(db, _clock, options);

        await till.SetAsync(WorkstationSettingKey.ReceiptPrinter, "EPSON");
        await new PrinterDirectoryStore(db, _clock).PublishAsync("TILL-1",
            new[] { new NetworkPrinter("TILL-1", "EPSON", "EPSON") });

        (await till.GetAsync(WorkstationSettingKey.ReceiptPrinter)).Should().Be("EPSON");
        (await new PrinterDirectoryStore(db, _clock).ListAsync()).Should().ContainSingle();
    }

    /// <remarks>
    /// Two dispensary computers set up as the same till: choosing the label
    /// printer on one used to hand the other a printer only the first had,
    /// and labels stopped there.
    /// </remarks>
    [Fact]
    public async Task Each_computer_keeps_its_own_printers_on_the_same_till()
    {
        await using var db = Database();
        var options = new PharmaXOptions();
        var first = new WorkstationSettingStore(db, _clock, options, "DISPENSARY1");
        var second = new WorkstationSettingStore(db, _clock, options, "DISPENSARY2");

        await first.SetAsync(WorkstationSettingKey.LabelPrinter, "SCRIPT");
        await second.SetAsync(WorkstationSettingKey.LabelPrinter, @"\\DISPENSARY1\SCRIPT");

        (await first.GetAsync(WorkstationSettingKey.LabelPrinter)).Should().Be("SCRIPT");
        (await second.GetAsync(WorkstationSettingKey.LabelPrinter))
            .Should().Be(@"\\DISPENSARY1\SCRIPT");

        // What is the till's rather than the computer's is still shared.
        await first.SetAsync(WorkstationSettingKey.Theme, "dark");
        (await second.GetAsync(WorkstationSettingKey.Theme)).Should().Be("dark");
    }

    /// A computer that has not chosen since keeps what the till had.
    [Fact]
    public async Task A_printer_chosen_before_choices_were_per_computer_still_reads()
    {
        await using var db = Database();
        var options = new PharmaXOptions();

        db.WorkstationSettings.Add(new WorkstationSetting
        {
            WorkstationCode = options.Workstation.TillNumber,
            Key = WorkstationSettingKey.ReceiptPrinter,
            Value = "EPSON",
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        (await new WorkstationSettingStore(db, _clock, options, "TILL-1")
                .GetAsync(WorkstationSettingKey.ReceiptPrinter))
            .Should().Be("EPSON");
    }

    private PharmaXDbContext Database()
    {
        var db = new PharmaXDbContext(new DbContextOptionsBuilder<PharmaXDbContext>()
            .UseSqlite(_connection).Options);

        db.Database.EnsureCreated();
        return db;
    }
}
