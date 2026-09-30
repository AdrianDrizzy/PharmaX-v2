using PharmaX.Application.Sales;
using PharmaX.Application.Tills;
using PharmaX.Desktop.Features.Sales;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Sales;

namespace PharmaX.Desktop.Features.Tills;

public sealed class TillSessionPresenter : PresenterBase<ITillSessionView>
{
    private readonly ITillSessionService _sessions;
    private readonly ICashUpService _cashUp;
    private readonly IDocumentPrinter _printer;
    private readonly ReceiptHeaderFactory _headers;

    public TillSessionPresenter(ITillSessionService sessions,
                                ICashUpService cashUp,
                                IDocumentPrinter printer,
                                ReceiptHeaderFactory headers)
    {
        _sessions = sessions;
        _cashUp = cashUp;
        _printer = printer;
        _headers = headers;
    }

    protected override void Subscribe()
    {
        View.OpenRequested         += (_, _) => Enqueue(OpenAsync);
        View.XReadRequested        += (_, _) => Enqueue(XReadAsync);
        View.DeclareRequested      += (_, _) => Enqueue(DeclareAsync);
        View.CloseSessionRequested += (_, _) => Enqueue(CloseAsync);
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        var session = await _sessions.CurrentAsync();
        View.ShowSession(session);
        View.ShowDenominationGrid(await _cashUp.DenominationsAsync());
    });

    private Task OpenAsync() => GuardAsync(async () =>
    {
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
        // Interim read: the session stays open and the read is logged.
        var read = await _cashUp.XReadAsync();
        View.ShowExpected(read.Expectations);

        // The header counts the reads taken, and taking one has just
        // changed that count.
        var session = await _sessions.CurrentAsync();
        View.ShowSession(session);

        if (session is null) return;

        // Printed, and the screen says whether it really did.
        var printed = await _printer.PrintAsync(
            TillReads.XRead(session, read), _headers.Build());

        if (printed.Printed)
            View.ShowInfo(printed.Describe($"X-read {read.ReadNumber}"));
        else
            View.ShowError(printed.Describe($"X-read {read.ReadNumber}"));
    });

    private Task DeclareAsync() => GuardAsync(async () =>
    {
        var declaration = await _cashUp.DeclareAsync(
            View.CountedDenominations, Statements());

        View.ShowVariance(declaration.Variances);
    });

    /// <summary>
    /// The tenders declared off a statement rather than counted, as the
    /// operator entered them.
    /// </summary>
    /// <remarks>
    /// Left out where nothing was entered, and the service then declares
    /// EcoCash as nothing received: a blank is not a statement, and
    /// taking the till's own figure for it would balance every session
    /// whether or not the money arrived.
    /// </remarks>
    private Dictionary<TenderType, decimal> Statements() =>
        View.EcoCashStatement is decimal ecoCash
            ? new Dictionary<TenderType, decimal> { [TenderType.EcoCash] = ecoCash }
            : new Dictionary<TenderType, decimal>();

    private Task CloseAsync() => GuardAsync(async () =>
    {
        var declaration = await _cashUp.DeclareAsync(
            View.CountedDenominations, Statements());

        string? reason = null;
        var worst = declaration.Variances
            .OrderByDescending(v => Math.Abs(v.Variance))
            .FirstOrDefault();

        // A reason is mandatory above the configured threshold (POS-023).
        if (worst is not null && worst.RequiresReason)
        {
            reason = View.PromptVarianceReason(worst.TenderType,
                                               worst.Variance);

            if (string.IsNullOrWhiteSpace(reason))
            {
                View.ShowError("A reason is required for this variance.");
                return;
            }
        }

        if (!View.Confirm("Close the session? This cannot be undone."))
            return;

        var session = await _sessions.CurrentAsync();
        var result = await _cashUp.CloseAsync(reason);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        // The Z-read is printed after the close has committed, and the
        // message says what actually happened to it. It used to say "The
        // Z-read has printed" when nothing had (DEF-0018).
        var message = "Session closed.";

        if (session is not null)
        {
            var printed = await _printer.PrintAsync(
                TillReads.ZRead(session, declaration, reason, DateTime.Now),
                _headers.Build());

            message += " " + printed.Describe("The Z-read");
        }

        View.ShowInfo(message);
        View.CloseView(true);
    });
}
