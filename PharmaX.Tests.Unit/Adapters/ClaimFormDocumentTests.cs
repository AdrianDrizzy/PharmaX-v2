using FluentAssertions;
using PharmaX.Adapters.Printing;
using PharmaX.Application.Claims;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// Draws the A4 claim form onto bitmaps the size of the page and looks at
/// what landed on them.
/// </summary>
public class ClaimFormDocumentTests
{
    // A4 in hundredths of an inch, as the document lays itself out.
    private const int Width = 827;
    private const int Height = 1169;

    private static readonly Rectangle Margins =
        new(40, 35, Width - 80, Height - 70);

    [Fact]
    public void The_form_is_a_front_and_a_back()
    {
        using var document = new ClaimFormDocument(Form());
        using var page = new Bitmap(Width, Height);
        using var g = Graphics.FromImage(page);

        document.RenderPage(g, Margins, 0).Should().BeTrue("a back follows");
        document.RenderPage(g, Margins, 1).Should().BeFalse("and ends it");
    }

    /// <remarks>
    /// Headed in words, with no logo: the pharmacy's name starts at the
    /// left margin, where a picture would otherwise have been.
    /// </remarks>
    [Fact]
    public void The_form_is_headed_by_the_pharmacy_name_at_the_margin()
    {
        using var front = Render(Form(), 0);

        FirstInkColumn(front, 35, 60).Should().BeLessThan(60,
            "the name is drawn from the margin, not beside a logo");
    }

    [Fact]
    public void A_script_longer_than_the_stationery_still_prints_every_line()
    {
        var form = Form() with
        {
            Lines = Enumerable.Range(1, 12)
                .Select(i => new ClaimFormLineDto(i, $"Medicine {i}",
                    $"M{i:000}", 30m, 10m, 8m, "One daily"))
                .ToList()
        };

        using var front = Render(form, 0);
        using var five = Render(Form(), 0);

        Ink(front).Should().BeGreaterThan(Ink(five),
            "twelve lines draw more than one");
    }

    [Theory]
    [InlineData("Grace Tariro", "G T")]
    [InlineData("  john   kudzai ", "J K")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Initials_are_the_first_letter_of_each_name(string? names,
                                                           string initials) =>
        ClaimFormService.Initials(names).Should().Be(initials);

    // ---- Steps -----------------------------------------------------------

    private static Bitmap Render(ClaimFormDto form, int pageNumber)
    {
        var page = new Bitmap(Width, Height);

        using var g = Graphics.FromImage(page);
        g.Clear(Color.White);

        using var document = new ClaimFormDocument(form);
        document.RenderPage(g, Margins, pageNumber);

        return page;
    }

    private static int Ink(Bitmap page)
    {
        var counted = 0;

        for (var y = 0; y < page.Height; y += 2)
        {
            for (var x = 0; x < page.Width; x += 2)
            {
                if (IsDark(page.GetPixel(x, y))) counted++;
            }
        }

        return counted;
    }

    /// The first column with anything dark in it, within a band of rows.
    private static int FirstInkColumn(Bitmap page, int fromY, int toY)
    {
        for (var x = 0; x < page.Width; x++)
        {
            for (var y = fromY; y < toY; y++)
            {
                if (IsDark(page.GetPixel(x, y))) return x;
            }
        }

        return page.Width;
    }

    private static bool IsDark(Color pixel) =>
        pixel.R < 100 && pixel.G < 100 && pixel.B < 100;

    private static ClaimFormDto Form() => new(
        ClaimNumber: "CLM-001-000007",
        ClaimedOn: new DateOnly(2026, 9, 25),
        PharmacyName: "Riverside Pharmacy",
        PharmacyAddress: "1 Example Street\nHarare",
        PharmacyNumber: "P00123",
        VatNumber: "4123456789",
        SchemeName: "Cimas",
        PatientName: "Grace Chirwa",
        MemberSurname: "Chirwa",
        MemberInitials: "G",
        MembershipNumber: "MEM-0004",
        DependantCode: "00",
        DateOfBirth: new DateOnly(1985, 4, 12),
        Telephone: "0771234567",
        ScriptNumber: "SCR-001-000013",
        DoctorName: "Dr Adams",
        DoctorNumber: "MP123456",
        DispensingNumber: "DSP-001-000021",
        DispensedOn: new DateOnly(2026, 9, 25),
        IsRepeat: false,
        RepeatsLeft: 1,
        Lines: new[]
        {
            new ClaimFormLineDto(1, "Amoxicillin 500mg capsules", "AMOX50",
                21m, 180m, 150m, "One capsule three times a day")
        },
        Gross: 180m,
        Claimed: 150m,
        CoPayment: 30m,
        CurrencySymbol: "$");
}
