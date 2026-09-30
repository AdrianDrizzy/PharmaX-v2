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

        Texts(rows).Should().NotContain(t => t.Contains("Riverside Pharmacy Ltd"),
            "the registered name is left off to keep the slip short");
    }

    [Fact]
    public void The_slip_follows_the_fiscal_tax_invoice_layout()
    {
        var texts = Texts(Rows(Sale(), Header(null)));

        texts.Should().ContainInOrder(
            "Fiscal Tax Invoice",
            "Cash Sale",
            "Trans No    : S-001-000042",
            "Date        : 11/09/2026 09:30",
            "Operator    : test.admin",
            "Till Number : 1",
            "Code      QTY     Unit Price Amount Inc");

        // The code and quantity at the unit price, then the description
        // and the amount under the total.
        texts.Should().Contain(t => t.StartsWith("PAN500    2 @", StringComparison.Ordinal)
                                    && t.EndsWith("34.95", StringComparison.Ordinal));

        var item = texts.Single(t => t.StartsWith("Panado 500mg 20s",
                                                  StringComparison.Ordinal));
        item.Should().EndWith("69.90");
        item.Length.Should().Be(39, "amounts end under the totals");

        texts.Should().Contain(AmountRow("Sub Total", "69.90"));
        texts.Should().Contain(AmountRow("VAT-USD : 15.0%", "9.12"));
        texts.Should().Contain(AmountRow("TOTAL-USD:", "69.90"));
        texts.Should().Contain(AmountRow("Cash USD", "100.00"));
        texts.Should().Contain(AmountRow("Change", "30.10"));
        texts[^1].Should().Be("Thank you");
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

        texts.Should().Contain(t => t.StartsWith("Ibuprofen 200mg 20s",
                                                 StringComparison.Ordinal)
                                    && t.EndsWith("1.00 NV", StringComparison.Ordinal));

        texts.Should().NotContain(t => t.StartsWith("Change",
                                                    StringComparison.Ordinal),
            "nothing was handed back");
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
            "Patient     : Tendai Moyo (F00012)",
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
        label.PadRight(39 - amount.Length) + amount;

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
        CurrencyCode: "USD");
}
