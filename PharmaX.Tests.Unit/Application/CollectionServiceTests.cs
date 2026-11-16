using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Sales;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Primitives;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// The dispensary hands a script to the till, and the till takes the
/// money for it without taking the stock a second time.
/// </summary>
public class CollectionServiceTests
{
    private static readonly DateTime Noon =
        new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task An_issued_script_appears_in_the_queue()
    {
        var builder = new CollectionServiceBuilder();
        builder.Waiting(tariff: 90m, scheme: 60m);

        var result = await builder.Build().WaitingAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle()
              .Which.CoPayment.Should().Be(30m);
    }

    [Fact]
    public async Task A_fully_covered_script_never_reaches_the_till()
    {
        var builder = new CollectionServiceBuilder();
        builder.Waiting(tariff: 90m, scheme: 90m);

        var result = await builder.Build().WaitingAsync();

        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Several_scripts_are_deleted_from_the_queue_at_once()
    {
        var builder = new CollectionServiceBuilder();
        var first = builder.Waiting(tariff: 90m, scheme: 60m);
        var second = builder.Waiting(tariff: 40m, scheme: 0m);
        var kept = builder.Waiting(tariff: 20m, scheme: 0m);
        var service = builder.Build();

        var result = await service.RemoveAsync(new[] { first.Id, second.Id });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
        (await service.WaitingAsync()).Value.Should().ContainSingle()
            .Which.DispensingId.Should().Be(kept.Id);
    }

    /// A till that had begun taking the money for a deleted script would
    /// otherwise charge for medicine already back on the shelf.
    [Fact]
    public async Task Deleting_a_script_voids_the_payment_a_till_had_begun()
    {
        var builder = new CollectionServiceBuilder();
        var dispensing = builder.Waiting(tariff: 90m, scheme: 60m);
        var service = builder.Build();
        await service.StartAsync(dispensing.Id);

        await service.RemoveAsync(new[] { dispensing.Id });

        (await builder.Sales.OpenCollectionForAsync(dispensing.Id)).Should().BeNull();
    }

    [Fact]
    public async Task A_script_waiting_over_24_hours_is_taken_off_by_itself()
    {
        var builder = new CollectionServiceBuilder();
        var dispensing = builder.Waiting(tariff: 90m, scheme: 60m);
        builder.Clock.Advance(TimeSpan.FromHours(24.5));

        var result = await builder.Build().WaitingAsync();

        result.Value.Should().BeEmpty();
        await builder.Reversals.Received(1).ReverseUncollectedAsync(dispensing.Id,
            "Not collected within 24 hours.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_script_waiting_under_24_hours_stays_on_the_queue()
    {
        var builder = new CollectionServiceBuilder();
        builder.Waiting(tariff: 90m, scheme: 60m);
        builder.Clock.Advance(TimeSpan.FromHours(23));

        var result = await builder.Build().WaitingAsync();

        result.Value.Should().ContainSingle();
    }

    /// <summary>
    /// The figure on the till is the figure the pharmacist quoted, to
    /// the cent. That is the whole promise of the hand-off.
    /// </summary>
    [Fact]
    public async Task The_till_opens_on_exactly_what_the_patient_owes()
    {
        var builder = new CollectionServiceBuilder();
        var dispensing = builder.Waiting(tariff: 90m, scheme: 60m);

        var result = await builder.Build().StartAsync(dispensing.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Totals.Total.Should().Be(30m);
    }

    /// <remarks>
    /// Thirty dollars over twenty-one tablets does not divide into
    /// cents. Pricing the line per tablet and multiplying back would
    /// lose a cent off the total, so the line is the item, once.
    /// </remarks>
    [Fact]
    public async Task A_copayment_that_does_not_divide_evenly_still_totals()
    {
        var builder = new CollectionServiceBuilder();
        var dispensing = builder.Waiting(tariff: 90m, scheme: 60m,
                                         quantity: 21m);

        var result = await builder.Build().StartAsync(dispensing.Id);

        result.Value.Totals.Total.Should().Be(30m);
        result.Value.Lines.Should().ContainSingle()
              .Which.Description.Should().Contain("21");
    }

    /// <summary>
    /// Rated where a society's tariff priced it, and only there: a private
    /// patient's script at the shelf price is not a rated transaction.
    /// </summary>
    [Theory]
    [InlineData(60, true)]
    [InlineData(0, false)]
    public async Task A_collected_line_is_rated_only_where_a_scheme_contributes(
        decimal scheme, bool rated)
    {
        var builder = new CollectionServiceBuilder();
        var dispensing = builder.Waiting(tariff: 90m, scheme: scheme);

        var result = await builder.Build().StartAsync(dispensing.Id);

        result.Value.Lines.Should().ContainSingle()
              .Which.IsRated.Should().Be(rated);
    }

    [Fact]
    public async Task A_script_already_paid_for_is_refused()
    {
        var builder = new CollectionServiceBuilder();
        var dispensing = builder.Waiting(tariff: 90m, scheme: 60m);
        dispensing.RecordCollection(Guid.NewGuid(), Noon);

        var result = await builder.Build().StartAsync(dispensing.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DSP-031");
        result.Error.Message.Should().Contain("Another till");
    }

    [Fact]
    public async Task A_collection_needs_an_open_till_session()
    {
        var builder = new CollectionServiceBuilder();
        var dispensing = builder.Waiting(tariff: 90m, scheme: 60m);
        builder.User.TillSessionId = null;

        var result = await builder.Build().StartAsync(dispensing.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("POS-013");
    }

    /// <remarks>
    /// The assistant at the dispensary computer does not take the money:
    /// the co-payment goes in the drawer of the till it is counted at.
    /// The script stays in the queue for the cashier.
    /// </remarks>
    [Fact]
    public async Task An_assistant_does_not_take_a_co_payment()
    {
        var builder = new CollectionServiceBuilder
        {
            Permissions = new FakePermissionRepository(granted: new[]
                { PharmaX.Domain.Security.PermissionCode.SaleCreate })
        };
        var dispensing = builder.Waiting(tariff: 90m, scheme: 60m);

        var result = await builder.Build().StartAsync(dispensing.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("POS-028");
        dispensing.AwaitsCollection.Should().BeTrue();
    }

    /// <summary>
    /// A cashier who opens a script and is called away must not lock the
    /// patient out of paying.
    /// </summary>
    /// <remarks>
    /// Only one sale may hold a dispensing - the database enforces it -
    /// so a second attempt that started a new sale was refused with a
    /// duplicate-key error, and the script could then never be collected
    /// by anybody. Found by abandoning a collection during the
    /// walkthrough, which is not a thing any test was doing.
    /// </remarks>
    [Fact]
    public async Task An_abandoned_collection_is_resumed_rather_than_restarted()
    {
        var builder = new CollectionServiceBuilder();
        var dispensing = builder.Waiting(tariff: 90m, scheme: 60m);
        var sut = builder.Build();

        var first = await sut.StartAsync(dispensing.Id);
        var second = await sut.StartAsync(dispensing.Id);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();

        // The same sale, not a second one holding the same dispensing.
        second.Value.Id.Should().Be(first.Value.Id);
        second.Value.Totals.Total.Should().Be(30m);
    }

    /// <summary>
    /// The till opened a script, the patient queried the price, and the
    /// pharmacist corrected it. Back at the till, the draft left behind
    /// must charge the corrected figure, not the one it was opened at.
    /// </summary>
    [Fact]
    public async Task A_resumed_collection_charges_what_the_script_costs_now()
    {
        var builder = new CollectionServiceBuilder();
        var dispensing = builder.Waiting(tariff: 1356m, scheme: 0m, quantity: 30m);
        var sut = builder.Build();

        (await sut.StartAsync(dispensing.Id)).Value.Totals.Total.Should().Be(1356m);

        dispensing.AmendLine(1, 30m, Money.Of(13.56m));

        var resumed = await sut.StartAsync(dispensing.Id);

        resumed.IsSuccess.Should().BeTrue(resumed.Error?.Message);
        resumed.Value.Totals.Total.Should().Be(13.56m);
        resumed.Value.Lines.Where(l => !l.IsVoided).Should().ContainSingle();
    }

    /// <summary>
    /// The medicine left the shelf when the pharmacist issued the
    /// script. Issuing it again at the till would take the pharmacy's
    /// stock figures out by every script it ever sold.
    /// </summary>
    [Fact]
    public async Task Paying_for_a_collection_does_not_move_the_stock_again()
    {
        var product = PharmaX.Domain.Inventory.Product.Create(
            "AMX0500", "Amoxicillin 500mg", Money.Of(30m));

        product.SetQuantityOnHand(100m);

        var stock = new PharmaX.Application.Inventory.StockService(
            new InMemoryProductRepository(product),
            new InMemoryBatchRepository(),
            Substitute.For<PharmaX.Application.Inventory
                .IStockMovementWriter>(),
            new FakeClock(Noon));

        var sale = OpenSale();
        sale.CollectFor(Guid.NewGuid());

        sale.AddLine(PharmaX.Domain.Sales.SaleLine.Create(
            product.Id, "Amoxicillin 500mg x 21", 1m, Money.Of(30m), 15m,
            "A", PharmaX.Domain.Sales.PriceSource.MedicalAidTariff,
            isScript: true));

        await stock.ApplyIssueAsync(sale);

        product.QuantityOnHand.Should().Be(100m);
    }

    /// <remarks>
    /// The same service, the same shape of sale, without the tie to a
    /// dispensing - so the test above is proving the collection rule
    /// rather than a stock service that never worked.
    /// </remarks>
    [Fact]
    public async Task An_ordinary_sale_still_moves_the_stock()
    {
        var product = PharmaX.Domain.Inventory.Product.Create(
            "SHAMP", "Shampoo", Money.Of(40m));

        product.SetQuantityOnHand(100m);

        var stock = new PharmaX.Application.Inventory.StockService(
            new InMemoryProductRepository(product),
            new InMemoryBatchRepository(),
            Substitute.For<PharmaX.Application.Inventory
                .IStockMovementWriter>(),
            new FakeClock(Noon));

        var sale = OpenSale();

        sale.AddLine(PharmaX.Domain.Sales.SaleLine.Create(
            product.Id, "Shampoo", 1m, Money.Of(40m), 15m, "A",
            PharmaX.Domain.Sales.PriceSource.StandardRetail));

        await stock.ApplyIssueAsync(sale);

        product.QuantityOnHand.Should().Be(99m);
    }

    private static PharmaX.Domain.Sales.Sale OpenSale() =>
        PharmaX.Domain.Sales.Sale.Open(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Noon);

    /// <summary>
    /// Finalising is what closes the queue entry, in the same
    /// transaction as the sale: a crash between the two would bill the
    /// patient and still show their script as waiting.
    /// </summary>
    [Fact]
    public async Task Paying_for_a_collection_closes_the_script()
    {
        var builder = new SaleServiceBuilder();

        var dispensing = builder.Dispensings.Add(
            CollectionServiceBuilder.IssuedDispensing(90m, 60m, 21m));

        var sale = PharmaX.Domain.Sales.Sale.Open(
            builder.Sales.CurrentBranchId,
            builder.Sales.CurrentWorkstationId,
            builder.User.TillSessionId!.Value, builder.User.Id, Noon);

        sale.CollectFor(dispensing.Id);

        sale.AddLine(PharmaX.Domain.Sales.SaleLine.Create(
            Guid.NewGuid(), "Amoxicillin 500mg x 21", 1m, Money.Of(30m),
            15m, "A", PharmaX.Domain.Sales.PriceSource.MedicalAidTariff));

        await builder.Sales.AddAsync(sale);

        await builder.Build().FinaliseAsync(sale.Id, new[]
        {
            new PharmaX.Application.Sales.Dtos.PaymentDto("Cash", 30m, null)
        });

        dispensing.SaleId.Should().Be(sale.Id);
        dispensing.AwaitsCollection.Should().BeFalse();

        (await builder.Dispensings.AwaitingCollectionAsync())
            .Should().BeEmpty();
    }

    /// <remarks>
    /// Whatever else the patient picks up at the counter goes on the same
    /// sale and is paid for in one go; the script's own line stays the
    /// pharmacist's to change.
    /// </remarks>
    [Fact]
    public async Task Counter_items_join_a_collection_but_the_script_line_stays_put()
    {
        var builder = new SaleServiceBuilder();

        var sale = PharmaX.Domain.Sales.Sale.Open(
            builder.Sales.CurrentBranchId,
            builder.Sales.CurrentWorkstationId,
            builder.User.TillSessionId!.Value, builder.User.Id, Noon);

        sale.CollectFor(Guid.NewGuid());
        sale.AddLine(PharmaX.Domain.Sales.SaleLine.Create(Guid.NewGuid(),
            "Amoxicillin 500mg x 21", 1m, PharmaX.Domain.Primitives.Money.Of(30m),
            0m, "A", PharmaX.Domain.Sales.PriceSource.StandardRetail, isScript: true));
        await builder.Sales.AddAsync(sale);

        var service = builder.Build();

        var added = await service.AddLineAsync(sale.Id, "PAR0500", 1m);
        added.IsSuccess.Should().BeTrue(added.Error.Message);

        var voided = await service.VoidLineAsync(sale.Id, 1, "Changed mind");
        voided.Error.Code.Should().Be("POS-014");
    }
}

/// <summary>
/// Assembles a CollectionService. The repositories it only reads from -
/// patients, scripts, users - are substitutes: the service asks them for
/// names to print on a queue, and building three in-memory repositories
/// to supply them would be more fake than test.
/// </summary>
internal sealed class CollectionServiceBuilder
{
    private static readonly DateTime Noon =
        new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    public InMemoryDispensingRepository Dispensings { get; } = new();
    public InMemorySaleRepository Sales { get; } = new();
    public InMemoryProductRepository Products { get; } = new();
    public FakeAuditWriter Audit { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeClock Clock { get; } = new(Noon);
    public FakeCurrentUser User { get; } = new();

    /// A cashier who also deletes from Collect (administrator grant in
    /// production; the tests need the delete path itself).
    public FakePermissionRepository Permissions { get; set; } =
        new(granted: new[]
        {
            PharmaX.Domain.Security.PermissionCode.SaleCreate,
            PharmaX.Domain.Security.PermissionCode.SaleTakePayment,
            PharmaX.Domain.Security.PermissionCode.ScriptDelete
        });

    /// <summary>
    /// An issued script sitting in the queue, and the patient, script
    /// and pharmacist behind it.
    /// </summary>
    public Dispensing Waiting(decimal tariff, decimal scheme,
                              decimal quantity = 1m)
    {
        var dispensing = IssuedDispensing(tariff, scheme, quantity);
        Dispensings.Add(dispensing);
        return dispensing;
    }

    public static Dispensing IssuedDispensing(decimal tariff, decimal scheme,
                                              decimal quantity)
    {
        var dispensing = Dispensing.Begin(Guid.NewGuid(), Guid.NewGuid(),
                                          Guid.NewGuid(), repeatNumber: 0);

        var line = dispensing.AddLine(DispensingLine.Create(
            productId: Guid.NewGuid(),
            description: "Amoxicillin 500mg",
            quantity: quantity,
            directions: "One three times a day",
            tariffAmount: Money.Of(tariff)));

        line.SetSchemeContribution(Money.Of(scheme));
        dispensing.Issue(Guid.NewGuid(), "DSP0001", Noon);

        return dispensing;
    }

    /// Reverses for real, as far as the queue can tell: the script stops
    /// waiting. Stock and claims are DispensingService's own tests.
    public PharmaX.Application.Dispensing.IDispensingService Reversals { get; }

    public CollectionServiceBuilder()
    {
        Reversals = Substitute.For<PharmaX.Application.Dispensing.IDispensingService>();
        Reversals.ReverseUncollectedAsync(Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var dispensing = await Dispensings.GetAsync(call.Arg<Guid>());
                dispensing!.Reverse(call.ArgAt<string>(1));
                return PharmaX.Application.Results.Result.Ok();
            });
    }

    public CollectionService Build() =>
        new(Dispensings, Sales, Products,
            Substitute.For<PharmaX.Application.Repositories
                .IPatientRepository>(),
            Substitute.For<PharmaX.Application.Dispensing
                .IScriptRepository>(),
            Substitute.For<PharmaX.Application.Repositories
                .IUserRepository>(),
            Reversals,
            new PharmaX.Application.Security.PermissionService(
                User, Permissions, Audit),
            UnitOfWork, User, Clock);
}
