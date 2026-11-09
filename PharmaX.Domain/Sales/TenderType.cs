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
    Eft = 6,

    /// <summary>
    /// EcoCash mobile money, paid from the customer's phone into the
    /// pharmacy's merchant wallet.
    /// </summary>
    /// <remarks>
    /// Taken wherever cash is - at the till, at collection, on account
    /// and for refunds - because in Zimbabwe it is used as freely as
    /// notes and coins. It is not in the drawer, though: it lands in the
    /// wallet, is posted to its own ledger account, and is declared at
    /// cash-up against the EcoCash statement. The customer sends the
    /// exact amount, so it never gives change.
    /// </remarks>
    EcoCash = 7
}

public static class TenderTypeExtensions
{
    /// Counted in the drawer at cash-up (specification Section 11).
    public static bool AffectsDrawer(this TenderType tender) =>
        tender == TenderType.Cash;

    /// Settled later rather than at the till, so it cannot give change.
    public static bool IsDeferred(this TenderType tender) =>
        tender is TenderType.Account or TenderType.MedicalAid;

    /// <summary>
    /// Money the pharmacy holds the moment it is taken, and so a tender
    /// that may settle a sale, a collection or an account, and refund one.
    /// </summary>
    public static bool IsCashLike(this TenderType tender) =>
        tender is TenderType.Cash or TenderType.EcoCash;

    /// <summary>
    /// Declared at cash-up against an outside statement rather than
    /// assumed to equal what the till expected. EcoCash from its merchant
    /// statement, card from the total the card machine collected.
    /// </summary>
    public static bool IsDeclaredAgainstStatement(this TenderType tender) =>
        tender is TenderType.EcoCash or TenderType.Card;

    /// <summary>The tender's name as the customer knows it.</summary>
    public static string DisplayName(this TenderType tender) => tender switch
    {
        TenderType.Eft => "EFT",
        TenderType.EcoCash => "EcoCash",
        TenderType.MedicalAid => "Medical aid",
        _ => tender.ToString()
    };
}
