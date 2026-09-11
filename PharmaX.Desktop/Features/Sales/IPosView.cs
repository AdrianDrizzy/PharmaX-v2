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

    event EventHandler<string>? CodeScanned;
    event EventHandler? QuantityRequested;      // F4
    event EventHandler? LineDiscountRequested;  // F5
    event EventHandler? VoidLineRequested;      // Esc
    event EventHandler? PaymentRequested;       // Enter on totals
    event EventHandler? HoldRequested;          // F8
    event EventHandler? CloseRequested;         // F7
}
