using FluentAssertions;
using PharmaX.Adapters.Printing;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// Draws a receipt onto a bitmap and looks at what landed on it.
/// </summary>
/// <remarks>
/// The only artefact PharmaX produces that a customer keeps and an
/// auditor reads. Asserting that the drawing code compiles proves
/// nothing about whether the logo and the fiscal particulars reached
/// the paper, and a roll printer is not available to a test.
/// </remarks>
public class ReceiptDocumentTests : IDisposable
{
    private const int Width = 315;
    private const int Height = 900;

    private readonly string _logoPath = Path.Combine(
        Path.GetTempPath(), $"phx-logo-{Guid.NewGuid():N}.png");

    public ReceiptDocumentTests()
    {
        // A block of solid colour is enough: the assertion is whether
        // anything at all was drawn where the logo belongs.
        using var image = new Bitmap(240, 80);
        using (var g = Graphics.FromImage(image)) g.Clear(Color.Black);

        image.Save(_logoPath, System.Drawing.Imaging.ImageFormat.Png);
    }

    public void Dispose()
    {
        if (File.Exists(_logoPath)) File.Delete(_logoPath);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_receipt_prints_the_pharmacy_logo_above_the_name()
    {
        using var withLogo = Render(Header(_logoPath));
        using var without = Render(Header(logoPath: null));

        // A solid block 240 pixels wide, which is what the test logo is.
        // Measured as a run rather than as a count of dark pixels: the
        // band also holds the trading name when there is no logo, and
        // centred text never draws two hundred dark pixels in a row.
        LongestDarkRun(withLogo, 0, 110).Should().BeGreaterThan(200,
            "the logo has to reach the paper");

        LongestDarkRun(without, 0, 110).Should().BeLessThan(200,
            "nothing but the logo draws a solid band");
    }

    /// <remarks>
    /// The particulars move down to make room. If they did not, the logo
    /// would be printing over the pharmacy's name.
    /// </remarks>
    [Fact]
    public void A_logo_pushes_the_particulars_down_rather_than_over_them()
    {
        using var withLogo = Render(Header(_logoPath));
        using var without = Render(Header(logoPath: null));

        FirstInkRow(withLogo).Should().BeLessOrEqualTo(FirstInkRow(without));

        LastInkRow(withLogo).Should().BeGreaterThan(LastInkRow(without),
            "a taller slip is the proof the text was not drawn over");
    }

    private static Bitmap Render(ReceiptHeaderDto header)
    {
        var sale = new SaleDto(Guid.NewGuid(), "S-001-000042",
            new[]
            {
                new SaleLineDto(1, Guid.NewGuid(), "Panado 500mg 20s", 2m,
                    34.95m, 0m, "A", 69.90m, "PAN-A",
                    new DateOnly(2027, 6, 30), false, false)
            },
            new SaleTotalsDto(69.90m, 0m, 9.11m, 69.90m, 100m, 30.10m));

        var page = new Bitmap(Width, Height);

        using (var g = Graphics.FromImage(page))
        {
            g.Clear(Color.White);

            using var document = new ReceiptDocument(sale, header);
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

    private static int FirstInkRow(Bitmap page)
    {
        for (var y = 0; y < page.Height; y++)
        {
            if (Ink(page, y, y + 1) > 0) return y;
        }

        return page.Height;
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
        Address: "1 Example Street",
        VatNumber: "4123456789",
        LicenceNumber: "P00123",
        BranchAndTill: "Branch 1 Till 1",
        OperatorName: "test.admin",
        PrintedAtLocal: new DateTime(2026, 9, 11, 9, 30, 0,
                                     DateTimeKind.Local),
        LogoPath: logoPath);
}
