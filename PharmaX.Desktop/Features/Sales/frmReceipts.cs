using System.Globalization;
using PharmaX.Application.Sales;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Sales;

/// <summary>
/// The day's receipts, newest first, any of which can be printed again.
/// </summary>
/// <remarks>
/// Open to everyone signed in: whoever is at the counter when a customer
/// comes back for a copy should be able to give them one. A copy says
/// "COPY - REPRINTED", carries the original's date and cashier, and the
/// reprint is recorded against whoever printed it.
/// </remarks>
public sealed class frmReceipts : PhxFormBase
{
    private readonly IReceiptHistoryService? _history;
    private readonly IReceiptPrinter? _printer;
    private readonly ReceiptHeaderFactory? _headers;

    private readonly DateTimePicker _day = new();
    private readonly TextBox _find = new();
    private readonly PhxGrid _grid = new();
    private readonly Label _status = new();
    private readonly Button _reprint = new();
    private readonly Button _close = new();
    private IReadOnlyList<ReceiptSummaryDto> _all = Array.Empty<ReceiptSummaryDto>();

    /// Designer and tests only.
    public frmReceipts() => Build();

    public frmReceipts(IReceiptHistoryService history, IReceiptPrinter printer,
                       ReceiptHeaderFactory headers)
    {
        _history = history;
        _printer = printer;
        _headers = headers;
        Build();
    }

    private void Build()
    {
        Text = "PharmaX - Receipts";
        Name = "frmReceipts";
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        var top = new Panel { Dock = DockStyle.Top, Height = 48, Size = new Size(820, 48) };
        top.Controls.Add(new Label { AutoSize = true, Location = new Point(12, 15), Text = "&Day" });
        _day.Format = DateTimePickerFormat.Short;
        _day.Location = new Point(56, 11);
        _day.Name = "dtpDay";
        _day.Size = new Size(140, 23);
        _day.Value = DateTime.Today;
        _day.ValueChanged += async (_, _) => await LoadAsync();

        top.Controls.Add(new Label { AutoSize = true, Location = new Point(220, 15), Text = "&Find" });
        _find.Location = new Point(262, 11);
        _find.Name = "txtFind";
        _find.PlaceholderText = "Receipt number, cashier or amount";
        _find.Size = new Size(300, 23);
        _find.TextChanged += (_, _) => Show();
        top.Controls.AddRange(new Control[] { _day, _find });
        top.Controls.SetChildIndex(_day, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.Name = "grdReceipts";
        _grid.ReadOnly = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AddColumn("Number", "Receipt");
        _grid.AddColumn("Time", "Time");
        _grid.AddColumn("Operator", "Cashier");
        _grid.AddColumn("Tenders", "Paid by");
        _grid.AddColumn("Total", "Total", format: "N2", rightAlign: true);
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await ReprintAsync(); };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, Size = new Size(820, 52) };
        _status.AutoSize = false;
        _status.Location = new Point(12, 16);
        _status.Name = "lblStatus";
        _status.Size = new Size(470, 22);

        _reprint.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _reprint.Location = new Point(536, 10);
        _reprint.Name = "btnReprint";
        _reprint.Size = new Size(150, 32);
        _reprint.Text = "&Reprint";
        _reprint.Click += async (_, _) => await ReprintAsync();

        _close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _close.Location = new Point(694, 10);
        _close.Name = "btnClose";
        _close.Size = new Size(114, 32);
        _close.Text = "&Close";
        _close.Click += (_, _) => Close();

        bottom.Controls.AddRange(new Control[] { _status, _reprint, _close });

        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(bottom);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await LoadAsync();
        _find.Focus();
    }

    private async Task LoadAsync()
    {
        if (_history is null) return;

        try
        {
            SetBusy(true);
            _all = await _history.DayAsync(DateOnly.FromDateTime(_day.Value));
            Show();
        }
#pragma warning disable CA1031 // Reported to the operator and logged.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorHandler.Log(ex);
            ShowError(ErrorHandler.Describe(ex));
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// The day's receipts that match what is typed in Find.
    private new void Show()
    {
        var words = _find.Text.Trim();
        var shown = _all.Where(r => words.Length == 0
            || r.DocumentNumber.Contains(words, StringComparison.OrdinalIgnoreCase)
            || r.Operator.Contains(words, StringComparison.OrdinalIgnoreCase)
            || r.Total.ToString("N2", CultureInfo.CurrentCulture).Contains(words, StringComparison.Ordinal))
            .ToList();

        _grid.Rows.Clear();
        foreach (var r in shown)
            _grid.Rows.Add(r.DocumentNumber, r.OccurredAtLocal.ToString("HH:mm", CultureInfo.CurrentCulture),
                           r.Operator, r.Tenders, r.Total);

        _status.Text = _all.Count == 0
            ? "No receipts on this day."
            : $"{shown.Count} of {_all.Count} receipts, {shown.Sum(r => r.Total).ToString("N2", CultureInfo.CurrentCulture)} in all.";
        _reprint.Enabled = shown.Count > 0;
    }

    private async Task ReprintAsync()
    {
        if (_history is null || _printer is null || _headers is null) return;

        // Every receipt selected (Ctrl+click, Shift+click, Ctrl+A), in
        // the order listed.
        var numbers = _grid.SelectedInOrder()
            .Select(r => r.Cells[0].Value as string)
            .OfType<string>()
            .ToList();

        if (numbers.Count == 0) return;
        if (numbers.Count > 1 && !Confirm($"Print copies of {numbers.Count} receipts?")) return;

        try
        {
            SetBusy(true);
            var problems = new List<string>();
            var printed = 0;
            PrintResult? last = null;

            foreach (var number in numbers)
            {
                var found = await _history.ReceiptAsync(number);

                if (found.IsFailure)
                {
                    problems.Add(found.Error.Message);
                    continue;
                }

                var sale = found.Value;
                var header = _headers.Build();

                // As the till's own reprint: marked a copy, dated and
                // signed as the original was.
                last = await _printer.PrintAsync(sale, header with
                {
                    IsCopy = true,
                    PrintedAtLocal = sale.OccurredAtUtc is DateTime sold ? sold.ToLocalTime() : header.PrintedAtLocal,
                    OperatorName = sale.OperatorName ?? header.OperatorName
                });

                if (last.Printed)
                {
                    printed++;
                    await _history.RecordReprintAsync(sale);
                }
                else
                {
                    problems.Add(last.Describe($"Copy of receipt {sale.DocumentNumber}"));

                    // No printer, or it failed: the rest would only fail the same way.
                    break;
                }
            }

            if (numbers.Count == 1 && last is not null && problems.Count == 0)
                ShowInfo(last.Describe($"Copy of receipt {numbers[0]}"));
            else if (problems.Count == 0)
                ShowInfo($"{printed} copies printed.");
            else
                ShowError($"{printed} of {numbers.Count} copies printed." + Environment.NewLine
                          + string.Join(Environment.NewLine, problems));
        }
#pragma warning disable CA1031 // Reported to the operator and logged.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorHandler.Log(ex);
            ShowError(ErrorHandler.Describe(ex));
        }
        finally
        {
            SetBusy(false);
        }
    }
}
