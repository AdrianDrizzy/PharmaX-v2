using PharmaX.Application.Reporting;
using PharmaX.Application.Abstractions;
using System.Globalization;
using PharmaX.Application.Finance;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Finance;

namespace PharmaX.Desktop.Features.Finance;

/// <summary>
/// The accounts the books post to.
/// </summary>
public partial class frmChartOfAccounts : PhxFormBase, IChartView
{
    private readonly ChartPresenter? _presenter;
    private readonly IWorkbookWriter? _workbooks;
    private readonly ICurrentUser? _user;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.LedgerRead;

    /// Designer only. Never called at runtime.
    public frmChartOfAccounts()
    {
        InitializeComponent();

        // A different list, so its first page.
        chkInactive.CheckedChanged += (_, _) =>
        {
            GridPager.Of(grdAccounts)?.Restart();
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        };

        btnAdd.Click += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);

        btnRename.Click += (_, _) =>
            RenameRequested?.Invoke(this, EventArgs.Empty);

        btnDeactivate.Click += (_, _) =>
            DeactivateRequested?.Invoke(this, EventArgs.Empty);

        grdAccounts.DoubleClick += (_, _) =>
            RenameRequested?.Invoke(this, EventArgs.Empty);

        btnExport.Click += (_, _) => Export();

        btnClose.Click += (_, _) => CloseView(false);
    }

    /// Runtime. Resolved by the container.
    public frmChartOfAccounts(ChartPresenter presenter, IWorkbookWriter workbooks,
                       ICurrentUser user,
                       Application.Security.IPermissionService permissions) : this()
    {
        _presenter = presenter;
        _workbooks = workbooks;
        _user = user;
        btnDeactivate.Visible = Application.Security.PermissionServiceExtensions
            .Has(permissions, Domain.Security.PermissionCode.AccountDeactivate);
        _presenter.Attach(this);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    public bool IncludeInactive => chkInactive.Checked;

    public IReadOnlyList<Guid> SelectedAccountIds =>
        grdAccounts.SelectedInOrder()
            .Select(r => r.DataBoundItem)
            .OfType<AccountRow>()
            .Select(a => a.Id)
            .ToList();

    public Guid? SelectedAccountId =>
        grdAccounts.CurrentRow?.DataBoundItem is AccountRow row
            ? row.Id
            : null;

    public void ShowAccounts(IReadOnlyList<LedgerAccountDto> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        GridPager.Attach(grdAccounts).Show(accounts.Select(AccountRow.From));

        foreach (DataGridViewRow row in grdAccounts.Rows)
        {
            if (row.DataBoundItem is AccountRow { IsActive: false })
                row.DefaultCellStyle.ForeColor = PhxTheme.Muted;
        }

        lblCount.Text = string.Format(CultureInfo.CurrentCulture,
            "{0} account(s)", accounts.Count);
    }

    public (string Code, string Name, LedgerAccountType Type)?
        PromptNewAccount()
    {
        var code = frmTextPrompt.Ask(this, "New account",
            "Account code, for example 6100", hint: "e.g. 6100");

        if (string.IsNullOrWhiteSpace(code)) return null;

        var name = frmTextPrompt.Ask(this, "New account",
            $"What is account {code} called?", hint: "e.g. Bank charges");

        if (string.IsNullOrWhiteSpace(name)) return null;

        var type = frmChoicePrompt.Ask(this, "New account",
            $"What kind of account is {code} {name}?",
            new[]
            {
                ("Asset", LedgerAccountType.Asset),
                ("Liability", LedgerAccountType.Liability),
                ("Equity", LedgerAccountType.Equity),
                ("Income", LedgerAccountType.Income),
                ("Expense", LedgerAccountType.Expense)
            });

        return type is null ? null : (code, name, type.Value);
    }

    public string? PromptRename(string current) =>
        frmTextPrompt.Ask(this, "Rename account",
            $"What should \"{current}\" be called?", hint: "e.g. Bank charges");

    /// One account as the grid shows it.
    private sealed record AccountRow(Guid Id, string Code, string Name,
        string TypeText, string StatusText, bool IsActive)
    {
        public static AccountRow From(LedgerAccountDto dto) => new(
            dto.Id, dto.Code, dto.Name,
            dto.AccountType switch
            {
                LedgerAccountType.Asset => "Asset",
                LedgerAccountType.Liability => "Liability",
                LedgerAccountType.Equity => "Equity",
                LedgerAccountType.Income => "Income",
                _ => "Expense"
            },
            dto.IsActive ? "In use" : "Not in use",
            dto.IsActive);
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? AddRequested;
    public event EventHandler? RenameRequested;
    public event EventHandler? DeactivateRequested;

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

        var saved = GridExport.Offer(this, _workbooks, grdAccounts,
            "Chart of Accounts", "Chart of accounts", _user.Username);

        if (saved is null)
        {
            if (grdAccounts.Rows.Count == 0)
                ShowInfo("There is nothing on the screen to export.");

            return;
        }

        ShowSaved(saved);
    }
}
