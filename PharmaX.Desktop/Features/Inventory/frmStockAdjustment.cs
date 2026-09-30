using System.Globalization;
using PharmaX.Application.Inventory;
using PharmaX.Application.Reference;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Inventory;

/// <summary>
/// Records stock that leaves or arrives other than by a sale, a
/// dispensing or a delivery: a breakage, expired stock written off, a
/// transfer to or from another branch.
/// </summary>
/// <remarks>
/// Hand-built rather than split into a designer file, for the same reason
/// as the accounts screen: every control is one labelled row.
/// </remarks>
public sealed class frmStockAdjustment : PhxFormBase
{
    private readonly IStockAdjustmentService? _adjustments;
    private readonly IReferenceDataService? _reference;

    private readonly ComboBox _kind = new();
    private readonly TextBox _code = new();
    private readonly Button _find = new();
    private readonly Label _product = new();
    private readonly ComboBox _batch = new();
    private readonly TextBox _batchNumber = new();
    private readonly DateTimePicker _expiry = new();
    private readonly MoneyTextBox _quantity = new();
    private readonly ComboBox _branch = new();
    private readonly TextBox _reason = new();
    private readonly Button _record = new();
    private readonly Button _close = new();

    private readonly List<Control> _outOnly = new();
    private readonly List<Control> _inOnly = new();
    private readonly List<Control> _transferOnly = new();

    private AdjustableProductDto? _found;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.StockTakePost;

    /// Designer only. Never called at runtime.
    public frmStockAdjustment()
    {
        BuildLayout();

        _kind.SelectedIndexChanged += (_, _) => ShowKind();
        _find.Click += (_, _) => Operations.Enqueue(FindAsync);
        _record.Click += (_, _) => Operations.Enqueue(RecordAsync);
        _close.Click += (_, _) => CloseView(false);

        // Before the box's own Enter, so a highlighted match is taken
        // rather than the half-typed name.
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
    public frmStockAdjustment(IStockAdjustmentService adjustments,
                              IReferenceDataService reference,
                              Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes)
        : this()
    {
        _adjustments = adjustments;
        _reference = reference;
        _suggestions.Source = Suggest.Products(scopes);
    }

    /// Products offered under the code box as a name is typed.
    private readonly SearchSuggestions _suggestions;

    /// <summary>
    /// Opens on one kind of adjustment, for the menu entry that names it.
    /// </summary>
    public void Preselect(StockAdjustmentKind kind) =>
        _kind.SelectedIndex = Kinds.FindIndex(k => k.Kind == kind);

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (_kind.SelectedIndex < 0) _kind.SelectedIndex = 0;
        ShowKind();

        if (_reference is null) return;

        var branches = await _reference.ListAsync(ReferenceKind.Branch,
            includeInactive: false);

        if (branches.IsSuccess)
            foreach (var branch in branches.Value)
                _branch.Items.Add($"{branch.Code}  {branch.Name}");

        _code.Focus();
    }

    private StockAdjustmentKind Kind =>
        _kind.SelectedIndex >= 0
            ? Kinds[_kind.SelectedIndex].Kind
            : StockAdjustmentKind.Breakage;

    private static readonly List<(StockAdjustmentKind Kind, string Text)> Kinds = new()
    {
        (StockAdjustmentKind.Breakage, "Breakage - damaged and thrown away"),
        (StockAdjustmentKind.ExpiredWriteOff, "Expired stock written off"),
        (StockAdjustmentKind.TransferOut, "Transfer out to another branch"),
        (StockAdjustmentKind.TransferIn, "Transfer in from another branch")
    };

    /// Shows the fields this kind of adjustment asks for, and no others.
    private void ShowKind()
    {
        var incoming = Kind == StockAdjustmentKind.TransferIn;
        var tracked = _found?.IsBatchTracked ?? true;

        foreach (var control in _outOnly) control.Visible = !incoming && tracked;
        foreach (var control in _inOnly) control.Visible = incoming && tracked;

        foreach (var control in _transferOnly)
            control.Visible = Kind is StockAdjustmentKind.TransferOut
                                  or StockAdjustmentKind.TransferIn;
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
            return;
        }

        _found = result.Value;
        _product.ForeColor = SystemColors.ControlText;
        _product.Text = string.Format(CultureInfo.CurrentCulture,
            "{0}   {1:0.###} on hand{2}", _found.Description,
            _found.QuantityOnHand,
            _found.IsBatchTracked ? "   (batch tracked)" : "");

        foreach (var batch in _found.Batches)
            _batch.Items.Add(new BatchChoice(batch));

        if (_batch.Items.Count > 0) _batch.SelectedIndex = 0;

        ShowKind();
        _quantity.Focus();
    });

    private Task RecordAsync() => GuardAsync(async () =>
    {
        if (_adjustments is null) return;

        if (_found is null)
        {
            ShowInfo("Find the product first.");
            return;
        }

        var edit = new StockAdjustmentEditDto(
            Kind: Kind,
            ProductCode: _found.Code,
            Quantity: _quantity.Amount ?? 0m,
            Reason: _reason.Text,
            BatchId: (_batch.SelectedItem as BatchChoice)?.Batch.BatchId,
            OtherBranch: _branch.Text.Trim().Length == 0
                ? null
                : _branch.Text.Trim().Split(' ')[0],
            BatchNumber: _batchNumber.Text.Trim().Length == 0
                ? null
                : _batchNumber.Text.Trim(),
            Expiry: DateOnly.FromDateTime(_expiry.Value.Date));

        if (!Confirm($"{Kinds[_kind.SelectedIndex].Text}: "
                     + $"{edit.Quantity:0.###} x {_found.Description}?"))
            return;

        var result = await _adjustments.RecordAsync(edit);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        ShowInfo(string.Format(CultureInfo.CurrentCulture,
            "Recorded. {0} now has {1:0.###} on hand.",
            result.Value.Description, result.Value.QuantityOnHand));

        _found = null;
        _code.Clear();
        _product.Text = "";
        _batch.Items.Clear();
        _batchNumber.Clear();
        _quantity.Clear();
        _reason.Clear();
        _code.Focus();
    });

    private void BuildLayout()
    {
        var y = 16;

        void Row(string caption, Control input, int width,
                 List<Control>? group = null)
        {
            var label = new Label
            {
                AutoSize = false,
                Location = new Point(16, y + 4),
                Size = new Size(150, 20),
                Text = caption
            };

            input.AccessibleName = caption.Replace("&", "",
                StringComparison.Ordinal);
            input.Location = new Point(170, y);
            input.Size = new Size(width, 23);

            Controls.Add(label);
            Controls.Add(input);

            group?.Add(label);
            group?.Add(input);

            y += 36;
        }

        _kind.DropDownStyle = ComboBoxStyle.DropDownList;
        _kind.Name = "cboKind";
        foreach (var (_, text) in Kinds) _kind.Items.Add(text);
        Row("&What happened", _kind, 300);

        _code.Name = "txtProductCode";
        _code.PlaceholderText = "Scan or type the code, then Enter";
        Row("&Product", _code, 200);

        _find.Location = new Point(378, y - 36);
        _find.Name = "btnFind";
        _find.Size = new Size(92, 25);
        _find.Text = "&Find";
        _find.UseVisualStyleBackColor = true;
        Controls.Add(_find);

        _product.AutoSize = false;
        _product.Location = new Point(170, y - 8);
        _product.Name = "lblProduct";
        _product.Size = new Size(420, 20);
        Controls.Add(_product);
        y += 20;

        _batch.DropDownStyle = ComboBoxStyle.DropDownList;
        _batch.Name = "cboBatch";
        Row("&Batch", _batch, 300, _outOnly);

        _batchNumber.Name = "txtBatchNumber";
        Row("Batch &number", _batchNumber, 200, _inOnly);

        _expiry.Format = DateTimePickerFormat.Short;
        _expiry.Name = "dtpExpiry";
        _expiry.Value = DateTime.Today.AddYears(1);
        Row("&Expires", _expiry, 140, _inOnly);

        _quantity.Name = "txtQuantity";
        Row("&Quantity (packs)", _quantity, 120);

        _branch.DropDownStyle = ComboBoxStyle.DropDown;
        _branch.Name = "cboBranch";
        Row("Other &branch", _branch, 300, _transferOnly);

        _reason.MaxLength = 250;
        _reason.Name = "txtReason";
        _reason.PlaceholderText = "Why - kept with the movement and the audit";
        Row("&Reason", _reason, 420);

        _record.Location = new Point(170, y + 8);
        _record.Name = "btnRecord";
        _record.Size = new Size(140, 34);
        _record.Text = "Re&cord";
        _record.UseVisualStyleBackColor = true;
        PhxTheme.MakePrimary(_record);
        Controls.Add(_record);

        _close.Location = new Point(500, y + 8);
        _close.Name = "btnClose";
        _close.Size = new Size(100, 34);
        _close.Text = "C&lose";
        _close.UseVisualStyleBackColor = true;
        Controls.Add(_close);

        AcceptButton = _record;
        CancelButton = _close;
        ClientSize = new Size(620, y + 60);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "PharmaX - Stock adjustment";
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
                "{0}   expires {1:dd/MM/yyyy}   {2:0.###} on hand{3}",
                Batch.BatchNumber, Batch.Expiry, Batch.QuantityOnHand,
                Batch.IsExpired ? "   EXPIRED" : "");
    }
}
