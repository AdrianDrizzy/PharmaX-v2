using PharmaX.Application.Sales;
using PharmaX.Application.Security;
using PharmaX.Application.Tills;
using PharmaX.Desktop.Features.Sales;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;

namespace PharmaX.Desktop.Features.Tills;

public sealed class TillSessionPresenter : PresenterBase<ITillSessionView>
{
    private readonly ITillSessionService _sessions;
    private readonly ICashUpService _cashUp;
    private readonly IDocumentPrinter _printer;
    private readonly ReceiptHeaderFactory _headers;
    private readonly IPermissionService _permissions;
    private readonly ITillPayoutService _payouts;

    public TillSessionPresenter(ITillSessionService sessions,
                                ICashUpService cashUp,
                                IDocumentPrinter printer,
                                ReceiptHeaderFactory headers,
                                IPermissionService permissions,
                                ITillPayoutService payouts)
    {
        _sessions = sessions;
        _cashUp = cashUp;
        _printer = printer;
        _headers = headers;
        _permissions = permissions;
        _payouts = payouts;
    }

    protected override void Subscribe()
    {
        View.OpenRequested         += (_, _) => Enqueue(OpenAsync);
        View.XReadRequested        += (_, _) => Enqueue(XReadAsync);
        View.DeclareRequested      += (_, _) => Enqueue(DeclareAsync);
        View.CloseSessionRequested += (_, _) => Enqueue(CloseAsync);
        View.PayoutsRequested      += (_, _) => Enqueue(PayoutsAsync);
    }

    /// <summary>
    /// Money out of the drawer - drawings, lunch, other expenses - before
    /// it is counted. A variance already shown was worked out without it,
    /// so it is worked out again.
    /// </summary>
    private Task PayoutsAsync() => GuardAsync(() =>
    {
        if (View.ShowPayouts(TargetSessionId))
        {
            View.ShowVariance(Array.Empty<Application.Tills.Dtos.VarianceLineDto>());
            View.ShowInfo("The cash expected now leaves out the money paid out. Declare "
                          + "again to see the variance.");
        }

        return Task.CompletedTask;
    });

    /// <summary>
    /// Another till's session, counted and closed on the cashier's behalf
    /// from the Cash Up screen. Null for this till's own.
    /// </summary>
    public Guid? TargetSessionId { get; set; }

    private Task<Application.Tills.Dtos.TillSessionDto?> SessionAsync() =>
        TargetSessionId is Guid id ? _sessions.GetAsync(id) : _sessions.CurrentAsync();

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        var session = await SessionAsync();
        View.ShowSession(session);
        View.ShowDenominationGrid(await _cashUp.DenominationsAsync());
    });

    private Task OpenAsync() => GuardAsync(async () =>
    {
        if (TargetSessionId is not null) return;

        var declared = View.PromptFloat();
        if (declared is null) return;

        var result = await _sessions.OpenAsync(declared.Value);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowSession(result.Value);
    });

    private Task XReadAsync() => GuardAsync(async () =>
    {
        // Another till's figures are on the Cash Up screen it came from.
        if (TargetSessionId is not null)
        {
            View.ShowInfo("An X-read is taken at the till itself. The Cash Up "
                          + "screen shows and prints this till's figures.");
            return;
        }

        // Interim read: the session stays open and the read is logged.
        var read = await _cashUp.XReadAsync();
        View.ShowExpected(read.Expectations);

        // The header counts the reads taken, and taking one has just
        // changed that count.
        var session = await _sessions.CurrentAsync();
        View.ShowSession(session);

        if (session is null) return;

        // Seen on screen as it will print, then printed if wanted, and the
        // screen says whether it really did.
        var header = _headers.Build();
        var document = TillReads.XRead(session, read, await _payouts.ListAsync());

        if (!View.PreviewSlip(document, header)) return;

        var printed = await _printer.PrintAsync(document, header);

        if (printed.Printed)
            View.ShowInfo(printed.Describe($"X-read {read.ReadNumber}"));
        else
            View.ShowError(printed.Describe($"X-read {read.ReadNumber}"));
    });

    private Task DeclareAsync() => GuardAsync(async () =>
    {
        if (!MayCashUp()) return;

        if (Statements() is not { } statements) return;

        var declaration = await _cashUp.DeclareAsync(
            View.CountedDenominations, statements, TargetSessionId);

        View.ShowVariance(declaration.Variances);
    });

    /// <summary>
    /// The tenders declared off a statement rather than counted, as the
    /// operator entered them.
    /// </summary>
    /// <remarks>
    /// Left out where nothing was entered, and the service then declares
    /// that tender as nothing received: a blank is not a statement, and
    /// taking the till's own figure for it would balance every session
    /// whether or not the money arrived. Null, with the operator told,
    /// for an amount below zero: the service refuses one, and that
    /// refusal reached the operator as "something went wrong".
    /// </remarks>
    private Dictionary<TenderType, decimal>? Statements()
    {
        var statements = new Dictionary<TenderType, decimal>();

        if (!Taken("The EcoCash statement", View.EcoCashStatement, out var ecoCash))
            return null;
        if (ecoCash is decimal eco) statements[TenderType.EcoCash] = eco;

        if (!Taken("The card amount", View.CardCollected, out var card))
            return null;
        if (card is decimal collected) statements[TenderType.Card] = collected;

        return statements;
    }

    private bool Taken(string what, decimal? amount, out decimal? value)
    {
        value = amount;

        if (amount is not < 0m) return true;

        View.ShowError($"{what} cannot be less than zero. Enter what was "
                       + "collected, or leave it blank if nothing came in.");
        return false;
    }

    private Task CloseAsync() => GuardAsync(async () =>
    {
        if (!MayCashUp()) return;

        if (Statements() is not { } statements) return;

        var declaration = await _cashUp.DeclareAsync(
            View.CountedDenominations, statements, TargetSessionId);

        string? reason = null;
        var worst = declaration.Variances
            .OrderByDescending(v => Math.Abs(v.Variance))
            .FirstOrDefault();

        // A reason is mandatory above the configured threshold (POS-023).
        if (worst is not null && worst.RequiresReason)
        {
            reason = View.PromptVarianceReason(worst.TenderType,
                                               worst.Variance);

            // Cancelled: back to the count, nothing said.
            if (reason is null) return;

            if (string.IsNullOrWhiteSpace(reason))
            {
                View.ShowError("A reason is required for this variance.");
                return;
            }
        }

        if (!View.Confirm("Close the session? This cannot be undone."))
            return;

        var session = await SessionAsync();
        var payouts = session is null
            ? Array.Empty<Application.Tills.Dtos.PayoutDto>()
            : await _payouts.ListAsync(session.Id);
        var result = await _cashUp.CloseAsync(reason, TargetSessionId);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        // The Z-read is printed after the close has committed, and the
        // message says what actually happened to it. It used to say "The
        // Z-read has printed" when nothing had (DEF-0018). It is seen on
        // screen first, as it will print.
        var message = "Session closed.";

        if (session is not null)
        {
            var header = _headers.Build();
            var document = TillReads.ZRead(session, declaration, reason, DateTime.Now,
                                           payouts);

            message += " " + (View.PreviewSlip(document, header)
                ? (await _printer.PrintAsync(document, header)).Describe("The Z-read")
                : "The Z-read was not printed; Cash Up still prints this till's slip.");
        }

        View.ShowInfo(message);
        View.CloseView(true);
    });

    /// Counting is the start of the close, so it stops here too. The
    /// pharmacist, the assistant and the administrator count and close
    /// for a cashier.
    private bool MayCashUp()
    {
        if (_permissions.Has(PermissionCode.SessionCashUp)
            || _permissions.Has(PermissionCode.SessionCloseOtherUser)) return true;

        View.ShowError("Only a cashier, a pharmacist, an assistant or an "
                       + "administrator can cash up and close this till.");
        return false;
    }
}
