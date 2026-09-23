using FluentAssertions;
using PharmaX.Desktop.Features.Sales;
using PharmaX.Domain.Sales;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// Taking the tender on a till with no payment gateway.
/// </summary>
/// <remarks>
/// PharmaX talks to no card machine and no medical aid platform. Those
/// sit beside the till and the cashier works them directly, so by the
/// time a tender is recorded the money has already moved. The till's job
/// is to record what was taken and on which tender; whether a slip or
/// approval number is copied back is the cashier's discretion.
/// </remarks>
public class PaymentDialogTests
{
    [Fact]
    public void The_tenders_read_as_words_rather_than_as_enum_names()
    {
        using var form = new frmPayment(100m);

        var offered = Tenders(form).ToList();

        offered.Should().Contain("EFT").And.Contain("Medical aid");

        offered.Should().NotContain("Eft",
            "the busiest screen in the pharmacy should not show the "
            + "spelling of a C# enum");

        offered.Should().NotContain("MedicalAid");

        // Every tender the domain knows is still offered, under some
        // name: a tender that cannot be selected cannot be taken.
        offered.Should().HaveCount(Enum.GetValues<TenderType>().Length);
    }

    /// <remarks>
    /// Cash first, then the two taken on the machine beside the till.
    /// </remarks>
    [Fact]
    public void Cash_is_offered_first_and_the_card_tenders_next()
    {
        using var form = new frmPayment(100m);

        Tenders(form).Take(3).Should().ContainInOrder("Cash", "Card", "EFT");
    }

    /// <remarks>
    /// The point of this change. Card, EFT and medical aid are approved
    /// on their own machine before the cashier reaches this dialog, so
    /// demanding a code back only ever blocked a payment that had
    /// already gone through.
    /// </remarks>
    [Theory]
    [InlineData(TenderType.Card)]
    [InlineData(TenderType.Eft)]
    [InlineData(TenderType.MedicalAid)]
    public void A_tender_settled_elsewhere_goes_through_without_a_reference(
        TenderType tender)
    {
        frmPayment.Refuse(tender, 100m, "").Should().BeNull();
        frmPayment.Refuse(tender, 100m, null).Should().BeNull();
    }

    [Theory]
    [InlineData(TenderType.Cash)]
    [InlineData(TenderType.Account)]
    [InlineData(TenderType.Voucher)]
    public void So_does_every_other_tender(TenderType tender)
    {
        frmPayment.Refuse(tender, 100m, "").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void The_amount_is_the_only_thing_the_till_insists_on(int? amount)
    {
        frmPayment.Refuse(TenderType.Card, amount, "AUTH 1")
            .Should().Contain("greater than zero");
    }

    /// <remarks>
    /// The screen and the domain have to agree on what is allowed. A
    /// dialog that let a tender through which the sale then refused
    /// would fail at finalise, after the customer had paid.
    /// </remarks>
    [Theory]
    [InlineData(TenderType.Card)]
    [InlineData(TenderType.Eft)]
    [InlineData(TenderType.MedicalAid)]
    public void What_the_dialog_allows_the_sale_accepts(TenderType tender)
    {
        frmPayment.Refuse(tender, 100m, null).Should().BeNull();

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
