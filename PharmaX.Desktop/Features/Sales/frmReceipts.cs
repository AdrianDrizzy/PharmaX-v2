using System.Globalization;
using PharmaX.Application.Sales;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Sales;

/// <summary>
/// The receipts printed, any day, newest first, a page at a time; any of
/// them can be printed again, and a manager can take one off the list.
/// </summary>
/// <remarks>
/// Open to everyone signed in: whoever is at the counter when a customer
/// comes back for a copy should be able to give them one. A copy says
/// "COPY - REPRINTED", carries the original's date and cashier, and the
/// reprint is recorded against whoever printed it. Hiding a receipt keeps
/// the sale - its cash-up, VAT and stock - exactly as it was.
/// </remarks>
public sealed class frmReceipts : PhxFormBase
{
    /// Receipts to a page: a busy day's worth, few enough to come back at once.
    internal const int PageSize = 100;

    private readonly IReceiptHistoryService? _history;
    private readonly IReceiptPrinter? _printer;
    private readonly ReceiptHeaderFactory? _headers;

    private readonly DateTimePicker _from = new();
    private readonly DateTimePicker _to = new();
    private readonly TextBox _find = new();
    private readonly CheckBox _showHidden = new();
    private readonly PhxGrid _grid = new();
    private readonly PagerBar _pager = new("pager") { Empty = "No receipts in these days" };
    private readonly Label _status = new();
    private readonly Button _hide = new();
    private readonly Button _unhide = new();
    private readonly Button _reprint = new();
    private readonly Button _close = new();
    private readonly System.Windows.Forms.Timer _typing = new() { Interval = 400 };

    private int _page;
    private int _total;

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
        top.Controls.Add(new Label { AutoSize = true, Location = new Point(12, 15), Text = "Fr&om" });
        Picker(_from, "dtpFrom", 56);
        top.Controls.Add(new Label { AutoSize = true, Location = new Point(200, 15), Text = "&To" });
        Picker(_to, "dtpTo", 228);

        top.Controls.Add(new Label { AutoSize = true, Location = new Point(372, 15), Text = "&Find" });
        _find.Location = new Point(410, 11);
        _find.Name = "txtFind";
        _find.PlaceholderText = "Receipt number or cashier";
        _find.Size = new Size(230, 23);

        // Searched in the database, a moment after the typing stops, so
        // every page of every day is searched and not just the one shown.
        _find.TextChanged += (_, _) =>
        {
            _typing.Stop();
            _typing.Start();
        };
        _typing.Tick += async (_, _) =>
        {
            _typing.Stop();
            await FirstPageAsync();
        };

        _showHidden.AutoSize = true;
        _showHidden.Location = new Point(654, 13);
        _showHidden.Name = "chkShowHidden";
        _showHidden.Text = "Show &hidden";
        _showHidden.Visible = false;
        _showHidden.CheckedChanged += async (_, _) => await FirstPageAsync();

        top.Controls.AddRange(new Control[] { _from, _to, _find, _showHidden });

        _grid.Dock = DockStyle.Fill;
        _grid.Name = "grdReceipts";
        _grid.ReadOnly = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AddColumn("Number", "Receipt");
        _grid.AddColumn("When", "Date");
        _grid.AddColumn("Operator", "Cashier");
        _grid.AddColumn("Tenders", "Paid by");
        _grid.AddColumn("Total", "Total", format: "N2", rightAlign: true);
        _grid.AddColumn("Hidden", "Hidden because");
        _grid.Columns["Hidden"]!.Visible = false;
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await ReprintAsync(); };
        _grid.SelectionChanged += (_, _) => ShowButtons();

        _pager.Dock = DockStyle.Bottom;
        _pager.PageRequested += async (_, move) => await TurnAsync(move);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, Size = new Size(820, 52) };
        _status.AutoSize = false;
        _status.Location = new Point(12, 16);
        _status.Name = "lblStatus";
        _status.Size = new Size(306, 22);

        Place(_hide, "btnHide", "H&ide...", 326, 112);
        _hide.Visible = false;
        _hide.Click += async (_, _) => await HideAsync();

        Place(_unhide, "btnShow", "Sho&w again", 444, 124);
        _unhide.Visible = false;
        _unhide.Click += async (_, _) => await UnhideAsync();

        Place(_reprint, "btnReprint", "&Reprint", 574, 114);
        _reprint.Click += async (_, _) => await ReprintAsync();

        Place(_close, "btnClose", "&Close", 694, 114);
        _close.Click += (_, _) => Close();

        bottom.Controls.AddRange(new Control[] { _status, _hide, _unhide, _reprint, _close });

        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(_pager);
        Controls.Add(bottom);
    }

    private void Picker(DateTimePicker picker, string name, int left)
    {
        picker.Format = DateTimePickerFormat.Short;
        picker.Location = new Point(left, 11);
        picker.Name = name;
        picker.Size = new Size(130, 23);

        // Set before the handler, so building the form loads nothing.
        picker.Value = DateTime.Today;
        picker.ValueChanged += async (_, _) => { if (!_quiet) await FirstPageAsync(); };
    }

    /// <summary>
    /// Opens on one receipt, from Day history: its day, found by number.
    /// </summary>
    public void FindReceipt(DateOnly day, string number) => _opening = (day, number);

    private (DateOnly Day, string Number)? _opening;

    // Set while OnLoad fills the filters, so they load once, not three times at once.
    private bool _quiet;

    private static void Place(Button button, string name, string text, int left, int width)
    {
        button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        button.Location = new Point(left, 10);
        button.Name = name;
        button.Size = new Size(width, 32);
        button.Text = text;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Hiding is the manager's and the administrator's; for anyone else
        // the list is the receipts and Reprint, as before.
        var mayHide = _history?.MayHide == true;
        _showHidden.Visible = mayHide;
        _hide.Visible = mayHide;

        if (_opening is var (day, number))
        {
            _quiet = true;
            _from.Value = _to.Value = day.ToDateTime(TimeOnly.MinValue);
            _find.Text = number;
            _typing.Stop();
            _quiet = false;
        }

        await LoadAsync();
        _find.Focus();
    }

    /// First, Previous, Next and Last from the keyboard, as on the other lists.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        _pager.Press(keyData) || base.ProcessCmdKey(ref msg, keyData);

    private Task FirstPageAsync()
    {
        _page = 0;
        return LoadAsync();
    }

    private Task TurnAsync(PageMove move)
    {
        var page = PagerBar.Target(_page, _total, PageSize, move);
        if (page == _page) return Task.CompletedTask;

        _page = page;
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_history is null) return;

        try
        {
            SetBusy(true);

            var page = await _history.PageAsync(Query());

            // Receipts hidden off the end of the last page leave it empty;
            // the page before it is the one to show.
            var last = Math.Max(0, (page.TotalCount - 1) / PageSize);
            if (_page > last)
            {
                _page = last;
                page = await _history.PageAsync(Query());
            }

            _total = page.TotalCount;
            Show(page);
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

    private ReceiptQuery Query() => new(
        DateOnly.FromDateTime(_from.Value), DateOnly.FromDateTime(_to.Value),
        _find.Text, _showHidden.Checked, _page * PageSize, PageSize);

    private void Show(ReceiptPageDto page)
    {
        var culture = CultureInfo.CurrentCulture;

        _grid.Rows.Clear();
        _grid.Columns["Hidden"]!.Visible = _showHidden.Checked;

        foreach (var r in page.Items)
        {
            var index = _grid.Rows.Add(r.DocumentNumber,
                r.OccurredAtLocal.ToString("dd/MM/yyyy HH:mm", culture),
                r.Operator, r.Tenders, r.Total, r.HiddenReason ?? "");

            var row = _grid.Rows[index];
            row.Tag = r;

            if (r.IsHidden) row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
        }

        _pager.ShowPage(_page, PageSize, page.Items.Count, page.TotalCount);

        _status.Text = page.TotalCount == 0
            ? "No receipts in these days."
            : string.Format(culture, "{0:N0} receipts, {1:N2} on this page.",
                page.TotalCount, page.Items.Sum(r => r.Total));

        ShowButtons();
    }

    private List<ReceiptSummaryDto> Selected() =>
        _grid.SelectedInOrder().Select(r => r.Tag).OfType<ReceiptSummaryDto>().ToList();

    private void ShowButtons()
    {
        var selected = Selected();

        _reprint.Enabled = selected.Any(r => !r.IsHidden);
        _hide.Enabled = selected.Any(r => !r.IsHidden);
        _unhide.Visible = _hide.Visible && selected.Any(r => r.IsHidden);
    }

    /// <summary>
    /// Takes the selected receipts off the list, with the one reason
    /// asked for. The sales themselves are not touched.
    /// </summary>
    private async Task HideAsync()
    {
        if (_history is null) return;

        var chosen = Selected().Where(r => !r.IsHidden).ToList();
        if (chosen.Count == 0) return;

        var which = chosen.Count == 1 ? $"receipt {chosen[0].DocumentNumber}" : $"these {chosen.Count} receipts";

        var reason = frmTextPrompt.Ask(this, "Hide receipt",
            $"Why is {which} being taken off the list? The sale stays: the cash-up, "
            + "VAT and stock do not change.", hint: "e.g. Printed in error");

        if (string.IsNullOrWhiteSpace(reason)) return;

        await ApplyAsync(chosen, r => _history.HideAsync(r.SaleId, reason));
    }

    private async Task UnhideAsync()
    {
        if (_history is null) return;

        var chosen = Selected().Where(r => r.IsHidden).ToList();
        if (chosen.Count == 0) return;

        await ApplyAsync(chosen, r => _history.ShowAsync(r.SaleId));
    }

    private async Task ApplyAsync(IReadOnlyList<ReceiptSummaryDto> chosen,
        Func<ReceiptSummaryDto, Task<Application.Results.Result>> act)
    {
        var problems = new List<string>();

        try
        {
            SetBusy(true);

            foreach (var receipt in chosen)
            {
                var result = await act(receipt);
                if (result.IsFailure) problems.Add(result.Error.Message);
            }
        }
#pragma warning disable CA1031 // Reported to the operator and logged.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorHandler.Log(ex);
            problems.Add(ErrorHandler.Describe(ex));
        }
        finally
        {
            SetBusy(false);
        }

        if (problems.Count > 0) ShowError(string.Join(Environment.NewLine, problems));

        await LoadAsync();
    }

    private async Task ReprintAsync()
    {
        if (_history is null || _printer is null || _headers is null) return;

        // Every receipt selected (Ctrl+click, Shift+click, Ctrl+A), in
        // the order listed. A hidden one is not printed again.
        var numbers = Selected().Where(r => !r.IsHidden).Select(r => r.DocumentNumber).ToList();

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

    protected override void Dispose(bool disposing)
    {
        if (disposing) _typing.Dispose();
        base.Dispose(disposing);
    }
}
