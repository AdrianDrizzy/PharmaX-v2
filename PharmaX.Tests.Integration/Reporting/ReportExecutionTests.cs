using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Reporting;
using PharmaX.Application.Security;

namespace PharmaX.Tests.Integration.Reporting;

/// <summary>
/// Runs every preset in the catalogue against the real SQLite database.
/// A report that compiles and then throws at run time is the whole class
/// of bug this catches: SQLite refuses Sum on a decimal, and nothing but
/// executing the query finds that out.
/// </summary>
public class ReportExecutionTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public ReportExecutionTests(PharmaXFixture fixture) => _fixture = fixture;

    public static TheoryData<string> PresetKeys()
    {
        // Built from the catalogue itself, so a preset added later is
        // covered without anyone remembering to add it here.
        using var fixture = new PharmaXFixture();
        using var scope = fixture.NewScope();

        var catalogue = PharmaXFixture.Resolve<IPresetCatalogue>(scope);

        var data = new TheoryData<string>();
        foreach (var preset in catalogue.All()) data.Add(preset.Key);

        return data;
    }

    [Theory]
    [MemberData(nameof(PresetKeys))]
    public async Task Every_preset_executes_against_the_database(string key)
    {
        await SignInAsync();

        using var scope = _fixture.NewScope();
        var runner = PharmaXFixture.Resolve<IReportRunner>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        var request = new ReportRequest(
            PresetKey: key,
            BranchCode: "1",
            From: clock.Today.AddMonths(-1),
            To: clock.Today,
            Slicers: new Dictionary<string, string?>());

        // No data is a perfectly good answer (RPT-011); throwing is not.
        var result = await runner.RunAsync(request);

        result.Should().NotBeNull();
        result.Columns.Should().NotBeEmpty(
            $"{key} must define the columns it reports");

        result.Provenance.ParameterSummary.Should().NotBeEmpty(
            "RPT-012: every report says what was asked");

        result.Provenance.DataCurrency.Should().NotBeEmpty();
    }

    /// <summary>
    /// The date range has to reach the query. A report that ignores it
    /// looks right on an empty database and wrong on a real one.
    /// </summary>
    [Fact]
    public async Task A_range_in_the_future_returns_nothing()
    {
        await SignInAsync();

        using var scope = _fixture.NewScope();
        var runner = PharmaXFixture.Resolve<IReportRunner>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        var result = await runner.RunAsync(new ReportRequest(
            "pos.transactions.all", "1", clock.Today.AddYears(1),
            clock.Today.AddYears(1).AddDays(1),
            new Dictionary<string, string?>()));

        result.RowCount.Should().Be(0);
    }

    [Fact]
    public async Task A_report_can_be_exported_to_every_format()
    {
        await SignInAsync();

        using var scope = _fixture.NewScope();
        var runner = PharmaXFixture.Resolve<IReportRunner>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        foreach (var format in new[]
                 { ExportFormat.Csv, ExportFormat.Excel, ExportFormat.Pdf })
        {
            var path = await runner.ExportAsync(new ReportRequest(
                "inv.stock.on-hand", "1", clock.Today.AddMonths(-1),
                clock.Today, new Dictionary<string, string?>()), format);

            try
            {
                File.Exists(path).Should().BeTrue(
                    $"{format} should have written a file");

                new FileInfo(path).Length.Should().BeGreaterThan(0,
                    $"{format} wrote an empty file");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }
}
