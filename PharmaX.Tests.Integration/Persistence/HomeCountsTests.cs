using FluentAssertions;
using PharmaX.Application.Home;
using PharmaX.Application.Repositories;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>The home screen's figures: what is on file and in use.</summary>
public class HomeCountsTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public HomeCountsTests(PharmaXFixture fixture) => _fixture = fixture;

    /// <remarks>
    /// A cashier's figure is the length of a cashier's product list: over
    /// the counter only, as the list is.
    /// </remarks>
    [Fact]
    public async Task Products_are_counted_as_the_user_sees_the_list()
    {
        var all = await ReadAsync(ProductScope.All);
        var shop = await ReadAsync(ProductScope.OverTheCounter);

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var gone = Product.Create(Code("OLD"), "Home count discontinued",
                Money.Of(5m));
            gone.Deactivate();

            db.Products.AddRange(
                Product.Create(Code("OTC"), "Home count shop item", Money.Of(5m)),
                Product.Create(Code("RX"), "Home count medicine", Money.Of(5m),
                    schedule: 3),
                gone);

            await db.SaveChangesAsync();
        }

        (await ReadAsync(ProductScope.All)).Products
            .Should().Be(all.Products + 2, "the discontinued one is not counted");

        (await ReadAsync(ProductScope.OverTheCounter)).Products
            .Should().Be(shop.Products + 1, "the scheduled medicine is not the shop's");
    }

    private static string Code(string kind) =>
        $"HC-{kind}-{Guid.NewGuid():N}"[..20];

    private async Task<HomeCountsDto> ReadAsync(ProductScope products)
    {
        using var scope = _fixture.NewScope();
        return await PharmaXFixture.Resolve<IHomeCounts>(scope).ReadAsync(products);
    }
}
