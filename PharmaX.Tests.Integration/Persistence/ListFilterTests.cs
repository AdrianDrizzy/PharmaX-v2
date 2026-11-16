using FluentAssertions;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Patients;
using PharmaX.Application.Reference;
using PharmaX.Application.Security;
using PharmaX.Application.Suppliers;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// The maintenance lists' Sort and date range, in the query against a real
/// SQLite database, so a page, the count and an export all agree.
/// </summary>
public class ListFilterTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public ListFilterTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Products_are_sorted_and_filtered_by_date_across_the_whole_catalogue()
    {
        await SignInAsync();
        var now = DateTime.UtcNow;

        await ProductAsync("LF-OLD", "Listfilter old", 5m, now.AddDays(-40));
        await ProductAsync("LF-MID", "Listfilter mid", 50m, now.AddDays(-10));
        await ProductAsync("LF-NEW", "Listfilter new", 20m, now.AddHours(-1));

        using var scope = _fixture.NewScope();
        var products = PharmaXFixture.Resolve<IProductService>(scope);

        async Task<string[]> Codes(ListFilter filter) =>
            (await products.SearchAsync("Listfilter", filter: filter)).Value.Items
                .Select(p => p.Code).ToArray();

        (await Codes(new(ListSort.PriceHighest))).Should().Equal("LF-MID", "LF-NEW", "LF-OLD");
        (await Codes(new(ListSort.PriceLowest))).Should().Equal("LF-OLD", "LF-NEW", "LF-MID");
        (await Codes(new(ListSort.NewestAdded))).Should().Equal("LF-NEW", "LF-MID", "LF-OLD");
        (await Codes(new(ListSort.NameZToA))).Should().Equal("LF-OLD", "LF-NEW", "LF-MID");

        var lastMonth = new ListFilter(ListSort.OldestAdded,
            FromUtc: now.AddDays(-30), ToUtc: now.AddDays(1));

        (await Codes(lastMonth)).Should().Equal("LF-MID", "LF-NEW");
        (await products.SearchAsync("Listfilter", filter: lastMonth))
            .Value.TotalCount.Should().Be(2, "the count is filtered as the page is");
    }

    /// Every Sort on every list, by either date, translates on SQLite.
    [Fact]
    public async Task Every_maintenance_list_takes_every_sort_and_a_date_range()
    {
        await SignInAsync();
        using var scope = _fixture.NewScope();

        var patients = PharmaXFixture.Resolve<IPatientService>(scope);
        var suppliers = PharmaXFixture.Resolve<ISupplierService>(scope);
        var reference = PharmaXFixture.Resolve<IReferenceDataService>(scope);
        var tariffs = PharmaXFixture.Resolve<ITariffService>(scope);
        var products = PharmaXFixture.Resolve<IProductService>(scope);

        foreach (var sort in Enum.GetValues<ListSort>())
        foreach (var byModified in new[] { false, true })
        {
            var filter = new ListFilter(sort, byModified,
                DateTime.UtcNow.AddDays(-7), DateTime.UtcNow.AddDays(1));

            (await patients.SearchAsync("", filter: filter)).IsSuccess.Should().BeTrue();
            (await patients.CountAsync("", filter: filter)).IsSuccess.Should().BeTrue();
            (await suppliers.ListAsync(true, filter)).IsSuccess.Should().BeTrue();
            (await reference.ListAsync(ReferenceKind.Department, filter: filter))
                .IsSuccess.Should().BeTrue();
            (await tariffs.ListAsync(filter: filter)).IsSuccess.Should().BeTrue();
            (await products.SearchBatchesAsync("", filter: filter)).IsSuccess.Should().BeTrue();
        }
    }

    /// Stamped as added on a given day; the audit keeps a date already set.
    private async Task ProductAsync(string code, string description, decimal price,
        DateTime addedUtc)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var product = Product.Create(code, description, Money.Of(price));
        product.CreatedAtUtc = addedUtc;
        db.Products.Add(product);

        await db.SaveChangesAsync();
    }

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        (await auth.AuthenticateAsync("admin", "ChangeMe#1"))
            .IsSuccess.Should().BeTrue();
    }
}
