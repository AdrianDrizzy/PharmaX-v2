using FluentAssertions;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Features.Inventory;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// The product editor's stock fields, against the real form: Active, On
/// hand, and the batch grid with its batch, expiry, days left and on hand
/// columns - the same columns the product list shows.
/// </summary>
public class ProductEditStockFormTests
{
    private static readonly DateOnly Today =
        DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public void A_product_with_batches_lists_each_one_and_totals_them()
    {
        using var form = new frmProductEdit();
        var near = Guid.NewGuid();
        var far = Guid.NewGuid();

        Show(form, Product(10m,
            new ProductBatchDto(near, "LOT-1", Today.AddDays(20), 20, 4m),
            new ProductBatchDto(far, "LOT-2", Today.AddDays(200), 200, 6m)));

        var grid = Find<DataGridView>(form, "grdBatches");
        var rows = Rows(grid);

        rows.Should().HaveCount(2);
        rows[0].Cells["BatchNumber"].Value.Should().Be("LOT-1");
        rows[0].Cells["DaysLeft"].Value.Should().Be("20");
        rows[0].Cells["QuantityOnHand"].Value.Should().Be("4");

        var onHand = Find<MoneyTextBox>(form, "txtOnHand");
        onHand.ReadOnly.Should().BeTrue("the batches are where it is counted");
        onHand.Amount.Should().Be(10m);

        var collected = form.Collect();

        collected.QuantityOnHand.Should().BeNull();
        collected.Batches.Should().BeEquivalentTo(new[]
        {
            new ProductBatchEditDto(near, "LOT-1", Today.AddDays(20), 4m),
            new ProductBatchEditDto(far, "LOT-2", Today.AddDays(200), 6m)
        });
    }

    [Fact]
    public void Days_left_moves_the_expiry_and_the_expiry_moves_days_left()
    {
        using var form = new frmProductEdit();
        var id = Guid.NewGuid();

        Show(form, Product(4m,
            new ProductBatchDto(id, "LOT-1", Today.AddDays(20), 20, 4m)));

        var row = Rows(Find<DataGridView>(form, "grdBatches"))[0];

        row.Cells["DaysLeft"].Value = "5";

        row.Cells["Expiry"].Value.Should().Be(
            Today.AddDays(5).ToString("dd/MM/yyyy",
                System.Globalization.CultureInfo.CurrentCulture));

        row.Cells["Expiry"].Value = Today.AddDays(45).ToString("yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture);

        row.Cells["DaysLeft"].Value.Should().Be("45");

        row.Cells["QuantityOnHand"].Value = "3";
        row.Cells["BatchNumber"].Value = " LOT-1B ";

        form.Collect().Batches.Should().ContainSingle().Which.Should().Be(
            new ProductBatchEditDto(id, "LOT-1B", Today.AddDays(45), 3m));

        Find<MoneyTextBox>(form, "txtOnHand").Amount.Should().Be(3m,
            "On hand follows the batches");
    }

    /// <remarks>
    /// The box shows the quantity rounded to two places. Sending that
    /// back unasked would post the rounding as a stock count.
    /// </remarks>
    [Fact]
    public void An_untouched_on_hand_is_not_sent_and_a_changed_one_is()
    {
        using var form = new frmProductEdit();

        Show(form, Product(2.125m));

        form.Collect().QuantityOnHand.Should().BeNull();

        Find<MoneyTextBox>(form, "txtOnHand").Text = "9";

        form.Collect().QuantityOnHand.Should().Be(9m);
    }

    [Fact]
    public void Active_is_collected_as_ticked()
    {
        using var form = new frmProductEdit();

        Show(form, Product(0m));

        form.Collect().IsActive.Should().BeTrue();

        Find<CheckBox>(form, "chkActive").Checked = false;

        form.Collect().IsActive.Should().BeFalse();
    }

    [Fact]
    public void The_code_of_an_existing_product_can_be_changed()
    {
        using var form = new frmProductEdit();

        form.LoadProduct(Guid.NewGuid());
        Show(form, Product(0m));

        var code = Find<TextBox>(form, "txtCode");

        code.ReadOnly.Should().BeFalse();
        code.Text = "NEWCODE";

        form.Collect().Code.Should().Be("NEWCODE");
    }

    /// The order the presenter uses: the filing lists, then the product.
    private static void Show(frmProductEdit form, ProductDto product)
    {
        form.ShowFilingOptions(Array.Empty<ReferenceItemDto>(),
            Array.Empty<ReferenceItemDto>(), Array.Empty<ReferenceItemDto>());
        form.ShowProduct(product);
    }

    private static T Find<T>(Form form, string name) where T : Control
    {
        var found = form.Controls.Find(name, searchAllChildren: true);

        found.Should().ContainSingle($"{name} has to exist");
        return found[0].Should().BeAssignableTo<T>().Subject;
    }

    private static List<DataGridViewRow> Rows(DataGridView grid) =>
        grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).ToList();

    private static ProductDto Product(decimal onHand,
        params ProductBatchDto[] batches) =>
        new(Id: Guid.NewGuid(),
            Code: "TEST",
            Description: "Test product",
            GenericName: null,
            RetailPrice: 10m,
            CostPrice: 5m,
            VatRatePercent: 15m,
            TaxCode: "A",
            Schedule: 0,
            IsBatchTracked: batches.Length > 0,
            IsActive: true,
            ReorderLevel: 1m,
            QuantityOnHand: onHand,
            PackSize: null,
            Barcodes: Array.Empty<string>(),
            DepartmentId: null,
            CategoryId: null,
            ManufacturerId: null,
            Batches: batches);
}
