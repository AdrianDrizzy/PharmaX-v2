namespace PharmaX.Domain.Sales;

/// <summary>
/// How a sale was paid for. Cash and Account behave differently at cash-up:
/// only cash is counted in the drawer, and only account moves a balance.
/// </summary>
public enum TenderType
{
    Cash = 1,
    Card = 2,
    Account = 3,
    MedicalAid = 4,
    Voucher = 5,
    Eft = 6
}

public static class TenderTypeExtensions
{
    /// Counted in the drawer at cash-up (specification Section 11).
    public static bool AffectsDrawer(this TenderType tender) =>
        tender == TenderType.Cash;

    /// Settled later rather than at the till, so it cannot give change.
    public static bool IsDeferred(this TenderType tender) =>
        tender is TenderType.Account or TenderType.MedicalAid;
}
