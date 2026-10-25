using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Finance;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Application.Tills.Dtos;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;
using PharmaX.Domain.Tills;

namespace PharmaX.Application.Tills;

/// <param name="Note">What it was for: "sandwiches for three", "owner".</param>
public sealed record PayoutEditDto(PayoutKind Kind, decimal Amount, string Note);

public interface ITillPayoutService
{
    /// <summary>
    /// The money taken out of a shift's drawer, voided entries included.
    /// Null is this till's own open shift.
    /// </summary>
    Task<IReadOnlyList<PayoutDto>> ListAsync(Guid? sessionId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Records money taken out of a shift's drawer - drawings, lunch,
    /// another expense - so the cash-up expects that much less. Null is
    /// this till's own open shift.
    /// </summary>
    Task<Result<PayoutDto>> RecordAsync(PayoutEditDto edit, Guid? sessionId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Takes back one recorded by mistake, while its till is open. It stays
    /// on the record, voided, with the reason.
    /// </summary>
    Task<Result> VoidAsync(Guid payoutId, string reason,
        CancellationToken ct = default);
}

/// <summary>
/// Money out of the drawer during a shift: the owner's drawings, lunch,
/// and the other small things a pharmacy pays for in cash from the till.
/// </summary>
/// <remarks>
/// Without it the drawer was counted short by every payout and the
/// cashier explained the variance in words, so the books never said where
/// the money went (23 October 2026). Every role with a till records one;
/// on somebody else's till, the same people who cash it up for them.
/// </remarks>
public sealed class TillPayoutService : ITillPayoutService
{
    private readonly ITillSessionRepository _sessions;
    private readonly ISaleRepository _sales;
    private readonly IReturnRepository _returns;
    private readonly IAccountRepository _accounts;
    private readonly IUserRepository _users;
    private readonly IJournalPoster _journals;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IPermissionService _permissions;

    public TillPayoutService(ITillSessionRepository sessions, ISaleRepository sales,
        IReturnRepository returns, IAccountRepository accounts, IUserRepository users,
        IJournalPoster journals, IAuditWriter audit, IUnitOfWork uow,
        ICurrentUser user, IClock clock, IPermissionService permissions)
    {
        _sessions = sessions;
        _sales = sales;
        _returns = returns;
        _accounts = accounts;
        _users = users;
        _journals = journals;
        _audit = audit;
        _uow = uow;
        _user = user;
        _clock = clock;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<PayoutDto>> ListAsync(Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var session = await SessionAsync(sessionId, ct);
        if (session is null) return Array.Empty<PayoutDto>();

        if (!MayActOn(session))
            throw new PermissionDeniedException(
                "You can see the money out of your own till only.");

        return await DtosAsync(await _sessions.PayoutsAsync(session.Id, ct), ct);
    }

    public async Task<Result<PayoutDto>> RecordAsync(PayoutEditDto edit,
        Guid? sessionId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.SessionPayOut);

        if (!Enum.IsDefined(edit.Kind))
            return Fail("POS-031", "Say what the money was taken out for: drawings, "
                                   + "lunch or another expense.");

        if (edit.Amount <= 0m)
            return Fail("POS-031", "Enter how much was taken out of the drawer.");

        if (string.IsNullOrWhiteSpace(edit.Note))
            return Fail("POS-031", "Say what the money was for, so the cash-up can "
                                   + "show where it went.");

        await using var tx = await _uow.BeginTransactionAsync(ct);

        var session = await SessionAsync(sessionId, ct);

        if (session is null || session.Status == TillSessionStatus.Closed)
            return Fail("POS-016", "No till is open. Money out of the drawer is "
                                   + "recorded while the till is open, before it is "
                                   + "cashed up.");

        if (!MayActOn(session))
            return Fail("POS-027", "Money out of somebody else's till is recorded by "
                                   + "a pharmacist, an assistant, the manager or the "
                                   + "administrator.");

        // A drawer cannot give out more than it holds: what is asked for
        // beyond that is a mistyped amount - 500 for 5.00 - not money.
        var amount = Money.Of(edit.Amount);
        var holds = await DrawerAsync(session, ct);

        if (amount.Amount > holds)
            return Fail("POS-032", string.Format(CultureInfo.CurrentCulture,
                "The drawer should hold {0:N2}, so {1:N2} cannot have come out of it. "
                + "Check the amount.", holds, amount.Amount));

        var payout = TillPayout.Record(session.Id, edit.Kind, amount, edit.Note,
            _user.Id, _clock.UtcNow);

        await _sessions.AddPayoutAsync(payout, ct);
        await _journals.PostPayoutAsync(session, payout, ct: ct);

        _audit.Applied("pos.session.pay-out", "TillSession", session.Id,
            Describe(payout));

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        return Result<PayoutDto>.Ok(PayoutDto.From(payout, await NameAsync(_user.Id, ct)));
    }

    public async Task<Result> VoidAsync(Guid payoutId, string reason,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SessionPayOut);

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Fail("POS-031", "Say why it is being voided.");

        await using var tx = await _uow.BeginTransactionAsync(ct);

        var payout = await _sessions.GetPayoutAsync(payoutId, ct);
        if (payout is null) return Result.Fail("POS-033", "That entry was not found.");

        if (payout.IsVoided) return Result.Fail("POS-033", "That entry is already voided.");

        var session = await _sessions.GetAsync(payout.TillSessionId, ct);

        if (session is null || session.Status == TillSessionStatus.Closed)
            return Result.Fail("POS-016", "That till has been cashed up and closed, so "
                                          + "what came out of it stands as counted.");

        if (!MayActOn(session))
            return Result.Fail("POS-027", "Money out of somebody else's till is voided "
                                          + "by a pharmacist, an assistant, the manager "
                                          + "or the administrator.");

        payout.Void(_user.Id, _clock.UtcNow, reason);
        await _journals.PostPayoutAsync(session, payout, voided: true, ct);

        _audit.Applied("pos.session.pay-out.void", "TillSession", session.Id,
            $"{Describe(payout)} voided: {reason.Trim()}");

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        return Result.Ok();
    }

    /// <summary>
    /// This till's own shift, or the one named: the one in the drawer the
    /// operator is standing at, or one chosen on the cash-up screen.
    /// </summary>
    private async Task<TillSession?> SessionAsync(Guid? sessionId, CancellationToken ct) =>
        sessionId is Guid id ? await _sessions.GetAsync(id, ct) : await _sessions.CurrentAsync(ct);

    /// Their own shift, this till's, or anyone's for a supervisor.
    private bool MayActOn(TillSession session) =>
        session.OpenedBy == _user.Id
        || _user.TillSessionId == session.Id
        || ShiftCashUpService.MayActForOthersOf(_permissions);

    /// What the drawer should hold now: the float, the cash taken net of
    /// refunds, less what has already come out.
    private async Task<decimal> DrawerAsync(TillSession session, CancellationToken ct)
    {
        var shift = ShiftTally.Build(session,
            await _sales.ForSessionAsync(session.Id, ct),
            await _returns.ForSessionAsync(session.Id, ct),
            await _accounts.PaymentsForSessionAsync(session.Id, ct),
            new Dictionary<Guid, string>(), new Dictionary<Guid, string>(),
            await _sessions.PayoutsAsync(session.Id, ct));

        return shift.ExpectedCash;
    }

    private async Task<IReadOnlyList<PayoutDto>> DtosAsync(IReadOnlyList<TillPayout> payouts,
        CancellationToken ct)
    {
        var list = new List<PayoutDto>();

        foreach (var p in payouts)
            list.Add(PayoutDto.From(p, await NameAsync(p.PaidBy, ct)));

        return list;
    }

    private async Task<string> NameAsync(Guid id, CancellationToken ct) =>
        (await _users.GetAsync(id, ct))?.DisplayName ?? "Unknown";

    private static string Describe(TillPayout p) =>
        string.Format(CultureInfo.CurrentCulture, "{0} {1:N2}: {2}",
            p.Kind.DisplayName(), p.Amount.Amount, p.Note);

    private static Result<PayoutDto> Fail(string code, string message) =>
        Result<PayoutDto>.Fail(code, message);
}
