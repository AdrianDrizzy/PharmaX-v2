using System.Globalization;
using PharmaX.Application.Inventory;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Inventory;

/// <summary>
/// Counts bulk stock into pre-packs: from a tin of a thousand, twenty-five
/// packs of forty. The pre-packs go on the shelf as their own item, made
/// the first time, and each gets a label.
/// </summary>
/// <remarks>
/// Its own screen, apart from Move Stock: pre-packing is the dispensary's
/// daily work, and the stock adjustment screen is a manager's. Hand-built
/// like that screen, one labelled row per control.
/// </remarks>
public sealed class frmPrepack : PhxFormBase
{
    private readonly IStockAdjustmentService? _adjustments;
    private readonly Application.Dispensing.ILabelPrinter? _labels;

    private readonly TextBox _code = new();
    private readonly Button _find = new();
    private readonly Label _product = new();
    private readonly ComboBox _batch = new();
    private readonly MoneyTextBox _units = new();
    private readonly MoneyTextBox _packs = new();
    private readonly MoneyTextBox _price = new();
    private readonly Label _summary = new();
    private readonly Button _record = new();
    private readonly Button _close = new();
    private readonly SearchSuggestions _suggestions;

    private AdjustableProductDto? _found;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.StockPrepack;

    /// Designer only. Never called at runtime.
    public frmPrepack()
    {
        BuildLayout();

        _find.Click += (_, _) => Operations.Enqueue(FindAsync);
        _record.Click += (_, _) => Operations.Enqueue(RecordAsync);
        _close.Click += (_, _) => CloseView(false);
        _units.TextChanged += (_, _) => Summarise();
        _packs.TextChanged += (_, _) => Summarise();

        _suggestions = new SearchSuggestions(_code) { Wanted = Suggest.IsName };
        _suggestions.Chosen += (_, chosen) =>
        {
            _code.Text = chosen.Value as string ?? "";
            Operations.Enqueue(FindAsync);
        };

        _code.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || e.Handled) return;

            e.SuppressKeyPress = true;
            Operations.Enqueue(FindAsync);
        };

        PhxTheme.Apply(this);
    }

    /// Runtime. Resolved by the container.
    public frmPrepack(IStockAdjustmentService adjustments,
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes,
        Application.Dispensing.ILabelPrinter labels) : this()
    {
        _adjustments = adjustments;
        _labels = labels;
        _suggestions.Source = Suggest.Products(scopes);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _code.Focus();
    }

    private Task FindAsync() => GuardAsync(async () =>
    {
        if (_adjustments is null) return;

        var result = await _adjustments.FindAsync(_code.Text);

        _batch.Items.Clear();

        if (result.IsFailure)
        {
            _found = null;
            _product.Text = result.Error.Message;
            _product.ForeColor = PhxTheme.ProblemText;
            Summarise();
            return;
        }

        _found = result.Value;
        _product.ForeColor = PhxTheme.Ink;
        _product.Text = string.Format(CultureInfo.CurrentCulture,
            "{0}   {1:0.###} on hand, {2:0.###} in each",
            _found.Description, _found.QuantityOnHand, _found.UnitsInPack);

        foreach (var batch in _found.Batches.Where(b => !b.IsExpired))
            _batch.Items.Add(new BatchChoice(batch));

        if (_batch.Items.Count > 0) _batch.SelectedIndex = 0;

        _batch.Enabled = _found.IsBatchTracked;
        Summarise();
        _units.Focus();
    });

    /// <summary>
    /// What the count comes to, before it is recorded: "25 x 40 = 1000,
    /// 1 of 9001 (1000 in each)".
    /// </summary>
    private void Summarise()
    {
        if (_found is null || _units.Amount is not decimal units || units <= 0m
            || _packs.Amount is not decimal packs || packs <= 0m)
        {
            _summary.Text = "";
            return;
        }

        var total = units * packs;

        _summary.Text = string.Format(CultureInfo.CurrentCulture,
            "{0:0} x {1:0.###} = {2:0.###}, which is {3:0.###} of {4} "
            + "({5:0.###} in each).",
            packs, units, total, Math.Round(total / _found.UnitsInPack, 3),
            _found.Code, _found.UnitsInPack);
    }

    private Task RecordAsync() => GuardAsync(async () =>
    {
        if (_adjustments is null) return;

        if (_found is null)
        {
            ShowInfo("Find the bulk item first.");
            return;
        }

        var packs = _packs.Amount ?? 0m;

        if (packs != Math.Floor(packs))
        {
            ShowInfo("Pre-packs are counted whole: say how many packs are made.");
            return;
        }

        var edit = new PrepackEditDto(_found.Code,
            (_batch.SelectedItem as BatchChoice)?.Batch.BatchId,
            _units.Amount ?? 0m, (int)packs, _price.Amount);

        if (!Confirm($"Pre-pack {_summary.Text}"))
            return;

        var result = await _adjustments.PrepackAsync(edit);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        ShowInfo(string.Format(CultureInfo.CurrentCulture,
            "Recorded. {0} now has {1:0.###} on hand, and {2} pre-packs of {3} "
            + "are on the shelf.", result.Value.Description,
            result.Value.QuantityOnHand, result.Value.PrePacks,
            result.Value.PrePack?.ProductDescription));

        if (result.Value is { PrePack: { } label, PrePacks: > 0 } && _labels is not null
            && Confirm($"Print {result.Value.PrePacks} pre-pack labels?"))
        {
            for (var i = 0; i < result.Value.PrePacks; i++)
            {
                var printed = await _labels.PrintAsync(label);

                if (printed.Printed) continue;

                ShowError((i == 0 ? "" : $"{i} label(s) printed, then ")
                          + printed.Describe("The label"));
                break;
            }
        }

        _found = null;
        _code.Clear();
        _product.Text = "";
        _batch.Items.Clear();
        _units.Clear();
        _packs.Clear();
        _price.Clear();
        _summary.Text = "";
        _code.Focus();
    });

    private void BuildLayout()
    {
        var y = 16;

        void Row(string caption, Control input, int width)
        {
            Controls.Add(new Label
            {
                AutoSize = false,
                Location = new Point(16, y + 4),
                Size = new Size(170, 20),
                Text = caption
            });

            input.AccessibleName = caption.Replace("&", "",
                StringComparison.Ordinal);
            input.Location = new Point(190, y);
            input.Size = new Size(width, 23);
            Controls.Add(input);

            y += 36;
        }

        _code.Name = "txtBulkCode";
        _code.PlaceholderText = "Scan, or type a code or name";
        Row("&Bulk item", _code, 200);

        _find.Location = new Point(398, y - 36);
        _find.Name = "btnFind";
        _find.Size = new Size(92, 25);
        _find.Text = "&Find";
        _find.UseVisualStyleBackColor = true;
        Controls.Add(_find);

        _product.AutoSize = false;
        _product.Location = new Point(190, y - 8);
        _product.Name = "lblProduct";
        _product.Size = new Size(440, 20);
        Controls.Add(_product);
        y += 20;

        _batch.DropDownStyle = ComboBoxStyle.DropDownList;
        _batch.Name = "cboBatch";
        Row("Ba&tch counted out of", _batch, 340);

        _units.Name = "txtUnitsPerPack";
        _units.PlaceholderText = "e.g. 40";
        Row("&Each pre-pack holds", _units, 120);

        _packs.Name = "txtPacks";
        _packs.PlaceholderText = "e.g. 25";
        Row("&How many pre-packs", _packs, 120);

        _price.Name = "txtPricePerPack";
        _price.PlaceholderText = "Blank: the bulk price, pro rata";
        Row("&Price of one pre-pack", _price, 220);

        _summary.AutoSize = false;
        _summary.Location = new Point(190, y);
        _summary.Name = "lblSummary";
        _summary.Size = new Size(440, 40);
        Controls.Add(_summary);
        y += 44;

        _record.Location = new Point(190, y + 8);
        _record.Name = "btnRecord";
        _record.Size = new Size(140, 34);
        _record.Text = "&Record";
        _record.UseVisualStyleBackColor = true;
        PhxTheme.MakePrimary(_record);
        Controls.Add(_record);

        _close.Location = new Point(530, y + 8);
        _close.Name = "btnClose";
        _close.Size = new Size(100, 34);
        _close.Text = "C&lose";
        _close.UseVisualStyleBackColor = true;
        Controls.Add(_close);

        AcceptButton = _record;
        CancelButton = _close;
        ClientSize = new Size(650, y + 60);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Pre-packs";
    }

    /// The same guard the presenters use; this screen has no presenter.
    private async Task GuardAsync(Func<Task> work)
    {
        try
        {
            SetBusy(true);
            await work();
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private sealed record BatchChoice(AdjustableBatchDto Batch)
    {
        public override string ToString() =>
            string.Format(CultureInfo.CurrentCulture,
                "{0}   expires {1:dd/MM/yyyy}   {2:0.###} on hand",
                Batch.BatchNumber, Batch.Expiry, Batch.QuantityOnHand);
    }
}
