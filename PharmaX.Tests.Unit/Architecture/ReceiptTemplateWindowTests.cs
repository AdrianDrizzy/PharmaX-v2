using FluentAssertions;
using PharmaX.Adapters.Printing;
using PharmaX.Application.Sales;
using PharmaX.Desktop.Features.Admin;

namespace PharmaX.Tests.Unit.Architecture;

public class ReceiptTemplateWindowTests
{
    private static readonly ReceiptHeaderDto Heading = new("BethRapha Pharmacy", "",
        "28 Woodstock Road\nThorngrove\nBulawayo", "1234567", "", "Branch 1 Till 1",
        "cashier", DateTime.Now, TillNumber: "1", CurrencyCode: "USD",
        Telephone: "029 123 4567", WhatsApp: "077 123 4567");

    [Fact]
    public void The_window_starting_fresh_prints_the_whole_heading()
    {
        using var window = new frmReceiptTemplate();
        var template = window.Current();

        template.Address.Should().BeTrue();
        template.VatNumber.Should().BeTrue();

        using var document = new ReceiptDocument(frmReceiptTemplate.SampleSale(),
            Heading with { Template = template });
        var texts = document.Rows().Select(r => r.Text).ToList();

        texts.Should().ContainInOrder("BethRapha Pharmacy", "28 Woodstock Road", "Thorngrove",
            "Bulawayo", "VAT No: 1234567", "Tel: 029 123 4567", "WhatsApp: 077 123 4567");
    }

    [Fact]
    public void Everything_made_up_on_the_sample_can_be_taken_off()
    {
        using var window = new frmReceiptTemplate();
        window.SampleItemTexts().Should().HaveCount(5);

        // The script takes its charge, reference and medical aid share with it.
        window.RemoveSampleItem(window.SampleItemTexts().ToList()
            .FindIndex(t => t.StartsWith("Script", StringComparison.Ordinal)));

        var texts = Texts(window);
        texts.Should().NotContain(t => t.Contains("Script", StringComparison.Ordinal));
        texts.Should().NotContain(t => t.Contains("Claimed from", StringComparison.Ordinal));
        texts.Should().Contain(t => t.Contains("Panado", StringComparison.Ordinal));

        window.ClearSample();

        window.SampleItemTexts().Should().BeEmpty();
        texts = Texts(window);
        texts.Should().NotContain(t => t.Contains("Panado", StringComparison.Ordinal)
                                       || t.Contains("Vitamin", StringComparison.Ordinal)
                                       || t.Contains("Patient", StringComparison.Ordinal));
    }

    private static List<string> Texts(frmReceiptTemplate window)
    {
        using var document = new ReceiptDocument(window.Sample(),
            Heading with { Template = window.Current() });
        return document.Rows().Select(r => r.Text).ToList();
    }
}
