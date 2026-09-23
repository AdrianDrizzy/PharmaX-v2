using FluentAssertions;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Desktop.Features.Inventory;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// The product editor's filing fields, against the real form.
/// </summary>
/// <remarks>
/// Constructed rather than driven through UI automation, which needs a
/// desktop and a signed-in application. What matters here is the round
/// trip the presenter relies on: the lists go in, a product's current
/// filing is selected from them, and Collect hands back the identifiers
/// rather than the names the operator read.
/// </remarks>
public class ProductEditFilingTests
{
    private static readonly ReferenceItemDto Dispensary =
        new(Guid.NewGuid(), "DISP", "Dispensary", null, true);

    private static readonly ReferenceItemDto FrontShop =
        new(Guid.NewGuid(), "FRONT", "Front shop", null, true);

    private static readonly ReferenceItemDto Analgesics =
        new(Guid.NewGuid(), "ANALG", "Analgesics", null, true);

    private static readonly ReferenceItemDto Adcock =
        new(Guid.NewGuid(), "ADCOCK", "Adcock Ingram", null, true);

    [Fact]
    public void The_editor_offers_a_dropdown_for_each_list()
    {
        using var form = new frmProductEdit();

        foreach (var name in new[]
                 { "cboDepartment", "cboCategory", "cboManufacturer" })
        {
            var found = form.Controls.Find(name, searchAllChildren: true);

            found.Should().ContainSingle($"{name} has to exist");

            found[0].Should().BeOfType<ComboBox>(
                "these are lists the pharmacy maintains, so they are "
                + "chosen rather than typed");
        }
    }

    [Fact]
    public void A_products_filing_is_selected_from_the_lists_it_was_given()
    {
        using var form = new frmProductEdit();

        form.ShowFilingOptions(
            new[] { Dispensary, FrontShop },
            new[] { Analgesics },
            new[] { Adcock });

        form.ShowProduct(Product(FrontShop.Id, Analgesics.Id, Adcock.Id));

        var collected = form.Collect();

        collected.DepartmentId.Should().Be(FrontShop.Id);
        collected.CategoryId.Should().Be(Analgesics.Id);
        collected.ManufacturerId.Should().Be(Adcock.Id);
    }

    /// <remarks>
    /// Filing is optional, so a blank entry has to be reachable and has
    /// to come back as null rather than as the first real entry.
    /// </remarks>
    [Fact]
    public void An_unfiled_product_stays_unfiled()
    {
        using var form = new frmProductEdit();

        form.ShowFilingOptions(new[] { Dispensary }, new[] { Analgesics },
                               new[] { Adcock });

        form.ShowProduct(Product(null, null, null));

        var collected = form.Collect();

        collected.DepartmentId.Should().BeNull();
        collected.CategoryId.Should().BeNull();
        collected.ManufacturerId.Should().BeNull();
    }

    /// <remarks>
    /// A department retired since the product was filed under it is not
    /// on the list any more. Showing the first entry instead would move
    /// the product silently the next time somebody pressed Save.
    /// </remarks>
    [Fact]
    public void Filing_against_something_no_longer_listed_falls_back_to_blank()
    {
        using var form = new frmProductEdit();

        form.ShowFilingOptions(new[] { Dispensary }, new[] { Analgesics },
                               new[] { Adcock });

        form.ShowProduct(Product(Guid.NewGuid(), Analgesics.Id, Adcock.Id));

        form.Collect().DepartmentId.Should().BeNull();
    }

    private static ProductDto Product(Guid? department, Guid? category,
                                      Guid? manufacturer) =>
        new(Id: Guid.NewGuid(),
            Code: "TEST",
            Description: "Test product",
            GenericName: null,
            RetailPrice: 10m,
            CostPrice: 5m,
            VatRatePercent: 15m,
            TaxCode: "A",
            Schedule: 0,
            IsBatchTracked: false,
            IsActive: true,
            ReorderLevel: 1m,
            QuantityOnHand: 3m,
            PackSize: null,
            Barcodes: Array.Empty<string>(),
            DepartmentId: department,
            CategoryId: category,
            ManufacturerId: manufacturer);
}
