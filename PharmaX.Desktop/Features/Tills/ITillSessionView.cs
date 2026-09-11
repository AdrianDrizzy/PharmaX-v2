using PharmaX.Application.Tills.Dtos;

namespace PharmaX.Desktop.Features.Tills;

public interface ITillSessionView : Mvp.IView
{
    void ShowSession(TillSessionDto? session);
    void ShowExpected(IReadOnlyList<TenderExpectationDto> expectations);
    void ShowVariance(IReadOnlyList<VarianceLineDto> variances);
    void ShowDenominationGrid(IReadOnlyList<DenominationDto> denominations);

    decimal? PromptFloat();
    string? PromptVarianceReason(decimal variance);
    IReadOnlyDictionary<string, int> CountedDenominations { get; }

    event EventHandler? OpenRequested;
    event EventHandler? XReadRequested;      // Ctrl+X
    event EventHandler? DeclareRequested;
    event EventHandler? CloseSessionRequested; // Ctrl+Z
}
