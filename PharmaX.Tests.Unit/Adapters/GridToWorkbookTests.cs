using ClosedXML.Excel;
using FluentAssertions;
using PharmaX.Application.Reporting;
using PharmaX.Desktop.Mvp;
using PharmaX.Reporting.Exporters;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// A grid on an accounts screen, all the way to a file Excel will open.
/// </summary>
/// <remarks>
/// The two halves are covered on their own - the projection in
/// GridExportTests, the workbook in WorkbookWriterTests - and this is the
/// join between them, which is what the Export button on each of the six
/// screens actually runs once the save dialog has answered.
/// </remarks>
public class GridToWorkbookTests
{
    [Fact]
    public void A_grid_becomes_a_workbook_that_opens_with_its_figures_intact()
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"phx-grid-{Guid.NewGuid():N}.xlsx");

        using var grid = new DataGridView { AllowUserToAddRows = false };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Code", HeaderText = "Code"
        });

        var owes = new DataGridViewTextBoxColumn
        {
            Name = "Balance", HeaderText = "Owes"
        };

        owes.DefaultCellStyle.Format = "N2";
        grid.Columns.Add(owes);

        grid.Rows.Add("ACC-001", 1250.75m);
        grid.Rows.Add("ACC-002", 80m);

        try
        {
            var writer = new ExcelExporter(new NoReportBranding());

            writer.WriteTo(
                GridExport.Build(grid, "Patient Accounts", "Owing only",
                                 "test.manager"),
                path);

            using var workbook = new XLWorkbook(path);
            var cells = workbook.Worksheets.First().RangeUsed()!.Cells()
                .ToList();

            cells.Select(c => c.GetString()).Should()
                 .Contain("Patient Accounts").And.Contain("Owes")
                 .And.Contain("ACC-001");

            cells.Any(c => c.DataType == XLDataType.Number
                           && c.GetDouble() == 1250.75d)
                 .Should().BeTrue(
                     "the balance has to arrive as a number, or nobody "
                     + "can total the column they were sent");

            cells.Single(c => c.DataType == XLDataType.Number
                              && c.GetDouble() == 1250.75d)
                 .Style.NumberFormat.Format.Should().Be("#,##0.00",
                     "the grid's N2 in Excel's terms: Excel offered to "
                     + "repair a workbook carrying N2 as a number format");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("N2", "#,##0.00")]
    [InlineData("N3", "#,##0.000")]
    [InlineData("N0", "#,##0")]
    [InlineData("F1", "0.0")]
    [InlineData("P1", "0.0%")]
    [InlineData("dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm")]
    [InlineData("#,##0.00", "#,##0.00")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_net_format_is_written_as_the_Excel_format_it_means(
        string? format, string? excel) =>
        ExcelExporter.ExcelFormat(format).Should().Be(excel);
}
