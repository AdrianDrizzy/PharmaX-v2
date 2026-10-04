using FluentAssertions;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Desktop.Controls;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// How a product reads in the suggestions under Capture script's product
/// box: its name, and then everything the pharmacist checks before
/// writing it on the script.
/// </summary>
public class ScriptProductSuggestionTests
{
    [Fact]
    public void A_suggestion_names_the_product_and_gives_all_its_details()
    {
        var losartan = Product("1850", "LOSARTAN VALOSAR (VARICHEM) 50MG",
            12m, onHand: 8.7m, schedule: 3, pack: "TAB 100", units: 100m);

        var suggestion = Suggest.ProductInFull(losartan);

        suggestion.Text.Should().Be("LOSARTAN VALOSAR (VARICHEM) 50MG");
        suggestion.Value.Should().BeSameAs(losartan,
            "the screen shows the same details beside the item it puts on");

        var parts = suggestion.Detail!.Split("   ·   ");

        parts[0].Should().Be("1850");
        parts[1].Should().Be("TAB 100");
        parts[2].Should().EndWith(" a pack").And.Contain("12.00");
        parts[3].Should().Be($"{8.7m:0.###} on hand");
        parts[4].Should().Be("schedule 3");
    }

    /// <remarks>
    /// A product with no pack size written still says how many units it
    /// holds, where that is known; one with neither leaves the pack out
    /// rather than printing an empty part.
    /// </remarks>
    [Fact]
    public void The_pack_is_described_from_what_is_known_of_it()
    {
        Suggest.ProductDetails(Product("AMOX50", "Amoxicillin 500mg capsules 21s",
                118.40m, onHand: 47.62m, schedule: 4, pack: null, units: 21m))
            .Should().Contain("21 per pack");

        var single = Suggest.ProductDetails(Product("469", "ACETONE", 2m,
            onHand: 10m, schedule: 0, pack: null, units: null));

        single.Split("   ·   ").Should().HaveCount(4);
        single.Should().EndWith("over the counter");
    }

    /// <remarks>
    /// Beside the item, where the code is already in its own column.
    /// </remarks>
    [Fact]
    public void The_details_beside_an_item_leave_out_the_code_already_shown()
    {
        Suggest.ProductDetails(Product("1850", "LOSARTAN VALOSAR (VARICHEM) 50MG",
                12m, onHand: 10m, schedule: 3, pack: "TAB 100", units: 100m),
                withCode: false)
            .Should().StartWith("TAB 100");
    }

    private static ProductListItemDto Product(string code, string description,
        decimal price, decimal onHand, int schedule, string? pack,
        decimal? units) =>
        new(Guid.NewGuid(), code, description, price, onHand, schedule,
            IsBatchTracked: schedule >= 3, IsActive: true,
            IsBelowReorderLevel: false, PackSize: pack, UnitsPerPack: units);
}
