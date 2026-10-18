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
    // The 102 by 36 mm label stock, in hundredths of an inch, inside its
    // margins.
    private static readonly Rectangle Label = new(4, 4, 392, 136);

    private const string Pharmacy = "Bethrapha Pharmacy, 28 Woodstock Road";

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
            "DR: madondoro D".PadRight(38) + "08/09/2026",
            "Bethrapha Pharmacy, 28 Woodstock Road",
            "Keep out of reach of children.");
    }

    /// <remarks>
    /// As wide as the item labels: a long name is shortened, never the
    /// quantity the bag is checked against or which issue of the script
    /// it is.
    /// </remarks>
    [Fact]
    public void Every_line_fits_the_label()
    {
        var trailer = Trailer(("CO-AMOXICLAV 875MG/125MG FILM COATED TABLETS EXTENDED", 14m))
            with { PatientName = "MRS PRIYANKA NAIDOO-RAMSAMY OF BORROWDALE" };

        var lines = TrailerLabelDocument.Lines(trailer,
            "Bethrapha Pharmacy, 28 Woodstock Road, Bulawayo", "+263 29 288 1234");

        lines.Should().OnlyContain(line => line.Length <= LabelDocument.Columns);
        lines[0].Should().StartWith("RX#: 13049 MRS").And.EndWith("(001)");
        lines[1].Should().EndWith("… (14)");
    }

    [Fact]
    public void The_trailer_gives_the_pharmacys_telephone_number()
    {
        var lines = TrailerLabelDocument.Lines(Trailer(("AMOXICILLIN 500MG CAP", 21m)),
            Pharmacy, "+263 29 288 1234");

        lines.Should().EndWith(new[] { "Tel: +263 29 288 1234", "Keep out of reach of children." });
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

        lines[0].Should().EndWith("(002)");
        lines.Should().Contain("DR: Adams".PadRight(38) + "08/09/2026");
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
        using var bitmap = new Bitmap(400, 144);
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
