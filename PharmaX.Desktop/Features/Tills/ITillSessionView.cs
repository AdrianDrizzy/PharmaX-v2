using PharmaX.Application.Tills.Dtos;

namespace PharmaX.Desktop.Features.Tills;

public interface ITillSessionView : Mvp.IView
{
    void ShowSession(TillSessionDto? session);
    void ShowExpected(IReadOnlyList<TenderExpectationDto> expectations);
    void ShowVariance(IReadOnlyList<VarianceLineDto> variances);
    void ShowDenominationGrid(IReadOnlyList<DenominationDto> denominations);

    decimal? PromptFloat();
    string? PromptVarianceReason(string tender, decimal variance);
    IReadOnlyDictionary<string, int> CountedDenominations { get; }

    /// What the EcoCash merchant statement shows was received this
    /// session, or null where nothing was entered.
    decimal? EcoCashStatement { get; }

    /// <summary>
    /// The money-out window for this session - the one being counted, or
    /// this till's own where null. True where anything was recorded.
    /// </summary>
    bool ShowPayouts(Guid? sessionId);

    /// <summary>
    /// The read as it will print, on screen. True where the operator chose
    /// to print it.
    /// </summary>
    bool PreviewSlip(Application.Sales.PrintableDocument document,
                     Application.Sales.ReceiptHeaderDto header);

    event EventHandler? OpenRequested;
    event EventHandler? XReadRequested;      // Ctrl+X
    event EventHandler? DeclareRequested;
    event EventHandler? CloseSessionRequested; // Ctrl+Z
    event EventHandler? PayoutsRequested;
}
