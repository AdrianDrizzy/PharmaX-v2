namespace PharmaX.Application.Home;

/// <summary>
/// The pharmacy in numbers, for the home screen: what is on file, and
/// what has been done today.
/// </summary>
/// <remarks>
/// Counts only, never the rows behind them, so it is one cheap query a
/// figure. The shell shows each to the people who can open the screen
/// it counts, and nobody else.
/// </remarks>
public sealed record HomeCountsDto(int Patients, int Products,
    int OpenPurchaseOrders, int SalesToday, int ScriptsToday);

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
