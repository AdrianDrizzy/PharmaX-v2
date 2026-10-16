using FluentAssertions;
using PharmaX.Application.Import;
using PharmaX.Application.Reporting;
using PharmaX.Persistence;
using PharmaX.Reporting.Definitions;
using PharmaX.Tests.Integration.Persistence;

namespace PharmaX.Tests.Integration.Reporting;

/// Imported history counts in the reports as PharmaX's own does, because
/// it is held in the same tables.
public sealed class HistoryReportTests : IDisposable
{
    private readonly PharmaXFixture _fixture = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"pharmax-export-{Guid.NewGuid():N}");

    public void Dispose()
    {
        _fixture.Dispose();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Imported_sales_and_scripts_group_into_top_sellers_doctors_and_cash_ups()
    {
        Directory.CreateDirectory(_folder);
        DataImporterTests.WriteExport(_folder);

        using (var import = _fixture.NewScope())
            (await PharmaXFixture.Resolve<IDataImporter>(import).ImportAsync(new[] { _folder }, dryRun: false))
                .Succeeded.Should().BeTrue();

        using var scope = _fixture.NewScope();
        var report = new HistoryReport(PharmaXFixture.Resolve<PharmaXDbContext>(scope));

        var top = await report.RunAsync(Request("Sale", "product"));
        top.Rows.Select(r => r["Key"]).Should().Equal("9002  DETTOL 250ML", "9001  NIFEDIPINE x30");
        top.Rows[0]["Value"].Should().Be(4.60m);
        top.Totals["Value"].Should().Be(7.60m);

        var doctors = await report.RunAsync(Request("Script", "party"));
        doctors.Rows.Should().ContainSingle().Which["Key"].Should().Be("NCUBE");

        var overrides = await report.RunAsync(Request("Override", "none"));
        overrides.Rows.Should().ContainSingle().Which["Party"].Should().Be("5.00 charged as 4.60");

        var cashUps = await report.RunAsync(Request("DayEnd", "none"));
        cashUps.Rows.Should().ContainSingle().Which["Value"].Should().Be(0m);

        (await report.RunAsync(Request("NoSale", "none"))).Rows.Should().BeEmpty();
    }

    private static ResolvedRequest Request(string kind, string groupBy) =>
        new(new ReportPreset("t", "t", "History", new Dictionary<string, string?>(), Array.Empty<string>(), ""),
            new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31), "MAIN",
            new Dictionary<string, string?> { ["kind"] = kind, ["groupBy"] = groupBy },
            new ReportProvenance("test", DateTime.Now, "", ""));
}
