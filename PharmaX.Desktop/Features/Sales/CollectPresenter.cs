using System.Globalization;
using PharmaX.Application.Sales;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Sales;

/// <summary>
/// Keeps the till's view of the dispensary's queue up to date.
/// </summary>
/// <remarks>
/// The whole screen is a poll. There is no push from the pharmacist's
/// machine to the tills - the pharmacy shares one database and nothing
/// else - so the alternative to asking again every few seconds is a
/// cashier pressing Refresh and a patient standing at the counter while
/// nobody does. The query is indexed and returns a handful of rows.
/// </remarks>
public sealed class CollectPresenter : PresenterBase<ICollectView>
{
    private readonly ICollectionService _collections;

    private IReadOnlyList<CollectionDto> _waiting = Array.Empty<CollectionDto>();

    public CollectPresenter(ICollectionService collections) =>
        _collections = collections;

    protected override void Subscribe()
    {
        View.RefreshRequested += (_, _) => Enqueue(LoadAsync);
        View.FilterChanged += (_, _) => Show();
        View.CollectRequested += (_, _) => Enqueue(OnCollectAsync);
    }

    public override Task InitialiseAsync() => LoadAsync();

    /// <summary>
    /// Re-reads the queue without the busy cursor.
    /// </summary>
    /// <remarks>
    /// Called by the screen's timer as well as by Refresh. Flickering
    /// the hourglass every few seconds on a screen nobody asked to wait
    /// for would make the till look broken.
    /// </remarks>
    public async Task RefreshQuietlyAsync()
    {
        var result = await _collections.WaitingAsync();
        if (result.IsFailure) return;

        _waiting = result.Value;
        Show();
    }

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _collections.WaitingAsync();

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        _waiting = result.Value;
        Show();
    });

    private Task OnCollectAsync() => GuardAsync(async () =>
    {
        if (View.SelectedDispensingId is not Guid id)
        {
            View.ShowInfo("Choose a script to collect first.");
            return;
        }

        var chosen = _waiting.FirstOrDefault(w => w.DispensingId == id);
        if (chosen is null) return;

        var paid = View.TakePayment(id, chosen.PatientName);

        // Refreshed either way. If the patient paid, the script is gone
        // from the queue; if the cashier backed out, another till may
        // have taken it in the meantime, and finding out now beats
        // finding out on the next click.
        await RefreshQuietlyAsync();

        if (paid) View.ShowInfo($"{chosen.PatientName} paid. Next patient.");
    });

    private void Show()
    {
        var filter = View.FilterText;

        var shown = string.IsNullOrWhiteSpace(filter)
            ? _waiting
            : _waiting.Where(w => Matches(w, filter)).ToList();

        View.ShowWaiting(shown);
        View.ShowSummary(Summarise(shown));
    }

    /// Patient name or either document number, whichever the cashier has
    /// in front of them: a name called across the shop, or a number off
    /// the slip the patient is holding.
    private static bool Matches(CollectionDto waiting, string filter) =>
        waiting.PatientName.Contains(filter,
            StringComparison.CurrentCultureIgnoreCase)
        || waiting.ScriptNumber.Contains(filter,
            StringComparison.OrdinalIgnoreCase)
        || waiting.DocumentNumber.Contains(filter,
            StringComparison.OrdinalIgnoreCase);

    private static string Summarise(IReadOnlyList<CollectionDto> shown)
    {
        if (shown.Count == 0) return "Nothing waiting.";

        var owed = shown.Sum(w => w.CoPayment);

        var longest = shown.Max(w => DateTime.UtcNow - w.IssuedAtUtc);

        return string.Format(CultureInfo.CurrentCulture,
            "{0} waiting  -  {1} owed  -  longest {2}",
            shown.Count, Format.Money(owed), Describe(longest));
    }

    /// <summary>
    /// How long the patient has been standing there, in the terms a
    /// person would use.
    /// </summary>
    internal static string Describe(TimeSpan waited)
    {
        if (waited < TimeSpan.Zero) waited = TimeSpan.Zero;

        if (waited.TotalMinutes < 1) return "just now";

        if (waited.TotalHours < 1)
            return string.Format(CultureInfo.CurrentCulture, "{0} min",
                (int)waited.TotalMinutes);

        return string.Format(CultureInfo.CurrentCulture, "{0}h {1:00}",
            (int)waited.TotalHours, waited.Minutes);
    }
}
