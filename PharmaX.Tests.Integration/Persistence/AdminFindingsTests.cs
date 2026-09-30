using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Application.Security;
using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// What the administrator's end-to-end run found, fixed and proved
/// against a real database.
/// </summary>
public class AdminFindingsTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public AdminFindingsTests(PharmaXFixture fixture) => _fixture = fixture;

    // ---- Stock adjustments (ADM-DEF-05) -------------------------------

    [Fact]
    public async Task A_breakage_takes_stock_off_the_shelf_with_its_reason()
    {
        await SignInAsync();
        var productId = await ProductAsync("ADJ-BRK", "Breakable syrup", 12m);

        using (var scope = _fixture.NewScope())
        {
            var adjustments = PharmaXFixture.Resolve<IStockAdjustmentService>(scope);

            (await adjustments.RecordAsync(new StockAdjustmentEditDto(
                    StockAdjustmentKind.Breakage, "ADJ-BRK", 2m, "")))
                .Error.Code.Should().Be("INV-023", "a write-off needs a reason");

            (await adjustments.RecordAsync(new StockAdjustmentEditDto(
                    StockAdjustmentKind.Breakage, "ADJ-BRK", 20m, "Dropped")))
                .Error.Code.Should().Be("INV-026", "only twelve are on the shelf");

            var result = await adjustments.RecordAsync(new StockAdjustmentEditDto(
                StockAdjustmentKind.Breakage, "ADJ-BRK", 2m, "Dropped a box"));

            result.IsSuccess.Should().BeTrue(result.Error.Message);
            result.Value.QuantityOnHand.Should().Be(10m);
        }

        using var check = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        var movement = await db.StockMovements.AsNoTracking()
            .SingleAsync(m => m.ProductId == productId
                              && m.MovementType == MovementType.Disposal);

        movement.Quantity.Should().Be(-2m);
        movement.ReasonCode.Should().StartWith("BREAKAGE");
    }

    [Fact]
    public async Task Stock_is_transferred_out_of_a_batch_and_in_as_one()
    {
        await SignInAsync();
        var productId = await ProductAsync("ADJ-TRF", "Transferred capsules",
            0m, batches: new[] { ("TRF-A", 6, 8m) });

        Guid batchId;

        using (var scope = _fixture.NewScope())
        {
            var adjustments = PharmaXFixture.Resolve<IStockAdjustmentService>(scope);

            var found = await adjustments.FindAsync("ADJ-TRF");
            batchId = found.Value.Batches.Single().BatchId;

            (await adjustments.RecordAsync(new StockAdjustmentEditDto(
                    StockAdjustmentKind.TransferOut, "ADJ-TRF", 3m,
                    "Short at the branch", BatchId: batchId)))
                .Error.Code.Should().Be("INV-024", "it has to say which branch");

            var sent = await adjustments.RecordAsync(new StockAdjustmentEditDto(
                StockAdjustmentKind.TransferOut, "ADJ-TRF", 3m,
                "Short at the branch", BatchId: batchId, OtherBranch: "BR2"));

            sent.IsSuccess.Should().BeTrue(sent.Error.Message);
            sent.Value.QuantityOnHand.Should().Be(5m);

            var clock = PharmaXFixture.Resolve<IClock>(scope);

            var received = await adjustments.RecordAsync(new StockAdjustmentEditDto(
                StockAdjustmentKind.TransferIn, "ADJ-TRF", 4m,
                "Returned surplus", OtherBranch: "BR3", BatchNumber: "TRF-B",
                Expiry: clock.Today.AddMonths(9)));

            received.IsSuccess.Should().BeTrue(received.Error.Message);
            received.Value.QuantityOnHand.Should().Be(9m);
        }

        using var check = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        var batches = await db.Batches.AsNoTracking()
            .Where(b => b.ProductId == productId)
            .ToListAsync();

        batches.Single(b => b.BatchNumber == "TRF-A").QuantityOnHand.Should().Be(5m);
        batches.Single(b => b.BatchNumber == "TRF-B").QuantityOnHand.Should().Be(4m);

        var kinds = await db.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId)
            .Select(m => m.MovementType)
            .ToListAsync();

        kinds.Should().Contain(MovementType.TransferOut)
            .And.Contain(MovementType.TransferIn);
    }

    [Fact]
    public async Task Only_an_expired_batch_is_written_off_as_expired()
    {
        await SignInAsync();
        await ProductAsync("ADJ-EXP", "Expiring drops", 0m,
            batches: new[] { ("EXP-OLD", -1, 3m), ("EXP-NEW", 12, 5m) });

        using var scope = _fixture.NewScope();
        var adjustments = PharmaXFixture.Resolve<IStockAdjustmentService>(scope);

        var found = (await adjustments.FindAsync("ADJ-EXP")).Value;
        var old = found.Batches.Single(b => b.BatchNumber == "EXP-OLD");
        var fresh = found.Batches.Single(b => b.BatchNumber == "EXP-NEW");

        old.IsExpired.Should().BeTrue();

        (await adjustments.RecordAsync(new StockAdjustmentEditDto(
                StockAdjustmentKind.ExpiredWriteOff, "ADJ-EXP", 1m,
                "Found at the back", BatchId: fresh.BatchId)))
            .Error.Code.Should().Be("INV-027", "that batch is still in date");

        var written = await adjustments.RecordAsync(new StockAdjustmentEditDto(
            StockAdjustmentKind.ExpiredWriteOff, "ADJ-EXP", 3m,
            "Past expiry", BatchId: old.BatchId));

        written.IsSuccess.Should().BeTrue(written.Error.Message);
        written.Value.QuantityOnHand.Should().Be(5m);
    }

    // ---- Purchasing (ADM-DEF-06, ADM-DEF-07) --------------------------

    [Fact]
    public async Task What_to_order_counts_what_is_already_on_order()
    {
        await SignInAsync();
        var supplierId = await SupplierAsync("ADM-SUP1", "Ordering Co");

        // Below its reorder level of 10 with 2 on hand.
        await ProductAsync("ADJ-ORD", "Reordered tablets", 2m, reorderLevel: 10m);

        using (var scope = _fixture.NewScope())
        {
            var purchasing = PharmaXFixture.Resolve<IPurchasingService>(scope);

            var before = await purchasing.SuggestAsync();
            before.Value.Single(l => l.ProductCode == "ADJ-ORD")
                .QuantityOrdered.Should().Be(18m, "twice the level less what is here");

            (await purchasing.RaiseAsync(new PurchaseOrderEditDto(supplierId,
                    null, new[] { new PurchaseOrderLineEditDto("ADJ-ORD", 18m, 5m) })))
                .IsSuccess.Should().BeTrue();
        }

        using (var scope = _fixture.NewScope())
        {
            var purchasing = PharmaXFixture.Resolve<IPurchasingService>(scope);

            (await purchasing.SuggestAsync()).Value
                .Should().NotContain(l => l.ProductCode == "ADJ-ORD",
                    "eighteen are already coming");
        }
    }

    [Fact]
    public async Task More_than_an_order_line_still_expects_is_refused()
    {
        await SignInAsync();
        var supplierId = await SupplierAsync("ADM-SUP2", "Over Co");
        await ProductAsync("ADJ-OVR", "Over-delivered item", 0m);

        Guid orderId;
        int lineNo;

        using (var scope = _fixture.NewScope())
        {
            var raised = await PharmaXFixture.Resolve<IPurchasingService>(scope)
                .RaiseAsync(new PurchaseOrderEditDto(supplierId, null,
                    new[] { new PurchaseOrderLineEditDto("ADJ-OVR", 5m, 2m) }));

            orderId = raised.Value.Id;
            lineNo = raised.Value.Lines[0].LineNo;
        }

        using var check = _fixture.NewScope();
        var receiving = PharmaXFixture.Resolve<IReceivingService>(check);
        var clock = PharmaXFixture.Resolve<IClock>(check);

        var result = await receiving.ReceiveAsync(new GoodsReceiptEditDto(
            supplierId, orderId, "INV-OVER", clock.Today, null,
            new[]
            {
                new GoodsReceiptLineEditDto("ADJ-OVR", 4m, 2m, null, null,
                    PurchaseOrderLineNo: lineNo),
                new GoodsReceiptLineEditDto("ADJ-OVR", 2m, 2m, null, null,
                    PurchaseOrderLineNo: lineNo)
            }));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("GRN-016",
            "four and two is more than the five ordered");
    }

    // ---- Suppliers (ADM-DEF-03, ADM-OBS-04) ---------------------------

    [Fact]
    public async Task A_deactivated_supplier_can_be_brought_back()
    {
        await SignInAsync();
        var supplierId = await SupplierAsync("ADM-SUP3", "Returning Co");

        using var scope = _fixture.NewScope();
        var suppliers = PharmaXFixture.Resolve<ISupplierService>(scope);

        (await suppliers.DeactivateAsync(supplierId)).IsSuccess.Should().BeTrue();
        (await suppliers.GetAsync(supplierId)).Value.IsActive.Should().BeFalse();

        (await suppliers.ReactivateAsync(supplierId)).IsSuccess.Should().BeTrue();
        (await suppliers.GetAsync(supplierId)).Value.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("not-an-email", false)]
    [InlineData("orders@", false)]
    [InlineData("Orders <orders@supplier.co.zw>", false)]
    [InlineData("orders@supplier.co.zw", true)]
    public async Task A_supplier_email_has_to_be_an_address(string email,
        bool accepted)
    {
        await SignInAsync();

        using var scope = _fixture.NewScope();
        var suppliers = PharmaXFixture.Resolve<ISupplierService>(scope);

        var result = await suppliers.CreateAsync(new SupplierEditDto(
            $"EM{Math.Abs(email.GetHashCode(StringComparison.Ordinal)) % 100000}",
            "Email Co", null, null, email, null, 30, 2));

        result.IsSuccess.Should().Be(accepted, result.Error.Message);
        if (!accepted) result.Error.Code.Should().Be("SUP-007");
    }

    // ---- Barcodes (ADM-DEF-04) ----------------------------------------

    [Fact]
    public async Task A_product_takes_barcodes_it_can_then_be_scanned_by()
    {
        await SignInAsync();

        Guid id;

        using (var scope = _fixture.NewScope())
        {
            var products = PharmaXFixture.Resolve<IProductService>(scope);

            var created = await products.CreateAsync(ProductEdit("ADJ-BAR",
                new[] { "6009876543210", "6009876543227" }));

            created.IsSuccess.Should().BeTrue(created.Error.Message);
            id = created.Value;

            (await products.CreateAsync(ProductEdit("ADJ-BAR2",
                    new[] { "6009876543210" })))
                .Error.Code.Should().Be("INV-010", "that barcode is taken");

            (await products.CreateAsync(ProductEdit("ADJ-BAR3",
                    new[] { "12 34" })))
                .IsFailure.Should().BeTrue();
        }

        using var check = _fixture.NewScope();
        var repository = PharmaXFixture.Resolve<Application.Repositories.IProductRepository>(check);

        (await repository.FindByCodeOrBarcodeAsync("6009876543227"))!
            .Id.Should().Be(id);

        (await PharmaXFixture.Resolve<IProductService>(check).GetAsync(id))
            .Value.Barcodes.Should().HaveCount(2);
    }

    // ---- Patients (ADM-OBS-12, ADM-OBS-16) ----------------------------

    [Fact]
    public async Task A_patient_is_found_by_first_name_ID_or_telephone()
    {
        await SignInAsync();

        using (var scope = _fixture.NewScope())
        {
            var created = await PharmaXFixture.Resolve<IPatientService>(scope)
                .CreateAsync(PatientEdit("Mlambo", "Rutendo", "63-123456 A 42",
                    "+263 77 123 4567"));

            created.IsSuccess.Should().BeTrue(created.Error.Message);
        }

        using var check = _fixture.NewScope();
        var patients = PharmaXFixture.Resolve<IPatientService>(check);

        foreach (var term in new[] { "Rutendo", "Rutendo Mlambo",
                     "Mlambo, Rutendo", "63123456A42", "0771234567" })
            (await patients.SearchAsync(term)).Value
                .Should().Contain(p => p.Surname == "Mlambo", term);

        (await patients.SearchAsync("Rutendo Moyo")).Value
            .Should().NotContain(p => p.Surname == "Mlambo",
                "every word has to match");
    }

    [Fact]
    public async Task A_second_file_for_the_same_person_is_caught()
    {
        await SignInAsync();

        using (var scope = _fixture.NewScope())
        {
            (await PharmaXFixture.Resolve<IPatientService>(scope)
                    .CreateAsync(PatientEdit("Sibanda", "Nkosana",
                        "08-765432 B 21", null)))
                .IsSuccess.Should().BeTrue();
        }

        using var check = _fixture.NewScope();
        var patients = PharmaXFixture.Resolve<IPatientService>(check);

        var sameId = await patients.CreateAsync(PatientEdit("Sibanda", "N",
            "08765432B21", null));

        sameId.Error.Code.Should().Be("PAT-014",
            "a national ID belongs to one person");

        (await patients.PossibleDuplicatesAsync(PatientEdit("Sibanda",
                "Nkosana", null, null)))
            .Value.Should().ContainSingle("the same names and birthday");
    }

    // ---- Steps ----------------------------------------------------------

    private static PatientEditDto PatientEdit(string surname, string names,
        string? idNumber, string? telephone) =>
        new(Surname: surname, FirstNames: names, IdNumber: idNumber,
            DateOfBirth: new DateOnly(1990, 5, 17), Telephone: telephone,
            Email: null, WeightKg: null, IsPregnant: false,
            IsBreastfeeding: false, ClinicalNotes: null,
            Allergies: Array.Empty<AllergyDto>(),
            ChronicConditions: Array.Empty<string>());

    private static ProductEditDto ProductEdit(string code,
        IReadOnlyList<string> barcodes) =>
        new(Code: code, Description: $"{code} description", GenericName: null,
            RetailPrice: 10m, CostPrice: 6m, VatRatePercent: 15m, TaxCode: "A",
            Schedule: 0, IsBatchTracked: false, ReorderLevel: 0m,
            PackSize: null, Barcodes: barcodes);

    private async Task<Guid> ProductAsync(string code, string description,
        decimal onHand, decimal reorderLevel = 0m,
        (string Number, int MonthsToExpiry, decimal Quantity)[]? batches = null)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        var product = Product.Create(code, description, Money.Of(10m),
            isBatchTracked: batches is not null);

        product.SetReorderLevel(reorderLevel);
        product.SetQuantityOnHand(batches?.Sum(b => b.Quantity) ?? onHand);
        db.Products.Add(product);

        foreach (var (number, months, quantity) in batches ?? Array.Empty<(string, int, decimal)>())
            db.Batches.Add(Batch.Receive(product.Id, number,
                clock.Today.AddMonths(months), quantity, Money.Of(5m),
                clock.UtcNow));

        await db.SaveChangesAsync();
        return product.Id;
    }

    private async Task<Guid> SupplierAsync(string code, string name)
    {
        using var scope = _fixture.NewScope();

        var created = await PharmaXFixture.Resolve<ISupplierService>(scope)
            .CreateAsync(new SupplierEditDto(code, name, null, null, null,
                null, 30, 2));

        created.IsSuccess.Should().BeTrue(created.Error.Message);
        return created.Value;
    }

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        (await auth.AuthenticateAsync("admin", "ChangeMe#1"))
            .IsSuccess.Should().BeTrue();
    }
}
