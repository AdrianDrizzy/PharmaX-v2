using FluentAssertions;
using PharmaX.Desktop.Features.Sales;
using PharmaX.Domain.Sales;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// Taking the tender on a till with no payment gateway.
/// </summary>
/// <remarks>
/// PharmaX talks to no card machine, EcoCash platform or medical aid
/// switch. Those sit beside the till and the cashier works them
/// directly, so by the time a tender is recorded the money has already
/// moved. The till's job is to record what was taken and on which
/// tender; whether a slip, approval number or EcoCash transaction ID is
/// copied back is the cashier's discretion.
/// </remarks>
public class PaymentDialogTests
{
    [Fact]
    public void The_tenders_read_as_words_rather_than_as_enum_names()
    {
        using var form = new frmPayment(100m);

        var offered = Tenders(form).ToList();

        offered.Should().Contain("EFT").And.Contain("EcoCash");

        offered.Should().NotContain("Eft",
            "the busiest screen in the pharmacy should not show the "
            + "spelling of a C# enum");
    }

    /// <remarks>
    /// A scheme pays through the claim raised when a script is issued.
    /// Offered at the till, "Medical aid" finalised walk-in sales that no
    /// scheme was ever billed for (ADM-DEF-10).
    /// </remarks>
    [Fact]
    public void Medical_aid_is_not_a_till_tender()
    {
        using var form = new frmPayment(100m);

        Tenders(form).Should().NotContain("Medical aid")
            .And.NotContain("MedicalAid");
    }

    /// <remarks>
    /// Cash first, EcoCash beside it because it is used as freely, then
    /// the two taken on the machine beside the till.
    /// </remarks>
    [Fact]
    public void Cash_and_EcoCash_are_offered_first_and_the_card_tenders_next()
    {
        using var form = new frmPayment(100m);

        Tenders(form).Take(4).Should()
            .ContainInOrder("Cash", "EcoCash", "Card", "EFT");
    }

    /// <remarks>
    /// Card, EFT and EcoCash are settled on their own machine or phone
    /// before the cashier reaches this dialog, so demanding a code back
    /// only ever blocked a payment that had already gone through.
    /// </remarks>
    [Theory]
    [InlineData(TenderType.Card)]
    [InlineData(TenderType.Eft)]
    [InlineData(TenderType.EcoCash)]
    public void A_tender_settled_elsewhere_goes_through_without_a_reference(
        TenderType tender)
    {
        frmPayment.Refuse(tender, 100m, "", outstanding: 100m).Should().BeNull();
        frmPayment.Refuse(tender, 100m, null, outstanding: 100m).Should().BeNull();
    }

    [Theory]
    [InlineData(TenderType.Cash)]
    [InlineData(TenderType.Account)]
    [InlineData(TenderType.Voucher)]
    public void So_does_every_other_tender(TenderType tender)
    {
        frmPayment.Refuse(tender, 100m, "", outstanding: 100m).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void The_amount_must_be_more_than_zero(int? amount)
    {
        frmPayment.Refuse(TenderType.Card, amount, "AUTH 1", outstanding: 100m)
            .Should().Contain("greater than zero");
    }

    /// <remarks>
    /// Only cash gives change. A card for more than was due used to go
    /// into the list as "Change $46.19" with Pay silently disabled
    /// (ADM-OBS-09); EcoCash is sent for the exact amount, so it is held
    /// to the same rule.
    /// </remarks>
    [Theory]
    [InlineData(TenderType.Card)]
    [InlineData(TenderType.Eft)]
    [InlineData(TenderType.EcoCash)]
    [InlineData(TenderType.Account)]
    [InlineData(TenderType.Voucher)]
    public void Only_cash_may_be_more_than_is_due(TenderType tender)
    {
        frmPayment.Refuse(tender, 150m, null, outstanding: 103.81m)
            .Should().Contain("cannot be more than").And.Contain("only cash");

        frmPayment.Refuse(TenderType.Cash, 150m, null, outstanding: 103.81m)
            .Should().BeNull("the difference comes back as change");
    }

    [Fact]
    public void A_refund_is_held_to_what_is_owed_on_every_tender()
    {
        frmPayment.Refuse(TenderType.Cash, 40m, null, outstanding: 33.28m,
                          refund: true)
            .Should().Contain("Only").And.Contain("owed back");

        frmPayment.Refuse(TenderType.EcoCash, 33.28m, null,
                          outstanding: 33.28m, refund: true)
            .Should().BeNull();
    }

    [Fact]
    public void Nothing_more_is_taken_once_the_sale_is_covered()
    {
        frmPayment.Refuse(TenderType.Cash, 5m, null, outstanding: 0m)
            .Should().Contain("already covered");
    }

    /// <remarks>
    /// The screen and the domain have to agree on what is allowed. A
    /// dialog that let a tender through which the sale then refused
    /// would fail at finalise, after the customer had paid.
    /// </remarks>
    [Theory]
    [InlineData(TenderType.Card)]
    [InlineData(TenderType.Eft)]
    [InlineData(TenderType.EcoCash)]
    public void What_the_dialog_allows_the_sale_accepts(TenderType tender)
    {
        frmPayment.Refuse(tender, 100m, null, outstanding: 100m).Should().BeNull();

        var act = () => PharmaX.Domain.Sales.SalePayment.Create(
            tender, PharmaX.Domain.Primitives.Money.Of(100m),
            reference: null);

        act.Should().NotThrow();
    }

    private static IEnumerable<string> Tenders(Form form) =>
        form.Controls.Find("cboTender", searchAllChildren: true)
            .Cast<ComboBox>()
            .Single()
            .Items.Cast<object>()
            .Select(i => i.ToString() ?? "");
}
