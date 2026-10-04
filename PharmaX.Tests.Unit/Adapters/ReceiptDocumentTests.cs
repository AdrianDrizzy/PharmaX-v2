using FluentAssertions;
using PharmaX.Adapters.Printing;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// Draws a receipt onto a bitmap, and reads it line by line, to see what
/// the customer is handed.
/// </summary>
/// <remarks>
/// The only artefact PharmaX produces that a customer keeps and an
/// auditor reads. Asserting that the drawing code compiles proves
/// nothing about what reached the paper, and a roll printer is not
/// available to a test.
/// </remarks>
public class ReceiptDocumentTests : IDisposable
{
    private const int Width = 315;
    private const int Height = 900;

    private readonly string _logoPath = Path.Combine(
        Path.GetTempPath(), $"phx-logo-{Guid.NewGuid():N}.png");

    public ReceiptDocumentTests()
    {
        // A block of solid colour: if it were drawn, nothing else on the
        // slip would draw two hundred dark pixels in a row.
        using var image = new Bitmap(240, 80);
        using (var g = Graphics.FromImage(image)) g.Clear(Color.Black);

        image.Save(_logoPath, System.Drawing.Imaging.ImageFormat.Png);
    }

    public void Dispose()
    {
        if (File.Exists(_logoPath)) File.Delete(_logoPath);
        GC.SuppressFinalize(this);
    }

    /// <remarks>
    /// The slip is kept minimal: the logo costs roll and time on every
    /// sale, and belongs on the A4 claim form instead.
    /// </remarks>
    [Fact]
    public void A_receipt_prints_no_logo_even_when_the_pharmacy_has_one()
    {
        using var withLogo = Render(Header(_logoPath));
        using var without = Render(Header(logoPath: null));

        LongestDarkRun(withLogo, 0, 110).Should().BeLessThan(200,
            "nothing on the slip draws a solid band");

        LastInkRow(withLogo).Should().Be(LastInkRow(without),
            "the slip is the same length with or without a logo set");
    }

    [Fact]
    public void The_slip_keeps_the_pharmacy_name_and_address_at_the_head()
    {
        var rows = Rows(Sale(), Header(_logoPath));

        rows[0].Text.Should().Be("Riverside Pharmacy");
        rows[0].Style.Should().Be(ReceiptStyle.Title);
        rows[0].Centred.Should().BeTrue();

        rows[1].Text.Should().Be("1 Example Street");
        rows[2].Text.Should().Be("Harare");
        rows[3].Text.Should().Be("VAT No: 4123456789");

        // The numbers a customer rings about what is on the slip. They
        // were on no receipt at all, so a query about a purchase had
        // nowhere to go but back to the shop.
        rows[4].Text.Should().Be("Tel: +263 78 376 6260");
        rows[5].Text.Should().Be("WhatsApp: +263 77 123 4567");

        Texts(rows).Should().NotContain(t => t.Contains("Riverside Pharmacy Ltd"),
            "the registered name is left off to keep the slip short");
    }

    /// <remarks>
    /// A pharmacy that has given no number prints no label for one,
    /// rather than a receipt headed "Tel:" with nothing after it.
    /// </remarks>
    [Fact]
    public void A_pharmacy_with_no_numbers_prints_no_room_for_them()
    {
        var texts = Texts(Rows(Sale(),
            Header(null) with { Telephone = null, WhatsApp = "" }));

        texts.Should().NotContain(t => t.StartsWith("Tel:",
                                                    StringComparison.Ordinal));
        texts.Should().NotContain(t => t.StartsWith("WhatsApp:",
                                                    StringComparison.Ordinal));
    }

    [Fact]
    public void The_slip_follows_the_tax_invoice_layout()
    {
        var texts = Texts(Rows(Sale(), Header(null)));

        texts.Should().ContainInOrder(
            // Room for the customer's own registrations, which whoever
            // is reclaiming the VAT writes in at the counter.
            "Customer TIN    :",
            "Customer VAT No :",
            "TAX INVOICE (CASH)",
            "Cashier   : test.admin",
            "Date&Time : 11/09/2026 09:30  Till No: 1",
            "Inv RefNo : S-001-000042",
            "Pay Method: CASH  Tendered: 100.00",
            "Tendered: USD = 100.00",
            "CUSTOMER COPY",
            "ORIGINAL",
            "Description",
            "Code        Qty UnitPrice DiscPerc   Total");

        // The code and the description, then the figures under their
        // columns: how many, at what each, less what discount, for what.
        texts.Should().Contain("PAN500    Panado 500mg 20s");

        var figures = texts.Single(t => t.TrimStart()
            .StartsWith("2     34.95", StringComparison.Ordinal));

        figures.Should().EndWith("0.00   69.90");
        figures.Length.Should().Be(42, "the figures end at the right edge");

        texts.Should().Contain(AmountRow("SubTotal", "69.90"));
        texts.Should().Contain(AmountRow("Vat-USD : 15.0%", "9.12"));
        texts.Should().Contain(AmountRow("Total(Incl VAT)", "69.90"));
        texts.Should().Contain(AmountRow("Cash USD", "100.00"));

        // What was put down against what was owed, and how many things
        // were bought: the two lines a customer checks at the counter.
        texts.Should().Contain("Change for Tendered 100.00 = 30.10");
        texts.Should().Contain("Total Items: 2.00");
        texts[^1].Should().Be("Thank you");
    }

    /// <remarks>
    /// The heading names the tender because the commonest query at the
    /// counter is how something was paid for.
    /// </remarks>
    [Fact]
    public void The_heading_names_how_the_sale_was_paid()
    {
        var account = Sale() with
        {
            Payments = new[] { new PaymentDto("Account", 69.90m, null) },
            Totals = new SaleTotalsDto(69.90m, 0m, 9.12m, 69.90m, 0m, 0m)
        };

        Texts(Rows(account, Header(null))).Should()
            .Contain("TAX INVOICE (ACCOUNT)");

        var split = Sale() with
        {
            Payments = new[]
            {
                new PaymentDto("Cash", 40m, null),
                new PaymentDto("Card", 29.90m, "1234")
            },
            Totals = new SaleTotalsDto(69.90m, 0m, 9.12m, 69.90m, 0m, 0m)
        };

        Texts(Rows(split, Header(null))).Should()
            .Contain("TAX INVOICE (CASH/CARD)");
    }

    /// <remarks>
    /// The column exists because a discount given at the till is the
    /// thing a manager is most often asked to explain afterwards.
    /// </remarks>
    [Fact]
    public void A_discounted_line_shows_what_percentage_came_off()
    {
        var sale = Sale() with
        {
            Lines = new[]
            {
                new SaleLineDto(1, Guid.NewGuid(), "Panado 500mg 20s", 2m,
                    34.95m, 6.99m, "A", 62.91m, null, null, false, false, 15m,
                    "PAN500")
            },
            Totals = new SaleTotalsDto(69.90m, 6.99m, 8.21m, 62.91m, 62.91m, 0m),
            Payments = new[] { new PaymentDto("Cash", 62.91m, null) }
        };

        var texts = Texts(Rows(sale, Header(null)));

        texts.Should().Contain(t => t.EndsWith("10.00   62.91",
                                               StringComparison.Ordinal));

        texts.Should().Contain(AmountRow("Discount", "6.99"));
    }

    [Fact]
    public void A_line_charged_no_vat_is_marked_NV()
    {
        var sale = Sale() with
        {
            Lines = new[]
            {
                Line("IBU200", "Ibuprofen 200mg 20s", 1m, 1.00m, rate: 0m)
            },
            Totals = new SaleTotalsDto(1m, 0m, 0m, 1m, 1m, 0m),
            Payments = new[] { new PaymentDto("Cash", 1m, null) }
        };

        var texts = Texts(Rows(sale, Header(null)));

        texts.Should().Contain(t => t.Contains("Ibuprofen 200mg 20s",
                                               StringComparison.Ordinal)
                                    && t.EndsWith(" NV", StringComparison.Ordinal));

        texts.Should().Contain("Change for Tendered 1.00 = 0.00",
            "nothing was handed back, and the slip says so");
    }

    [Fact]
    public void A_collected_script_carries_its_reference()
    {
        var sale = Sale() with
        {
            Particulars = new ReceiptParticularsDto(null, null, "Tendai Moyo",
                "F00012", "13972", null, null)
        };

        Texts(Rows(sale, Header(null))).Should().Contain(new[]
        {
            "Patient   : Tendai Moyo (F00012)",
            "REF: Script No-13972"
        });
    }

    [Fact]
    public void A_reprint_says_copy_at_the_head_and_the_foot()
    {
        var texts = Texts(Rows(Sale(), Header(null) with { IsCopy = true }));

        texts.Count(t => t == "*** COPY - REPRINTED ***").Should().Be(2);
    }

    [Fact]
    public void Every_left_aligned_line_fits_the_roll()
    {
        var sale = Sale() with
        {
            Lines = new[]
            {
                Line("AVERYLONGPRODUCTCODE", "Metformin 850mg tablets 60s x 30 "
                     + "extended release film coated", 3m, 1234.5m, rate: 15m)
            }
        };

        Rows(sale, Header(null)).Where(r => !r.Centred)
            .Should().OnlyContain(r => r.Text.Length <= 42);
    }

    // ---- Steps -----------------------------------------------------------

    private static string AmountRow(string label, string amount) =>
        label.PadRight(42 - amount.Length) + amount;

    private static IReadOnlyList<ReceiptRow> Rows(SaleDto sale,
                                                  ReceiptHeaderDto header)
    {
        using var document = new ReceiptDocument(sale, header);
        return document.Rows();
    }

    private static List<string> Texts(IEnumerable<ReceiptRow> rows) =>
        rows.Select(r => r.Text).ToList();

    private static SaleLineDto Line(string code, string description,
        decimal quantity, decimal price, decimal rate) =>
        new(1, Guid.NewGuid(), description, quantity, price, 0m, "A",
            quantity * price, null, null, false, false, rate, code);

    private static SaleDto Sale() => new(Guid.NewGuid(), "S-001-000042",
        new[] { Line("PAN500", "Panado 500mg 20s", 2m, 34.95m, rate: 15m) },
        new SaleTotalsDto(69.90m, 0m, 9.12m, 69.90m, 100m, 30.10m),
        new[] { new PaymentDto("Cash", 100m, null) });

    private static Bitmap Render(ReceiptHeaderDto header)
    {
        var page = new Bitmap(Width, Height);

        using (var g = Graphics.FromImage(page))
        {
            g.Clear(Color.White);

            using var document = new ReceiptDocument(Sale(), header);
            document.RenderPage(g, new Rectangle(10, 10, Width - 20,
                                                 Height - 20));
        }

        return page;
    }

    private static int Ink(Bitmap page, int fromY, int toY)
    {
        var counted = 0;

        for (var y = fromY; y < Math.Min(toY, page.Height); y++)
        {
            for (var x = 0; x < page.Width; x++)
            {
                if (IsDark(page.GetPixel(x, y))) counted++;
            }
        }

        return counted;
    }

    /// The longest unbroken horizontal run of dark pixels in a band.
    private static int LongestDarkRun(Bitmap page, int fromY, int toY)
    {
        var longest = 0;

        for (var y = fromY; y < Math.Min(toY, page.Height); y++)
        {
            var run = 0;

            for (var x = 0; x < page.Width; x++)
            {
                run = IsDark(page.GetPixel(x, y)) ? run + 1 : 0;
                longest = Math.Max(longest, run);
            }
        }

        return longest;
    }

    private static int LastInkRow(Bitmap page)
    {
        for (var y = page.Height - 1; y >= 0; y--)
        {
            if (Ink(page, y, y + 1) > 0) return y;
        }

        return 0;
    }

    private static bool IsDark(Color pixel) =>
        pixel.R < 200 || pixel.G < 200 || pixel.B < 200;

    private static ReceiptHeaderDto Header(string? logoPath) => new(
        TradingName: "Riverside Pharmacy",
        RegisteredName: "Riverside Pharmacy Ltd",
        Address: "1 Example Street\nHarare",
        VatNumber: "4123456789",
        LicenceNumber: "P00123",
        BranchAndTill: "Branch 1 Till 1",
        OperatorName: "test.admin",
        PrintedAtLocal: new DateTime(2026, 9, 11, 9, 30, 0,
                                     DateTimeKind.Local),
        LogoPath: logoPath,
        TillNumber: "1",
        CurrencyCode: "USD",
        Telephone: "+263 78 376 6260",
        WhatsApp: "+263 77 123 4567");
}
