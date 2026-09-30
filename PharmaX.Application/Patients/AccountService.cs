using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Finance;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Patients;

public sealed record AccountDto(Guid Id, string AccountNumber, string Name,
    Guid? PatientId, decimal Balance, decimal CreditLimit,
    decimal Available, bool IsOnHold, string? HoldReason, int TermsDays);

/// One line of a statement, with the balance it left behind.
public sealed record StatementLineDto(DateOnly OccurredOn,
    AccountEntryKind Kind, string KindText, string? Reference,
    string? Narration, decimal Charged, decimal Paid, decimal Balance);

public sealed record StatementDto(AccountDto Account, DateOnly From,
    DateOnly Until, decimal Opening,
    IReadOnlyList<StatementLineDto> Lines, decimal Closing);

/// <summary>
/// Patient accounts: what somebody owes the pharmacy, and what has been
/// put on or taken off it.
/// </summary>
/// <remarks>
/// Every write goes through here, because Account.Balance and the
/// movements behind it have to agree and this is the only place that
/// writes both.
/// </remarks>
public interface IAccountService
{
    Task<Result<IReadOnlyList<AccountDto>>> SearchAsync(string term = "",
        CancellationToken ct = default);

    /// Everything owing, largest first.
    Task<Result<IReadOnlyList<AccountDto>>> OwingAsync(
        CancellationToken ct = default);

    Task<Result<AccountDto>> GetAsync(Guid id,
                                      CancellationToken ct = default);

    Task<Result<AccountDto>> OpenAsync(Guid patientId, decimal creditLimit,
        int termsDays = 30, CancellationToken ct = default);

    Task<Result> ChangeCreditLimitAsync(Guid accountId, decimal limit,
        CancellationToken ct = default);

    Task<Result> HoldAsync(Guid accountId, string reason,
                           CancellationToken ct = default);

    Task<Result> ReleaseAsync(Guid accountId,
                              CancellationToken ct = default);

    /// <summary>
    /// Puts a finalised sale on the account. Refuses where it would
    /// breach the credit limit, or the account is on hold.
    /// </summary>
    /// <remarks>
    /// Called from inside the sale's transaction, so a sale that fails
    /// to save charges nobody - and a charge that is refused takes the
    /// sale down with it, which is the right way round: better a till
    /// that will not complete the sale than a patient who owes money
    /// nobody recorded.
    /// </remarks>
    Task ChargeAsync(Account account, Sale sale, Money amount,
                     CancellationToken ct = default);

    /// Money received off an account.
    Task<Result<AccountDto>> TakePaymentAsync(Guid accountId, decimal amount,
        string tenderType, DateOnly receivedOn, string? reference,
        CancellationToken ct = default);

    Task<Result<StatementDto>> StatementAsync(Guid accountId, DateOnly from,
        DateOnly until, CancellationToken ct = default);
}

public sealed class AccountService : IAccountService
{
    private readonly IAccountRepository _accounts;
    private readonly IPatientRepository _patients;
    private readonly IJournalPoster _journals;
    private readonly ICurrentUser _user;
    private readonly IDocumentNumbering _numbering;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public AccountService(IAccountRepository accounts,
        IPatientRepository patients, IJournalPoster journals,
        ICurrentUser user,
        IDocumentNumbering numbering, IPermissionService permissions,
        IAuditWriter audit, IUnitOfWork uow, IClock clock)
    {
        _accounts = accounts;
        _patients = patients;
        _journals = journals;
        _user = user;
        _numbering = numbering;
        _permissions = permissions;
        _audit = audit;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<AccountDto>>> SearchAsync(
        string term = "", CancellationToken ct = default)
    {
        // A cashier needs to find an account to put a sale on it, so this
        // is the till's permission rather than a manager's.
        _permissions.Demand(PermissionCode.SaleCreate);

        var found = await _accounts.SearchAsync(term, ct: ct);

        IReadOnlyList<AccountDto> described =
            found.Select(Describe).ToList();

        return Result<IReadOnlyList<AccountDto>>.Ok(described);
    }

    public async Task<Result<IReadOnlyList<AccountDto>>> OwingAsync(
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        var owing = await _accounts.OwingAsync(ct);

        IReadOnlyList<AccountDto> described =
            owing.Select(Describe).ToList();

        return Result<IReadOnlyList<AccountDto>>.Ok(described);
    }

    public async Task<Result<AccountDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        var account = await _accounts.GetAsync(id, ct);

        return account is null
            ? Result<AccountDto>.Fail("ACC-000", "Account not found.")
            : Result<AccountDto>.Ok(Describe(account));
    }

    public async Task<Result<AccountDto>> OpenAsync(Guid patientId,
        decimal creditLimit, int termsDays = 30,
        CancellationToken ct = default)
    {
        // Deciding somebody may owe the pharmacy money is not a till
        // decision.
        _permissions.Demand(PermissionCode.LedgerPost);

        if (creditLimit < 0m)
            return Result<AccountDto>.Fail("ACC-001",
                "A credit limit cannot be negative.");

        var patient = await _patients.GetAsync(patientId, ct);

        if (patient is null)
            return Result<AccountDto>.Fail("PAT-000", "Patient not found.");

        if (patient.AccountId is Guid existing)
        {
            var already = await _accounts.GetAsync(existing, ct);

            if (already is not null)
                return Result<AccountDto>.Fail("ACC-002",
                    $"{patient.FullName} already has account "
                    + $"{already.AccountNumber}.");
        }

        var number = await _numbering.NextAsync(DocumentSeries.Account, ct);

        var account = Account.Open(number, patient.FullName,
            Money.Of(creditLimit), patient.Id, termsDays);

        await _accounts.AddAsync(account, ct);
        patient.LinkAccount(account.Id);

        _audit.Applied("acc.account.open", "Account", account.Id,
            $"{number} for {patient.FullName}, limit {account.CreditLimit}");

        await _uow.SaveChangesAsync(ct);

        return Result<AccountDto>.Ok(Describe(account));
    }

    public async Task<Result> ChangeCreditLimitAsync(Guid accountId,
        decimal limit, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.LedgerPost);

        var account = await _accounts.GetAsync(accountId, ct);

        if (account is null)
            return Result.Fail("ACC-000", "Account not found.");

        try
        {
            account.ChangeCreditLimit(Money.Of(limit));
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            return Result.Fail("ACC-003", ex.Message);
        }

        _audit.Applied("acc.limit.change", "Account", accountId,
            string.Format(CultureInfo.CurrentCulture, "New limit {0:0.00}",
                limit));

        await _uow.SaveChangesAsync(ct);

        return Result.Ok();
    }

    public async Task<Result> HoldAsync(Guid accountId, string reason,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.LedgerPost);

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Fail("ACC-004",
                "Putting an account on hold needs a reason. The cashier "
                + "who has to refuse the customer will be asked why.");

        var account = await _accounts.GetAsync(accountId, ct);

        if (account is null)
            return Result.Fail("ACC-000", "Account not found.");

        account.PlaceOnHold(reason);
        _audit.Applied("acc.hold.place", "Account", accountId, reason);

        await _uow.SaveChangesAsync(ct);

        return Result.Ok();
    }

    public async Task<Result> ReleaseAsync(Guid accountId,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.LedgerPost);

        var account = await _accounts.GetAsync(accountId, ct);

        if (account is null)
            return Result.Fail("ACC-000", "Account not found.");

        account.ReleaseHold();
        _audit.Applied("acc.hold.release", "Account", accountId);

        await _uow.SaveChangesAsync(ct);

        return Result.Ok();
    }

    /// <remarks>
    /// No permission demand and no SaveChanges: this runs inside the
    /// sale's own transaction, and the cashier's right to be doing it
    /// was established when the sale was opened.
    /// </remarks>
    public async Task ChargeAsync(Account account, Sale sale, Money amount,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(sale);

        // Throws on hold or over the limit, and is meant to: the sale
        // must not complete if the charge cannot be made.
        account.Charge(amount);

        await _accounts.AddTransactionAsync(AccountTransaction.Record(
            account.Id, AccountEntryKind.Charge, amount, _clock.Today,
            sale.DocumentNumber, "Sale on account"), ct);
    }

    public async Task<Result<AccountDto>> TakePaymentAsync(Guid accountId,
        decimal amount, string tenderType, DateOnly receivedOn,
        string? reference, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        if (amount <= 0m)
            return Result<AccountDto>.Fail("ACC-005",
                "A payment must be for more than nothing.");

        if (!Enum.TryParse<TenderType>(tenderType, ignoreCase: true,
                out var tender)
            || !Enum.IsDefined(tender) || tender.IsDeferred()
            || tender == TenderType.Voucher)
            return Result<AccountDto>.Fail("ACC-006",
                "An account is paid in cash, EcoCash, by card or by EFT.");

        var account = await _accounts.GetAsync(accountId, ct);

        if (account is null)
            return Result<AccountDto>.Fail("ACC-000", "Account not found.");

        // Cash and EcoCash go into a till's drawer or wallet, so they are
        // taken where a session is open and its cash-up can expect them.
        // A card or EFT settles through the bank and may be taken from
        // the back office, but is still tied to the till where one is.
        var till = _user.TillSessionId;

        if (tender.IsCashLike() && till is null)
            return Result<AccountDto>.Fail("ACC-007",
                $"{tender.DisplayName()} is taken at a till with an open "
                + "session, so the cash-up expects it. Open a session on "
                + "this workstation, or take the payment at the till.");

        var paid = Money.Of(amount);

        // Overpayment is allowed and leaves the account in credit, which
        // is a real thing: somebody settles a round figure, or pays for
        // a script they then do not collect.
        account.Credit(paid);

        await _accounts.AddTransactionAsync(AccountTransaction.RecordPayment(
            account.Id, paid, receivedOn, tender,
            till,
            reference, $"Payment by {tender.DisplayName()}"), ct);

        // Money in, debtor down. The same entry a till takes, from the
        // other direction.
        await _journals.PostAccountPaymentAsync(account.AccountNumber, paid,
            tender.ToString(), receivedOn, reference, ct);

        _audit.Applied("acc.payment.take", "Account", accountId,
            string.Format(CultureInfo.CurrentCulture,
                "{0:0.00} by {1}. {2}", amount, tender.DisplayName(),
                reference));

        await _uow.SaveChangesAsync(ct);

        return Result<AccountDto>.Ok(Describe(account));
    }

    public async Task<Result<StatementDto>> StatementAsync(Guid accountId,
        DateOnly from, DateOnly until, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        var account = await _accounts.GetAsync(accountId, ct);

        if (account is null)
            return Result<StatementDto>.Fail("ACC-000",
                "Account not found.");

        var movements = await _accounts.TransactionsAsync(accountId, from,
            until, ct);

        // Everything before the period, so the statement opens where the
        // last one closed rather than at zero.
        var earlier = await _accounts.TransactionsAsync(accountId,
            DateOnly.MinValue, from.AddDays(-1), ct);

        var running = earlier.Aggregate(0m,
            (sum, t) => sum + t.Effect.Amount);

        var opening = running;
        var lines = new List<StatementLineDto>(movements.Count);

        foreach (var movement in movements)
        {
            running += movement.Effect.Amount;

            lines.Add(new StatementLineDto(
                movement.OccurredOn, movement.Kind, Describe(movement.Kind),
                movement.Reference, movement.Narration,
                movement.Increases ? movement.Amount.Amount : 0m,
                movement.Increases ? 0m : movement.Amount.Amount,
                running));
        }

        return Result<StatementDto>.Ok(new StatementDto(Describe(account),
            from, until, opening, lines, running));
    }

    private static string Describe(AccountEntryKind kind) => kind switch
    {
        AccountEntryKind.Charge => "Sale",
        AccountEntryKind.Payment => "Payment",
        AccountEntryKind.Adjustment => "Adjustment",
        _ => "Written off"
    };

    private static AccountDto Describe(Account a) =>
        new(a.Id, a.AccountNumber, a.Name, a.PatientId, a.Balance.Amount,
            a.CreditLimit.Amount, a.Available.Amount, a.IsOnHold,
            a.HoldReason, a.TermsDays);
}
