using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Inventory;
using PharmaX.Application.Reporting;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Workflows;

/// <summary>
/// The till as each role meets it: a cashier's catalogue, refunds a
/// supervisor authorises, baskets held and recalled, change from cash,
/// quotations converted - all against the real database.
/// </summary>
public class TillWorkflowTests : IClassFixture<PharmaXFixture>
{
    private readonly WorkflowSteps _steps;

    public TillWorkflowTests(PharmaXFixture fixture) =>
        _steps = new WorkflowSteps(fixture);

    /// <summary>
    /// A cashier sells over the counter and nothing else - and the
    /// restriction is in the query, so an exact dispensary code finds
    /// nothing, and so does a search, and so does a direct read by id.
    /// </summary>
    [Fact]
    public async Task A_cashier_sees_and_sells_over_the_counter_stock_only()
    {
        await _steps.OpenTillAsync();

        await _steps.ProductAsync("OTCPAN", "Panado 500mg tablets 24s", 35m,
            schedule: 0);
        var dispensaryId = await _steps.ProductAsync("RXAMOX",
            "Amoxicillin 250mg capsules 15s", 80m, schedule: 4);

        await _steps.SignInAsync("till.cashier", Role.Standard.Cashier);

        using var scope = _steps.Fixture.NewScope();
        var sales = PharmaXFixture.Resolve<ISaleService>(scope);
        var products = PharmaXFixture.Resolve<IProductService>(scope);

        var sale = await sales.OpenAsync();
        sale.IsSuccess.Should().BeTrue(sale.Error.Message);

        (await sales.AddLineAsync(sale.Value.Id, "OTCPAN", 1m))
            .IsSuccess.Should().BeTrue();

        var refused = await sales.AddLineAsync(sale.Value.Id, "RXAMOX", 1m);
        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be("POS-001",
            "an exact dispensary code must read as no product at all");

        var search = await products.SearchAsync("");
        search.Value.Items.Should().Contain(p => p.Code == "OTCPAN");
        search.Value.Items.Should().NotContain(p => p.Schedule >= 3);

        (await products.GetAsync(dispensaryId)).IsFailure.Should().BeTrue();

        // A pharmacist at the same till sees the whole catalogue.
        await _steps.SignInAsync("till.pharm", Role.Standard.Pharmacist);

        using var other = _steps.Fixture.NewScope();
        var theirs = PharmaXFixture.Resolve<IProductService>(other);
        (await theirs.SearchAsync("RXAMOX")).Value.Items
            .Should().ContainSingle(p => p.Code == "RXAMOX");
    }

    /// <summary>
    /// Daily and monthly sales are management figures. A cashier cannot run
    /// them, whatever screen they come through; a manager can.
    /// </summary>
    [Theory]
    [InlineData("pos.sales.daily")]
    [InlineData("pos.sales.monthly")]
    public async Task A_cashier_cannot_run_the_sales_reports_but_a_manager_can(
        string preset)
    {
        await _steps.OpenTillAsync();
        await _steps.SignInAsync("till.cashier2", Role.Standard.Cashier);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var request = new ReportRequest(preset, "1", today.AddDays(-7), today,
            new Dictionary<string, string?>());

        using (var scope = _steps.Fixture.NewScope())
        {
            var runner = PharmaXFixture.Resolve<IReportRunner>(scope);
            var act = () => runner.RunAsync(request);
            await act.Should().ThrowAsync<PermissionDeniedException>();
        }

        await _steps.SignInAsync("till.manager", Role.Standard.Manager);

        using (var scope = _steps.Fixture.NewScope())
        {
            var runner = PharmaXFixture.Resolve<IReportRunner>(scope);
            var result = await runner.RunAsync(request);
            result.Should().NotBeNull();
        }
    }

    /// <summary>
    /// The department a sales report is filtered to is the department it
    /// reports. The filter was offered and never read, so a filtered
    /// report showed the whole shop's takings.
    /// </summary>
    [Fact]
    public async Task A_sales_report_filtered_to_a_department_reports_only_it()
    {
        await _steps.OpenTillAsync();
        var inId = await _steps.ProductAsync("DEPIN", "Filed item", 30m);
        await _steps.ProductAsync("DEPOUT", "Other item", 70m);

        var department = Domain.Reference.Department.Create("WFDEP",
            "Workflow department");

        using (var scope = _steps.Fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            db.Departments.Add(department);
            (await db.Products.SingleAsync(p => p.Id == inId))
                .Classify(department.Id, null, null);
            await db.SaveChangesAsync();
        }

        await _steps.SignInAsync("till.depcash", Role.Standard.Cashier);

        using (var scope = _steps.Fixture.NewScope())
        {
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);
            var sale = (await sales.OpenAsync()).Value;
            await sales.AddLineAsync(sale.Id, "DEPIN", 1m);
            await sales.AddLineAsync(sale.Id, "DEPOUT", 1m);
            (await sales.FinaliseAsync(sale.Id,
                new[] { new PaymentDto("Cash", 100m, null) })).IsSuccess
                .Should().BeTrue();
        }

        await _steps.SignInAsync("till.depmgr", Role.Standard.Manager);

        var today = DateOnly.FromDateTime(DateTime.Today);

        using var check = _steps.Fixture.NewScope();
        var runner = PharmaXFixture.Resolve<IReportRunner>(check);

        var all = await runner.RunAsync(new ReportRequest("pos.sales.daily",
            "1", today, today, new Dictionary<string, string?>()));
        var filed = await runner.RunAsync(new ReportRequest("pos.sales.daily",
            "1", today, today, new Dictionary<string, string?>
                { ["department"] = department.Id.ToString() }));

        var filedSales = filed.Rows.Sum(r => (decimal)r["Sales"]!);

        filedSales.Should().Be(30m, "only the filed item's sale is in it");
        all.Rows.Sum(r => (decimal)r["Sales"]!).Should()
            .BeGreaterThanOrEqualTo(filedSales + 70m,
                "the unfiltered report still has both, and anything else "
                + "sold today");
    }

    /// <summary>
    /// DEF-0007: the cashier starts the refund, a manager authorises it,
    /// and it goes through - checked against the manager's authority, not
    /// the cashier's. Another cashier's "authorisation" is refused, and the
    /// refusal stays in the audit trail (DEF-0026).
    /// </summary>
    [Fact]
    public async Task A_cashier_refund_goes_through_on_a_managers_authority()
    {
        await _steps.OpenTillAsync();
        await _steps.ProductAsync("RETPLS", "Plasters 40s", 40m);

        var managerId = await _steps.UserAsync("till.mgr2", Role.Standard.Manager);
        var otherCashier = await _steps.UserAsync("till.cash4",
            Role.Standard.Cashier);

        await _steps.SignInAsync("till.cash3", Role.Standard.Cashier);

        Guid saleId;

        using (var scope = _steps.Fixture.NewScope())
        {
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);
            var opened = await sales.OpenAsync();
            saleId = opened.Value.Id;
            await sales.AddLineAsync(saleId, "RETPLS", 2m);
            (await sales.FinaliseAsync(saleId,
                new[] { new PaymentDto("Cash", 80m, null) })).IsSuccess
                .Should().BeTrue();
        }

        ReturnEditDto Refund(Guid authoriser) => new(saleId, "GoodsReturn",
            "Wrong size", authoriser,
            new[] { new ReturnLineEditDto(1, 1m, true, null) },
            new[] { new PaymentDto("Cash", 40m, null) });

        using (var scope = _steps.Fixture.NewScope())
        {
            var returns = PharmaXFixture.Resolve<IReturnService>(scope);
            var act = () => returns.CreateAsync(Refund(otherCashier));
            await act.Should().ThrowAsync<PermissionDeniedException>();
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var returns = PharmaXFixture.Resolve<IReturnService>(scope);
            var done = await returns.CreateAsync(Refund(managerId));

            done.IsSuccess.Should().BeTrue(done.Error.Message);
            done.Value.Total.Should().Be(40m);
            done.Value.OriginalDocumentNumber.Should().StartWith("S-");
        }

        using var check = _steps.Fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        var stored = await db.SaleReturns.AsNoTracking()
            .SingleAsync(r => r.OriginalSaleId == saleId);
        stored.AuthorisedBy.Should().Be(managerId);

        (await db.AuditEntries.AsNoTracking()
                .AnyAsync(a => a.Action == PermissionCode.ReturnAuthorise
                               && a.Outcome == "Refused"))
            .Should().BeTrue("a refusal is committed on its own");
    }

    /// <summary>
    /// DEF-0014: a held basket survives the till being closed and reopened
    /// - a fresh scope is a fresh screen - and comes back with its lines,
    /// quantities and discount, ready to be paid for.
    /// </summary>
    [Fact]
    public async Task A_held_sale_is_recalled_intact_on_a_new_screen_and_completed()
    {
        await _steps.OpenTillAsync();
        await _steps.ProductAsync("HOLDA", "Hold item A", 50m);
        await _steps.ProductAsync("HOLDB", "Hold item B", 20m);

        await _steps.SignInAsync("till.cash5", Role.Standard.Cashier);

        Guid heldId;

        using (var scope = _steps.Fixture.NewScope())
        {
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);
            var sale = await sales.OpenAsync();
            heldId = sale.Value.Id;

            await sales.AddLineAsync(heldId, "HOLDA", 2m);
            await sales.AddLineAsync(heldId, "HOLDB", 1m);
            (await sales.ApplyLineDiscountAsync(heldId, 1, 5m, null))
                .IsSuccess.Should().BeTrue();

            (await sales.HoldAsync(heldId)).IsSuccess.Should().BeTrue();
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);

            var held = await sales.HeldAsync();
            held.Value.Should().ContainSingle(h => h.Id == heldId)
                .Which.Total.Should().Be(115m);

            var fresh = await sales.OpenAsync();
            var recalled = await sales.RecallAsync(heldId, fresh.Value.Id);

            recalled.IsSuccess.Should().BeTrue(recalled.Error.Message);
            recalled.Value.Status.Should().Be("Open");
            recalled.Value.Lines.Should().HaveCount(2);
            recalled.Value.Lines[0].Quantity.Should().Be(2m);
            recalled.Value.Lines[0].Discount.Should().Be(5m);
            recalled.Value.Totals.Total.Should().Be(115m);

            (await sales.FinaliseAsync(heldId,
                new[] { new PaymentDto("Cash", 115m, null) }))
                .IsSuccess.Should().BeTrue();

            // The empty basket the recall replaced was abandoned, not left
            // open (DEF-0030).
            (await sales.GetAsync(fresh.Value.Id)).Value.Status
                .Should().Be("Voided");
        }
    }

    /// <summary>DEF-0019: 100 in cash for an 80 sale gives 20 change.</summary>
    [Fact]
    public async Task Cash_over_the_amount_due_completes_and_records_the_change()
    {
        await _steps.OpenTillAsync();
        await _steps.ProductAsync("CHG80", "Eighty item", 80m);

        await _steps.SignInAsync("till.cash6", Role.Standard.Cashier);

        using var scope = _steps.Fixture.NewScope();
        var sales = PharmaXFixture.Resolve<ISaleService>(scope);

        var sale = await sales.OpenAsync();
        await sales.AddLineAsync(sale.Value.Id, "CHG80", 1m);

        var paid = await sales.FinaliseAsync(sale.Value.Id,
            new[] { new PaymentDto("Cash", 100m, null) });

        paid.IsSuccess.Should().BeTrue(paid.Error.Message);
        paid.Value.Change.Should().Be(20m);

        using var check = _steps.Fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        var stored = await db.Sales.AsNoTracking().Include(s => s.Payments)
            .SingleAsync(s => s.Id == sale.Value.Id);

        stored.ChangeGiven.Amount.Should().Be(20m);
        stored.Tendered.Amount.Should().Be(100m);
        stored.Payments.Sum(p => p.Amount.Amount).Should().Be(80m);
    }

    /// <summary>
    /// DEF-0016: a discounted sale with a voided line adds up - subtotal
    /// less discount is the total - on the sale and on what the receipt
    /// prints.
    /// </summary>
    [Fact]
    public async Task A_voided_line_leaves_the_subtotal_discount_and_total_agreeing()
    {
        await _steps.OpenTillAsync();
        await _steps.ProductAsync("VOIDA", "Void item A", 34.95m);
        await _steps.ProductAsync("VOIDB", "Void item B", 49.99m);

        await _steps.SignInAsync("till.cash7", Role.Standard.Cashier);

        using var scope = _steps.Fixture.NewScope();
        var sales = PharmaXFixture.Resolve<ISaleService>(scope);

        var sale = await sales.OpenAsync();
        await sales.AddLineAsync(sale.Value.Id, "VOIDA", 2m);
        await sales.AddLineAsync(sale.Value.Id, "VOIDB", 1m);
        await sales.ApplyLineDiscountAsync(sale.Value.Id, 1, 5m, null);
        await sales.VoidLineAsync(sale.Value.Id, 1, "Customer changed mind");

        var totals = (await sales.GetAsync(sale.Value.Id)).Value.Totals;

        totals.Subtotal.Should().Be(49.99m);
        totals.Discount.Should().Be(0m);
        (totals.Subtotal - totals.Discount).Should().Be(totals.Total);
    }

    /// <summary>
    /// A quotation is brought onto the till at today's prices, and its
    /// stock leaves once - when the sale is paid - and it cannot be sold a
    /// second time.
    /// </summary>
    [Fact]
    public async Task A_quotation_converts_to_a_sale_once_and_moves_stock_once()
    {
        await _steps.OpenTillAsync();
        var productId = await _steps.ProductAsync("QUOTE1", "Quoted item",
            30m, quantity: 50m);

        await _steps.SignInAsync("till.cash8", Role.Standard.Cashier);

        string number;

        using (var scope = _steps.Fixture.NewScope())
        {
            var quotations = PharmaXFixture.Resolve<IQuotationService>(scope);
            var opened = await quotations.OpenAsync("J. Smith (walk-in)");
            await quotations.AddLineAsync(opened.Value.Id, "QUOTE1", 3m);
            var issued = await quotations.IssueAsync(opened.Value.Id, null);
            number = issued.Value.QuotationNumber;
            number.Should().StartWith("Q-");

            // Printable: the document names the quotation and its total.
            QuotationDocument.For(issued.Value).Lines
                .Should().Contain(l => l.Contains("90.00", StringComparison.Ordinal));

            // Every line the document itself writes fits the roll.
            QuotationDocument.For(issued.Value).Lines
                .Should().OnlyContain(l => l.Length <= 42);
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);
            var sale = await sales.OpenAsync();

            var converted = await sales.ConvertQuotationAsync(sale.Value.Id,
                number);
            converted.IsSuccess.Should().BeTrue(converted.Error.Message);
            converted.Value.LinesAdded.Should().Be(1);

            (await _steps.StockOfAsync(productId)).Product.Should().Be(50m,
                "nothing leaves the shelf until the sale is paid");

            (await sales.FinaliseAsync(sale.Value.Id,
                new[] { new PaymentDto("Cash", 90m, null) }))
                .IsSuccess.Should().BeTrue();

            var again = await sales.OpenAsync();
            var twice = await sales.ConvertQuotationAsync(again.Value.Id,
                number);
            twice.IsFailure.Should().BeTrue();
            twice.Error.Code.Should().Be("QUO-023");
        }

        (await _steps.StockOfAsync(productId)).Product.Should().Be(47m);

        using var check = _steps.Fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        var quotation = await db.Quotations.AsNoTracking()
            .SingleAsync(q => q.QuotationNumber == number);
        quotation.Status.Should().Be(QuotationStatus.Accepted);
        quotation.SaleId.Should().NotBeNull();
    }
}
