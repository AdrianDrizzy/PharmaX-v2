using FluentAssertions;
using PharmaX.Application.Reporting;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Domain.Security;

namespace PharmaX.Tests.Integration.Workflows;

/// <summary>
/// Daily and Monthly Sales, transaction by transaction and line by line,
/// for those allowed the day's figures.
/// </summary>
public class SalesLinesReportTests : IClassFixture<PharmaXFixture>
{
    private readonly WorkflowSteps _steps;

    public SalesLinesReportTests(PharmaXFixture fixture) =>
        _steps = new WorkflowSteps(fixture);

    [Fact]
    public async Task Every_receipt_is_listed_line_by_line_under_its_days_total()
    {
        await _steps.OpenTillAsync();
        await _steps.ProductAsync("SLRA", "Line report tablets 30s", 30m);
        await _steps.ProductAsync("SLRB", "Line report syrup 100ml", 12.50m);

        await _steps.SignInAsync("slr.cash1", Role.Standard.Cashier);

        var first = await SellAsync(new[] { ("SLRA", 2m), ("SLRB", 1m) },
            new PaymentDto("Cash", 72.50m, null));
        var second = await SellAsync(new[] { ("SLRB", 1m) },
            new PaymentDto("Card", 12.50m, "1234"));

        var today = DateOnly.FromDateTime(DateTime.Today);
        var daily = new ReportRequest("pos.sales.daily.lines", "1", today,
            today, new Dictionary<string, string?>());

        // Not a cashier's to read: the day's takings are management's.
        using (var scope = _steps.Fixture.NewScope())
        {
            var runner = PharmaXFixture.Resolve<IReportRunner>(scope);
            var act = () => runner.RunAsync(daily);
            await act.Should().ThrowAsync<PermissionDeniedException>();
        }

        await _steps.SignInAsync("slr.mgr1", Role.Standard.Manager);

        ReportResult report;

        using (var scope = _steps.Fixture.NewScope())
            report = await PharmaXFixture.Resolve<IReportRunner>(scope)
                .RunAsync(daily);

        report.Title.Should().Be("Daily Sales (Line Items)");

        var receipt = report.Rows
            .Where(row => Equals(row["Document"], first))
            .ToList();

        // The receipt's lines, its details on the first of them.
        receipt.Should().HaveCount(2);
        receipt[0]["Type"].Should().Be("Sale");
        receipt[0]["PaidBy"].Should().Be("Cash 72.50");
        ((string)receipt[0]["Operator"]!).Should().NotBeNullOrWhiteSpace();
        receipt[0]["Code"].Should().Be("SLRA");
        receipt[0]["Quantity"].Should().Be(2m);
        receipt[0]["UnitPrice"].Should().Be(30m);
        receipt[0]["Total"].Should().Be(60m);
        receipt[1]["Type"].Should().BeNull("the receipt's details are on its first line");
        receipt[1]["Document"].Should().Be(first, "every line still names its receipt");
        receipt[1]["Total"].Should().Be(12.50m);

        report.Rows.Where(row => Equals(row["Document"], second))
            .Should().ContainSingle()
            .Which["PaidBy"].Should().Be("Card 12.50");

        // Each day ends with its total, and the totals are the lines'.
        var lines = report.Rows.Where(row => row["Document"] is string).ToList();
        var dayTotal = report.Rows.Single(row => Equals(row["Type"], "Day total"));

        dayTotal["Total"].Should().Be(lines.Sum(row => (decimal)row["Total"]!));
        dayTotal["Vat"].Should().Be(lines.Sum(row => (decimal)row["Vat"]!));
        ((string)dayTotal["PaidBy"]!).Should().Contain("Cash").And.Contain("Card");

        report.Rows[^1]["Type"].Should().Be("TOTAL");
        report.Rows[^1]["Total"].Should().Be(dayTotal["Total"]);

        // The month's version adds the month's total.
        using (var scope = _steps.Fixture.NewScope())
        {
            var monthly = await PharmaXFixture.Resolve<IReportRunner>(scope)
                .RunAsync(new ReportRequest("pos.sales.monthly.lines", "1",
                    new DateOnly(today.Year, today.Month, 1), today,
                    new Dictionary<string, string?>()));

            monthly.Title.Should().Be("Monthly Sales (Line Items)");
            monthly.Rows.Should().Contain(row => Equals(row["Type"], "Month total"));
            monthly.Rows.Where(row => Equals(row["Document"], first))
                .Should().HaveCount(2);
        }

        // Filtered to one tender, the other receipt is gone.
        using (var scope = _steps.Fixture.NewScope())
        {
            var card = await PharmaXFixture.Resolve<IReportRunner>(scope)
                .RunAsync(daily with
                {
                    Slicers = new Dictionary<string, string?> { ["tender"] = "Card" }
                });

            card.Rows.Should().Contain(row => Equals(row["Document"], second));
            card.Rows.Should().NotContain(row => Equals(row["Document"], first));
        }
    }

    private async Task<string> SellAsync((string Code, decimal Quantity)[] items,
                                         PaymentDto payment)
    {
        using var scope = _steps.Fixture.NewScope();
        var sales = PharmaXFixture.Resolve<ISaleService>(scope);

        var sale = await sales.OpenAsync();
        sale.IsSuccess.Should().BeTrue(sale.Error.Message);

        foreach (var (code, quantity) in items)
            (await sales.AddLineAsync(sale.Value.Id, code, quantity))
                .IsSuccess.Should().BeTrue();

        var paid = await sales.FinaliseAsync(sale.Value.Id, new[] { payment });
        paid.IsSuccess.Should().BeTrue(paid.Error.Message);

        return paid.Value.DocumentNumber;
    }
}
