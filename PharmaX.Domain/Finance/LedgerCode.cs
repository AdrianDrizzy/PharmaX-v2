using PharmaX.Domain.Sales;

namespace PharmaX.Domain.Finance;

/// <summary>
/// The accounts PharmaX posts to by itself.
/// </summary>
/// <remarks>
/// Constants rather than literals for the same reason as the permission
/// codes: a mistyped account code is a journal that posts somewhere
/// nobody looks, and it will balance while it does it.
///
/// The chart is seeded with these and can be added to, but these
/// particular codes cannot be renumbered without changing this file -
/// which is the honest trade for automatic posting. A pharmacy that
/// wants a different numbering can rename the accounts; the codes are
/// internal plumbing, and only the names are ever printed.
/// </remarks>
public static class LedgerCode
{
    public const string Bank = "1000";
    public const string CashOnHand = "1010";
    public const string TradeDebtors = "1100";
    public const string StockOnHand = "1200";

    public const string TradeCreditors = "2000";
    public const string VatControl = "2100";

    public const string OwnersCapital = "3000";
    public const string RetainedEarnings = "3100";

    public const string Sales = "4000";

    public const string Purchases = "5000";
    public const string StockWrittenOff = "5100";
    public const string CashShortOrOver = "5900";

    /// <summary>
    /// The standard chart, as seeded on a new installation.
    /// </summary>
    /// <remarks>
    /// Small deliberately. A pharmacy's books are simple, and a chart
    /// with sixty accounts in it on day one is a chart where everything
    /// ends up in Sundries because nobody can find the right one.
    /// </remarks>
    public static IReadOnlyList<(string Code, string Name,
        LedgerAccountType Type)> Standard { get; } = new[]
    {
        (Bank, "Bank", LedgerAccountType.Asset),
        (CashOnHand, "Cash on Hand", LedgerAccountType.Asset),
        (TradeDebtors, "Trade Debtors", LedgerAccountType.Asset),
        (StockOnHand, "Stock on Hand", LedgerAccountType.Asset),

        (TradeCreditors, "Trade Creditors", LedgerAccountType.Liability),
        (VatControl, "VAT Control", LedgerAccountType.Liability),

        (OwnersCapital, "Owner's Capital", LedgerAccountType.Equity),
        (RetainedEarnings, "Retained Earnings", LedgerAccountType.Equity),

        (Sales, "Sales", LedgerAccountType.Income),

        (Purchases, "Purchases", LedgerAccountType.Expense),
        (StockWrittenOff, "Stock Written Off", LedgerAccountType.Expense),
        (CashShortOrOver, "Cash Short or Over", LedgerAccountType.Expense)
    };

    /// <summary>
    /// Where the money from one tender type lands.
    /// </summary>
    /// <remarks>
    /// Card, EFT and voucher all reach the bank rather than the drawer,
    /// which is why cash-up counts only cash. Account and medical aid
    /// are money the pharmacy is owed rather than money it has, so they
    /// go to debtors and stay there until somebody settles.
    /// </remarks>
    public static string For(TenderType tender) => tender switch
    {
        TenderType.Cash => CashOnHand,
        TenderType.Account or TenderType.MedicalAid => TradeDebtors,
        _ => Bank
    };
}
