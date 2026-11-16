using FluentAssertions;
using NSubstitute;
using PharmaX.Adapters.Printing;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing.Dtos;

namespace PharmaX.Tests.Unit.Adapters;

public class PrintLayoutTests
{
    [Fact]
    public void Default_label_is_the_current_77_by_39_stock()
    {
        DocumentLayout.DefaultLabel.WidthMm.Should().Be(77m);
        DocumentLayout.DefaultLabel.HeightMm.Should().Be(39m);
        DocumentLayout.DefaultLabel.WidthHundredths
            .Should().Be(LabelDocument.WidthHundredths);
        DocumentLayout.DefaultLabel.HeightHundredths
            .Should().Be(LabelDocument.HeightHundredths);
    }

    [Fact]
    public void Custom_layout_keeps_font_spacing_and_side_padding()
    {
        var custom = DocumentLayout.DefaultLabel
            .WithCustom(62m, 29m, fontScale: 120, lineSpacing: 90,
                padTop: 8, padRight: 6, padBottom: 4, padLeft: 10);

        custom.PresetId.Should().Be("custom");
        custom.WidthMm.Should().Be(62m);
        custom.FontScale.Should().BeApproximately(1.2f, 0.001f);
        custom.LineSpacingScale.Should().BeApproximately(0.9f, 0.001f);
        custom.PaddingTop.Should().Be(8);
        custom.PaddingRight.Should().Be(6);
        custom.PaddingBottom.Should().Be(4);
        custom.PaddingLeft.Should().Be(10);

        var margins = custom.ToMargins(0);
        margins.Top.Should().Be(8);
        margins.Right.Should().Be(6);
        margins.Bottom.Should().Be(4);
        margins.Left.Should().Be(10);
    }

    [Fact]
    public void ApplyStock_uses_the_saved_label_layout()
    {
        var layout = new DocumentLayout("label-50x25", 50m, 25m,
            PaddingTop: 4, PaddingRight: 4, PaddingBottom: 4, PaddingLeft: 4);

        using var doc = new System.Drawing.Printing.PrintDocument();
        LabelDocument.ApplyStock(doc.DefaultPageSettings, layout);

        doc.DefaultPageSettings.PaperSize.Width.Should().Be(
            DocumentLayout.MmToHundredths(50m));
        doc.DefaultPageSettings.PaperSize.Height.Should().Be(
            DocumentLayout.MmToHundredths(25m));
        doc.DefaultPageSettings.Margins.Left.Should().Be(4);
    }

    [Fact]
    public void A_draft_layout_on_a_document_does_not_change_saved_layouts()
    {
        PrintLayouts.Label = DocumentLayout.DefaultLabel;
        var draft = new DocumentLayout("label-50x25", 50m, 25m);

        using var doc = new LabelDocument(
            new LabelDto("", "Paracetamol 500mg Tab", 20m, "Take one",
                "1", new DateOnly(2028, 1, 1), "test", "Pre-pack",
                new DateOnly(2026, 10, 19), Array.Empty<string>(), Price: 0.5m),
            layout: draft);

        PrintLayouts.Label.Should().Be(DocumentLayout.DefaultLabel);
        doc.DefaultPageSettings.PaperSize.Width.Should().Be(
            DocumentLayout.MmToHundredths(50m));
    }

    [Fact]
    public async Task Layouts_round_trip_through_workstation_settings()
    {
        var store = new Dictionary<string, string?>(StringComparer.Ordinal);
        var settings = Substitute.For<IWorkstationSettings>();
        settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(store.GetValueOrDefault(ci.ArgAt<string>(0))));
        settings.SetAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                store[ci.ArgAt<string>(0)] = ci.ArgAt<string?>(1);
                return Task.CompletedTask;
            });

        var label = new DocumentLayout("label-62x29", 62m, 29m, 110, 95,
            PaddingTop: 6, PaddingRight: 6, PaddingBottom: 6, PaddingLeft: 6);
        var receipt = new DocumentLayout("receipt-80", 80m, 0m, 100, 100,
            PaddingTop: 5, PaddingRight: 5, PaddingBottom: 5, PaddingLeft: 5);
        var claim = DocumentLayout.DefaultClaim.WithCustom(210m, 297m, 100, 100,
            40, 40, 35, 35);

        await PrintLayouts.SaveAsync(settings, label, receipt, claim);
        PrintLayouts.Label = DocumentLayout.DefaultLabel;
        PrintLayouts.Receipt = DocumentLayout.DefaultReceipt;
        PrintLayouts.Claim = DocumentLayout.DefaultClaim;

        await PrintLayouts.LoadAsync(settings);

        PrintLayouts.Label.Should().Be(label);
        PrintLayouts.Receipt.Should().Be(receipt);
        PrintLayouts.Claim.Should().Be(claim);
    }
}
