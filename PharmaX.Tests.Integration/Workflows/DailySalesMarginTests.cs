using FluentAssertions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Reporting;
using PharmaX.Application.Repositories;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;

namespace PharmaX.Tests.Integration.Workflows;

/// <summary>
/// Daily Sales, and the margin it strikes: the turnover it can cost,
/// against the cost of it, with the script co-payments out of both.
/// </summary>
/// <remarks>
/// A collection's line is the patient's share of a dispensing, not the
/// item's price, and the medicine was costed against the dispensing that
/// issued it. Charged again in full against the co-payment, Daily Sales
/// reported a pharmacy trading at a loss on every script it collected.
/// Daily Sales (Line Items) already leaves those lines out of its cost;
/// this is the same fix on the report that totals them.
/// </remarks>
public class DailySalesMarginTests : IClassFixture<PharmaXFixture>
{
    private readonly WorkflowSteps _steps;

    public DailySalesMarginTests(PharmaXFixture fixture) =>
        _steps = new WorkflowSteps(fixture);

    [Fact]
    public async Task The_margin_is_struck_on_the_turnover_it_can_cost()
    {
        await _steps.OpenTillAsync();

        // Both are priced at a cost of six tenths of retail, which is
        // what the step that files them uses.
        await _steps.ProductAsync("DSMA", "Daily margin tablets 30s", 100m);

        await _steps.ProductAsync("DSMB", "Daily margin capsules 21s", 200m,
            schedule: 3, unitsPerPack: 21m,
            batches: new[] { ("DSMB-A", 9, 100m) });

        var patientId = await _steps.PatientAsync("Margin", "Tendai");

        var today = DateOnly.FromDateTime(DateTime.Today);
        var daily = new ReportRequest("pos.sales.daily", "1", today, today,
            new Dictionary<string, string?>());

        var before = await DayAsync(daily, today);

        // One sale over the counter, and one script collected: the day
        // has both kinds of money on it.
        await SellAsync("DSMA", 2m);

        var coPayment = await CollectAsync(patientId);

        coPayment.Should().BeGreaterThan(0m,
            "a collection that cost the patient nothing proves nothing");

        var after = await DayAsync(daily, today);

        after.Sales.Should().Be(before.Sales + 200m + coPayment,
            "the day took the counter sale and the co-payment");

        after.Scripts.Should().Be(before.Scripts + coPayment,
            "and the report says how much of it was a co-payment");

        after.Cost.Should().Be(before.Cost + 120m,
            "the counter sale's cost, and nothing against the collection");

        after.Margin.Should().Be(before.Margin + 80m,
            "200 sold at a cost of 120: the co-payment is out of both");
    }

    /// <remarks>
    /// Daily Sales left refunds out and Daily Sales (Line Items) netted
    /// them, so the two reports of one day disagreed by exactly the day's
    /// refunds (OCT-DEF-03). Goods put back on the shelf bring their cost
    /// back; goods written off do not.
    /// </remarks>
    [Fact]
    public async Task A_refund_comes_off_the_day_and_both_reports_agree()
    {
        await _steps.OpenTillAsync();

        // 50.00 each against a cost of 30.00.
        await _steps.ProductAsync("DSMR", "Daily refund lozenges 24s", 50m);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var daily = new ReportRequest("pos.sales.daily", "1", today, today,
            new Dictionary<string, string?>());
        var lines = daily with { PresetKey = "pos.sales.daily.lines" };

        var before = await DayAsync(daily, today);
        var beforeLines = await LineTotalsAsync(lines);

        var receipt = await SellAsync("DSMR", 3m);

        // One back on the shelf, one written off.
        await RefundAsync(receipt, (1m, true), (1m, false));

        var after = await DayAsync(daily, today);

        after.Sales.Should().Be(before.Sales + 150m - 100m,
            "three sold and two refunded");

        after.Cost.Should().Be(before.Cost + 90m - 30m,
            "the restocked one brings its cost back and the written-off "
            + "one does not");

        after.Margin.Should().Be(before.Margin + 60m - 70m);

        var afterLines = await LineTotalsAsync(lines);

        (after.Sales - before.Sales).Should().Be(
            afterLines.Total - beforeLines.Total,
            "Daily Sales and Daily Sales (Line Items) count the same day");

        (after.Cost - before.Cost).Should().Be(
            afterLines.Cost - beforeLines.Cost);

        (after.Margin - before.Margin).Should().Be(
            afterLines.Margin - beforeLines.Margin);

        // And the markup summary keeps refunds in a band of their own.
        await _steps.SignInAsync("dsm.mgr1", Role.Standard.Manager);

        using var scope = _steps.Fixture.NewScope();

        var bands = await PharmaXFixture.Resolve<IReportRunner>(scope)
            .RunAsync(daily with { PresetKey = "pos.sales.summary.by-markup" });

        bands.Rows.Should().Contain(r =>
            Equals(r["Group"], "7. Refunds and credit notes")
            && (decimal)r["Sales"]! <= -100m);
    }

    // ---- Steps -----------------------------------------------------------

    /// The TOTAL row of Daily Sales (Line Items).
    private async Task<(decimal Total, decimal Cost, decimal Margin)>
        LineTotalsAsync(ReportRequest request)
    {
        await _steps.SignInAsync("dsm.mgr1", Role.Standard.Manager);

        using var scope = _steps.Fixture.NewScope();

        var report = await PharmaXFixture.Resolve<IReportRunner>(scope)
            .RunAsync(request);

        var total = report.Rows.SingleOrDefault(r => Equals(r["Type"], "TOTAL"));

        return total is null
            ? (0m, 0m, 0m)
            : ((decimal)total["Total"]!, (decimal)total["Cost"]!,
               (decimal)total["Margin"]!);
    }

    private async Task RefundAsync(string receipt,
        params (decimal Quantity, bool Restock)[] parts)
    {
        await _steps.SignInAsync("dsm.cash1", Role.Standard.Cashier);

        using var scope = _steps.Fixture.NewScope();
        var returns = PharmaXFixture.Resolve<IReturnService>(scope);
        var users = PharmaXFixture.Resolve<IUserRepository>(scope);

        var found = await returns.FindSaleAsync(receipt);
        found.IsSuccess.Should().BeTrue(found.Error.Message);

        var line = found.Value.Lines.Should().ContainSingle().Subject;

        foreach (var (quantity, restock) in parts)
        {
            var result = await returns.CreateAsync(new ReturnEditDto(
                SaleId: found.Value.SaleId,
                Kind: nameof(ReturnKind.GoodsReturn),
                Reason: restock ? "Unopened, back on the shelf."
                                : "Damaged, written off.",
                AuthorisedBy: (await users.ByUsernameAsync("admin"))!.Id,
                Lines: new[]
                {
                    new ReturnLineEditDto(line.LineNo, quantity, restock, null)
                },
                Refunds: new[] { new PaymentDto("Cash", quantity * line.UnitPrice, null) }));

            result.IsSuccess.Should().BeTrue(result.Error.Message);
        }
    }

    private sealed record Day(decimal Sales, decimal Scripts, decimal Cost,
                              decimal Margin);

    /// <summary>
    /// The day's row of Daily Sales, or nothing where the day has not
    /// traded yet.
    /// </summary>
    private async Task<Day> DayAsync(ReportRequest request, DateOnly day)
    {
        await _steps.SignInAsync("dsm.mgr1", Role.Standard.Manager);

        using var scope = _steps.Fixture.NewScope();

        var report = await PharmaXFixture.Resolve<IReportRunner>(scope)
            .RunAsync(request);

        report.Title.Should().Be("Daily Sales");

        var row = report.Rows.SingleOrDefault(r =>
            Equals(r["Group"], day.ToString("yyyy-MM-dd")));

        return row is null
            ? new Day(0m, 0m, 0m, 0m)
            : new Day((decimal)row["Sales"]!, (decimal)row["Scripts"]!,
                      (decimal)row["Cost"]!, (decimal)row["Margin"]!);
    }

    private async Task<string> SellAsync(string code, decimal quantity)
    {
        await _steps.SignInAsync("dsm.cash1", Role.Standard.Cashier);

        using var scope = _steps.Fixture.NewScope();
        var sales = PharmaXFixture.Resolve<ISaleService>(scope);

        var sale = await sales.OpenAsync();
        sale.IsSuccess.Should().BeTrue(sale.Error.Message);

        (await sales.AddLineAsync(sale.Value.Id, code, quantity))
            .IsSuccess.Should().BeTrue();

        var total = (await sales.GetAsync(sale.Value.Id)).Value.Totals.Total;

        var paid = await sales.FinaliseAsync(sale.Value.Id,
            new[] { new PaymentDto("Cash", total, null) });

        paid.IsSuccess.Should().BeTrue(paid.Error.Message);

        return paid.Value.DocumentNumber;
    }

    /// <summary>
    /// A script dispensed and then collected at the till, which is the
    /// only way a sale is made that has no cost of its own.
    /// </summary>
    private async Task<decimal> CollectAsync(Guid patientId)
    {
        await _steps.SignInAsync("dsm.pharm1", Role.Standard.Pharmacist);

        var scriptId = await _steps.ScriptAsync(patientId,
            ("DSMB", 21m, "One capsule three times a day"));

        Guid dispensingId;

        using (var scope = _steps.Fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            var begun = await dispensing.BeginAsync(scriptId);
            begun.IsSuccess.Should().BeTrue(begun.Error.Message);

            (await dispensing.AddScriptItemsAsync(begun.Value.Id))
                .IsSuccess.Should().BeTrue();

            var issued = await dispensing.IssueAsync(begun.Value.Id);
            issued.IsSuccess.Should().BeTrue(issued.Error.Message);

            dispensingId = begun.Value.Id;
        }

        await _steps.SignInAsync("dsm.cash1", Role.Standard.Cashier);

        using (var scope = _steps.Fixture.NewScope())
        {
            var collections = PharmaXFixture.Resolve<ICollectionService>(scope);
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);

            var started = await collections.StartAsync(dispensingId);
            started.IsSuccess.Should().BeTrue(started.Error.Message);

            var owed = started.Value.Totals.Total;

            var paid = await sales.FinaliseAsync(started.Value.Id,
                new[] { new PaymentDto("Cash", owed, null) });

            paid.IsSuccess.Should().BeTrue(paid.Error.Message);

            return owed;
        }
    }
}
