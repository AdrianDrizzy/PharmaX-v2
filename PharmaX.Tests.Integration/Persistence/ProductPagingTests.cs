using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Inventory;
using PharmaX.Application.Security;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// The product list a page at a time: every match on exactly one page,
/// whatever the names have in common.
/// </summary>
public class ProductPagingTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public ProductPagingTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task The_pages_between_them_hold_every_match_once()
    {
        await GivenProductsAsync();
        await SignInAsync();

        using var scope = _fixture.NewScope();
        var products = PharmaXFixture.Resolve<IProductService>(scope);

        var codes = new List<string>();

        for (var page = 0; page < 3; page++)
        {
            var result = await products.SearchAsync("PGT-", take: 2,
                                                    skip: page * 2);

            result.IsSuccess.Should().BeTrue(result.Error.Message);
            result.Value.TotalCount.Should().Be(5);
            codes.AddRange(result.Value.Items.Select(p => p.Code));
        }

        // Three of the five share a name, which is where an order by name
        // alone let a product turn up on two pages and another on none.
        codes.Should().OnlyHaveUniqueItems()
            .And.BeEquivalentTo("PGT-1", "PGT-2", "PGT-3", "PGT-4", "PGT-5");
    }

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    private async Task GivenProductsAsync()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        if (await db.Products.AnyAsync(p => p.Code == "PGT-1")) return;

        db.Products.AddRange(
            Product.Create("PGT-1", "PGT same name", Money.Of(10m), schedule: 0),
            Product.Create("PGT-2", "PGT same name", Money.Of(10m), schedule: 0),
            Product.Create("PGT-3", "PGT same name", Money.Of(10m), schedule: 0),
            Product.Create("PGT-4", "PGT other", Money.Of(10m), schedule: 0),
            Product.Create("PGT-5", "PGT another", Money.Of(10m), schedule: 0));

        await db.SaveChangesAsync();
    }
}
