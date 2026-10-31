using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;

namespace PharmaX.Application.Dispensing;

/// <summary>
/// What a cash patient is charged for the units handed over: the pack's
/// price pro rata, rounded up to the pharmacy's step.
/// </summary>
/// <remarks>
/// The previous system charged this way. Its pack prices came across as
/// they were (Calcigard 20mg: 6.66 for a pack of 100), and the counter
/// charged the share for the tablets handed over rounded up to the
/// dollar: 14 tablets 1.00, 30 tablets 2.00, 60 tablets 4.00. Most of its
/// part-pack charges for October 2026 fit that rule. The step is set in
/// System setup and is a dollar where nothing has been set.
/// A medical aid's tariff is never rounded: the scheme pays its own
/// figure, and the claim has to match it.
/// </remarks>
public static class DispensingCharge
{
    /// The step where System setup has not set one: the nearest dollar above.
    public const decimal DefaultRoundTo = 1m;

    /// <summary>
    /// The charge for <paramref name="quantity"/> units of a pack of
    /// <paramref name="unitsInPack"/> priced <paramref name="packPrice"/>.
    /// </summary>
    /// <remarks>
    /// Only a count out of a bigger pack - tablets, capsules, millilitres -
    /// is rounded: an item handed over whole (an injection, a bottle) is
    /// charged its price, as the previous system charged it. A step of a
    /// cent or less is no rounding at all, as PharmaX charged before.
    /// </remarks>
    public static decimal For(decimal packPrice, decimal unitsInPack, decimal quantity, decimal roundTo) =>
        unitsInPack <= 1m || roundTo <= 0.01m
            ? Math.Round(Exact(packPrice, unitsInPack, quantity), 2, MidpointRounding.AwayFromZero)
            : Pricing.RoundUp(Exact(packPrice, unitsInPack, quantity), roundTo);

    /// The share before rounding.
    public static decimal Exact(decimal packPrice, decimal unitsInPack, decimal quantity) =>
        packPrice * quantity / (unitsInPack > 0m ? unitsInPack : 1m);

    /// The step as set, or a dollar where it never was.
    public static async Task<decimal> RoundToAsync(IPharmacySettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var said = await settings.GetAsync(PharmacySettingKey.DispensingRounding, ct).ConfigureAwait(false);

        return decimal.TryParse(said, NumberStyles.Number, CultureInfo.InvariantCulture, out var step) && step > 0m
            ? step
            : DefaultRoundTo;
    }

    /// <summary>
    /// What the dispenser types in: "the number of tablets handed over (a
    /// pack holds 100)", or whole packs where the product has no units.
    /// </summary>
    public static string WhatToEnter(string unit, decimal unitsInPack) =>
        unitsInPack > 1m
            ? $"Enter the number of {Plural(unit)} handed over (a pack holds "
              + $"{unitsInPack.ToString("0.###", CultureInfo.InvariantCulture)})."
            : "Enter the number of packs handed over.";

    /// <summary>
    /// The sum shown as it is typed: "6.66 for 100, so 14 tablets come to
    /// 0.93, rounded up to 1.00".
    /// </summary>
    public static string Explain(decimal packPrice, decimal unitsInPack, string unit, decimal quantity, decimal roundTo)
    {
        // Up to four places, so 1.9992 does not read as 2.00 and look unrounded.
        var exact = Math.Round(Exact(packPrice, unitsInPack, quantity), 4, MidpointRounding.AwayFromZero);
        var charged = For(packPrice, unitsInPack, quantity, roundTo);
        var count = quantity.ToString("0.###", CultureInfo.InvariantCulture);
        var what = unitsInPack > 1m ? $"{count} {(quantity == 1m ? unit : Plural(unit))}" : $"{count} pack(s)";
        var pack = unitsInPack > 1m
            ? $"{Show(packPrice)} for {unitsInPack.ToString("0.###", CultureInfo.InvariantCulture)}"
            : $"{Show(packPrice)} a pack";

        var come = quantity == 1m ? "comes" : "come";

        return charged == exact
            ? $"{pack}, so {what} {come} to {Show(charged)}."
            : $"{pack}, so {what} {come} to {exact.ToString("#,0.00##", CultureInfo.InvariantCulture)}, rounded up to {Show(charged)}.";
    }

    private static string Plural(string unit) =>
        unit is "ml" || unit.EndsWith('s') ? unit : unit + "s";

    private static string Show(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
}
