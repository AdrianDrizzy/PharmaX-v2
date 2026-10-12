using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Domain.Claims;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Suppliers;
using PharmaX.Domain.Tills;

namespace PharmaX.Application.Finance;

/// <summary>
/// Writes the pharmacy's own trading into the books.
/// </summary>
/// <remarks>
/// Every method here is called from inside the transaction of the thing
/// it is recording, so a sale that fails to save takes its journal with
/// it and the books can never describe a sale that did not happen.
///
/// The method is periodic inventory rather than perpetual: a delivery is
/// expensed to Purchases when it is received, and Stock on Hand is
/// brought to the counted figure by a journal at period end. Perpetual
/// costing would mean looking up a cost price for every line of every
/// sale, inside the transaction, at a till with a customer waiting - for
/// a gross-margin figure that a monthly stock count gives more
/// accurately anyway. Periodic is what a pharmacy of this size actually
/// does, and it is why manual journals are part of this feature rather
/// than an afterthought.
/// </remarks>
public interface IJournalPoster
{
    /// Money in, revenue and VAT recognised.
    Task PostSaleAsync(Sale sale, CancellationToken ct = default);

    /// The same entry backwards.
    Task PostReturnAsync(SaleReturn saleReturn,
                         CancellationToken ct = default);

    /// Stock bought on credit.
    Task PostReceiptAsync(GoodsReceipt receipt,
                          CancellationToken ct = default);

    /// Stock sent back to a supplier: the receipt's entry backwards, at cost.
    Task PostSupplierReturnAsync(string supplier, Money value,
                                 CancellationToken ct = default);

    /// <summary>
    /// What the drawer was actually short or over by. Posts nothing
    /// where it balanced, which is most days.
    /// </summary>
    Task PostCashUpAsync(TillSession session, Money expected,
                         Money counted, CancellationToken ct = default);

    /// <summary>
    /// One tender's cash-up difference, against the account that tender
    /// lands in: the drawer for cash, the wallet for EcoCash.
    /// </summary>
    Task PostCashUpAsync(TillSession session, Domain.Sales.TenderType tender,
                         Money expected, Money counted,
                         CancellationToken ct = default);

    /// <summary>
    /// A scheme's share of a dispensing, recognised the moment the
    /// medicine leaves.
    /// </summary>
    /// <param name="splits">
    /// The VAT split of the scheme's share, a line at a time. Passed in
    /// rather than worked out here because a script can mix rates - a
    /// zero-rated medicine beside a standard-rated one - and only the
    /// caller knows which product each part of the claim came from.
    /// </param>
    Task PostClaimAsync(MedicalAidClaim claim, string schemeName,
        IReadOnlyList<Domain.Pricing.VatSplit> splits,
        CancellationToken ct = default);

    /// <summary>
    /// The mirror of a claim, for a dispensing reversed before the scheme
    /// paid: the debtor and the revenue both come off.
    /// </summary>
    Task PostClaimReversalAsync(MedicalAidClaim claim, string schemeName,
        IReadOnlyList<Domain.Pricing.VatSplit> splits,
        CancellationToken ct = default);

    /// <summary>
    /// Money received off a patient's account.
    /// </summary>
    Task PostAccountPaymentAsync(string accountNumber, Money paid,
        string tenderType, DateOnly receivedOn, string? reference,
        CancellationToken ct = default);

    /// <summary>
    /// A scheme's remittance: what it paid, and what the pharmacy has
    /// given up on.
    /// </summary>
    Task PostRemittanceAsync(string schemeName, Money paid, Money writtenOff,
        DateOnly receivedOn, string? reference,
        CancellationToken ct = default);
}

public sealed class JournalPoster : IJournalPoster
{
    private readonly ILedgerRepository _ledger;
    private readonly IDocumentNumbering _numbering;
    private readonly PharmaXOptions _options;
    private readonly IClock _clock;

    public JournalPoster(ILedgerRepository ledger,
        IDocumentNumbering numbering, PharmaXOptions options, IClock clock)
    {
        _ledger = ledger;
        _numbering = numbering;
        _options = options;
        _clock = clock;
    }

    private bool Enabled => _options.Accounting.AutoPost;

    public async Task PostSaleAsync(Sale sale, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sale);

        if (!Enabled || sale.Total.IsZero) return;

        var batch = await OpenAsync("POS",
            $"Sale {sale.DocumentNumber}", ct);

        // One debit per tender, so the books say how the money arrived
        // and the bank reconciliation has something to work with.
        foreach (var group in sale.Payments
                     .GroupBy(p => _options.Accounting.CodeFor(p.Tender)))
        {
            var taken = group.Aggregate(Money.Zero(),
                (sum, p) => sum + p.Amount);

            if (!taken.IsZero) batch.AddLine(group.Key, taken, isDebit: true);
        }

        // No line for change. BR-POS-01 refuses to finalise a sale
        // whose tender does not equal its total, so the payments on a
        // finalised sale add up to exactly what it was worth and the
        // change has already been worked out at the payment screen. If
        // that rule is ever relaxed, the batch here stops balancing and
        // Post throws, which is the right way to find out.

        AddRevenue(batch, sale.Lines.Where(l => !l.IsVoided)
            .Select(l => l.Vat), isDebit: false);

        await PostAsync(batch, ct);
    }

    public async Task PostReturnAsync(SaleReturn saleReturn,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(saleReturn);

        if (!Enabled || saleReturn.Total.IsZero) return;

        var batch = await OpenAsync("RTN",
            $"Refund {saleReturn.DocumentNumber}", ct);

        foreach (var group in saleReturn.Refunds
                     .GroupBy(r => _options.Accounting.CodeFor(r.Tender)))
        {
            var paid = group.Aggregate(Money.Zero(),
                (sum, r) => sum + r.Amount);

            // Money out: the mirror of the sale's debit.
            if (!paid.IsZero) batch.AddLine(group.Key, paid, isDebit: false);
        }

        // A return line keeps its rate but not its split, so the split
        // is worked out here the same way the sale line works out its
        // own - from the inclusive line total.
        AddRevenue(batch, saleReturn.Lines.Select(l =>
            Domain.Pricing.VatCalculator.FromInclusive(
                l.LineTotal, l.VatRatePercent)),
            isDebit: true);

        await PostAsync(batch, ct);
    }

    public async Task PostReceiptAsync(GoodsReceipt receipt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        if (!Enabled || receipt.Total.IsZero) return;

        var batch = await OpenAsync("GRN",
            $"Goods received {receipt.ReceiptNumber}", ct);

        // Expensed on arrival, not capitalised: see the class remarks on
        // periodic inventory. The stock figure on the balance sheet is
        // put right by journal when the shelves are counted.
        batch.AddLine(LedgerCode.Purchases, receipt.Total, isDebit: true);
        batch.AddLine(LedgerCode.TradeCreditors, receipt.Total,
                      isDebit: false);

        await PostAsync(batch, ct);
    }

    public async Task PostSupplierReturnAsync(string supplier, Money value,
        CancellationToken ct = default)
    {
        if (!Enabled || value.IsZero) return;

        var batch = await OpenAsync("RTS", $"Returned to {supplier}", ct);

        batch.AddLine(LedgerCode.TradeCreditors, value, isDebit: true);
        batch.AddLine(LedgerCode.Purchases, value, isDebit: false);

        await PostAsync(batch, ct);
    }

    public Task PostCashUpAsync(TillSession session, Money expected,
        Money counted, CancellationToken ct = default) =>
        PostCashUpAsync(session, Domain.Sales.TenderType.Cash, expected,
                        counted, ct);

    public async Task PostCashUpAsync(TillSession session,
        Domain.Sales.TenderType tender, Money expected, Money counted,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!Enabled) return;

        var variance = counted - expected;

        // A till that balanced has nothing to say. Posting a zero
        // journal every evening would bury the ones that matter.
        if (variance.IsZero) return;

        var batch = await OpenAsync("CASHUP",
            string.Format(CultureInfo.CurrentCulture,
                tender == Domain.Sales.TenderType.Cash
                    ? "Till {0} cash-up, counted {1:0.00} against {2:0.00}"
                    : "Till {0} cash-up, {3} declared {1:0.00} against {2:0.00}",
                session.TillNumber, counted.Amount, expected.Amount,
                tender.DisplayName()),
            ct);

        var amount = Money.Of(Math.Abs(variance.Amount), variance.Currency);

        // The same account the takings went to, so a short EcoCash
        // statement brings the wallet down rather than the drawer.
        var holding = _options.Accounting.CodeFor(tender);

        if (variance.IsNegative)
        {
            // Short: the till holds less than the sales say it should,
            // so the loss is an expense and the holding account comes
            // down to the truth.
            batch.AddLine(LedgerCode.CashShortOrOver, amount, isDebit: true);
            batch.AddLine(holding, amount, isDebit: false);
        }
        else
        {
            batch.AddLine(holding, amount, isDebit: true);
            batch.AddLine(LedgerCode.CashShortOrOver, amount,
                          isDebit: false);
        }

        await PostAsync(batch, ct);
    }

    public async Task PostClaimAsync(MedicalAidClaim claim,
        string schemeName, IReadOnlyList<Domain.Pricing.VatSplit> splits,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(splits);

        if (!Enabled || claim.Claimed.IsZero) return;

        var batch = await OpenAsync("CLAIM",
            $"Claim {claim.ClaimNumber} on {schemeName}", ct);

        // Revenue is earned when the medicine leaves the shelf, not when
        // the scheme gets round to paying. Without this entry the books
        // recorded only the patient's co-payment - for a dispensary, a
        // small fraction of what it actually earned.
        batch.AddLine(LedgerCode.MedicalAidDebtors, claim.Claimed,
                      isDebit: true);

        AddRevenue(batch, splits, isDebit: false);

        await PostAsync(batch, ct);
    }

    public async Task PostClaimReversalAsync(MedicalAidClaim claim,
        string schemeName, IReadOnlyList<Domain.Pricing.VatSplit> splits,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(splits);

        if (!Enabled || claim.Claimed.IsZero) return;

        var batch = await OpenAsync("CLAIMREV",
            $"Claim {claim.ClaimNumber} on {schemeName} withdrawn", ct);

        batch.AddLine(LedgerCode.MedicalAidDebtors, claim.Claimed,
                      isDebit: false);

        AddRevenue(batch, splits, isDebit: true);

        await PostAsync(batch, ct);
    }

    public async Task PostAccountPaymentAsync(string accountNumber,
        Money paid, string tenderType, DateOnly receivedOn,
        string? reference, CancellationToken ct = default)
    {
        if (!Enabled || paid.IsZero) return;

        var batch = JournalBatch.Create(
            await _numbering.NextAsync(DocumentSeries.Journal, ct), receivedOn, "ACCPAY",
            $"Payment on account {accountNumber}"
            + (string.IsNullOrWhiteSpace(reference)
                ? "" : $", {reference}"));

        // Where the money landed follows the tender, exactly as it does
        // at a till: cash into the drawer, EcoCash into the wallet,
        // anything else to the bank.
        var landed = Enum.TryParse<Domain.Sales.TenderType>(tenderType,
            ignoreCase: true, out var tender)
            ? _options.Accounting.CodeFor(tender)
            : LedgerCode.Bank;

        // An account settled in cash still goes through the drawer, so
        // it has to reach the same account the cash-up counts.
        batch.AddLine(landed, paid, isDebit: true);

        // No VAT here. It was accounted for when the sale was put on the
        // account; this is only the debtor being settled.
        batch.AddLine(LedgerCode.TradeDebtors, paid, isDebit: false);

        await PostAsync(batch, ct);
    }

    public async Task PostRemittanceAsync(string schemeName, Money paid,
        Money writtenOff, DateOnly receivedOn, string? reference,
        CancellationToken ct = default)
    {
        if (!Enabled) return;

        var cleared = paid + writtenOff;
        if (cleared.IsZero) return;

        var batch = JournalBatch.Create(
            await _numbering.NextAsync(DocumentSeries.Journal, ct), receivedOn, "REMIT",
            $"Remittance from {schemeName}"
            + (string.IsNullOrWhiteSpace(reference)
                ? "" : $", {reference}"));

        if (!paid.IsZero)
            batch.AddLine(LedgerCode.Bank, paid, isDebit: true);

        // What the scheme was never going to pay is a cost of trading
        // with them, booked where a manager can see which scheme costs
        // the most.
        if (!writtenOff.IsZero)
            batch.AddLine(LedgerCode.ClaimsWrittenOff, writtenOff,
                          isDebit: true, "Short payment");

        batch.AddLine(LedgerCode.MedicalAidDebtors, cleared, isDebit: false);

        await PostAsync(batch, ct);
    }

    /// <summary>
    /// The revenue side of a sale or a refund: net to Sales, VAT to the
    /// control account.
    /// </summary>
    /// <remarks>
    /// Summed from the per-line VAT splits rather than taking a
    /// percentage of the total, because a basket can mix rates - a
    /// zero-rated medicine and a standard-rated tube of hand cream on
    /// the same receipt - and one rate applied to the whole would
    /// misstate what is owed to the revenue authority.
    /// </remarks>
    private static void AddRevenue(JournalBatch batch,
        IEnumerable<Domain.Pricing.VatSplit> splits, bool isDebit)
    {
        var net = Money.Zero();
        var vat = Money.Zero();

        foreach (var split in splits)
        {
            net += split.NetAmount;
            vat += split.VatAmount;
        }

        if (!net.IsZero)
            batch.AddLine(LedgerCode.Sales, net, isDebit);

        if (!vat.IsZero)
            batch.AddLine(LedgerCode.VatControl, vat, isDebit);
    }

    private async Task<JournalBatch> OpenAsync(string source,
        string narration, CancellationToken ct)
    {
        var number = await _numbering.NextAsync(DocumentSeries.Journal, ct);

        return JournalBatch.Create(number, _clock.Today, source, narration);
    }

    /// <summary>
    /// Posts the batch and files it.
    /// </summary>
    /// <remarks>
    /// Post throws where the lines do not balance, and that is left to
    /// throw rather than caught. An automatic journal that does not
    /// balance is a defect in this class, and the right time to find out
    /// is on the transaction that produced it - not three weeks later in
    /// a trial balance nobody can tie back.
    /// </remarks>
    private async Task PostAsync(JournalBatch batch, CancellationToken ct)
    {
        if (batch.Lines.Count == 0) return;

        batch.Post(_clock.UtcNow);
        await _ledger.AddBatchAsync(batch, ct);
    }
}
