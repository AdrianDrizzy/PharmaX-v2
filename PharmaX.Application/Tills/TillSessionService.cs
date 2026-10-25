using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Tills.Dtos;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Tills;

namespace PharmaX.Application.Tills;

public interface ITillSessionService
{
    /// The open session for this workstation, or null. Null first thing in
    /// the morning is the normal state, not an error.
    Task<TillSessionDto?> CurrentAsync(CancellationToken ct = default);

    /// Any till's session, open or closed: one cashed up on somebody's
    /// behalf from the Cash Up screen. Attaches nothing to this till.
    Task<TillSessionDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<Result<TillSessionDto>> OpenAsync(decimal openingFloat,
                                           CancellationToken ct = default);

    Task<Result> SuspendAsync(CancellationToken ct = default);
    Task<Result> ResumeAsync(CancellationToken ct = default);

    /// The drawer opened without a sale, logged with the reason given.
    Task<Result> NoSaleAsync(string reason, CancellationToken ct = default);
}

public sealed class TillSessionService : ITillSessionService
{
    private readonly ITillSessionRepository _sessions;
    private readonly IUserRepository _users;
    private readonly ISessionContext _context;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly PharmaXOptions _options;

    public TillSessionService(ITillSessionRepository sessions,
        IUserRepository users, ISessionContext context, IAuditWriter audit,
        IOutbox outbox, IUnitOfWork uow, ICurrentUser user, IClock clock,
        PharmaXOptions options)
    {
        _sessions = sessions;
        _users = users;
        _context = context;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _user = user;
        _clock = clock;
        _options = options;
    }

    public async Task<TillSessionDto?> CurrentAsync(
        CancellationToken ct = default)
    {
        var session = await _sessions.CurrentAsync(ct);
        if (session is null) return null;

        // Re-attach on every read, so that a workstation restarted with a
        // session still open picks it up rather than refusing to sell.
        _context.AttachTillSession(session.Id);

        var openedBy = await _users.GetAsync(session.OpenedBy, ct);
        return TillSessionDto.From(session, openedBy?.DisplayName ?? "Unknown");
    }

    public async Task<TillSessionDto?> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        var session = await _sessions.GetAsync(id, ct);
        if (session is null || session.Status == TillSessionStatus.Closed) return null;

        var openedBy = await _users.GetAsync(session.OpenedBy, ct);
        return TillSessionDto.From(session, openedBy?.DisplayName ?? "Unknown");
    }

    public async Task<Result<TillSessionDto>> OpenAsync(decimal openingFloat,
        CancellationToken ct = default)
    {
        var existing = await _sessions.CurrentAsync(ct);

        if (existing is not null)
            return Result<TillSessionDto>.Fail("POS-014",
                "This till already has an open session. Close it first.");

        if (openingFloat < 0m)
            return Result<TillSessionDto>.Fail("POS-015",
                "An opening float cannot be negative.");

        var session = TillSession.Open(
            branchId: Guid.Parse(_options.Workstation.BranchId),
            workstationId: Guid.Parse(_options.Workstation.WorkstationId),
            tillNumber: _options.Workstation.TillNumber,
            openedBy: _user.Id,
            openingFloat: Money.Of(openingFloat),
            openedAtUtc: _clock.UtcNow);

        await _sessions.AddAsync(session, ct);

        _context.AttachTillSession(session.Id);
        _audit.Applied("pos.session.open", "TillSession", session.Id,
            $"Float {Money.Of(openingFloat)}");
        _outbox.Append("TillSession", session.Id,
            $"{{\"id\":\"{session.Id}\",\"float\":{openingFloat}}}");

        await _uow.SaveChangesAsync(ct);

        var openedBy = await _users.GetAsync(session.OpenedBy, ct);

        return Result<TillSessionDto>.Ok(
            TillSessionDto.From(session, openedBy?.DisplayName ?? "Unknown"));
    }

    public async Task<Result> SuspendAsync(CancellationToken ct = default)
    {
        var session = await _sessions.CurrentAsync(ct);
        if (session is null) return Result.Fail("POS-016", "No open session.");

        session.Suspend();
        _audit.Applied("pos.session.suspend", "TillSession", session.Id);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> ResumeAsync(CancellationToken ct = default)
    {
        var session = await _sessions.CurrentAsync(ct);
        if (session is null) return Result.Fail("POS-016", "No open session.");

        session.Resume();
        _context.AttachTillSession(session.Id);
        _audit.Applied("pos.session.resume", "TillSession", session.Id);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> NoSaleAsync(string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Fail("POS-040", "Say why the drawer is being opened.");

        var session = await _sessions.CurrentAsync(ct);
        if (session is null) return Result.Fail("POS-016", "No open session.");

        _audit.Applied("pos.drawer.nosale", "TillSession", session.Id, reason.Trim());

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }
}
