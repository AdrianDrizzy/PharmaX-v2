using PharmaX.Application.Tills;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Tills;

/// <summary>
/// Cash-up history: every till closed between two days, with what its
/// drawer should have held, what was counted and why it was out. Opened
/// from Cash Up, which then shows the one chosen. Built in code, about 900
/// by 520 before scaling.
/// </summary>
public sealed class frmCashUpHistory : PhxFormBase, ICashUpHistoryView
{
    internal const string Guidance =
        "Every till cashed up between the two days, the latest first. Open one, or double-click it, "
        + "to see it on Cash Up, where its slip can be printed again or exported.";

    private readonly CashUpHistoryPresenter? _presenter;

    private readonly DateTimePicker _from = new();
    private readonly DateTimePicker _to = new();
    private readonly PhxGrid _grid = new();
    private readonly Label _total = new();

    /// Designer and tests only.
    public frmCashUpHistory() => Build();

    public frmCashUpHistory(CashUpHistoryPresenter presenter)
    {
        _presenter = presenter;
        Build();
        _presenter.Attach(this);
    }

    /// Whoever may open Cash Up.
    protected override bool MayOpen(Application.Security.IPermissionService permissions) =>
        Application.Security.PermissionServiceExtensions.HasAny(permissions,
            Domain.Security.PermissionCode.SessionCashUp,
            Domain.Security.PermissionCode.SessionCloseOtherUser);

    /// The cash-up chosen, once the window has closed with OK.
    public ShiftCashUpDto? Picked { get; private set; }

    private void Build()
    {
        Text = "PharmaX - Cash-up history";
        Name = "frmCashUpHistory";
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(900, 520);
        MinimumSize = new Size(760, 420);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var guide = new Label
        {
            AutoSize = false,
            BackColor = PhxTheme.Information,
            Dock = DockStyle.Top,
            Height = 52,
            Name = "lblGuide",
            Padding = new Padding(12, 6, 12, 6),
            Text = Guidance
        };

        // ---- The days ---------------------------------------------------
        var bar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(10, 6, 10, 2),
            WrapContents = false
        };

        Label Caption(string text) => new()
        {
            AutoSize = true,
            Margin = new Padding(3, 7, 6, 0),
            Text = text
        };

        void Day(DateTimePicker picker, string name, int daysBack)
        {
            picker.Format = DateTimePickerFormat.Custom;
            picker.CustomFormat = "ddd dd/MM/yyyy";
            picker.Width = 150;
            picker.Name = name;
            picker.MaxDate = DateTime.Today;
            picker.Value = DateTime.Today.AddDays(-daysBack);
        }

        Day(_from, "dtpFrom", 30);
        Day(_to, "dtpTo", 0);

        var show = new Button { AutoSize = true, Name = "btnShow", Text = "&Show" };
        show.Click += (_, _) => RangeChanged?.Invoke(this, EventArgs.Empty);

        bar.Controls.Add(Caption("&From"));
        bar.Controls.Add(_from);
        bar.Controls.Add(Caption("&To"));
        bar.Controls.Add(_to);
        bar.Controls.Add(show);

        // ---- The cash-ups -----------------------------------------------
        _grid.Dock = DockStyle.Fill;
        _grid.Name = "grdHistory";
        _grid.AddColumn("Closed", "Closed", 120, "ddd dd/MM/yyyy HH:mm");
        _grid.AddColumn("Till", "Till", 45);
        _grid.AddColumn("OpenedBy", "Opened by", 100);
        _grid.AddColumn("ClosedBy", "Closed by", 100);
        _grid.AddColumn("Net", "Net takings", 85, "N2", rightAlign: true);
        _grid.AddColumn("Expected", "Expected", 85, "N2", rightAlign: true);
        _grid.AddColumn("Counted", "Counted", 85, "N2", rightAlign: true);
        _grid.AddColumn("Variance", "Variance", 80, "N2", rightAlign: true);
        _grid.AddColumn("Reason", "Reason given", 160);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Open(); };

        var list = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4) };
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
        var open = new Button
        {
            AutoSize = true,
            Name = "btnOpen",
            Padding = new Padding(8, 2, 8, 2),
            Text = "&Open on Cash Up"
        };
        open.Click += (_, _) => Open();

        _total.AutoSize = true;
        _total.Font = new Font(Font, FontStyle.Bold);
        _total.Margin = new Padding(3, 8, 24, 0);
        _total.Name = "lblTotal";

        buttons.Controls.Add(close);
        buttons.Controls.Add(open);
        buttons.Controls.Add(_total);

        Controls.Add(list);
        Controls.Add(bar);
        Controls.Add(guide);
        Controls.Add(buttons);

        CancelButton = close;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        frmCashUp.FitGuide(this);
        if (_presenter is not null) await _presenter.InitialiseAsync();
        ActiveControl = _grid;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && _grid.Focused)
        {
            Open();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Open()
    {
        if (_grid.SelectedInOrder().FirstOrDefault()?.Tag is not ShiftCashUpDto shift)
        {
            ShowError("Choose the cash-up to open.");
            return;
        }

        Picked = shift;
        CloseView(true);
    }

    public DateOnly From => DateOnly.FromDateTime(_from.Value);

    public DateOnly To => DateOnly.FromDateTime(_to.Value);

    public void ShowHistory(IReadOnlyList<ShiftCashUpDto> shifts)
    {
        ArgumentNullException.ThrowIfNull(shifts);

        _grid.Rows.Clear();

        foreach (var s in shifts)
        {
            var index = _grid.Rows.Add(s.ClosedAtLocal, s.TillNumber, s.CashierName,
                s.ClosedByName, s.NetTakings, s.ExpectedTotal, s.Declared, s.Variance,
                s.VarianceReason);
            var row = _grid.Rows[index];
            row.Tag = s;

            // Selection colours too: the newest row starts selected.
            if (s.Variance is < 0m)
                row.Cells["Variance"].Style.ForeColor =
                    row.Cells["Variance"].Style.SelectionForeColor = PhxTheme.Negative;
        }

        _total.Text = shifts.Count == 0
            ? "No till was cashed up between those days."
            : string.Format(Format.Culture, "{0} cash-up(s), variance in all {1}", shifts.Count,
                Format.Money(shifts.Sum(s => s.Variance ?? 0m)));
    }

    public event EventHandler? RangeChanged;
}
