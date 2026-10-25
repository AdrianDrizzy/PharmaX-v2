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
}
