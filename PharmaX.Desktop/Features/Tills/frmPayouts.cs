using PharmaX.Application.Tills;
using PharmaX.Application.Tills.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Tills;

namespace PharmaX.Desktop.Features.Tills;

/// <summary>
/// Money out of the drawer: drawings, lunch and other expenses taken out
/// of a till during its shift, each with the amount and what it was for.
/// Opened from the till session, Cash Up and End of Day. Built in code,
/// about 760 by 480 before scaling (1140 by 720 at 150%).
/// </summary>
public sealed class frmPayouts : PhxFormBase, IPayoutsView
{
    internal const string Guidance =
        "Record money taken out of the drawer: the owner's drawings, lunch, or another expense. "
        + "Each is taken off the cash the cash-up expects, and printed on the cash-up slip and "
        + "the Z-read. Void one entered by mistake while the till is open.";

    private readonly PayoutsPresenter? _presenter;

    private readonly ComboBox _shift = new();
    private readonly ComboBox _kind = new();
    private readonly MoneyTextBox _amount = new();
    private readonly TextBox _note = new();
    private readonly PhxGrid _grid = new();
    private readonly Label _total = new();
    private readonly Label _guide = new();
    private bool _filling;

    /// Designer and tests only.
    public frmPayouts() => Build();

    public frmPayouts(PayoutsPresenter presenter)
    {
        _presenter = presenter;
        Build();
        _presenter.Attach(this);
    }

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.SessionPayOut;

    /// <summary>
    /// Opens on this shift rather than this till's own. Set before the
    /// window is shown.
    /// </summary>
    public void ForSession(Guid? sessionId)
    {
        if (_presenter is not null) _presenter.TargetSessionId = sessionId;
    }

    /// Whether anything was recorded or voided while it was open.
    public bool Changed => _presenter?.Changed ?? false;

    private void Build()
    {
        Text = "PharmaX - Money out of the drawer";
        Name = "frmPayouts";
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(760, 480);
        MinimumSize = new Size(700, 420);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var guide = _guide;
        guide.AutoSize = false;
        guide.BackColor = PhxTheme.Information;
        guide.Dock = DockStyle.Top;
        guide.Height = 52;
        guide.Name = "lblGuide";
        guide.Padding = new Padding(12, 6, 12, 6);
        guide.Text = Guidance;

        // ---- The till, and what came out of it --------------------------
        var entry = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 4,
            Dock = DockStyle.Top,
            Name = "pnlEntry",
            Padding = new Padding(10, 8, 10, 4),
            RowCount = 2
        };
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        Label Caption(string text) => new()
        {
            AutoSize = true,
            Margin = new Padding(3, 7, 6, 0),
            Text = text
        };

        _shift.DropDownStyle = ComboBoxStyle.DropDownList;
        _shift.Dock = DockStyle.Fill;
        _shift.Name = "cboShift";
        _shift.AccessibleName = "Till";
        _shift.SelectedIndexChanged += (_, _) =>
        {
            if (!_filling) ShiftChanged?.Invoke(this, EventArgs.Empty);
        };

        entry.Controls.Add(Caption("&Till"), 0, 0);
        entry.Controls.Add(_shift, 1, 0);
        entry.SetColumnSpan(_shift, 3);

        _kind.DropDownStyle = ComboBoxStyle.DropDownList;
        _kind.Dock = DockStyle.Fill;
        _kind.Name = "cboKind";
        _kind.AccessibleName = "For";
        foreach (var kind in Enum.GetValues<PayoutKind>()) _kind.Items.Add(new KindChoice(kind));
        _kind.SelectedIndex = 0;

        entry.Controls.Add(Caption("&For"), 0, 1);
        entry.Controls.Add(_kind, 1, 1);

        _amount.AccessibleName = "Amount";
        _amount.Name = "txtAmount";
        _amount.PlaceholderText = "0.00";
        _amount.Width = 110;

        entry.Controls.Add(Caption("&Amount"), 2, 1);
        entry.Controls.Add(_amount, 3, 1);

        var notes = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Name = "pnlNote",
            Padding = new Padding(10, 0, 10, 6)
        };
        notes.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        notes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        notes.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _note.AccessibleName = "Note";
        _note.Dock = DockStyle.Fill;
        _note.MaxLength = TillPayout.NoteLength;
        _note.Name = "txtNote";
        _note.PlaceholderText = "What it was for, e.g. sandwiches for three, the owner";

        var add = new Button
        {
            AutoSize = true,
            Name = "btnAdd",
            Padding = new Padding(8, 2, 8, 2),
            Text = "&Add"
        };
        add.Click += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);

        notes.Controls.Add(Caption("&Note"), 0, 0);
        notes.Controls.Add(_note, 1, 0);
        notes.Controls.Add(add, 2, 0);

        // ---- What has come out so far -----------------------------------
        _grid.Dock = DockStyle.Fill;
        _grid.Name = "grdPayouts";
        _grid.AddColumn("Time", "Time", 55, "HH:mm");
        _grid.AddColumn("Kind", "For", 100);
        _grid.AddColumn("Amount", "Amount", 80, "N2", rightAlign: true);
        _grid.AddColumn("Note", "Note", 230);
        _grid.AddColumn("By", "By", 110);
        _grid.AddColumn("Status", "Status", 140);

        // Each column as wide as its heading and values, the note taking
        // what is left: fixed widths clipped "Time" and "Amount" at 150%.
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        _grid.Columns["Note"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        var list = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 4) };
        list.Controls.Add(_grid);

        // ---- Total and buttons ------------------------------------------
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10, 6, 10, 8)
        };

        var close = new Button
        {
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Name = "btnClose",
            Padding = new Padding(8, 2, 8, 2),
            Text = "Close"
        };
        var voidButton = new Button
        {
            AutoSize = true,
            Name = "btnVoid",
            Padding = new Padding(8, 2, 8, 2),
            Text = "&Void..."
        };
        voidButton.Click += (_, _) => VoidRequested?.Invoke(this, EventArgs.Empty);

        _total.AutoSize = true;
        _total.Font = new Font(Font, FontStyle.Bold);
        _total.Margin = new Padding(3, 8, 24, 0);
        _total.Name = "lblTotal";

        buttons.Controls.Add(close);
        buttons.Controls.Add(voidButton);
        buttons.Controls.Add(_total);

        Controls.Add(list);
        Controls.Add(notes);
        Controls.Add(entry);
        Controls.Add(guide);
        Controls.Add(buttons);

        AcceptButton = add;
        CancelButton = close;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // As tall as the guidance needs at the size the window scaled to;
        // fixed, it showed one line of three at 150%.
        _guide.Height = TextRenderer.MeasureText(_guide.Text, _guide.Font,
            new Size(ClientSize.Width - _guide.Padding.Horizontal, 0), TextFormatFlags.WordBreak).Height
            + _guide.Padding.Vertical + 4;

        if (_presenter is not null) await _presenter.InitialiseAsync();
        ActiveControl = _amount;
    }

    public void ShowShifts(IReadOnlyList<ShiftChoiceDto> shifts, Guid? select)
    {
        ArgumentNullException.ThrowIfNull(shifts);

        _filling = true;
        _shift.Items.Clear();
        foreach (var s in shifts) _shift.Items.Add(s);
        _filling = false;

        var index = shifts.ToList().FindIndex(s => s.SessionId == select);

        if (shifts.Count > 0) _shift.SelectedIndex = Math.Max(0, index); // raises ShiftChanged
        else ShowPayouts(Array.Empty<PayoutDto>());
    }

    public Guid? SelectedShiftId => (_shift.SelectedItem as ShiftChoiceDto)?.SessionId;

    public void ShowPayouts(IReadOnlyList<PayoutDto> payouts)
    {
        ArgumentNullException.ThrowIfNull(payouts);

        _grid.Rows.Clear();

        foreach (var p in payouts)
        {
            var index = _grid.Rows.Add(p.PaidAtLocal, p.KindName, -p.Amount, p.Note,
                p.PaidByName, p.IsVoided ? $"Voided: {p.VoidReason}" : "");
            var row = _grid.Rows[index];
            row.Tag = p.Id;

            if (p.IsVoided) row.DefaultCellStyle.ForeColor = PhxTheme.Muted;
            else row.Cells["Amount"].Style.ForeColor = PhxTheme.Negative;
        }

        _total.Text = string.Format(Format.Culture, "Paid out of this drawer: {0}",
            Format.Money(PayoutDto.Total(payouts)));
    }

    public PayoutKind EntryKind => (_kind.SelectedItem as KindChoice)?.Kind ?? PayoutKind.Expense;

    public decimal? EntryAmount => _amount.Amount;

    public string EntryNote => _note.Text.Trim();

    public void ClearEntry()
    {
        _amount.Amount = null;
        _note.Text = "";
        _amount.Focus();
    }

    public Guid? SelectedPayoutId => _grid.CurrentRow?.Tag as Guid?;

    public string? PromptVoidReason(PayoutDto payout)
    {
        ArgumentNullException.ThrowIfNull(payout);

        var reason = frmTextPrompt.Ask(this, "Void money out",
            string.Format(Format.Culture, "Void {0} {1}: {2}? Why?", payout.KindName,
                Format.Money(payout.Amount), payout.Note),
            hint: "e.g. Entered twice");

        if (reason is not null && reason.Length == 0)
        {
            ShowError("Say why it is being voided.");
            return null;
        }

        return reason;
    }

    private sealed class KindChoice
    {
        public KindChoice(PayoutKind kind) => Kind = kind;

        public PayoutKind Kind { get; }

        public override string ToString() => Kind.DisplayName();
    }

    public event EventHandler? ShiftChanged;
    public event EventHandler? AddRequested;
    public event EventHandler? VoidRequested;
}
