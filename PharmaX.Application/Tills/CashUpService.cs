using PharmaX.Application.Abstractions;
using PharmaX.Application.Finance;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Tills.Dtos;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Tills;

namespace PharmaX.Application.Tills;

public interface ICashUpService
{
    Task<IReadOnlyList<DenominationDto>> DenominationsAsync(
        CancellationToken ct = default);

    /// Interim read. The session stays open and the totals are not reset;
    /// only the Z-read at close does that, and only once.
    Task<XReadDto> XReadAsync(CancellationToken ct = default);

    Task<DeclarationDto> DeclareAsync(
        IReadOnlyDictionary<string, int> countedDenominations,
        CancellationToken ct = default);

    Task<Result> CloseAsync(string? varianceReason,
                            CancellationToken ct = default);
}

public sealed class CashUpService : ICashUpService
{
    /// Above this, closing the session needs a written reason (POS-023).
    private const decimal VarianceReasonThreshold = 20m;

    private readonly ITillSessionRepository _sessions;
    private readonly ISaleRepository _sales;
    private readonly IReturnRepository _returns;
    private readonly IJournalPoster _journals;
    private readonly ISessionContext _context;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public CashUpService(ITillSessionRepository sessions,
        ISaleRepository sales, IReturnRepository returns,
        IJournalPoster journals, ISessionContext context,
        IAuditWriter audit, IOutbox outbox, IUnitOfWork uow,
        ICurrentUser user, IClock clock)
    {
        _sessions = sessions;
        _sales = sales;
        _returns = returns;
        _journals = journals;
        _context = context;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _user = user;
        _clock = clock;
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
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(countedDenominations);

        var session = await RequireSessionAsync(ct);
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
            // Only cash is counted in the drawer. Every other tender is
            // reconciled against its own slip, so it declares as expected.
            var declared =
                expectation.TenderType == nameof(TenderType.Cash)
                    ? cashDeclared
                    : Money.Of(expectation.Expected);

            var expected = Money.Of(expectation.Expected);
            var variance = declared - expected;

            declaration.AddLine(
                Enum.Parse<TenderType>(expectation.TenderType), expected,
                declared);

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
        CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);

        var session = await _sessions.CurrentAsync(ct);
        if (session is null) return Result.Fail("POS-016", "No open session.");

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

        // A session closed without a count has nothing better to record
        // than the expectation, and says so through the reason.
        var counted = declaration?.TotalDeclared ?? expected;

        session.Close(_user.Id, counted, _clock.UtcNow,
            declaration is null
                ? varianceReason ?? "Closed without a cash declaration."
                : varianceReason);

        // The drawer against the books. Posts only where they differ.
        await _journals.PostCashUpAsync(session, expected, counted, ct);

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
        await _sessions.CurrentAsync(ct)
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

        var byTender = taken.Concat(refunded)
            .GroupBy(p => p.Tender)
            .Select(g => new TenderExpectationDto(
                g.Key.ToString(),
                g.Sum(p => p.Amount),
                g.Sum(p => p.Count)))
            .ToList();

        // The float is in the drawer whether or not anything sold, so cash
        // is always expected even when no cash sale was taken.
        var cash = byTender
            .FirstOrDefault(t => t.TenderType == nameof(TenderType.Cash));

        if (cash is null)
            byTender.Add(new TenderExpectationDto(
                nameof(TenderType.Cash), session.OpeningFloat.Amount, 0));
        else
            byTender[byTender.IndexOf(cash)] = cash with
            {
                Expected = cash.Expected + session.OpeningFloat.Amount
            };

        return byTender;
    }
}
