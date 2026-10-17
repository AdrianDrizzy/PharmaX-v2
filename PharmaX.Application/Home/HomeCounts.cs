namespace PharmaX.Application.Home;

/// <summary>
/// The pharmacy in numbers, for the home screen: what is on file and how
/// much of it was added today, and what has been done today.
/// </summary>
/// <remarks>
/// Counts only, never the rows behind them, so it is one cheap query a
/// figure. The shell shows each to the people who can open the screen
/// it counts, and nobody else. "Today" is the till's local day.
///
/// The three "last" dates say when a job that comes round now and again
/// was last done: a stock count posted, a purchase order raised, a report
/// asked for. Null where it never has been.
/// </remarks>
public sealed record HomeCountsDto(int Patients, int Products,
    int OpenPurchaseOrders, int SalesToday, int ScriptsToday,
    int PatientsAddedToday = 0, int ProductsAddedToday = 0,
    int ReturnsToday = 0, int DeliveriesToday = 0, int OpenStocktakes = 0,
    DateTime? LastStocktakeUtc = null, DateOnly? LastPurchaseOrderOn = null,
    DateTime? LastReportRunUtc = null);

public interface IHomeCounts
{
    /// <param name="products">
    /// The part of the catalogue this user sees, so a cashier's figure is
    /// the length of a cashier's product list.
    /// </param>
    Task<HomeCountsDto> ReadAsync(
        Repositories.ProductScope products = Repositories.ProductScope.All,
        CancellationToken ct = default);
}
