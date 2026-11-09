using PharmaX.Application.Abstractions;
using PharmaX.Application.Finance;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Application.Tills.Dtos;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;
using PharmaX.Domain.Tills;

namespace PharmaX.Application.Tills;

public interface ICashUpService
{
    Task<IReadOnlyList<DenominationDto>> DenominationsAsync(
        CancellationToken ct = default);

    /// Interim read. The session stays open and the totals are not reset;
    /// only the Z-read at close does that, and only once.
    Task<XReadDto> XReadAsync(CancellationToken ct = default);

    /// <param name="countedDenominations">The drawer, note by note.</param>
    /// <param name="statements">
    /// What each tender declared off a statement - EcoCash, card - shows
    /// was received. A tender missing here is declared as nothing received.
    /// </param>
    /// <param name="sessionId">
    /// Another till's session, counted on somebody's behalf; null for
    /// this workstation's own.
    /// </param>
    Task<DeclarationDto> DeclareAsync(
        IReadOnlyDictionary<string, int> countedDenominations,
        IReadOnlyDictionary<TenderType, decimal>? statements = null,
        Guid? sessionId = null,
        CancellationToken ct = default);

    Task<Result> CloseAsync(string? varianceReason, Guid? sessionId = null,
                            CancellationToken ct = default);
}

public sealed class CashUpService : ICashUpService
{
    /// Above this, closing the session needs a written reason (POS-023).
    private const decimal VarianceReasonThreshold = 20m;

    private readonly ITillSessionRepository _sessions;
    private readonly ISaleRepository _sales;
    private readonly IReturnRepository _returns;
    private readonly IAccountRepository _accounts;
    private readonly IJournalPoster _journals;
    private readonly ISessionContext _context;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IPermissionService _permissions;

    public CashUpService(ITillSessionRepository sessions,
        ISaleRepository sales, IReturnRepository returns,
        IAccountRepository accounts,
        IJournalPoster journals, ISessionContext context,
        IAuditWriter audit, IOutbox outbox, IUnitOfWork uow,
        ICurrentUser user, IClock clock, IPermissionService permissions)
    {
        _sessions = sessions;
        _sales = sales;
        _returns = returns;
        _accounts = accounts;
        _journals = journals;
        _context = context;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _user = user;
        _clock = clock;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<DenominationDto>> DenominationsAsync(
        CancellationToken ct = default)
    {
        var denominations = await _sessions.DenominationsAsync(ct: ct);

        return denominations
            .OrderBy(d => d.SortOrder)
            .Select(DenominationDto.From)
            .ToList();
    }

    public async Task<XReadDto> XReadAsync(CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct);

        session.RecordXRead();
        _audit.Applied("pos.session.xread", "TillSession", session.Id);
        await _uow.SaveChangesAsync(ct);

        var expectations = await ExpectationsAsync(session, ct);

        return new XReadDto(session.Id, _clock.UtcNow, session.XReadCount,
            expectations, expectations.Sum(e => e.Expected));
    }

    public async Task<DeclarationDto> DeclareAsync(
        IReadOnlyDictionary<string, int> countedDenominations,
        IReadOnlyDictionary<TenderType, decimal>? statements = null,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(countedDenominations);

        if (statements?.Values.Any(v => v < 0m) == true)
            throw new ArgumentException(
                "A statement cannot show less than nothing received.",
                nameof(statements));

        var session = await RequireSessionAsync(sessionId, ct);
        var expectations = await ExpectationsAsync(session, ct);

        // The grid sent counts, not a total. Recomputing here rather than
        // trusting a displayed figure is the whole point: a total that has
        // drifted on screen must never become the declared amount.
        var denominations = await _sessions.DenominationsAsync(ct: ct);
        var cashDeclared = Money.Zero();

        var declaration = TillDeclaration.Create(
            session.Id, _user.Id, _clock.UtcNow, isFinal: false);

        foreach (var (key, count) in countedDenominations)
        {
            var denomination = denominations
                .FirstOrDefault(d => d.Key.Equals(key, StringComparison.Ordinal));

            if (denomination is null || count <= 0) continue;

            var extended = denomination.Extended(count);
            cashDeclared += extended;
            declaration.CountDenomination(key, count, extended);
        }

        var variances = new List<VarianceLineDto>();

        foreach (var expectation in expectations)
        {
            var tender = Enum.Parse<TenderType>(expectation.TenderType);
            var expected = Money.Of(expectation.Expected);

            // Cash is counted in the drawer. EcoCash and card are declared
            // off what their machines say was collected: both can come up
            // short of what the till rang up. EFT settles through the bank
            // and declares as expected.
            var declared = tender.AffectsDrawer()
                ? cashDeclared
                : tender.IsDeclaredAgainstStatement()
                    ? Money.Of(statements?.GetValueOrDefault(tender) ?? 0m)
                    : expected;

            var variance = declared - expected;

            declaration.AddLine(tender, expected, declared);

            variances.Add(new VarianceLineDto(
                expectation.TenderType, expected.Amount, declared.Amount,
                variance.Amount,
                Math.Abs(variance.Amount) > VarianceReasonThreshold));
        }

        await _sessions.AddDeclarationAsync(declaration, ct);
        await _uow.SaveChangesAsync(ct);

        return new DeclarationDto(session.Id,
            declaration.TotalDeclared.Amount,
            declaration.TotalVariance.Amount,
            variances);
    }

    public async Task<Result> CloseAsync(string? varianceReason,
        Guid? sessionId = null, CancellationToken ct = default)
    {
        // A cashier closes their own till; the pharmacist, the assistant,
        // the manager and the administrator close one for a cashier.
        var forOthers = _permissions.Has(PermissionCode.SessionCloseOtherUser);

        if (!forOthers && !_permissions.Has(PermissionCode.SessionCashUp))
            return Result.Fail("POS-027",
                "Only a cashier, a pharmacist, an assistant or an "
                + "administrator can cash up and close this till.");

        await using var tx = await _uow.BeginTransactionAsync(ct);

        var session = sessionId is Guid id
            ? await _sessions.GetAsync(id, ct)
            : await _sessions.CurrentAsync(ct);
        if (session is null || session.Status == TillSessionStatus.Closed)
            return Result.Fail("POS-016", "No open session.");

        // Named by id - from the Cash Up screen, perhaps on another
        // computer - a cashier may close only a shift they opened, or the
        // one open on the till they are signed in at: the cashiers take
        // turns at one till, and whoever is on it at the end cashes it up.
        if (sessionId is not null && session.OpenedBy != _user.Id && !forOthers
            && _user.TillSessionId != session.Id)
            return Result.Fail("POS-027",
                "Somebody else's till is closed by a pharmacist, an assistant "
                + "or the administrator.");

        var expectations = await ExpectationsAsync(session, ct);
        var expected = Money.Of(expectations.Sum(e => e.Expected));

        // What the operator counted, not what the sales say they should
        // have. Closing on the expected figure was recording the
        // system's own opinion as the declaration, so every session
        // closed with a variance of exactly zero and a short till looked
        // like a balanced one - which is the single thing a cash-up
        // exists to catch.
        var declaration = await _sessions.LatestDeclarationAsync(
            session.Id, ct);

        // Tender by tender, because a short drawer, a short EcoCash
        // statement and a short card total come off different ledger
        // accounts. A tender that was not declared - or took money after
        // the declaration - has nothing better to record than what was
        // expected of it.
        var lines = expectations
            .Select(e =>
            {
                var tender = Enum.Parse<TenderType>(e.TenderType);
                var line = declaration?.Lines
                    .FirstOrDefault(l => l.Tender == tender);

                var declared = line is not null
                               && (tender.AffectsDrawer()
                                   || tender.IsDeclaredAgainstStatement())
                    ? line.Declared
                    : Money.Of(e.Expected);

                return (Tender: tender, Expected: Money.Of(e.Expected),
                        Declared: declared);
            })
            .ToList();

        // A session closed without a count has nothing better to record
        // than the expectation, and says so through the reason.
        var counted = declaration is null
            ? expected
            : lines.Aggregate(Money.Zero(), (sum, l) => sum + l.Declared);

        session.Close(_user.Id, counted, _clock.UtcNow,
            declaration is null
                ? varianceReason ?? "Closed without a cash declaration."
                : varianceReason);

        // The drawer, the EcoCash wallet and the card total against the
        // books. Posts only where they differ.
        foreach (var line in lines.Where(l => l.Declared != l.Expected))
            await _journals.PostCashUpAsync(session, line.Tender,
                line.Expected, line.Declared, ct);

        // Only this till lets go of its session; closing another till's
        // leaves this one trading.
        if (sessionId is null || _user.TillSessionId == session.Id)
            _context.AttachTillSession(null);

        _audit.Applied("pos.session.close", "TillSession", session.Id,
            $"counted {counted}, expected {expected}. {varianceReason}");

        _outbox.Append("TillSessionClose", session.Id,
            $"{{\"id\":\"{session.Id}\",\"declared\":{counted.Amount},"
            + $"\"expected\":{expected.Amount}}}");

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        return Result.Ok();
    }

    private async Task<TillSession> RequireSessionAsync(CancellationToken ct) =>
        await RequireSessionAsync(null, ct);

    private async Task<TillSession> RequireSessionAsync(Guid? sessionId,
                                                        CancellationToken ct) =>
        (sessionId is Guid id
            ? await _sessions.GetAsync(id, ct)
            : await _sessions.CurrentAsync(ct))
        ?? throw new InvalidOperationException(
            "There is no open till session on this workstation.");

    private async Task<IReadOnlyList<TenderExpectationDto>> ExpectationsAsync(
        TillSession session, CancellationToken ct)
    {
        var sales = await _sales.ForSessionAsync(session.Id, ct);
        var returns = await _returns.ForSessionAsync(session.Id, ct);

        var taken = sales
            .Where(s => s.Status == SaleStatus.Finalised)
            .SelectMany(s => s.Payments)
            .Select(p => (p.Tender, Amount: p.Amount.Amount, Count: 1));

        // A refund is money that left the drawer, so it subtracts. A
        // till that counts refunds as takings never balances, and the
        // operator gets blamed for the difference.
        var refunded = returns
            .Where(r => r.Status == ReturnStatus.Finalised)
            .SelectMany(r => r.Refunds)
            .Select(p => (p.Tender, Amount: -p.Amount.Amount, Count: 1));

        // Money off a patient's account taken at this till is in its
        // drawer or wallet like any sale. Left out, every account paid
        // in cash showed as the drawer being over by exactly that much
        // (ADM-DEF-18).
        var accountPayments = (await _accounts.PaymentsForSessionAsync(
                session.Id, ct))
            .Where(t => t.Tender is not null)
            .Select(t => (Tender: t.Tender!.Value, Amount: t.Amount.Amount,
                          Count: 1));

        var byTender = taken.Concat(refunded).Concat(accountPayments)
            .GroupBy(p => p.Tender)
            .Select(g => new TenderExpectationDto(
                g.Key.ToString(),
                g.Sum(p => p.Amount),
                g.Sum(p => p.Count)))
            .ToList();

        // Money taken out of the drawer during the shift - drawings, lunch,
        // other expenses - is not in it to count, so it is not expected.
        var paidOut = (await _sessions.PayoutsAsync(session.Id, ct))
            .Where(p => !p.IsVoided)
            .Sum(p => p.Amount.Amount);

        // The float is in the drawer whether or not anything sold, so cash
        // is always expected even when no cash sale was taken.
        var cash = byTender
            .FirstOrDefault(t => t.TenderType == nameof(TenderType.Cash));

        if (cash is null)
            byTender.Add(new TenderExpectationDto(
                nameof(TenderType.Cash), session.OpeningFloat.Amount - paidOut, 0));
        else
            byTender[byTender.IndexOf(cash)] = cash with
            {
                Expected = cash.Expected + session.OpeningFloat.Amount - paidOut
            };

        return byTender;
    }
}
