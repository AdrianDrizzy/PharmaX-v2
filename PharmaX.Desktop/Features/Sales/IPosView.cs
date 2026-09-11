using PharmaX.Application.Sales.Dtos;

namespace PharmaX.Desktop.Features.Sales;

public interface IPosView : Mvp.IView
{
    void ShowLines(IReadOnlyList<SaleLineDto> lines);
    void ShowTotals(SaleTotalsDto totals);
    void ShowCustomer(string name, string accountInfo, bool ratedPricing);
    void ShowDocumentNumber(string documentNumber);
    void ClearSale();
    void FocusScan();

    int? SelectedLineNo { get; }

    // Prompts, kept on the view because they are UI concerns; the
    // presenter decides when to ask, not how.
    decimal? PromptQuantity(decimal current);
    decimal? PromptDiscount(decimal lineTotal);
    Guid? PromptSupervisor(string reason);
    IReadOnlyList<PaymentDto>? PromptPayment(decimal amountDue);
    string? PromptVoidReason();

    /// <summary>
    /// Asks the operator to find a product by name or code and returns
    /// the code of the one they chose, or null if they gave up.
    /// </summary>
    /// <param name="term">
    /// What they have already typed, if anything, so a search that began
    /// as a scan attempt does not start from an empty box.
    /// </param>
    string? PromptProductSearch(string? term);

    event EventHandler<string>? CodeScanned;
    event EventHandler? ProductSearchRequested; // F2
    event EventHandler? QuantityRequested;      // F4
    event EventHandler? LineDiscountRequested;  // F5
    event EventHandler? VoidLineRequested;      // Esc
    event EventHandler? PaymentRequested;       // Enter on totals
    event EventHandler? HoldRequested;          // F8
    event EventHandler? CloseRequested;         // F7
}
