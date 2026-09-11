using PharmaX.Application.Tills;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Tills;

public sealed class TillSessionPresenter : PresenterBase<ITillSessionView>
{
    private readonly ITillSessionService _sessions;
    private readonly ICashUpService _cashUp;

    public TillSessionPresenter(ITillSessionService sessions,
                                ICashUpService cashUp)
    {
        _sessions = sessions;
        _cashUp = cashUp;
    }

    protected override void Subscribe()
    {
        View.OpenRequested         += (_, _) => _ = OpenAsync();
        View.XReadRequested        += (_, _) => _ = XReadAsync();
        View.DeclareRequested      += (_, _) => _ = DeclareAsync();
        View.CloseSessionRequested += (_, _) => _ = CloseAsync();
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
        View.ShowSession(await _sessions.CurrentAsync());
    });

    private Task DeclareAsync() => GuardAsync(async () =>
    {
        var declaration = await _cashUp.DeclareAsync(View.CountedDenominations);
        View.ShowVariance(declaration.Variances);
    });

    private Task CloseAsync() => GuardAsync(async () =>
    {
        var declaration = await _cashUp.DeclareAsync(View.CountedDenominations);

        string? reason = null;
        var worst = declaration.Variances
            .OrderByDescending(v => Math.Abs(v.Variance))
            .FirstOrDefault();

        // A reason is mandatory above the configured threshold (POS-023).
        if (worst is not null && worst.RequiresReason)
        {
            reason = View.PromptVarianceReason(worst.Variance);

            if (string.IsNullOrWhiteSpace(reason))
            {
                View.ShowError("A reason is required for this variance.");
                return;
            }
        }

        if (!View.Confirm("Close the session? This cannot be undone."))
            return;

        var result = await _cashUp.CloseAsync(reason);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowInfo("Session closed. The Z-read has printed.");
        View.CloseView(true);
    });
}
