namespace PharmaX.Domain.Inventory;

/// <summary>
/// Why stock moved. Every change to a quantity on hand carries one, which
/// is what makes a stock take reconcilable rather than a mystery.
/// </summary>
public enum MovementType
{
    Receipt = 1,
    Issue = 2,
    Return = 3,
    Adjustment = 4,
    TransferOut = 5,
    TransferIn = 6,
    StockTake = 7,
    Disposal = 8
}

public static class MovementTypeExtensions
{
    /// True where the movement adds to the shelf rather than taking from it.
    public static bool IsInbound(this MovementType type) =>
        type is MovementType.Receipt
             or MovementType.Return
             or MovementType.TransferIn;
}
