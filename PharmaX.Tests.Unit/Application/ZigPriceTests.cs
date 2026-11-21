using FluentAssertions;
using PharmaX.Application.Reference;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;

namespace PharmaX.Tests.Unit.Application;

public class ZigPriceTests
{
    [Fact]
    public void A_zig_price_is_not_converted_from_the_dollar_price()
    {
        var product = Product.Create("AML10", "Amlovar 10mg", Money.Of(12.50m, "USD"));
        product.SetZigRetail(320m);

        product.ResolvePrice(null).Amount.Should().Be(Money.Of(12.50m, "USD"));
        product.ListedPrice("USD", null)!.Value.Amount.Should().Be(12.50m);
        product.ListedPrice("ZWG", null)!.Value.Should().Be(Money.Of(320m, "ZWG"));
        product.ListedPrice("ZWG", null)!.Value.Currency.Should().Be("ZWG");
    }

    [Fact]
    public void A_product_with_no_zig_price_is_not_given_one()
    {
        var product = Product.Create("AML10", "Amlovar 10mg", Money.Of(12.50m));
        product.ListedPrice("ZWG", null).Should().BeNull();
        product.ListedPrice(Currencies.Usd.Code, null).Should().NotBeNull();
    }

    [Fact]
    public void Warning_codes_expand_like_dosage_codes()
    {
        var text = DosageShorthand.Expand("DROWSY FINISH",
            DosageShorthand.Catalogue(WarningCodes.Standard));

        text.Should().Contain("drowsiness");
        text.Should().Contain("Finish the course");
    }

    [Fact]
    public void Day_history_is_held_by_the_dispensary_roles()
    {
        StandardRoleGrants.For(Role.Standard.Administrator).Should().Contain(PermissionCode.HistoryView);
        StandardRoleGrants.For(Role.Standard.Pharmacist).Should().Contain(PermissionCode.HistoryView);
        StandardRoleGrants.For(Role.Standard.PharmacistAssistant).Should().Contain(PermissionCode.HistoryView);
        StandardRoleGrants.For(Role.Standard.Cashier).Should().NotContain(PermissionCode.HistoryView);
        StandardRoleGrants.For(Role.Standard.Manager).Should().NotContain(PermissionCode.HistoryView);
    }
}
