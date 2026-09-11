using PharmaX.Domain.Primitives;

namespace PharmaX.Domain.Pricing;

public sealed record VatSplit(Money NetAmount, Money VatAmount, Money GrossAmount);

public static class VatCalculator
{
    /// Inclusive extraction: gross 115.00 at 15% gives vat 15.00, net 100.00.
    public static VatSplit FromInclusive(Money gross, decimal ratePercent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ratePercent, 0m);

        if (ratePercent == 0m)
            return new VatSplit(gross, Money.Zero(gross.Currency), gross);

        var factor = ratePercent / (100m + ratePercent);
        var vat = gross * factor;
        var net = gross - vat;
        return new VatSplit(net, vat, gross);
    }

    /// Exclusive addition, for supplier invoices captured net of VAT.
    public static VatSplit FromExclusive(Money net, decimal ratePercent)
    {
        var vat = net.Percentage(ratePercent);
        return new VatSplit(net, vat, net + vat);
    }
}
