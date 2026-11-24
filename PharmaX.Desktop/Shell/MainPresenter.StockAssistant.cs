using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Alerts;
using PharmaX.Application.Security;
using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Desktop.Features.Inventory;
using PharmaX.Desktop.Features.Suppliers;
using PharmaX.Domain.Security;

namespace PharmaX.Desktop.Shell;

/// <summary>
/// The stock assistant at sign-in: a summary of the stock alerts, draft
/// orders for what has run out or is low, and the status bar button that
/// leads to them.
/// </summary>
/// <remarks>
/// Every figure comes from code (IStockAlertAssistant), not from the
/// language model. Nothing is sent to a supplier from here.
/// </remarks>
public sealed partial class MainPresenter
{
    private void SubscribeStockAssistant()
    {
        View.Opened += (_, _) => Enqueue(() => GuardAsync(StockAssistantAsync));
        View.AssistantNoticeClicked += (_, _) => Enqueue(() => GuardAsync(DraftNoticeAsync));
    }

    private bool MayRaiseOrders =>
        _permissions.Check(PermissionCode.PurchaseOrderRaise) == PermissionOutcome.Granted;

    private async Task StockAssistantAsync()
    {
        var showPopup = await StockPopupWantedAsync();

        string summary = "", drafts = "", missing = "";

        using (var scope = _scopes.CreateScope())
        {
            var assistant = scope.ServiceProvider.GetRequiredService<IStockAlertAssistant>();

            var digest = await assistant.DigestAsync();
            if (digest.IsSuccess && !digest.Value.IsEmpty)
                summary = digest.Value.Describe();

            var made = await assistant.DraftOrdersAsync();

            if (made.IsFailure)
                missing = "The draft orders could not be made: " + made.Error.Message;
            else
            {
                if (made.Value.Orders.Count > 0)
                    drafts = DraftNotice.Describe(made.Value.Orders, justMade: true);

                missing = made.Value.DescribeMissing();
            }
        }

        var waiting = await ShowDraftCountAsync();

        // Drafts from an earlier sign-in, still not sent, are mentioned too.
        if (drafts.Length == 0 && waiting.Count > 0)
            drafts = DraftNotice.Describe(waiting, justMade: false);

        if (!showPopup) return;
        if (summary.Length == 0 && drafts.Length == 0 && missing.Length == 0) return;

        ShowStockAssistant(summary.Length > 0 ? "Stock needing attention" : "Purchase orders",
                           summary, drafts, missing);
    }

    private async Task<bool> StockPopupWantedAsync()
    {
        using var scope = _scopes.CreateScope();
        var value = await scope.ServiceProvider
            .GetRequiredService<IPharmacySettings>()
            .GetAsync(PharmacySettingKey.StockAssistant);

        return !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
    }

    private async Task DraftNoticeAsync()
    {
        var waiting = await ShowDraftCountAsync();

        ShowStockAssistant("Purchase orders", "",
            DraftNotice.Describe(waiting, justMade: false), "");
    }

    private void ShowStockAssistant(string heading, string summary, string drafts,
                                    string missing)
    {
        frmStockAssistant.Choice chosen;

        using (var popup = new frmStockAssistant())
        {
            popup.Prepare(heading, summary, drafts, missing,
                ScreenAccess.CanOpen("alerts", _permissions));
            popup.ShowDialog((Form)View);
            chosen = popup.Chosen;
        }

        if (chosen == frmStockAssistant.Choice.Drafts)
            Open<frmDraftOrders>("draft-orders", closed: async () => await ShowDraftCountAsync());
        else if (chosen == frmStockAssistant.Choice.Alerts)
            Open<frmAlerts>("alerts", closed: ShowAlertsAsync);
    }

    /// <summary>
    /// The drafts waiting, shown on the status bar button; none for a user
    /// who may not raise orders.
    /// </summary>
    private async Task<IReadOnlyList<PurchaseOrderDto>> ShowDraftCountAsync()
    {
        if (!MayRaiseOrders)
        {
            View.SetAssistantNotice("");
            return Array.Empty<PurchaseOrderDto>();
        }

        using var scope = _scopes.CreateScope();

        var drafts = await scope.ServiceProvider
            .GetRequiredService<IPurchasingService>()
            .DraftsAsync();

        var waiting = drafts.IsSuccess ? drafts.Value : Array.Empty<PurchaseOrderDto>();

        View.SetAssistantNotice(DraftNotice.Caption(waiting.Count));
        return waiting;
    }
}
