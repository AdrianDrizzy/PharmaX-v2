using System.Globalization;
using PharmaX.Application.Tills.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Sales;

namespace PharmaX.Desktop.Features.Tills;

public partial class frmTillSession : PhxFormBase, ITillSessionView
{
    private readonly TillSessionPresenter? _presenter;

    /// Designer only. Never called at runtime.
    public frmTillSession()
    {
        InitializeComponent();

        btnOpen.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        btnXRead.Click += (_, _) => XReadRequested?.Invoke(this, EventArgs.Empty);
        btnDeclare.Click += (_, _) =>
            DeclareRequested?.Invoke(this, EventArgs.Empty);
        btnCloseSession.Click += (_, _) =>
            CloseSessionRequested?.Invoke(this, EventArgs.Empty);
        btnClose.Click += (_, _) => CloseView(false);

        Resize += (_, _) => SizeCountGridToRows();
    }

    /// Runtime. Resolved by the container.
    public frmTillSession(TillSessionPresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.X:
                XReadRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case Keys.Control | Keys.Z:
                CloseSessionRequested?.Invoke(this, EventArgs.Empty);
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    public void ShowSession(TillSessionDto? session)
    {
        var open = session is not null;

        lblTitle.Text = open
            ? $"Till {session!.TillNumber} - session open"
            : "Till session";

        lblSessionState.Text = session is null
            ? "No session is open on this till. Nothing can be sold until "
              + "one is (POS-013)."
            : string.Format(Format.Culture,
                "Opened {0} by {1}   -   float {2}   -   {3} X-read(s) taken",
                Format.DateTimeShort(session.OpenedAtUtc.ToLocalTime()),
                session.OpenedByName,
                Format.Money(session.OpeningFloat),
                session.XReadCount);

        btnOpen.Enabled = !open;
        btnXRead.Enabled = open;
        btnDeclare.Enabled = open;
        btnCloseSession.Enabled = open;
        grdDenominations.Enabled = open;
        txtEcoCash.Enabled = open;

        // Nothing to count against a closed till, so the grid reads as
        // unavailable rather than as an empty one somebody should fill.
        grdDenominations.DefaultCellStyle.BackColor = open
            ? PhxTheme.Field
            : PhxTheme.Band;
    }

    public void ShowExpected(IReadOnlyList<TenderExpectationDto> expectations)
    {
        ArgumentNullException.ThrowIfNull(expectations);

        grdExpected.Rows.Clear();

        foreach (var e in expectations)
            grdExpected.Rows.Add(e.TenderType, e.Expected,
                                 e.TransactionCount);
    }

    public void ShowVariance(IReadOnlyList<VarianceLineDto> variances)
    {
        ArgumentNullException.ThrowIfNull(variances);

        grdVariance.Rows.Clear();

        foreach (var v in variances)
        {
            var index = grdVariance.Rows.Add(v.TenderType, v.Expected,
                                             v.Declared, v.Variance);

            var row = grdVariance.Rows[index];

            // Red only where the difference is big enough to need
            // explaining; a few cents out is normal and colouring it
            // teaches the operator to ignore the colour.
            if (v.RequiresReason)
                PhxTheme.Flag(row, PhxTheme.Problem);
            else if (v.Variance != 0m)
                PhxTheme.Flag(row, PhxTheme.Attention);

            if (v.Variance < 0m)
                row.Cells["Variance"].Style.ForeColor = PhxTheme.Negative;
        }
    }

    // ---- The denomination grid ------------------------------------------

    /// <summary>
    /// The counted denominations come from the form as a plain
    /// dictionary, so the presenter never touches a grid cell.
    /// </summary>
    public IReadOnlyDictionary<string, int> CountedDenominations
    {
        get
        {
            var counted = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (DataGridViewRow row in grdDenominations.Rows)
            {
                if (row.Cells["Denomination"].Value is not string key)
                    continue;

                var count = ToCount(row.Cells["Count"].Value);
                if (count > 0) counted[key] = count;
            }

            return counted;
        }
    }

    /// <summary>
    /// What the EcoCash merchant statement shows this till received, or
    /// null where the operator has not entered it.
    /// </summary>
    public decimal? EcoCashStatement => txtEcoCash.Amount;

    public void ShowDenominationGrid(
        IReadOnlyList<DenominationDto> denominations)
    {
        ArgumentNullException.ThrowIfNull(denominations);

        grdDenominations.Rows.Clear();

        foreach (var d in denominations)
            grdDenominations.Rows.Add(d.Key, d.Value, 0, 0m);

        lblDeclaredValue.Text = Format.Money(0m);
        SizeCountGridToRows();
    }

    /// <summary>
    /// The grid is made exactly as tall as the notes and coins it lists,
    /// so the counted total sits against the column it totals instead of
    /// at the bottom of a panel of empty white.
    /// </summary>
    private void SizeCountGridToRows()
    {
        if (grdDenominations.Rows.Count == 0) return;

        var wanted = grdDenominations.ColumnHeadersHeight + 2
            + grdDenominations.Rows.Cast<DataGridViewRow>()
                              .Sum(row => row.Height);

        var available = pnlCount.ClientSize.Height
            - pnlCount.Padding.Vertical
            - lblCountCaption.Height
            - lblCountHint.Height
            - pnlCountFooter.Height
            - pnlStatement.Height;

        grdDenominations.Height = Math.Max(120, Math.Min(wanted, available));
    }

    private void OnDenominationEdited(object? sender,
                                      DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (grdDenominations.Columns[e.ColumnIndex].Name != "Count") return;

        var row = grdDenominations.Rows[e.RowIndex];
        var value = Convert.ToDecimal(row.Cells["Value"].Value,
                                      CultureInfo.CurrentCulture);
        var count = ToCount(row.Cells["Count"].Value);

        // Presentation arithmetic only: the authoritative total is
        // computed by the service from the same counts. A display total
        // that has drifted must never become the declared amount.
        row.Cells["Total"].Value = value * count;
        lblDeclaredValue.Text = Format.Money(TotalDeclared());
    }

    private decimal TotalDeclared() =>
        grdDenominations.Rows.Cast<DataGridViewRow>()
            .Sum(r => r.Cells["Total"].Value is decimal total ? total : 0m);

    /// <summary>
    /// A grid cell hands back whatever the operator typed. Anything that
    /// is not a whole number of notes counts as none rather than as an
    /// exception in the middle of a cash-up.
    /// </summary>
    private static int ToCount(object? value) =>
        value switch
        {
            int n => n,
            null => 0,
            _ => int.TryParse(value.ToString(), NumberStyles.Integer,
                              CultureInfo.CurrentCulture, out var parsed)
                ? parsed
                : 0
        };

    // ---- Prompts ---------------------------------------------------------

    public decimal? PromptFloat() =>
        frmNumberPrompt.Ask(this, "Open session",
            "How much is being counted into the drawer as the opening "
            + "float?", 0m, decimals: 2);

    public string? PromptVarianceReason(string tender, decimal variance) =>
        frmTextPrompt.Ask(this, "Variance",
            string.Format(Format.Culture,
                "The {0} is {1} by {2}. Why?",
                Enum.TryParse<TenderType>(tender, out var t)
                    && t != TenderType.Cash
                    ? t.DisplayName() + " statement"
                    : "drawer",
                variance < 0m ? "short" : "over",
                Format.Money(Math.Abs(variance))),
            hint: "e.g. Change given short on one sale");

    public event EventHandler? OpenRequested;
    public event EventHandler? XReadRequested;
    public event EventHandler? DeclareRequested;
    public event EventHandler? CloseSessionRequested;
}
