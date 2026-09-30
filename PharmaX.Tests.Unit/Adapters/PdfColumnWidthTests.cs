using FluentAssertions;
using PharmaX.Application.Reporting;
using PharmaX.Reporting.Exporters;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// A printed report gives each column the room its contents need, not an
/// equal share: fifteen equal columns wrapped an item's name a word to a
/// line beside a quantity column with room to spare.
/// </summary>
public class PdfColumnWidthTests
{
    [Fact]
    public void A_column_is_as_wide_as_what_it_holds_within_limits()
    {
        var report = new ReportResult("Test",
            new[]
            {
                new ReportColumn("Qty", "Qty", "0.###"),
                new ReportColumn("Item", "Item"),
                new ReportColumn("Note", "Note")
            },
            new[]
            {
                Row(2m, "Amoxicillin 500mg capsules 21s", new string('x', 90)),
                Row(12.5m, "Panado", "")
            },
            new Dictionary<string, object?>(),
            new ReportProvenance("test", DateTime.Now, "", ""));

        var widths = PdfExporter.Widths(report);

        widths[0].Should().Be(5f, "a short number still gets a readable minimum");
        widths[1].Should().Be(30f, "the longest item name");
        widths[2].Should().Be(40f, "a very long value is capped");
    }

    private static IReadOnlyDictionary<string, object?> Row(decimal qty,
        string item, string note) =>
        new Dictionary<string, object?>
        {
            ["Qty"] = qty,
            ["Item"] = item,
            ["Note"] = note
        };
}
