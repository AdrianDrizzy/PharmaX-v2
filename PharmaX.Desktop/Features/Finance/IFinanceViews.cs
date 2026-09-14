using PharmaX.Application.Claims;
using PharmaX.Application.Finance;
using PharmaX.Application.Reference;
using PharmaX.Domain.Finance;

namespace PharmaX.Desktop.Features.Finance;

/// <summary>
/// The chart of accounts: what the books post to.
/// </summary>
public interface IChartView : Mvp.IView
{
    bool IncludeInactive { get; }
    Guid? SelectedAccountId { get; }

    void ShowAccounts(IReadOnlyList<LedgerAccountDto> accounts);

    /// Collects a new account. Null where the operator gave up.
    (string Code, string Name, LedgerAccountType Type)? PromptNewAccount();

    /// Collects a new name for an account that already exists.
    string? PromptRename(string current);

    event EventHandler? RefreshRequested;
    event EventHandler? AddRequested;
    event EventHandler? RenameRequested;
    event EventHandler? DeactivateRequested;
}

/// <summary>
/// A hand-written journal: the entry PharmaX cannot make for itself.
/// </summary>
public interface IJournalView : Mvp.IView
{
    DateOnly PostingDate { get; }
    string Narration { get; }

    /// The lines as captured, in the order they were typed.
    IReadOnlyList<JournalLineDto> Lines { get; }

    /// Fills the account drop-down the grid picks from.
    void ShowAccounts(IReadOnlyList<LedgerAccountDto> accounts);

    /// <summary>
    /// The running totals under the grid, and whether they agree.
    /// </summary>
    /// <remarks>
    /// Shown as the operator types rather than only on Post, because
    /// "your journal is out by 40 cents" is far easier to act on while
    /// the last line is still on the screen.
    /// </remarks>
    void ShowTotals(decimal debits, decimal credits);

    void Clear();

    event EventHandler? LinesChanged;
    event EventHandler? PostRequested;
}

/// <summary>
/// Every account and what it owes or is owed, as at a date.
/// </summary>
public interface ITrialBalanceView : Mvp.IView
{
    DateOnly AsAt { get; }
    string? SelectedAccountCode { get; }

    void ShowBalance(TrialBalanceDto balance);
    void ShowEntries(string accountCode, IReadOnlyList<
        Application.Repositories.LedgerEntry> entries);

    event EventHandler? RefreshRequested;
    event EventHandler? DrillDownRequested;
}

/// <summary>
/// What the medical aid schemes owe, and where their remittances are
/// recorded.
/// </summary>
public interface IClaimsView : Mvp.IView
{
    /// Null means every scheme. A remittance needs one named.
    Guid? SelectedSchemeId { get; }

    DateOnly ReceivedOn { get; }
    string Reference { get; }
    Guid? SelectedClaimId { get; }

    void ShowSchemes(IReadOnlyList<ReferenceItemDto> schemes);
    void ShowClaims(IReadOnlyList<ClaimDto> claims);
    void ShowAgeAnalysis(IReadOnlyList<SchemeAgeDto> analysis);

    /// The rows the operator filled in from the remittance advice.
    IReadOnlyList<RemittanceLineDto> CollectRemittance();

    string? PromptRejectReason();
    void ClearRemittance();

    event EventHandler? RefreshRequested;
    event EventHandler? SchemeChanged;
    event EventHandler? SettleRequested;
    event EventHandler? RejectRequested;
    event EventHandler? AgeAnalysisRequested;
}
