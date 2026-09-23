using ClosedXML.Excel;
using FluentAssertions;
using PharmaX.Application.Reporting;

namespace PharmaX.Tests.Integration.Reporting;

/// <summary>
/// The workbook the accounts and ledger screens export to.
/// </summary>
/// <remarks>
/// Read back with ClosedXML rather than checked for a non-zero file
/// length: a corrupt workbook is still a file, and the failure a
/// spreadsheet export actually has is one Excel refuses to open.
/// </remarks>
public class WorkbookWriterTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public WorkbookWriterTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public void A_result_is_written_where_it_was_asked_for_and_opens_again()
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"phx-books-{Guid.NewGuid():N}.xlsx");

        try
        {
            using (var scope = _fixture.NewScope())
            {
                PharmaXFixture.Resolve<IWorkbookWriter>(scope)
                    .WriteTo(TrialBalance(), path);
            }

            File.Exists(path).Should().BeTrue(
                "the writer must use the path it was given, not one of "
                + "its own making");

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.First();

            var used = sheet.RangeUsed();
            used.Should().NotBeNull();

            var text = used!.Cells().Select(c => c.GetString()).ToList();

            // Provenance, headings and figures all reached the sheet.
            text.Should().Contain("Trial Balance");
            text.Should().Contain(t => t.Contains("Run by test.user",
                StringComparison.Ordinal));

            text.Should().Contain("Account");
            text.Should().Contain("Bank");
            text.Should().Contain("Trade Debtors");

            // Written as numbers, not as text that looks like numbers -
            // a column of strings is a column nobody can total.
            sheet.Cells().Any(c => c.DataType == XLDataType.Number
                                   && c.GetDouble() == 1500d)
                 .Should().BeTrue("the debit has to arrive as a number");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void A_result_with_no_rows_still_writes_a_readable_sheet()
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"phx-empty-{Guid.NewGuid():N}.xlsx");

        try
        {
            using (var scope = _fixture.NewScope())
            {
                var empty = TrialBalance() with
                {
                    Rows = Array.Empty<IReadOnlyDictionary<string, object?>>()
                };

                PharmaXFixture.Resolve<IWorkbookWriter>(scope)
                    .WriteTo(empty, path);
            }

            using var workbook = new XLWorkbook(path);

            workbook.Worksheets.First().RangeUsed()!.Cells()
                .Select(c => c.GetString())
                .Should().Contain("Account",
                    "the headings stand even with nothing under them");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static ReportResult TrialBalance() => new(
        Title: "Trial Balance",
        Columns: new[]
        {
            new ReportColumn("Code", "Code"),
            new ReportColumn("Name", "Account"),
            new ReportColumn("Debit", "Debit", "N2", RightAlign: true),
            new ReportColumn("Credit", "Credit", "N2", RightAlign: true)
        },
        Rows: new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Code"] = "1000", ["Name"] = "Bank",
                ["Debit"] = 1500m, ["Credit"] = 0m
            },
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Code"] = "1100", ["Name"] = "Trade Debtors",
                ["Debit"] = 240.50m, ["Credit"] = 0m
            }
        },
        Totals: new Dictionary<string, object?>(StringComparer.Ordinal),
        Provenance: new ReportProvenance(
            RunByUsername: "test.user",
            RunAtLocal: new DateTime(2026, 9, 20, 14, 30, 0,
                                     DateTimeKind.Local),
            ParameterSummary: "As at 20/09/2026",
            DataCurrency: "As shown on screen"));
}
