using System.Globalization;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Desktop.Controls;

namespace PharmaX.Desktop.Features.Dispensing;

/// <summary>
/// The questions changing one item asks - which item, how many, how much,
/// and after the issue why - the same on the dispensing screen and on the
/// labels screen.
/// </summary>
public static class ItemChange
{
    /// <param name="Amount">
    /// What to charge; null to price it as a new item would be, which is
    /// what leaving the suggested amount alone means before the issue.
    /// </param>
    public sealed record Answer(int LineNo, decimal Quantity, decimal? Amount,
                                string? Reason);

    /// <param name="lineNo">The item the operator is on, if any.</param>
    public static Answer? Ask(IWin32Window owner,
        IReadOnlyList<DispensingLineDto> items, int? lineNo, bool issued)
    {
        ArgumentNullException.ThrowIfNull(items);

        var item = items.FirstOrDefault(i => i.LineNo == lineNo)
                   ?? (items.Count == 1 ? items[0] : null);

        if (item is null)
        {
            if (frmChoicePrompt.Ask(owner, "Change item", "Which item?",
                    items.Select(i => (
                        string.Create(CultureInfo.CurrentCulture,
                            $"{i.Description}   x{i.Quantity:0.###}   {i.Tariff:N2}"),
                        i.LineNo)).ToList()) is not int picked)
                return null;

            item = items.First(i => i.LineNo == picked);
        }

        if (frmNumberPrompt.Ask(owner, "Change item",
                $"{item.Description}: how many "
                + (issued ? "were handed over?" : "to hand over?"),
                item.Quantity, 3) is not decimal quantity || quantity <= 0m)
            return null;

        var suggested = Math.Round(item.Tariff * quantity / item.Quantity, 2,
                                   MidpointRounding.AwayFromZero);

        if (frmNumberPrompt.Ask(owner, "Change item",
                string.Create(CultureInfo.CurrentCulture,
                    $"{item.Description} x{quantity:0.###}: the amount to charge. "
                    + $"It was {item.Tariff:N2} for {item.Quantity:0.###}.")
                + (issued ? "" : " Leave it as it is to price it as usual."),
                suggested, 2) is not decimal amount)
            return null;

        if (!issued)
            return new Answer(item.LineNo, quantity,
                amount == suggested ? null : amount, null);

        var reason = frmTextPrompt.Ask(owner, "Change item",
            "Why is the issued item being changed? This is kept on the record.",
            hint: "e.g. Patient took 30, not 60");

        return string.IsNullOrWhiteSpace(reason)
            ? null
            : new Answer(item.LineNo, quantity, amount, reason);
    }

    /// <summary>
    /// What the change did to what the patient pays, in words for the
    /// operator - and where money has to change hands at the till.
    /// </summary>
    public static string Describe(ItemAmendedDto amended)
    {
        ArgumentNullException.ThrowIfNull(amended);

        var c = CultureInfo.CurrentCulture;

        return amended.ToSettle switch
        {
            > 0m => string.Format(c, "The patient now pays {0:N2}. It was already "
                    + "paid, so take the {1:N2} more at the till as a sale.",
                    amended.CoPaymentAfter, amended.ToSettle),
            < 0m => string.Format(c, "The patient now pays {0:N2}. It was already "
                    + "paid, so give {1:N2} back through Returns.",
                    amended.CoPaymentAfter, -amended.ToSettle),
            _ when amended.WasCollected => "Nothing changes for the patient at the till.",
            _ => string.Format(c, "The patient pays {0:N2} at the till.",
                    amended.CoPaymentAfter)
        };
    }
}
