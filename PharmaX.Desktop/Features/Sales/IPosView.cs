using PharmaX.Application.Sales.Dtos;

namespace PharmaX.Desktop.Features.Sales;

public interface IPosView : Mvp.IView
{
    void ShowLines(IReadOnlyList<SaleLineDto> lines);
    void ShowTotals(SaleTotalsDto totals);
    void ShowCustomer(string name, string accountInfo, bool ratedPricing);

    /// Whether the basket is priced at a medical aid tariff, which is
    /// what the cashier tells the customer the price rests on.
    void ShowRatedPricing(bool rated);
    void ShowDocumentNumber(string documentNumber);
    void ClearSale();
    void FocusScan();

    /// <summary>
    /// The one thing left to do on a collection: the lines are already
    /// priced from the script, so there is nothing to scan.
    /// </summary>
    void FocusPayment();

    int? SelectedLineNo { get; }

    /// Every line picked, with Ctrl or Shift, top to bottom. Esc voids them all.
    IReadOnlyList<int> SelectedLineNos { get; }

    // Prompts, kept on the view because they are UI concerns; the
    // presenter decides when to ask, not how.
    decimal? PromptQuantity(decimal current);
    decimal? PromptDiscount(decimal lineTotal);
    Guid? PromptSupervisor(string reason);
    IReadOnlyList<PaymentDto>? PromptPayment(decimal amountDue);

    /// <summary>USD or ZWG: which price list this sale is ringing.</summary>
    string SellingCurrency { get; }
    string? PromptVoidReason();

    /// <summary>
    /// Asks the operator to find a product by name or code and returns
    /// the codes of the ones they chose, in list order, or none if they
    /// gave up.
    /// </summary>
    /// <param name="term">
    /// What they have already typed, if anything, so a search that began
    /// as a scan attempt does not start from an empty box.
    /// </param>
    IReadOnlyList<string> PromptProductsSearch(string? term);

    /// <summary>
    /// Asks the operator to find the account this sale goes on, and
    /// returns it. Null where they gave up.
    /// </summary>
    Application.Patients.AccountDto? PromptAccount();

    /// Which held basket to recall, or null if cancelled.
    Guid? PromptHeldSale(IReadOnlyList<HeldSaleDto> held);

    /// The number on the quotation the customer brought back, or null.
    string? PromptQuotationNumber();

    /// <summary>
    /// Which receipt to print again, offering the last one this till
    /// printed. Null where the cashier backed out.
    /// </summary>
    string? PromptReceiptNumber(string? last);

    /// <summary>
    /// The change to hand back from the last sale, shown where the cashier
    /// is looking until the next one starts.
    /// </summary>
    void ShowChange(decimal change);

    event EventHandler<string>? CodeScanned;
    event EventHandler? ProductSearchRequested; // F2
    event EventHandler? CustomerRequested;      // F3
    event EventHandler? QuantityRequested;      // F4
    event EventHandler? LineDiscountRequested;  // F5
    event EventHandler? VoidLineRequested;      // Esc
    event EventHandler? PaymentRequested;       // Enter on totals
    event EventHandler? HoldRequested;          // F8
    event EventHandler? RecallRequested;        // F9
    event EventHandler? QuotationRequested;     // F10
    event EventHandler? ReprintRequested;       // F12
    event EventHandler? CloseRequested;         // F7
    event EventHandler? LockRequested;          // F1
    event EventHandler? NoSaleRequested;        // F11

    /// Why the drawer is being opened without a sale; null to cancel.
    string? PromptNoSaleReason();

    /// Holds the till until the operator, or a supervisor, signs in.
    void PromptUnlock(string username);
}
