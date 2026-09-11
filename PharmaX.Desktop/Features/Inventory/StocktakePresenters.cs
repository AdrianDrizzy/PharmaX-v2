using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Inventory;

public interface IStocktakeListView : IView
{
    Guid? SelectedId { get; }

    void ShowStocktakes(IReadOnlyList<StocktakeListItemDto> stocktakes);
    StocktakeScopeDto? PromptScope();
    string? PromptCancelReason();

    event EventHandler? RefreshRequested;
    event EventHandler? OpenNewRequested;
    event EventHandler? ContinueRequested;
    event EventHandler? CancelStocktakeRequested;
}

public interface IStocktakeCaptureView : IView
{
    Guid StocktakeId { get; }
    int? SelectedLineNo { get; }

    void ShowStocktake(StocktakeDto stocktake);
    void ShowVarianceOnly(bool varianceOnly);

    decimal? PromptCount(string description, decimal expected,
                         decimal? current);

    string? PromptVarianceReason(decimal netVariance);

    event EventHandler? CountRequested;
    event EventHandler? ClearCountRequested;
    event EventHandler? VarianceFilterToggled;
    event EventHandler? PostRequested;
    event EventHandler? CloseRequested;
}

public sealed class StocktakeListPresenter
    : PresenterBase<IStocktakeListView>
{
    private readonly IStocktakeService _stocktakes;
    private readonly ViewFactory _views;

    public StocktakeListPresenter(IStocktakeService stocktakes,
                                  ViewFactory views)
    {
        _stocktakes = stocktakes;
        _views = views;
    }

    protected override void Subscribe()
    {
        View.RefreshRequested += (_, _) => _ = LoadAsync();
        View.OpenNewRequested += (_, _) => _ = OpenNewAsync();
        View.ContinueRequested += (_, _) => _ = ContinueAsync(View.SelectedId);
        View.CancelStocktakeRequested += (_, _) => _ = CancelAsync();
    }

    public override Task InitialiseAsync() => LoadAsync();

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _stocktakes.RecentAsync();

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowStocktakes(result.Value);
    });

    private Task OpenNewAsync() => GuardAsync(async () =>
    {
        if (View.PromptScope() is not StocktakeScopeDto scope) return;

        var result = await _stocktakes.OpenAsync(scope);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowInfo($"Stocktake {result.Value.StocktakeNumber} opened "
                      + $"with {result.Value.Lines.Count} line(s). What "
                      + "the system believes now is frozen on those lines, "
                      + "so trading can carry on while you count.");

        await ContinueAsync(result.Value.Id);
        await LoadAsync();
    });

    private Task ContinueAsync(Guid? id) => GuardAsync(async () =>
    {
        if (id is not Guid stocktakeId)
        {
            View.ShowInfo("Choose a stocktake first.");
            return;
        }

        using var capture = _views.Create<frmStocktakeCapture>();
        capture.LoadStocktake(stocktakeId);
        capture.ShowDialog((Form)View);

        await LoadAsync();
    });

    private Task CancelAsync() => GuardAsync(async () =>
    {
        if (View.SelectedId is not Guid id)
        {
            View.ShowInfo("Choose a stocktake first.");
            return;
        }

        var reason = View.PromptCancelReason();
        if (string.IsNullOrWhiteSpace(reason)) return;

        var result = await _stocktakes.CancelAsync(id, reason);

        if (result.IsFailure) View.ShowError(result.Error.Message);
        await LoadAsync();
    });
}

public sealed class StocktakeCapturePresenter
    : PresenterBase<IStocktakeCaptureView>
{
    private readonly IStocktakeService _stocktakes;

    private StocktakeDto? _current;

    public StocktakeCapturePresenter(IStocktakeService stocktakes) =>
        _stocktakes = stocktakes;

    protected override void Subscribe()
    {
        View.CountRequested += (_, _) => _ = CountAsync();
        View.ClearCountRequested += (_, _) => _ = ClearAsync();
        View.VarianceFilterToggled += (_, _) => Redraw();
        View.PostRequested += (_, _) => _ = PostAsync();
        View.CloseRequested += (_, _) => View.CloseView(true);
    }

    public override Task InitialiseAsync() => LoadAsync();

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _stocktakes.GetAsync(View.StocktakeId);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            View.CloseView(false);
            return;
        }

        _current = result.Value;
        Redraw();
    });

    private void Redraw()
    {
        if (_current is not null) View.ShowStocktake(_current);
    }

    private Task CountAsync() => GuardAsync(async () =>
    {
        if (Selected() is not StocktakeLineDto line) return;

        var counted = View.PromptCount(line.Description, line.Expected,
                                       line.Counted);

        if (counted is null) return;

        var result = await _stocktakes.RecordCountAsync(View.StocktakeId,
            line.LineNo, counted);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        _current = result.Value;
        Redraw();
    });

    private Task ClearAsync() => GuardAsync(async () =>
    {
        if (Selected() is not StocktakeLineDto line) return;

        // Cleared to uncounted, not to zero: zero means the shelf was
        // empty, uncounted means nobody looked.
        var result = await _stocktakes.RecordCountAsync(View.StocktakeId,
            line.LineNo, counted: null);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        _current = result.Value;
        Redraw();
    });

    private Task PostAsync() => GuardAsync(async () =>
    {
        if (_current is null) return;

        if (_current.UncountedLines > 0)
        {
            View.ShowError(
                $"{_current.UncountedLines} line(s) have not been counted. "
                + "Posting an uncounted line would write off stock nobody "
                + "looked at.");

            return;
        }

        if (_current.RequiresVarianceReason
            && string.IsNullOrWhiteSpace(_current.VarianceReason))
        {
            var reason = View.PromptVarianceReason(_current.NetVarianceValue);

            if (string.IsNullOrWhiteSpace(reason))
            {
                View.ShowError("A variance this size needs a reason.");
                return;
            }

            var explained = await _stocktakes.ExplainVarianceAsync(
                View.StocktakeId, reason);

            if (explained.IsFailure)
            {
                View.ShowError(explained.Error.Message);
                return;
            }
        }

        if (!View.Confirm(
                $"Post this count? {_current.Lines.Count} line(s), net "
                + $"variance {_current.NetVarianceValue:N2}.\n\nPosting "
                + "changes the system's figures to match the shelf and "
                + "cannot be undone - a mistake is corrected by a fresh "
                + "count."))
            return;

        var result = await _stocktakes.PostAsync(View.StocktakeId);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowInfo($"Posted. Net variance {result.Value.NetVarianceValue:N2} "
                      + $"over {result.Value.Lines.Count(l => l.Variance != 0m)} "
                      + "line(s).");

        View.CloseView(true);
    });

    private StocktakeLineDto? Selected()
    {
        if (View.SelectedLineNo is not int lineNo || _current is null)
        {
            View.ShowInfo("Choose a line first.");
            return null;
        }

        return _current.Lines.FirstOrDefault(l => l.LineNo == lineNo);
    }
}
