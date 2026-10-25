using FluentAssertions;
using PharmaX.Adapters.Printing;
using PharmaX.Application.Dispensing;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// The trailer label that follows a script's item labels: the script,
/// the patient, each medicine and how many, the prescriber, and the
/// pharmacy with the date.
/// </summary>
public class TrailerLabelDocumentTests
{
    // The label page, in hundredths of an inch, inside its margins.
    private static readonly Rectangle Label = new(4, 4,
        LabelDocument.WidthHundredths - 8, LabelDocument.HeightHundredths - 8);

    private const string Pharmacy =
        "Bethrapha Pharmacy, 28 Woodstock Road, Thorngrove, Bulawayo";

    /// <remarks>
    /// The pharmacy's address whole, over two lines, and no "Keep out of
    /// reach of children": the bag the labels go on prints it.
    /// </remarks>
    [Fact]
    public void The_trailer_reads_as_the_pharmacys_own_trailer_labels_do()
    {
        var lines = TrailerLabelDocument.Lines(Trailer(
            ("ENALAPRIL +5 10MG TAB", 15m),
            ("SPIRONOLACTONE ALDAC 25MG TAB", 5m),
            ("INDAPAMIDE 1.5MG TAB", 5m),
            ("NIFEDIPINE CALCIGARD 20MG TAB", 14m)), Pharmacy);

        lines.Should().Equal(
            "RX#: 13049 JOSEPHAT NYAKUNHUWA (001)",
            "ENALAPRIL +5 10MG TAB (15)",
            "SPIRONOLACTONE ALDAC 25MG TAB (5)",
            "INDAPAMIDE 1.5MG TAB (5)",
            "NIFEDIPINE CALCIGARD 20MG TAB (14)",
            "DR: madondoro D".PadRight(LabelDocument.Columns - 10) + "08/09/2026",
            "Bethrapha Pharmacy, 28 Woodstock Road,",
            "Thorngrove, Bulawayo");
        string.Join(" ", lines).Should().Contain("28 Woodstock Road")
            .And.Contain("Thorngrove").And.Contain("Bulawayo");
    }

    /// <remarks>
    /// As wide as the item labels: a long name wraps, and neither the
    /// quantity the bag is checked against nor the issue number is dropped.
    /// </remarks>
    [Fact]
    public void Every_line_fits_the_label()
    {
        var trailer = Trailer(("CO-AMOXICLAV 875MG/125MG FILM COATED TABLETS EXTENDED", 14m))
            with { PatientName = "MRS PRIYANKA NAIDOO-RAMSAMY OF BORROWDALE" };

        var lines = TrailerLabelDocument.Lines(trailer,
            "Bethrapha Pharmacy, 28 Woodstock Road, Bulawayo", "+263 29 288 1234");

        lines.Should().OnlyContain(line => line.Length <= LabelDocument.Columns);
        lines[0].Should().StartWith("RX#: 13049 MRS");
        string.Join(" ", lines).Should().Contain("(001)")
            .And.Contain("CO-AMOXICLAV 875MG/125MG FILM COATED TABLETS EXTENDED (14)");
    }

    [Fact]
    public void The_trailer_gives_the_pharmacys_telephone_number()
    {
        var lines = TrailerLabelDocument.Lines(Trailer(("AMOXICILLIN 500MG CAP", 21m)),
            Pharmacy, "+263 29 288 1234");

        lines[^1].Should().Be("Tel: +263 29 288 1234");
    }

    [Fact]
    public void A_repeat_is_numbered_and_the_prescriber_titled_once()
    {
        var trailer = Trailer(("AMOXICILLIN 500MG CAP", 21m)) with
        {
            FillNumber = 2,
            PrescriberName = "Dr Adams"
        };

        var lines = TrailerLabelDocument.Lines(trailer, Pharmacy);

        string.Join(" ", lines).Should().Contain("(002)");
        lines.Should().Contain("DR: Adams".PadRight(LabelDocument.Columns - 10) + "08/09/2026");
    }

    [Fact]
    public void A_short_script_fits_one_label()
    {
        using var document = new TrailerLabelDocument(Trailer(
            ("ENALAPRIL +5 10MG TAB", 15m),
            ("INDAPAMIDE 1.5MG TAB", 5m)), Pharmacy);

        Render(document).Should().Be(1);
    }

    /// <remarks>
    /// Nothing on the script is left off: a script longer than one label
    /// carries on to the next.
    /// </remarks>
    [Fact]
    public void A_long_script_carries_on_to_another_label()
    {
        var items = Enumerable.Range(1, 30)
            .Select(i => ($"MEDICINE NUMBER {i} 10MG TAB", (decimal)i))
            .ToArray();

        using var document = new TrailerLabelDocument(Trailer(items), Pharmacy);

        Render(document).Should().BeGreaterThan(1);
    }

    /// Renders labels until the document says it is done; returns how many.
    private static int Render(TrailerLabelDocument document)
    {
        using var bitmap = new Bitmap(LabelDocument.WidthHundredths,
            LabelDocument.HeightHundredths);
        using var g = Graphics.FromImage(bitmap);

        var labels = 1;

        while (document.RenderPage(g, Label) && labels < 50)
        {
            g.Clear(Color.White);
            labels++;
        }

        return labels;
    }

    private static TrailerLabelDto Trailer(
        params (string Description, decimal Quantity)[] items) => new(
        ScriptNumber: "13049",
        PatientName: "JOSEPHAT NYAKUNHUWA",
        FillNumber: 1,
        Items: items.Select(i => new TrailerItemDto(i.Description, i.Quantity))
                    .ToList(),
        PrescriberName: "madondoro D",
        DispensedOn: new DateOnly(2026, 9, 8));
}
