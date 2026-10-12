using FluentAssertions;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Legacy;
using PharmaX.Persistence;
using PharmaX.Reporting.Definitions;

namespace PharmaX.Tests.Integration.Reporting;

/// The RSI history counts in the reports alongside PharmaX's own figures.
public sealed class HistoryReportTests : IDisposable
{
    private readonly PharmaXFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Rsi_sales_and_scripts_group_into_top_sellers_and_doctors()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        var day = new DateTime(2026, 3, 10, 9, 0, 0);
        db.LegacyRecords.AddRange(
            LegacyRecord.Create("RSI", "Sale", "1", day, "PANADO", "{}", "P1", null, "CASH", 2m, 10m),
            LegacyRecord.Create("RSI", "Sale", "2", day, "PANADO", "{}", "P1", null, "CASH", 1m, 5m),
            LegacyRecord.Create("RSI", "Sale", "3", day, "ASPIRIN", "{}", "A1", null, "CARD", 1m, 3m),
            LegacyRecord.Create("RSI", "Script", "9", day, "AMOXIL", "{}", "X1", null, "CLINIC", 21m, 40m),
            LegacyRecord.Create("RSI", "Sale", "4", day.AddYears(-1), "OUT OF RANGE", "{}", "P1", null, "CASH", 9m, 99m));
        await db.SaveChangesAsync();

        var report = new HistoryReport(db);

        var top = await report.RunAsync(Request("Sale", "product"));
        top.Rows.Select(r => r["Key"]).Should().Equal("P1  PANADO", "A1  ASPIRIN");
        top.Rows[0]["Value"].Should().Be(15m);
        top.Rows[0]["Rsi"].Should().Be(2);
        top.Totals["Value"].Should().Be(18m);

        var doctors = await report.RunAsync(Request("Script", "party"));
        doctors.Rows.Should().ContainSingle().Which["Key"].Should().Be("CLINIC");

        // PharmaX's own till discounts and drawer openings, read off the audit trail.
        (await report.RunAsync(Request("Override", "none"))).Rows.Should().BeEmpty();
        (await report.RunAsync(Request("NoSale", "none"))).Rows.Should().BeEmpty();
    }

    private static ResolvedRequest Request(string kind, string groupBy) =>
        new(new ReportPreset("t", "t", "History", new Dictionary<string, string?>(), Array.Empty<string>(), ""),
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "MAIN",
            new Dictionary<string, string?> { ["kind"] = kind, ["groupBy"] = groupBy },
            new ReportProvenance("test", DateTime.Now, "", ""));
}
