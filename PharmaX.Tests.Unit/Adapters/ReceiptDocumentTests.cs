using System.Text;
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
    /// The 75 mm roll, in hundredths of an inch.
    private const int Width = 295;
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

    /// <remarks>
    /// The pharmacy's previous till slip, as photographed, line for line
    /// under the head. Its "Fiscal Tax Invoice" is "Tax Invoice" here,
    /// as PharmaX is not connected to a fiscal device; its unit price was
    /// the price of the whole pack, where PharmaX's is the price of one of
    /// what was sold; and the date is added, which a tax invoice has to
    /// carry.
    /// </remarks>
    [Fact]
    public void The_slip_reads_as_the_pharmacys_own_slip()
    {
        var sale = new SaleDto(Guid.NewGuid(), "31159",
            new[] { Line("1616", "MYPRODOL", 10m, 0.25m, rate: 0m) },
            new SaleTotalsDto(2.50m, 0m, 0m, 2.50m, 2.50m, 0m),
            new[] { new PaymentDto("Cash", 2.50m, null) });

        var texts = Texts(Rows(sale, Header(null) with { OperatorName = "test" }));

        texts.SkipWhile(t => !t.StartsWith('-')).Should().Equal(
            "--------------------------------",
            "Tax Invoice",
            "Cash Sale",
            "--------------------------------",
            "Trans No: 31159",
            "Till Operator : test",
            "Till Number : 1",
            "Date : 11/09/2026 09:30",
            "--------------------------------",
            "Code  QTY  Unit Price Amount Inc",
            "--------------------------------",
            "1616",
            "MYPRODOL",
            "     10 @        0.25    2.50 NV",
            "--------------------------------",
            "Sub Total                2.50",
            "VAT-USD :                0.00",
            "TOTAL-USD:               2.50",
            "--------------------------------",
            "Cash USD                 2.50",
            "--------------------------------",
            "Thank you");
    }

    /// <remarks>
    /// 75 mm, and as long as the slip. A fixed short page is what made
    /// the printer stop in the middle and wait for its feed button.
    /// </remarks>
    [Fact]
    public void The_roll_is_75_mm_and_as_long_as_the_slip()
    {
        ReceiptDocument.RollWidthHundredths.Should().Be(295);

        using var document = new ReceiptDocument(Sale(), Header(null));
        var height = ReceiptDocument.RollHeight(document.Rows().Count);

        height.Should().BeGreaterThan(document.Rows().Count * 14);
        height.Should().BeLessThan(1200,
            "a fixed 12-inch page is what the printer was stopping in");

        var sale = Sale() with
        {
            Lines = Enumerable.Range(1, 40)
                .Select(i => Line($"P{i}", $"Product number {i}", 1m, 1m, rate: 15m))
                .ToArray()
        };

        using var longer = new ReceiptDocument(sale, Header(null));

        ReceiptDocument.RollHeight(longer.Rows().Count).Should().BeGreaterThan(height);
    }

    [Fact]
    public void A_long_address_stays_whole_and_centred()
    {
        var header = Header(null) with
        {
            Address = "28 Woodstock Road, Thorngrove\nBulawayo\n(Along Luveve Road)"
        };

        var rows = Rows(Sale(), header);
        var address = rows.Where(row => row.Centred).Select(row => row.Text).ToList();

        address.Should().Contain("28 Woodstock Road, Thorngrove",
            "it fits the 32 columns, so it stays one line");
        address.Should().Contain("Bulawayo");
        address.Should().Contain("(Along Luveve Road)");
        rows[0].Text.Should().Be("Riverside Pharmacy");
        rows[^1].Text.Should().Be("Thank you");
    }

    /// <remarks>
    /// The slip is one block in the middle of the roll. Centred line by
    /// line, a total ending three columns short of an item's "NV" was
    /// moved a column and a half further right than the item's amount.
    /// </remarks>
    [Fact]
    public void The_amounts_line_up_under_each_other()
    {
        var rows = Rows(Sale(), Header(null));

        rows.Where(row => row.Text.StartsWith("Trans No", StringComparison.Ordinal)
                          || row.Text.StartsWith("Sub Total", StringComparison.Ordinal)
                          || row.Text.StartsWith('-'))
            .Should().OnlyContain(row => !row.Centred);

        using var page = Render(Header(null));

        // The block is centred: as much white either side of the rules.
        var (left, right) = InkAcross(page, RuleRow(page));
        Math.Abs(left - (Width - 1 - right)).Should().BeLessThanOrEqualTo(3);
    }

    /// <remarks>
    /// Thirty-two columns so the sizing can still reach 10 points. The
    /// slip itself is capped at 9, because 10 point ran off the 75 mm roll.
    /// </remarks>
    [Fact]
    public void The_type_is_larger_and_nothing_runs_off_the_roll()
    {
        using var page = new Bitmap(Width, Height);
        using var g = Graphics.FromImage(page);

        FixedPitch.Size(g, ReceiptDocument.CharactersPerLine, Width - 20, 10f)
            .Should().Be(10f, "the shorter line leaves room for 10 point type");

        using var slip = Render(Header(null));

        LastInkColumn(slip).Should().BeLessThan(Width - 10,
            "every amount ends inside the roll");
    }

    [Fact]
    public void A_rated_sale_gives_its_vat_rate_and_the_change()
    {
        var texts = Texts(Rows(Sale(), Header(null)));

        texts.Should().ContainInOrder(
            "PAN500",
            "Panado 500mg 20s",
            ItemRow("2", "34.95", "69.90"));

        texts.Should().Contain(AmountRow("VAT-USD : 15.0%", "9.12"));
        texts.Should().Contain(AmountRow("Cash USD", "100.00"));
        texts.Should().Contain(AmountRow("Change USD", "30.10"));
    }

    /// <remarks>
    /// The second line of the heading names the tender because the
    /// commonest query at the counter is how something was paid for.
    /// </remarks>
    [Fact]
    public void The_heading_names_how_the_sale_was_paid()
    {
        var account = Sale() with
        {
            Payments = new[] { new PaymentDto("Account", 69.90m, null) },
            Totals = new SaleTotalsDto(69.90m, 0m, 9.12m, 69.90m, 0m, 0m)
        };

        Texts(Rows(account, Header(null))).Should().Contain("Account Sale");

        var split = Sale() with
        {
            Payments = new[]
            {
                new PaymentDto("Cash", 40m, null),
                new PaymentDto("Card", 29.90m, "1234")
            },
            Totals = new SaleTotalsDto(69.90m, 0m, 9.12m, 69.90m, 0m, 0m)
        };

        Texts(Rows(split, Header(null))).Should().Contain("Cash/Card Sale");
    }

    /// <remarks>
    /// The amount is what the line came to after its discount, and the
    /// sub total is the sum of the amounts, so the slip adds up line by
    /// line to what was paid. The sub total used to be the sale's own,
    /// before its discounts.
    /// </remarks>
    [Fact]
    public void A_discounted_slip_adds_up()
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

        texts.Should().Contain(ItemRow("2", "34.95", "62.91"));
        texts.Should().Contain(AmountRow("Sub Total", "62.91"));
        texts.Should().Contain(AmountRow("TOTAL-USD:", "62.91"));
        texts.Should().NotContain(t => t.StartsWith("Discount", StringComparison.Ordinal));
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

        texts.Should().Contain(ItemRow("1", "1.00", "1.00") + " NV");
        texts.Should().NotContain(t => t.StartsWith("Change", StringComparison.Ordinal),
            "nothing was handed back");
    }

    /// <remarks>
    /// The code on its own line, the name under it, then the figures, so
    /// a long code or name never pushes a figure from under its head.
    /// </remarks>
    [Fact]
    public void Code_name_and_figures_each_take_a_line()
    {
        var sale = Sale() with
        {
            Lines = new[]
            {
                Line("AVERYLONGCODE", "Metformin 850mg tablets 60s extended release",
                     2m, 3.50m, rate: 15m)
            }
        };

        Texts(Rows(sale, Header(null))).Should().ContainInOrder(
            "AVERYLONGCODE",
            "Metformin 850mg tablets 60s",
            "extended release",
            ItemRow("2", "3.50", "7.00"));
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
            "Patient : Tendai Moyo (F00012)",
            "REF: Script No-13972"
        });
    }

    /// <remarks>
    /// A receipt is no place to list somebody's medicines: a collected
    /// script prints as one line with its number, as the pharmacy's
    /// previous system printed it, and what was sold over the counter
    /// alongside it prints as usual.
    /// </remarks>
    [Fact]
    public void A_collected_script_prints_as_its_number_not_its_medicines()
    {
        var sale = Sale() with
        {
            Lines = new[]
            {
                Line("AMOX500", "Amoxicillin 500mg caps x 21", 1m, 9.00m, rate: 0m)
                    with { IsScript = true },
                Line("ENAL10", "Enalapril 10mg tabs x 30", 1m, 5.00m, rate: 0m)
                    with { LineNo = 2, IsScript = true },
                Line("PAN500", "Panado 500mg 20s", 2m, 34.95m, rate: 15m)
                    with { LineNo = 3 }
            },
            Particulars = new ReceiptParticularsDto(null, null, "Tendai Moyo",
                "F00012", "417", null, null)
        };

        var texts = Texts(Rows(sale, Header(null)));

        texts.Should().NotContain(t => t.Contains("Amoxicillin", StringComparison.Ordinal)
                                       || t.Contains("Enalapril", StringComparison.Ordinal));

        texts.Should().ContainInOrder(
            "Script Charge: 417",
            ItemRow("1", "14.00", "14.00") + " NV",
            "PAN500",
            "Panado 500mg 20s",
            ItemRow("2", "34.95", "69.90"));
    }

    [Fact]
    public void A_script_keeps_its_number_when_the_template_hides_product_names()
    {
        var sale = Sale() with
        {
            Lines = new[]
            {
                Line("AMOX500", "Amoxicillin 500mg caps x 21", 1m, 9.00m, rate: 0m)
                    with { IsScript = true },
                Line("PAN500", "Panado 500mg 20s", 2m, 34.95m, rate: 15m)
                    with { LineNo = 2 }
            },
            Particulars = new ReceiptParticularsDto(null, null, "Tendai Moyo",
                "F00012", "417", null, null)
        };

        var header = Header(null) with
        {
            Template = ReceiptTemplate.Default with { ProductNames = false }
        };

        var texts = Texts(Rows(sale, header));

        texts.Should().Contain("Script Charge: 417");
        texts.Should().NotContain(t => t.Contains("Amoxicillin", StringComparison.Ordinal)
                                       || t.Contains("Panado", StringComparison.Ordinal));
    }

    [Fact]
    public void A_reprint_looks_like_the_original_unless_the_mark_is_turned_on()
    {
        var plain = Texts(Rows(Sale(), Header(null) with { IsCopy = true }));
        plain.Should().NotContain("*** COPY - REPRINTED ***",
            "the pharmacy asked for copies without the mark");

        var marked = Texts(Rows(Sale(), Header(null) with
        {
            IsCopy = true,
            Template = ReceiptTemplate.Default with { CopyMark = true }
        }));
        marked.Count(t => t == "*** COPY - REPRINTED ***").Should().Be(2);
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
            },
            Payments = new[]
            {
                new PaymentDto("MedicalAid", 1000m, null),
                new PaymentDto("Cash", 2703.50m, null)
            },
            Particulars = new ReceiptParticularsDto("ACC-000123",
                "Nyakunhuwa Family Holdings", "Nokuthula Mpofu-Ndlovu",
                "F00012", "13972", null, null)
        };

        var rows = Rows(sale, Header(null) with { IsCopy = true });

        rows.Should().OnlyContain(r => r.Text.Length <= ReceiptDocument.CharactersPerLine);
        rows[^1].Text.Should().Be("Thank you");
    }

    /// <remarks>
    /// The pharmacy's till printer is on Windows' Generic / Text Only
    /// driver, as its previous system set it up. Drawn through it, the slip
    /// came out on half the roll without its last columns or Thank you
    /// (photographed 22 October 2026). As text it is forty-two across and
    /// centred, every line ends in a line feed so the last one prints, and
    /// it is fed past the tear bar and cut.
    /// </remarks>
    [Fact]
    public void As_text_the_slip_fills_the_roll_and_prints_its_last_line()
    {
        using var document = new ReceiptDocument(Sale(), Header(null));
        var bytes = document.AsText();

        bytes.Take(5).Should().Equal(new byte[] { 0x1B, 0x40, 0x1B, 0x61, 0x01 },
            "the printer starts clean and centres every line");
        bytes.TakeLast(6).Should().Equal(new byte[] { 0x1B, 0x64, 0x04, 0x1D, 0x56, 0x01 },
            "the slip is fed past the tear bar and cut");

        var lines = Encoding.ASCII.GetString(bytes[5..^6])
            .Replace("\u001BE\u0001", "", StringComparison.Ordinal)
            .Replace("\u001BE\u0000", "", StringComparison.Ordinal)
            .Split('\n');

        lines[^1].Should().BeEmpty("every line ends in a line feed");
        lines[^2].Should().Be("Thank you");
        lines[..^1].Should().OnlyContain(line => line.Length <= SlipLayout.Width);
        lines.Should().ContainInOrder(
            "Code   QTY      Unit Price      Amount Inc",
            "PAN500".PadRight(SlipLayout.Width),
            ("         2 @         34.95".PadRight(34) + "69.90").PadRight(SlipLayout.Width),
            ("Sub Total".PadRight(34) + "69.90").PadRight(SlipLayout.Width));
    }

    /// <remarks>
    /// A label too long to sit beside its amount wraps rather than losing
    /// its end: which scheme was claimed from is the part that matters.
    /// </remarks>
    [Fact]
    public void A_long_total_label_wraps_with_the_amount_on_its_last_line()
    {
        var sale = Sale() with
        {
            Particulars = new ReceiptParticularsDto(null, null, "Tendai Moyo",
                "F00012", "13972", "Premier Service Medical Aid Society", 25.00m)
        };

        Texts(Rows(sale, Header(null))).Should().ContainInOrder(
            "Claimed from Premier",
            "Service Medical Aid",
            AmountRow("Society", "25.00"));
    }

    /// <remarks>
    /// The file number is how the pharmacy finds the patient again, so a
    /// long name wraps rather than taking it off the edge of the roll.
    /// </remarks>
    [Fact]
    public void A_long_patient_name_wraps_and_keeps_the_file_number()
    {
        var sale = Sale() with
        {
            Particulars = new ReceiptParticularsDto(null, null,
                "Nokuthula Mpofu-Ndlovu", "F00012", "13972", null, null)
        };

        Texts(Rows(sale, Header(null))).Should().ContainInOrder(
            "Patient : Nokuthula Mpofu-Ndlovu",
            "          (F00012)");
    }

    /// <remarks>
    /// The slip was drawn as one page, so on a long sale everything past
    /// the foot of the paper - the total and the tenders first - was
    /// never printed. It carries on to another page instead.
    /// </remarks>
    [Fact]
    public void A_long_sale_carries_on_to_another_page()
    {
        var sale = Sale() with
        {
            Lines = Enumerable.Range(1, 60)
                .Select(i => Line($"P{i}", $"Product number {i}", 1m, 1m, rate: 15m))
                .ToArray()
        };

        Pages(Sale()).Should().Be(1);
        Pages(sale).Should().BeGreaterThan(1);
    }

    // ---- Steps -----------------------------------------------------------

    /// Where an amount ends: three columns short of the roll's edge, to
    /// leave room for " NV".
    [Fact]
    public void The_receipt_template_leaves_out_what_the_administrator_unticked()
    {
        var header = Header(null) with
        {
            Address = "28 Woodstock Road",
            Template = ReceiptTemplate.Default with
            {
                Address = false,
                ProductCodes = false,
                QuantityAndUnitPrice = false,
                Vat = false,
                FooterText = "Get well soon"
            }
        };

        using var document = new ReceiptDocument(Sale(), header);
        var text = string.Join("\n", document.Rows().Select(r => r.Text));

        text.Should().NotContain("Woodstock");
        text.Should().NotContain(" @ ", "quantity and unit price are off");
        text.Should().NotContain("VAT-");
        text.Should().Contain("Get well soon").And.NotContain("Thank you");
        text.Should().Contain("TOTAL", "the total was left on");
    }

    [Fact]
    public void The_receipt_number_prints_whatever_the_template_says()
    {
        var nothing = ReceiptTemplate.Default with
        {
            TradingName = false, Address = false, VatNumber = false, Telephone = false,
            WhatsApp = false, DocumentTitle = false, Operator = false, TillNumber = false,
            Date = false, AccountAndPatient = false, ColumnHeadings = false, ProductCodes = false,
            ProductNames = false, QuantityAndUnitPrice = false, LineAmounts = false,
            SubTotal = false, Vat = false, Total = false, Tenders = false, Change = false,
            MedicalAidShare = false, AccountSignature = false, Footer = false
        };

        using var document = new ReceiptDocument(Sale(),
            Header(null) with { IsCopy = true, Template = nothing });
        var text = string.Join("\n", document.Rows().Select(r => r.Text));

        text.Should().Contain("Trans No:");
        text.Should().NotContain("COPY - REPRINTED");
    }

    [Fact]
    public void A_saved_template_reads_back_and_a_broken_one_prints_the_full_slip()
    {
        var saved = ReceiptTemplate.Default with { Telephone = false, FooterText = "Bye" };

        ReceiptTemplate.Parse(saved.ToJson()).Should().Be(saved);
        ReceiptTemplate.Parse("{not json").Should().Be(ReceiptTemplate.Default);
        ReceiptTemplate.Parse(null).Should().Be(ReceiptTemplate.Default);
    }

    private static string AmountRow(string label, string amount) =>
        label.PadRight(ReceiptDocument.AmountEnd - amount.Length) + amount;

    /// An item's figures: the quantity under QTY, the price under Unit
    /// Price, the amount under Amount Inc.
    private static string ItemRow(string quantity, string price, string amount) =>
        $"{quantity,7} @{price,12}".PadRight(ReceiptDocument.AmountEnd - amount.Length)
        + amount;

    /// The first row of pixels with ink across most of the page: a rule.
    private static int RuleRow(Bitmap page)
    {
        for (var y = 0; y < page.Height; y++)
        {
            var (left, right) = InkAcross(page, y);
            if (right - left > page.Width * 0.8) return y;
        }

        return 0;
    }

    private static (int Left, int Right) InkAcross(Bitmap page, int y)
    {
        var (left, right) = (page.Width, 0);

        for (var x = 0; x < page.Width; x++)
        {
            if (!IsDark(page.GetPixel(x, y))) continue;
            left = Math.Min(left, x);
            right = x;
        }

        return (left, right);
    }

    private static int LastInkColumn(Bitmap page)
    {
        var last = 0;

        for (var y = 0; y < page.Height; y++)
            for (var x = last; x < page.Width; x++)
                if (IsDark(page.GetPixel(x, y))) last = x;

        return last;
    }

    /// Renders the slip a page at a time until it is done; says how many.
    private static int Pages(SaleDto sale)
    {
        using var page = new Bitmap(Width, Height);
        using var g = Graphics.FromImage(page);
        using var document = new ReceiptDocument(sale, Header(null));

        var pages = 1;

        while (document.RenderPage(g, new Rectangle(10, 10, Width - 20, Height - 20))
               && pages < 50)
        {
            g.Clear(Color.White);
            pages++;
        }

        return pages;
    }

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

    /// Paid with 100.00 in cash: the payment is recorded at what stayed in
    /// the drawer, as the till records it.
    private static SaleDto Sale() => new(Guid.NewGuid(), "S-001-000042",
        new[] { Line("PAN500", "Panado 500mg 20s", 2m, 34.95m, rate: 15m) },
        new SaleTotalsDto(69.90m, 0m, 9.12m, 69.90m, 100m, 30.10m),
        new[] { new PaymentDto("Cash", 69.90m, null) });

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
