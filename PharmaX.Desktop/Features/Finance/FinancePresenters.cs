using System.Globalization;
using PharmaX.Application.Finance;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Finance;

public sealed class ChartPresenter : PresenterBase<IChartView>
{
    private readonly ILedgerService _ledger;

    private IReadOnlyList<LedgerAccountDto> _accounts =
        Array.Empty<LedgerAccountDto>();

    public ChartPresenter(ILedgerService ledger) => _ledger = ledger;

    protected override void Subscribe()
    {
        View.RefreshRequested += (_, _) => _ = LoadAsync();
        View.AddRequested += (_, _) => _ = OnAddAsync();
        View.RenameRequested += (_, _) => _ = OnRenameAsync();
        View.DeactivateRequested += (_, _) => _ = OnDeactivateAsync();
    }

    public override Task InitialiseAsync() => LoadAsync();

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _ledger.AccountsAsync(View.IncludeInactive);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        _accounts = result.Value;
        View.ShowAccounts(_accounts);
    });

    private Task OnAddAsync() => GuardAsync(async () =>
    {
        if (View.PromptNewAccount() is not var (code, name, type)) return;

        var result = await _ledger.AddAccountAsync(code, name, type);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        await LoadAsync();
    });

    private Task OnRenameAsync() => GuardAsync(async () =>
    {
        if (Selected() is not LedgerAccountDto account) return;

        var name = View.PromptRename(account.Name);
        if (string.IsNullOrWhiteSpace(name)) return;

        var result = await _ledger.RenameAccountAsync(account.Id, name);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        await LoadAsync();
    });

    private Task OnDeactivateAsync() => GuardAsync(async () =>
    {
        if (Selected() is not LedgerAccountDto account) return;

        if (!View.Confirm($"Take {account.Code} {account.Name} out of use? "
                          + "Its history stays in the books."))
            return;

        var result = await _ledger.DeactivateAccountAsync(account.Id);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        await LoadAsync();
    });

    private LedgerAccountDto? Selected()
    {
        if (View.SelectedAccountId is not Guid id)
        {
            View.ShowInfo("Choose an account first.");
            return null;
        }

        return _accounts.FirstOrDefault(a => a.Id == id);
    }
}

public sealed class JournalPresenter : PresenterBase<IJournalView>
{
    private readonly ILedgerService _ledger;

    public JournalPresenter(ILedgerService ledger) => _ledger = ledger;

    protected override void Subscribe()
    {
        View.LinesChanged += (_, _) => ShowTotals();
        View.PostRequested += (_, _) => _ = OnPostAsync();
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        var result = await _ledger.AccountsAsync();

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowAccounts(result.Value);
        ShowTotals();
    });

    private void ShowTotals()
    {
        var lines = View.Lines;

        View.ShowTotals(
            lines.Where(l => l.IsDebit).Sum(l => l.Amount),
            lines.Where(l => !l.IsDebit).Sum(l => l.Amount));
    }

    private Task OnPostAsync() => GuardAsync(async () =>
    {
        var lines = View.Lines;

        // Caught here as well as in the service, because the message
        // that helps is the one that arrives before the operator has
        // pressed Post and started wondering what went wrong.
        if (lines.Count < 2)
        {
            View.ShowError("A journal needs at least one debit and one "
                           + "credit.");
            return;
        }

        var result = await _ledger.PostJournalAsync(View.PostingDate,
            View.Narration, lines);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowInfo(string.Format(CultureInfo.CurrentCulture,
            "Journal {0} posted, {1} each side.",
            result.Value.BatchNumber,
            Format.Money(result.Value.TotalDebits)));

        View.Clear();
        ShowTotals();
    });
}

public sealed class TrialBalancePresenter : PresenterBase<ITrialBalanceView>
{
    private readonly ILedgerService _ledger;

    public TrialBalancePresenter(ILedgerService ledger) => _ledger = ledger;

    protected override void Subscribe()
    {
        View.RefreshRequested += (_, _) => _ = LoadAsync();
        View.DrillDownRequested += (_, _) => _ = OnDrillDownAsync();
    }

    public override Task InitialiseAsync() => LoadAsync();

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _ledger.TrialBalanceAsync(View.AsAt);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowBalance(result.Value);

        // This should be impossible - a batch that does not balance is
        // refused at posting - so if it ever happens it means something
        // reached the tables without going through a journal, and the
        // operator needs to know before they act on the figures.
        if (!result.Value.IsBalanced)
            View.ShowError(
                "The trial balance does not balance. Debits come to "
                + Format.Money(result.Value.TotalDebits) + " and credits to "
                + Format.Money(result.Value.TotalCredits)
                + ". Tell support before relying on these figures.");
    });

    private Task OnDrillDownAsync() => GuardAsync(async () =>
    {
        if (View.SelectedAccountCode is not string code)
        {
            View.ShowInfo("Choose an account first.");
            return;
        }

        // From the beginning: a trial balance is cumulative, so the
        // postings behind a figure are every posting there has been.
        var result = await _ledger.EntriesAsync(code,
            new DateOnly(2000, 1, 1), View.AsAt);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowEntries(code, result.Value);
    });
}
