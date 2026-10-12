using PharmaX.Application.Reporting;
using PharmaX.Application.Abstractions;
using System.Globalization;
using System.Text;
using PharmaX.Application.Finance;
using PharmaX.Application.Repositories;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Finance;

namespace PharmaX.Desktop.Features.Finance;

/// <summary>
/// Every account and what it owes or is owed, as at a date.
/// </summary>
/// <remarks>
/// The one report that proves the books are internally consistent, which
/// is why the totals line is the largest thing on the screen.
/// </remarks>
public partial class frmTrialBalance : PhxFormBase, ITrialBalanceView
{
    private readonly TrialBalancePresenter? _presenter;
    private readonly IWorkbookWriter? _workbooks;
    private readonly ICurrentUser? _user;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.LedgerRead;

    /// Designer only. Never called at runtime.
    public frmTrialBalance()
    {
        InitializeComponent();

        dtpAsAt.Value = DateTime.Today;

        dtpAsAt.ValueChanged += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);

        btnRefresh.Click += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);

        btnEntries.Click += (_, _) =>
            DrillDownRequested?.Invoke(this, EventArgs.Empty);

        grdBalance.DoubleClick += (_, _) =>
            DrillDownRequested?.Invoke(this, EventArgs.Empty);

        btnExport.Click += (_, _) => Export();

        btnClose.Click += (_, _) => CloseView(false);
    }

    /// Runtime. Resolved by the container.
    public frmTrialBalance(TrialBalancePresenter presenter, IWorkbookWriter workbooks,
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

    public DateOnly AsAt => DateOnly.FromDateTime(dtpAsAt.Value);

    public string? SelectedAccountCode =>
        grdBalance.CurrentRow?.DataBoundItem is BalanceRow row
            ? row.Code
            : null;

    public void ShowBalance(TrialBalanceDto balance)
    {
        ArgumentNullException.ThrowIfNull(balance);

        grdBalance.DataSource = balance.Lines.Select(BalanceRow.From).ToList();

        lblTotals.Text = string.Format(CultureInfo.CurrentCulture,
            "Debits {0}    Credits {1}    {2}",
            Format.Money(balance.TotalDebits),
            Format.Money(balance.TotalCredits),
            balance.IsBalanced ? "in balance" : "OUT OF BALANCE");

        lblTotals.ForeColor = balance.IsBalanced
            ? PhxTheme.Title
            : PhxTheme.ProblemText;
    }

    /// <summary>
    /// The postings behind one figure, in a window of their own.
    /// </summary>
    /// <remarks>
    /// A read-only dialog rather than a second grid on this form: the
    /// trial balance is read down a column, and splitting it in half to
    /// make room would spoil the one thing it is good at.
    /// </remarks>
    public void ShowEntries(string accountCode,
        IReadOnlyList<LedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            ShowInfo($"Nothing has been posted to {accountCode}.");
            return;
        }

        var text = new StringBuilder();

        foreach (var entry in entries)
        {
            text.AppendLine(string.Format(CultureInfo.CurrentCulture,
                "{0}  {1,-12} {2,-8} {3,12} {4,12}  {5}",
                Format.Date(entry.PostingDate), entry.BatchNumber,
                entry.Source,
                entry.Debit == 0m ? "" : Format.Amount(entry.Debit),
                entry.Credit == 0m ? "" : Format.Amount(entry.Credit),
                entry.Narration ?? ""));
        }

        text.AppendLine();

        text.AppendLine(string.Format(CultureInfo.CurrentCulture,
            "{0} posting(s).  Debits {1}, credits {2}.",
            entries.Count,
            Format.Money(entries.Sum(e => e.Debit)),
            Format.Money(entries.Sum(e => e.Credit))));

        frmTextViewer.Show(this, $"Postings to {accountCode}",
                           text.ToString());
    }

    /// One line of the trial balance, already formatted.
    private sealed record BalanceRow(string Code, string Name,
        string TypeText, string DebitText, string CreditText)
    {
        public static BalanceRow From(TrialBalanceLineDto line) => new(
            line.Code,
            line.Name,
            Describe(line.AccountType),
            line.Debit == 0m ? "" : Format.Amount(line.Debit),
            line.Credit == 0m ? "" : Format.Amount(line.Credit));

        private static string Describe(LedgerAccountType type) => type switch
        {
            LedgerAccountType.Asset => "Asset",
            LedgerAccountType.Liability => "Liability",
            LedgerAccountType.Equity => "Equity",
            LedgerAccountType.Income => "Income",
            _ => "Expense"
        };
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? DrillDownRequested;

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

        var saved = GridExport.Offer(this, _workbooks, grdBalance,
            "Trial Balance", string.Format(CultureInfo.CurrentCulture, "As at {0:dd/MM/yyyy}", dtpAsAt.Value), _user.Username);

        if (saved is null)
        {
            if (grdBalance.Rows.Count == 0)
                ShowInfo("There is nothing on the screen to export.");

            return;
        }

        ShowSaved(saved);
    }
}
