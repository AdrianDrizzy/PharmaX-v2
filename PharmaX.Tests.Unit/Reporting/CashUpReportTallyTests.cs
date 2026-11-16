using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Application.Tills;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Tills;
using PharmaX.Persistence;
using PharmaX.Persistence.Interceptors;
using PharmaX.Reporting.Definitions;
using PharmaX.Reporting.Engine;

namespace PharmaX.Tests.Unit.Reporting;

/// <summary>
/// Sales Summary and Daily Sales must not read short of the till
/// cash-up: account payments taken at the till belong in both.
/// </summary>
public sealed class CashUpReportTallyTests : IDisposable
{
    private static readonly DateTime Noon =
        new(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);

    private static readonly ReportProvenance Provenance =
        new("test", Noon, "", "");

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public CashUpReportTallyTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Sales_summary_net_includes_account_payments_like_the_cash_up()
    {
        await using var db = Database();
        var (session, sales, returns, payments) = await SeedShiftAsync(db);

        var shift = ShiftTally.Build(session, sales, returns, payments,
            new Dictionary<Guid, string> { [session.OpenedBy] = "Thandi" },
            new Dictionary<Guid, string>());

        var day = DateOnly.FromDateTime(Noon.ToLocalTime());
        var summary = await new SalesSummaryReport(db).RunAsync(Request(day));

        Convert.ToDecimal(summary.Totals["Sales"]).Should().Be(80m,
            "account payments must not inflate the Sales column");
        Convert.ToDecimal(summary.Totals["Account"]).Should().Be(20m);
        Convert.ToDecimal(summary.Totals["Net"]).Should().Be(shift.NetTakings);
        Convert.ToDecimal(summary.Totals["AccountPayments"]).Should().Be(20m);
        summary.Figures.Should().Contain(f => f.Label == "Account payments");
    }

    [Fact]
    public async Task Daily_sales_net_takings_includes_account_payments()
    {
        await using var db = Database();
        var (session, sales, returns, payments) = await SeedShiftAsync(db);

        var shift = ShiftTally.Build(session, sales, returns, payments,
            new Dictionary<Guid, string> { [session.OpenedBy] = "Thandi" },
            new Dictionary<Guid, string>());

        var day = DateOnly.FromDateTime(Noon.ToLocalTime());
        var daily = await new SalesAnalysisReport(db).RunAsync(
            new ResolvedRequest(
                new ReportPreset("pos.sales.daily", "Daily Sales", "SalesAnalysis",
                    new Dictionary<string, string?> { ["groupBy"] = "day" },
                    Array.Empty<string>(), ""),
                day, day, "1",
                new Dictionary<string, string?> { ["groupBy"] = "day" },
                Provenance));

        Convert.ToDecimal(daily.Totals["AccountPayments"]).Should().Be(20m);
        Convert.ToDecimal(daily.Totals["NetTakings"]).Should().Be(shift.NetTakings);
        daily.Figures.Should().Contain(f => f.Label == "Net takings"
            && f.Value == shift.NetTakings.ToString("N2"));
    }

    private static ResolvedRequest Request(DateOnly day,
        string key = SalesSummaryReport.PresetKey, string? groupBy = null) =>
        new(new ReportPreset(key, "Test",
                groupBy is null ? "SalesSummary" : "SalesAnalysis",
                new Dictionary<string, string?>(), Array.Empty<string>(), ""),
            day, day, "1",
            groupBy is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["groupBy"] = groupBy },
            Provenance);

    private async Task<(TillSession Session, List<Sale> Sales,
        List<SaleReturn> Returns, List<AccountTransaction> Payments)>
        SeedShiftAsync(PharmaXDbContext db)
    {
        var cashier = Guid.NewGuid();
        var session = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "2", cashier,
                                       Money.Of(100m), Noon);

        Sale Sell(string description, decimal price, TenderType tender, decimal handed,
                  string number)
        {
            var sale = Sale.Open(session.BranchId, session.WorkstationId, session.Id,
                                 cashier, Noon);
            sale.AddLine(SaleLine.Create(Guid.NewGuid(), description, 1m, Money.Of(price),
                15m, "A", PriceSource.StandardRetail));
            sale.TakePayments(new[] { SalePayment.Create(tender, Money.Of(handed)) });
            sale.Finalise(number, Noon);
            return sale;
        }

        var cash = Sell("Panado 500mg 20s", 20m, TenderType.Cash, 50m, "R1");
        var card = Sell("Vitamin C 1000mg", 15m, TenderType.Card, 15m, "R2");
        var collection = Sell("Amoxicillin 500mg", 45m, TenderType.Cash, 45m, "R3");

        var refund = SaleReturn.Against(cash, ReturnKind.GoodsReturn, session.Id, cashier,
            Guid.NewGuid(), "Damaged box", Noon.AddHours(1));
        refund.AddLine(cash.Lines[0], 1m, 0m, false, DateOnly.FromDateTime(Noon));
        refund.AddRefund(SalePayment.Create(TenderType.Cash, Money.Of(20m)));
        refund.Finalise("CN1", Noon.AddHours(1));

        var account = Account.Open("ACC-1", "Test account", Money.Of(1_000m));
        var payment = AccountTransaction.RecordPayment(account.Id, Money.Of(20m),
            DateOnly.FromDateTime(Noon), TenderType.Cash, session.Id, "AP1");
        payment.CreatedBy = cashier;
        payment.CreatedAtUtc = Noon.AddHours(2);

        db.TillSessions.Add(session);
        db.Sales.AddRange(cash, card, collection);
        db.SaleReturns.Add(refund);
        db.Accounts.Add(account);
        db.AccountTransactions.Add(payment);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (session,
            new List<Sale> { cash, card, collection },
            new List<SaleReturn> { refund },
            new List<AccountTransaction> { payment });
    }

    private PharmaXDbContext Database()
    {
        var db = new PharmaXDbContext(new DbContextOptionsBuilder<PharmaXDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new RowVersionInterceptor())
            .Options);

        db.Database.EnsureCreated();
        return db;
    }
}
