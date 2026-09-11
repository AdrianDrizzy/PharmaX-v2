using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Application.Tills;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// A refund from the receipt number to the drawer, against a real
/// database. The two things that matter here cannot be unit tested: what
/// happens to the batch the goods came from, and what the till expects
/// at cash-up afterwards.
/// </summary>
public class ReturnFlowTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public ReturnFlowTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_returned_item_goes_back_on_the_shelf()
    {
        await SignInAsync();
        await OpenTillSessionAsync();

        var code = await GivenAProductAsync("RET-PLAIN", "Returnable item",
                                            onHand: 20m);

        var sale = await GivenASaleAsync(code, quantity: 3m, unitPrice: 15m);

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var product = await db.Products.AsNoTracking()
                .SingleAsync(p => p.Code == code);

            product.QuantityOnHand.Should().Be(17m, "three were sold");
        }

        using (var scope = _fixture.NewScope())
        {
            var returns = PharmaXFixture.Resolve<IReturnService>(scope);

            var found = await returns.FindSaleAsync(sale.DocumentNumber);
            found.IsSuccess.Should().BeTrue(found.Error.Message);

            var line = found.Value.Lines.Should().ContainSingle().Subject;
            line.Returnable.Should().Be(3m);
            line.UnitPrice.Should().Be(15m);

            var result = await returns.CreateAsync(new ReturnEditDto(
                SaleId: found.Value.SaleId,
                Kind: nameof(ReturnKind.GoodsReturn),
                Reason: "Customer changed their mind.",
                AuthorisedBy: await SupervisorIdAsync(scope),
                Lines: new[]
                {
                    new ReturnLineEditDto(line.LineNo, 2m, true, null)
                },
                Refunds: new[] { new PaymentDto("Cash", 30m, null) }));

            result.IsSuccess.Should().BeTrue(result.Error.Message);
            result.Value.DocumentNumber.Should().MatchRegex(@"^R-001-\d{6}$");
            result.Value.Total.Should().Be(30m);
            result.Value.RestockedLines.Should().Be(1);
            result.Value.WriteOffValue.Should().Be(0m);
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var product = await db.Products.AsNoTracking()
                .SingleAsync(p => p.Code == code);

            product.QuantityOnHand.Should().Be(19m, "two came back");

            var movement = await db.StockMovements.AsNoTracking()
                .SingleAsync(m => m.ProductId == product.Id
                                  && m.SourceDocumentType == "SaleReturn");

            movement.MovementType.Should().Be(MovementType.Return);
            movement.Quantity.Should().Be(2m);
        }
    }

    /// <summary>
    /// Returned stock goes back to the batch it came from, not the one
    /// FEFO would pick now: expiry follows the physical box.
    /// </summary>
    [Fact]
    public async Task Stock_returns_to_the_batch_it_came_from()
    {
        await SignInAsync();
        await OpenTillSessionAsync();

        Guid soonerId;
        Guid laterId;

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            var clock = PharmaXFixture.Resolve<IClock>(scope);

            var product = Product.Create("RET-BATCH", "Batched returnable",
                Money.Of(40m), isBatchTracked: true);

            product.SetQuantityOnHand(15m);
            db.Products.Add(product);

            var sooner = Batch.Receive(product.Id, "R-SOON",
                clock.Today.AddMonths(2), 5m, Money.Of(20m), clock.UtcNow);

            var later = Batch.Receive(product.Id, "R-LATE",
                clock.Today.AddMonths(20), 10m, Money.Of(20m), clock.UtcNow);

            db.Batches.AddRange(sooner, later);
            soonerId = sooner.Id;
            laterId = later.Id;

            await db.SaveChangesAsync();
        }

        // FEFO sells from the near-expiry batch.
        var sale = await GivenASaleAsync("RET-BATCH", 3m, 40m);

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            (await db.Batches.AsNoTracking().SingleAsync(b => b.Id == soonerId))
                .QuantityOnHand.Should().Be(2m);
        }

        using (var scope = _fixture.NewScope())
        {
            var returns = PharmaXFixture.Resolve<IReturnService>(scope);
            var found = await returns.FindSaleAsync(sale.DocumentNumber);

            var result = await returns.CreateAsync(new ReturnEditDto(
                found.Value.SaleId, nameof(ReturnKind.GoodsReturn),
                "Unopened, wrong item.", await SupervisorIdAsync(scope),
                new[]
                {
                    new ReturnLineEditDto(found.Value.Lines[0].LineNo, 3m,
                                          true, null)
                },
                new[] { new PaymentDto("Cash", 120m, null) }));

            result.IsSuccess.Should().BeTrue(result.Error.Message);
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            // Back to the near-expiry batch it left, not the long-dated
            // one: the box on the counter has the near date on it.
            (await db.Batches.AsNoTracking().SingleAsync(b => b.Id == soonerId))
                .QuantityOnHand.Should().Be(5m);

            (await db.Batches.AsNoTracking().SingleAsync(b => b.Id == laterId))
                .QuantityOnHand.Should().Be(10m);
        }
    }

    /// <summary>
    /// A line credited but not restocked is stock that has gone: the
    /// movement records a disposal and the shelf figure does not move.
    /// </summary>
    [Fact]
    public async Task An_unfit_return_is_credited_and_written_off()
    {
        await SignInAsync();
        await OpenTillSessionAsync();

        var code = await GivenAProductAsync("RET-UNFIT", "Damaged goods",
                                            onHand: 10m);

        var sale = await GivenASaleAsync(code, 1m, 25m);

        using (var scope = _fixture.NewScope())
        {
            var returns = PharmaXFixture.Resolve<IReturnService>(scope);
            var found = await returns.FindSaleAsync(sale.DocumentNumber);

            var result = await returns.CreateAsync(new ReturnEditDto(
                found.Value.SaleId, nameof(ReturnKind.GoodsReturn),
                "Packaging damaged in transit.",
                await SupervisorIdAsync(scope),
                new[]
                {
                    new ReturnLineEditDto(found.Value.Lines[0].LineNo, 1m,
                        RestockToShelf: false, "Seal broken.")
                },
                new[] { new PaymentDto("Cash", 25m, null) }));

            result.IsSuccess.Should().BeTrue(result.Error.Message);
            result.Value.RestockedLines.Should().Be(0);
            result.Value.WriteOffValue.Should().Be(25m);
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var product = await db.Products.AsNoTracking()
                .SingleAsync(p => p.Code == code);

            product.QuantityOnHand.Should().Be(9m,
                "the one sold is gone and did not come back to the shelf");

            var movement = await db.StockMovements.AsNoTracking()
                .SingleAsync(m => m.ProductId == product.Id
                                  && m.SourceDocumentType == "SaleReturn");

            movement.MovementType.Should().Be(MovementType.Disposal);
            movement.ReasonCode.Should().Be("RETURN-UNFIT");
        }
    }

    /// <summary>
    /// The reason the till expectation subtracts refunds. A till that
    /// counts a refund as takings never balances, and the operator gets
    /// blamed for the difference.
    /// </summary>
    [Fact]
    public async Task A_refund_reduces_what_the_till_expects()
    {
        await SignInAsync();
        await OpenTillSessionAsync(float_: 200m);

        var code = await GivenAProductAsync("RET-TILL", "Till item",
                                            onHand: 50m);

        var sale = await GivenASaleAsync(code, 4m, 25m);   // 100 taken

        decimal expectedAfterSale;

        using (var scope = _fixture.NewScope())
        {
            var cashUp = PharmaXFixture.Resolve<ICashUpService>(scope);
            var read = await cashUp.XReadAsync();

            var cash = read.Expectations
                .Single(e => e.TenderType == nameof(TenderType.Cash));

            // Measured rather than asserted absolutely: this class
            // shares one till session, so what is already in the drawer
            // depends on which sibling test ran first. The claim under
            // test is the change, not the starting figure.
            expectedAfterSale = cash.Expected;
            expectedAfterSale.Should().BeGreaterThan(0m);
        }

        using (var scope = _fixture.NewScope())
        {
            var returns = PharmaXFixture.Resolve<IReturnService>(scope);
            var found = await returns.FindSaleAsync(sale.DocumentNumber);

            var result = await returns.CreateAsync(new ReturnEditDto(
                found.Value.SaleId, nameof(ReturnKind.GoodsReturn),
                "Two too many.", await SupervisorIdAsync(scope),
                new[]
                {
                    new ReturnLineEditDto(found.Value.Lines[0].LineNo, 2m,
                                          true, null)
                },
                new[] { new PaymentDto("Cash", 50m, null) }));

            result.IsSuccess.Should().BeTrue(result.Error.Message);
        }

        using (var scope = _fixture.NewScope())
        {
            var cashUp = PharmaXFixture.Resolve<ICashUpService>(scope);
            var read = await cashUp.XReadAsync();

            var cash = read.Expectations
                .Single(e => e.TenderType == nameof(TenderType.Cash));

            // Fifty left the drawer, so the till expects fifty less.
            cash.Expected.Should().Be(expectedAfterSale - 50m);
        }
    }

    /// <summary>
    /// Previous returns are separate documents, so the returnable
    /// quantity has to net them off or a line gets refunded twice.
    /// </summary>
    [Fact]
    public async Task A_second_return_only_offers_what_is_left()
    {
        await SignInAsync();
        await OpenTillSessionAsync();

        var code = await GivenAProductAsync("RET-TWICE", "Twice returned",
                                            onHand: 20m);

        var sale = await GivenASaleAsync(code, 5m, 10m);

        using (var scope = _fixture.NewScope())
        {
            var returns = PharmaXFixture.Resolve<IReturnService>(scope);
            var found = await returns.FindSaleAsync(sale.DocumentNumber);

            await returns.CreateAsync(new ReturnEditDto(
                found.Value.SaleId, nameof(ReturnKind.GoodsReturn),
                "First two back.", await SupervisorIdAsync(scope),
                new[]
                {
                    new ReturnLineEditDto(found.Value.Lines[0].LineNo, 2m,
                                          true, null)
                },
                new[] { new PaymentDto("Cash", 20m, null) }));
        }

        using (var scope = _fixture.NewScope())
        {
            var returns = PharmaXFixture.Resolve<IReturnService>(scope);
            var found = await returns.FindSaleAsync(sale.DocumentNumber);

            var line = found.Value.Lines[0];
            line.AlreadyReturned.Should().Be(2m);
            line.Returnable.Should().Be(3m);

            // Asking for four when three are left is refused outright.
            var act = () => returns.CreateAsync(new ReturnEditDto(
                found.Value.SaleId, nameof(ReturnKind.GoodsReturn),
                "Four more.", Guid.NewGuid(),
                new[] { new ReturnLineEditDto(line.LineNo, 4m, true, null) },
                new[] { new PaymentDto("Cash", 40m, null) }));

            await act.Should()
                .ThrowAsync<Domain.Exceptions.DomainException>()
                .WithMessage("*only 3 can come back*");
        }
    }

    [Fact]
    public async Task An_unknown_receipt_number_says_so()
    {
        await SignInAsync();

        using var scope = _fixture.NewScope();
        var returns = PharmaXFixture.Resolve<IReturnService>(scope);

        var result = await returns.FindSaleAsync("S-999-999999");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RET-002");
    }

    [Fact]
    public async Task A_return_is_audited_with_its_authoriser_and_reason()
    {
        await SignInAsync();
        await OpenTillSessionAsync();

        var code = await GivenAProductAsync("RET-AUDIT", "Audited return",
                                            onHand: 10m);

        var sale = await GivenASaleAsync(code, 1m, 30m);

        Guid returnId;

        using (var scope = _fixture.NewScope())
        {
            var returns = PharmaXFixture.Resolve<IReturnService>(scope);
            var found = await returns.FindSaleAsync(sale.DocumentNumber);

            var result = await returns.CreateAsync(new ReturnEditDto(
                found.Value.SaleId, nameof(ReturnKind.CreditNote),
                "Priced incorrectly at the till.",
                await SupervisorIdAsync(scope),
                new[]
                {
                    new ReturnLineEditDto(found.Value.Lines[0].LineNo, 1m,
                                          false, null)
                },
                new[] { new PaymentDto("Cash", 30m, null) }));

            result.IsSuccess.Should().BeTrue(result.Error.Message);
            returnId = result.Value.Id;
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var entry = await db.AuditEntries.AsNoTracking()
                .SingleAsync(a => a.EntityId == returnId
                                  && a.Action == "pos.return.authorise");

            entry.Reason.Should().Contain("Priced incorrectly");
            entry.Reason.Should().Contain("authorised by");

            (await db.OutboxEntries.AsNoTracking()
                .AnyAsync(o => o.EntityId == returnId
                               && o.EntityType == "SaleReturn"))
                .Should().BeTrue();
        }
    }

    [Fact]
    public async Task A_refund_payment_belongs_to_the_return_not_a_sale()
    {
        await SignInAsync();
        await OpenTillSessionAsync();

        var code = await GivenAProductAsync("RET-OWNER", "Owner check",
                                            onHand: 10m);

        var sale = await GivenASaleAsync(code, 1m, 12m);

        using (var scope = _fixture.NewScope())
        {
            var returns = PharmaXFixture.Resolve<IReturnService>(scope);
            var found = await returns.FindSaleAsync(sale.DocumentNumber);

            await returns.CreateAsync(new ReturnEditDto(
                found.Value.SaleId, nameof(ReturnKind.GoodsReturn),
                "Owner check.", await SupervisorIdAsync(scope),
                new[]
                {
                    new ReturnLineEditDto(found.Value.Lines[0].LineNo, 1m,
                                          true, null)
                },
                new[] { new PaymentDto("Cash", 12m, null) }));
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            // The tender table is shared, so exactly one of the two
            // owners is set on any row.
            var refund = await db.SalePayments.AsNoTracking()
                .Where(p => p.Amount.Amount == 12m)
                .Where(p => p.SaleId == null)
                .ToListAsync();

            refund.Should().ContainSingle();
            refund[0].IsRefund.Should().BeTrue();
        }
    }

    // ---- Steps -----------------------------------------------------------

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    /// <summary>
    /// The seeded administrator holds pos.return.authorise, so it stands
    /// in for the supervisor. What the rule cares about is the
    /// permission, not the job title.
    /// </summary>
    private static async Task<Guid> SupervisorIdAsync(IServiceScope scope)
    {
        var users = PharmaXFixture.Resolve<IUserRepository>(scope);
        var admin = await users.ByUsernameAsync("admin");

        return admin!.Id;
    }

    private async Task<string> GivenAProductAsync(string code,
        string description, decimal onHand)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        if (await db.Products.AnyAsync(p => p.Code == code)) return code;

        var product = Product.Create(code, description, Money.Of(25m));
        product.SetQuantityOnHand(onHand);

        db.Products.Add(product);
        await db.SaveChangesAsync();

        return code;
    }

    private async Task<FinaliseDto> GivenASaleAsync(string code,
        decimal quantity, decimal unitPrice)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        // Priced explicitly so the credit figures in the tests are
        // obvious rather than derived from a seeded price.
        var product = await db.Products.SingleAsync(p => p.Code == code);
        product.ChangeRetailPrice(Money.Of(unitPrice));
        await db.SaveChangesAsync();

        var sales = PharmaXFixture.Resolve<ISaleService>(scope);

        var opened = await sales.OpenAsync();
        opened.IsSuccess.Should().BeTrue(opened.Error.Message);

        var line = await sales.AddLineAsync(opened.Value.Id, code, quantity);
        line.IsSuccess.Should().BeTrue(line.Error.Message);

        var finalised = await sales.FinaliseAsync(opened.Value.Id,
            new[] { new PaymentDto("Cash", quantity * unitPrice, null) });

        finalised.IsSuccess.Should().BeTrue(finalised.Error.Message);
        return finalised.Value;
    }

    private async Task OpenTillSessionAsync(decimal float_ = 100m)
    {
        using var scope = _fixture.NewScope();
        var sessions = PharmaXFixture.Resolve<ITillSessionService>(scope);

        if (await sessions.CurrentAsync() is not null) return;

        var result = await sessions.OpenAsync(float_);
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }
}
