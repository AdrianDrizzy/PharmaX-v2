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

    /// <remarks>
    /// "New today" is what was added today, not what was brought across
    /// today from records years old.
    /// </remarks>
    [Fact]
    public async Task Patients_added_today_are_counted_apart_from_those_on_file()
    {
        var before = await ReadAsync(ProductScope.All);

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var imported = Domain.Patients.Patient.Create(Code("PAT"), "Longstanding", "On file");
            imported.CreatedAtUtc = DateTime.UtcNow.AddYears(-3);

            db.Patients.AddRange(imported,
                Domain.Patients.Patient.Create(Code("PAT"), "Walkin", "New today"));

            await db.SaveChangesAsync();
        }

        var after = await ReadAsync(ProductScope.All);

        after.Patients.Should().Be(before.Patients + 2);
        after.PatientsAddedToday.Should().Be(before.PatientsAddedToday + 1,
            "the one on file for three years was not added today");
    }

    /// <remarks>
    /// The order raised last, not the last one entered: an order keyed in
    /// late for last week does not make this week's look done. A cancelled
    /// order was not raised at all.
    /// </remarks>
    [Fact]
    public async Task The_last_order_raised_is_dated_and_a_cancelled_one_is_not()
    {
        var latest = new DateOnly(2099, 3, 14);

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var supplier = Domain.Suppliers.Supplier.Create(Code("SUP"), "Home count supplier");
            db.Add(supplier);

            var cancelled = Domain.Suppliers.PurchaseOrder.Raise(Code("PO"), supplier.Id,
                latest.AddDays(30));
            cancelled.Cancel("Raised in error");

            db.AddRange(
                Domain.Suppliers.PurchaseOrder.Raise(Code("PO"), supplier.Id, latest.AddDays(-7)),
                Domain.Suppliers.PurchaseOrder.Raise(Code("PO"), supplier.Id, latest),
                cancelled);

            await db.SaveChangesAsync();
        }

        (await ReadAsync(ProductScope.All)).LastPurchaseOrderOn.Should().Be(latest);
    }

    /// <remarks>
    /// Nothing recorded when a report was asked for, so the home screen
    /// could not say. Each run is in the audit trail now.
    /// </remarks>
    [Fact]
    public async Task A_report_run_is_recorded_and_dated_on_the_home_screen()
    {
        using (var scope = _fixture.NewScope())
        {
            var signedIn = await PharmaXFixture.Resolve<Application.Security.IAuthenticationService>(scope)
                .AuthenticateAsync("admin", "ChangeMe#1");
            signedIn.IsSuccess.Should().BeTrue();
        }

        var before = DateTime.UtcNow.AddSeconds(-1);

        using (var scope = _fixture.NewScope())
        {
            var clock = PharmaXFixture.Resolve<Application.Abstractions.IClock>(scope);

            await PharmaXFixture.Resolve<Application.Reporting.IReportRunner>(scope)
                .RunAsync(new Application.Reporting.ReportRequest("pos.transactions.all", "1",
                    clock.Today, clock.Today, new Dictionary<string, string?>()));
        }

        (await ReadAsync(ProductScope.All)).LastReportRunUtc
            .Should().NotBeNull().And.BeOnOrAfter(before);
    }

    private static string Code(string kind) =>
        $"HC-{kind}-{Guid.NewGuid():N}"[..20];

    private async Task<HomeCountsDto> ReadAsync(ProductScope products)
    {
        using var scope = _fixture.NewScope();
        return await PharmaXFixture.Resolve<IHomeCounts>(scope).ReadAsync(products);
    }
}
