using System.Globalization;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Sales;

/// <summary>
/// The baskets parked at this branch, to pick one to take back up.
/// </summary>
/// <remarks>
/// Hold used to tell the cashier to "recall it from the till session
/// screen", where no such thing existed, so every held basket was
/// orphaned as an open sale nobody could reach (DEF-0014). Any till can
/// recall any held sale: the customer comes back to whichever one is
/// free.
/// </remarks>
public sealed class frmHeldSales : Form
{
    private readonly PhxGrid _grid = new();
    private readonly Button _recall = new();
    private readonly Button _cancel = new();

    public frmHeldSales(IReadOnlyList<HeldSaleDto> held)
    {
        ArgumentNullException.ThrowIfNull(held);

        Name = "frmHeldSales";
        Text = WindowTitles.Brand("PharmaX - Recall held sale");

        _grid.Dock = DockStyle.Fill;
        _grid.Name = "grdHeld";
        _grid.AddColumn("Held", "Held at", 90);
        _grid.AddColumn("Operator", "By", 120);
        _grid.AddColumn("Customer", "Customer", 150);
        _grid.AddColumn("Lines", "Items", 50, rightAlign: true);
        _grid.AddColumn("Total", "Total", 80, "N2", true);

        foreach (var sale in held)
        {
            var row = _grid.Rows.Add(
                sale.HeldAtLocal.ToString("dd/MM HH:mm",
                    CultureInfo.CurrentCulture),
                sale.OperatorName, sale.Customer ?? "Cash sale", sale.Lines,
                sale.Total);

            _grid.Rows[row].Tag = sale.Id;
        }

        _grid.DoubleClick += (_, _) => Choose();

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 48 };

        _recall.Location = new Point(12, 8);
        _recall.Name = "btnRecall";
        _recall.Size = new Size(120, 32);
        _recall.Text = "&Recall";
        _recall.Click += (_, _) => Choose();

        _cancel.DialogResult = DialogResult.Cancel;
        _cancel.Location = new Point(138, 8);
        _cancel.Name = "btnCancel";
        _cancel.Size = new Size(100, 32);
        _cancel.Text = "Cancel";

        buttons.Controls.AddRange(new Control[] { _recall, _cancel });

        Controls.Add(_grid);
        Controls.Add(buttons);

        AcceptButton = _recall;
        CancelButton = _cancel;
        ClientSize = new Size(560, 320);
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
    }

    public Guid? Chosen { get; private set; }

    private void Choose()
    {
        if (_grid.CurrentRow?.Tag is not Guid id) return;

        Chosen = id;
        DialogResult = DialogResult.OK;
        Close();
    }
}
