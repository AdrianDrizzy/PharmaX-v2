using System.Globalization;
using PharmaX.Application.Claims;
using PharmaX.Application.Finance;
using PharmaX.Application.Reference;
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
        View.RefreshRequested += (_, _) => Enqueue(LoadAsync);
        View.AddRequested += (_, _) => Enqueue(OnAddAsync);
        View.RenameRequested += (_, _) => Enqueue(OnRenameAsync);
        View.DeactivateRequested += (_, _) => Enqueue(OnDeactivateAsync);
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
        View.PostRequested += (_, _) => Enqueue(OnPostAsync);
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
        View.RefreshRequested += (_, _) => Enqueue(LoadAsync);
        View.DrillDownRequested += (_, _) => Enqueue(OnDrillDownAsync);
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

public sealed class ClaimsPresenter : PresenterBase<IClaimsView>
{
    private readonly IClaimService _claims;
    private readonly IReferenceDataService _reference;

    public ClaimsPresenter(IClaimService claims,
                           IReferenceDataService reference)
    {
        _claims = claims;
        _reference = reference;
    }

    protected override void Subscribe()
    {
        View.RefreshRequested += (_, _) => Enqueue(LoadAsync);
        View.SchemeChanged += (_, _) => Enqueue(LoadAsync);
        View.SettleRequested += (_, _) => Enqueue(OnSettleAsync);
        View.RejectRequested += (_, _) => Enqueue(OnRejectAsync);
        View.AgeAnalysisRequested += (_, _) => Enqueue(OnAgeAnalysisAsync);
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        var schemes = await _reference.ListAsync(
            ReferenceKind.MedicalAidScheme, includeInactive: false);

        if (schemes.IsSuccess) View.ShowSchemes(schemes.Value);

        await Load();
    });

    private Task LoadAsync() => GuardAsync(Load);

    private async Task Load()
    {
        var result = await _claims.OutstandingAsync(View.SelectedSchemeId);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowClaims(result.Value);
    }

    private Task OnSettleAsync() => GuardAsync(async () =>
    {
        if (View.SelectedSchemeId is not Guid schemeId)
        {
            View.ShowInfo("Choose the scheme the remittance came from. "
                          + "One remittance settles one scheme's claims.");
            return;
        }

        var lines = View.CollectRemittance();

        if (lines.Count == 0)
        {
            View.ShowInfo("Type what each claim was paid, or use Pay in "
                          + "Full, before recording the remittance.");
            return;
        }

        var result = await _claims.SettleAsync(schemeId, View.ReceivedOn,
            View.Reference, lines);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowInfo(string.Format(CultureInfo.CurrentCulture,
            "{0} claim(s) settled. {1} received, {2} written off.",
            result.Value.Claims, Format.Money(result.Value.Paid),
            Format.Money(result.Value.WrittenOff)));

        View.ClearRemittance();
        await Load();
    });

    private Task OnRejectAsync() => GuardAsync(async () =>
    {
        if (View.SelectedClaimId is not Guid claimId)
        {
            View.ShowInfo("Choose the claim the scheme refused.");
            return;
        }

        var reason = View.PromptRejectReason();
        if (string.IsNullOrWhiteSpace(reason)) return;

        var result = await _claims.RejectAsync(claimId, reason);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        await Load();
    });

    private Task OnAgeAnalysisAsync() => GuardAsync(async () =>
    {
        var result = await _claims.AgeAnalysisAsync();

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowAgeAnalysis(result.Value);
    });
}
