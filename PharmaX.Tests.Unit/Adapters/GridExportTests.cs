using FluentAssertions;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// The projection behind the accounts and ledger exports: a grid as the
/// report result the workbook writer takes.
/// </summary>
/// <remarks>
/// The file dialog is not testable without a desktop, but the projection
/// is all the logic there is - and getting it wrong is how an export
/// comes out with the wrong columns, the display text instead of the
/// figures, or a blank row on the end.
/// </remarks>
public class GridExportTests
{
    [Fact]
    public void The_visible_columns_become_the_headings_in_display_order()
    {
        using var grid = Grid();

        var result = GridExport.Build(grid, "Trial Balance", "As at today",
                                      "test.user");

        result.Columns.Select(c => c.Header).Should()
              .ContainInOrder("Code", "Account", "Debit");

        result.Columns.Should().NotContain(c => c.Header == "Internal",
            "a hidden column is not on the screen, so it is not in the "
            + "workbook either");
    }

    /// <remarks>
    /// The format and the alignment are what make a money column read as
    /// money once it is in Excel.
    /// </remarks>
    [Fact]
    public void A_money_column_keeps_its_format_and_its_alignment()
    {
        using var grid = Grid();

        var debit = GridExport.Build(grid, "Trial Balance", "", "test.user")
            .Columns.Single(c => c.Header == "Debit");

        debit.Format.Should().Be("N2");
        debit.RightAlign.Should().BeTrue();
    }

    /// <remarks>
    /// The underlying value, not the string the cell painted. A decimal
    /// exported as "1,500.00" is a column nobody can total.
    /// </remarks>
    [Fact]
    public void The_rows_carry_the_values_not_the_text()
    {
        using var grid = Grid();

        var result = GridExport.Build(grid, "Trial Balance", "", "test.user");

        result.Rows.Should().HaveCount(2);
        result.Rows[0]["Code"].Should().Be("1000");
        result.Rows[0]["Debit"].Should().Be(1500m);
        result.Rows[1]["Debit"].Should().Be(240.50m);
    }

    [Fact]
    public void The_provenance_says_who_asked_and_what_for()
    {
        using var grid = Grid();

        var result = GridExport.Build(grid, "Trial Balance",
            "As at 20/09/2026", "test.manager");

        result.Title.Should().Be("Trial Balance");
        result.Provenance.RunByUsername.Should().Be("test.manager");
        result.Provenance.ParameterSummary.Should().Be("As at 20/09/2026");
        result.Provenance.DataCurrency.Should().Be("As shown on screen");
    }

    private static DataGridView Grid()
    {
        var grid = new DataGridView { AllowUserToAddRows = false };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Code", HeaderText = "Code"
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name", HeaderText = "Account"
        });

        var debit = new DataGridViewTextBoxColumn
        {
            Name = "Debit", HeaderText = "Debit"
        };

        debit.DefaultCellStyle.Format = "N2";
        debit.DefaultCellStyle.Alignment =
            DataGridViewContentAlignment.MiddleRight;

        grid.Columns.Add(debit);

        // Hidden: present on the grid, absent from the export.
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Internal", HeaderText = "Internal", Visible = false
        });

        grid.Rows.Add("1000", "Bank", 1500m, "hidden");
        grid.Rows.Add("1100", "Trade Debtors", 240.50m, "hidden");

        return grid;
    }
}
