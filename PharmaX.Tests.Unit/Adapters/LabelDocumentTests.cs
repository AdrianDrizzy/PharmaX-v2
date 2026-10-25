using FluentAssertions;
using PharmaX.Adapters.Printing;
using PharmaX.Application.Dispensing.Dtos;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// Reads a dispensing label row by row, and draws one onto a bitmap the
/// size of the stock, to see what goes onto the container.
/// </summary>
/// <remarks>
/// The label is the only instruction the patient takes home, and the
/// only thing a pharmacist has to go on when somebody rings holding a
/// container. A label printer is not available to a test, and "it
/// compiled" says nothing about what reached the stock - least of all
/// whether it reached it inside the edges.
/// </remarks>
public class LabelDocumentTests
{
    /// The page the label printer is given, in hundredths of an inch.
    private const int Width = LabelDocument.WidthHundredths;
    private const int Height = LabelDocument.HeightHundredths;

    /// <remarks>
    /// The pharmacy's own pre-pack sticker, as photographed on its bag,
    /// character for character: the price beside the date, the pharmacy
    /// and its address at the foot, and no "Keep out of reach of
    /// children" - the bag prints it.
    /// </remarks>
    [Fact]
    public void A_pre_pack_reads_as_the_pharmacys_own_label()
    {
        using var document = new LabelDocument(new LabelDto(
            PatientName: "",
            ProductDescription: "Paracetamol 500mg Tab",
            Quantity: 20m,
            Directions: "Take 1-2 tablets after meals orally 3 times daily "
                        + "after food when necessary",
            BatchNumber: "2076",
            Expiry: new DateOnly(2028, 6, 30),
            PharmacistName: "test",
            ScriptNumber: "Pre-pack",
            DispensedOn: new DateOnly(2026, 10, 19),
            Cautions: new[] { "" },
            Price: 0.50m), Bethrapha);

        document.Lines().Should().Equal(
            "PARACETAMOL 500MG TAB (20)",
            "TAKE 1-2 TABLETS AFTER MEALS ORALLY 3",
            "TIMES DAILY AFTER FOOD WHEN NECESSARY",
            "EXPIRY: 30/06/2028".PadRight(
                LabelDocument.Columns - "BATCH NO: 2076".Length) + "BATCH NO: 2076",
            "0.50".PadRight(LabelDocument.Columns - 20) + "PACKED ON:19/10/2026",
            "Bethrapha Pharmacy, 28 Woodstock Road",
            "Thorngrove, Bulawayo");

        // Underlined as the pharmacy's own: the medicine, and the price and
        // date - not the expiry and batch between them.
        document.Rows().Where(row => row.Underlined).Select(row => row.Text)
            .Should().Equal("PARACETAMOL 500MG TAB (20)",
                "0.50".PadRight(LabelDocument.Columns - 20) + "PACKED ON:19/10/2026");
    }

    /// <remarks>
    /// An underline runs under the words, word by word: under the price,
    /// and under the date, not across the gap between them.
    /// </remarks>
    [Fact]
    public void A_row_is_underlined_run_by_run()
    {
        LabelPrint.Segments("0.50" + new string(' ', 18) + "PACKED ON:19/10/2026")
            .Should().Equal((0, "0.50"), (22, "PACKED ON:19/10/2026"));

        LabelPrint.Segments("PARACETAMOL 500MG TAB (20)")
            .Should().Equal((0, "PARACETAMOL 500MG TAB (20)"));
    }

    /// <remarks>
    /// Every label carries its expiry, a patient's as well as a
    /// pre-pack's. Where nothing records it - a medicine not kept by batch -
    /// there is a space to write it in.
    /// </remarks>
    [Fact]
    public void Every_label_has_an_expiry_line()
    {
        using var document = new LabelDocument(Label() with
        {
            Expiry = null,
            BatchNumber = null
        }, Pharmacy);

        document.Lines().Should().Contain(line =>
            line.EndsWith("EXP:__/__/____", StringComparison.Ordinal));
    }

    /// <remarks>
    /// A patient's label, laid out as the dispensing label the pharmacy
    /// photographed on a bag to follow: no blank row, every row as full as
    /// it goes, the quantity, the expiry and the date against the right
    /// edge, the medical aid after the name, and the whole address.
    /// </remarks>
    [Fact]
    public void A_patients_label_follows_the_pharmacys_chosen_layout()
    {
        using var document = new LabelDocument(new LabelDto(
            PatientName: "Nobuhle Masuku",
            ProductDescription: "Levonorgestrel Backup 1.5mg",
            Quantity: 1m,
            Directions: "Take at once",
            BatchNumber: null,
            Expiry: null,
            PharmacistName: "admin",
            ScriptNumber: "SC-001-000023",
            DispensedOn: new DateOnly(2026, 10, 21),
            Cautions: new[] { "Use only as directed." },
            SchemeName: "Cimas"), BethRapha);

        document.Lines().Should().Equal(
            "LEVONORGESTREL BACKUP 1.5MG".PadRight(39) + "(1)",
            "TAKE AT ONCE USE ONLY AS DIRECTED.",
            "Rx:SC-001-000023".PadRight(28) + "EXP:__/__/____",
            "NOBUHLE MASUKU / CIMAS".PadRight(32) + "21/10/2026",
            "BETHRAPHA PHARMACY, 28 WOODSTOCK ROAD,",
            "THORNGROVE, BULAWAYO, (ALONG LUVEVE ROAD)");

        document.Rows().Should().OnlyContain(row => row.Compact && !row.Underlined,
            "every row in the same face, none past the edge the printer reaches");
    }

    [Fact]
    public void A_batch_goes_beside_the_script_where_it_fits()
    {
        using var document = new LabelDocument(Label() with
        {
            ScriptNumber = "13972",
            BatchNumber = "3454"
        }, Pharmacy);

        document.Lines().Should().Contain(
            "Rx:13972 BN:3454".PadRight(28) + "EXP:31/01/2027");
    }

    /// <remarks>
    /// The Amoxicillin label photographed on 22 October 2026 ran to nine
    /// rows and came out in tiny type; the labels on eight were fine.
    /// Without the dispenser's initials the script, the batch and the
    /// expiry share a row again, and it is eight.
    /// </remarks>
    [Fact]
    public void A_patients_label_names_no_dispenser_and_keeps_to_eight_rows()
    {
        using var document = new LabelDocument(new LabelDto(
            PatientName: "Catherine Antonio",
            ProductDescription: "Amoxycillin Amoxen (Varichem) 250mg",
            Quantity: 15m,
            Directions: "Take 2 capsules three times a day before meals",
            BatchNumber: "B7L4",
            Expiry: new DateOnly(2028, 8, 19),
            PharmacistName: "Ian Admin",
            ScriptNumber: "SC-001-000025",
            DispensedOn: new DateOnly(2026, 10, 22),
            Cautions: Array.Empty<string>()), BethRapha);

        document.Lines().Should().HaveCount(LabelDocument.LineCount);
        document.Lines().Should().Contain(
            "Rx:SC-001-000025 BN:B7L4".PadRight(28) + "EXP:19/08/2028");
        string.Join(" ", document.Lines()).Should().NotContain("Dis/BY");
    }

    /// <remarks>
    /// A long batch list and a two-row address leave the directions one
    /// row. The warning is what is left off - never the directions - so
    /// the label stays on eight rows and in the printer's own face.
    /// </remarks>
    [Fact]
    public void A_crowded_patients_label_leaves_the_warning_off_to_keep_to_eight_rows()
    {
        using var document = new LabelDocument(Label(), Pharmacy);

        document.Lines().Should().HaveCount(LabelDocument.LineCount);
        Joined(document.Rows(), LabelStyle.Directions).Should()
            .Be("ONE CAPSULE THREE TIMES A DAY");
    }

    /// <remarks>
    /// The scheme is cut to stay beside the date, as on the label the
    /// pharmacy chose; the patient's name never is.
    /// </remarks>
    [Fact]
    public void A_long_scheme_is_cut_short_and_the_name_is_not()
    {
        using var document = new LabelDocument(Label() with
        {
            PatientName = "Mirriam Dzengwerere",
            SchemeName = "First Mutual Life Assurance"
        }, Pharmacy);

        document.Lines().Should().Contain(
            "MIRRIAM DZENGWERERE / FIRST MUT 23/09/2026");
    }

    /// <remarks>
    /// Twenty paracetamol packs take twenty stickers, one after another
    /// on the stock, in one job.
    /// </remarks>
    [Fact]
    public void A_run_of_stickers_prints_as_many_copies_as_asked_for()
    {
        using var document = new LabelDocument(Label(), Bethrapha, copies: 20);
        using var page = new Bitmap(Width, Height);
        using var g = Graphics.FromImage(page);

        var printed = 1;

        while (document.RenderPage(g, new Rectangle(4, 4, Width - 8, Height - 8))
               && printed < 100)
            printed++;

        printed.Should().Be(20);
    }

    [Fact]
    public void No_line_is_wider_than_the_stock()
    {
        using var document = new LabelDocument(Crowded(), Pharmacy);

        var product = document.Rows().Where(r => r.Style == LabelStyle.Product).ToList();

        product.Should().HaveCountGreaterThan(1, "a long medicine name wraps");
        Words(product).Should().Be(
            "CO-AMOXICLAV 875MG/125MG FILM COATED TABLETS 14S EXTENDED COURSE (21)");
        Words(document.Rows().Where(r => r.Style == LabelStyle.Imprint)).Should().Be(
            "RIVERSIDE PHARMACY, 1 EXAMPLE STREET, HARARE");
        document.Lines().Should().OnlyContain(line =>
            line.Length <= LabelDocument.Columns);
    }

    /// <remarks>
    /// A batch list too long to sit beside the expiry goes under it,
    /// against the right edge, rather than off the side of the stock.
    /// </remarks>
    [Fact]
    public void A_long_batch_list_goes_under_the_expiry()
    {
        using var document = new LabelDocument(Label(), Pharmacy);

        document.Lines().Should().ContainInOrder(
            "Rx:SC-001-000003",
            "BN:AMOX50-A x4, AMOX50-B x17",
            "EXP:31/01/2027".PadLeft(LabelDocument.Columns));
    }

    /// <remarks>
    /// Every label carries a warning, whatever the medicine and however it
    /// was printed: every label used to go out with none.
    /// </remarks>
    [Fact]
    public void Every_label_carries_a_warning()
    {
        using var document = new LabelDocument(Label() with
        {
            ProductDescription = "COTTON WOOL 125G",
            Directions = "Use as directed"
        });

        Joined(document.Rows(), LabelStyle.Directions).Should().Be(
            "USE AS DIRECTED USE ONLY AS DIRECTED.");
    }

    /// <remarks>
    /// A pre-pack can be a quantity and nothing else. Medicine, directions,
    /// warning, batch, price and supplier are left off when blank; the
    /// expiry line stays, to be written in.
    /// </remarks>
    [Fact]
    public void A_quantity_only_label_leaves_the_blanks_off()
    {
        using var document = new LabelDocument(new LabelDto(
            PatientName: "",
            ProductDescription: "",
            Quantity: 20m,
            Directions: "",
            BatchNumber: null,
            Expiry: null,
            PharmacistName: "",
            ScriptNumber: "Pre-pack",
            DispensedOn: new DateOnly(2026, 10, 20),
            Cautions: new[] { "" },
            Price: null), Bethrapha);

        var lines = document.Lines().Where(line => line.Length > 0).ToList();

        lines[0].Should().Be("(20)");
        lines.Should().Contain(line => line.Trim().StartsWith("PACKED ON:",
            StringComparison.Ordinal) || line.Contains("PACKED ON:20/10/2026",
            StringComparison.Ordinal));
        string.Join(" ", lines).Should().Contain("Bethrapha Pharmacy");
        lines.Should().Contain("EXPIRY: __/__/____");
        lines.Should().NotContain(line =>
            line.Contains("BATCH", StringComparison.Ordinal)
            || line.Contains("TAKE", StringComparison.Ordinal));
    }

    /// <remarks>
    /// Stickers made up by hand carry the warning they are given, and
    /// none where it is left blank, as the pharmacy's paracetamol packs
    /// do.
    /// </remarks>
    [Fact]
    public void A_label_does_not_say_do_not_share_this_medicine()
    {
        using var document = new LabelDocument(Label() with
        {
            Directions = "Take at once. Do not share this medicine with anyone.",
            Cautions = new[] { "Use only as directed. Do not share this medicine." }
        });

        var text = string.Join(" ", document.Lines());

        text.Should().Contain("TAKE AT ONCE");
        text.Should().Contain("USE ONLY AS DIRECTED");
        text.Should().NotContain("SHARE");
        text.Should().Contain("EXP:");
    }

    [Fact]
    public void A_blank_warning_given_with_the_label_prints_none()
    {
        using var document = new LabelDocument(Label() with
        {
            ProductDescription = "Paracetamol 500mg Tab",
            Cautions = new[] { "" }
        });

        Joined(document.Rows(), LabelStyle.Directions).Should()
            .Be("ONE CAPSULE THREE TIMES A DAY");
    }

    /// <remarks>
    /// Directions that fill both lines still carry the most important
    /// warning where the label has a row left for it.
    /// </remarks>
    [Fact]
    public void Long_directions_still_carry_the_first_warning()
    {
        using var document = new LabelDocument(Label() with
        {
            Directions = "Take one capsule three times a day after meals for "
                         + "seven days and finish the course",
            Cautions = new[] { "Do not drink alcohol.", "Keep in the fridge." }
        });

        Joined(document.Rows(), LabelStyle.Directions).Should().EndWith(
            "FINISH THE COURSE DO NOT DRINK ALCOHOL.");
    }

    [Fact]
    public void Warnings_given_with_the_label_are_the_ones_printed()
    {
        using var document = new LabelDocument(Label() with
        {
            Cautions = new[] { "Keep in the fridge." }
        });

        Joined(document.Rows(), LabelStyle.Directions).Should()
            .Be("ONE CAPSULE THREE TIMES A DAY KEEP IN THE FRIDGE.");
    }

    /// <remarks>
    /// A container out of its bag said nothing of where it was dispensed
    /// (ADM-DEF-16). A label printed with no pharmacy at all is still a
    /// label, so nothing is invented.
    /// </remarks>
    [Fact]
    public void A_label_with_no_pharmacy_carries_no_foot()
    {
        using var document = new LabelDocument(Label());

        document.Rows().Should().NotContain(row => row.Style == LabelStyle.Imprint);
    }

    /// <remarks>
    /// The year and the street were cut off the sticker. The foot is set
    /// smaller than the directions, and every word of the address is still
    /// on the label.
    /// </remarks>
    [Fact]
    public void The_foot_is_smaller_and_keeps_the_year_and_the_street()
    {
        using var document = new LabelDocument(new LabelDto(
            PatientName: "",
            ProductDescription: "Paracetamol 500mg (Caps)",
            Quantity: 30m,
            Directions: "Take 2 tablets three times a day after meals until finished",
            BatchNumber: "345",
            Expiry: new DateOnly(2026, 12, 31),
            PharmacistName: "",
            ScriptNumber: "Pre-pack",
            DispensedOn: new DateOnly(2026, 10, 20),
            Cautions: new[] { "" },
            Price: 1.00m),
            new PharmacyImprint("BethRapha Pharmacy",
                "28 Woodstock Road\nThorngrove\nBulawayo\n(Along Luveve Road)",
                null));

        var text = string.Join(" ", document.Lines());

        text.Should().Contain("31/12/2026");
        text.Should().Contain("20/10/2026");
        text.Should().Contain("Thorngrove");
        text.Should().Contain("Bulawayo");
        text.Should().Contain("Luveve");
        document.Rows().Where(row => row.Style is LabelStyle.Detail or LabelStyle.Imprint)
            .Should().OnlyContain(row => row.Compact);
        document.Rows().Where(row => row.Style == LabelStyle.Product)
            .Should().OnlyContain(row => !row.Compact);

        using var page = new Bitmap(Width, Height);
        using var g = Graphics.FromImage(page);

        g.Clear(Color.White);
        document.RenderPage(g, new Rectangle(4, 4, Width - 8, Height - 8));

        var (lastRow, lastColumn) = LastInk(page);

        lastRow.Should().BeLessThan(Height - 4);
        lastColumn.Should().BeLessThan(Width - 4);
    }

    [Fact]
    public void A_template_can_leave_the_address_and_the_date_off()
    {
        using var document = new LabelDocument(Label() with
        {
            PatientName = "",
            Price = 1.00m,
            Omit = new LabelOmissions(PackedOn: true, Address: true, Pharmacy: true)
        }, Pharmacy);

        var text = string.Join("\n", document.Lines());

        text.Should().NotContain("PACKED ON");
        text.Should().NotContain("Riverside");
        text.Should().NotContain("Example Street");
        text.Should().Contain("EXPIRY:");
    }

    /// <remarks>
    /// The page does not grow. A label that
    /// runs off the bottom loses the pharmacy's address, and one that runs
    /// off the side loses the end of every line.
    /// </remarks>
    [Fact]
    public void Everything_fits_the_stock_even_when_it_all_wraps()
    {
        // 77 by 39 mm: one sticker to a page, so each label starts on its
        // own sticker rather than creeping up the roll.
        LabelDocument.WidthHundredths.Should().Be(303);
        LabelDocument.HeightHundredths.Should().Be(154);

        using var document = new LabelDocument(Crowded(), Pharmacy);

        using var page = new Bitmap(Width, Height);
        using var g = Graphics.FromImage(page);

        g.Clear(Color.White);
        document.RenderPage(g, new Rectangle(4, 4, Width - 8, Height - 8));

        var (lastRow, lastColumn) = LastInk(page);

        lastRow.Should().BeLessThan(Height - 4, "the whole label prints inside the stock");
        lastColumn.Should().BeLessThan(Width - 4, "every line ends inside the stock");
    }

    // ---- Steps -----------------------------------------------------------

    private static readonly PharmacyImprint Bethrapha =
        new("Bethrapha Pharmacy", "28 Woodstock Road\nThorngrove\nBulawayo", null);

    private static readonly PharmacyImprint BethRapha =
        new("BethRapha Pharmacy",
            "28 Woodstock Road\nThorngrove\nBulawayo\n(Along Luveve Road)", null);

    private static readonly PharmacyImprint Pharmacy =
        new("Riverside Pharmacy", "1 Example Street\nHarare",
            "+263 78 376 6260");

    private static LabelDto Label() => new(
        PatientName: "Priya Naidoo",
        ProductDescription: "Amoxicillin 500mg capsules 21s",
        Quantity: 21m,
        Directions: "One capsule three times a day",
        BatchNumber: "AMOX50-A x4, AMOX50-B x17",
        Expiry: new DateOnly(2027, 1, 31),
        PharmacistName: "Test Pharmacist",
        ScriptNumber: "SC-001-000003",
        DispensedOn: new DateOnly(2026, 9, 23),
        Cautions: Array.Empty<string>(),
        ItemNumber: 2,
        ItemCount: 3,
        PrescriberName: "Dr N Moyo",
        PrescriberNumber: "370");

    private static LabelDto Crowded() => Label() with
    {
        ProductDescription = "Co-amoxiclav 875mg/125mg film coated "
                             + "tablets 14s extended course",
        Directions = "Take one tablet twice a day with food for seven "
                     + "days, and finish the course even if you feel "
                     + "better",
        BatchNumber = "AMOX50-A x4, AMOX50-B x17, AMOX50-C x9",
        PatientName = "Mrs Priyanka Naidoo-Ramsamy of Borrowdale West",
        Cautions = new[] { "May cause drowsiness. Do not drive." }
    };

    /// The rows' words, the padding between them taken out.
    private static string Words(IEnumerable<LabelRow> rows) =>
        string.Join(" ", rows.SelectMany(r => r.Text.Split(' ',
            StringSplitOptions.RemoveEmptyEntries)));

    private static string Joined(IEnumerable<LabelRow> rows, LabelStyle style) =>
        string.Join(" ", rows.Where(r => r.Style == style && r.Text.Length > 0)
                             .Select(r => r.Text));

    private static (int Row, int Column) LastInk(Bitmap page)
    {
        var (row, column) = (0, 0);

        for (var y = 0; y < page.Height; y++)
        {
            for (var x = 0; x < page.Width; x++)
            {
                if (IsInk(page.GetPixel(x, y)))
                    (row, column) = (y, Math.Max(column, x));
            }
        }

        return (row, column);
    }

    private static bool IsInk(Color pixel) =>
        pixel.R < 200 || pixel.G < 200 || pixel.B < 200;
}
