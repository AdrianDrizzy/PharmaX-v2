using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Suppliers;

public interface IGoodsReceiptView : IView
{
    Guid? SupplierId { get; }
    Guid? PurchaseOrderId { get; }

    GoodsReceiptEditDto Collect();

    void ShowSupplier(string description);
    void ShowOrder(PurchaseOrderDto? order);
    void ShowLines(IReadOnlyList<GoodsReceiptLineDto> lines);

    /// What each captured line's code names, line for line: null where
    /// it names nothing.
    void ShowProducts(IReadOnlyList<GoodsReceiptProductDto?> products);
    void ShowTotals(decimal captured, decimal? invoiced, decimal? variance);

    /// Why the lines cannot be posted as they stand, or null to clear it.
    void ShowProblem(string? problem);
    void ClearCapture();

    Guid? PromptSupplier();
    Guid? PromptOrder(Guid supplierId);

    event EventHandler? SupplierRequested;
    event EventHandler? OrderRequested;
    event EventHandler? LinesChanged;
    event EventHandler? PostRequested;
    event EventHandler? CloseRequested;
}

/// <summary>
/// Receiving is where batch numbers and expiry dates enter the system,
/// so it is where FEFO either becomes possible or does not. The screen
/// previews on every change and posts once, because a receipt half
/// captured is stock the shelf has and the system does not.
/// </summary>
public sealed class GoodsReceiptPresenter : PresenterBase<IGoodsReceiptView>
{
    private readonly IReceivingService _receiving;
    private readonly ISupplierService _suppliers;
    private readonly IPurchasingService _purchasing;

    public GoodsReceiptPresenter(IReceivingService receiving,
        ISupplierService suppliers, IPurchasingService purchasing)
    {
        _receiving = receiving;
        _suppliers = suppliers;
        _purchasing = purchasing;
    }

    protected override void Subscribe()
    {
        View.SupplierRequested += (_, _) => Enqueue(ChooseSupplierAsync);
        View.OrderRequested += (_, _) => Enqueue(ChooseOrderAsync);
        View.LinesChanged += (_, _) => Enqueue(PreviewAsync);
        View.PostRequested += (_, _) => Enqueue(PostAsync);
        View.CloseRequested += (_, _) => View.CloseView(true);
    }

    private Task ChooseSupplierAsync() => GuardAsync(async () =>
    {
        if (View.PromptSupplier() is not Guid id) return;

        var result = await _suppliers.GetAsync(id);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowSupplier($"{result.Value.Name}  ({result.Value.Code})");

        // Any order already attached belonged to the previous supplier.
        View.ShowOrder(null);
        await PreviewAsync();
    });

    private Task ChooseOrderAsync() => GuardAsync(async () =>
    {
        if (View.SupplierId is not Guid supplierId)
        {
            View.ShowError("Choose the supplier first.");
            return;
        }

        if (View.PromptOrder(supplierId) is not Guid orderId) return;

        var result = await _purchasing.GetAsync(orderId);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowOrder(result.Value);
        await PreviewAsync();
    });

    /// <summary>
    /// Resolves the typed codes without writing anything, so the
    /// operator sees the descriptions and the line totals as they type
    /// and every problem is named before the post.
    /// </summary>
    private Task PreviewAsync() => GuardAsync(async () =>
    {
        var edit = View.Collect();

        // What each code names comes first and on its own, so the product
        // beside a code is always the one it names. It used to arrive only
        // with a preview that passed, so while any line still had a problem
        // a code typed over another kept the old product's description.
        if (edit.Lines.Count > 0)
        {
            var identified = await _receiving.IdentifyAsync(edit);

            if (identified.IsSuccess)
            {
                View.ShowProducts(identified.Value);

                // A newly identified line may have brought its cost price.
                edit = View.Collect();
            }
        }

        if (edit.Lines.Count == 0)
        {
            View.ShowLines(Array.Empty<GoodsReceiptLineDto>());
            View.ShowTotals(0m, edit.InvoiceTotal, null);
            View.ShowProblem(null);
            return;
        }

        var result = await _receiving.PreviewAsync(edit);

        if (result.IsFailure)
        {
            // Shown on the status line rather than in a message box: the
            // operator is still typing, and a modal per keystroke would
            // be unusable. It used to be shown nowhere at all.
            View.ShowLines(Array.Empty<GoodsReceiptLineDto>());
            View.ShowTotals(0m, edit.InvoiceTotal, null);
            View.ShowProblem(result.Error.Message);
            return;
        }

        View.ShowProblem(null);

        var captured = result.Value.Sum(l => l.LineTotal);

        View.ShowLines(result.Value);
        View.ShowTotals(captured, edit.InvoiceTotal,
            edit.InvoiceTotal is decimal invoiced
                ? invoiced - captured
                : null);
    });

    private Task PostAsync() => GuardAsync(async () =>
    {
        var edit = View.Collect();

        if (edit.SupplierId == Guid.Empty)
        {
            View.ShowError("Choose a supplier.");
            return;
        }

        var preview = await _receiving.PreviewAsync(edit);

        if (preview.IsFailure)
        {
            View.ShowError(preview.Error.Message);
            return;
        }

        var captured = preview.Value.Sum(l => l.LineTotal);

        // A mismatch against the supplier's invoice is a question for
        // the operator, not a reason to refuse: the delivery note and
        // the invoice disagree often enough that stopping the receipt
        // would leave the stock off the shelf.
        if (edit.InvoiceTotal is decimal invoiced && invoiced != captured
            && !View.Confirm(
                $"The lines come to {captured:N2} but the invoice says "
                + $"{invoiced:N2}, a difference of {invoiced - captured:N2}."
                + "\n\nPost anyway? The variance is kept on the receipt."))
            return;

        if (!View.Confirm(
                $"Post {preview.Value.Count} line(s) worth {captured:N2}? "
                + "Posting moves the stock onto the shelf and cannot be "
                + "undone - a mistake is corrected by an adjustment."))
            return;

        var result = await _receiving.ReceiveAsync(edit);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowInfo($"Received on {result.Value.ReceiptNumber}. "
                      + $"{result.Value.Lines.Count} line(s), "
                      + $"{result.Value.Total:N2}.");

        View.ClearCapture();
        await PreviewAsync();
    });
}
