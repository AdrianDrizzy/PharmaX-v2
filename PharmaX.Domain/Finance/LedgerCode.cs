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

    /// <summary>
    /// The pharmacy's EcoCash merchant wallet.
    /// </summary>
    /// <remarks>
    /// Its own account rather than Cash on Hand: the money is in the
    /// wallet, not the drawer, and a cash-up that counted it as cash
    /// would never match the notes and coins.
    /// </remarks>
    public const string EcoCashWallet = "1020";
    public const string TradeDebtors = "1100";

    /// <summary>
    /// What the medical aid schemes owe, kept apart from what patients
    /// owe.
    /// </summary>
    /// <remarks>
    /// A pharmacy chases a scheme and chases a patient in completely
    /// different ways, on completely different timescales, and the age
    /// analysis that matters is the one per scheme. Folding both into
    /// Trade Debtors would hide the largest debtor the pharmacy has
    /// behind a column of small ones.
    /// </remarks>
    public const string MedicalAidDebtors = "1150";
    public const string StockOnHand = "1200";

    /// <summary>
    /// Vouchers taken as tender and not yet redeemed against the issuer.
    /// </summary>
    /// <remarks>
    /// Kept out of the bank accounts because a voucher is not money until
    /// somebody honours it, and a cash-up that counted vouchers as cash
    /// would balance against a drawer that does not contain them.
    /// </remarks>
    public const string VouchersRedeemed = "1250";

    public const string TradeCreditors = "2000";
    public const string VatControl = "2100";

    public const string OwnersCapital = "3000";
    public const string RetainedEarnings = "3100";

    /// <summary>
    /// What the owner has taken out of the business, from a till's
    /// drawer or anywhere else.
    /// </summary>
    /// <remarks>
    /// Equity coming down, not an expense: drawings booked as a cost
    /// would understate the profit the owner is drawing on.
    /// </remarks>
    public const string Drawings = "3200";

    public const string Sales = "4000";

    /// <summary>
    /// Where the fractions go when a line total is rounded to the minor
    /// unit under pos.rounding.mode.
    /// </summary>
    /// <remarks>
    /// Small and unavoidable rather than exceptional: without somewhere
    /// for the half-cent to land, a rounded sale posts a journal that
    /// does not balance, and an unbalanced journal is worse than a
    /// visible line an accountant can total at year end.
    /// </remarks>
    public const string RoundingDifferences = "4900";

    public const string Purchases = "5000";
    public const string StockWrittenOff = "5100";

    /// <summary>
    /// The part of a claim the scheme was never going to pay.
    /// </summary>
    /// <remarks>
    /// Short payment is normal rather than exceptional: a scheme pays
    /// its own tariff and not the pharmacy's price. Booking the
    /// difference here rather than quietly reducing the claim is what
    /// lets a manager see which scheme costs them the most to trade
    /// with.
    /// </remarks>
    public const string ClaimsWrittenOff = "5200";

    /// <summary>
    /// The small things paid in cash out of a till's drawer - lunch,
    /// bread, airtime - each recorded at the till with what it was for.
    /// </summary>
    public const string TillExpenses = "5800";

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
        (EcoCashWallet, "EcoCash Wallet", LedgerAccountType.Asset),
        (TradeDebtors, "Trade Debtors", LedgerAccountType.Asset),
        (MedicalAidDebtors, "Medical Aid Debtors", LedgerAccountType.Asset),
        (StockOnHand, "Stock on Hand", LedgerAccountType.Asset),
        (VouchersRedeemed, "Vouchers Redeemed", LedgerAccountType.Asset),

        (TradeCreditors, "Trade Creditors", LedgerAccountType.Liability),
        (VatControl, "VAT Control", LedgerAccountType.Liability),

        (OwnersCapital, "Owner's Capital", LedgerAccountType.Equity),
        (RetainedEarnings, "Retained Earnings", LedgerAccountType.Equity),
        (Drawings, "Drawings", LedgerAccountType.Equity),

        (Sales, "Sales", LedgerAccountType.Income),
        (RoundingDifferences, "Rounding Differences", LedgerAccountType.Income),

        (Purchases, "Purchases", LedgerAccountType.Expense),
        (StockWrittenOff, "Stock Written Off", LedgerAccountType.Expense),
        (ClaimsWrittenOff, "Claims Written Off", LedgerAccountType.Expense),
        (TillExpenses, "Till Expenses", LedgerAccountType.Expense),
        (CashShortOrOver, "Cash Short or Over", LedgerAccountType.Expense)
    };

    /// <summary>
    /// Where the money from one tender type lands.
    /// </summary>
    /// <remarks>
    /// Card and EFT reach the bank, EcoCash its wallet and vouchers the
    /// voucher account until the issuer honours them, which is why the
    /// cash-up counts only cash. Account and medical aid are money the
    /// pharmacy is owed rather than money it has, so they go to their
    /// debtors - patients and schemes apart - and stay there until
    /// somebody settles. The day-end journal and the journals posted as
    /// trading happens both read this, so they cannot disagree.
    /// </remarks>
    public static string For(TenderType tender) => tender switch
    {
        TenderType.Cash => CashOnHand,
        TenderType.EcoCash => EcoCashWallet,
        TenderType.Account => TradeDebtors,
        TenderType.MedicalAid => MedicalAidDebtors,
        TenderType.Voucher => VouchersRedeemed,
        _ => Bank
    };
}
