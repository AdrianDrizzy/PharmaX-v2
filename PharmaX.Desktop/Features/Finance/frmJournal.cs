using PharmaX.Application.Reporting;
using PharmaX.Application.Abstractions;
using System.Globalization;
using PharmaX.Application.Finance;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Finance;

/// <summary>
/// A hand-written journal: the entry PharmaX cannot make for itself.
/// </summary>
/// <remarks>
/// Most of the books write themselves as the pharmacy trades. This is
/// for the rest - month-end stock, an opening balance, a correction, the
/// accountant's year-end adjustments. It exists because periodic
/// inventory needs it: Stock on Hand is brought to the counted figure by
/// a journal from this screen.
/// </remarks>
public partial class frmJournal : PhxFormBase, IJournalView
{
    private readonly JournalPresenter? _presenter;
    private readonly IWorkbookWriter? _workbooks;
    private readonly ICurrentUser? _user;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.LedgerPost;

    /// Designer only. Never called at runtime.
    public frmJournal()
    {
        InitializeComponent();

        dtpPostingDate.Value = DateTime.Today;

        // Every edit re-totals, so the operator sees the journal come
        // into balance as they type rather than being told it did not
        // after they pressed Post.
        grdLines.CellValueChanged += (_, _) => RaiseChanged();
        grdLines.UserDeletedRow += (_, _) => RaiseChanged();

        // A combo box commits its value only when the cell loses focus,
        // which is one keystroke too late for a running total.
        grdLines.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grdLines.IsCurrentCellDirty)
                grdLines.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        btnPost.Click += (_, _) => PostRequested?.Invoke(this, EventArgs.Empty);
        btnClear.Click += (_, _) => Clear();
        btnExport.Click += (_, _) => Export();

        btnClose.Click += (_, _) => CloseView(false);
    }

    /// Runtime. Resolved by the container.
    public frmJournal(JournalPresenter presenter, IWorkbookWriter workbooks,
                       ICurrentUser user) : this()
    {
        _presenter = presenter;
        _workbooks = workbooks;
        _user = user;
        _presenter.Attach(this);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    private void RaiseChanged() =>
        LinesChanged?.Invoke(this, EventArgs.Empty);

    public DateOnly PostingDate => DateOnly.FromDateTime(dtpPostingDate.Value);

    public string Narration => txtNarration.Text.Trim();

    public void ShowAccounts(IReadOnlyList<LedgerAccountDto> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        // "5900  Cash Short or Over" in the list, "5900" as the value:
        // the operator picks by name and the journal stores the code.
        colAccount.DataSource = accounts
            .Select(a => new { a.Code, Label = $"{a.Code}  {a.Name}" })
            .ToList();

        colAccount.DisplayMember = "Label";
        colAccount.ValueMember = "Code";
    }

    /// <summary>
    /// The lines as captured.
    /// </summary>
    /// <remarks>
    /// Anything half-typed is skipped rather than reported: a row with
    /// an account and no amount is the row the operator is in the middle
    /// of, and complaining about it on every keystroke would be
    /// unusable. What cannot be skipped is caught at Post, where the
    /// totals have to agree.
    /// </remarks>
    public IReadOnlyList<JournalLineDto> Lines
    {
        get
        {
            var lines = new List<JournalLineDto>();

            foreach (DataGridViewRow row in grdLines.Rows)
            {
                if (row.IsNewRow) continue;

                if (row.Cells["Account"].Value is not string code
                    || string.IsNullOrWhiteSpace(code))
                    continue;

                var debit = Amount(row.Cells["Debit"].Value);
                var credit = Amount(row.Cells["Credit"].Value);

                // One side or the other, never both. A row carrying two
                // figures is ambiguous, and guessing which was meant is
                // how a journal balances while saying the wrong thing.
                if (debit > 0m && credit > 0m) continue;
                if (debit <= 0m && credit <= 0m) continue;

                lines.Add(new JournalLineDto(code,
                    debit > 0m ? debit : credit,
                    debit > 0m,
                    row.Cells["Note"].Value as string));
            }

            return lines;
        }
    }

    public void ShowTotals(decimal debits, decimal credits)
    {
        var balanced = debits == credits && debits > 0m;

        lblTotals.Text = string.Format(CultureInfo.CurrentCulture,
            "Debits {0}     Credits {1}     {2}",
            Format.Money(debits), Format.Money(credits),
            balanced
                ? "in balance"
                : $"out by {Format.Money(Math.Abs(debits - credits))}");

        lblTotals.ForeColor = balanced
            ? PhxTheme.Accent
            : PhxTheme.ProblemText;

        // Post is unavailable until the journal balances, which is
        // kinder than letting it be pressed and refused.
        btnPost.Enabled = balanced;
        PhxTheme.MakePrimary(btnPost);
    }

    public void Clear()
    {
        grdLines.Rows.Clear();
        txtNarration.Clear();
        dtpPostingDate.Value = DateTime.Today;
        txtNarration.Focus();
    }

    /// <summary>
    /// Reads a typed figure, treating anything unparseable as nothing.
    /// </summary>
    /// <remarks>
    /// Silent because the cell is being typed into: half of "12.50" is
    /// "12." and a message box on the way through would make the screen
    /// impossible to use.
    /// </remarks>
    private static decimal Amount(object? value)
    {
        if (value is decimal already) return already;

        return decimal.TryParse(value?.ToString(), NumberStyles.Number,
                                CultureInfo.CurrentCulture, out var parsed)
                   && parsed > 0m
            ? parsed
            : 0m;
    }

    public event EventHandler? LinesChanged;
    public event EventHandler? PostRequested;

    /// <summary>
    /// The grid as it stands, to a workbook the operator names.
    /// </summary>
    /// <remarks>
    /// What is on the screen, filters and all: this is read as a working
    /// document and the figures were previously only reachable by typing
    /// them again somewhere else.
    /// </remarks>
    private void Export()
    {
        if (_workbooks is null || _user is null) return;   // designer

        var saved = GridExport.Offer(this, _workbooks, grdLines,
            "Journal Entry", "Journal lines as captured", _user.Username);

        if (saved is null)
        {
            if (grdLines.Rows.Count == 0)
                ShowInfo("There is nothing on the screen to export.");

            return;
        }

        ShowInfo($"Exported to {saved}");
    }
}
