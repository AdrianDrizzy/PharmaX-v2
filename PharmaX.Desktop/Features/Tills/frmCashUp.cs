using PharmaX.Application.Tills;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Tills;

/// <summary>
/// Cash Up: a shift's takings by tender, every transaction if wanted, the
/// slip for the till roll, and the way into counting and closing. Built
/// in code, sized for the 150% display (about 1000 by 620 before scaling).
/// </summary>
public sealed class frmCashUp : PhxFormBase, ICashUpView
{
    /// What the screen says a cash-up is, step by step.
    internal const string Guidance =
        "1. Choose the day (today unless cashing up an earlier one) and the shift; this PC's till comes first, and History finds past cash-ups.  2. Check the totals; Money out records drawings, lunch and expenses.  "
        + "3. Preview and print the slip (F9) and count the drawer against \"Cash in drawer\".  "
        + "4. Count cash and close till (over 20.00 out needs a reason). Scripts show by number only.";

    private readonly CashUpPresenter? _presenter;
    private readonly ViewFactory? _views;

    private readonly DateTimePicker _day = new();
    private readonly ComboBox _shift = new();
    private readonly Label _behalf = new();
    private readonly PhxGrid _tenders = new();
    private readonly PhxGrid _cashiers = new();
    private readonly PhxGrid _transactions = new();
    private readonly CheckBox _every = new();
    private readonly Label _empty = new();
    private readonly Dictionary<string, Label> _figures = new(StringComparer.Ordinal);
    private bool _filling;

    /// Designer and tests only.
    public frmCashUp() => Build();

    public frmCashUp(CashUpPresenter presenter, ViewFactory views)
    {
        _presenter = presenter;
        _views = views;
        Build();
        _presenter.Attach(this);
    }

    /// The cashier's own (pos.session.cash-up) or anyone's for a supervisor.
    protected override bool MayOpen(Application.Security.IPermissionService permissions) =>
        Application.Security.PermissionServiceExtensions.HasAny(permissions,
            Domain.Security.PermissionCode.SessionCashUp,
            Domain.Security.PermissionCode.SessionCloseOtherUser);

    private static readonly (string Key, string Text)[] FigureRows =
    {
        ("Sales", "Sales"), ("Scripts", "  of which scripts"), ("Refunds", "Refunds"),
        ("Account", "Account payments"), ("Net", "Net takings"),
        ("Float", "Opening float"), ("Change", "Change given"),
        ("PaidOut", "Paid out"),
        ("Drawer", "Cash in drawer"),
        ("EcoCash", "EcoCash"), ("Card", "Card"),
        ("Counted", "Declared, all tenders"),
        ("Variance", "Variance")
    };

    private void Build()
    {
        Text = "PharmaX - Cash Up";
        Name = "frmCashUp";
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1000, 620);
        MinimumSize = new Size(860, 520);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        var guide = new Label
        {
            AutoSize = false,
            BackColor = PhxTheme.Information,
            Dock = DockStyle.Top,
            Height = 64,
            Name = "lblGuide",
            Padding = new Padding(12, 6, 12, 6),
            Text = Guidance
        };

        // ---- The shift --------------------------------------------------
        var bar = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 7,
            Dock = DockStyle.Top,
            Padding = new Padding(10, 6, 10, 2)
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430F));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        _shift.DropDownStyle = ComboBoxStyle.DropDownList;
        _shift.Dock = DockStyle.Fill;
        _shift.Name = "cboShift";
        _shift.SelectedIndexChanged += (_, _) =>
        {
            if (!_filling) ShiftChanged?.Invoke(this, EventArgs.Empty);
        };

        var refresh = new Button { AutoSize = true, Name = "btnRefresh", Text = "&Refresh" };
        refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);

        var history = new Button { AutoSize = true, Name = "btnHistory", Text = "&History..." };
        history.Click += (_, _) => HistoryRequested?.Invoke(this, EventArgs.Empty);

        _behalf.AutoSize = true;
        _behalf.Font = new Font(Font, FontStyle.Bold);
        _behalf.ForeColor = PhxTheme.ProblemText;
        _behalf.Margin = new Padding(8, 7, 3, 0);
        _behalf.Name = "lblBehalf";

        // Today unless a till left from an earlier day still needs cashing
        // up, or a closed day's slip is wanted again.
        _day.Format = DateTimePickerFormat.Custom;
        _day.CustomFormat = "ddd dd/MM/yyyy";
        _day.Width = 150;
        _day.Name = "dtpDay";
        _day.Value = DateTime.Today;
        _day.MaxDate = DateTime.Today;
        _day.ValueChanged += (_, _) =>
        {
            if (!_filling) DayChanged?.Invoke(this, EventArgs.Empty);
        };

        bar.Controls.Add(new Label { AutoSize = true, Margin = new Padding(3, 7, 6, 0), Text = "&Day" }, 0, 0);
        bar.Controls.Add(_day, 1, 0);
        bar.Controls.Add(new Label { AutoSize = true, Margin = new Padding(12, 7, 6, 0), Text = "&Shift" }, 2, 0);
        bar.Controls.Add(_shift, 3, 0);
        bar.Controls.Add(refresh, 4, 0);
        bar.Controls.Add(history, 5, 0);
        bar.Controls.Add(_behalf, 6, 0);

        // ---- Totals left, tenders right --------------------------------
        var totals = new TableLayoutPanel
        {
            ColumnCount = 4,
            Dock = DockStyle.Left,
            Padding = new Padding(10, 6, 10, 6),
            Width = 560
        };
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        totals.Controls.Add(Heading("Totals for the shift"), 0, 0);
        totals.SetColumnSpan(totals.GetControlFromPosition(0, 0)!, 4);

        // Two pairs of columns, five rows: the takings on the left, the
        // drawer on the right, so the transaction list below has room.
        var r = 1;
        var c = 0;
        foreach (var (key, text) in FigureRows)
        {
            if (key == "Float") { r = 1; c = 2; }

            var bold = key is "Net" or "Drawer" or "Variance";
            totals.Controls.Add(new Label
            {
                AutoSize = true,
                Font = bold ? new Font(Font, FontStyle.Bold) : Font,
                Margin = new Padding(3, 1, 3, 1),
                Text = text
            }, c, r);

            var value = new Label
            {
                Anchor = AnchorStyles.Right,
                AutoSize = true,
                Font = bold ? new Font(Font, FontStyle.Bold) : Font,
                Margin = new Padding(3, 1, 3, 1),
                Name = "lbl" + key,
                Text = "-",
                TextAlign = ContentAlignment.MiddleRight
            };
            _figures[key] = value;
            totals.Controls.Add(value, c + 1, r++);
        }

        var tenderPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 10, 6) };
        _tenders.Dock = DockStyle.Fill;
        _tenders.Name = "grdTenders";
        _tenders.AddColumn("Tender", "Tender", 140);
        _tenders.AddColumn("Payments", "Payments", 70, rightAlign: true);
        _tenders.AddColumn("Amount", "Amount", 90, "N2", rightAlign: true);

        // Several cashiers take turns at the one till: each one's own takings.
        _cashiers.Dock = DockStyle.Fill;
        _cashiers.Name = "grdCashiers";
        _cashiers.AddColumn("Cashier", "Cashier", 140);
        _cashiers.AddColumn("Sales", "Sales", 50, rightAlign: true);
        _cashiers.AddColumn("Net", "Net takings", 90, "N2", rightAlign: true);
        _cashiers.AddColumn("Cash", "of which cash", 90, "N2", rightAlign: true);

        _tenders.Dock = DockStyle.Top;
        _tenders.Height = 100;
        tenderPanel.Controls.Add(_cashiers);
        tenderPanel.Controls.Add(Heading("By cashier (who rang each sale up)"));
        tenderPanel.Controls.Add(_tenders);
        tenderPanel.Controls.Add(Heading("By tender (refunds taken off)"));

        // Six rows on the drawer's side since money paid out has its own;
        // the cashiers under the tenders need the rest.
        var top = new Panel { Dock = DockStyle.Top, Height = 280 };
        top.Controls.Add(tenderPanel);
        top.Controls.Add(totals);

        // ---- Every transaction -----------------------------------------
        _every.AutoSize = true;
        _every.Dock = DockStyle.Top;
        _every.Name = "chkEvery";
        _every.Padding = new Padding(10, 4, 0, 2);
        _every.Text = "Show every &transaction (tenders, items, scripts collected, change given)";
        _every.CheckedChanged += (_, _) =>
            EveryTransactionChanged?.Invoke(this, EventArgs.Empty);

        _transactions.Dock = DockStyle.Fill;
        _transactions.Name = "grdTransactions";
        _transactions.AddColumn("Time", "Time", 55, "HH:mm");
        _transactions.AddColumn("Type", "Type", 100);
        _transactions.AddColumn("Number", "Number", 95);
        _transactions.AddColumn("By", "By", 90);
        _transactions.AddColumn("Items", "Items (scripts by number only)", 200);
        _transactions.AddColumn("Tenders", "Paid by", 120);
        _transactions.AddColumn("Total", "Total", 60, "N2", rightAlign: true);
        _transactions.AddColumn("Change", "Change", 65, "N2", rightAlign: true);
        _transactions.Visible = false;

        _empty.AutoSize = false;
        _empty.Dock = DockStyle.Fill;
        _empty.ForeColor = PhxTheme.Muted;
        _empty.Name = "lblEmpty";
        _empty.Padding = new Padding(12);
        _empty.TextAlign = ContentAlignment.TopLeft;

        var lower = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 4) };
        lower.Controls.Add(_transactions);
        lower.Controls.Add(_empty);

        // ---- Buttons ---------------------------------------------------
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10, 6, 10, 8)
        };

        Button Make(string name, string text, Action click)
        {
            var b = new Button { AutoSize = true, Name = name, Padding = new Padding(8, 2, 8, 2), Text = text };
            b.Click += (_, _) => click();
            return b;
        }

        var close = Make("btnClose", "Close", () => CloseView(false));
        close.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(close);
        buttons.Controls.Add(Make("btnCount", "C&ount cash and close till...",
            () => CountRequested?.Invoke(this, EventArgs.Empty)));
        buttons.Controls.Add(Make("btnPayouts", "&Money out...",
            () => PayoutsRequested?.Invoke(this, EventArgs.Empty)));
        buttons.Controls.Add(Make("btnExcel", "Export &Excel",
            () => ExportExcelRequested?.Invoke(this, EventArgs.Empty)));
        buttons.Controls.Add(Make("btnPdf", "Export P&DF",
            () => ExportPdfRequested?.Invoke(this, EventArgs.Empty)));
        buttons.Controls.Add(Make("btnPrint", "&Print slip (F9)",
            () => PrintRequested?.Invoke(this, EventArgs.Empty)));

        Controls.Add(lower);
        Controls.Add(_every);
        Controls.Add(top);
        Controls.Add(bar);
        Controls.Add(guide);
        Controls.Add(buttons);

        CancelButton = close;
    }

    private Label Heading(string text) => new()
    {
        AutoSize = true,
        Dock = DockStyle.Top,
        Font = new Font(Font, FontStyle.Bold),
        Margin = new Padding(3, 0, 3, 6),
        Padding = new Padding(0, 0, 0, 4),
        Text = text
    };

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitGuide(this);
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    /// <summary>
    /// The guidance band as tall as its text at the size the window scaled
    /// to: a fixed 64 showed two of its three lines at 150%.
    /// </summary>
    internal static void FitGuide(Form form)
    {
        if (form.Controls["lblGuide"] is not Label guide) return;

        guide.Height = TextRenderer.MeasureText(guide.Text, guide.Font,
            new Size(form.ClientSize.Width - guide.Padding.Horizontal, 0),
            TextFormatFlags.WordBreak).Height + guide.Padding.Vertical + 4;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F9)
        {
            PrintRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    public void ShowShifts(IReadOnlyList<ShiftChoiceDto> shifts, bool forOthers, Guid? select)
    {
        ArgumentNullException.ThrowIfNull(shifts);

        _filling = true;
        _shift.Items.Clear();
        foreach (var s in shifts) _shift.Items.Add(s);
        _filling = false;

        _empty.Text = shifts.Count == 0
            ? (forOthers
                ? "No till is open, and none was cashed up today."
                : "You have no shift open, and none closed today. Open the till from "
                  + "File > Till Session (Ctrl+T) to start one.")
            : "";

        var index = shifts.ToList().FindIndex(s => s.SessionId == select);

        if (shifts.Count > 0) _shift.SelectedIndex = Math.Max(0, index); // raises ShiftChanged
    }

    public Guid? SelectedShiftId => (_shift.SelectedItem as ShiftChoiceDto)?.SessionId;

    public DateOnly Day => DateOnly.FromDateTime(_day.Value);

    public void ShowDay(DateOnly day)
    {
        _filling = true;
        _day.Value = day.ToDateTime(TimeOnly.MinValue);
        _filling = false;
    }

    public ShiftCashUpDto? PickFromHistory()
    {
        if (_views is null) return null;

        using var history = _views.Create<frmCashUpHistory>();
        return history.ShowDialog(this) == DialogResult.OK ? history.Picked : null;
    }

    public bool EveryTransaction => _every.Checked;

    public void ShowShift(ShiftCashUpDto? shift, bool onBehalf)
    {
        _behalf.Text = onBehalf ? "On the cashier's behalf" : "";
        _tenders.Rows.Clear();
        _cashiers.Rows.Clear();
        _transactions.Rows.Clear();

        if (shift is null)
        {
            foreach (var label in _figures.Values) label.Text = "-";
            _transactions.Visible = false;
            return;
        }

        string M(decimal v) => Format.Money(v);

        _figures["Sales"].Text = $"{M(shift.SalesTotal)}  ({shift.SalesCount})";
        _figures["Scripts"].Text = shift.ScriptsCollected.ToString(Format.Culture);
        _figures["Refunds"].Text = $"{M(-shift.RefundsTotal)}  ({shift.RefundCount})";
        _figures["Account"].Text = M(shift.AccountPayments);
        _figures["Net"].Text = M(shift.NetTakings);
        _figures["Float"].Text = M(shift.OpeningFloat);
        _figures["Change"].Text = M(shift.ChangeGiven);
        _figures["PaidOut"].Text = shift.PaidOut == 0m
            ? M(0m)
            : $"{M(-shift.PaidOut)}  ({shift.Payouts!.Count(p => !p.IsVoided)})";
        _figures["PaidOut"].ForeColor = shift.PaidOut > 0m ? PhxTheme.Negative : PhxTheme.Ink;
        _figures["Drawer"].Text = M(shift.ExpectedCash);
        _figures["EcoCash"].Text = M(Tender("EcoCash"));
        _figures["Card"].Text = M(Tender("Card"));
        _figures["Counted"].Text = shift.Declared is decimal d ? M(d) : "not yet counted";
        _figures["Variance"].Text = shift.Variance is decimal v ? M(v) : "-";
        _figures["Variance"].ForeColor = shift.Variance is < 0m ? PhxTheme.Negative : PhxTheme.Ink;

        foreach (var t in ShiftTally.WithStatements(shift.Tenders))
            _tenders.Rows.Add(ShiftTally.Label(t.Tender), t.Count, t.Amount);

        decimal Tender(string name) =>
            shift.Tenders.FirstOrDefault(t => t.Tender == name)?.Amount ?? 0m;

        foreach (var c in shift.Cashiers)
            _cashiers.Rows.Add(c.Name, c.Sales, c.Net, c.Cash);

        if (EveryTransaction)
            foreach (var t in shift.Transactions)
            {
                var index = _transactions.Rows.Add(t.AtLocal, t.Kind, t.Number, t.OperatorName,
                    string.Join("; ", t.Items.Select(i => i.IsScript
                        ? i.Text
                        : $"{i.Quantity.ToString("0.###", Format.Culture)} x {i.Text}")),
                    string.Join(", ", t.Tenders.Select(p =>
                        $"{ShiftTally.Label(p.Tender)} {M(p.Amount)}")),
                    t.Total, t.Change == 0m ? null : t.Change);

                if (t.Total < 0m)
                    _transactions.Rows[index].Cells["Total"].Style.ForeColor = PhxTheme.Negative;
            }

        _transactions.Visible = EveryTransaction;
        _empty.Text = EveryTransaction
            ? ""
            : $"{shift.Transactions.Count} transaction(s) in this shift. Tick \"Show every "
              + "transaction\" to list them; the printed slip and the exports then list them too.";
    }

    public string? PromptSavePath(string suggestedName, bool pdf)
    {
        using var save = new SaveFileDialog
        {
            DefaultExt = pdf ? "pdf" : "xlsx",
            FileName = suggestedName,
            Filter = pdf ? "PDF document (*.pdf)|*.pdf" : "Excel workbook (*.xlsx)|*.xlsx",
            OverwritePrompt = true,
            Title = "Export the cash-up",
            InitialDirectory = GridExport.Downloads
        };

        return save.ShowDialog(this) == DialogResult.OK ? save.FileName : null;
    }

    public bool CountAndClose(Guid sessionId, bool onBehalf)
    {
        if (_views is null) return false;

        using var till = _views.Create<frmTillSession>();
        till.ForSession(sessionId, onBehalf);
        till.ShowDialog(this);
        return till.DialogResult == DialogResult.OK;
    }

    public bool ShowPayouts(Guid sessionId)
    {
        if (_views is null) return false;

        using var payouts = _views.Create<frmPayouts>();
        payouts.ForSession(sessionId);
        payouts.ShowDialog(this);
        return payouts.Changed;
    }

    public bool PreviewSlip(Application.Sales.PrintableDocument document,
                            Application.Sales.ReceiptHeaderDto header) =>
        frmSlipPreview.Confirm(this, document, header);

    public event EventHandler? ShiftChanged;
    public event EventHandler? DayChanged;
    public event EventHandler? RefreshRequested;
    public event EventHandler? EveryTransactionChanged;
    public event EventHandler? PrintRequested;
    public event EventHandler? ExportPdfRequested;
    public event EventHandler? ExportExcelRequested;
    public event EventHandler? CountRequested;
    public event EventHandler? PayoutsRequested;
    public event EventHandler? HistoryRequested;
}
