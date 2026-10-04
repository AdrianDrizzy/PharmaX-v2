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
    /// The stock: 70 by 50 millimetres, in hundredths of an inch.
    private const int Width = 276;
    private const int Height = 197;

    [Fact]
    public void The_label_reads_medicine_first_and_pharmacy_last()
    {
        using var document = new LabelDocument(Label(), Pharmacy);

        var rows = document.Rows();

        // What the patient reads, in the largest type, over a rule.
        rows[0].Style.Should().Be(LabelStyle.Product);
        rows[0].Text.Should().Be("Amoxicillin 500mg capsules 21s  x21");
        rows[1].Style.Should().Be(LabelStyle.Directions);
        rows[1].Text.Should().Be("One capsule three times a day");
        rows[2].Style.Should().Be(LabelStyle.Rule);

        // Then what the pharmacy is asked about afterwards.
        document.Lines().Should().ContainInOrder(
            "Batch: AMOX50-A x4, AMOX50-B x17   Exp: 31/01/2027",
            "Priya Naidoo",
            "Disp by: Test Pharmacist   23 Sept 26",
            "Doc. Dr N Moyo",
            "RxNo: SC-001-000003  Item: 2 of 3  Prof# 370");

        // And who dispensed it, with the number to ring, at the foot.
        rows[^3].Should().Be(new LabelRow("Riverside Pharmacy",
                                          LabelStyle.Pharmacy));
        rows[^2].Should().Be(new LabelRow("1 Example Street, Harare",
                                          LabelStyle.Imprint));
        rows[^1].Should().Be(new LabelRow("+263 78 376 6260",
                                          LabelStyle.Imprint));
    }

    /// <remarks>
    /// One item counted out of one reads as though something is missing
    /// from the bag; three counted out of three is the point of it.
    /// </remarks>
    [Fact]
    public void A_script_of_one_item_does_not_count_it_out_of_one()
    {
        using var document = new LabelDocument(Label() with
        {
            ItemNumber = 1,
            ItemCount = 1,
            PrescriberNumber = null
        });

        document.Lines().Should().Contain("RxNo: SC-001-000003  Item: 1");
    }

    /// <remarks>
    /// A container out of its bag said nothing of where it was dispensed
    /// or who to call about it (ADM-DEF-16). A label printed with no
    /// pharmacy at all is still a label, so nothing is invented.
    /// </remarks>
    [Fact]
    public void A_label_with_no_pharmacy_carries_no_foot()
    {
        using var document = new LabelDocument(Label());

        document.Rows().Should().NotContain(row =>
            row.Style == LabelStyle.Pharmacy
            || row.Style == LabelStyle.Imprint);
    }

    /// <remarks>
    /// The stock is 70 by 50 millimetres and does not grow. A label that
    /// runs off the bottom of it loses the pharmacy's own name and
    /// telephone number, which is the half a patient needs hours later.
    /// </remarks>
    [Fact]
    public void Everything_fits_the_stock_even_when_it_all_wraps()
    {
        var crowded = Label() with
        {
            ProductDescription = "Co-amoxiclav 875mg/125mg film coated "
                                 + "tablets 14s extended course",
            Directions = "Take one tablet twice a day with food for seven "
                         + "days, and finish the course even if you feel "
                         + "better",
            BatchNumber = "AMOX50-A x4, AMOX50-B x17, AMOX50-C x9",
            PatientName = "Mrs Priyanka Naidoo-Ramsamy",
            Cautions = new[] { "May cause drowsiness. Do not drive." }
        };

        using var document = new LabelDocument(crowded, Pharmacy);

        using var page = new Bitmap(Width, Height);
        using var g = Graphics.FromImage(page);

        g.Clear(Color.White);
        document.RenderPage(g, new Rectangle(8, 8, Width - 16, Height - 16));

        LastInkRow(page).Should().BeLessThan(Height - 8,
            "the whole label prints inside the stock");
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

    private static int LastInkRow(Bitmap page)
    {
        for (var y = page.Height - 1; y >= 0; y--)
        {
            for (var x = 0; x < page.Width; x++)
            {
                var pixel = page.GetPixel(x, y);

                if (pixel.R < 200 || pixel.G < 200 || pixel.B < 200)
                    return y;
            }
        }

        return 0;
    }
}
