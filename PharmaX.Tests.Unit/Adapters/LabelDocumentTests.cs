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
    /// The stock: 102 by 36 millimetres, in hundredths of an inch.
    private const int Width = 400;
    private const int Height = 144;

    /// <remarks>
    /// The pharmacy's own pre-pack label, as photographed on its bag,
    /// character for character - but for the "1.00" its fifth row starts
    /// with, which nothing yet says the meaning of.
    /// </remarks>
    [Fact]
    public void A_pre_pack_reads_as_the_pharmacys_own_label()
    {
        using var document = new LabelDocument(new LabelDto(
            PatientName: "",
            ProductDescription: "Co-Codamol 30mg Tab",
            Quantity: 12m,
            Directions: "Take 2 tablets after meals three times daily for pain",
            BatchNumber: "0874",
            Expiry: new DateOnly(2030, 6, 30),
            PharmacistName: "test",
            ScriptNumber: "Pre-pack",
            DispensedOn: new DateOnly(2026, 10, 11),
            Cautions: new[] { "May cause drowsiness" }),
            new PharmacyImprint("Bethrapha Pharmacy",
                                "28 Woodstock Road\nThorngroove\nBulawayo",
                                "029 2881234"));

        document.Lines().Should().Equal(
            "CO-CODAMOL 30MG TAB (12)",
            "TAKE 2 TABLETS AFTER MEALS THREE TIMES DAILY FOR",
            "PAIN MAY CAUSE DROWSINESS",
            "",
            "EXPIRY: 30/06/2030                BATCH NO: 0874",
            "                            PACKED ON:11/10/2026",
            "Bethrapha Pharmacy, 28 Woodstock Road",
            "Thorngroove, Bulawayo");
    }

    /// <remarks>
    /// A dispensed label is somebody's: their name goes on the fourth
    /// row, which a pre-pack leaves empty.
    /// </remarks>
    [Fact]
    public void A_patients_label_carries_their_name_on_the_fourth_row()
    {
        using var document = new LabelDocument(Label(), Pharmacy);

        var rows = document.Rows();

        rows[0].Should().Be(new LabelRow("AMOXICILLIN 500MG CAPSULES 21S (21)",
                                         LabelStyle.Product));
        rows[1].Text.Should().StartWith("ONE CAPSULE THREE TIMES A DAY FINISH");
        rows[3].Should().Be(new LabelRow("Priya Naidoo", LabelStyle.Patient));
        rows[^2].Text.Should().Be("Riverside Pharmacy, 1 Example Street");
        rows[^1].Text.Should().Be("Harare");
    }

    [Fact]
    public void No_line_is_wider_than_the_stock()
    {
        using var document = new LabelDocument(Crowded(), Pharmacy);

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
            "EXPIRY: 31/01/2027",
            "BATCH NO: AMOX50-A x4, AMOX50-B x17".PadLeft(LabelDocument.Columns));
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
            "USE AS DIRECTED USE ONLY AS DIRECTED. DO NOT SHARE THIS MEDICINE.");
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
    /// The stock is 102 by 36 millimetres and does not grow. A label that
    /// runs off the bottom loses the pharmacy's own name, and one that
    /// runs off the side loses the end of every line.
    /// </remarks>
    [Fact]
    public void Everything_fits_the_stock_even_when_it_all_wraps()
    {
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
                var pixel = page.GetPixel(x, y);

                if (pixel.R < 200 || pixel.G < 200 || pixel.B < 200)
                    (row, column) = (y, Math.Max(column, x));
            }
        }

        return (row, column);
    }
}
